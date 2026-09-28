using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Kasa.Api.Tests.MonthlyExpenseTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Rapor yükleme dayanıklılığı (gap-veri-degismezleri-patlama-yaricapi-3). Rapora satır veren tek bir kaydın kendi verisindeki
/// sorun (bilinmeyen kanal kimliği, boş kanal, okunamayan dağılım/pay JSON'u, eksik kaynak, türetilemeyen plan) raporları 500'e
/// düşürmez. Kayıt karantinaya alınır: gider "Dağılım bekliyor" olur (tutar kasadan düşer, hiçbir kanala yazılmaz), gelir genel
/// kasaya girer, tutarı türetilemeyen kayıt rapora alınmaz. Rapor uçları 200 döner, haftalık/aylık rapor ve ana sayfa veri sağlığı
/// uyarısı taşır, kayıt anahtarıyla bir kez Warning loglanır. İlgisiz ayın raporu ne düşer ne uyarı taşır. Takip başlangıcı
/// 1 Haziran 2026, bugün 25 Eylül 2026 (sabit saat), kasa açılışı 1.000; kanal 1 MEZAT.
/// </summary>
public class RaporDayaniklilikTests
{
    public enum Bozulma
    {
        EkstreGiderBilinmeyenKanal, EkstreGiderKanalsizPay, EkstreGiderBozukDagilim, EkstreGelirBilinmeyenKanal,
        AylikGiderBilinmeyenKanal, AylikGiderRevizyonuYok, AlisDagilimiHesaplanamaz, TakipliKrediTaksitiBilinmeyenKanal,
        TakipliKrediCekimiBilinmeyenKanal, KartOdemesiBozukPaylar, EkGelirKanalsiz, EskiKrediPlaniGecersiz,
    }

    /// <param name="Sql">Kaydı bozan ham SQL (yabancı anahtar denetimi kapalıyken çalışır: eksik kaynak da kurulabilir).</param>
    /// <param name="Parca">Uyarıda ve logda kaydı kimliğiyle tanıtan metin (bozulmadan sonra okunur).</param>
    /// <param name="Ay">Kaydın dokunduğu ay: o ayın aylık raporu uyarı taşır.</param>
    /// <param name="Bekleyen">Panelin dağılım bekleyen tutarındaki artış.</param>
    /// <param name="MezatFarki">MEZAT kanal bakiyesindeki değişim (kasa toplamı her senaryoda aynı kalır).</param>
    /// <param name="OzetDuser">Takip özeti hesaplanamaz (ana sayfa özeti null + uyarı ile döner).</param>
    private sealed record Senaryo(string Sql, Func<KasaDbContext, string> Parca, DateOnly Ay, decimal Bekleyen, decimal MezatFarki, bool OzetDuser = false);

    private static readonly DateOnly Temmuz = new(2026, 7, 1), Agustos = new(2026, 8, 1);
    private static readonly string[] SabitUclar = ["/api/rapor/panel", "/api/rapor/haftalik", "/api/donemler", "/api/rapor/ana-sayfa", "/api/kasa-esikleri",
        "/api/rapor/aylik?yil=2025&ay=1"];

    [Theory]
    [InlineData(Bozulma.EkstreGiderBilinmeyenKanal)]
    [InlineData(Bozulma.EkstreGiderKanalsizPay)]
    [InlineData(Bozulma.EkstreGiderBozukDagilim)]
    [InlineData(Bozulma.EkstreGelirBilinmeyenKanal)]
    [InlineData(Bozulma.AylikGiderBilinmeyenKanal)]
    [InlineData(Bozulma.AylikGiderRevizyonuYok)]
    [InlineData(Bozulma.AlisDagilimiHesaplanamaz)]
    [InlineData(Bozulma.TakipliKrediTaksitiBilinmeyenKanal)]
    [InlineData(Bozulma.TakipliKrediCekimiBilinmeyenKanal)]
    [InlineData(Bozulma.KartOdemesiBozukPaylar)]
    [InlineData(Bozulma.EkGelirKanalsiz)]
    [InlineData(Bozulma.EskiKrediPlaniGecersiz)]
    public async Task Bozuk_kayit_raporlari_dusurmez_karantinaya_alinir_uyari_ve_bir_kez_log_verir(Bozulma bozulma)
    {
        var loglar = new UyariToplayici();
        await using var f = new LogluFabrika(loglar); using var c = await Editor(f);
        var senaryo = await Kur(bozulma, f, c);
        var once = await Panel(c);
        var saglamAnaSayfa = JsonNode.Parse(await c.GetStringAsync("/api/rapor/ana-sayfa"))!;
        Assert.Null(saglamAnaSayfa["veriSagligiUyarisi"]);
        Assert.NotNull(saglamAnaSayfa["takipOzeti"]);

        string parca;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Bozuk(db, senaryo.Sql);
            parca = senaryo.Parca(db);
        }

