using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using static Kasa.Api.Tests.PaketD;

namespace Kasa.Api.Tests;

/// <summary>
/// Güncellemeyi geri alma — "Önceki haline döndür" (Paket D, özellik 36). Aynı kayıt, normal
/// düzenlemedeki doğrulamalar; kayıt sonradan değiştiyse 409; para iki kez sayılmaz.
/// </summary>
public class GuncellemeGeriAlmaTests : IClassFixture<PaketDFactory>
{
    private readonly PaketDFactory _factory;
    public GuncellemeGeriAlmaTests(PaketDFactory factory) => _factory = factory;

    private static async Task<JsonElement> SonGuncelleme(HttpClient c, string tur, int kayitId)
        => (await Gecmis(c, tur)).First(g => g.GetProperty("kayitId").ValueKind == JsonValueKind.Number
                                           && g.GetProperty("kayitId").GetInt32() == kayitId && g.Str("eylem") == "Güncellendi");

    private static Task<HttpResponseMessage> IslemGuncelle(HttpClient c, int id, decimal tutar, string cari = "A", string? not = null)
        => c.PutAsJsonAsync($"/api/islemler/{id}", new { tarih = "2026-09-10", cari, tutarTl = tutar, kanal = "MEZAT", tip = "Cari", not });

    private static async Task<JsonElement> Islem(HttpClient c, int id)
        => (await GetJson(c, "/api/islemler")).EnumerateArray().Single(i => i.Id() == id);

