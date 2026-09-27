using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Güvenlik ağı: zengin bir tohumun bütün rapor/okuma uçlarındaki yanıtları, okuma yolu ve kart hesabı
/// değişikliklerinden önce üretilmiş 'altın' JSON ile birebir karşılaştırılır. Tohum sabit saatle yalnız
/// sabit tarihler kullanır; kimlikler taze veritabanında belirlenimcidir. Altın dosyayı yeniden üretmek
/// yalnız bilinçli bir rapor değişikliğinde yapılır: KASA_ALTIN_YAZ=1 ile çalıştırılır ve fark incelenir.
/// </summary>
public class AltinRaporTests
{
    [Fact]
    public async Task Rapor_ve_okuma_uclari_altin_ciktiyla_birebir_esit()
    {
        await using var f = KasaWebFactory.Sabit(AltinTohum.Bugun);
        using var c = await f.EditorClientAsync();
        var tohum = await AltinTohum.Kur(f, c);
        var gercek = await AltinTohum.Yanitlar(c, tohum);

        var yol = AltinDosyasi();
        if (Environment.GetEnvironmentVariable("KASA_ALTIN_YAZ") == "1" || !File.Exists(yol))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(yol)!);
            File.WriteAllText(yol, gercek.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Assert.Fail($"Altın dosya yazıldı: {yol}. İçeriği inceleyip testi yeniden çalıştırın.");
        }
        var beklenen = JsonNode.Parse(File.ReadAllText(yol))!.AsObject();
        Assert.Equal(beklenen.Select(p => p.Key).Order(StringComparer.Ordinal), gercek.Select(p => p.Key).Order(StringComparer.Ordinal));
        // Birebir: sayı belirteçleri de metin olarak karşılaştırılır (ör. 1190.5 ile 1190.50 farklı sayılır).
        foreach (var (uc, deger) in beklenen)
            Assert.True(deger!.ToJsonString() == gercek[uc]!.ToJsonString(), $"{uc} altın çıktıdan farklı:\nBEKLENEN {deger.ToJsonString()}\nGERÇEK   {gercek[uc]!.ToJsonString()}");

        // Okumalar veri değiştirmez: aynı uçlar ikinci kez aynı yanıtı verir.
        var ikinci = await AltinTohum.Yanitlar(c, tohum);
        foreach (var (uc, deger) in gercek)
            Assert.True(deger!.ToJsonString() == ikinci[uc]!.ToJsonString(), $"{uc} ikinci okumada değişti.");
    }

    private static string AltinDosyasi([CallerFilePath] string kaynak = "") =>
        Path.Combine(Path.GetDirectoryName(kaynak)!, "Altin", "rapor-altin.json");
}

/// <summary>Altın rapor tohumu: aktif/pasif kanallar, bütün gider tipleri, takipli/eski/geçişli kart ve kredi,
/// iade, iptal edilen harcama ve ödeme, avans, alışlar (onaylı, kartlı, onaysız), aylık gider, ekstre aktarımı,
/// eski hesap geliri, kasa eşiği ve ay kilidi. Bütün tarihler sabittir; bugün <see cref="Bugun"/>.</summary>
internal static class AltinTohum
{
    public static readonly DateOnly Bugun = new(2026, 9, 25);
    public static readonly DateOnly Baslangic = new(2026, 1, 1);

    public sealed record Kimlikler(int TakipKartA, int TakipKartB, int EskiKart, int GecisKarti, int TakipKredi, int EskiKredi, int GecisKredisi);

