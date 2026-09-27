using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Benzer kayıt kontrolü çapraz yollarda simetriktir (gap-coklu-giris-cift-sayim-mutabakat-2, -9, statement-11): aynı
/// para genel gider, Ortak gider, bankadan genel/çok kanallı gider, taslak/onaylı alış ödemesi, aylık gider ödemesi, kart
/// ödemesi ya da kredi taksiti olarak girilmiş olsun; hangi yoldan sorulursa sorulsun ±3 gün içinde aynı tutarla bulunur.
/// Kanal süzgeci yalnız kanalı kesin olarak başka olan kaydı eler: genel kasa, Ortak, dağılım bekleyen ve kanalı kesişen
/// çok kanallı kayıtlar görünür. Ekstre önizlemesi aynı servisi kullanır.
/// </summary>
public class BenzerKayitCaprazTests
{
    private static DateOnly Bugun => KasaWebFactory.VarsayilanBugun;
    /// <summary>Kaynak kaydın tarihi sorgudan iki gün öncedir: eşleşme ±3 günlük pencereden gelir.</summary>
    private static DateOnly KaynakTarihi => Bugun.AddDays(-2);
    private const decimal Tutar = 1_234.50m;
    private const int Mezat = 1, Perakende = 2, Toptan = 3;

    public enum Kaynak { ManuelKanal, ManuelOrtak, EkstreGenel, EkstreCokKanalli, TaslakAlisOdemesi, OnayliAlisOdemesi, AylikGenel, AylikCokKanalli, KartOdemesi, KrediTaksidi, EskiKrediTaksidi }
    private sealed record Beklenen(string Kaynak, int Id, string? KanalEtiketi, int? AlisId = null, int? EkstreKayitId = null, int? AylikGiderOdemeId = null);

    [Theory]
    [InlineData(Kaynak.ManuelKanal, false, true)]
    [InlineData(Kaynak.ManuelOrtak, true, true)]
    [InlineData(Kaynak.EkstreGenel, true, true)]
    [InlineData(Kaynak.EkstreCokKanalli, false, true)]
    [InlineData(Kaynak.TaslakAlisOdemesi, true, true)]
    [InlineData(Kaynak.OnayliAlisOdemesi, false, true)]
    [InlineData(Kaynak.AylikGenel, true, true)]
    [InlineData(Kaynak.AylikCokKanalli, false, true)]
    [InlineData(Kaynak.KartOdemesi, true, true)]
    [InlineData(Kaynak.KrediTaksidi, true, false)]
    [InlineData(Kaynak.EskiKrediTaksidi, true, false)]
    public async Task Her_kaynak_her_sorgu_yolundan_bulunur_kanal_yalniz_kesin_baska_kanali_eler(Kaynak kaynak, bool perakendedeGorunur, bool kartOdemesindeGorunur)
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        var kart = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Kart A", 50_000m, 5, 25, new(2026, 1, 1), 0m, []));
        var taslak = await Alis(c, Mezat);
        var onayli = await Onayla(c, await Alis(c, Perakende));
        var beklenen = await Kur(f, c, kaynak, kart);

        async Task Gorunur(BenzerKayitSorgu sorgu, bool gorunur)
        {
            var sonuc = await Bul(c, sorgu);
            if (!gorunur) { Assert.Empty(sonuc); return; }
            var kayit = Assert.Single(sonuc);
            Assert.Equal((beklenen.Kaynak, beklenen.Id, beklenen.KanalEtiketi), (kayit.Kaynak, kayit.Id, kayit.KanalEtiketi));
            Assert.Equal((beklenen.AlisId, beklenen.EkstreKayitId, beklenen.AylikGiderOdemeId), (kayit.AlisId, kayit.EkstreKayitId, kayit.AylikGiderOdemeId));
            Assert.Equal(KaynakTarihi, kayit.Tarih); Assert.Equal(Tutar, kayit.Tutar);
        }
        await Gorunur(new("Gider", Bugun, Tutar, Kanal: "MEZAT"), true);
        await Gorunur(new("Gider", Bugun, Tutar, Kanal: "Ortak"), true);
        await Gorunur(new("Gider", Bugun, Tutar, Kanal: "PERAKENDE"), perakendedeGorunur);
        await Gorunur(new("AylikGider", Bugun, Tutar), true);
        await Gorunur(new("AlisOdeme", Bugun, Tutar, AlisId: taslak.Id), true);
        await Gorunur(new("AlisOdeme", Bugun, Tutar, AlisId: onayli.Id), perakendedeGorunur);
        await Gorunur(new("KartOdeme", Bugun, Tutar, kart.Id), kartOdemesindeGorunur);
        // Tutar farkı hiçbir yolda eşleşmez.
        Assert.Empty(await Bul(c, new("AylikGider", Bugun, Tutar + 0.01m)));