    [Fact]
    public async Task Islem_guncellemesi_onceki_haline_doner_ayni_kayit_gecmise_yazilir()
    {
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 9, 1));
        var id = await IslemEkle(c, "A", 100m);
        var kasa0 = await GuncelKasa(c);
        await Basarili(await IslemGuncelle(c, id, 150m, not: "düzeltme"));
        var satir = await SonGuncelleme(c, GecmisTurleri.Islem, id);
        Assert.True(satir.Bool("geriAlinabilir"));

        var r = await GeriAl(c, satir.Id());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var islem = await Islem(c, id);   // aynı Id
        Assert.Equal(100m, islem.Dec("tutarTl"));
        Assert.True(islem.Null("not"));
        Assert.Equal(kasa0, await GuncelKasa(c));
        Assert.Single((await GetJson(c, "/api/islemler")).EnumerateArray(), i => i.Id() == id);

        var gecmis = await Gecmis(c, GecmisTurleri.Islem);
        var donus = gecmis.First(g => g.GetProperty("kayitId").GetInt32() == id);
        Assert.Equal(Eylemler.GuncellemeGeriAlindi, donus.Str("eylem"));
        Assert.StartsWith("İşlem önceki haline döndürüldü: ", donus.Str("ozet"));
        Assert.Contains("Tutar: 150,00 ₺ → 100,00 ₺", donus.Str("ozet"));
        Assert.False(donus.Bool("geriAlinabilir"));
        var eski = gecmis.Single(g => g.Id() == satir.Id());
        Assert.True(eski.Bool("geriAlindi"));
        Assert.False(eski.Bool("geriAlinabilir"));

        var ikinci = await GeriAl(c, satir.Id());
        Assert.Equal(HttpStatusCode.Conflict, ikinci.StatusCode);
        Assert.Equal("Bu değişiklik zaten geri alındı.", await Hata(ikinci));
    }

    [Fact]
    public async Task Sonradan_degisen_kayit_409_sirayla_geri_alinir()
    {
        var c = await _factory.EditorClientAsync();
        var id = await IslemEkle(c, "A", 10m);
        await Basarili(await IslemGuncelle(c, id, 20m));
        var s1 = await SonGuncelleme(c, GecmisTurleri.Islem, id);
        await Basarili(await IslemGuncelle(c, id, 30m));
        var s2 = await SonGuncelleme(c, GecmisTurleri.Islem, id);

        Assert.False((await Gecmis(c, GecmisTurleri.Islem)).Single(g => g.Id() == s1.Id()).Bool("geriAlinabilir"));
        var r1 = await GeriAl(c, s1.Id());
        Assert.Equal(HttpStatusCode.Conflict, r1.StatusCode);
        Assert.Contains("yeniden değişti", await Hata(r1));

        Assert.Equal(HttpStatusCode.OK, (await GeriAl(c, s2.Id())).StatusCode);
        Assert.Equal(20m, (await Islem(c, id)).Dec("tutarTl"));
        Assert.True((await Gecmis(c, GecmisTurleri.Islem)).Single(g => g.Id() == s1.Id()).Bool("geriAlinabilir"));
        Assert.Equal(HttpStatusCode.OK, (await GeriAl(c, s1.Id())).StatusCode);
        Assert.Equal(10m, (await Islem(c, id)).Dec("tutarTl"));
    }

    [Fact]
    public async Task Eski_deger_normal_dogrulamadan_gecer_silinen_kayit_409()
    {
        var c = await _factory.EditorClientAsync();
        var cariAd = "Geçici Cari " + Guid.NewGuid().ToString("N")[..5];
        var cari = await Basarili(await c.PostAsJsonAsync("/api/cariler", new { ad = cariAd, aktif = true }));
        var id = await IslemEkle(c, cariAd, 10m);
        await Basarili(await IslemGuncelle(c, id, 10m, cari: "B"));
        var satir = await SonGuncelleme(c, GecmisTurleri.Islem, id);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/cariler/{cari.Id()}")).StatusCode);
        var r = await GeriAl(c, satir.Id());
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal($"Önceki haline döndürülemedi: '{cariAd}' adında bir cari yok. Önce Cariler sayfasından ekleyin.", await Hata(r));
        Assert.Equal("B", (await Islem(c, id)).Str("cari"));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/islemler/{id}")).StatusCode);
        var silinmis = await GeriAl(c, satir.Id());
        Assert.Equal(HttpStatusCode.Conflict, silinmis.StatusCode);
        Assert.Contains("Kayıt artık yok", await Hata(silinmis));
    }

    [Fact]
    public async Task Cek_durumu_geri_alininca_kasa_hareketi_kalkar()
    {
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 9, 1));
        var cek = await Basarili(await c.PostAsJsonAsync("/api/cekler", new
        {
            yon = "Alinan", kisi = "Geri", tutar = 750m, duzenlemeTarihi = "2026-09-01", vadeTarihi = "2026-09-20",
            kanal = "MEZAT", durum = "Portfoyde", konum = "BankadaTahsilde",
        }));
        var kasa0 = await GuncelKasa(c);
        await Basarili(await c.PostAsJsonAsync($"/api/cekler/{cek.Id()}/durum", new { durum = "TahsilEdildi" }));
        Assert.Equal(kasa0 + 750m, await GuncelKasa(c));
        var satir = await SonGuncelleme(c, GecmisTurleri.Cek, cek.Id());
        Assert.Equal(HttpStatusCode.OK, (await GeriAl(c, satir.Id())).StatusCode);
        var geri = (await GetJson(c, "/api/cekler")).EnumerateArray().Single(x => x.Id() == cek.Id());
        Assert.Equal("Portfoyde", geri.Str("durum"));
        Assert.True(geri.Null("islemTarihi"));
        Assert.Equal("BankadaTahsilde", geri.Str("konum"));
        Assert.Equal(kasa0, await GuncelKasa(c));
    }

    [Fact]
    public async Task Gelen_kanal_ve_kasa_acilis_devri_geri_alinir()
    {
        using var f = new PaketDFactory();
        var c = await f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 9, 1));
        var kasa0 = await GuncelKasa(c);

        // Gelen: 100 → 250 → geri 100.
        var gelen = await Basarili(await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-09-01", kanal = "MEZAT", tutarTl = 100m }));
        await Basarili(await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-09-01", kanal = "MEZAT", tutarTl = 250m }));
        Assert.Equal(HttpStatusCode.OK, (await GeriAl(c, (await SonGuncelleme(c, GecmisTurleri.Gelen, gelen.Id())).Id())).StatusCode);
        Assert.Equal(kasa0 + 100m, await GuncelKasa(c));

        // Kanal açılış devri (ad değişmeden).
        var kanal = (await GetJson(c, "/api/kanallar")).EnumerateArray().Single(k => k.Str("ad") == "TOPTAN");
        await Basarili(await c.PutAsJsonAsync($"/api/kanallar/{kanal.Id()}", new { ad = "TOPTAN", aktif = true, sira = 2, acilisDevri = 500m }));
        Assert.Equal(500m, (await GetJson(c, "/api/kanallar")).EnumerateArray().Single(k => k.Id() == kanal.Id()).Dec("acilisDevri"));
        Assert.Equal(HttpStatusCode.OK, (await GeriAl(c, (await SonGuncelleme(c, GecmisTurleri.Kanal, kanal.Id())).Id())).StatusCode);
        Assert.Equal(0m, (await GetJson(c, "/api/kanallar")).EnumerateArray().Single(k => k.Id() == kanal.Id()).Dec("acilisDevri"));

        // Kasa açılış devri (takip başlangıcı aynı).
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2026-09-01", kasaAcilisDevri = 1_000m })).EnsureSuccessStatusCode();
        Assert.Equal(kasa0 + 1_100m, await GuncelKasa(c));   // gelen 100 + açılış 1000
        var ayarSatiri = (await Gecmis(c, GecmisTurleri.Ayar)).First(g => g.Str("eylem") == "Güncellendi");
        Assert.True(ayarSatiri.Bool("geriAlinabilir"));
        Assert.Equal(HttpStatusCode.OK, (await GeriAl(c, ayarSatiri.Id())).StatusCode);
        Assert.Equal(kasa0 + 100m, await GuncelKasa(c));
        Assert.Equal(0m, (await GetJson(c, "/api/ayarlar")).Dec("kasaAcilisDevri"));
    }

    [Fact]
    public async Task Ad_degisimi_takip_degisimi_birlesen_gelen_ve_desteklenmeyen_tur_geri_alinamaz()
    {
        using var f = new PaketDFactory();
        var c = await f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 8, 3));   // Pazartesi: haftalık dönemler

        // Takip başlangıcı değişikliği.
        var takip = (await Gecmis(c, GecmisTurleri.Ayar)).First(g => g.Str("eylem") == "Güncellendi");
        Assert.False(takip.Bool("geriAlinabilir"));
        var r1 = await GeriAl(c, takip.Id());
        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);
        Assert.StartsWith("Takip başlangıcı değişikliği geri alınamaz", await Hata(r1));

        // Kanal adı değişimi.
        var kanal = (await GetJson(c, "/api/kanallar")).EnumerateArray().Single(k => k.Str("ad") == "PERAKENDE");
        await Basarili(await c.PutAsJsonAsync($"/api/kanallar/{kanal.Id()}", new { ad = "PERAKENDE 2", aktif = true, sira = 1, acilisDevri = 0m }));
        var ad = await SonGuncelleme(c, GecmisTurleri.Kanal, kanal.Id());
        Assert.False(ad.Bool("geriAlinabilir"));
        Assert.StartsWith("Kanal adı değişiklikleri geri alınamaz", await Hata(await GeriAl(c, ad.Id())));

        // Desteklenmeyen tür (cari güncellemesi).
        var cari = await Basarili(await c.PostAsJsonAsync("/api/cariler", new { ad = "Tür Deneme", aktif = true }));
        await Basarili(await c.PutAsJsonAsync($"/api/cariler/{cari.Id()}", new { ad = "Tür Deneme", aktif = false }));
        var cariSatiri = await SonGuncelleme(c, GecmisTurleri.Cari, cari.Id());
        Assert.False(cariSatiri.Bool("geriAlinabilir"));
        Assert.Equal("Cari güncellemeleri geri alınamaz.", await Hata(await GeriAl(c, cariSatiri.Id())));

        // Takip değişince birleşen gelen (GecmisTests'teki senaryo): iki dönemin geleni tek döneme toplanır.
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 10));
        await Basarili(await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-10", kanal = "MEZAT", tutarTl = 100m }));
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 11));
        await Basarili(await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-11", kanal = "MEZAT", tutarTl = 200m }));
        var kasaOnce = await GuncelKasa(c);
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 9));
        Assert.Contains(await Gecmis(c, GecmisTurleri.Gelen), g => g.Str("ozet").Contains("dönemine birleştirildi"));
        var gelenSatirlari = (await Gecmis(c, GecmisTurleri.Gelen)).Where(g => g.Str("eylem") == "Güncellendi").ToList();
        Assert.NotEmpty(gelenSatirlari);
        Assert.All(gelenSatirlari, g => Assert.False(g.Bool("geriAlinabilir")));
        var kasaBirlesik = await GuncelKasa(c);
        foreach (var g in gelenSatirlari)
            Assert.NotEqual(HttpStatusCode.OK, (await GeriAl(c, g.Id())).StatusCode);
        Assert.Equal(kasaBirlesik, await GuncelKasa(c));
        _ = kasaOnce;

        var izleyici = await f.IzleyiciAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await GeriAl(izleyici, cariSatiri.Id())).StatusCode);
    }
}
