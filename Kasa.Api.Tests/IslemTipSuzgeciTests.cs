using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using static Kasa.Api.Tests.PaketB;

namespace Kasa.Api.Tests;

/// <summary>
/// 22 · Rapordan İşlemler'e iniş: işlem listesi ve Excel'e aktar gider tipine göre sunucuda süzülür.
/// Tip motorun etkin tipidir (karta bağlı işlem K.K sayılır); süzülen listenin toplamı rapordaki rakama eşittir.
/// </summary>
public class IslemTipSuzgeciTests : IClassFixture<PaketBFactory>
{
    private readonly PaketBFactory _f;
    public IslemTipSuzgeciTests(PaketBFactory f) => _f = f;

    private async Task<(HttpClient C, int Kart)> HazirlaAsync()
    {
        _f.Temizle();
        _f.Saat.Ayarla(new DateOnly(2026, 9, 24));
        var c = await _f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 1));
        var kart = (await (await c.PostAsJsonAsync("/api/kredikartlari", new { ad = "Kart T", kesimTarihi = "2026-08-05", sonOdemeTarihi = "2026-08-15", limit = 100_000, borc = 0 }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        if (!(await Oku(c, "/api/giderkalemleri")).EnumerateArray().Any(k => k.GetProperty("ad").GetString() == "Kira"))
            (await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = "Kira", aktif = true })).EnsureSuccessStatusCode();

        // Ağustos, MEZAT: Cari 100, sabit gider 200, kartsız K.K 300, karta bağlı 400.
        await IslemEkle(c, new DateOnly(2026, 8, 3), 100m);
        await IslemEkle(c, new DateOnly(2026, 8, 4), 200m, "MEZAT", "SabitGider", "Kira");
        await IslemEkle(c, new DateOnly(2026, 8, 5), 300m, "MEZAT", "KrediKarti", "Market");
        await IslemEkle(c, new DateOnly(2026, 8, 6), 400m, "MEZAT", "Cari", "Market", kart);
        // Eski kayıt: karta bağlı ama kayıtlı tipi Cari/sabit gider (API bugün K.K'ya çevirir; DB'de kalmış olabilir).
        _f.Db(db =>
        {
            db.Islemler.Add(new IslemEntity { Tarih = new DateOnly(2026, 8, 7), Cari = "X", TutarTl = 50m, Kanal = "MEZAT", Tip = GiderTipi.Cari, KrediKartiId = kart });
            db.Islemler.Add(new IslemEntity { Tarih = new DateOnly(2026, 8, 8), Cari = "Kira", TutarTl = 60m, Kanal = "MEZAT", Tip = GiderTipi.SabitGider, KrediKartiId = kart });
            db.SaveChanges();
        });
        // Ortak: kart dışı 1.000 ve 250 (sabit), K.K 70. Eylül'de MEZAT Cari 5 (aralık dışı).
        await IslemEkle(c, new DateOnly(2026, 8, 9), 1_000m, "Ortak");
        await IslemEkle(c, new DateOnly(2026, 8, 10), 250m, "Ortak", "SabitGider", "Kira");
        await IslemEkle(c, new DateOnly(2026, 8, 11), 70m, "Ortak", "KrediKarti", "Market");
        await IslemEkle(c, new DateOnly(2026, 9, 2), 5m);
        return (c, kart);
    }

    /// <summary>Listenin tutarları (her kayıt ayrı tutarlı), sunucu sırasıyla.</summary>
    private static async Task<List<decimal>> Tutarlar(HttpClient c, string sorgu)
        => (await Oku(c, "/api/islemler?" + sorgu)).EnumerateArray().Select(i => i.GetProperty("tutarTl").GetDecimal()).ToList();

    [Fact]
    public async Task Tip_etkin_tipe_gore_suzer_ve_sayfa_toplami_suzulmus()
    {
        var (c, _) = await HazirlaAsync();
        const string ay = "baslangic=2026-08-01&bitis=2026-08-31&kanal=MEZAT";
        Assert.Equal(new[] { 100m }, await Tutarlar(c, ay + "&tip=Cari"));
        Assert.Equal(new[] { 200m }, await Tutarlar(c, ay + "&tip=SabitGider"));
        Assert.Equal(new[] { 300m, 400m, 50m, 60m }, await Tutarlar(c, ay + "&tip=KrediKarti"));
        Assert.Equal(new[] { 100m, 200m }, await Tutarlar(c, ay + "&tip=Nakit"));
        Assert.Equal(6, (await Tutarlar(c, ay)).Count);                // tip yok: hepsi

        // Sayfalı istekte toplam (X-Toplam-Kayit) süzülmüş listeye göredir.
        var r = await c.GetAsync("/api/islemler?" + ay + "&tip=KrediKarti&limit=2&offset=1");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("4", r.Headers.GetValues("X-Toplam-Kayit").Single());
        var sayfa = (await r.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Select(i => i.GetProperty("tutarTl").GetDecimal()).ToList();
        Assert.Equal(new[] { 400m, 50m }, sayfa);

        // Harf büyüklüğü önemsiz (gelişmiş aramayla aynı süzgeç).
        Assert.Equal(new[] { 100m, 200m }, await Tutarlar(c, ay + "&tip=nakit"));

        // Bilinmeyen tip: 400 (sessizce tüm liste dönmez). Hem liste hem CSV.
        var kotu = await c.GetAsync("/api/islemler?" + ay + "&tip=Havale");
        Assert.Equal(HttpStatusCode.BadRequest, kotu.StatusCode);
        Assert.Equal("Geçersiz gider tipi (Cari, SabitGider, KrediKarti ya da Nakit).", await HataMetni(kotu));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/disaaktar/islemler.csv?tip=1")).StatusCode);

        // İzleyici de süzebilir (okuma).
        var izleyici = await IzleyiciAsync(_f);
        Assert.Equal(new[] { 200m }, await Tutarlar(izleyici, ay + "&tip=SabitGider"));
    }

    [Fact]
    public async Task Suzulen_liste_rapordaki_rakamlara_esit()
    {
        var (c, _) = await HazirlaAsync();
        static decimal Toplam(JsonElement l) => l.EnumerateArray().Sum(i => i.GetProperty("tutarTl").GetDecimal());

        // Aylık: Eylül'ün "Kredi kartı (geçen ay)" rakamı = Ağustos'un etkin K.K işlemleri.
        var eylul = (await Oku(c, "/api/rapor/aylik?yil=2026&ay=9")).GetProperty("kanallar").EnumerateArray()
            .First(k => k.GetProperty("kanal").GetString() == "MEZAT");
        Assert.Equal(D(eylul, "krediKarti"), Toplam(await Oku(c, "/api/islemler?baslangic=2026-08-01&bitis=2026-08-31&kanal=MEZAT&tip=KrediKarti")));
        var agustos = (await Oku(c, "/api/rapor/aylik?yil=2026&ay=8")).GetProperty("kanallar").EnumerateArray()
            .First(k => k.GetProperty("kanal").GetString() == "MEZAT");
        Assert.Equal(D(agustos, "cariGiden"), Toplam(await Oku(c, "/api/islemler?baslangic=2026-08-01&bitis=2026-08-31&kanal=MEZAT&tip=Cari")));
        Assert.Equal(D(agustos, "sabitGider"), Toplam(await Oku(c, "/api/islemler?baslangic=2026-08-01&bitis=2026-08-31&kanal=MEZAT&tip=SabitGider")));

        // Kasa dökümü: Ortak gider adımı = Ortak kanalının kart dışı işlemleri (K.K 70 hariç).
        var dokum = await Oku(c, "/api/rapor/kasa-dokumu?baslangic=2026-08-01&bitis=2026-08-31");
        var ortak = dokum.GetProperty("adimlar").EnumerateArray().Where(a => a.GetProperty("tur").GetString() == "OrtakGider").Sum(a => D(a, "tutar"));
        Assert.Equal(-1_250m, ortak);
        Assert.Equal(-ortak, Toplam(await Oku(c, "/api/islemler?baslangic=2026-08-01&bitis=2026-08-31&kanal=Ortak&tip=Nakit")));
    }

    [Fact]
    public async Task Excel_aktarimi_tip_suzgecini_uygular()
    {
        var (c, _) = await HazirlaAsync();
        var r = await c.GetAsync("/api/disaaktar/islemler.csv?baslangic=2026-08-01&bitis=2026-08-31&kanal=MEZAT&tip=KrediKarti");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var csv = Encoding.UTF8.GetString(await r.Content.ReadAsByteArrayAsync());
        foreach (var t in new[] { "300,00", "400,00", "50,00", "60,00" }) Assert.Contains(t, csv);
        Assert.DoesNotContain("100,00", csv);
        Assert.DoesNotContain("200,00", csv);
        Assert.Contains("Toplam (4 işlem)", csv);                      // toplam satırı yalnız süzülenler
        Assert.Contains("810,00", csv);

        var hepsi = Encoding.UTF8.GetString(await (await c.GetAsync("/api/disaaktar/islemler.csv?baslangic=2026-08-01&bitis=2026-08-31&kanal=MEZAT")).Content.ReadAsByteArrayAsync());
        Assert.Contains("200,00", hepsi);
        Assert.Contains("1110,00", hepsi);
    }
}
