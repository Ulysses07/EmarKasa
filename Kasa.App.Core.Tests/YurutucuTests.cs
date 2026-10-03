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
        public bool BaglantiKopuk => false;
    }

    // ---- Tekil işlem (yazma, ekran yüklemesi) ----

    [Fact]
    public async Task Tekil_islem_surerken_ikincisi_calismaz_mesgul_ilki_bitince_iner()
    {
        var yuzey = new Yuzey();
        var yurutucu = new Yurutucu(yuzey);
        var bekleyen = new TaskCompletionSource();
        var cagri = 0;

        var ilk = yurutucu.YurutAsync(async _ => { cagri++; await bekleyen.Task; });
        Assert.True(yuzey.Mesgul);
        await yurutucu.YurutAsync(_ => { cagri++; return Task.CompletedTask; });

        Assert.Equal(1, cagri);
        Assert.True(yuzey.Mesgul);
        bekleyen.SetResult();
        await ilk;
        Assert.False(yuzey.Mesgul);
        await yurutucu.YurutAsync(_ => { cagri++; return Task.CompletedTask; });
        Assert.Equal(2, cagri);
    }

    [Fact]
    public async Task Tekil_islem_baslarken_onceki_hata_ve_ileti_temizlenir()
    {
        var yuzey = new Yuzey { Hata = "eski hata", Mesaj = "eski ileti" };
        var yurutucu = new Yurutucu(yuzey);
        string? gorulenHata = "?", gorulenIleti = "?";

        await yurutucu.YurutAsync(_ => { gorulenHata = yuzey.Hata; gorulenIleti = yuzey.Mesaj; return Task.CompletedTask; });

        Assert.Null(gorulenHata);
        Assert.Null(gorulenIleti);
        Assert.False(yuzey.Mesgul);
    }

    [Fact]
    public async Task Gecersiz_kilinan_islemin_sonucu_hatasi_ve_bitisi_yeni_islemi_ezmez()
    {
        var yuzey = new Yuzey();
        var yurutucu = new Yurutucu(yuzey);
        var eski = new TaskCompletionSource();
        var yeni = new TaskCompletionSource();
        var ilk = yurutucu.YurutAsync(async n =>
        {
            await eski.Task;
            if (yurutucu.Gecerli(n))
                yuzey.Mesaj = "eski sonuç";
            throw new HttpRequestException();
        });

        yurutucu.GecersizKil();
        yuzey.Mesgul = false;               // oturum değişimi: ekran sıfırlanır
        var ikinci = yurutucu.YurutAsync(_ => yeni.Task);
        eski.SetResult();
        await ilk;

        Assert.Null(yuzey.Mesaj);
        Assert.Null(yuzey.Hata);
        Assert.True(yuzey.Mesgul);                                  // eski işin bitişi yeni işin göstergesini indirmez
        yeni.SetResult();
        await ikinci;
        Assert.False(yuzey.Mesgul);
    }

    [Fact]
    public async Task Yazma_zaman_asimi_sunucuda_tamamlanmis_olabilir_der_yetkisiz_yanit_oturumun_bittigini_soyler()
    {
        var yuzey = new Yuzey();
        var yurutucu = new Yurutucu(yuzey);

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
        var yuzey = new Yuzey();
        var hat = new SonIstekHatti(new Yurutucu(yuzey));
        var uygulanan = new List<int>();
        var eski = new TaskCompletionSource<int>();
        var yeni = new TaskCompletionSource<int>();
        CancellationToken eskiBelirtec = default;

        var ilk = hat.YukleAsync(ct => { eskiBelirtec = ct; return eski.Task; }, uygulanan.Add);
        var ikinci = hat.YukleAsync(_ => yeni.Task, uygulanan.Add);
        Assert.True(eskiBelirtec.IsCancellationRequested);

        eski.SetResult(1);
        await ilk;
        Assert.Empty(uygulanan);
        Assert.True(yuzey.Mesgul);
        yeni.SetResult(2);
        await ikinci;
        Assert.Equal(new[] { 2 }, uygulanan);
        Assert.False(yuzey.Mesgul);
        Assert.Null(yuzey.Hata);
    }

    [Fact]
    public async Task Birakilan_istek_iptal_edilir_iptal_hata_sayilmaz_sonucu_uygulanmaz()
    {
        var yuzey = new Yuzey();
        var hat = new SonIstekHatti(new Yurutucu(yuzey));
        var uygulandi = false;
        var basladi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var istek = hat.YukleAsync(async ct => { basladi.SetResult(); await Task.Delay(Timeout.Infinite, ct); return 1; }, _ => uygulandi = true);
        await basladi.Task;
        hat.Birak();
        await istek;

        Assert.False(uygulandi);
        Assert.Null(yuzey.Hata);
    }

    [Fact]
    public async Task Okuma_zaman_asimi_yalniz_yeniden_deneme_ister_eski_istegin_hatasi_yazilmaz()
    {
        var yuzey = new Yuzey();
        var hat = new SonIstekHatti(new Yurutucu(yuzey));
        await hat.YukleAsync<int>(_ => Task.FromException<int>(new TimeoutException(KasaZamanAsimlari.Ileti)), _ => { });
        Assert.Contains("zamanında yanıt vermedi", yuzey.Hata);
        Assert.DoesNotContain("tamamlanmış olabilir", yuzey.Hata);

        var eski = new TaskCompletionSource<int>();
        var ilk = hat.YukleAsync(_ => eski.Task, _ => { });
        await hat.YukleAsync(_ => Task.FromResult(2), _ => { });
        eski.SetException(new HttpRequestException());
        await ilk;
        Assert.Null(yuzey.Hata);
        Assert.False(yuzey.Mesgul);
    }

    [Fact]
    public void Oturum_degisince_son_istek_bileti_eskir()
    {
        var yurutucu = new Yurutucu(new Yuzey());
        var hat = new SonIstekHatti(yurutucu);
        var bilet = hat.Baslat();
        Assert.True(hat.Guncel(bilet));

        yurutucu.GecersizKil();

        Assert.False(hat.Guncel(bilet));
        Assert.True(hat.Guncel(hat.Baslat()));
    }

    // ---- Tekil işlem ile yüzeydeki okuma (SonIstekHatti.YukleAsync) aynı göstergeyi paylaşır ----

    [Fact]
    public async Task Okuma_surerken_baslayan_tekil_islem_sessizce_engellenmez_okumayi_eskitir_ve_iptal_eder()
    {
        var yuzey = new Yuzey();
        var yurutucu = new Yurutucu(yuzey);
        var hat = new SonIstekHatti(yurutucu);
        var okuma = new TaskCompletionSource<int>();
        CancellationToken belirtec = default;
        var uygulanan = new List<int>();
        var yukleme = hat.YukleAsync(ct => { belirtec = ct; return okuma.Task; }, uygulanan.Add);
        Assert.True(yuzey.Mesgul);

        var yazma = new TaskCompletionSource();
        var cagri = 0;
        var islem = yurutucu.YurutAsync(async _ => { cagri++; await yazma.Task; });

        Assert.Equal(1, cagri);
        Assert.True(belirtec.IsCancellationRequested);              // okuma ağda da bırakılır
        okuma.SetResult(1);
        await yukleme;
        Assert.Empty(uygulanan);                                    // eskiyen okumanın sonucu uygulanmaz
        Assert.True(yuzey.Mesgul);                                  // bitişi yazmanın göstergesini indirmez
        yazma.SetResult();
        await islem;
        Assert.False(yuzey.Mesgul);
        Assert.Null(yuzey.Hata);
    }

    [Fact]
    public async Task Tekil_islem_surerken_baslayan_okuma_yazmanin_iletisini_silmez_gosterge_ikisi_de_bitince_iner()
    {
        var yuzey = new Yuzey();
        var yurutucu = new Yurutucu(yuzey);
        var hat = new SonIstekHatti(yurutucu);
        var yazma = new TaskCompletionSource();
        var uygulanan = new List<int>();
        var cagri = 0;
        var islem = yurutucu.YurutAsync(async _ => { yuzey.Mesaj = "kaydedildi"; await yazma.Task; });

        // Önce biten okuma: sonucu uygulanır; yazmanın iletisi ve göstergesi kalır, yazma sürdüğü için ikinci tekil işlem yapılmaz.
        await hat.YukleAsync(_ => Task.FromResult(1), uygulanan.Add);
        Assert.Equal(new[] { 1 }, uygulanan);
        Assert.Equal("kaydedildi", yuzey.Mesaj);
        Assert.True(yuzey.Mesgul);
        await yurutucu.YurutAsync(_ => { cagri++; return Task.CompletedTask; });
        Assert.Equal(0, cagri);

        // Sonra biten okuma: yazma bitince gösterge okumada kalır, okuma bitince iner.
        var okuma = new TaskCompletionSource<int>();
        var yukleme = hat.YukleAsync(_ => okuma.Task, uygulanan.Add);
        yazma.SetResult();
        await islem;
        Assert.True(yuzey.Mesgul);
        Assert.Equal("kaydedildi", yuzey.Mesaj);
        okuma.SetResult(2);
        await yukleme;
        Assert.Equal(new[] { 1, 2 }, uygulanan);
        Assert.False(yuzey.Mesgul);
        Assert.Null(yuzey.Hata);
    }

    // ---- Ekran garantileri: eskiden CalistirAsync + elle 'Mesgul ?' ile korunan İşlemler ekranı ----

    [Fact]
    public async Task Islemler_kayit_surerken_silme_calismaz()
    {
        var bekleyen = new TaskCompletionSource<IslemDto>();
        var api = new SahteApi { IslemKayitYaniti = bekleyen.Task };
        var vm = new IslemlerViewModel(api, TestOturumu.Ac()) { DuzenTutar = 100, DuzenCari = "Mal", DuzenKanal = "MEZAT" };

        var kayit = vm.KaydetCommand.ExecuteAsync(null);
        Assert.True(vm.Mesgul);
        await vm.SilCommand.ExecuteAsync(Gider());

        Assert.Null(api.SonIslemSil);
        // Silme onay diyaloğundan sonra gelir: sessizce yok sayılmaz, yapılmadığı söylenir; kaydın bitişi iletiyi silmez.
        Assert.Equal(Yurutucu.SurenIslemIletisi, vm.Hata);
        bekleyen.SetResult(Gider(1));
        await kayit;
        Assert.False(vm.Mesgul);
        Assert.Equal(Yurutucu.SurenIslemIletisi, vm.Hata);
        await vm.SilCommand.ExecuteAsync(Gider());
        Assert.Equal(5, api.SonIslemSil);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Mesgul_iken_bildirilen_tekil_islem_calismaz_ve_yapilmadigini_soyler()
    {
        var yuzey = new Yuzey();
        var yurutucu = new Yurutucu(yuzey);
        var bekleyen = new TaskCompletionSource();
        var cagri = 0;
        var ilk = yurutucu.YurutAsync(_ => bekleyen.Task);

        await yurutucu.YurutAsync(_ => { cagri++; return Task.CompletedTask; });
        Assert.Null(yuzey.Hata);                                    // çift tıklama: sessiz
        await yurutucu.YurutAsync(_ => { cagri++; return Task.CompletedTask; }, mesgulkenBildir: true);
        Assert.Equal(Yurutucu.SurenIslemIletisi, yuzey.Hata);

        Assert.Equal(0, cagri);
        bekleyen.SetResult();
        await ilk;
        Assert.False(yuzey.Mesgul);
    }

    [Fact]
    public async Task Islemler_gelir_kaydindan_sonraki_yukleme_surerken_oturum_degisirse_kayit_iletisi_yeni_oturuma_yazilmaz()
    {
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var hafta = new DonemDto(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 2026, 9);
        var api = new SahteApi { KanallarListe = new[] { new KanalDto(1, "MEZAT", true, 0, 0m) }, DonemlerListe = new[] { hafta } };
        var vm = new IslemlerViewModel(api, auth: auth, zaman: new IslemEditorTests.SabitZaman(new DateOnly(2026, 9, 23)));
        await vm.YukleAsync();
        vm.SecGelenKanalCommand.Execute(vm.GelenKanallari.First(c => c.Ad == "MEZAT"));
        vm.GelenTutar = 5000m;
        var yeniden = new TaskCompletionSource<IReadOnlyList<GelenDto>>();
        api.GelenlerGetir = _ => yeniden.Task;

        var kayit = vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal(1, api.GelenKaydetCagri);
        auth.OturumSurumu++;
        var yeniOturumBilgisi = vm.GelenBilgi;
        yeniden.SetResult(new[] { new GelenDto(3, hafta.Start, "MEZAT", 5000m, KanalId: 1) });
        await kayit;

        Assert.Equal(yeniOturumBilgisi, vm.GelenBilgi);
        Assert.DoesNotContain("kaydedildi", vm.GelenBilgi ?? "");
        Assert.Null(vm.Hata);
        Assert.False(vm.Mesgul);
    }

    // ---- Alışlar: oturum değişimini sayfa kod-arkası olmadan model kendisi alır ----

    private static AlisDto Alis() => new(
        7, 2, 3, "Ayşe", new(2026, 9, 21), "Tedarikçi", null, "Taslak", null, 100m, 0m, 100m,
        new[] { new AlisKalemDto(1, "Mal alımı", 100m, new[] { new AlisDagilimDto(1, "MEZAT", 100m) }) },
        Array.Empty<AlisOdemeDto>());

    [Fact]
    public async Task Alislar_oturum_degisimini_sayfa_olmadan_alir_onceki_editorun_verisi_ve_bekleyen_yanit_yansimaz()
    {
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var api = new AlislarViewModelTests.SahteAlisApi { Liste = new[] { Alis() }, Giderler = new[] { Gider(9) } };
        var vm = new AlislarViewModel(api, new SahteApi(), auth: auth);
        Assert.True(vm.EditorMu);                                   // rol oturumdan gelir
        await vm.YukleAsync();
        Assert.Single(vm.Alislar);
        Assert.NotEmpty(vm.Kanallar);
        Assert.NotEmpty(vm.BaglanabilirGiderler);

        var bekleyen = new TaskCompletionSource<IReadOnlyList<AlisDto>>();
        api.ListeGetir = () => bekleyen.Task;
        var yukleme = vm.YenileCommand.ExecuteAsync(null);
        Assert.True(vm.Mesgul);

        auth.AktifRol = Rol.Alici;
        auth.OturumSurumu++;

        Assert.False(vm.Mesgul);
        Assert.False(vm.EditorMu);
        Assert.False(vm.VeriHazir);
        Assert.Empty(vm.Alislar);
        Assert.Empty(vm.Kanallar);
        Assert.Empty(vm.BaglanabilirGiderler);
        Assert.Empty(vm.OdemeKartlari);
        Assert.Empty(vm.Alicilar);
        bekleyen.SetResult(new[] { Alis() });
        await yukleme;
        Assert.Empty(vm.Alislar);
        Assert.False(vm.VeriHazir);
        Assert.False(vm.Mesgul);
        Assert.Null(vm.Hata);

        // Yeni oturumda yükleme çalışır.
        api.ListeGetir = null;
        await vm.YukleAsync();
        Assert.Single(vm.Alislar);
        Assert.True(vm.VeriHazir);
    }

    [Fact]
    public async Task Alislar_oturum_degisince_bekleyen_yazmanin_hatasi_yeni_oturuma_yazilmaz()
    {
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var api = new AlislarViewModelTests.SahteAlisApi { Liste = new[] { Alis() } };
        var vm = new AlislarViewModel(api, new SahteApi(), auth: auth);
        await vm.YukleAsync();
        var bekleyen = new TaskCompletionSource<IReadOnlyList<AlisDto>>();
        api.ListeGetir = () => bekleyen.Task;
        var yukleme = vm.YukleAsync();

        auth.OturumSurumu++;
        bekleyen.SetException(new KasaApiException(HttpStatusCode.Unauthorized, "Yetkisiz"));
        await yukleme;

        Assert.Null(vm.Hata);
        Assert.False(vm.Mesgul);
    }

    // ---- Ayarlar: yükleme ve kayıtlar yürütücünün tekil işlemidir, oturum değişimini model kendisi alır ----

    [Fact]
    public async Task Ayarlar_kanal_silme_baska_islem_surerken_yapilmaz_ve_yapilmadigi_soylenir()
    {
        var kanallar = new TaskCompletionSource<IReadOnlyList<KanalDto>>();
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false), KanallarGetir = () => kanallar.Task };
        var vm = new AyarlarViewModel(api, TestOturumu.Ac());
        var kanal = new KanalDto(4, "PERAKENDE", true, 1, 0m);
        var yukleme = vm.YukleAsync();

        // Silme onay diyaloğundan sonra gelir: sessizce yok sayılmaz, yüklemenin bitişi iletiyi silmez.
        await vm.KanalSilCommand.ExecuteAsync(kanal);
        Assert.Null(api.SonKanalSil);
        Assert.Equal(Yurutucu.SurenIslemIletisi, vm.Hata);
        kanallar.SetResult(new[] { kanal });
        await yukleme;
        Assert.Equal(Yurutucu.SurenIslemIletisi, vm.Hata);

        api.KanallarGetir = null;
        await vm.KanalSilCommand.ExecuteAsync(kanal);
        Assert.Equal(4, api.SonKanalSil);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Ayarlar_oturum_degisince_bekleyen_yukleme_yansimaz_onceki_oturumun_ayarlari_formlari_ve_izleyici_sifresi_kalkar()
    {
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 15000m, true, IzleyiciSifreKisa: true, VekilUyarisi: "Vekil ayarı hatalı."),
            KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 0, 5000m) },
        };
        var vm = new AyarlarViewModel(api, auth: auth);
        await vm.YukleAsync();
        vm.KanalDuzenle(vm.Kanallar[0]);
        vm.DuzenKanalAcilisDevri = 0m;
        await vm.KanalKaydetCommand.ExecuteAsync(null);             // sıfır onayı bekliyor
        Assert.NotNull(vm.KanalUyarisi);
        vm.YeniIzleyiciSifre = "onceki-editorun-sifresi";
        var kanallar = new TaskCompletionSource<IReadOnlyList<KanalDto>>();
        api.KanallarGetir = () => kanallar.Task;
        var yukleme = vm.YukleAsync();
        Assert.True(vm.Mesgul);

        auth.OturumSurumu++;

        Assert.False(vm.Mesgul);
        Assert.Null(vm.Hata);
        Assert.Empty(vm.Kanallar);
        Assert.Equal(0m, vm.KasaAcilisDevri);
        Assert.Equal((0, "", 0m), (vm.DuzenKanalId, vm.DuzenKanalAd, vm.DuzenKanalAcilisDevri));
        Assert.Null(vm.KanalUyarisi);
        Assert.Equal("", vm.YeniIzleyiciSifre);
        Assert.Null(vm.IzleyiciSifreUyarisi);
        Assert.Null(vm.VekilUyarisi);
        kanallar.SetResult(new[] { new KanalDto(1, "MEZAT", true, 0, 5000m) });
        await yukleme;
        Assert.Empty(vm.Kanallar);
        Assert.False(vm.Mesgul);
        Assert.Null(vm.Hata);

        // Yeni oturumun yüklemesi eskisini beklemez; kanalın sıfır onayı önceki oturumdan taşınmaz.
        api.KanallarGetir = null;
        await vm.YukleAsync();
        Assert.Single(vm.Kanallar);
        Assert.Equal(15000m, vm.KasaAcilisDevri);
        vm.KanalDuzenle(vm.Kanallar[0]);
        vm.DuzenKanalAcilisDevri = 0m;
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonKanalGuncelle);
        Assert.NotNull(vm.KanalUyarisi);
    }

    [Fact]
    public async Task Ayarlar_oturum_degisince_bekleyen_kaydin_hatasi_ve_izleyici_sifresinin_sonucu_yeni_oturuma_yazilmaz()
    {
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var ayar = new TaskCompletionSource();
        var sifre = new TaskCompletionSource();
        var api = new SahteApi { AyarGuncelleYaniti = ayar.Task, IzleyiciSifreYaniti = sifre.Task, AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false) };
        var vm = new AyarlarViewModel(api, auth: auth);
        await vm.YukleAsync();
        vm.KasaAcilisDevri = 100m;
        var kayit = vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.NotNull(api.SonAyar);

        auth.OturumSurumu++;
        Assert.False(vm.Mesgul);
        // Yeni oturumdaki kayıt eskisinin bitmesini beklemez.
        vm.YeniIzleyiciSifre = "yeni-oturumun-sifresi";
        var sifreKaydi = vm.IzleyiciSifreKaydetCommand.ExecuteAsync(null);
        Assert.Equal("yeni-oturumun-sifresi", api.SonIzleyiciSifre);
        auth.OturumSurumu++;

        ayar.SetException(new KasaApiException(HttpStatusCode.Unauthorized, "Yetkisiz"));
        await kayit;
        sifre.SetResult();
        await sifreKaydi;
        Assert.Null(vm.Hata);
        Assert.Null(vm.IzleyiciSifreHatasi);
        Assert.Null(vm.IzleyiciSifreMesaji);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Ayarlar_yukleme_surerken_ayar_ve_kanal_kaydi_gonderilmez()
    {
        var kanallar = new TaskCompletionSource<IReadOnlyList<KanalDto>>();
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false), KanallarGetir = () => kanallar.Task };
        var vm = new AyarlarViewModel(api, TestOturumu.Ac());

        var yukleme = vm.YukleAsync();
        Assert.True(vm.Mesgul);
        vm.DuzenKanalAd = "YENİ";
        var ayarKaydi = vm.AyarKaydetCommand.ExecuteAsync(null);
        var kanalKaydi = vm.KanalKaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonAyar);
        Assert.Null(api.SonKanalOlustur);
        kanallar.SetResult(new[] { new KanalDto(1, "MEZAT", true, 0, 0m) });
        await yukleme;
        await ayarKaydi;
        await kanalKaydi;
        Assert.Null(api.SonAyar);
        Assert.Null(api.SonKanalOlustur);
        Assert.False(vm.Mesgul);
        api.KanallarGetir = null;
        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.NotNull(api.SonAyar);
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
        vm.DuzenTutar = 200;
        vm.DuzenCari = "Yeni";
        vm.DuzenKanal = "MEZAT";
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