        // Bütün rapor uçları (ilgisiz ay dahil) iki tur boyunca 200 döner: bozuk kayıt her istekte yeniden görülür.
        var uclar = SabitUclar.Concat([Aylik(senaryo.Ay), Aylik(Month)]).ToList();
        for (var tur = 0; tur < 2; tur++)
            foreach (var uc in uclar)
            {
                var yanit = await c.GetAsync(uc);
                Assert.True(yanit.StatusCode == HttpStatusCode.OK, $"{bozulma} {uc}: {yanit.StatusCode} {await yanit.Content.ReadAsStringAsync()}");
            }

        // Uyarı: ana sayfa, haftalık raporun son dönemi ve kaydın ayı; ilgisiz ay uyarı taşımaz.
        var anaSayfa = JsonNode.Parse(await c.GetStringAsync("/api/rapor/ana-sayfa"))!;
        Assert.Contains(parca, (string)anaSayfa["veriSagligiUyarisi"]!);
        Assert.StartsWith("Okunamayan ", (string)anaSayfa["veriSagligiUyarisi"]!);
        Assert.Equal(senaryo.OzetDuser, anaSayfa["takipOzeti"] is null);
        if (senaryo.OzetDuser) Assert.Contains("Kart ve kredi takip özeti hesaplanamadı", (string)anaSayfa["veriSagligiUyarisi"]!);
        Assert.NotNull(anaSayfa["panel"]); Assert.NotNull(anaSayfa["kasaEsikleri"]);
        var haftalik = JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik"))!.AsArray();
        Assert.Contains(parca, (string)haftalik[^1]!["veriSagligiUyarisi"]!);
        Assert.Contains(parca, (string)JsonNode.Parse(await c.GetStringAsync(Aylik(senaryo.Ay)))!["veriSagligiUyarisi"]!);
        Assert.Null(JsonNode.Parse(await c.GetStringAsync("/api/rapor/aylik?yil=2025&ay=1"))!["veriSagligiUyarisi"]);

        // Log: kayıt kimliğiyle tam bir kez (iki tur ve ek okumalara rağmen).
        var kayitLoglari = loglar.Uyarilar.Where(m => m.Contains(parca, StringComparison.Ordinal)).ToList();
        Assert.True(kayitLoglari.Count == 1, $"{bozulma}: {kayitLoglari.Count} log\n" + string.Join("\n", loglar.Uyarilar));
        Assert.StartsWith("Veri karantinası [", kayitLoglari[0]);