        // Ekstre önizlemesi aynı kuralla (kanal ayırmadan) uyarır ve ayrıca onay ister.
        var belge = await EkstreBelgesi(f, c, Bugun, Tutar);
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{belge.Id}/onizleme",
            new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [new(1, Bugun, "Banka hareketi", Tutar, "Gider", "Genel", [])]));
        Assert.True(onizleme.TekrarOnayGerekli);
        var uyari = Assert.Single(onizleme.Satirlar[0].Uyarilar, u => u.StartsWith("Benzer kayıt:", StringComparison.Ordinal));
        Assert.Contains($"#{beklenen.Id} ", uyari);
        Assert.Contains(KaynakTarihi.ToString("dd.MM.yyyy"), uyari);
    }

    [Fact]
    public async Task Pencere_arti_eksi_uc_gundur_iki_yonde_de_ayni_kuralla_uygulanir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        await EkstreGideri(f, c, KaynakTarihi, Tutar, "Genel", []);
        foreach (var gun in new[] { -3, 0, 3 })
            Assert.Single(await Bul(c, new("Gider", KaynakTarihi.AddDays(gun), Tutar, Kanal: "TOPTAN")));
        foreach (var gun in new[] { -4, 4 })
            Assert.Empty(await Bul(c, new("Gider", KaynakTarihi.AddDays(gun), Tutar, Kanal: "TOPTAN")));
        // Ters yön: elle girilen gider sonradan işlenen banka satırının önizlemesinde de aynı pencereyle görünür.
        await PostCreated(c, new IslemYazDto(KaynakTarihi.AddDays(-5), "Elle girilen", 77m, "MEZAT", GiderTipi.Cari));
        var belge = await EkstreBelgesi(f, c, KaynakTarihi.AddDays(-2), 77m);
        var yakin = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{belge.Id}/onizleme",
            new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [new(1, KaynakTarihi.AddDays(-2), "Banka hareketi", 77m, "Gider", "Genel", [])]));
        Assert.True(yakin.TekrarOnayGerekli); Assert.Contains(yakin.Satirlar[0].Uyarilar, u => u.StartsWith("Benzer kayıt:", StringComparison.Ordinal));
        var uzak = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{belge.Id}/onizleme",
            new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [new(1, KaynakTarihi.AddDays(-1), "Banka hareketi", 77m, "Gider", "Genel", [])]));
        Assert.False(uzak.TekrarOnayGerekli);
    }

    [Fact]
    public async Task Iptal_edilen_kaynaklar_ve_baska_kartin_odemesi_gorunmez()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        var kart = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Kart A", 50_000m, 5, 25, new(2026, 1, 1), 0m, []));
        var baska = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Kart B", 50_000m, 5, 25, new(2026, 1, 1), 0m, []));
        var (belge, satir) = await EkstreGideri(f, c, KaynakTarihi, Tutar, "Genel", []);
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{belge.Id}/kayitlar/{satir.Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış satır"));
        var odenen = await AylikOde(c, "Genel", []);
        await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/odemeler/{odenen.OdemeId}/iptal", new AylikGiderIptalYaz(Guid.NewGuid(), "Hatalı ödeme"));
        baska = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{baska.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), baska.Surum, KaynakTarihi, Tutar));
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, KaynakTarihi, Tutar));
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler/{kart.Odemeler.Single().Id}/iptal", new TakipIptalYaz(Guid.NewGuid(), kart.Surum, "Mükerrer"));
        Seed(f, db =>
        {
            var kredi = new KrediEntity { Ad = "İptal taksitli kredi", CekilenTutar = 10_000m, CekimTarihi = new(2026, 8, 10), TaksitSayisi = 12, AylikOdeme = 1m, OdemeGunu = 23, Kanal = "MEZAT", KanalId = Mezat };
            db.Krediler.Add(kredi); db.SaveChanges();
            db.TakipKrediler.Add(new() { KrediId = kredi.Id, Baslangic = new(2026, 1, 1) }); db.SaveChanges();
            db.TakipKrediTaksitler.Add(new() { KrediId = kredi.Id, No = 2, Tarih = KaynakTarihi, Tutar = Tutar, Iptal = true, DagilimJson = Pay(Mezat, Tutar) }); db.SaveChanges();
        });
        // Yalnız B kartının iptal edilmemiş ödemesi kalır: kartsız gider sorgusunda görünür, A kartının ödeme sorgusunda görünmez.
        Assert.Equal("KartOdeme", Assert.Single(await Bul(c, new("Gider", Bugun, Tutar, Kanal: "MEZAT"))).Kaynak);
        Assert.Empty(await Bul(c, new("KartOdeme", Bugun, Tutar, kart.Id)));
    }

    [Fact]
    public async Task Benzerlik_sorgusu_yazma_kilidi_almaz_ve_paralel_yazma_beklemez()
    {
        var kapi = new OkumaYoluTests.OkumaKapisi();
        await using var f = new DosyaFabrikasi { Kesiciler = [kapi] };
        using var c = await Editor(f);
        await PostCreated(c, new IslemYazDto(KaynakTarihi, "Önceki", Tutar, "MEZAT", GiderTipi.Cari));
        kapi.Kur("Islemler");
        var okuma = c.PostAsJsonAsync("/api/islemler/benzerlik", new BenzerKayitSorgu("Gider", Bugun, Tutar, Kanal: "MEZAT"));
        Assert.True(await kapi.Girildi(), "Benzerlik sorgusu gider tablosuna ulaşmadı.");
        HttpResponseMessage yazma; var sure = Stopwatch.StartNew();
        try { yazma = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Bugun, "Okuma sırasında", Tutar, "MEZAT", GiderTipi.Cari)); }
        finally { sure.Stop(); kapi.Birak(); }
        var yanit = await okuma;
        Assert.Equal(HttpStatusCode.Created, yazma.StatusCode);
        Assert.True(sure.Elapsed < TimeSpan.FromSeconds(3), $"Yazma benzerlik sorgusunu {sure.ElapsedMilliseconds} ms bekledi.");
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        // Sorgu başladığı andaki anlık görüntüyü görür; paralel yazma sonraki sorguda görünür.
        Assert.Single((await yanit.Content.ReadFromJsonAsync<List<BenzerKayitDto>>())!);
        Assert.Equal(2, (await Bul(c, new("Gider", Bugun, Tutar, Kanal: "MEZAT"))).Count);
    }

    [Fact]
    public async Task Ekstre_onizlemesi_Sync_dahil_kalici_yazmaz_kaydet_ayni_ozetle_yazar()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        // Kesim ekstresi henüz türetilmemiş takipli kart: tarihe bağlı türetme (Sync) yalnız gerçek yazmada kalıcı olur.
        Seed(f, db =>
        {
            db.KrediKartlari.Add(new() { Id = 7, Ad = "Takip kartı", KesimTarihi = new(2026, 1, 10), SonOdemeTarihi = new(2026, 1, 20), Limit = 10_000m }); db.SaveChanges();
            db.TakipKartlar.Add(new() { KrediKartiId = 7, Baslangic = new(2026, 1, 1) }); db.SaveChanges();
        });
        var belge = await EkstreBelgesi(f, c, Bugun, 55m);
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [new(1, Bugun, "Banka hareketi", 55m, "Gider", "Genel", [])]);
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{belge.Id}/onizleme", istek);
        Assert.Equal(-55m, onizleme.KasaEtkisi);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.Empty(db.TakipEkstreler); Assert.Empty(db.Islemler); Assert.Empty(db.EkstreKayitlar);
        }
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{belge.Id}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti });
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.NotEmpty(db.TakipEkstreler); Assert.Single(db.Islemler);
        }
    }

    private static async Task<Beklenen> Kur(KasaWebFactory f, HttpClient c, Kaynak kaynak, KartTakipDto kart)
    {
        switch (kaynak)
        {
            case Kaynak.ManuelKanal:
                return new("Islem", await PostCreated(c, new IslemYazDto(KaynakTarihi, "Manuel", Tutar, "MEZAT", GiderTipi.Cari)), "MEZAT");
            case Kaynak.ManuelOrtak:
                return new("Islem", await PostCreated(c, new IslemYazDto(KaynakTarihi, "Ortak gider", Tutar, Kanallar.Ortak, GiderTipi.Cari)), Kanallar.Ortak);
            case Kaynak.EkstreGenel:
            {
                var (_, satir) = await EkstreGideri(f, c, KaynakTarihi, Tutar, "Genel", []);
                return new("Islem", satir.IslemId!.Value, "Genel kasa", EkstreKayitId: satir.Id);
            }
            case Kaynak.EkstreCokKanalli:
            {
                var (_, satir) = await EkstreGideri(f, c, KaynakTarihi, Tutar, "Esit", [new(Mezat, 0), new(Toptan, 0)]);
                return new("Islem", satir.IslemId!.Value, "MEZAT / TOPTAN", EkstreKayitId: satir.Id);
            }
            case Kaynak.TaslakAlisOdemesi or Kaynak.OnayliAlisOdemesi:
            {
                var alis = await Alis(c, Mezat);
                alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), KaynakTarihi, Tutar));
                if (kaynak == Kaynak.OnayliAlisOdemesi) alis = await Onayla(c, alis);
                return new("Islem", alis.Odemeler.Single().IslemId, kaynak == Kaynak.OnayliAlisOdemesi ? "MEZAT" : Kanallar.DagilimBekliyor, AlisId: alis.Id);
            }
            case Kaynak.AylikGenel or Kaynak.AylikCokKanalli:
            {
                var odenen = kaynak == Kaynak.AylikGenel ? await AylikOde(c, "Genel", []) : await AylikOde(c, "Esit", [new(Mezat, 0), new(Toptan, 0)]);
                return new("Islem", odenen.IslemId!.Value, kaynak == Kaynak.AylikGenel ? "Genel kasa" : "MEZAT / TOPTAN", AylikGiderOdemeId: odenen.OdemeId);
            }
            case Kaynak.KartOdemesi:
            {
                kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, KaynakTarihi, Tutar));
                return new("KartOdeme", kart.Odemeler.Single().Id, null);
            }
            case Kaynak.KrediTaksidi:
            {
                var id = 0;
                // Takipli kredinin eski türetme kuralı da aynı gün ve tutarı üretirdi; takipli kredide yalnız takip taksidi sayılır.
                Seed(f, db =>
                {
                    var kredi = new KrediEntity { Ad = "Takipli kredi", CekilenTutar = 10_000m, CekimTarihi = new(2026, 8, 10), TaksitSayisi = 12, AylikOdeme = Tutar, OdemeGunu = KaynakTarihi.Day, Kanal = "MEZAT", KanalId = Mezat };
                    db.Krediler.Add(kredi); db.SaveChanges();
                    db.TakipKrediler.Add(new() { KrediId = kredi.Id, Baslangic = new(2026, 1, 1) }); db.SaveChanges();
                    var taksit = new TakipKrediTaksitEntity { KrediId = kredi.Id, No = 2, Tarih = KaynakTarihi, Tutar = Tutar, DagilimJson = Pay(Mezat, Tutar) };
                    db.TakipKrediTaksitler.Add(taksit); db.SaveChanges(); id = taksit.Id;
                });
                return new("KrediTaksidi", id, "MEZAT");
            }
            case Kaynak.EskiKrediTaksidi:
            {
                var id = 0;
                Seed(f, db =>
                {
                    var kredi = new KrediEntity { Ad = "Eski kredi", CekilenTutar = 10_000m, CekimTarihi = new(2026, 8, 10), TaksitSayisi = 12, AylikOdeme = Tutar, OdemeGunu = KaynakTarihi.Day, Kanal = "MEZAT", KanalId = Mezat };
                    db.Krediler.Add(kredi); db.SaveChanges(); id = kredi.Id;
                });
                return new("EskiKrediTaksidi", id, "MEZAT");
            }
            default: throw new ArgumentOutOfRangeException(nameof(kaynak));
        }
    }

    internal static async Task<HttpClient> Editor(KasaWebFactory f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(2026, 1, 1), kasaAcilisDevri = 100_000m })).EnsureSuccessStatusCode();
        return c;
    }
    /// <summary>Tek satırlık banka ekstresi belgesi (PDF okuma adımı atlanır; satırlar belgeye hazır yazılır).</summary>
    internal static async Task<EkstreBelgeDto> EkstreBelgesi(KasaWebFactory f, HttpClient c, DateOnly tarih, decimal tutar)
    {
        int id = 0;
        Seed(f, db =>
        {
            var d = new EkstreBelgeEntity { Kaynak = "Banka", Banka = "Akbank", HesapAdi = "İş hesabı", DosyaAdi = "test.pdf", DosyaOzeti = Guid.NewGuid().ToString(),
                Dosya = "%PDF-test"u8.ToArray(), Yuklendi = f.Saat!.GetUtcNow().ToUnixTimeMilliseconds(),
                SatirlarJson = JsonSerializer.Serialize(new[] { new EkstreOkunanSatir(1, 1, "Kaynak 1", tarih, "Banka hareketi", tutar, "Cikis", "Gider", "Hareket", "TRY", []) }) };
            db.EkstreBelgeler.Add(d); db.SaveChanges(); id = d.Id;
        });
        return (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{id}"))!;
    }
    /// <summary>Bankadan gider satırını önizleyip (benzer kayıt onayıyla) kaydeder.</summary>
    internal static async Task<(EkstreBelgeDto Belge, EkstreKayitDto Satir)> EkstreGideri(KasaWebFactory f, HttpClient c, DateOnly tarih, decimal tutar, string dagilimTuru, IReadOnlyList<KanalPayYaz> paylar)
    {
        var belge = await EkstreBelgesi(f, c, tarih, tutar);
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [new(1, tarih, "Banka hareketi", tutar, "Gider", dagilimTuru, paylar)]);
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{belge.Id}/onizleme", istek);
        belge = await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{belge.Id}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = true });
        return (belge, belge.Kayitlar.Single(k => !k.Iptal));
    }
    private static async Task<AylikGiderSatirDto> AylikOde(HttpClient c, string dagilimTuru, IReadOnlyList<KanalPayYaz> paylar)
    {
        var sablon = await Post<AylikGiderSablonDto>(c, "/api/aylik-giderler/sablonlar", new AylikGiderSablonYaz(Guid.NewGuid(), 0, "Kira", "Kira", Tutar, 1, dagilimTuru, paylar, new(Bugun.Year, Bugun.Month, 1)));
        return await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{sablon.Id}/ode", new AylikGiderOdemeYaz(Guid.NewGuid(), sablon.Surum, Bugun.Year, Bugun.Month, KaynakTarihi));
    }
    private static Task<AlisDto> Alis(HttpClient c, int kanal) => Post<AlisDto>(c, "/api/alis", new AlisYaz(0, KaynakTarihi, "Satıcı", null, [new("Mal", 5_000m, [new(kanal, 5_000m)])]));
    private static async Task<AlisDto> Onayla(HttpClient c, AlisDto alis)
    {
        var gonderilen = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/gonder", new AlisDurumYaz(alis.Surum));
        return await Post<AlisDto>(c, $"/api/alis/{alis.Id}/onayla", new AlisDurumYaz(gonderilen.Surum));
    }
    private static string Pay(int kanal, decimal tutar) => JsonSerializer.Serialize(new[] { new KanalPayYaz(kanal, tutar) });
    private static void Seed(KasaWebFactory f, Action<KasaDbContext> action) { using var scope = f.Services.CreateScope(); action(scope.ServiceProvider.GetRequiredService<KasaDbContext>()); }
    internal static Task<List<BenzerKayitDto>> Bul(HttpClient c, BenzerKayitSorgu sorgu) => Post<List<BenzerKayitDto>>(c, "/api/islemler/benzerlik", sorgu);
    private static async Task<int> PostCreated(HttpClient c, IslemYazDto dto)
    {
        var r = await c.PostAsJsonAsync("/api/islemler", dto);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }
    private static async Task<T> Post<T>(HttpClient c, string path, object body)
    {
        var r = await c.PostAsJsonAsync(path, body);
        Assert.True(r.IsSuccessStatusCode, $"{path} {r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }
}
