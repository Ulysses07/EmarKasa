using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class AyarlarViewModelTests
{
    [Fact]
    public async Task Yukle_ayar_ve_kanallari_doldurur()
    {
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 15000m, true),
            KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 0, 0m) },
        };
        var vm = new AyarlarViewModel(api, TestOturumu.Ac());

        await vm.YukleAsync();

        Assert.Equal(15000m, vm.KasaAcilisDevri);
        Assert.Single(vm.Kanallar);
    }

    [Fact]
    public async Task Yeni_kanal_olustur_cagirir()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false) };
        var vm = new AyarlarViewModel(api, TestOturumu.Ac()) { DuzenKanalAd = "TOPTAN", DuzenKanalSira = 3 };

        await vm.KanalKaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonKanalOlustur);
        Assert.Equal("TOPTAN", api.SonKanalOlustur!.Ad);
        Assert.Equal(3, api.SonKanalOlustur!.Sira);
    }

    [Fact]
    public async Task Kanal_sil_cagirir()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false) };
        var vm = new AyarlarViewModel(api, TestOturumu.Ac());

        await vm.KanalSilCommand.ExecuteAsync(new KanalDto(4, "PERAKENDE", true, 1, 0m));

        Assert.Equal(4, api.SonKanalSil);
    }

    [Fact]
    public async Task Izleyici_sifre_kaydet_cagirir()
    {
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api, TestOturumu.Ac()) { YeniIzleyiciSifre = "gizli-izleyici-123" };

        await vm.IzleyiciSifreKaydetCommand.ExecuteAsync(null);

        Assert.Equal("gizli-izleyici-123", api.SonIzleyiciSifre);
        Assert.Equal("", vm.YeniIzleyiciSifre);
    }

    [Fact]
    public async Task Ayar_kaydet_cagirir()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false) };
        var vm = new AyarlarViewModel(api, TestOturumu.Ac());
        await vm.YukleAsync();
        vm.TakipBaslangic = new DateTime(2026, 2, 1);
        vm.KasaAcilisDevri = 20000m;

        await vm.AyarKaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonAyar);
        Assert.Equal(new DateOnly(2026, 2, 1), api.SonAyar!.TakipBaslangic);
        Assert.Equal(20000m, api.SonAyar!.KasaAcilisDevri);
    }

    [Fact]
    public async Task Kayitli_kasa_acilis_devrini_sifira_indirmek_ikinci_basista_onaylanir()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 15000m, true), KanallarListe = new List<KanalDto>() };
        var vm = new AyarlarViewModel(api, TestOturumu.Ac());
        await vm.YukleAsync();
        vm.KasaAcilisDevri = 0m;                                   // alan boşaltılmış olabilir

        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonAyar);
        Assert.Contains("15.000,00 ₺ yerine 0,00 ₺", vm.AyarUyarisi);
        Assert.Contains("Onaylamak için yeniden kaydedin", vm.AyarUyarisi);

        vm.KasaAcilisDevri = 1m;
        vm.KasaAcilisDevri = 0m;          // değer değişti: onay sıfırlanır
        Assert.Null(vm.AyarUyarisi);
        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonAyar);
        vm.TakipBaslangic = new DateTime(2026, 2, 1);              // diğer alan değişti: onay sıfırlanır
        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonAyar);

        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0m, api.SonAyar!.KasaAcilisDevri);
        Assert.Null(vm.AyarUyarisi);

        api.SonAyar = null;                                        // kayıtlı değer artık 0: yeniden onay istenmez
        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0m, api.SonAyar!.KasaAcilisDevri);
    }

    [Fact]
    public async Task Kayitli_kanal_acilis_devrini_sifira_indirmek_ikinci_basista_onaylanir()
    {
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, true),
            KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 0, 5000m), new(2, "PERAKENDE", true, 1, 700m) },
        };
        var vm = new AyarlarViewModel(api, TestOturumu.Ac());
        await vm.YukleAsync();
        vm.KanalDuzenle(vm.Kanallar[0]);
        vm.DuzenKanalAcilisDevri = 0m;

        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonKanalGuncelle);
        Assert.Contains("MEZAT açılış devri 5.000,00 ₺ yerine 0,00 ₺", vm.KanalUyarisi);

        vm.KanalDuzenle(vm.Kanallar[1]);
        vm.DuzenKanalAcilisDevri = 0m;   // başka kanal: onay sıfırlanır
        Assert.Null(vm.KanalUyarisi);
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonKanalGuncelle);
        Assert.Contains("PERAKENDE açılış devri 700,00 ₺", vm.KanalUyarisi);

        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.SonKanalGuncelle!.Value.Id);
        Assert.Equal(0m, api.SonKanalGuncelle!.Value.G.AcilisDevri);
        Assert.Null(vm.KanalUyarisi);

        vm.KanalDuzenle(vm.Kanallar[0]);
        vm.DuzenKanalAcilisDevri = 4000m;  // sıfır olmayan değişiklik onaysız gider
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Equal((1, 4000m), (api.SonKanalGuncelle!.Value.Id, api.SonKanalGuncelle.Value.G.AcilisDevri));

        vm.YeniKanalCommand.Execute(null);
        vm.DuzenKanalAd = "TOPTAN";     // yeni kanalın kayıtlı devri yok
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Equal("TOPTAN", api.SonKanalOlustur!.Ad);
    }

    // Tamamlanmış ayların kanal kümesi sunucuda dondurulduğundan kilit varken de aktif kanal eklenebilir: kanal formu kilidi söyler,
    // yeni kanal kilitte de aktif gelir; düzenlenen kanal kendi aktifliğiyle açılır.
    [Fact]
    public async Task Kilit_varken_kanal_formu_kilidi_soyler_yeni_kanal_aktif_gelir()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false), KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 0, 0m) } };
        var kilit = new SahteKilit { KilitliSonTarih = new DateOnly(2026, 8, 31) };
        var vm = new AyarlarViewModel(api, TestOturumu.Ac(), kilit);

        await vm.YukleAsync();
        Assert.True(vm.DuzenKanalAktif);
        Assert.Contains("31.08.2026", vm.KanalKilitNotu);

        vm.KanalDuzenle(vm.Kanallar[0]);
        Assert.True(vm.DuzenKanalAktif);
        vm.YeniKanalCommand.Execute(null);
        Assert.True(vm.DuzenKanalAktif);

        vm.DuzenKanalAd = "E-TİCARET";
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.True(api.SonKanalOlustur!.Aktif);
        Assert.True(vm.DuzenKanalAktif);                           // kayıttan sonraki boş form yine aktif

        kilit.KilitliSonTarih = null;                              // kilit tamamen açıldı: yenilenince yeni kanal aktif varsayılır
        await vm.YukleAsync();
        Assert.True(vm.DuzenKanalAktif);
        Assert.Null(vm.KanalKilitNotu);
    }

    [Fact]
    public async Task Kilit_yokken_ya_da_kilit_durumu_okunamazsa_yeni_kanal_aktif_varsayilir_ayarlar_yuklenir()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false), KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 0, 0m) } };
        var vm = new AyarlarViewModel(api, TestOturumu.Ac(), new SahteKilit());
        await vm.YukleAsync();
        Assert.True(vm.DuzenKanalAktif);
        Assert.Null(vm.KanalKilitNotu);

        // Kilit durumu yalnız form varsayılanı içindir (kuralı sunucu uygular): okunamazsa ayarlar yine yüklenir.
        var okunamaz = new AyarlarViewModel(api, TestOturumu.Ac(), new SahteKilit { Hata = new KasaApiException(HttpStatusCode.NotFound) });
        await okunamaz.YukleAsync();
        Assert.Null(okunamaz.Hata);
        Assert.Single(okunamaz.Kanallar);
        Assert.True(okunamaz.DuzenKanalAktif);
        Assert.Null(okunamaz.KanalKilitNotu);
    }

    // Ayarlar bu oturumda sunucudan okunmadan formda varsayılanlar durur (takip başlangıcı bugün, açılış devri 0): yükleme
    // başarısızken ya da hiç yapılmamışken 'Ayarı kaydet' bu değerleri sunucuya göndermez; düğme devre dışıdır, çağrılırsa ileti
    // verir. Başarılı yüklemeden sonra kayıt çalışır; oturum değişince yeniden yükleme gerekir.
    [Fact]
    public async Task Ayarlar_basariyla_yuklenmeden_varsayilan_form_sunucuya_gonderilmez()
    {
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var api = new SahteApi { YuklemeHatasi = new HttpRequestException("Sunucuya ulaşılamadı.") };
        // Varsayılan "bugün" modelin kurulduğu gündür; aralık, koşu gece yarısını geçerse de doğru günü kabul eder.
        var once = DateTime.Today;
        var vm = new AyarlarViewModel(api, auth: auth);
        Assert.False(vm.AyarKaydetCommand.CanExecute(null));

        await vm.YukleAsync();
        Assert.NotNull(vm.Hata);
        Assert.InRange(vm.TakipBaslangic, once, DateTime.Today);
        Assert.Equal(0m, vm.KasaAcilisDevri);
        Assert.False(vm.AyarKaydetCommand.CanExecute(null));
        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonAyar);
        Assert.Equal(AyarlarViewModel.AyarlarYuklenmediMesaji, vm.Hata);

        api.YuklemeHatasi = null;
        api.AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 15000m, false);
        await vm.YukleAsync();
        Assert.True(vm.AyarKaydetCommand.CanExecute(null));
        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Equal(new AyarYaz(new DateOnly(2026, 1, 1), 15000m), api.SonAyar);

        // Yeni oturumun formu önceki oturumun yüklemesine dayanmaz.
        api.SonAyar = null;
        auth.OturumSurumu++;
        Assert.False(vm.AyarKaydetCommand.CanExecute(null));
        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonAyar);
    }

    private sealed class SahteKilit : IAylikGiderApi
    {
        public DateOnly? KilitliSonTarih; public Exception? Hata;
        public Task<AyKilidiDto> AyKilidiAsync() => Hata is not null ? Task.FromException<AyKilidiDto>(Hata)
            : Task.FromResult(new AyKilidiDto(1, KilitliSonTarih, Array.Empty<AyKilidiOlayDto>()));
        public Task<AyKilidiDto> AyKilidiDegistirAsync(bool kapat, AyKilidiYaz girdi) => throw new NotSupportedException();
        public Task<IReadOnlyList<AylikGiderSablonDto>> AylikGiderSablonlariAsync() => throw new NotSupportedException();
        public Task<AylikGiderSablonDto> AylikGiderSablonKaydetAsync(int? id, AylikGiderSablonYaz girdi) => throw new NotSupportedException();
        public Task<AylikGiderAyDto> AylikGiderlerAsync(int yil, int ay) => throw new NotSupportedException();
        public Task<AylikGiderSatirDto> AylikGiderOdeAsync(int sablonId, AylikGiderOdemeYaz girdi) => throw new NotSupportedException();
        public Task<AylikGiderSatirDto> AylikGiderIptalAsync(int odemeId, AylikGiderIptalYaz girdi) => throw new NotSupportedException();
    }
}