    private static readonly JsonSerializerOptions Json = Secenekler();
    private static JsonSerializerOptions Secenekler()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    public static async Task<T> Post<T>(HttpClient c, string yol, object govde)
    {
        var r = await c.PostAsJsonAsync(yol, govde);
        Assert.True(r.IsSuccessStatusCode, $"POST {yol} {r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }
    public static async Task<T> Put<T>(HttpClient c, string yol, object govde)
    {
        var r = await c.PutAsJsonAsync(yol, govde);
        Assert.True(r.IsSuccessStatusCode, $"PUT {yol} {r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }
    private static async Task Put(HttpClient c, string yol, object govde)
    {
        var r = await c.PutAsJsonAsync(yol, govde);
        Assert.True(r.IsSuccessStatusCode, $"PUT {yol} {r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }
    private static async Task Gider(HttpClient c, DateOnly tarih, string cari, decimal tutar, string kanal, GiderTipi tip, int? kart = null)
    {
        var r = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(tarih, cari, tutar, kanal, tip, null, kart), Json);
        Assert.True(r.IsSuccessStatusCode, $"POST /api/islemler {r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }

    public static async Task<Kimlikler> Kur(KasaWebFactory f, HttpClient c)
    {
        await Put(c, "/api/ayarlar", new { takipBaslangic = Baslangic, kasaAcilisDevri = 50_000m });
        await Put(c, "/api/kanallar/1", new KanalYazDto("MEZAT", true, 0, 12_000m));
        await Put(c, "/api/kanallar/2", new KanalYazDto("PERAKENDE", true, 1, 8_500.25m));
        await Put(c, "/api/kanallar/3", new KanalYazDto("TOPTAN", true, 2, -1_250m));
        await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("ESKI", false, 3, 1_000m));

        // Doğrudan veritabanına: eski (takipsiz) kart ve krediler, geçirilecek kart/kredi, eski hesap geliri.
        int eskiKart, gecisKarti, eskiKredi, gecisKredisi;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var k1 = new KrediKartiEntity { Ad = "Eski kart", KesimTarihi = new(2026, 1, 15), SonOdemeTarihi = new(2026, 1, 25), Limit = 20_000m, Borc = 1_200m };
            var k2 = new KrediKartiEntity { Ad = "Geçiş kartı", KesimTarihi = new(2026, 1, 20), SonOdemeTarihi = new(2026, 1, 30), Limit = 15_000m, Borc = 0m };
            db.KrediKartlari.AddRange(k1, k2);
            var kr1 = new KrediEntity { Ad = "Eski kredi", CekilenTutar = 10_000m, CekimTarihi = new(2026, 2, 1), TaksitSayisi = 12, AylikOdeme = 900m, OdemeGunu = 5, Kanal = "MEZAT", KanalId = 1 };
            var kr2 = new KrediEntity { Ad = "Geçiş kredisi", CekilenTutar = 6_000m, CekimTarihi = new(2026, 3, 10), TaksitSayisi = 10, AylikOdeme = 650m, OdemeGunu = 20, Kanal = Kanallar.Ortak };
            db.Krediler.AddRange(kr1, kr2);
            var hesap = new HesapEntity { Ad = "Eski hesap", Tur = "Kasa", AcilisTarihi = Baslangic };
            db.Hesaplar.Add(hesap);
            db.SaveChanges();
            db.HesapHareketler.Add(new() { HesapId = hesap.Id, KanalId = 2, Tarih = new(2026, 4, 14), Tutar = 450m, Aciklama = "Eski ek gelir" });
            db.SaveChanges();
            (eskiKart, gecisKarti, eskiKredi, gecisKredisi) = (k1.Id, k2.Id, kr1.Id, kr2.Id);
        }

        // Gelirler (takip başlangıcı, ay başı ve pazartesi dönemleri; pasif kanal dahil).
        foreach (var (donem, kanal, tutar) in new[] { (Baslangic, "MEZAT", 10_000m), (new DateOnly(2026, 3, 2), "PERAKENDE", 8_000m),
            (new DateOnly(2026, 6, 1), "TOPTAN", 5_000m), (new DateOnly(2026, 9, 1), "MEZAT", 12_000m), (new DateOnly(2026, 9, 21), "PERAKENDE", 3_000m),
            (new DateOnly(2026, 2, 2), "ESKI", 500m) })
            await Put(c, "/api/gelenler", new GelenUpsertDto(donem, kanal, tutar));

        // Genel giderler: bütün tipler, Ortak, pasif kanal, eski kart ve ileri tarihli kayıt.
        await Gider(c, new(2026, 1, 5), "Cari ödeme", 1_500m, "MEZAT", GiderTipi.Cari);
        await Gider(c, new(2026, 2, 10), "SGK", 900m, Kanallar.Ortak, GiderTipi.SabitGider);
        await Gider(c, new(2026, 3, 15), "Nakliye", 700.55m, "PERAKENDE", GiderTipi.Cari);
        await Gider(c, new(2026, 4, 20), "Eski kart alışı", 400m, "MEZAT", GiderTipi.KrediKarti, eskiKart);
        await Gider(c, new(2026, 5, 31), "Eski kart ortak", 333.33m, Kanallar.Ortak, GiderTipi.KrediKarti, eskiKart);
        await Gider(c, new(2026, 7, 7), "Elektrik", 250m, "TOPTAN", GiderTipi.SabitGider);
        await Gider(c, new(2026, 8, 18), "Pasif kanal gideri", 120m, "ESKI", GiderTipi.Cari);
        await Gider(c, new(2026, 8, 10), "Geçiş öncesi kart", 800m, "PERAKENDE", GiderTipi.KrediKarti, gecisKarti);
        await Gider(c, new(2026, 10, 5), "İleri tarihli ödeme", 999m, "MEZAT", GiderTipi.Cari);
        await Post<KartOdemeEntity>(c, "/api/kartodemeler", new KartOdemeYazDto(eskiKart, new(2026, 6, 25), 500m, "Eski kart ödemesi"));

        // Takipli kart A: açılış borcu, taksitli harcamalar, iade, iptal edilen harcama, ödemeler, iptal edilen ödeme.
        var a = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Takip kart A", 30_000m, 12, 2, new(2026, 2, 1), 1_000m, [new(1, 600m), new(2, 400m)]));
        a = await Harcama(c, a, new(2026, 2, 15), "Malzeme", 1_200m, 3, [new(1, 700m), new(3, 500m)]);
        a = await Harcama(c, a, new(2026, 3, 20), "Yakıt", 450.50m, 1, [new(2, 450.50m)]);
        a = await Harcama(c, a, new(2026, 4, 2), "Ekipman", 3_000m, 6, [new(1, 1_000m), new(2, 1_000m), new(3, 1_000m)]);
        var ekipman = a.Harcamalar.Single(h => h.Aciklama == "Ekipman").Id;
        a = await Harcama(c, a, new(2026, 4, 10), "Ekipman iadesi", -300m, 1, [], ekipman);
        a = await Harcama(c, a, new(2026, 4, 11), "Hatalı harcama", 99m, 1, [new(1, 99m)]);
        var hatali = a.Harcamalar.Single(h => h.Aciklama == "Hatalı harcama").Id;
        a = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{a.Id}/harcamalar/{hatali}/iptal", new TakipIptalYaz(Guid.NewGuid(), a.Surum, "Yanlış kart"));
        a = await Odeme(c, a, new(2026, 3, 5), 800m);
        a = await Odeme(c, a, new(2026, 5, 3), 1_500m, "Mayıs ödemesi");
        a = await Odeme(c, a, new(2026, 6, 2), 700m);
        var iptalOdeme = a.Odemeler.OrderBy(o => o.Id).Last().Id;
        a = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{a.Id}/odemeler/{iptalOdeme}/iptal", new TakipIptalYaz(Guid.NewGuid(), a.Surum, "Mükerrer"));
        a = await Odeme(c, a, new(2026, 7, 2), 1_100m);
        a = await Odeme(c, a, new(2026, 9, 2), 600m);
        await Gider(c, new(2026, 9, 24), "Kartla ortak gider", 90m, Kanallar.Ortak, GiderTipi.KrediKarti, a.Id);

        // Takipli kart B: avans, sonradan harcama, genel giderden ve onaylı alıştan gelen kart harcamaları.
        var b = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Takip kart B", 10_000m, 28, 8, new(2026, 3, 1), 0m, []));
        b = await Odeme(c, b, new(2026, 5, 10), 500m, "Avans");
        b = await Harcama(c, b, new(2026, 6, 15), "Sonradan harcama", 300m, 1, [new(3, 300m)]);
        await Gider(c, new(2026, 7, 12), "Kartla ortak", 240m, Kanallar.Ortak, GiderTipi.KrediKarti, b.Id);

        // Alışlar: onaylı nakit ödemeli, onaylı kartlı ödemeli (takipli kart B), onaysız (dağılım bekliyor).
        var a1 = await Alis(c, new(2026, 3, 1), "Tedarikçi A", 5_000m, [new(1, 3_000m), new(2, 2_000m)], onayla: true);
        a1 = await AlisOdeme(c, a1, new(2026, 3, 3), 2_000m);
        a1 = await AlisOdeme(c, a1, new(2026, 3, 25), 1_500m);
        var a2 = await Alis(c, new(2026, 8, 1), "Tedarikçi B", 2_400m, [new(2, 1_400m), new(3, 1_000m)], onayla: true);
        await AlisOdeme(c, a2, new(2026, 8, 5), 1_000m, b.Id);
        var a3 = await Alis(c, new(2026, 4, 5), "Tedarikçi C", 1_000m, [new(1, 1_000m)], onayla: false);
        await AlisOdeme(c, a3, new(2026, 4, 8), 700m);
        b = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{b.Id}", Json))!;
        b = await Odeme(c, b, new(2026, 9, 8), 400m);

        // Eski kartın yeni takibe geçişi (bugün) ve geçiş sonrası harcama.
        var gecis = new KartGecisYaz(Guid.NewGuid(), 0, Bugun, 800m, 800m, [new(2, 800m)], "Banka borcu doğrulandı", false);
        var onizleme = await Post<TakipGecisDto>(c, $"/api/takip/kartlar/{gecisKarti}/gecis-onizleme", gecis);
        Assert.True(onizleme.KabulEdilebilir, string.Join(" ", onizleme.Aciklamalar));
        var g = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{gecisKarti}/gecis", gecis with { Onay = true });
        await Harcama(c, g, Bugun, "Geçiş sonrası", 150m, 1, [new(2, 150m)]);

        // Krediler: yeni takipli (erken kapama dahil), mevcut kredi, eski krediden geçiş.
        var kredi = await Post<KrediTakipDto>(c, "/api/takip/krediler", new KrediTakipYaz(Guid.NewGuid(), "Takip kredisi", 20_000m, new(2026, 2, 10), new(2026, 3, 10), 12, 1_850m, [1, 2]));
        await Post<KrediTakipDto>(c, $"/api/takip/krediler/{kredi.Id}/erken-kapat", new KrediKapatYaz(Guid.NewGuid(), kredi.Surum, new(2026, 12, 10), 12_000m, "Erken kapama teklifi"));
        await Post<KrediTakipDto>(c, "/api/takip/krediler", new KrediTakipYaz(Guid.NewGuid(), "Mevcut banka kredisi", 8_000m, new(2025, 12, 1), new(2026, 10, 1), 6, 700m, [3], true));
        await Post<TakipGecisDto>(c, $"/api/takip/krediler/{gecisKredisi}/gecis-onizleme", new KrediGecisYaz(Guid.NewGuid(), 0, Bugun.AddDays(1), [1, 3], "Ortak paylaşım", false));
        await Post<KrediTakipDto>(c, $"/api/takip/krediler/{gecisKredisi}/gecis", new KrediGecisYaz(Guid.NewGuid(), 0, Bugun.AddDays(1), [1, 3], "Ortak paylaşım", true));

        // Aylık gider (eşit dağılım) ve ödemesi.
        var sablon = await Post<AylikGiderSablonDto>(c, "/api/aylik-giderler/sablonlar", new AylikGiderSablonYaz(Guid.NewGuid(), 0, "Kira", "Kira", 4_000m, 15, "Esit", [new(1, 0), new(2, 0), new(3, 0)], new(2026, 9, 1)));
        await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{sablon.Id}/ode", new AylikGiderOdemeYaz(Guid.NewGuid(), sablon.Surum, 2026, 9, new(2026, 9, 15), "Eylül kirası"));

        // Banka ekstresi aktarımı: gelir (özel dağılım), gider (eşit dağılım), takipli kart A ödemesi.
        int belge;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var satirlar = new[] { new EkstreOkunanSatir(1, 1, "Kaynak 1", new(2026, 9, 15), "Havale geliri", 2_500m, "Giris", "Gelir", "Hareket", "TRY", []),
                new EkstreOkunanSatir(2, 1, "Kaynak 2", new(2026, 9, 16), "Banka masrafı", 350m, "Cikis", "Gider", "Hareket", "TRY", []),
                new EkstreOkunanSatir(3, 1, "Kaynak 3", new(2026, 9, 18), "Kart A ödemesi", 250m, "Cikis", "KartOdemesi", "Hareket", "TRY", []) };
            var d = new EkstreBelgeEntity { Kaynak = "Banka", Banka = "Akbank", HesapAdi = "İş hesabı", DosyaAdi = "altin.pdf", DosyaOzeti = "altin-belge",
                Dosya = "%PDF-altin"u8.ToArray(), Yuklendi = f.Saat!.GetUtcNow().ToUnixTimeMilliseconds(), SatirlarJson = JsonSerializer.Serialize(satirlar) };
            db.EkstreBelgeler.Add(d); db.SaveChanges(); belge = d.Id;
        }
        var doc = (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{belge}", Json))!;
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), doc.Surum, [
            new(1, new(2026, 9, 15), "Havale geliri", 2_500m, "Gelir", "Ozel", [new(1, 1_500m), new(3, 1_000m)]),
            new(2, new(2026, 9, 16), "Banka masrafı", 350m, "Gider", "Esit", [new(1, 0), new(2, 0)]),
            new(3, new(2026, 9, 18), "Kart A ödemesi", 250m, "KartOdemesi", "Otomatik", [], a.Id)]);
        var ekstreOnizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{belge}/onizleme", istek);
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{belge}/kaydet", istek with { OnizlemeOzeti = ekstreOnizleme.OnizlemeOzeti, TekrarOnay = true });

        // Kasa eşiği ve ay kilidi (Haziran kapatılır).
        await Put(c, "/api/kasa-esikleri/2", new KasaEsikYaz(0, 1_000_000m, true));
        var kilit = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi", Json))!;
        await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), kilit.Surum, 2026, 6, "Haziran kapandı"));

        return new(a.Id, b.Id, eskiKart, gecisKarti, kredi.Id, eskiKredi, gecisKredisi);
    }

