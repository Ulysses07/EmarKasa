using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kasa.Api.Tests;

/// <summary>
/// Takipsiz (geçişi yapılmamış) kart ve kredilerin hatırlatmaları (gap-tarihsel-spec-ve-emekli-web-7). 2.0 öncesi masaüstü
/// hatırlatıcısının kaldırılmasıyla geçişi yapılmamış kartlar hiçbir kanaldan hatırlatma almıyordu. Sunucunun bildirim olayları
/// artık eski kartlar için de eski modelin ekstre hesabından (GET /api/kredikartlari EkstreBorc) kesim ve son ödeme olayı üretir;
/// kapı 07-15 spec'indeki gibi ekstre borcu &gt; 0 ve kesimden sonra ödeme yok. Eski kredilerin taksitleri eski plandan hatırlatılır.
/// Takipli kayıtların olayları ve takip özeti değişmez; ana sayfa takipte olmayan kayıtları kalıcı uyarı için listeler.
/// Bugün 25.09.2026; eski kartın kesimi 18, son ödemesi 28 (bugün son ödemeye 3 gün var).
/// </summary>
public class TakipsizHatirlatmaTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 25);
    private static readonly DateOnly Baslangic = new(2026, 1, 1);
    private const string Ek = " (eski model; geçiş yapılmadı)";

    private static async Task<HttpClient> Editor(KasaWebFactory f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Baslangic, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
        return c;
    }
    private static async Task<T> Post<T>(HttpClient c, string path, object body)
    {
        var r = await c.PostAsJsonAsync(path, body);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }
    private static T Veri<T>(KasaWebFactory f, Func<KasaDbContext, T> is_)
    {
        using var scope = f.Services.CreateScope();
        return is_(scope.ServiceProvider.GetRequiredService<KasaDbContext>());
    }
    /// <summary>Eski (takipsiz) kart: açılış borcu, kart harcamaları ve eski kart ödemeleri doğrudan veritabanına yazılır
    /// (eski kayıtlar API'den oluşturulamaz).</summary>
    private static int EskiKart(KasaWebFactory f, string ad, decimal borc, (DateOnly Tarih, decimal Tutar)[] harcamalar, (DateOnly Tarih, decimal Tutar)[] odemeler,
        int kesimGunu = 18, int sonOdemeGunu = 28) => Veri(f, db =>
    {
        var kart = new KrediKartiEntity { Ad = ad, KesimTarihi = new(2026, 1, kesimGunu), SonOdemeTarihi = new(2026, 1, sonOdemeGunu), Limit = 50_000m, Borc = borc };
        db.KrediKartlari.Add(kart); db.SaveChanges();
        foreach (var (tarih, tutar) in harcamalar)
            db.Islemler.Add(new() { Tarih = tarih, Cari = ad + " harcaması", TutarTl = tutar, KanalId = 1, Kanal = "MEZAT", Tip = GiderTipi.KrediKarti, KrediKartiId = kart.Id });
        foreach (var (tarih, tutar) in odemeler) db.KartOdemeler.Add(new() { KrediKartiId = kart.Id, Tarih = tarih, Tutar = tutar, Not = "Eski ödeme" });
        db.SaveChanges();
        return kart.Id;
    });
    private static int EskiKredi(KasaWebFactory f, string ad, DateOnly cekim, int taksitSayisi, int odemeGunu, bool gerceklesmeTakibi = false) => Veri(f, db =>
    {
        var kredi = new KrediEntity { Ad = ad, CekilenTutar = 6000m, CekimTarihi = cekim, TaksitSayisi = taksitSayisi, AylikOdeme = 1000m, OdemeGunu = odemeGunu, Kanal = "MEZAT", KanalId = 1, GerceklesmeTakibi = gerceklesmeTakibi };
        db.Krediler.Add(kredi); db.SaveChanges();
        return kredi.Id;
    });
    private static IReadOnlyList<TakipOlayDto> Olaylar(KasaWebFactory f) => Veri(f, db => FinansTakipServisi.GetNotificationEvents(db, Bugun));
    private static (IReadOnlyList<TakipOlayDto> Olaylar, List<BildirimKaynakHatasi> Hatalar) IsciOlaylari(KasaWebFactory f) => Veri(f, db =>
    {
        var hatalar = new List<BildirimKaynakHatasi>();
        return (new FinansBildirimKaynaklari().Oku(db, Bugun, hatalar), hatalar);
    });

    [Fact]
    public async Task Takipsiz_kart_borclu_ve_donemde_odemesizse_eski_ekstre_borcuyla_kesim_ve_son_odeme_olayi_uretir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        // Kesim 18 Eylül: 10 Eylül harcaması ekstreye girer, 20 Eylül harcaması girmez. Ekstre borcu 1.000 + 500 = 1.500.
        var id = EskiKart(f, "Bonus", 1000m, [(new(2026, 9, 10), 500m), (new(2026, 9, 20), 200m)], []);

        // Eski modelin mevcut hesabı yeniden kullanılır: olay tutarı kart listesinin EkstreBorc'u ile aynıdır.
        var liste = await c.GetFromJsonAsync<JsonElement>("/api/kredikartlari");
        Assert.Equal(1500m, liste.EnumerateArray().Single(k => k.GetProperty("id").GetInt32() == id).GetProperty("ekstreBorc").GetDecimal());

        var olaylar = Olaylar(f).Where(e => e.KaynakId == id && e.Kaynak == "Kart").ToList();
        Assert.Equal(
            [new TakipOlayDto("Kart", id, 0, "Bonus" + Ek, new(2026, 9, 18), 1500m, "Kesim", false) { EskiModel = true },
             new TakipOlayDto("Kart", id, 0, "Bonus" + Ek, new(2026, 9, 28), 1500m, "SonOdeme", false) { EskiModel = true }], olaylar);
        // Bildirim işçisinin yalıtılmış kaynağı aynı olayları üretir.
        var (isci, hatalar) = IsciOlaylari(f);
        Assert.Empty(hatalar);
        Assert.Equal(Olaylar(f), isci);

        // Son ödemeye 3 gün kala ve son ödeme günü bildirim; metin kartın takipte olmadığını ve eski kasa kuralını söyler.
        var ucGun = Assert.Single(BildirimTakvimi.Olustur(olaylar, Bugun));
        Assert.Equal(("Ödemeye 3 gün kaldı", "SonOdeme", $"/#cards/{id}"), (ucGun.Baslik, ucGun.Tur, ucGun.Hedef));
        Assert.StartsWith("Bonus (eski model; geçiş yapılmadı): eski kayıtlardan hesaplanan ekstre borcu 1.500,00 TL. Son gün 28.09.2026.", ucGun.Mesaj);
        Assert.DoesNotContain("ödeme kaydettiğinde", ucGun.Mesaj);
        Assert.Equal("Bugün ödeme günü", Assert.Single(BildirimTakvimi.Olustur(olaylar, new(2026, 9, 28))).Baslik);
        var kesim = Assert.Single(BildirimTakvimi.Olustur(olaylar, new(2026, 9, 18)));
        Assert.Equal("Bugün hesap kesim günü", kesim.Baslik);
        Assert.Contains("Kartlar ekranından geçişi yapın", kesim.Mesaj);

        // Bildirim servisi turu (saat 12:00, varsayılan bildirim saati 09:00) kalıcı "3 gün kaldı" kaydını yazar.
        using (var scope = f.Services.CreateScope())
        {
            var servis = ActivatorUtilities.CreateInstance<BildirimServisi>(scope.ServiceProvider, NullLogger<BildirimServisi>.Instance);
            await servis.Yenile();
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var satir = Assert.Single(db.Set<BildirimEntity>().AsNoTracking().Where(b => b.KaynakId == id && b.Tur == "SonOdeme" && !b.Iptal).ToList());
            Assert.Equal(("Ödemeye 3 gün kaldı", $"/#cards/{id}"), (satir.Baslik, satir.Hedef));
        }
    }

    [Fact]
    public async Task Takipsiz_kartta_kesimden_sonra_odeme_varsa_ya_da_ekstre_borcu_yoksa_olay_uretilmez()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        // Kesimden (18 Eylül) sonra girilmiş kısmi ödeme dönemi "ele alınmış" sayar: kalan borç olsa da susar.
        var odendi = EskiKart(f, "Ödendi", 800m, [], [(new(2026, 9, 21), 100m)]);
        // Yalnız kesimden sonraki harcama: ekstre borcu 0.
        var borcsuz = EskiKart(f, "Borçsuz", 0m, [(new(2026, 9, 20), 300m)], []);
        // Kesimden önce kapatılmış borç: ekstre borcu 0 (sonraki kesimden önce hatırlatma yok).
        var kapali = EskiKart(f, "Kapalı", 400m, [], [(new(2026, 9, 5), 400m)]);
        // Kesimden önceki kısmi ödeme kapıyı açmaz: kalan ekstre borcuyla hatırlatılır.
        var kismi = EskiKart(f, "Kısmi", 400m, [], [(new(2026, 9, 5), 150m)]);

        var olaylar = Olaylar(f);
        Assert.DoesNotContain(olaylar, e => e.KaynakId == odendi || e.KaynakId == borcsuz || e.KaynakId == kapali);
        Assert.Equal([("Kesim", 250m), ("SonOdeme", 250m)], olaylar.Where(e => e.KaynakId == kismi).Select(e => (e.Tur, e.Tutar)));
        var (isci, hatalar) = IsciOlaylari(f);
        Assert.Empty(hatalar); Assert.Equal(olaylar, isci);
    }

    [Fact]
    public async Task Kesim_ve_son_odeme_ayni_gunse_onceki_ekstrenin_son_odeme_gunu_de_hatirlatilir()
    {
        // Kesim ve son ödeme 25: bugün hem Eylül ekstresi kesilir hem Ağustos ekstresinin son günüdür (eski hatırlatıcıdaki gibi).
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        var id = EskiKart(f, "Aynı gün", 300m, [(new(2026, 9, 1), 100m)], [], kesimGunu: 25, sonOdemeGunu: 25);
        var olaylar = Olaylar(f).Where(e => e.KaynakId == id).Select(e => (e.Tur, e.Tarih, e.Tutar)).ToList();
        // Ağustos ekstresi 1 Eylül harcamasını içermez (300); Eylül ekstresi içerir (400).
        Assert.Equal([("Kesim", new DateOnly(2026, 8, 25), 300m), ("SonOdeme", new DateOnly(2026, 9, 25), 300m),
            ("Kesim", new DateOnly(2026, 9, 25), 400m), ("SonOdeme", new DateOnly(2026, 10, 25), 400m)], olaylar);
        Assert.Equal(["Bugün ödeme günü", "Bugün hesap kesim günü"], BildirimTakvimi.Olustur(Olaylar(f), Bugun).Select(b => b.Baslik));
    }

    [Fact]
    public async Task Takipli_kartin_olaylari_ve_takip_ozeti_takipsiz_kart_ve_kredi_eklenince_degismez()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        var kart = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Takipli", 10_000m, 18, 28, Baslangic, 0, []));
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, new(2026, 9, 10), "Malzeme", 250m, 1, null, [new(1, 250m)]));
        var kredi = await Post<KrediTakipDto>(c, "/api/takip/krediler", new KrediTakipYaz(Guid.NewGuid(), "Takipli kredi", 300m, Bugun.AddDays(-10), Bugun.AddDays(3), 3, 100m, [1]));
        var once = Olaylar(f);
        var ozetOnce = await c.GetStringAsync("/api/takip/ozet?gun=366");
        Assert.Contains(once, e => e.KaynakId == kart.Id && e.Tur == "SonOdeme");
        Assert.Contains(once, e => e.KaynakId == kredi.Id && e.Tur == "Taksit");

        var eskiKart = EskiKart(f, "Bonus", 1000m, [], []);
        var eskiKredi = EskiKredi(f, "Eski kredi", new(2026, 8, 1), 6, 28);
        var sonra = Olaylar(f);

        // Takipli olaylar aynı sırayla ve aynı içerikle önde; eski model olayları sonda ve işaretli.
        Assert.Equal(once, sonra.Take(once.Count));
        Assert.All(once, e => Assert.False(e.EskiModel));
        Assert.All(sonra.Skip(once.Count), e => { Assert.True(e.EskiModel); Assert.EndsWith(Ek, e.Ad); });
        Assert.Contains(sonra, e => e.Kaynak == "Kart" && e.KaynakId == eskiKart);
        Assert.Contains(sonra, e => e.Kaynak == "Kredi" && e.KaynakId == eskiKredi);
        // Takip özeti (ana sayfa "yaklaşan ödemeler") olayları değişmez; eski model olayları yalnız bildirim hattına girer.
        var ozetSonra = await c.GetStringAsync("/api/takip/ozet?gun=366");
        Assert.Equal(JsonDocument.Parse(ozetOnce).RootElement.GetProperty("olaylar").GetRawText(), JsonDocument.Parse(ozetSonra).RootElement.GetProperty("olaylar").GetRawText());
        Assert.DoesNotContain("eskiModel", ozetSonra, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Takipsiz_eski_kredinin_taksitleri_eski_planindan_hatirlatilir_gerceklesme_takiplide_otomatik_degil()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        // Çekim 1 Ağustos, ödeme günü 28: taksitler 28 Ağustos, 28 Eylül, ... (bugünden 3 gün sonra 2. taksit).
        var otomatik = EskiKredi(f, "Taşıt", new(2026, 8, 1), 6, 28);
        var gercek = EskiKredi(f, "Konut", new(2026, 8, 1), 6, 28, gerceklesmeTakibi: true);

        var olaylar = Olaylar(f);
        var taksit = olaylar.Single(e => e.KaynakId == otomatik && e.Tarih == new DateOnly(2026, 9, 28));
        Assert.Equal(new TakipOlayDto("Kredi", otomatik, 0, "Taşıt / 2. taksit" + Ek, new(2026, 9, 28), 1000m, "Taksit", true) { EskiModel = true }, taksit);
        Assert.Equal(6, olaylar.Count(e => e.KaynakId == otomatik));
        Assert.False(olaylar.Single(e => e.KaynakId == gercek && e.Tarih == new DateOnly(2026, 9, 28)).OtomatikKasa);

        var bildirimler = BildirimTakvimi.Olustur(olaylar, Bugun);
        Assert.Contains("Taksit tarihinde ilgili kanal kasalarından otomatik düşecek.", bildirimler.Single(b => b.KaynakId == otomatik).Mesaj);
        var gercekBildirim = bildirimler.Single(b => b.KaynakId == gercek);
        Assert.Equal(("Ödemeye 3 gün kaldı", $"/#loans/{gercek}"), (gercekBildirim.Baslik, gercekBildirim.Hedef));
        Assert.Contains("kasaya otomatik işlenmez", gercekBildirim.Mesaj);
        var (isci, hatalar) = IsciOlaylari(f);
        Assert.Empty(hatalar); Assert.Equal(olaylar, isci);
    }

    [Fact]
    public async Task Plani_gecersiz_eski_kredi_bildirim_hattinda_yalitilir_diger_hatirlatmalar_surer()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        var kart = EskiKart(f, "Bonus", 1000m, [], []);
        var bozuk = EskiKredi(f, "Bozuk plan", new(2026, 8, 1), 6, 0);
        var (isci, hatalar) = IsciOlaylari(f);
        Assert.Equal(("Kredi", bozuk), (Assert.Single(hatalar).Kaynak, hatalar[0].KaynakId));
        Assert.Contains(isci, e => e.KaynakId == kart && e.Tur == "SonOdeme");
    }

    [Fact]
    public async Task Ana_sayfa_takipte_olmayan_kart_ve_suren_eski_krediyi_kalici_uyari_icin_listeler()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Takipli", 10_000m, 18, 28, Baslangic, 0, []));
        // Takipsiz kayıt yokken alan yazılmaz (yanıt biçimi aynen korunur).
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/rapor/ana-sayfa?gun=30")).TryGetProperty("takipsizKayitlar", out _));

        var borcsuz = EskiKart(f, "Borçsuz eski kart", 0m, [], []);
        var bonus = EskiKart(f, "Bonus", 1000m, [], []);
        var suren = EskiKredi(f, "Süren kredi", new(2026, 8, 1), 6, 28);
        EskiKredi(f, "Biten kredi", new(2025, 1, 1), 3, 10);
        var bozuk = EskiKredi(f, "Bozuk plan", new(2026, 8, 1), 6, 0);

        var json = await c.GetFromJsonAsync<JsonElement>("/api/rapor/ana-sayfa?gun=30");
        var liste = json.GetProperty("takipsizKayitlar").EnumerateArray()
            .Select(k => (k.GetProperty("kaynak").GetString(), k.GetProperty("id").GetInt32(), k.GetProperty("ad").GetString())).ToList();
        // Kartlar geçiş yapılana kadar listede kalır; kredi yalnız kalan taksidi varsa (geçersiz planın kalanı bilinmez: listede).
        Assert.Equal([("Kart", borcsuz, "Borçsuz eski kart"), ("Kart", bonus, "Bonus"), ("Kredi", suren, "Süren kredi"), ("Kredi", bozuk, "Bozuk plan")], liste);
    }
}
