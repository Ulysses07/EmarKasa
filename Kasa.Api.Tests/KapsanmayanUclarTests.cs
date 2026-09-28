using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>
/// Hiçbir testin ulaşmadığı uçların gerçek sözleşmesi (tests-10): kart ve kredi durumu, kart düzenleme, ekstre asgarisi,
/// push/okundu uçları, sürüm, alış belgeleri listesi ve kullanılmayan kanalın silinmesi. Testler bugünkü davranışı
/// sabitler; her uç kendi sabit saatli sunucusunda koşar. İzleyici/alıcı 403 satırları UcYetkiTaramasiTests'tedir.
/// </summary>
public class KapsanmayanUclarTests
{
    private static readonly DateOnly Bugun = KasaWebFactory.VarsayilanBugun;
    // Takip başlangıcı bugünün ayından 8 ay önce: kesim ekstreleri ve geçmiş taksitler oluşur.
    private static readonly DateOnly Baslangic = new DateOnly(Bugun.Year, Bugun.Month, 1).AddMonths(-8);

    private static async Task<HttpClient> Editor(KasaWebFactory f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Baslangic, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
        return c;
    }
    private static async Task<T> Gonder<T>(HttpClient c, HttpMethod yontem, string yol, object govde)
    {
        using var r = await c.SendAsync(new HttpRequestMessage(yontem, yol) { Content = JsonContent.Create(govde) });
        Assert.True(r.IsSuccessStatusCode, $"{yontem} {yol} → {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<(HttpStatusCode Durum, string Govde)> Dene(HttpClient c, HttpMethod yontem, string yol, object govde)
    {
        using var r = await c.SendAsync(new HttpRequestMessage(yontem, yol) { Content = JsonContent.Create(govde) });
        return (r.StatusCode, await r.Content.ReadAsStringAsync());
    }
    private static Task<KartTakipDto> Kart(HttpClient c) => Gonder<KartTakipDto>(c, HttpMethod.Post, "/api/takip/kartlar",
        new KartTakipYaz(Guid.NewGuid(), 0, "Kapsam kartı", 10000m, 5, 25, Baslangic, 0, []));
    private static Task<KartTakipDto> Harcama(HttpClient c, KartTakipDto kart, decimal tutar) => Gonder<KartTakipDto>(c, HttpMethod.Post,
        $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, Baslangic, "Malzeme", tutar, 1, null, [new(1, tutar)]));
    private static async Task<KartTakipDto> KartOku(HttpClient c, int id) => (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{id}"))!;
    private static async Task<decimal> Kasa(HttpClient c) => (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa;

    [Fact]
    public async Task Kart_pasife_alininca_surum_artar_yeni_harcama_reddedilir_tekrar_ayni_yaniti_verir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        var kart = await Kart(c);
        var istek = new TakipDurumYaz(Guid.NewGuid(), kart.Surum, false, "Kart kapatıldı");
        var pasif = await Gonder<KartTakipDto>(c, HttpMethod.Post, $"/api/takip/kartlar/{kart.Id}/durum", istek);
        Assert.False(pasif.Aktif); Assert.Equal(kart.Surum + 1, pasif.Surum);
        Assert.False((await KartOku(c, kart.Id)).Aktif);

        // Pasif kart yeni harcamaya kapalıdır (FinansTakipEndpoints.ApplyCardCharge).
        var (durum, govde) = await Dene(c, HttpMethod.Post, $"/api/takip/kartlar/{kart.Id}/harcamalar",
            new KartHarcamaYaz(Guid.NewGuid(), pasif.Surum, Bugun, "Malzeme", 50m, 1, null, [new(1, 50m)]));
        Assert.Equal(HttpStatusCode.Conflict, durum); Assert.Contains("yeni kullanıma kapalı", govde);

        // Aynı istek kimliği ve içerik: aynı sonuç, sürüm ikinci kez artmaz.
        var tekrar = await Gonder<KartTakipDto>(c, HttpMethod.Post, $"/api/takip/kartlar/{kart.Id}/durum", istek);
        Assert.Equal(pasif.Surum, tekrar.Surum); Assert.False(tekrar.Aktif);
        Assert.Equal(pasif.Surum, (await KartOku(c, kart.Id)).Surum);
        // Aynı kimlik farklı içerikle ve eski sürümle yeni istek reddedilir.
        Assert.Equal(HttpStatusCode.Conflict, (await Dene(c, HttpMethod.Post, $"/api/takip/kartlar/{kart.Id}/durum", istek with { Aktif = true })).Durum);
        Assert.Equal(HttpStatusCode.Conflict, (await Dene(c, HttpMethod.Post, $"/api/takip/kartlar/{kart.Id}/durum",
            new TakipDurumYaz(Guid.NewGuid(), kart.Surum, true, "Eski sürüm"))).Durum);
        Assert.Equal(HttpStatusCode.BadRequest, (await Dene(c, HttpMethod.Post, $"/api/takip/kartlar/{kart.Id}/durum",
            new TakipDurumYaz(Guid.NewGuid(), pasif.Surum, true, " "))).Durum);

        var aktif = await Gonder<KartTakipDto>(c, HttpMethod.Post, $"/api/takip/kartlar/{kart.Id}/durum", new TakipDurumYaz(Guid.NewGuid(), pasif.Surum, true, "Yeniden açıldı"));
        Assert.True(aktif.Aktif); Assert.Equal(pasif.Surum + 1, aktif.Surum);
        await Gonder<KartTakipDto>(c, HttpMethod.Post, $"/api/takip/kartlar/{kart.Id}/harcamalar",
            new KartHarcamaYaz(Guid.NewGuid(), aktif.Surum, Bugun, "Malzeme", 50m, 1, null, [new(1, 50m)]));
    }

    /// <summary>Pasif kart gün dönümünde yeni kesim ekstresi almaz: bakım adımı (Sync) yalnız aktif kartın kesim
    /// ekstrelerini yazar, okuma da pasif karta ekstre türetmez. Yeniden aktifleşince yazma yolunun Sync'i bugünün ve
    /// önceki kesimin ekstrelerini yazar.</summary>
    [Fact]
    public async Task Pasif_kart_gun_donumunde_ekstre_uretmez_yeniden_aktiflesince_uretir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        var kart = await Kart(c); // kesim günü 5
        var pasif = await Gonder<KartTakipDto>(c, HttpMethod.Post, $"/api/takip/kartlar/{kart.Id}/durum", new TakipDurumYaz(Guid.NewGuid(), kart.Surum, false, "Kart kapatıldı"));
        var onceki = pasif.Ekstreler.Select(e => e.KesimTarihi).ToList();
        var kayitli = EkstreSayisi(f, kart.Id);
        Assert.NotEmpty(onceki);

        // İki ay sonra iki yeni kesim günü geçmiştir.
        var sonra = Bugun.AddMonths(2); ((SabitSaat)f.Saat!).Ayarla(sonra);
        var kesim = new DateOnly(sonra.Year, sonra.Month, 5); if (kesim < sonra) kesim = kesim.AddMonths(1);
        var yeniKesimler = new[] { kesim.AddMonths(-1), kesim };
        Assert.DoesNotContain(yeniKesimler, onceki.Contains);

        Bakim(f);
        Assert.Equal(kayitli, EkstreSayisi(f, kart.Id));
        Assert.Equal(onceki, (await KartOku(c, kart.Id)).Ekstreler.Select(e => e.KesimTarihi));

        var aktif = await Gonder<KartTakipDto>(c, HttpMethod.Post, $"/api/takip/kartlar/{kart.Id}/durum", new TakipDurumYaz(Guid.NewGuid(), pasif.Surum, true, "Yeniden açıldı"));
        Assert.True(aktif.Aktif);
        Assert.All(yeniKesimler, k => Assert.Contains(aktif.Ekstreler, e => e.KesimTarihi == k && e.Id != 0));
        Assert.Equal(kayitli + yeniKesimler.Length, EkstreSayisi(f, kart.Id));
    }

    private static int EkstreSayisi(KasaWebFactory f, int kartId)
    {
        using var scope = f.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<KasaDbContext>().TakipEkstreler.AsNoTracking().Count(e => e.KrediKartiId == kartId);
    }

    private static void Bakim(KasaWebFactory f)
    {
        using var scope = f.Services.CreateScope();
        FinansTakipServisi.Bakim(scope.ServiceProvider.GetRequiredService<KasaDbContext>());
    }

    [Fact]
    public async Task Kart_duzenleme_ad_limit_ve_gunleri_gunceller_eski_surum_ve_gecersiz_gun_reddedilir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        var kart = await Kart(c);
        // Düzenleme açılış alanlarını (tarih, borç, dağılım) yok sayar; yalnız ad, limit ve günler değişir.
        var istek = new KartTakipYaz(Guid.NewGuid(), kart.Surum, "  Yeni ad  ", 25000m, 10, 28, Bugun, 999m, [new(1, 999m)]);
        var yeni = await Gonder<KartTakipDto>(c, HttpMethod.Put, $"/api/takip/kartlar/{kart.Id}", istek);
        Assert.Equal("Yeni ad", yeni.Ad); Assert.Equal(25000m, yeni.Limit); Assert.Equal(10, yeni.KesimGunu); Assert.Equal(28, yeni.SonOdemeGunu);
        Assert.Equal(kart.Surum + 1, yeni.Surum); Assert.Equal(0m, yeni.Borc); Assert.Empty(yeni.Harcamalar);
        Assert.Equal(yeni.Ad, (await KartOku(c, kart.Id)).Ad);

        Assert.Equal(HttpStatusCode.Conflict, (await Dene(c, HttpMethod.Put, $"/api/takip/kartlar/{kart.Id}", istek with { IstekId = Guid.NewGuid(), Ad = "Eski sürüm" })).Durum);
        Assert.Equal(HttpStatusCode.BadRequest, (await Dene(c, HttpMethod.Put, $"/api/takip/kartlar/{kart.Id}", istek with { IstekId = Guid.NewGuid(), Surum = yeni.Surum, KesimGunu = 32 })).Durum);
        Assert.Equal(HttpStatusCode.NotFound, (await Dene(c, HttpMethod.Put, "/api/takip/kartlar/999999", istek with { IstekId = Guid.NewGuid() })).Durum);
        Assert.Equal(yeni.Surum, (await KartOku(c, kart.Id)).Surum);
    }

