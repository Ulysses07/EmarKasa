using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.Tests.PaketB;

namespace Kasa.Api.Tests;

/// <summary>
/// 11 · Ay kilidi: kilitli aya hiçbir uç noktadan yazılamaz (merkezî SaveChanges denetimi, 409),
/// tüm ayları etkileyen ayarlar kilit varken değişmez, kilit/aç/yayın editöre özeldir ve geçmişe yazılır.
/// </summary>
public class AyKilidiTests : IClassFixture<PaketBFactory>
{
    private readonly PaketBFactory _f;
    public AyKilidiTests(PaketBFactory f) => _f = f;

    private static readonly DateOnly Haz1 = new(2026, 6, 1);

    private async Task<HttpClient> HazirlaAsync()
    {
        _f.Temizle();
        _f.Saat.Ayarla(new DateOnly(2026, 9, 24));
        var c = await _f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, Haz1);
        return c;
    }

    private static Task<HttpResponseMessage> Kilitle(HttpClient c, int yil, int ay)
        => c.PostAsJsonAsync("/api/ay-kapanisi/kilitle", new { yil, ay });

    private static Task<HttpResponseMessage> KilitAc(HttpClient c, int yil, int ay)
        => c.PostAsJsonAsync("/api/ay-kapanisi/kilit-ac", new { yil, ay });

    private static async Task Kilitli409(HttpResponseMessage r, string ayAdi = "Ağustos 2026")
    {
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var hata = await HataMetni(r);
        Assert.Contains(ayAdi, hata);
        Assert.Contains("kilit", hata);
    }

    [Fact]
    public async Task Kilitli_aya_islem_eklenemez_tasinamaz_silinemez_kilit_acilinca_serbest()
    {
        var c = await HazirlaAsync();
        var temmuzId = await IslemEkle(c, new DateOnly(2026, 7, 10), 100m);
        var agustosId = await IslemEkle(c, new DateOnly(2026, 8, 10), 200m);

        var r = await Kilitle(c, 2026, 8);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var durum = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(durum.GetProperty("kilitli").GetBoolean());
        Assert.Equal("Ağustos 2026", durum.GetProperty("etiket").GetString());

        await Kilitli409(await IslemYaz(c, new DateOnly(2026, 8, 11), 50m));
        // Kilitli aydan çıkarma ve kilitli aya taşıma.
        await Kilitli409(await c.PutAsJsonAsync($"/api/islemler/{agustosId}", new { tarih = "2026-09-01", cari = "X", tutarTl = 200m, kanal = "MEZAT", tip = "Cari" }));
        await Kilitli409(await c.PutAsJsonAsync($"/api/islemler/{temmuzId}", new { tarih = "2026-08-01", cari = "X", tutarTl = 100m, kanal = "MEZAT", tip = "Cari" }));
        // Program.cs'deki (Yaz'sız) silme uç noktası da grup filtresiyle 409 döner.
        await Kilitli409(await c.DeleteAsync($"/api/islemler/{agustosId}"));
        Assert.Equal(2, _f.Db(db => db.Islemler.Count()));

        // Kilitsiz aylar serbest.
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/islemler/{temmuzId}", new { tarih = "2026-07-11", cari = "X", tutarTl = 150m, kanal = "MEZAT", tip = "Cari" })).StatusCode);
        await IslemEkle(c, new DateOnly(2026, 9, 1), 10m);

        Assert.Equal(HttpStatusCode.OK, (await KilitAc(c, 2026, 8)).StatusCode);
        await IslemEkle(c, new DateOnly(2026, 8, 11), 50m);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/islemler/{agustosId}")).StatusCode);
        var tekrar = await KilitAc(c, 2026, 8);
        Assert.Equal(HttpStatusCode.Conflict, tekrar.StatusCode);
        Assert.Equal("Ağustos 2026 kilitli değil.", await HataMetni(tekrar));
    }

    [Fact]
    public async Task Gelen_kart_odemesi_cek_ve_sayim_de_kilitli()
    {
        var c = await HazirlaAsync();
        var kart = (await (await c.PostAsJsonAsync("/api/kredikartlari", new { ad = "K", kesimTarihi = "2026-08-05", sonOdemeTarihi = "2026-08-15", limit = 1000, borc = 0 }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var cek = (await (await c.PostAsJsonAsync("/api/cekler", new
        {
            yon = "Alinan", kisi = "Müşteri", tutar = 500m, duzenlemeTarihi = "2026-07-01", vadeTarihi = "2026-08-20",
            kanal = "MEZAT", durum = "Portfoyde",
        })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await Kilitle(c, 2026, 8)).StatusCode);

        await Kilitli409(await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-08-03", kanal = "MEZAT", tutarTl = 10m }));
        await Kilitli409(await c.PostAsJsonAsync("/api/kartodemeler", new { krediKartiId = kart, tarih = "2026-08-10", tutar = 5m }));
        await Kilitli409(await c.PostAsJsonAsync("/api/kasasayimlari", new { tarih = "2026-08-31", sayilanTutar = 1m }));
        // Portföydeki çeki kilitli ayda tahsil etmek kilitli aya kasa hareketi yazar.
        await Kilitli409(await c.PutAsJsonAsync($"/api/cekler/{cek}", new
        {
            yon = "Alinan", kisi = "Müşteri", tutar = 500m, duzenlemeTarihi = "2026-07-01", vadeTarihi = "2026-08-20",
            kanal = "MEZAT", durum = "TahsilEdildi", islemTarihi = "2026-08-20",
        }));
        // Eylülde tahsil serbest (vade Ağustos olsa da işlem tarihi belirleyicidir).
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/cekler/{cek}", new
        {
            yon = "Alinan", kisi = "Müşteri", tutar = 500m, duzenlemeTarihi = "2026-07-01", vadeTarihi = "2026-08-20",
            kanal = "MEZAT", durum = "TahsilEdildi", islemTarihi = "2026-09-02",
        })).StatusCode);
        await GelenYaz(c, new DateOnly(2026, 9, 7), "MEZAT", 10m);
    }

    [Fact]
    public async Task KK_islemi_sonraki_kilitli_ayi_da_korur()
    {
        var c = await HazirlaAsync();
        Assert.Equal(HttpStatusCode.OK, (await Kilitle(c, 2026, 8)).StatusCode);
        // Temmuz'un kartsız K.K'sı Ağustos'ta kasadan/aylık sonuçtan düşer → Ağustos kilitliyken eklenemez.
        await Kilitli409(await IslemYaz(c, new DateOnly(2026, 7, 20), 75m, "MEZAT", "KrediKarti", "Market"));
        // Temmuz'un Cari işlemi Ağustos'a dokunmaz.
        await IslemEkle(c, new DateOnly(2026, 7, 20), 75m);
    }

    [Fact]
    public async Task Tum_aylari_etkileyen_ayarlar_kilitliyken_degismez_etiketler_serbest()
    {
        var c = await HazirlaAsync();
        Assert.Equal(HttpStatusCode.OK, (await Kilitle(c, 2026, 7)).StatusCode);

        var r1 = await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2026-06-01", kasaAcilisDevri = 1m });
        Assert.Equal(HttpStatusCode.Conflict, r1.StatusCode);
        Assert.Contains("kasa açılış devri", await HataMetni(r1));
        var r2 = await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2026-05-01", kasaAcilisDevri = 0m });
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
        Assert.Contains("Takip başlangıcı", await HataMetni(r2));
        // Değişmeyen ayar kaydı serbest.
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2026-06-01", kasaAcilisDevri = 0m })).StatusCode);

        var kanal = _f.Db(db => db.Kanallar.AsNoTracking().OrderBy(k => k.Id).First());
        var r3 = await c.PutAsJsonAsync($"/api/kanallar/{kanal.Id}", new { ad = kanal.Ad, aktif = kanal.Aktif, sira = kanal.Sira, acilisDevri = kanal.AcilisDevri + 5m });
        Assert.Equal(HttpStatusCode.Conflict, r3.StatusCode);
        Assert.Contains("kanal açılış devri", await HataMetni(r3));
        var r4 = await c.PostAsJsonAsync("/api/kanallar", new { ad = "YENİ KANAL", aktif = true, sira = 9, acilisDevri = 10m });
        Assert.Equal(HttpStatusCode.Conflict, r4.StatusCode);

        // Ad ve aktiflik değişimi (yalnız etiket) serbest; sonra eski haline döner.
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/kanallar/{kanal.Id}", new { ad = kanal.Ad + " X", aktif = false, sira = kanal.Sira, acilisDevri = kanal.AcilisDevri })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/kanallar/{kanal.Id}", new { ad = kanal.Ad, aktif = kanal.Aktif, sira = kanal.Sira, acilisDevri = kanal.AcilisDevri })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await KilitAc(c, 2026, 7)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2026-06-01", kasaAcilisDevri = 0m })).StatusCode);
    }

    [Fact]
    public async Task Takip_baslangici_yalniz_etkilenen_kilitte_engellenir()
    {
        var c = await HazirlaAsync();
        Assert.Equal(HttpStatusCode.OK, (await Kilitle(c, 2026, 6)).StatusCode);
        // Haziran kilitli; takibi Ağustos'a almak Haziran'ı da değiştirir (takip dışına düşer).
        var r = await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = "2026-08-01", kasaAcilisDevri = 0m });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("Haziran 2026", await HataMetni(r));
        Assert.Equal(HttpStatusCode.OK, (await KilitAc(c, 2026, 6)).StatusCode);
    }

    [Fact]
    public async Task Kilit_kurallari_ve_yetki()
    {
        var c = await HazirlaAsync();
        var buAy = await Kilitle(c, 2026, 9);
        Assert.Equal(HttpStatusCode.BadRequest, buAy.StatusCode);
        Assert.Equal("Eylül 2026 henüz bitmedi; yalnız bitmiş bir ay kilitlenebilir.", await HataMetni(buAy));
        Assert.Equal(HttpStatusCode.BadRequest, (await Kilitle(c, 2026, 13)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Kilitle(c, 2026, 8)).StatusCode);
        var iki = await Kilitle(c, 2026, 8);
        Assert.Equal(HttpStatusCode.Conflict, iki.StatusCode);
        Assert.Equal("Ağustos 2026 zaten kilitli.", await HataMetni(iki));

        var izleyici = await IzleyiciAsync(_f);
        Assert.Equal(HttpStatusCode.Forbidden, (await Kilitle(izleyici, 2026, 7)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await KilitAc(izleyici, 2026, 8)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PostAsJsonAsync("/api/ay-kapanisi/yayinla", new { yil = 2026, ay = 8 })).StatusCode);
        var liste = await Oku(izleyici, "/api/ay-kapanisi/kilitler");
        Assert.Equal("Ağustos 2026", liste.EnumerateArray().Single().GetProperty("etiket").GetString());
        Assert.True((await Oku(izleyici, "/api/ay-kapanisi?yil=2026&ay=8")).GetProperty("kilitli").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/api/ay-kapanisi?yil=2026&ay=8")).StatusCode);

        // Kilitle/aç geçmişe yazılır.
        Assert.Equal(HttpStatusCode.OK, (await KilitAc(c, 2026, 8)).StatusCode);
        var gecmis = (await Oku(c, "/api/gecmis?tur=Ay%20kilidi")).EnumerateArray().Select(g => g.GetProperty("ozet").GetString()).ToList();
        Assert.Contains("Ay kilidi eklendi: Ağustos 2026", gecmis);
        Assert.Contains("Ay kilidi silindi: Ağustos 2026", gecmis);
    }

    [Fact]
    public async Task Tekrarlayan_onay_kilitli_ayda_engellenir_atla_serbest_geri_alma_da_engellenir()
    {
        var c = await HazirlaAsync();
        (await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = "Kira", aktif = true })).EnsureSuccessStatusCode();
        var t = (await (await c.PostAsJsonAsync("/api/tekrarlayangiderler", new { kalem = "Kira", kanal = "Ortak", tutar = 1000m, ayinGunu = 5, aktif = true, baslangicAyi = "2026-07-01" }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var silinecek = await IslemEkle(c, new DateOnly(2026, 8, 3), 40m);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/islemler/{silinecek}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Kilitle(c, 2026, 8)).StatusCode);

        await Kilitli409(await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{t}/onayla", new { ay = "2026-08-01" }));
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{t}/atla", new { ay = "2026-08-01" })).StatusCode);

        var silme = (await Oku(c, "/api/gecmis?tur=%C4%B0%C5%9Flem")).EnumerateArray().First(g => g.GetProperty("eylem").GetString() == "Silindi");
        await Kilitli409(await c.PostAsync($"/api/gecmis/{silme.GetProperty("id").GetInt32()}/geri-al", null));
        Assert.Equal(HttpStatusCode.OK, (await KilitAc(c, 2026, 8)).StatusCode);
    }

    [Fact]
    public void Kilit_alanlari_ozniteliklerden_okunur()
    {
        Assert.Equal(["Tarih"], AyKilidiKurali.KilitAlanlari(typeof(IslemEntity)));
        Assert.Equal(["DonemStart"], AyKilidiKurali.KilitAlanlari(typeof(GelenEntity)));
        Assert.Equal(["Tarih"], AyKilidiKurali.KilitAlanlari(typeof(KartOdemeEntity)));
        Assert.Equal(["IslemTarihi"], AyKilidiKurali.KilitAlanlari(typeof(CekEntity)));
        Assert.Equal(["Tarih"], AyKilidiKurali.KilitAlanlari(typeof(KasaSayimEntity)));
        Assert.Empty(AyKilidiKurali.KilitAlanlari(typeof(TekrarlayanGirisEntity)));
        Assert.Empty(AyKilidiKurali.KilitAlanlari(typeof(KanalHedefEntity)));
    }
}
