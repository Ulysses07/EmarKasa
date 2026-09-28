using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>appcore-10: görünüm modellerinin tek async yürütme deseni. Önce yürütücünün kendi garantileri (yeniden giriş,
/// eski yanıtın yok sayılması, iptal, hata/zaman aşımı iletisi), sonra eskiden elle korunan ekranların bu garantileri aldığı.</summary>
public class YurutucuTests
{
    private static IslemDto Gider(int id = 5) => new(id, new DateOnly(2026, 9, 23), "Mal", 100, "MEZAT", GiderTipi.Cari, null);

    private sealed class Yuzey : IYurutmeYuzeyi
    {
        public bool Mesgul { get; set; }
        public string? Hata { get; set; }
        public string? Mesaj { get; set; }
        public void IletiyiTemizle() => Mesaj = null;
    }

    // ---- Tekil işlem (yazma, ekran yüklemesi) ----

    [Fact]
    public async Task Tekil_islem_surerken_ikincisi_calismaz_mesgul_ilki_bitince_iner()
    {
        var yuzey = new Yuzey(); var yurutucu = new Yurutucu(yuzey); var bekleyen = new TaskCompletionSource(); var cagri = 0;

        var ilk = yurutucu.YurutAsync(async _ => { cagri++; await bekleyen.Task; });
        Assert.True(yuzey.Mesgul);
        await yurutucu.YurutAsync(_ => { cagri++; return Task.CompletedTask; });

        Assert.Equal(1, cagri); Assert.True(yuzey.Mesgul);
        bekleyen.SetResult(); await ilk;
        Assert.False(yuzey.Mesgul);
        await yurutucu.YurutAsync(_ => { cagri++; return Task.CompletedTask; });
        Assert.Equal(2, cagri);
    }

    [Fact]
    public async Task Tekil_islem_baslarken_onceki_hata_ve_ileti_temizlenir()
    {
        var yuzey = new Yuzey { Hata = "eski hata", Mesaj = "eski ileti" }; var yurutucu = new Yurutucu(yuzey);
        string? gorulenHata = "?", gorulenIleti = "?";

        await yurutucu.YurutAsync(_ => { gorulenHata = yuzey.Hata; gorulenIleti = yuzey.Mesaj; return Task.CompletedTask; });

        Assert.Null(gorulenHata); Assert.Null(gorulenIleti); Assert.False(yuzey.Mesgul);
    }

    [Fact]
    public async Task Gecersiz_kilinan_islemin_sonucu_hatasi_ve_bitisi_yeni_islemi_ezmez()
    {
        var yuzey = new Yuzey(); var yurutucu = new Yurutucu(yuzey); var eski = new TaskCompletionSource(); var yeni = new TaskCompletionSource();
        var ilk = yurutucu.YurutAsync(async n =>
        {
            await eski.Task;
            if (yurutucu.Gecerli(n)) yuzey.Mesaj = "eski sonuç";
            throw new HttpRequestException();
        });

        yurutucu.GecersizKil(); yuzey.Mesgul = false;               // oturum değişimi: ekran sıfırlanır
        var ikinci = yurutucu.YurutAsync(_ => yeni.Task);
        eski.SetResult(); await ilk;

        Assert.Null(yuzey.Mesaj); Assert.Null(yuzey.Hata);
        Assert.True(yuzey.Mesgul);                                  // eski işin bitişi yeni işin göstergesini indirmez
        yeni.SetResult(); await ikinci;
        Assert.False(yuzey.Mesgul);
    }

    [Fact]
    public async Task Yazma_zaman_asimi_sunucuda_tamamlanmis_olabilir_der_yetkisiz_yanit_oturumun_bittigini_soyler()
    {
        var yuzey = new Yuzey(); var yurutucu = new Yurutucu(yuzey);

        await yurutucu.YurutAsync(_ => Task.FromException(new TimeoutException(KasaZamanAsimlari.Ileti)));
        Assert.Contains("tamamlanmış olabilir", yuzey.Hata);
        Assert.False(yuzey.Mesgul);

        await yurutucu.YurutAsync(_ => Task.FromException(new KasaApiException(HttpStatusCode.Unauthorized, "Yetkisiz")));
        Assert.Equal("Oturumunuz sona erdi. Yeniden giriş yapın.", yuzey.Hata);
    }

    // ---- Son istek kazanır (okuma) ----

    [Fact]
    public async Task Son_istek_kazanir_eski_yanit_uygulanmaz_eski_istek_iptal_edilir_gosterge_son_istekle_iner()
    {
        var yuzey = new Yuzey(); var hat = new SonIstekHatti(new Yurutucu(yuzey)); var uygulanan = new List<int>();
        var eski = new TaskCompletionSource<int>(); var yeni = new TaskCompletionSource<int>(); CancellationToken eskiBelirtec = default;

        var ilk = hat.YukleAsync(ct => { eskiBelirtec = ct; return eski.Task; }, uygulanan.Add);
        var ikinci = hat.YukleAsync(_ => yeni.Task, uygulanan.Add);
        Assert.True(eskiBelirtec.IsCancellationRequested);

        eski.SetResult(1); await ilk;
        Assert.Empty(uygulanan); Assert.True(yuzey.Mesgul);
        yeni.SetResult(2); await ikinci;
        Assert.Equal(new[] { 2 }, uygulanan); Assert.False(yuzey.Mesgul); Assert.Null(yuzey.Hata);
    }

    [Fact]
    public async Task Birakilan_istek_iptal_edilir_iptal_hata_sayilmaz_sonucu_uygulanmaz()
    {
        var yuzey = new Yuzey(); var hat = new SonIstekHatti(new Yurutucu(yuzey)); var uygulandi = false;
        var basladi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var istek = hat.YukleAsync(async ct => { basladi.SetResult(); await Task.Delay(Timeout.Infinite, ct); return 1; }, _ => uygulandi = true);
        await basladi.Task;
        hat.Birak();
        await istek;

        Assert.False(uygulandi); Assert.Null(yuzey.Hata);
    }

    [Fact]
    public async Task Okuma_zaman_asimi_yalniz_yeniden_deneme_ister_eski_istegin_hatasi_yazilmaz()
    {
        var yuzey = new Yuzey(); var hat = new SonIstekHatti(new Yurutucu(yuzey));
        await hat.YukleAsync<int>(_ => Task.FromException<int>(new TimeoutException(KasaZamanAsimlari.Ileti)), _ => { });
        Assert.Contains("zamanında yanıt vermedi", yuzey.Hata);
        Assert.DoesNotContain("tamamlanmış olabilir", yuzey.Hata);

        var eski = new TaskCompletionSource<int>();
        var ilk = hat.YukleAsync(_ => eski.Task, _ => { });
        await hat.YukleAsync(_ => Task.FromResult(2), _ => { });
        eski.SetException(new HttpRequestException()); await ilk;
        Assert.Null(yuzey.Hata); Assert.False(yuzey.Mesgul);
    }

    [Fact]
    public void Oturum_degisince_son_istek_bileti_eskir()
    {
        var yurutucu = new Yurutucu(new Yuzey()); var hat = new SonIstekHatti(yurutucu);
        var bilet = hat.Baslat();
        Assert.True(hat.Guncel(bilet));

        yurutucu.GecersizKil();

        Assert.False(hat.Guncel(bilet));
        Assert.True(hat.Guncel(hat.Baslat()));
    }

    // ---- Ekran garantileri: eskiden CalistirAsync + elle 'Mesgul ?' ile korunan İşlemler ekranı ----

    [Fact]
    public async Task Islemler_kayit_surerken_silme_calismaz()
    {
        var bekleyen = new TaskCompletionSource<IslemDto>();
        var api = new SahteApi { IslemKayitYaniti = bekleyen.Task };
        var vm = new IslemlerViewModel(api) { DuzenTutar = 100, DuzenCari = "Mal", DuzenKanal = "MEZAT" };

        var kayit = vm.KaydetCommand.ExecuteAsync(null);
        Assert.True(vm.Mesgul);
        await vm.SilCommand.ExecuteAsync(Gider());

        Assert.Null(api.SonIslemSil);
        bekleyen.SetResult(Gider(1)); await kayit;
        Assert.False(vm.Mesgul);
        await vm.SilCommand.ExecuteAsync(Gider());
        Assert.Equal(5, api.SonIslemSil);
    }

    [Fact]
    public async Task Islemler_oturum_degisince_bekleyen_kaydin_hatasi_yazilmaz_yeni_oturum_beklemez()
    {
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var bekleyen = new TaskCompletionSource<IslemDto>();
        var api = new SahteApi { IslemKayitYaniti = bekleyen.Task };
        var vm = new IslemlerViewModel(api, auth: auth) { DuzenTutar = 100, DuzenCari = "Mal", DuzenKanal = "MEZAT" };

        var kayit = vm.KaydetCommand.ExecuteAsync(null);
        auth.OturumSurumu++;
        Assert.False(vm.Mesgul);

        // Yeni oturumdaki kayıt eski kaydın bitmesini beklemez.
        api.IslemKayitYaniti = null;
        vm.DuzenTutar = 200; vm.DuzenCari = "Yeni"; vm.DuzenKanal = "MEZAT";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.IslemOlusturCagri);
        Assert.Equal("Gider kaydedildi.", vm.Mesaj);

        bekleyen.SetException(new KasaApiException(HttpStatusCode.Unauthorized, "Yetkisiz"));
        await kayit;
        Assert.Null(vm.Hata);
        Assert.Equal("Gider kaydedildi.", vm.Mesaj);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Islemler_oturum_degisince_onceki_oturumun_form_hatasi_kalkar()
    {
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var vm = new IslemlerViewModel(new SahteApi(), auth: auth) { DuzenTutar = ParaAyristirici.Gecersiz };
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);

        auth.OturumSurumu++;

        Assert.Null(vm.Hata);
    }
}