    [Fact]
    public async Task Ekstre_asgari_odeme_ve_son_odeme_tarihi_kaydedilir_asgari_kalan_odemeyle_azalir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        var kart = await Harcama(c, await Kart(c), 300m);
        var ekstre = kart.Ekstreler.Single(e => e.Borc == 300m);
        Assert.Null(ekstre.AsgariOdeme); Assert.Null(ekstre.AsgariKalan);
        var sonOdeme = ekstre.SonOdemeTarihi.AddDays(2);
        var yol = $"/api/takip/kartlar/{kart.Id}/ekstreler/{ekstre.Id}";

        kart = await Gonder<KartTakipDto>(c, HttpMethod.Put, yol, new KartEkstreYaz(Guid.NewGuid(), kart.Surum, sonOdeme, 100m, "Banka asgarisi"));
        var kayitli = kart.Ekstreler.Single(e => e.Id == ekstre.Id);
        Assert.Equal(sonOdeme, kayitli.SonOdemeTarihi); Assert.Equal(100m, kayitli.AsgariOdeme); Assert.Equal(100m, kayitli.AsgariKalan);

        // Ekstreye ödeme asgari kalanı düşürür; asgari, ekstrenin kalan borcunu aşamaz.
        kart = await Gonder<KartTakipDto>(c, HttpMethod.Post, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, Bugun, 40m, ekstre.Id, null));
        Assert.Equal(60m, kart.Ekstreler.Single(e => e.Id == ekstre.Id).AsgariKalan);
        kart = await Gonder<KartTakipDto>(c, HttpMethod.Put, yol, new KartEkstreYaz(Guid.NewGuid(), kart.Surum, sonOdeme, 1000m, "Yüksek asgari"));
        Assert.Equal(260m, kart.Ekstreler.Single(e => e.Id == ekstre.Id).AsgariKalan);

        Assert.Equal(HttpStatusCode.BadRequest, (await Dene(c, HttpMethod.Put, yol, new KartEkstreYaz(Guid.NewGuid(), kart.Surum, sonOdeme, -1m, "Negatif"))).Durum);
        Assert.Equal(HttpStatusCode.BadRequest, (await Dene(c, HttpMethod.Put, yol, new KartEkstreYaz(Guid.NewGuid(), kart.Surum, sonOdeme, 10.001m, "Kuruş altı"))).Durum);
        Assert.Equal(HttpStatusCode.BadRequest, (await Dene(c, HttpMethod.Put, yol, new KartEkstreYaz(Guid.NewGuid(), kart.Surum, ekstre.KesimTarihi.AddDays(-1), 10m, "Kesimden önce"))).Durum);
        Assert.Equal(HttpStatusCode.NotFound, (await Dene(c, HttpMethod.Put, $"/api/takip/kartlar/{kart.Id}/ekstreler/999999", new KartEkstreYaz(Guid.NewGuid(), kart.Surum, sonOdeme, 10m, "Yok"))).Durum);
        Assert.Equal(HttpStatusCode.Conflict, (await Dene(c, HttpMethod.Put, yol, new KartEkstreYaz(Guid.NewGuid(), kart.Surum - 1, sonOdeme, 10m, "Eski sürüm"))).Durum);

        // Asgari boşaltılınca hedef de kalkar.
        kart = await Gonder<KartTakipDto>(c, HttpMethod.Put, yol, new KartEkstreYaz(Guid.NewGuid(), kart.Surum, sonOdeme, null, "Asgari yok"));
        Assert.Null(kart.Ekstreler.Single(e => e.Id == ekstre.Id).AsgariOdeme); Assert.Null(kart.Ekstreler.Single(e => e.Id == ekstre.Id).AsgariKalan);
    }

    [Fact]
    public async Task Kredi_arsivlenince_taksit_plani_ve_kasa_degismez_eski_surum_reddedilir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await Editor(f);
        var kredi = await Gonder<KrediTakipDto>(c, HttpMethod.Post, "/api/takip/krediler",
            new KrediTakipYaz(Guid.NewGuid(), "Arşiv kredisi", 1200m, Baslangic, Baslangic.AddMonths(1), 12, 100m, [1]));
        var kasa = await Kasa(c);
        var taksitAyi = Baslangic.AddMonths(1); var raporYolu = $"/api/rapor/aylik?yil={taksitAyi.Year}&ay={taksitAyi.Month}";
        var rapor = await c.GetStringAsync(raporYolu);
        var istek = new TakipDurumYaz(Guid.NewGuid(), kredi.Surum, false, "Kapandı, arşive");
        var arsiv = await Gonder<KrediTakipDto>(c, HttpMethod.Post, $"/api/takip/krediler/{kredi.Id}/durum", istek);
        Assert.False(arsiv.Aktif); Assert.Equal(kredi.Surum + 1, arsiv.Surum);
        Assert.Equal(kredi.Taksitler.Select(t => (t.No, t.Tarih, t.Tutar, t.Durum)), arsiv.Taksitler.Select(t => (t.No, t.Tarih, t.Tutar, t.Durum)));
        Assert.Equal(kredi.KalanPlanliOdeme, arsiv.KalanPlanliOdeme);
        Assert.Equal(kasa, await Kasa(c));
        Assert.Equal(rapor, await c.GetStringAsync(raporYolu));

        Assert.Equal(arsiv.Surum, (await Gonder<KrediTakipDto>(c, HttpMethod.Post, $"/api/takip/krediler/{kredi.Id}/durum", istek)).Surum);
        Assert.Equal(HttpStatusCode.Conflict, (await Dene(c, HttpMethod.Post, $"/api/takip/krediler/{kredi.Id}/durum",
            new TakipDurumYaz(Guid.NewGuid(), kredi.Surum, true, "Eski sürüm"))).Durum);
        Assert.Equal(HttpStatusCode.NotFound, (await Dene(c, HttpMethod.Post, "/api/takip/krediler/999999/durum",
            new TakipDurumYaz(Guid.NewGuid(), 1, true, "Yok"))).Durum);
        var geri = await Gonder<KrediTakipDto>(c, HttpMethod.Post, $"/api/takip/krediler/{kredi.Id}/durum", new TakipDurumYaz(Guid.NewGuid(), arsiv.Surum, true, "Geri al"));
        Assert.True(geri.Aktif); Assert.Equal(kasa, await Kasa(c));
    }

    [Fact]
    public async Task Push_anahtari_abonelik_test_iletisi_ve_kaldirma_uclari_calisir()
    {
        await using var f = new BildirimFabrikasi(); using var c = await f.EditorClientAsync();
        var anahtar = await c.GetFromJsonAsync<JsonElement>("/api/bildirimler/push/anahtar");
        Assert.True(anahtar.GetProperty("etkin").GetBoolean()); Assert.Equal(f.AcikAnahtar, anahtar.GetProperty("publicKey").GetString());

        const string Uc = "https://fcm.googleapis.com/fcm/send/kapsam-cihazi";
        var abonelik = await Gonder<JsonElement>(c, HttpMethod.Post, "/api/bildirimler/push/abonelik", Abonelik(Uc, "Kasa masası"));
        var id = abonelik.GetProperty("id").GetInt32(); Assert.Equal("Kasa masası", abonelik.GetProperty("cihazAdi").GetString());
        var cihaz = Assert.Single(await Cihazlar(c)); Assert.Equal(id, cihaz.GetProperty("id").GetInt32()); Assert.True(cihaz.GetProperty("etkin").GetBoolean());

        var test = await Gonder<JsonElement>(c, HttpMethod.Post, "/api/bildirimler/test", new { endpoint = Uc });
        Assert.True(test.GetProperty("basarili").GetBoolean());
        var ileti = Assert.Single(f.Gonderici.Iletiler); Assert.Equal("Emar Kasa", ileti.Baslik); Assert.Equal("kasa-test", ileti.Tag);
        Assert.NotNull((await Cihazlar(c)).Single().GetProperty("sonBasarili").GetString());

        // Gövdeli DELETE aboneliği kapatır (kayıt geçmiş için kalır); kapalı cihaza test iletisi gönderilmez.
        using (var r = await c.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/bildirimler/push/abonelik") { Content = JsonContent.Create(new { endpoint = Uc }) }))
            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.False((await Cihazlar(c)).Single().GetProperty("etkin").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await Dene(c, HttpMethod.Post, "/api/bildirimler/test", new { endpoint = Uc })).Durum);
        Assert.Single(f.Gonderici.Iletiler);

        // Yeniden abone olmak aynı kaydı açar; kimlikle kaldırma kapatır, bilinmeyen kimlik 404.
        Assert.Equal(id, (await Gonder<JsonElement>(c, HttpMethod.Post, "/api/bildirimler/push/abonelik", Abonelik(Uc, "Kasa masası"))).GetProperty("id").GetInt32());
        Assert.True((await Cihazlar(c)).Single().GetProperty("etkin").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/bildirimler/push/abonelikler/{id}")).StatusCode);
        Assert.False((await Cihazlar(c)).Single().GetProperty("etkin").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync("/api/bildirimler/push/abonelikler/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Dene(c, HttpMethod.Post, "/api/bildirimler/push/abonelik", Abonelik("https://ornek.test/push", "Yabancı"))).Durum);
    }

    [Fact]
    public async Task Push_kapaliyken_abonelik_reddedilir_anahtar_bos_doner()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await f.EditorClientAsync();
        var anahtar = await c.GetFromJsonAsync<JsonElement>("/api/bildirimler/push/anahtar");
        Assert.False(anahtar.GetProperty("etkin").GetBoolean()); Assert.Equal(JsonValueKind.Null, anahtar.GetProperty("publicKey").ValueKind);
        var (durum, govde) = await Dene(c, HttpMethod.Post, "/api/bildirimler/push/abonelik", Abonelik("https://fcm.googleapis.com/fcm/send/kapali", "Cihaz"));
        Assert.Equal(HttpStatusCode.BadRequest, durum); Assert.Contains("açık değil", govde);
    }

    [Fact]
    public async Task Bildirim_okundu_isaretlenir_bilinmeyen_kimlik_404()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await f.EditorClientAsync();
        int id;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var bildirim = new BildirimEntity { OlayAnahtari = "kapsam-okundu", Baslik = "Son ödeme", Mesaj = "Kart ödemesi", Tarih = Bugun, Hedef = "/#cards", Tur = "SonOdeme" };
            db.Add(bildirim); db.SaveChanges(); id = bildirim.Id;
        }
        using (var r = await c.PostAsync($"/api/bildirimler/{id}/okundu", null)) Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        using (var scope = f.Services.CreateScope())
            Assert.True(scope.ServiceProvider.GetRequiredService<KasaDbContext>().Set<BildirimEntity>().AsNoTracking().Single(b => b.Id == id).Okundu);
        // Okunan bildirim, kaynağı artık üretmese de listede kalır.
        var liste = await c.GetFromJsonAsync<JsonElement[]>("/api/bildirimler");
        Assert.True(Assert.Single(liste!, b => b.GetProperty("id").GetInt32() == id).GetProperty("okundu").GetBoolean());
        using (var r = await c.PostAsync("/api/bildirimler/999999/okundu", null)) Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("https://indir.ornek.test/Kasa-2.3.0.zip", "https://indir.ornek.test/Kasa-2.3.0.zip")]
    [InlineData("http://indir.ornek.test/Kasa.zip", null)]
    [InlineData("https://kullanici:sifre@indir.ornek.test/Kasa.zip", null)]
    [InlineData("javascript:alert(1)", null)]
    public async Task Surum_kimliksiz_okunur_indirme_adresi_yalniz_guvenli_https(string? ayar, string? beklenen)
    {
        await using var f = new SurumFabrikasi(ayar); using var c = f.CreateClient();
        using var r = await c.GetAsync("/api/surum");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var surum = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Matches(@"^\d+\.\d+\.\d+$", surum.GetProperty("surum").GetString());
        Assert.Matches(@"^\d+\.\d+\.\d+$", surum.GetProperty("minimumIstemci").GetString());
        Assert.False(string.IsNullOrWhiteSpace(surum.GetProperty("notlar").GetString()));
        Assert.Equal(beklenen, surum.GetProperty("indirmeAdresi").GetString());
    }

    [Fact]
    public async Task Alis_belgeleri_sahibine_ve_editore_listelenir_baska_alici_goremez()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var editor = await f.EditorClientAsync();
        using var alici = await Alici(f, editor, "belge-sahibi");
        using var baska = await Alici(f, editor, "belge-baskasi");
        var alis = await Gonder<AlisDto>(alici, HttpMethod.Post, "/api/alis", new AlisYaz(0, Bugun, "Tedarikçi", null, [new("Mal", 100m, [new(1, 100m)])]));
        Assert.Empty((await alici.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{alis.Id}/belgeler"))!);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("%PDF-1.7 fatura"u8.ToArray()), "dosya", "fatura.pdf");
        using var yukle = await alici.PostAsync($"/api/alis/{alis.Id}/belgeler", form);
        Assert.Equal(HttpStatusCode.Created, yukle.StatusCode);
        var belge = (await yukle.Content.ReadFromJsonAsync<BelgeDto>())!;

        foreach (var sahip in new[] { alici, editor })
        {
            var liste = (await sahip.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{alis.Id}/belgeler"))!;
            var satir = Assert.Single(liste);
            Assert.Equal((belge.Id, alis.Id, (int?)null, "fatura.pdf", "application/pdf", 15L), (satir.Id, satir.AlisId, satir.OdemeId, satir.DosyaAdi, satir.IcerikTuru, satir.Boyut));
            Assert.Equal(belge.Yuklendi, satir.Yuklendi);
        }
        // Başka alıcıya alışın varlığı da sızmaz: 403 değil 404.
        Assert.Equal(HttpStatusCode.NotFound, (await baska.GetAsync($"/api/alis/{alis.Id}/belgeler")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync("/api/alis/999999/belgeler")).StatusCode);
    }

    [Fact]
    public async Task Kullanilmayan_kanal_silinir_listeden_kalkar_ikinci_silme_404()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await f.EditorClientAsync();
        using var olustur = await c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("Silinecek kanal", true, 9, 0m));
        Assert.Equal(HttpStatusCode.Created, olustur.StatusCode);
        var kanal = (await olustur.Content.ReadFromJsonAsync<KanalEntity>())!;
        Assert.Contains(await c.GetFromJsonAsync<KanalEntity[]>("/api/kanallar") ?? [], k => k.Id == kanal.Id);

        using (var r = await c.DeleteAsync($"/api/kanallar/{kanal.Id}")) Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.DoesNotContain(await c.GetFromJsonAsync<KanalEntity[]>("/api/kanallar") ?? [], k => k.Id == kanal.Id);
        using (var r = await c.DeleteAsync($"/api/kanallar/{kanal.Id}")) Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    private static async Task<HttpClient> Alici(KasaWebFactory f, HttpClient editor, string kullanici)
    {
        (await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz(kullanici, "Alıcı " + kullanici, "alici-sifre-1"))).EnsureSuccessStatusCode();
        var c = f.CreateClient();
        (await c.PostAsJsonAsync("/api/auth/login", new { kullanici, sifre = "alici-sifre-1" })).EnsureSuccessStatusCode();
        return c;
    }

    private static async Task<JsonElement[]> Cihazlar(HttpClient c) => (await c.GetFromJsonAsync<JsonElement[]>("/api/bildirimler/push/abonelikler"))!;

    private static PushAbonelikYaz Abonelik(string uc, string ad)
    {
        using var anahtar = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = anahtar.ExportParameters(false).Q;
        return new(uc, new(PushDogrulama.Encode([4, .. q.X!, .. q.Y!]), PushDogrulama.Encode(RandomNumberGenerator.GetBytes(16))), ad, Guid.NewGuid());
    }

    /// <summary>Push etkin, anahtarlar yapılandırmadan (dosya yazılmaz), gönderici sahte: ağa çıkılmaz.</summary>
    private sealed class BildirimFabrikasi : KasaWebFactory
    {
        public SahteGonderici Gonderici { get; } = new();
        public string AcikAnahtar { get; }
        private readonly string _gizliAnahtar;
        public BildirimFabrikasi()
        {
            Saat = new SabitSaat(VarsayilanBugun);
            using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var p = ec.ExportParameters(true);
            AcikAnahtar = PushDogrulama.Encode([4, .. p.Q.X!, .. p.Q.Y!]); _gizliAnahtar = PushDogrulama.Encode(p.D!);
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bildirim:PushEtkin"] = "true", ["Bildirim:WorkerEtkin"] = "false",
                ["Bildirim:PublicKey"] = AcikAnahtar, ["Bildirim:PrivateKey"] = _gizliAnahtar,
            }));
            builder.ConfigureServices(s => { s.RemoveAll<IPushGonderici>(); s.AddSingleton<IPushGonderici>(Gonderici); });
        }
    }

    private sealed class SahteGonderici : IPushGonderici
    {
        public List<PushIleti> Iletiler { get; } = [];
        public Task<PushSonuc> Gonder(PushAbonelikEntity abonelik, PushIleti ileti, int ttl, CancellationToken ct)
        {
            lock (Iletiler) Iletiler.Add(ileti);
            return Task.FromResult(PushSonuc.Basarili);
        }
    }

    private sealed class SurumFabrikasi : KasaWebFactory
    {
        private readonly string? _indirme;
        public SurumFabrikasi(string? indirme) { _indirme = indirme; Saat = new SabitSaat(VarsayilanBugun); }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Kasa:IndirmeAdresi"] = _indirme }));
        }
    }
}
