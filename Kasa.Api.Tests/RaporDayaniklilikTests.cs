using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Kasa.Api.Tests.AylikGiderTests;

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
        // Okunabilen ama tutarsız dağılım: boş öğe ("[null]"), boş liste, tutardan eksik pay toplamı, aynı gidere ikinci bağ.
        EkstreGiderBosOge, EkstreGiderBosDagilim, EkstreGiderEksikPay, EkstreGelirBosOge, AylikGiderBosOge,
        TakipliKrediTaksitiBosOge, TakipliKrediCekimiBosOge, KartHarcamasiBosOge, EkstreGiderIkinciBag,
    }

    /// <param name="Sql">Kaydı bozan ham SQL (yabancı anahtar denetimi kapalıyken çalışır: eksik kaynak da kurulabilir).</param>
    /// <param name="Parca">Uyarıda ve logda kaydı kimliğiyle tanıtan metin (bozulmadan sonra okunur).</param>
    /// <param name="Ay">Kaydın dokunduğu ay: o ayın aylık raporu uyarı taşır.</param>
    /// <param name="Bekleyen">Panelin dağılım bekleyen tutarındaki artış.</param>
    /// <param name="MezatFarki">MEZAT kanal bakiyesindeki değişim. Bu senaryolarda kasa toplamı aynı kalır; kasada önceden sayılan
    /// tutarı olan geçiş kartının sapması <see cref="Gecis_kartinin_karantinasi_kasada_onceden_sayilan_payi_ikinci_kez_dusmez"/>'de.</param>
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
    [InlineData(Bozulma.EkstreGiderBosOge)]
    [InlineData(Bozulma.EkstreGiderBosDagilim)]
    [InlineData(Bozulma.EkstreGiderEksikPay)]
    [InlineData(Bozulma.EkstreGelirBosOge)]
    [InlineData(Bozulma.AylikGiderBosOge)]
    [InlineData(Bozulma.TakipliKrediTaksitiBosOge)]
    [InlineData(Bozulma.TakipliKrediCekimiBosOge)]
    [InlineData(Bozulma.KartHarcamasiBosOge)]
    [InlineData(Bozulma.EkstreGiderIkinciBag)]
    public async Task Bozuk_kayit_raporlari_dusurmez_karantinaya_alinir_uyari_ve_bir_kez_log_verir(Bozulma bozulma)
    {
        var loglar = new UyariToplayici();
        await using var f = new LogluFabrika(loglar);
        using var c = await Editor(f);
        var senaryo = await Kur(bozulma, f, c);
        var once = await Panel(c);
        var saglamAnaSayfa = JsonNode.Parse(await c.GetStringAsync("/api/rapor/ana-sayfa", TestContext.Current.CancellationToken))!;
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
                var yanit = await c.GetAsync(uc, TestContext.Current.CancellationToken);
                Assert.True(yanit.StatusCode == HttpStatusCode.OK, $"{bozulma} {uc}: {yanit.StatusCode} {await yanit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
            }

        // Uyarı: ana sayfa, haftalık raporun son dönemi ve kaydın ayı; ilgisiz ay uyarı taşımaz.
        var anaSayfa = JsonNode.Parse(await c.GetStringAsync("/api/rapor/ana-sayfa", TestContext.Current.CancellationToken))!;
        Assert.Contains(parca, (string)anaSayfa["veriSagligiUyarisi"]!);
        Assert.StartsWith("Okunamayan ", (string)anaSayfa["veriSagligiUyarisi"]!);
        Assert.Equal(senaryo.OzetDuser, anaSayfa["takipOzeti"] is null);
        if (senaryo.OzetDuser)
            Assert.Contains("Kart ve kredi takip özeti hesaplanamadı", (string)anaSayfa["veriSagligiUyarisi"]!);
        Assert.NotNull(anaSayfa["panel"]);
        Assert.NotNull(anaSayfa["kasaEsikleri"]);
        var haftalik = JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik", TestContext.Current.CancellationToken))!.AsArray();
        Assert.Contains(parca, (string)haftalik[^1]!["veriSagligiUyarisi"]!);
        Assert.Contains(parca, (string)JsonNode.Parse(await c.GetStringAsync(Aylik(senaryo.Ay), TestContext.Current.CancellationToken))!["veriSagligiUyarisi"]!);
        Assert.Null(JsonNode.Parse(await c.GetStringAsync("/api/rapor/aylik?yil=2025&ay=1", TestContext.Current.CancellationToken))!["veriSagligiUyarisi"]);

        // Log: kayıt kimliğiyle tam bir kez (iki tur ve ek okumalara rağmen).
        var kayitLoglari = loglar.Uyarilar.Where(m => m.Contains(parca, StringComparison.Ordinal)).ToList();
        Assert.True(kayitLoglari.Count == 1, $"{bozulma}: {kayitLoglari.Count} log\n" + string.Join("\n", loglar.Uyarilar));
        Assert.StartsWith("Veri karantinası [", kayitLoglari[0]);

        // Karantina kasayı değiştirmez: tutar kasada kalır, yalnız kanal dağılımı eksiktir (ya da kayıt hiç sayılmaz).
        var sonra = await Panel(c);
        Assert.Equal(once.GuncelKasa, sonra.GuncelKasa);
        Assert.Equal(once.DagilimBekleyenTutar + senaryo.Bekleyen, sonra.DagilimBekleyenTutar);
        Assert.Equal(Mezat(once) + senaryo.MezatFarki, Mezat(sonra));

        // Kasa hareket dökümü (gap-denetim-izi-gozlemlenebilirlik-3) karantinalı kayıtla da panelin genel kasasını ve kanal kasasını
        // verir: karantinaya alınan tutar dökümde "Dağılım bekliyor" ya da genel kasa satırıdır.
        var takipBaslangici = Month.AddMonths(-3).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var dokum = (await c.GetFromJsonAsync<KasaHareketleriDto>($"/api/kasa-hareketleri?baslangic={takipBaslangici}", cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal((1_000m, sonra.GuncelKasa), (dokum.AcilisBakiyesi, dokum.KapanisBakiyesi));
        Assert.Equal(dokum.KapanisBakiyesi - dokum.AcilisBakiyesi, dokum.Hareketler.Sum(h => h.GenelKasaEtkisi));
        var mezat = (await c.GetFromJsonAsync<KasaHareketleriDto>($"/api/kasa-hareketleri?baslangic={takipBaslangici}&kanalId=1", cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal(Mezat(sonra), mezat.KapanisBakiyesi);
    }

    /// <summary>Sağlam veride karantina yoktur: altın rapor tohumunun (bütün kayıt türleri) hiçbir uç yanıtı uyarı ya da karantina
    /// logu taşımaz; ana sayfa özeti tam döner. Yanıtların altın çıktıyla birebir eşitliğini <see cref="AltinRaporTests"/> sınar.</summary>
    [Fact]
    public async Task Saglam_veride_karantina_uyarisi_ve_logu_yoktur()
    {
        var loglar = new UyariToplayici();
        await using var f = new LogluFabrika(loglar, AltinTohum.Bugun);
        using var c = await f.EditorClientAsync();
        var tohum = await AltinTohum.Kur(f, c);
        var yanitlar = await AltinTohum.Yanitlar(c, tohum);
        var anaSayfa = JsonNode.Parse(await c.GetStringAsync("/api/rapor/ana-sayfa?gun=366", TestContext.Current.CancellationToken))!;

        Assert.NotNull(anaSayfa["takipOzeti"]);
        Assert.Null(anaSayfa["veriSagligiUyarisi"]);
        Assert.DoesNotContain("karantina", yanitlar.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(loglar.Uyarilar, m => m.Contains("Veri karantinası", StringComparison.Ordinal));
        // Geçersiz gün eskisi gibi 400 döner (takip özetinin doğrulaması karantinaya alınmaz).
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/rapor/ana-sayfa?gun=0", TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>Birden çok bozuk kayıtta uyarı ilk beş kaydı adıyla, kalanını sayıyla söyler; her kayıt ayrı ve bir kez loglanır.</summary>
    [Fact]
    public async Task Cok_sayida_bozuk_kayit_uyarida_ozetlenir_her_biri_bir_kez_loglanir()
    {
        var loglar = new UyariToplayici();
        await using var f = new LogluFabrika(loglar);
        using var c = await Editor(f);
        var doc = await Ekstre(f, c, Enumerable.Range(0, 7).Select(i => ("Gider", 10m + i, new KanalPayYaz[] { new(1, 10m + i) })).ToArray());
        using (var scope = f.Services.CreateScope())
            Bozuk(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), "UPDATE EkstreKayitlar SET DagilimJson = '[{bozuk';");

        var uyari = (string)JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik", TestContext.Current.CancellationToken))!.AsArray()[^1]!["veriSagligiUyarisi"]!;
        Assert.StartsWith("Okunamayan 7 kayıt karantinaya alındı: ", uyari);
        Assert.Contains("; … ve 2 kayıt daha.", uyari);
        Assert.Equal(5, doc.Kayitlar.Count(k => uyari.Contains($"Ekstre kaydı #{k.Id} (", StringComparison.Ordinal)));
        await c.GetStringAsync("/api/rapor/panel", TestContext.Current.CancellationToken);
        Assert.All(doc.Kayitlar, k => Assert.Single(loglar.Uyarilar, m => m.Contains($"Ekstre kaydı #{k.Id} (", StringComparison.Ordinal)));
        Assert.Equal(1_000m - 7 * 10m - 21m, (await Panel(c)).GuncelKasa);
        Assert.Equal(7 * 10m + 21m, (await Panel(c)).DagilimBekleyenTutar);
    }

    /// <summary>Kart karantinası nakit etkisini kanal dağılımından ayrı hesaplar: geçiş kartında ödemenin kasada önceden sayılmış
    /// devir borcuna düşen kısmı (KasadaOncedenSayilanTutar) kasadan ikinci kez düşmez, yalnız kalan nakit etkisi "Dağılım
    /// bekliyor" olur ve kasa aynı kalır. Ödemenin payları da okunamıyorsa hangi harcamayı kapattığı bilinmez: ödeme tam tutarıyla
    /// sayılır ve uyarı kasadaki olası sapmayı (en çok önceden sayılan tutar kadar) açıkça söyler.</summary>
    [Fact]
    public async Task Gecis_kartinin_karantinasi_kasada_onceden_sayilan_payi_ikinci_kez_dusmez()
    {
        await using var f = new LogluFabrika(new UyariToplayici());
        using var c = await Editor(f);
        int id;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var eski = new KrediKartiEntity { Ad = "Geçiş kartı", Borc = 100m, Limit = 1_000m, KesimTarihi = new(2000, 1, 5), SonOdemeTarihi = new(2000, 1, 25) };
            db.KrediKartlari.Add(eski);
            db.SaveChanges();
            id = eski.Id;
        }
        // Devir borcu 100, tamamı kasada önceden sayılmış; geçişten sonra MEZAT'a 60 harcama; 160 ödeme ikisini de kapatır.
        var kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/gecis", new KartGecisYaz(Guid.NewGuid(), 0, Today, 100m, 100m, [new(1, 100m)], "Önceden kasada sayıldı", true));
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, Today, "Malzeme", 60m, 1, null, [new(1, 60m)]));
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, Today, 160m));
        Assert.Equal(60m, kart.Odemeler.Single().KasaEtkisi);
        var once = await Panel(c);
        Assert.Equal(1_000m - 60m, once.GuncelKasa);

        // Devir harcamasının dağılımı okunamaz: kart karantinada; ödemenin nakit etkisi (60) "Dağılım bekliyor", kasa aynı.
        using (var scope = f.Services.CreateScope())
            Bozuk(scope.ServiceProvider.GetRequiredService<KasaDbContext>(),
                $"UPDATE TakipHarcamalar SET DagilimJson = '{{bozuk' WHERE KrediKartiId = {id} AND Aciklama = 'Onaylanan eski borç devri';");
        var sonra = await Panel(c);
        Assert.Equal(once.GuncelKasa, sonra.GuncelKasa);
        Assert.Equal(once.DagilimBekleyenTutar + 60m, sonra.DagilimBekleyenTutar);
        Assert.Equal(Mezat(once) + 60m, Mezat(sonra));
        var uyari = (string)JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik", TestContext.Current.CancellationToken))!.AsArray()[^1]!["veriSagligiUyarisi"]!;
        Assert.Contains($"Kredi kartı #{id} ('Geçiş kartı'): ödemelerin kanal dağılımı hesaplanamadı (dağılımı okunamayan harcama: #", uyari);
        Assert.Contains("1 ödemenin nakit etkisi (60,00 TL) 'Dağılım bekliyor' sayıldı", uyari);
        Assert.DoesNotContain("önceden sayılan", uyari);

        // Ödemenin payları da okunamaz: ödeme tam tutarıyla (160) sayılır; kasa önceden sayılan 100 kadar düşük görünür, uyarı söyler.
        using (var scope = f.Services.CreateScope())
            Bozuk(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), $"UPDATE TakipKartOdemeler SET PaylarJson = 'bozuk' WHERE KrediKartiId = {id};");
        var son = await Panel(c);
        Assert.Equal(once.GuncelKasa - 100m, son.GuncelKasa);
        Assert.Equal(once.DagilimBekleyenTutar + 160m, son.DagilimBekleyenTutar);
        uyari = (string)JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik", TestContext.Current.CancellationToken))!.AsArray()[^1]!["veriSagligiUyarisi"]!;
        Assert.Contains("payları okunamayan ödeme: #", uyari);
        Assert.Contains("payları okunamayan ödemenin kasada önceden sayılan kısmı ayrılamadı; kasa en çok 100,00 TL düşük görünebilir", uyari);
    }

    /// <summary>Kart karantinası devir iadesinin kasaya döndürdüğü önceden sayılan kısmı (TakipIadeHesaplari.KasadaSayilanDuzeltme)
    /// kart hesabıyla aynı kuralla sayar: düzeltme devrin önceden sayılan tutarından düşülür ve iade tarihinde kasaya geri döner.
    /// Kart karantinaya alınınca kasa aynı kalır (eskiden düzeltme kadar düşük görünüyordu); yalnız kanalı bilinmeyen iade
    /// "Dağılım bekliyor"dan düşer ve uyarı bunu söyler.</summary>
    [Fact]
    public async Task Gecis_kartinin_karantinasi_devir_iadesinin_kasaya_dondurdugu_tutari_kart_hesabiyla_ayni_sayar()
    {
        await using var f = new LogluFabrika(new UyariToplayici());
        using var c = await Editor(f);
        int id;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var eski = new KrediKartiEntity { Ad = "Geçiş kartı", Borc = 100m, Limit = 1_000m, KesimTarihi = new(2000, 1, 5), SonOdemeTarihi = new(2000, 1, 25) };
            db.KrediKartlari.Add(eski);
            db.SaveChanges();
            id = eski.Id;
        }
        // Devir borcu 100, tamamı kasada önceden sayılmış. 30 iade edilir: kasaya döner (düzeltme 30), devrin sayılan tutarı 70'e
        // iner. Bankanın istediği kalan 70 ödenir: kasada sayılmış kabul edilir, kasa etkisi 0.
        var kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/gecis", new KartGecisYaz(Guid.NewGuid(), 0, Today, 100m, 100m, [new(1, 100m)], "Önceden kasada sayıldı", true));
        var devir = Assert.Single(kart.Harcamalar);
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, Today, "Satıcı iadesi", -30m, 1, null, [], devir.Id));
        Assert.Equal(30m, kart.Harcamalar.Single(h => h.Tutar < 0).KasadaSayilanDuzeltme);
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, Today, 70m));
        Assert.Equal(0m, kart.Odemeler.Single().KasaEtkisi);
        var once = await Panel(c);

        // Devir harcamasının dağılımı okunamaz: kart karantinada. Kasa aynı; iadenin kasaya döndürdüğü 30 MEZAT yerine
        // "Dağılım bekliyor"dan düşer, ödemenin nakit etkisi yine 0.
        using (var scope = f.Services.CreateScope())
            Bozuk(scope.ServiceProvider.GetRequiredService<KasaDbContext>(),
                $"UPDATE TakipHarcamalar SET DagilimJson = '{{bozuk' WHERE KrediKartiId = {id} AND Aciklama = 'Onaylanan eski borç devri';");
        var sonra = await Panel(c);
        Assert.Equal(once.GuncelKasa, sonra.GuncelKasa);
        Assert.Equal(once.DagilimBekleyenTutar - 30m, sonra.DagilimBekleyenTutar);
        Assert.Equal(Mezat(once) - 30m, Mezat(sonra));
        var uyari = (string)JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik", TestContext.Current.CancellationToken))!.AsArray()[^1]!["veriSagligiUyarisi"]!;
        Assert.Contains($"Kredi kartı #{id} ('Geçiş kartı'): ödemelerin kanal dağılımı hesaplanamadı", uyari);
        Assert.Contains("1 ödemenin nakit etkisi (0,00 TL) 'Dağılım bekliyor' sayıldı", uyari);
        Assert.Contains("1 devir iadesinin kasaya döndürdüğü önceden sayılan tutar (30,00 TL) kasaya geri eklendi, kanalı 'Dağılım bekliyor'", uyari);

        // Ödemenin payları da okunamaz: ödeme tam tutarıyla (70) sayılır. Olası sapma iadeyle düşülmüş sayılan tutar (70) kadardır.
        using (var scope = f.Services.CreateScope())
            Bozuk(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), $"UPDATE TakipKartOdemeler SET PaylarJson = 'bozuk' WHERE KrediKartiId = {id};");
        var son = await Panel(c);
        Assert.Equal(once.GuncelKasa - 70m, son.GuncelKasa);
        uyari = (string)JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik", TestContext.Current.CancellationToken))!.AsArray()[^1]!["veriSagligiUyarisi"]!;
        Assert.Contains("payları okunamayan ödemenin kasada önceden sayılan kısmı ayrılamadı; kasa en çok 70,00 TL düşük görünebilir", uyari);
    }

    /// <summary>Bozuk kayıtla hesaplanan rapor dondurulmaz. Kapatılacak aylardan birine dokunan karantina kaydı varsa ay kapatma 409
    /// ile reddedilir; ileti ayı ve kaydı kimliğiyle söyler, kilit, kilit olayı ve görüntü yazılmaz. Kayda dokunmayan önceki ay
    /// kapanabilir. Kayıt düzeltilince ay kapanır ve dondurulmuş rapor uyarı taşımaz.</summary>
    [Fact]
    public async Task Karantinali_kayda_dokunan_ay_kapatilamaz_409_kaydi_kimligiyle_soyler()
    {
        await using var f = new LogluFabrika(new UyariToplayici());
        using var c = await Editor(f);
        var senaryo = await Kur(Bozulma.EkGelirKanalsiz, f, c); // Ağustos'ta kanalı olmayan eski ek gelir
        string parca;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Bozuk(db, senaryo.Sql);
            parca = senaryo.Parca(db);
        }

        var yanit = await c.PostAsJsonAsync("/api/ay-kilidi/kapat", await KilitIstegi(c, Agustos), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
        var hata = (string)JsonNode.Parse(await yanit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["hata"]!;
        Assert.StartsWith("2026-08 ayı kapatılamaz: raporuna giren 1 kayıt karantinada (", hata);
        Assert.Contains(parca, hata);
        Assert.EndsWith("Bozuk kayıtla hesaplanan rapor dondurulmaz; önce kaydı düzeltin, sonra ayı kapatın.", hata);
        var kilit = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi", cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Null(kilit.KilitliSonTarih);
        Assert.Empty(kilit.Gecmis);
        Assert.Equal(0, Goruntu(f));

        // Kayda dokunmayan Temmuz (Haziran'la birlikte) kapanır.
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/ay-kilidi/kapat", await KilitIstegi(c, Temmuz), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(2, Goruntu(f));

        // Kayıt düzeltilince Ağustos kapanır; dondurulmuş raporu uyarı taşımaz.
        using (var scope = f.Services.CreateScope())
            Bozuk(scope.ServiceProvider.GetRequiredService<KasaDbContext>(), "UPDATE HesapHareketler SET KanalId = 1 WHERE KanalId IS NULL;");
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/ay-kilidi/kapat", await KilitIstegi(c, Agustos), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        var agustos = JsonNode.Parse(await c.GetStringAsync(Aylik(Agustos), TestContext.Current.CancellationToken))!;
        Assert.True((bool)agustos["dondurulmus"]!);
        Assert.Null(agustos["veriSagligiUyarisi"]);
    }

    /// <summary>Karantina yalnız kaydın kendi verisinden doğan hatayı kapsar. Temiz veride rapor yolunda çıkan kod hatası
    /// (NullReferenceException, InvalidOperationException) karantinaya alınmaz: kayıt doğrulaması sorun bulmaz, istisna yükselir ve
    /// uç 500 döner; karantina uyarısı ve logu oluşmaz. Kesici bir kez çalışır: sonraki istek yine 200 döner.</summary>
    [Theory]
    [InlineData("TakipKartTaksitler", "/api/rapor/panel", true)]
    [InlineData("TakipKartTaksitler", "/api/rapor/panel", false)]
    [InlineData("KrediKartlari", "/api/rapor/ana-sayfa", true)]
    [InlineData("KrediKartlari", "/api/rapor/ana-sayfa", false)]
    public async Task Temiz_veride_kod_hatasi_karantinaya_alinmaz_rapor_hatayla_doner(string tablo, string uc, bool bosBasvuru)
    {
        var loglar = new UyariToplayici();
        var kesici = new KodHatasiKesici();
        await using var f = new LogluFabrika(loglar, kesici: kesici);
        using var c = await Editor(f);
        await Kur(Bozulma.KartOdemesiBozukPaylar, f, c); // temiz takipli kart: harcama ve ödeme (bozan SQL çalıştırılmaz)
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(uc, TestContext.Current.CancellationToken)).StatusCode);

        kesici.Kur(tablo, bosBasvuru ? new NullReferenceException("Benzetilmiş kod hatası") : new InvalidOperationException("Benzetilmiş kod hatası"));
        Assert.Equal(HttpStatusCode.InternalServerError, (await c.GetAsync(uc, TestContext.Current.CancellationToken)).StatusCode);
        Assert.False(kesici.Kurulu);
        Assert.DoesNotContain(loglar.Uyarilar, m => m.Contains("Veri karantinası", StringComparison.Ordinal));
        Assert.Null(JsonNode.Parse(await c.GetStringAsync(uc, TestContext.Current.CancellationToken))!["veriSagligiUyarisi"]);
    }

    private static async Task<Senaryo> Kur(Bozulma bozulma, KasaWebFactory f, HttpClient c)
    {
        const string Mezat999 = "REPLACE({0}, '\"KanalId\":1,', '\"KanalId\":999,')";
        string Degistir(string alan) => string.Format(System.Globalization.CultureInfo.InvariantCulture, Mezat999, alan);
        switch (bozulma)
        {
            case Bozulma.EkstreGiderBilinmeyenKanal or Bozulma.EkstreGiderKanalsizPay or Bozulma.EkstreGiderBozukDagilim
                or Bozulma.EkstreGiderBosOge or Bozulma.EkstreGiderBosDagilim or Bozulma.EkstreGiderEksikPay:
                {
                    var id = (await Ekstre(f, c, ("Gider", 90m, [new(1, 90m)]))).Kayitlar.Single().Id;
                    var yeni = bozulma switch
                    {
                        Bozulma.EkstreGiderBilinmeyenKanal => Degistir("DagilimJson"),
                        Bozulma.EkstreGiderKanalsizPay => "REPLACE(DagilimJson, '\"KanalId\":1,', '\"KanalId\":null,')",
                        Bozulma.EkstreGiderBosOge => "'[null]'",
                        Bozulma.EkstreGiderBosDagilim => "'[]'",
                        // Payların toplamı (60) gider tutarını (90) tutmaz: eksik 30 'Dağılım bekliyor' olur, MEZAT'ın 60'ı yerinde kalır.
                        Bozulma.EkstreGiderEksikPay => "'[{\"KanalId\":1,\"Kanal\":\"MEZAT\",\"Tutar\":60}]'",
                        _ => "'[{\"KanalId\":1,\"Tutar\":'",
                    };
                    var fark = bozulma == Bozulma.EkstreGiderEksikPay ? 30m : 90m;
                    return new($"UPDATE EkstreKayitlar SET DagilimJson = {yeni} WHERE Id = {id};", _ => $"Ekstre kaydı #{id} (gider, 25.09.2026)", Month, fark, fark);
                }
            case Bozulma.EkstreGiderIkinciBag:
                {
                    // Benzersizlik dizini kaldırılmış (eski/geri yüklenmiş) veritabanında aynı gidere ikinci ekstre kaydı bağlanır: gider bir
                    // kez, ilk kaydın dağılımıyla sayılır; ikinci bağ rapora alınmaz ve karantinada görünür.
                    var id = (await Ekstre(f, c, ("Gider", 90m, [new(1, 90m)]))).Kayitlar.Single().Id;
                    return new($"""
                    DROP INDEX IX_EkstreKayitlar_IslemId;
                    INSERT INTO EkstreKayitlar (BelgeId, SatirNo, Tarih, Aciklama, Tutar, IslemTuru, DagilimTuru, DagilimJson, IslemId, Iptal)
                        SELECT BelgeId, SatirNo + 100, Tarih, Aciklama, Tutar, IslemTuru, DagilimTuru, '[]', IslemId, 0 FROM EkstreKayitlar WHERE Id = {id};
                    """, db => $"Ekstre kaydı #{db.EkstreKayitlar.Max(k => k.Id)} (gider, 25.09.2026)", Month, 0m, 0m);
                }
            case Bozulma.EkstreGelirBilinmeyenKanal or Bozulma.EkstreGelirBosOge:
                {
                    var id = (await Ekstre(f, c, ("Gelir", 200m, [new(1, 200m)]))).Kayitlar.Single().Id;
                    var yeni = bozulma == Bozulma.EkstreGelirBosOge ? "'[null]'" : Degistir("DagilimJson");
                    return new($"UPDATE EkstreKayitlar SET DagilimJson = {yeni} WHERE Id = {id};", _ => $"Ekstre kaydı #{id} (gelir, 25.09.2026)", Month, 0m, -200m);
                }
            case Bozulma.AylikGiderBilinmeyenKanal or Bozulma.AylikGiderRevizyonuYok or Bozulma.AylikGiderBosOge:
                {
                    var sablon = await Create(c, "Ozel", [new(1, 100m)]);
                    var odeme = (await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{sablon.Id}/ode", Payment(sablon))).OdemeId!.Value;
                    var sql = bozulma switch
                    {
                        Bozulma.AylikGiderBilinmeyenKanal => $"UPDATE AylikGiderRevizyonlar SET DagilimJson = {Degistir("DagilimJson")};",
                        Bozulma.AylikGiderBosOge => "UPDATE AylikGiderRevizyonlar SET DagilimJson = '[null]';",
                        _ => $"UPDATE AylikGiderOdemeler SET RevizyonId = 9999 WHERE Id = {odeme};",
                    };
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
            case Bozulma.TakipliKrediTaksitiBilinmeyenKanal or Bozulma.TakipliKrediCekimiBilinmeyenKanal
                or Bozulma.TakipliKrediTaksitiBosOge or Bozulma.TakipliKrediCekimiBosOge:
                {
                    // Çekim 10 Temmuz; taksitler 10 Ağustos'tan itibaren (Ağustos ve Eylül taksitleri kasaya işlenmiş). Boş öğeli dağılımı
                    // takip özetinin kredi hesabı da okuyamaz; bilinmeyen kanalı ise "Silinmiş kanal" adıyla gösterir.
                    var kredi = await Post<KrediTakipDto>(c, "/api/takip/krediler", new KrediTakipYaz(Guid.NewGuid(), "Takip kredisi", 12_000m, new(2026, 7, 10), new(2026, 8, 10), 12, 1_000m, [1]));
                    var bosOge = bozulma is Bozulma.TakipliKrediTaksitiBosOge or Bozulma.TakipliKrediCekimiBosOge;
                    if (bozulma is Bozulma.TakipliKrediCekimiBilinmeyenKanal or Bozulma.TakipliKrediCekimiBosOge)
                        return new($"UPDATE TakipKrediler SET CekimPaylariJson = {(bosOge ? "'[null]'" : Degistir("CekimPaylariJson"))} WHERE KrediId = {kredi.Id};",
                            _ => $"Kredi #{kredi.Id} ('Takip kredisi') çekimi", Temmuz, 0m, -12_000m, OzetDuser: bosOge);
                    return new($"UPDATE TakipKrediTaksitler SET DagilimJson = {(bosOge ? "'[null]'" : Degistir("DagilimJson"))} WHERE KrediId = {kredi.Id} AND No = 1;",
                        db => $"Kredi taksiti #{db.TakipKrediTaksitler.Single(t => t.KrediId == kredi.Id && t.No == 1).Id} ('Takip kredisi' 1. taksit, 10.08.2026)", Agustos, 1_000m, 1_000m, OzetDuser: bosOge);
                }
            case Bozulma.KartOdemesiBozukPaylar or Bozulma.KartHarcamasiBosOge:
                {
                    var kart = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Takip kartı", 10_000m, 5, 25, new(2026, 6, 1), 0m, []));
                    kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, new(2026, 8, 1), "Malzeme", 300m, 1, null, [new(1, 300m)]));
                    kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, new(2026, 9, 10), 300m));
                    var odeme = kart.Odemeler.Single().Id;
                    var basi = $"Kredi kartı #{kart.Id} ('Takip kartı'): ödemelerin kanal dağılımı hesaplanamadı";
                    if (bozulma == Bozulma.KartHarcamasiBosOge)
                    {
                        // Harcamanın boş öğeli dağılımı kart hesabında NullReferenceException verir; kayıt doğrulaması onu veri hatası sayar.
                        var harcama = kart.Harcamalar.Single().Id;
                        return new($"UPDATE TakipHarcamalar SET DagilimJson = '[null]' WHERE Id = {harcama};",
                            _ => $"{basi} (dağılımı okunamayan harcama: #{harcama}); 1 ödemenin nakit etkisi (300,00 TL)", Month, 300m, 300m, OzetDuser: true);
                    }
                    return new($"UPDATE TakipKartOdemeler SET PaylarJson = 'bozuk' WHERE Id = {odeme};",
                        _ => $"{basi} (payları okunamayan ödeme: #{odeme}); 1 ödemenin nakit etkisi (300,00 TL)", Month, 300m, 300m, OzetDuser: true);
                }
            case Bozulma.EkGelirKanalsiz:
                {
                    int id;
                    using (var scope = f.Services.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
                        var hesap = new HesapEntity { Ad = "Eski hesap", Tur = "Kasa", AcilisTarihi = new(2026, 6, 1) };
                        db.Hesaplar.Add(hesap);
                        db.SaveChanges();
                        var hareket = new HesapHareketEntity { HesapId = hesap.Id, KanalId = 1, Tarih = new(2026, 8, 14), Tutar = 450m, Aciklama = "Eski ek gelir" };
                        db.HesapHareketler.Add(hareket);
                        db.SaveChanges();
                        id = hareket.Id;
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
            var d = new EkstreBelgeEntity
            {
                Kaynak = "Banka",
                Banka = "Akbank",
                HesapAdi = "İş hesabı",
                DosyaAdi = "dayaniklilik.pdf",
                DosyaOzeti = Guid.NewGuid().ToString(),
                Yuklendi = f.Saat!.GetUtcNow().ToUnixTimeMilliseconds(),
                SatirlarJson = JsonSerializer.Serialize(okunan)
            };
            db.EkstreBelgeler.Add(d);
            db.SaveChanges();
            belge = d.Id;
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
                using var komut = baglanti.CreateCommand();
                komut.CommandText = komutMetni;
                return Convert.ToString(komut.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
            }
            var fk = Tek("PRAGMA foreign_keys;");
            Tek("PRAGMA foreign_keys = OFF;");
            try
            { using var komut = baglanti.CreateCommand(); komut.CommandText = sql; Assert.True(komut.ExecuteNonQuery() > 0, sql); }
            finally { Tek($"PRAGMA foreign_keys = {fk};"); }
        }
        finally { db.Database.CloseConnection(); }
    }

    private static string Aylik(DateOnly ay) => $"/api/rapor/aylik?yil={ay.Year}&ay={ay.Month}";
    private static decimal Mezat(PanelDto panel) => panel.Kanallar.Single(k => k.KanalId == 1).Bakiye;

    private static async Task<AyKilidiYaz> KilitIstegi(HttpClient c, DateOnly ay)
    {
        var durum = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi"))!;
        return new(Guid.NewGuid(), durum.Surum, ay.Year, ay.Month, "Ay tamamlandı");
    }

    private static int Goruntu(KasaWebFactory f)
    {
        using var scope = f.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<KasaDbContext>().AyRaporAnlikGoruntuleri.Count();
    }

    private sealed class LogluFabrika : KasaWebFactory
    {
        private readonly UyariToplayici _loglar;
        private readonly KodHatasiKesici? _kesici;
        public LogluFabrika(UyariToplayici loglar, DateOnly? bugun = null, KodHatasiKesici? kesici = null)
        {
            _loglar = loglar;
            _kesici = kesici;
            Saat = new SabitSaat(bugun ?? Today);
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(l => l.AddProvider(_loglar));
            if (_kesici is { } kesici)
                builder.ConfigureServices(s => s.ConfigureDbContext<KasaDbContext>(o => o.AddInterceptors(kesici)));
        }
    }

    /// <summary>Kurulunca verilen tabloyu okuyan ilk komutta verilen istisnayı bir kez fırlatır (temiz veride kod hatası benzetimi).</summary>
    private sealed class KodHatasiKesici : DbCommandInterceptor
    {
        private sealed record Ariza(string Tablo, Exception Hata);
        private Ariza? _sonraki;
        public bool Kurulu => Volatile.Read(ref _sonraki) is not null;
        public void Kur(string tablo, Exception hata) => Volatile.Write(ref _sonraki, new(tablo, hata));
        private void Firlat(DbCommand komut)
        {
            if (Volatile.Read(ref _sonraki) is { } a && komut.CommandText.Contains($"\"{a.Tablo}\"", StringComparison.Ordinal)
                && ReferenceEquals(Interlocked.CompareExchange(ref _sonraki, null, a), a))
                throw a.Hata;
        }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r) { Firlat(c); return r; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r, CancellationToken ct = default)
        { Firlat(c); return ValueTask.FromResult(r); }
    }
}