        // Karantina kasayı değiştirmez: tutar kasada kalır, yalnız kanal dağılımı eksiktir (ya da kayıt hiç sayılmaz).
        var sonra = await Panel(c);
        Assert.Equal(once.GuncelKasa, sonra.GuncelKasa);
        Assert.Equal(once.DagilimBekleyenTutar + senaryo.Bekleyen, sonra.DagilimBekleyenTutar);
        Assert.Equal(Mezat(once) + senaryo.MezatFarki, Mezat(sonra));
    }

    /// <summary>Sağlam veride karantina yoktur: altın rapor tohumunun (bütün kayıt türleri) hiçbir uç yanıtı uyarı ya da karantina
    /// logu taşımaz; ana sayfa özeti tam döner. Yanıtların altın çıktıyla birebir eşitliğini <see cref="AltinRaporTests"/> sınar.</summary>
    [Fact]
    public async Task Saglam_veride_karantina_uyarisi_ve_logu_yoktur()
    {
        var loglar = new UyariToplayici();
        await using var f = new LogluFabrika(loglar, AltinTohum.Bugun); using var c = await f.EditorClientAsync();
        var tohum = await AltinTohum.Kur(f, c);
        var yanitlar = await AltinTohum.Yanitlar(c, tohum);
        var anaSayfa = JsonNode.Parse(await c.GetStringAsync("/api/rapor/ana-sayfa?gun=366"))!;

        Assert.NotNull(anaSayfa["takipOzeti"]);
        Assert.Null(anaSayfa["veriSagligiUyarisi"]);
        Assert.DoesNotContain("karantina", yanitlar.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(loglar.Uyarilar, m => m.Contains("Veri karantinası", StringComparison.Ordinal));
        // Geçersiz gün eskisi gibi 400 döner (takip özetinin doğrulaması karantinaya alınmaz).
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/rapor/ana-sayfa?gun=0")).StatusCode);
    }

    /// <summary>Birden çok bozuk kayıtta uyarı ilk beş kaydı adıyla, kalanını sayıyla söyler; her kayıt ayrı ve bir kez loglanır.</summary>
    [Fact]
    public async Task Cok_sayida_bozuk_kayit_uyarida_ozetlenir_her_biri_bir_kez_loglanir()
    {
        var loglar = new UyariToplayici();
        await using var f = new LogluFabrika(loglar); using var c = await Editor(f);
        var doc = await Ekstre(f, c, Enumerable.Range(0, 7).Select(i => ("Gider", 10m + i, new KanalPayYaz[] { new(1, 10m + i) })).ToArray());
        using (var scope = f.Services.CreateScope())
            Bozuk(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), "UPDATE EkstreKayitlar SET DagilimJson = '[{bozuk';");

        var uyari = (string)JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik"))!.AsArray()[^1]!["veriSagligiUyarisi"]!;
        Assert.StartsWith("Okunamayan 7 kayıt karantinaya alındı: ", uyari);
        Assert.Contains("; … ve 2 kayıt daha.", uyari);
        Assert.Equal(5, doc.Kayitlar.Count(k => uyari.Contains($"Ekstre kaydı #{k.Id} (", StringComparison.Ordinal)));
        await c.GetStringAsync("/api/rapor/panel");
        Assert.All(doc.Kayitlar, k => Assert.Single(loglar.Uyarilar, m => m.Contains($"Ekstre kaydı #{k.Id} (", StringComparison.Ordinal)));
        Assert.Equal(1_000m - 7 * 10m - 21m, (await Panel(c)).GuncelKasa);
        Assert.Equal(7 * 10m + 21m, (await Panel(c)).DagilimBekleyenTutar);
    }

    private static async Task<Senaryo> Kur(Bozulma bozulma, KasaWebFactory f, HttpClient c)
    {
        const string Mezat999 = "REPLACE({0}, '\"KanalId\":1,', '\"KanalId\":999,')";
        string Degistir(string alan) => string.Format(System.Globalization.CultureInfo.InvariantCulture, Mezat999, alan);
        switch (bozulma)
        {
            case Bozulma.EkstreGiderBilinmeyenKanal or Bozulma.EkstreGiderKanalsizPay or Bozulma.EkstreGiderBozukDagilim:
            {
                var id = (await Ekstre(f, c, ("Gider", 90m, [new(1, 90m)]))).Kayitlar.Single().Id;
                var yeni = bozulma switch
                {
                    Bozulma.EkstreGiderBilinmeyenKanal => Degistir("DagilimJson"),
                    Bozulma.EkstreGiderKanalsizPay => "REPLACE(DagilimJson, '\"KanalId\":1,', '\"KanalId\":null,')",
                    _ => "'[{\"KanalId\":1,\"Tutar\":'",
                };
                return new($"UPDATE EkstreKayitlar SET DagilimJson = {yeni} WHERE Id = {id};", _ => $"Ekstre kaydı #{id} (gider, 25.09.2026)", Month, 90m, 90m);
            }
            case Bozulma.EkstreGelirBilinmeyenKanal:
            {
                var id = (await Ekstre(f, c, ("Gelir", 200m, [new(1, 200m)]))).Kayitlar.Single().Id;
                return new($"UPDATE EkstreKayitlar SET DagilimJson = {Degistir("DagilimJson")} WHERE Id = {id};", _ => $"Ekstre kaydı #{id} (gelir, 25.09.2026)", Month, 0m, -200m);
            }
            case Bozulma.AylikGiderBilinmeyenKanal or Bozulma.AylikGiderRevizyonuYok:
            {
                var sablon = await Create(c, "Ozel", [new(1, 100m)]);
                var odeme = (await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{sablon.Id}/ode", Payment(sablon))).OdemeId!.Value;
                var sql = bozulma == Bozulma.AylikGiderBilinmeyenKanal
                    ? $"UPDATE AylikGiderRevizyonlar SET DagilimJson = {Degistir("DagilimJson")};"
                    : $"UPDATE AylikGiderOdemeler SET RevizyonId = 9999 WHERE Id = {odeme};";
                return new(sql, _ => $"Aylık gider ödemesi #{odeme} (", Month, 100m, 100m);
            }
            case Bozulma.AlisDagilimiHesaplanamaz:
            {
                var alis = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Today, "Tedarikçi", null, [new("Mal", 1_000m, [new(1, 1_000m)])]));
                alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/gonder", new AlisDurumYaz(alis.Surum));
                alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/onayla", new AlisDurumYaz(alis.Surum, "Uygun"));
                await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Today, 400m));
                // Kalem dağılımı ödemelerin altına iner: ödeme dağılımı (D'Hondt) hesaplanamaz.
                return new($"UPDATE AlisDagilimlar SET Tutar = '100' WHERE AlisKalemId IN (SELECT Id FROM AlisKalemler WHERE AlisId = {alis.Id});",
                    _ => $"Alış #{alis.Id} ödemesi", Month, 400m, 400m);
            }
            case Bozulma.TakipliKrediTaksitiBilinmeyenKanal or Bozulma.TakipliKrediCekimiBilinmeyenKanal:
            {
                // Çekim 10 Temmuz; taksitler 10 Ağustos'tan itibaren (Ağustos ve Eylül taksitleri kasaya işlenmiş).
                var kredi = await Post<KrediTakipDto>(c, "/api/takip/krediler", new KrediTakipYaz(Guid.NewGuid(), "Takip kredisi", 12_000m, new(2026, 7, 10), new(2026, 8, 10), 12, 1_000m, [1]));
                if (bozulma == Bozulma.TakipliKrediCekimiBilinmeyenKanal)
                    return new($"UPDATE TakipKrediler SET CekimPaylariJson = {Degistir("CekimPaylariJson")} WHERE KrediId = {kredi.Id};",
                        _ => $"Kredi #{kredi.Id} ('Takip kredisi') çekimi", Temmuz, 0m, -12_000m);
                return new($"UPDATE TakipKrediTaksitler SET DagilimJson = {Degistir("DagilimJson")} WHERE KrediId = {kredi.Id} AND No = 1;",
                    db => $"Kredi taksiti #{db.TakipKrediTaksitler.Single(t => t.KrediId == kredi.Id && t.No == 1).Id} ('Takip kredisi' 1. taksit, 10.08.2026)", Agustos, 1_000m, 1_000m);
            }
            case Bozulma.KartOdemesiBozukPaylar:
            {
                var kart = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Takip kartı", 10_000m, 5, 25, new(2026, 6, 1), 0m, []));
                kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, new(2026, 8, 1), "Malzeme", 300m, 1, null, [new(1, 300m)]));
                kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, new(2026, 9, 10), 300m));
                var odeme = kart.Odemeler.Single().Id;
                return new($"UPDATE TakipKartOdemeler SET PaylarJson = 'bozuk' WHERE Id = {odeme};",
                    _ => $"Kredi kartı #{kart.Id} ('Takip kartı'): ödemeleri hesaplanamadı (payları okunamayan ödeme: #{odeme}); 1 ödeme tam tutarıyla", Month, 300m, 300m, OzetDuser: true);
            }
            case Bozulma.EkGelirKanalsiz:
            {
                int id;
                using (var scope = f.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                    var hesap = new HesapEntity { Ad = "Eski hesap", Tur = "Kasa", AcilisTarihi = new(2026, 6, 1) };
                    db.Hesaplar.Add(hesap); db.SaveChanges();
                    var hareket = new HesapHareketEntity { HesapId = hesap.Id, KanalId = 1, Tarih = new(2026, 8, 14), Tutar = 450m, Aciklama = "Eski ek gelir" };
                    db.HesapHareketler.Add(hareket); db.SaveChanges(); id = hareket.Id;
                }
                return new($"UPDATE HesapHareketler SET KanalId = NULL WHERE Id = {id};", _ => $"Ek gelir #{id} (14.08.2026): kanalı yok", Agustos, 0m, -450m);
            }
            case Bozulma.EskiKrediPlaniGecersiz:
                // Eski (takipsiz) kredi, ödeme günü 0: taksit planı türetilemez. Takip özeti de onu okuyamaz.
                return new("""
                    INSERT INTO Krediler (Ad, CekilenTutar, CekimTarihi, TaksitSayisi, AylikOdeme, OdemeGunu, Kanal, KanalId, GerceklesmeTakibi)
                        VALUES ('Bozuk plan', '5000', '2026-07-01', 10, '500', 0, 'MEZAT', 1, 0);
                    """, db => $"Kredi #{db.Krediler.Single(k => k.Ad == "Bozuk plan").Id} ('Bozuk plan', çekim 01.07.2026): taksit planı geçersiz", Agustos, 0m, 0m, OzetDuser: true);
            default:
                throw new ArgumentOutOfRangeException(nameof(bozulma));
        }
    }

    /// <summary>Banka ekstresinden satırları (tarih bugün, özel dağılım) önizleyip kaydeder.</summary>
    private static async Task<EkstreBelgeDto> Ekstre(KasaWebFactory f, HttpClient c, params (string Tur, decimal Tutar, KanalPayYaz[] Paylar)[] satirlar)
    {
        int belge;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var okunan = satirlar.Select((s, i) => new EkstreOkunanSatir(i + 1, 1, "Kaynak " + (i + 1), Today, "Hareket " + (i + 1), s.Tutar,
                s.Tur == "Gelir" ? "Giris" : "Cikis", s.Tur, "Hareket", "TRY", [])).ToList();
            var d = new EkstreBelgeEntity { Kaynak = "Banka", Banka = "Akbank", HesapAdi = "İş hesabı", DosyaAdi = "dayaniklilik.pdf", DosyaOzeti = Guid.NewGuid().ToString(),
                Dosya = "%PDF-test"u8.ToArray(), Yuklendi = f.Saat!.GetUtcNow().ToUnixTimeMilliseconds(), SatirlarJson = JsonSerializer.Serialize(okunan) };
            db.EkstreBelgeler.Add(d); db.SaveChanges(); belge = d.Id;
        }
        var doc = (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{belge}"))!;
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), doc.Surum, satirlar.Select((s, i) => new EkstreSatirYaz(i + 1, Today, "Hareket " + (i + 1), s.Tutar, s.Tur, "Ozel", s.Paylar)).ToList());
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{belge}/onizleme", istek);
        return await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{belge}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = true });
    }

    /// <summary>Ham SQL (EF biçimlendirmesi olmadan: JSON süslü parantez içerir); yabancı anahtar denetimi geçici olarak kapatılır,
    /// eski/geri yüklenmiş veritabanındaki gibi eksik kaynak kurulabilir.</summary>
    private static void Bozuk(KasaDbContext db, string sql)
    {
        db.Database.OpenConnection();
        try
        {
            var baglanti = db.Database.GetDbConnection();
            string Tek(string komutMetni)
            {
                using var komut = baglanti.CreateCommand(); komut.CommandText = komutMetni;
                return Convert.ToString(komut.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
            }
            var fk = Tek("PRAGMA foreign_keys;");
            Tek("PRAGMA foreign_keys = OFF;");
            try { using var komut = baglanti.CreateCommand(); komut.CommandText = sql; Assert.True(komut.ExecuteNonQuery() > 0, sql); }
            finally { Tek($"PRAGMA foreign_keys = {fk};"); }
        }
        finally { db.Database.CloseConnection(); }
    }

    private static string Aylik(DateOnly ay) => $"/api/rapor/aylik?yil={ay.Year}&ay={ay.Month}";
    private static decimal Mezat(PanelDto panel) => panel.Kanallar.Single(k => k.KanalId == 1).Bakiye;

    private sealed class LogluFabrika : KasaWebFactory
    {
        private readonly UyariToplayici _loglar;
        public LogluFabrika(UyariToplayici loglar, DateOnly? bugun = null) { _loglar = loglar; Saat = new SabitSaat(bugun ?? Today); }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder); builder.ConfigureLogging(l => l.AddProvider(_loglar));
        }
    }
}
