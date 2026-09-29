using System.Net;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>Sürüm, yedek, şifre değişimi ve kurtarma; durum kodu sözleşmesi (201/204/400/401/404/409/429) ve istemcinin
/// sunucu iletilerini okuyabilmesi.</summary>
public class YonetimVeDurumKoduSozlesmeTests : SozlesmeTemeli
{
    [Fact]
    [SozlesmeKapsami(nameof(IYonetimApi.SurumAsync), nameof(IYonetimApi.YedekIndirAsync), nameof(IYonetimApi.YedekDurumuAsync))]
    public async Task Surum_kimliksiz_okunur_yedek_indirilir_durumu_okunur()
    {
        var surum = await Istemci().Yonetim.SurumAsync();
        Assert.Matches(@"^\d+\.\d+\.\d+$", surum.Surum); Assert.Matches(@"^\d+\.\d+\.\d+$", surum.MinimumIstemci); Assert.Null(surum.IndirmeAdresi);
        var o = await Editor();
        var once = await o.Yonetim.YedekDurumuAsync();
        Assert.False(once.OtomatikEtkin); Assert.Null(once.SonYedek);
        using var zip = new MemoryStream();
        var yedek = await o.Yonetim.YedekIndirAsync(zip);
        Assert.Equal("application/zip", yedek.IcerikTuru); Assert.EndsWith(".zip", yedek.DosyaAdi); Assert.Equal(zip.Length, yedek.Boyut);
        using (var arsiv = new System.IO.Compression.ZipArchive(new MemoryStream(zip.ToArray())))
            Assert.NotNull(arsiv.GetEntry("kasa.db"));
        var sonra = await o.Yonetim.YedekDurumuAsync();
        Assert.NotNull(sonra.SonYedek); Assert.NotNull(sonra.SonDogrulama); Assert.Null(sonra.Hata);
    }

    [Fact]
    [SozlesmeKapsami(nameof(IYonetimApi.SifreDegistirAsync), nameof(IYonetimApi.KurtarmaKoduOlusturAsync), nameof(IYonetimApi.SifreKurtarAsync), nameof(IKasaApi.KanallarAsync))]
    public async Task Sifre_degisimi_oturumlari_kapatir_istemci_olay_yayar_kurtarma_kodu_yeni_sifre_verir()
    {
        var o = await Editor();
        var baska = await Editor();
        var olaylar = new List<OturumSonuNedeni>(); var baskaOlaylar = new List<OturumSonuNedeni>();
        o.Istemci.OturumSonlandi += (_, e) => olaylar.Add(((OturumSonlandiEventArgs)e).Neden);
        baska.Istemci.OturumSonlandi += (_, e) => baskaOlaylar.Add(((OturumSonlandiEventArgs)e).Neden);

        await o.Yonetim.SifreDegistirAsync(new SifreDegistirYaz(SozlesmeFabrikasi.EditorSifresi, "yeni-editor-sifresi-1"));
        Assert.Equal(HttpStatusCode.NoContent, o.SonYanit.Durum);
        Assert.Equal([OturumSonuNedeni.SifreDegisti], olaylar); Assert.Null(await o.Depo.OkuAsync());
        // Eski şifreyle açılmış başka oturumun ilk isteği 401 alır; istemci token'ı siler ve olayı yayar.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Assert.ThrowsAsync<KasaApiException>(() => baska.Kasa.KanallarAsync())).DurumKodu);
        Assert.Equal([OturumSonuNedeni.OturumGecersiz], baskaOlaylar); Assert.Null(await baska.Depo.OkuAsync());

        await o.Kasa.LoginAsync("editor", "yeni-editor-sifresi-1");
        var kod = await o.Yonetim.KurtarmaKoduOlusturAsync("yeni-editor-sifresi-1");
        Assert.Matches("^[0-9A-F]{48}$", kod.Kod);
        var kurtaran = Istemci();
        await kurtaran.Yonetim.SifreKurtarAsync(new SifreKurtarYaz("editor", kod.Kod, "kurtarilmis-sifre-1"));
        Assert.Equal(HttpStatusCode.NoContent, kurtaran.SonYanit.Durum);
        Assert.Equal("editor", (await kurtaran.Kasa.LoginAsync("editor", "kurtarilmis-sifre-1")).Rol);
    }

    [Fact]
    [SozlesmeKapsami(nameof(IKasaApi.KanalOlusturAsync), nameof(IKasaApi.IslemOlusturAsync), nameof(IKasaApi.IslemSilAsync), nameof(IFinansTakipApi.TakipKartAsync),
        nameof(IFinansTakipApi.TakipKartKaydetAsync))]
    public async Task Hata_durum_kodlari_ve_iletileri_istemciye_ulasir()
    {
        var o = await Editor();
        // 409: gövdedeki 'hata' iletisi istemcinin istisnasına taşınır.
        var cakisma = await Assert.ThrowsAsync<KasaApiException>(() => o.Kasa.KanalOlusturAsync(new KanalYaz("mezat", true, 9, 0m)));
        Assert.Equal((HttpStatusCode.Conflict, "Bu kanal adı zaten kullanılıyor."), (cakisma.DurumKodu, cakisma.Message));
        // 400 (doğrulama sorunu, 'errors' sözlüğü): alan iletileri okunur.
        var gecersiz = await Assert.ThrowsAsync<KasaApiException>(() => o.Kasa.IslemOlusturAsync(new IslemYaz(Bugun, "Kuruş altı", 10.001m, "MEZAT", GiderTipi.Cari, null)));
        Assert.Equal(HttpStatusCode.BadRequest, gecersiz.DurumKodu);
        // 400 (takip hatası, 'hata' gövdesi).
        var gun = await Assert.ThrowsAsync<KasaApiException>(() => o.Takip.TakipKartKaydetAsync(null, new KartTakipYaz(Guid.NewGuid(), 0, "Kart", 1000m, 32, 25, Bugun, 0m, [])));
        Assert.Equal((HttpStatusCode.BadRequest, "Gün 1–31 olmalı."), (gun.DurumKodu, gun.Message));
        // 404: gövdesiz ya da iletisiz; istemci genel iletiyi verir.
        var yok = await Assert.ThrowsAsync<KasaApiException>(() => o.Takip.TakipKartAsync(999_999));
        Assert.Equal(HttpStatusCode.NotFound, yok.DurumKodu);
        var silinemez = await Assert.ThrowsAsync<KasaApiException>(() => o.Kasa.IslemSilAsync(999_999));
        Assert.Equal(HttpStatusCode.NotFound, silinemez.DurumKodu);
    }

    [Fact]
    [SozlesmeKapsami(nameof(IYonetimApi.KurtarmaKoduOlusturAsync))]
    public async Task Hiz_siniri_429_ve_bekleme_iletisi_istemciye_ulasir()
    {
        var f = Fabrika(new() { ["Kasa:HizSiniri:GuvenlikIzni"] = "1" });
        var o = await Editor(f);
        await o.Yonetim.KurtarmaKoduOlusturAsync(SozlesmeFabrikasi.EditorSifresi);
        var hata = await Assert.ThrowsAsync<KasaApiException>(() => o.Yonetim.KurtarmaKoduOlusturAsync(SozlesmeFabrikasi.EditorSifresi));
        Assert.Equal(HttpStatusCode.TooManyRequests, hata.DurumKodu);
        Assert.Matches(@"^Çok fazla deneme yapıldı\. \d+ dakika sonra yeniden deneyin\.$", hata.Message);
    }
}
