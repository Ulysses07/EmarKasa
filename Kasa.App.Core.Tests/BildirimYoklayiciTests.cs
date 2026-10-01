using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Masaüstü bildirim yoklayıcısı (tasarım 2026-09-30 masaüstü bildirimleri §1, Test): yeni bildirim bu bilgisayarda yalnız bir kez
/// gösterilir; okunmuş ve bugünden eski bildirim gösterilmez (iptal bilgisi BildirimDto'da yok; sunucu iptal edileni okunmamışsa ve
/// tarayıcıya gitmemişse listelemez); ayar kapalıyken ya da editör oturumu yokken bakılmaz; sunucu hatasında sessizce durulur ve
/// durum satırına yazılır; okunmamış sayısı listedeki bütün okunmamışlardır; tıklama okundu işaretler ve rotayı döndürür.
/// </summary>
public class BildirimYoklayiciTests
{
    private static BildirimDto B(int id, DateOnly? tarih = null, bool okundu = false, string hedef = "/#cards/1")
        => SahteBildirimApi.Bildirim(id, tarih ?? BildirimOrtami.Bugun, okundu, hedef);

    [Fact]
    public async Task Yeni_bildirim_yalniz_bir_kez_gosterilir()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(2), B(1)];
        var ilk = await o.Yoklayici.YoklaAsync(true);
        Assert.Equal([1, 2], o.Gosterici.Gosterilenler.Select(b => b.Id));
        Assert.Equal(new YoklamaSonucu(o.Saat.GetLocalNow(), YoklamaDurumu.Basarili, 2), ilk);
        Assert.Equal("Son kontrol 14:05 · 2 yeni bildirim", ilk!.Metin);
        // Göstermek okundu yapmaz.
        Assert.Empty(o.Api.Okunanlar);
        o.Api.Liste = [B(1), B(2), B(3)];
        await o.Yoklayici.YoklaAsync(true);
        Assert.Equal([1, 2, 3], o.Gosterici.Gosterilenler.Select(b => b.Id));
        Assert.Equal(1, o.Yoklayici.SonSonuc!.YeniSayisi);
        await o.Yoklayici.YoklaAsync(true);
        Assert.Equal(3, o.Gosterici.Gosterilenler.Count);
        Assert.Equal("Son kontrol 14:05 · yeni bildirim yok", o.Yoklayici.SonSonuc!.Metin);
    }

    [Fact]
    public async Task Okunmus_ve_bugunden_eski_bildirim_gosterilmez_okunmamis_sayisi_listenin_tamamidir()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1, okundu: true), B(2, BildirimOrtami.Bugun.AddDays(-1)), B(3)];
        await o.Yoklayici.YoklaAsync(true);
        Assert.Equal([3], o.Gosterici.Gosterilenler.Select(b => b.Id));
        Assert.Equal([3], o.Depo.Kayitli.Order());
        Assert.Equal(2, o.Yoklayici.Okunmamis);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Editor_oturumu_yokken_ya_da_ayar_kapaliyken_bakilmaz(bool editorOturumu, bool ayarAcik)
    {
        var o = new BildirimOrtami();
        o.Ayar.Acik = ayarAcik;
        o.Api.Liste = [B(1)];
        Assert.Null(await o.Yoklayici.YoklaAsync(editorOturumu));
        Assert.Equal(0, o.Api.ListeCagri);
        Assert.Empty(o.Gosterici.Gosterilenler);
        Assert.Null(o.Yoklayici.SonSonuc);
    }

    [Theory]
    [InlineData(0, YoklamaDurumu.SunucuyaUlasilamadi, "Son kontrol 14:05 · sunucuya ulaşılamadı")]
    [InlineData(1, YoklamaDurumu.SunucuHatasi, "Son kontrol 14:05 · sunucu yanıt veremedi")]
    [InlineData(2, YoklamaDurumu.OturumGecersiz, "Son kontrol 14:05 · oturum geçersiz, yeniden giriş yapın")]
    [InlineData(3, YoklamaDurumu.OturumGecersiz, "Son kontrol 14:05 · oturum geçersiz, yeniden giriş yapın")]
    [InlineData(4, YoklamaDurumu.SunucuyaUlasilamadi, "Son kontrol 14:05 · sunucuya ulaşılamadı")]
    public async Task Sunucu_hatasinda_sessizce_durulur_ve_durum_satirina_yazilir(int hata, YoklamaDurumu durum, string metin)
    {
        var o = new BildirimOrtami();
        o.Api.ListeHatasi = hata switch
        {
            0 => new HttpRequestException("bağlantı yok"),
            1 => new KasaApiException(HttpStatusCode.InternalServerError),
            2 => new KasaApiException(HttpStatusCode.Unauthorized),
            3 => new KasaApiException(HttpStatusCode.Forbidden),
            _ => new TimeoutException("süre doldu"),
        };
        o.Yoklayici.OkunmamisBildir(4);
        var sonuc = await o.Yoklayici.YoklaAsync(true);
        Assert.Equal(durum, sonuc!.Durum);
        Assert.Equal(metin, o.Yoklayici.SonSonuc!.Metin);
        Assert.Empty(o.Gosterici.Gosterilenler);
        Assert.Equal(4, o.Yoklayici.Okunmamis);
    }

    [Fact]
    public async Task Yerel_kayit_acilamazsa_gosterilmez_ve_durum_yazilir()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1)];
        o.Depo.Hata = new IOException("kilitli");
        var sonuc = await o.Yoklayici.YoklaAsync(true);
        Assert.Equal(YoklamaDurumu.YerelKayitHatasi, sonuc!.Durum);
        Assert.Equal("Son kontrol 14:05 · bu bilgisayardaki kayıt dosyası açılamadı", sonuc.Metin);
        Assert.Empty(o.Gosterici.Gosterilenler);
        Assert.Equal(1, o.Yoklayici.Okunmamis);
    }

    [Fact]
    public async Task Bir_bildirimin_gosterilememesi_otekileri_durdurmaz()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1), B(2)];
        o.Gosterici.HataliKimlik = 1;
        var sonuc = await o.Yoklayici.YoklaAsync(true);
        Assert.Equal([2], o.Gosterici.Gosterilenler.Select(b => b.Id));
        Assert.Equal(YoklamaDurumu.Basarili, sonuc!.Durum);
    }

    [Fact]
    public async Task Tiklama_okundu_isaretler_ve_rotayi_dondurur()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(5, hedef: "/#loans/7"), B(6)];
        await o.Yoklayici.YoklaAsync(true);
        Assert.Equal(2, o.Yoklayici.Okunmamis);
        Assert.Equal("//krediler?KrediId=7", await o.Yoklayici.TiklandiAsync(new BildirimTiklamasi(5, "/#loans/7"), true));
        Assert.Equal([5], o.Api.Okunanlar);
        Assert.Equal(1, o.Yoklayici.Okunmamis);
        // Deneme bildiriminin kimliği yok: okundu işaretlenmez, Bildirimler açılır.
        Assert.Equal("//bildirimler", await o.Yoklayici.TiklandiAsync(new BildirimTiklamasi(null, BildirimTiklamasi.DenemeHedefi), true));
        // Editör oturumu yok: hiçbir şey yapılmaz.
        Assert.Null(await o.Yoklayici.TiklandiAsync(new BildirimTiklamasi(6, "/#cards/1"), false));
        Assert.Equal([5], o.Api.Okunanlar);
    }

    [Fact]
    public async Task Ayni_bildirime_iki_tiklama_rozeti_bir_dusurur_okunmamis_olmayan_kimlik_dusurmez()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1), B(2), B(3, okundu: true)];
        await o.Yoklayici.YoklaAsync(true);
        Assert.Equal(2, o.Yoklayici.Okunmamis);
        // Aynı tıklama iki yoldan gelebilir (olay ve etkinleştirme argümanı): rozet bir kez düşer.
        await o.Yoklayici.TiklandiAsync(new BildirimTiklamasi(1, "/#cards/1"), true);
        await o.Yoklayici.TiklandiAsync(new BildirimTiklamasi(1, "/#cards/1"), true);
        Assert.Equal(1, o.Yoklayici.Okunmamis);
        // Telefonda zaten okunmuş ya da listede olmayan bildirim rozeti düşürmez; okundu işareti yine gönderilir.
        await o.Yoklayici.TiklandiAsync(new BildirimTiklamasi(3, "/#cards/1"), true);
        await o.Yoklayici.TiklandiAsync(new BildirimTiklamasi(99, "/#cards/1"), true);
        Assert.Equal(1, o.Yoklayici.Okunmamis);
        Assert.Equal([1, 1, 3, 99], o.Api.Okunanlar);
    }

    [Fact]
    public async Task Okundu_isaretlenemezse_rota_yine_doner()
    {
        var o = new BildirimOrtami();
        o.Yoklayici.OkunmamisBildir(3);
        o.Api.OkunduHatasi = new HttpRequestException("bağlantı yok");
        Assert.Equal("//kartlar?KartId=4", await o.Yoklayici.TiklandiAsync(new BildirimTiklamasi(9, "/#cards/4"), true));
        Assert.Equal(3, o.Yoklayici.Okunmamis);
    }

    [Fact]
    public async Task Sifirlama_rozeti_ve_durumu_temizler()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1)];
        await o.Yoklayici.YoklaAsync(true);
        o.Yoklayici.Sifirla();
        Assert.Equal(0, o.Yoklayici.Okunmamis);
        Assert.Null(o.Yoklayici.SonSonuc);
        o.Yoklayici.OkunmamisBildir(-3);
        Assert.Equal(0, o.Yoklayici.Okunmamis);
    }

    [Fact]
    public async Task Surmekte_olan_bakmaya_ikinci_cagri_katilir()
    {
        var o = new BildirimOrtami();
        var yanit = new TaskCompletionSource<IReadOnlyList<BildirimDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        o.Api.Bekleyen = yanit.Task;
        var ilk = o.Yoklayici.YoklaAsync(true);
        var ikinci = o.Yoklayici.YoklaAsync(true);
        Assert.Same(ilk, ikinci);
        Assert.Equal(1, o.Api.ListeCagri);
        yanit.SetResult([B(1)]);
        Assert.Equal(new YoklamaSonucu(o.Saat.GetLocalNow(), YoklamaDurumu.Basarili, 1), await ikinci);
        Assert.Single(o.Gosterici.Gosterilenler);
        // Bakma bitince yeni çağrı yeniden sorar.
        o.Api.Bekleyen = null;
        o.Api.Liste = [B(1)];
        await o.Yoklayici.YoklaAsync(true);
        Assert.Equal(2, o.Api.ListeCagri);
    }

    [Fact]
    public async Task Surerken_sifirlanirsa_eski_bakma_rozeti_ve_durumu_yazmaz()
    {
        var o = new BildirimOrtami();
        var yanit = new TaskCompletionSource<IReadOnlyList<BildirimDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        o.Api.Bekleyen = yanit.Task;
        var eski = o.Yoklayici.YoklaAsync(true);
        o.Yoklayici.Sifirla();
        // Sıfırlamadan sonraki çağrı eski bakmaya katılmaz, yeniden sorar.
        o.Api.Bekleyen = null;
        o.Api.Liste = [B(2)];
        Assert.Equal(YoklamaDurumu.Basarili, (await o.Yoklayici.YoklaAsync(true))!.Durum);
        Assert.Equal(2, o.Api.ListeCagri);
        yanit.SetResult([B(1), B(3), B(4)]);
        Assert.Null(await eski);
        Assert.Equal(1, o.Yoklayici.Okunmamis);
        Assert.Equal([2], o.Gosterici.Gosterilenler.Select(b => b.Id));
    }

    [Fact]
    public async Task Bugun_ve_durum_satiri_yerel_saat_dilimine_goredir()
    {
        // UTC 29.09 22:30 = İstanbul 30.09 01:30: UTC gününe (29.09) göre değil yerel güne göre bakılır.
        var istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var o = new BildirimOrtami(saat: new SabitBildirimSaati(new DateTimeOffset(2026, 9, 29, 22, 30, 0, TimeSpan.Zero), istanbul));
        o.Api.Liste = [B(1, new DateOnly(2026, 9, 30)), B(2, new DateOnly(2026, 9, 29))];
        var sonuc = await o.Yoklayici.YoklaAsync(true);
        Assert.Equal([1], o.Gosterici.Gosterilenler.Select(b => b.Id));
        Assert.Equal("Son kontrol 01:30 · 1 yeni bildirim", sonuc!.Metin);
    }

    [Fact]
    public async Task Windows_ayarinda_kapaliyken_bildirim_harcanmaz_ve_durum_yazilir()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1), B(2, okundu: true)];
        o.Gosterici.WindowsAyarindaKapali = true;
        var sonuc = await o.Yoklayici.YoklaAsync(true);
        Assert.Equal(YoklamaDurumu.WindowsAyarindaKapali, sonuc!.Durum);
        Assert.Equal("Son kontrol 14:05 · Windows ayarlarında bildirimler kapalı", sonuc.Metin);
        Assert.Empty(o.Gosterici.Gosterilenler);
        Assert.Empty(o.Depo.Kayitli);
        Assert.Equal(1, o.Yoklayici.Okunmamis);
        // Windows ayarı açılınca aynı bildirim gösterilir.
        o.Gosterici.WindowsAyarindaKapali = false;
        await o.Yoklayici.YoklaAsync(true);
        Assert.Equal([1], o.Gosterici.Gosterilenler.Select(b => b.Id));
    }

    [Fact]
    public void Tiklama_argumanlari_kimlik_ve_hedefe_cozulur()
    {
        Assert.Equal(new BildirimTiklamasi(12, "/#cards/3"),
            BildirimTiklamasi.Coz(new Dictionary<string, string> { ["bildirim"] = "12", ["hedef"] = "/#cards/3" }));
        Assert.Equal(new BildirimTiklamasi(null, "/#notifications"),
            BildirimTiklamasi.Coz(new Dictionary<string, string> { ["hedef"] = "/#notifications" }));
        Assert.Equal(new BildirimTiklamasi(null, null),
            BildirimTiklamasi.Coz(new Dictionary<string, string> { ["bildirim"] = "-1", ["baska"] = "x" }));
    }

    [Fact]
    public void Bekleyen_tiklama_bir_kez_alinir_son_tiklama_gecerlidir()
    {
        var tiklamalar = new BildirimTiklamalari();
        var olay = 0;
        tiklamalar.Istendi += (_, _) => olay++;
        tiklamalar.Ekle(new BildirimTiklamasi(1, "/#cards/1"));
        tiklamalar.Ekle(new BildirimTiklamasi(2, "/#loans/2"));
        Assert.Equal(2, olay);
        Assert.Equal(new BildirimTiklamasi(2, "/#loans/2"), tiklamalar.Al());
        Assert.Null(tiklamalar.Al());
    }
}