    private static Task<KartTakipDto> Harcama(HttpClient c, KartTakipDto kart, DateOnly tarih, string aciklama, decimal tutar, int taksit, IReadOnlyList<KanalPayYaz> paylar, int? kaynak = null) =>
        Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, tarih, aciklama, tutar, taksit, null, paylar, kaynak));
    private static Task<KartTakipDto> Odeme(HttpClient c, KartTakipDto kart, DateOnly tarih, decimal tutar, string? not = null) =>
        Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, tarih, tutar, null, not));
    private static async Task<AlisDto> Alis(HttpClient c, DateOnly tarih, string tedarikci, decimal tutar, IReadOnlyList<AlisDagilimYaz> paylar, bool onayla)
    {
        var alis = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, tarih, tedarikci, null, [new("Mal", tutar, paylar)]));
        if (!onayla) return alis;
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/gonder", new AlisDurumYaz(alis.Surum));
        return await Post<AlisDto>(c, $"/api/alis/{alis.Id}/onayla", new AlisDurumYaz(alis.Surum, "Uygun"));
    }
    private static Task<AlisDto> AlisOdeme(HttpClient c, AlisDto alis, DateOnly tarih, decimal tutar, int? kart = null) =>
        Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), tarih, tutar, kart));

    /// <summary>Okuma uçlarının ham JSON yanıtları (uç → yanıt). Tarih/ay listesi sabittir.</summary>
    public static async Task<JsonObject> Yanitlar(HttpClient c, Kimlikler k)
    {
        var uclar = new List<string> { "/api/rapor/panel", "/api/rapor/haftalik", "/api/donemler",
            "/api/takip/kartlar", $"/api/takip/kartlar/{k.TakipKartA}", $"/api/takip/kartlar/{k.GecisKarti}", "/api/takip/krediler",
            $"/api/takip/krediler/{k.TakipKredi}", "/api/takip/ozet", "/api/takip/ozet?gun=7", "/api/takip/ozet?gun=366",
            "/api/kasa-esikleri", "/api/islemler", "/api/alis", "/api/ay-kilidi", "/api/kredikartlari" };
        for (var ay = new DateOnly(2026, 1, 1); ay <= new DateOnly(2027, 3, 1); ay = ay.AddMonths(1))
            uclar.Add($"/api/rapor/aylik?yil={ay.Year}&ay={ay.Month}");
        var sonuc = new JsonObject();
        foreach (var uc in uclar)
        {
            var r = await c.GetAsync(uc);
            var govde = await r.Content.ReadAsStringAsync();
            Assert.True(r.IsSuccessStatusCode, $"GET {uc} {r.StatusCode}: {govde}");
            sonuc[uc] = JsonNode.Parse(govde);
        }
        return sonuc;
    }
}
