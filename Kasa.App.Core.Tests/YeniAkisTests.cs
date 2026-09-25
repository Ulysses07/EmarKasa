using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class YeniAkisTests
{
    private static AuthViewModel Auth(bool editor = true) => new(new SahteApi()) { AktifRol = editor ? Rol.Editor : Rol.Izleyici };
    private static readonly DateOnly Bugun = new(2026, 9, 23);
    private static AlisDto Alis(int id = 7) => new(id, 3, null, "Editör", Bugun, "Firma", null, "Taslak", null, 100, 10, 90,
        new[] { new AlisKalemDto(1, "Mal", 100, new[] { new AlisDagilimDto(1, "MEZAT", 100) }) },
        new[] { new AlisOdemeDto(4, 20, Bugun, 10, null, true, Array.Empty<AlisDagilimDto>()) }, 2, Bugun.AddDays(10));
    private static async Task<AlislarViewModel> AlisVm(Fake api, SahteApi? finans = null, IBenzerKayitApi? benzerlik = null)
    {
        var vm = new AlislarViewModel(api, finans ?? new SahteApi(), api, api, benzerlik) { EditorMu = true };
        await vm.YukleAsync(); vm.SecCommand.Execute(vm.Alislar.Single(a => a.Veri.Id == 7)); return vm;
    }
    [Fact] public async Task Odeme_tasima_hedef_surumu_kullanir_ve_yanit_kaynak_alisi_korur()
    {
        var api = new Fake(); var vm = await AlisVm(api);
        vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]); vm.HedefAlis = vm.DuzeltmeHedefleri[0]; vm.DuzeltmeAciklamasi = "Yanlış alışa bağlandı";
        await vm.OdemeDuzeltKaydetCommand.ExecuteAsync(null);
        Assert.Equal(8, api.SonDuzelt!.HedefAlisId); Assert.Equal(3, api.SonDuzelt.HedefSurum);
        Assert.Equal(7, vm.Secili!.Id); Assert.Contains("taşındı", vm.Mesaj);
    }
    [Fact] public async Task Odeme_duzeltme_ag_hatasinda_ayni_anahtarla_tekrarlanir()
    {
        var api = new Fake { DuzeltHata = true }; var vm = await AlisVm(api);
        vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]); vm.DuzeltmeAciklamasi = "Tarih düzeltildi";
        await vm.OdemeDuzeltKaydetCommand.ExecuteAsync(null); var ilk = api.SonDuzelt;
        api.DuzeltHata = false; await vm.OdemeDuzeltKaydetCommand.ExecuteAsync(null);
        Assert.Equal(ilk!.IstekId, api.SonDuzelt!.IstekId);
    }
    [Fact] public async Task Iptal_gerekce_olmadan_gonderilmez()
    {
        var api = new Fake(); var vm = await AlisVm(api);
        vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]); await vm.OdemeIptalAsync();
        Assert.Null(api.SonIptal); Assert.Contains("nedenini", vm.Hata);
        vm.DuzeltmeAciklamasi = "Hatalı kayıt"; await vm.OdemeIptalAsync(); Assert.Equal("Hatalı kayıt", api.SonIptal!.Aciklama);
    }
    [Fact] public async Task Karti_silinmis_eski_harcama_nakit_olarak_gosterilmez_ve_hesaba_baglanmaz()
    {
        var api = new Fake(); var finans = new SahteApi { IslemlerListe = new[] { new IslemDto(20, Bugun, "Firma", 10, "MEZAT", GiderTipi.KrediKarti, null) } };
        var vm = await AlisVm(api, finans); vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]);
        Assert.True(vm.EskiKartHarcamasi); Assert.Contains("Eski kart", vm.DuzeltmeKarti!.Ad);
        vm.DuzeltmeAciklamasi = "Tarih düzeltildi";
        await vm.OdemeDuzeltKaydetCommand.ExecuteAsync(null); Assert.Null(api.SonDuzelt!.HesapId);
    }
    [Fact] public async Task Oturum_degistiginde_eski_belge_listesi_yansitilmaz()
    {
        var bekleyen = new TaskCompletionSource<IReadOnlyList<BelgeDto>>(); var api = new Fake { BelgeYaniti = bekleyen.Task }; var vm = await AlisVm(api);
        var islem = vm.BelgeleriYukleAsync(); vm.OturumuAyarla(2, false);
        bekleyen.SetResult(new[] { new BelgeDto(1, 7, null, "eski.pdf", "application/pdf", 10, DateTimeOffset.UtcNow) }); await islem;
        Assert.Empty(vm.Belgeler); Assert.Null(vm.Secili);
    }
    [Fact] public async Task Kisa_editor_sifresi_ve_ters_rapor_tarihi_apiye_gitmez()
    {
        var api = new Fake(); var vm = new GuvenlikViewModel(api, Auth()) { MevcutSifre = "eski", YeniSifre = "12345678" };
        await vm.SifreDegistirCommand.ExecuteAsync(null); Assert.False(api.SifreDegisti); Assert.Contains("12", vm.Hata);
        var rapor = new DisariAktarViewModel(api, new SahteApi(), Auth()) { Baslangic = new(2026, 10, 1), Bitis = new(2026, 9, 1) };
        Assert.Null(await rapor.IndirAsync("xlsx")); Assert.False(api.RaporIndirildi);
    }
    [Fact] public async Task Gecikmis_kurtarma_kodu_ekrandan_ayrildiktan_sonra_gosterilmez()
    {
        var bekleyen = new TaskCompletionSource<KurtarmaKoduDto>(); var api = new Fake { KurtarmaYaniti = bekleyen.Task };
        var vm = new GuvenlikViewModel(api, Auth()) { MevcutSifre = "eski-sifre" };
        var islem = vm.KurtarmaKoduOlusturCommand.ExecuteAsync(null); vm.EkrandanAyril(); bekleyen.SetResult(new("gizli-kod")); await islem;
        Assert.Null(vm.KurtarmaKodu); Assert.Empty(vm.MevcutSifre);
    }

    [Fact] public async Task Alis_serbest_aciklama_tutar_ve_dagilimla_kaydedilir_erp_alanlari_gonderilmez()
    {
        var api = new Fake(); var vm = await AlisVm(api);
        vm.Tedarikci = "  Açık artırmadan alındı  "; vm.Kalemler[0].Tutar = 80; vm.Kalemler[0].Dagilimlar[0].Tutar = 80;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal("Açık artırmadan alındı", api.SonAlis!.Tedarikci); Assert.Equal(80, api.SonAlis.Kalemler[0].Tutar);
        Assert.Null(api.SonAlis.TedarikciId); Assert.Null(api.SonAlis.Vade);
        Assert.Null(api.SonAlis.Kalemler[0].Miktar); Assert.Null(api.SonAlis.Kalemler[0].BirimFiyat);
    }
    [Fact] public async Task Odeme_duzeltmede_eski_hesap_alani_tekrar_yazilmaz()
    {
        var ilk = Alis(); var api = new Fake { IlkAlis = ilk with { Odemeler = new[] { ilk.Odemeler[0] with { HesapId = 1 } } } };
        var vm = await AlisVm(api); vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]); vm.DuzeltmeAciklamasi = "Tarih düzeltildi";
        await vm.OdemeDuzeltKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonDuzelt!.HesapId);
    }
    [Fact] public async Task Yeni_alis_odemesi_benzer_kaydi_onayla_kaydeder_mevcut_gider_baglama_sormaz()
    {
        var api = new Fake(); var lookup = new BenzerKayitTests.Fake(); var finans = new SahteApi { IslemlerListe = new[] { new IslemDto(30, Bugun, "Yeni gider", 10, "MEZAT", GiderTipi.Cari, null) } };
        var vm = await AlisVm(api, finans, lookup); vm.OdemeTutari = 10;
        await vm.OdemeKaydetCommand.ExecuteAsync(null); Assert.Null(api.SonOdeme); Assert.Equal("AlisOdeme", lookup.SonArama!.Tur); Assert.Equal(7, lookup.SonArama.AlisId);
        await vm.OdemeyiAyriKaydetCommand.ExecuteAsync(null); Assert.Equal(10, api.SonOdeme!.Tutar);
        var ikinciApi = new Fake(); var ikinciLookup = new BenzerKayitTests.Fake { Hata = true }; var ikinci = await AlisVm(ikinciApi, finans, ikinciLookup);
        ikinci.MevcutGiderKullan = true; ikinci.SeciliGider = ikinci.BaglanabilirGiderler.Single(); await ikinci.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(30, ikinciApi.SonOdeme!.MevcutIslemId); Assert.Equal(0, ikinciLookup.Cagri);
    }
    [Fact] public async Task Alis_benzerlik_hatasi_odeme_eklemez()
    {
        var api = new Fake(); var vm = await AlisVm(api, benzerlik: new BenzerKayitTests.Fake { Hata = true }); vm.OdemeTutari = 10;
        await vm.OdemeKaydetCommand.ExecuteAsync(null); Assert.Null(api.SonOdeme); Assert.Contains("ulaşılamadı", vm.Hata);
    }

    private sealed class Fake : IAlisOdemeApi, IYonetimApi, IAlisApi
    {
        public AlisYaz? SonAlis;
        public AlisDto? IlkAlis;
        public AlisOdemeDuzeltYaz? SonDuzelt;
        public AlisOdemeIptalYaz? SonIptal;
        public AlisOdemeYaz? SonOdeme;
        public bool DuzeltHata, SifreDegisti, RaporIndirildi;
        public Task<IReadOnlyList<BelgeDto>>? BelgeYaniti;
        public Task<KurtarmaKoduDto>? KurtarmaYaniti;
        public Task<AlisDto> AlisOdemeDuzeltAsync(int a, int o, AlisOdemeDuzeltYaz g) { SonDuzelt = g; return DuzeltHata ? Task.FromException<AlisDto>(new HttpRequestException()) : Task.FromResult(Alis(a)); }
        public Task<AlisDto> AlisOdemeIptalAsync(int a, int o, AlisOdemeIptalYaz g) { SonIptal = g; return Task.FromResult(Alis(a)); }
        public Task SifreDegistirAsync(SifreDegistirYaz g) { SifreDegisti = true; return Task.CompletedTask; }
        public Task<KurtarmaKoduDto> KurtarmaKoduOlusturAsync(string s) => KurtarmaYaniti ?? Task.FromResult(new KurtarmaKoduDto("kod"));
        public Task SifreKurtarAsync(SifreKurtarYaz g) => Task.CompletedTask;
        public Task<SurumDto> SurumAsync() => Task.FromResult(new SurumDto("2.0.0", "2.0.0", null, null));
        public Task<YedekDurumuDto> YedekDurumuAsync() => Task.FromResult(new YedekDurumuDto(true, null, null, null));
        public Task<IndirilenDosya> YedekIndirAsync() => Task.FromResult(new IndirilenDosya(Array.Empty<byte>(), "yedek.zip", "application/zip"));
        public Task<IReadOnlyList<BelgeDto>> BelgelerAsync(int id) => BelgeYaniti ?? Task.FromResult<IReadOnlyList<BelgeDto>>(Array.Empty<BelgeDto>());
        public Task<BelgeDto> BelgeYukleAsync(int id, string ad, string tur, byte[] b, int? odemeId = null) => Task.FromResult(new BelgeDto(1, id, odemeId, ad, tur, b.Length, DateTimeOffset.UtcNow));
        public Task<IndirilenDosya> BelgeIndirAsync(int id) => YedekIndirAsync();
        public Task BelgeSilAsync(int id) => Task.CompletedTask;
        public Task<IndirilenDosya> DisariAktarAsync(DateOnly b, DateOnly s, string? k, string bicim) { RaporIndirildi = true; return YedekIndirAsync(); }
        public Task<IReadOnlyList<AlisKanalDto>> AlisKanallariAsync() => Task.FromResult<IReadOnlyList<AlisKanalDto>>(new[] { new AlisKanalDto(1, "MEZAT", true) });
        public Task<IReadOnlyList<AlisDto>> AlislarAsync() => Task.FromResult<IReadOnlyList<AlisDto>>(new[] { IlkAlis ?? Alis(), Alis(8) });
        public Task<AlisDto> AlisOlusturAsync(AlisYaz g) { SonAlis = g; return Task.FromResult(Alis()); }
        public Task<AlisDto> AlisGuncelleAsync(int id, AlisYaz g) => AlisOlusturAsync(g);
        public Task<AlisDto> AlisGonderAsync(int id, AlisDurumYaz g) => Task.FromResult(Alis());
        public Task<AlisDto> AlisOnaylaAsync(int id, AlisDurumYaz g) => Task.FromResult(Alis());
        public Task<AlisDto> AlisIadeAsync(int id, AlisDurumYaz g) => Task.FromResult(Alis());
        public Task<AlisDto> AlisOdemeKaydetAsync(int id, AlisOdemeYaz g) { SonOdeme = g; return Task.FromResult(Alis()); }
        public Task<IReadOnlyList<AliciDto>> AlicilarAsync() => Task.FromResult<IReadOnlyList<AliciDto>>(Array.Empty<AliciDto>());
        public Task<AliciDto> AliciOlusturAsync(AliciYaz g) => Task.FromResult(new AliciDto(1, g.Kullanici, g.Ad, true));
        public Task<AliciDto> AliciGuncelleAsync(int id, AliciYaz g) => AliciOlusturAsync(g);
    }
}
