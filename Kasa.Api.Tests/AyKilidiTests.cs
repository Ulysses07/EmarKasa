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

        // Kilit geriye doğru kapsar: Temmuz da kilitli (Ağustos'un açılış kasası Temmuz'dan devreder). Eylül serbest.
        await Kilitli409(await c.PutAsJsonAsync($"/api/islemler/{temmuzId}", new { tarih = "2026-07-11", cari = "X", tutarTl = 150m, kanal = "MEZAT", tip = "Cari" }), "Temmuz 2026");
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
        Assert.Equal(HttpStatusCode.OK, (await Kilitle(c, 2026, 6)).StatusCode);
        // Takip öncesi Mayıs kilitli değil; ama Mayıs'ın kartsız K.K'sı takibin ilk ayı Haziran'da kasadan
        // ve aylık sonuçtan düşer → Haziran kilitliyken eklenemez.
        await Kilitli409(await IslemYaz(c, new DateOnly(2026, 5, 20), 75m, "MEZAT", "KrediKarti", "Market"), "Haziran 2026");
        // Mayıs'ın Cari işlemi Haziran'a dokunmaz (takipten önce: hiçbir rakama girmez).
        await IslemEkle(c, new DateOnly(2026, 5, 20), 75m);
    }

    [Fact]
    public async Task Kilit_geriye_dogru_kapsar_onceki_ay_duzeltmesi_kilitli_ayin_kasasini_degistiremez()
    {
        var c = await HazirlaAsync();
        var temmuzId = await IslemEkle(c, new DateOnly(2026, 7, 10), 100m);
        await GelenYaz(c, new DateOnly(2026, 8, 3), "MEZAT", 1_000m);
        var once = await Oku(c, "/api/rapor/kasa-dokumu?baslangic=2026-08-01&bitis=2026-08-31");
        Assert.Equal((-100m, 900m), (D(once, "acilis"), D(once, "kapanis")));

        Assert.Equal(HttpStatusCode.OK, (await Kilitle(c, 2026, 8)).StatusCode);
        // Takip başlangıcından Ağustos'a kadar kilitsiz aylar da kilitlendi (ve geçmişe yazıldı).
        var kilitler = (await Oku(c, "/api/ay-kapanisi/kilitler")).EnumerateArray().Select(k => k.GetProperty("etiket").GetString()).ToList();
        Assert.Equal(new[] { "Ağustos 2026", "Temmuz 2026", "Haziran 2026" }, kilitler);
        Assert.True((await Oku(c, "/api/ay-kapanisi?yil=2026&ay=7")).GetProperty("kilitli").GetBoolean());
        Assert.False((await Oku(c, "/api/ay-kapanisi?yil=2026&ay=9")).GetProperty("kilitli").GetBoolean());
        var gecmis = (await Oku(c, "/api/gecmis?tur=Ay%20kilidi")).EnumerateArray().Select(g => g.GetProperty("ozet").GetString()).ToList();
        Assert.Contains("Ay kilidi eklendi: Haziran 2026", gecmis);

        // Temmuz'daki düzeltme Ağustos'un açılış ve kapanış kasasını değiştirirdi: 409, rakamlar aynı.
        var r = await c.PutAsJsonAsync($"/api/islemler/{temmuzId}", new { tarih = "2026-07-10", cari = "X", tutarTl = 20_000m, kanal = "MEZAT", tip = "Cari" });
        await Kilitli409(r, "Temmuz 2026");
        var sonra = await Oku(c, "/api/rapor/kasa-dokumu?baslangic=2026-08-01&bitis=2026-08-31");
        Assert.Equal((-100m, 900m), (D(sonra, "acilis"), D(sonra, "kapanis")));

        // Temmuz'un kilidini açmak Ağustos'unkini de açar (kasası Temmuz'dan devreder); Haziran kilitli kalır.
        var ac = await KilitAc(c, 2026, 7);
        Assert.Equal(HttpStatusCode.OK, ac.StatusCode);
        Assert.False((await ac.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("kilitli").GetBoolean());
        Assert.False((await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8")).GetProperty("kilitli").GetBoolean());
        kilitler = (await Oku(c, "/api/ay-kapanisi/kilitler")).EnumerateArray().Select(k => k.GetProperty("etiket").GetString()).ToList();
        Assert.Equal(new[] { "Haziran 2026" }, kilitler);
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/islemler/{temmuzId}", new { tarih = "2026-07-10", cari = "X", tutarTl = 150m, kanal = "MEZAT", tip = "Cari" })).StatusCode);
        await Kilitli409(await IslemYaz(c, new DateOnly(2026, 6, 15), 1m), "Haziran 2026");
    }

    [Fact]
    public async Task Arada_kilitsiz_ay_kalmis_olsa_da_onceki_aylar_yazilamaz()
    {
        // Eski/elle oluşmuş durum: yalnız Ağustos satırı var. Kural yine geriye doğru kapsar.
        var c = await HazirlaAsync();
        var temmuzId = await IslemEkle(c, new DateOnly(2026, 7, 10), 100m);
        _f.Db(db => { db.AyKilitleri.Add(new AyKilidiEntity { Ay = new DateOnly(2026, 8, 1), Etiket = "Ağustos 2026" }); db.SaveChanges(); });
        await Kilitli409(await c.DeleteAsync($"/api/islemler/{temmuzId}"), "Temmuz 2026");
        var temmuz = await Oku(c, "/api/ay-kapanisi?yil=2026&ay=7");
        Assert.True(temmuz.GetProperty("kilitli").GetBoolean());
        // Aylık rapordaki "Kilidi aç" Temmuz'dan da çalışır: Temmuz ve sonrası açılır.
        Assert.Equal(HttpStatusCode.OK, (await KilitAc(c, 2026, 7)).StatusCode);
        Assert.Empty((await Oku(c, "/api/ay-kapanisi/kilitler")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/islemler/{temmuzId}")).StatusCode);
    }

    private async Task<int> KanalId(HttpClient c, string ad)
        => (await Oku(c, "/api/kanallar")).EnumerateArray().First(k => k.GetProperty("ad").GetString() == ad).GetProperty("id").GetInt32();

    private static Task<HttpResponseMessage> KanalYaz(HttpClient c, int id, string ad, bool aktif, int sira)
        => c.PutAsJsonAsync($"/api/kanallar/{id}", new { ad, aktif, sira, acilisDevri = 0m });

    [Fact]
    public async Task Kanal_sirasi_Ortak_kurusunu_degistirirse_kilitliyken_engellenir()
    {
        var c = await HazirlaAsync();
        var kanallar = (await Oku(c, "/api/kanallar")).EnumerateArray()
            .Select(k => (Id: k.GetProperty("id").GetInt32(), Ad: k.GetProperty("ad").GetString()!, Aktif: k.GetProperty("aktif").GetBoolean(), Sira: k.GetProperty("sira").GetInt32())).ToList();
        var mezat = kanallar.First(k => k.Ad == "MEZAT");
        await GelenYaz(c, new DateOnly(2026, 8, 3), "MEZAT", 1_000m);
        await GelenYaz(c, new DateOnly(2026, 8, 3), "PERAKENDE", 1_000m);
        await IslemEkle(c, new DateOnly(2026, 8, 5), 100.01m, "Ortak");
        var once = await Oku(c, "/api/rapor/aylik?yil=2026&ay=8");
        Assert.Equal(HttpStatusCode.OK, (await Kilitle(c, 2026, 8)).StatusCode);

        try
        {
            // Artık kuruş kanal sırasındaki ilk kanala gider: sırayı değiştirmek kilitli Ağustos'un rakamını değiştirirdi.
            var r = await KanalYaz(c, mezat.Id, "MEZAT", true, 99);
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var hata = await HataMetni(r);
            Assert.Contains("Ağustos 2026", hata);
            Assert.Contains("Ortak gider", hata);
            Assert.Equal(once.GetRawText(), (await Oku(c, "/api/rapor/aylik?yil=2026&ay=8")).GetRawText());

            // Sıra değişse de dağılım aynı kalıyorsa (TOPTAN o ay pay almıyor) serbest; ad değişimi de serbest.
            var toptan = kanallar.First(k => k.Ad == "TOPTAN");
            Assert.Equal(HttpStatusCode.OK, (await KanalYaz(c, toptan.Id, "TOPTAN", true, 99)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await KanalYaz(c, toptan.Id, "TOPTAN", false, 99)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await KanalYaz(c, mezat.Id, "MEZAT YENİ", true, mezat.Sira)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await KanalYaz(c, mezat.Id, "MEZAT", true, mezat.Sira)).StatusCode);
            Assert.Equal(once.GetRawText(), (await Oku(c, "/api/rapor/aylik?yil=2026&ay=8")).GetRawText());
        }
        finally
        {
            Assert.Equal(HttpStatusCode.OK, (await KilitAc(c, 2026, 6)).StatusCode);
            foreach (var k in kanallar) (await KanalYaz(c, k.Id, k.Ad, k.Aktif, k.Sira)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Hareketsiz_ayda_aktiflik_ve_kanal_ekleme_silme_Ortak_bolusunu_degistirirse_engellenir()
    {
        var c = await HazirlaAsync();
        var kanallar = (await Oku(c, "/api/kanallar")).EnumerateArray()
            .Select(k => (Id: k.GetProperty("id").GetInt32(), Ad: k.GetProperty("ad").GetString()!, Aktif: k.GetProperty("aktif").GetBoolean(), Sira: k.GetProperty("sira").GetInt32())).ToList();
        var ekstra = (await (await c.PostAsJsonAsync("/api/kanallar", new { ad = "EKSTRA", aktif = true, sira = 50, acilisDevri = 0m }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        // Ağustos'ta hiçbir kanal hareketli değil: Ortak gider aktif kanallara bölünür.
        await IslemEkle(c, new DateOnly(2026, 8, 5), 300m, "Ortak");
        var once = await Oku(c, "/api/rapor/aylik?yil=2026&ay=8");
        Assert.Equal(HttpStatusCode.OK, (await Kilitle(c, 2026, 8)).StatusCode);
        try
        {
            var toptan = kanallar.First(k => k.Ad == "TOPTAN");
            var r1 = await KanalYaz(c, toptan.Id, "TOPTAN", false, toptan.Sira);
            Assert.Equal(HttpStatusCode.Conflict, r1.StatusCode);
            Assert.Contains("Ortak gider", await HataMetni(r1));
            var r2 = await c.PostAsJsonAsync("/api/kanallar", new { ad = "YENİ", aktif = true, sira = 60, acilisDevri = 0m });
            Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/kanallar/{ekstra}")).StatusCode);
            Assert.Equal(once.GetRawText(), (await Oku(c, "/api/rapor/aylik?yil=2026&ay=8")).GetRawText());

            // Pasif kanal eklemek bölüşü değiştirmez: serbest (silmek de).
            var pasif = await c.PostAsJsonAsync("/api/kanallar", new { ad = "PASİF", aktif = false, sira = 70, acilisDevri = 0m });
            Assert.Equal(HttpStatusCode.Created, pasif.StatusCode);
            var pasifId = (await pasif.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
            Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/kanallar/{pasifId}")).StatusCode);
        }
        finally
        {
            Assert.Equal(HttpStatusCode.OK, (await KilitAc(c, 2026, 6)).StatusCode);
            (await c.DeleteAsync($"/api/kanallar/{ekstra}")).EnsureSuccessStatusCode();
            foreach (var k in kanallar) (await KanalYaz(c, k.Id, k.Ad, k.Aktif, k.Sira)).EnsureSuccessStatusCode();
        }
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
        Assert.Equal(new[] { "Ağustos 2026", "Temmuz 2026", "Haziran 2026" },
            liste.EnumerateArray().Select(k => k.GetProperty("etiket").GetString()));
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
