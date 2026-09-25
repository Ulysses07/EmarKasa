using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class AlislarViewModelTests
{
    private static readonly AlisKanalDto Kanal1 = new(1, "MEZAT", true);
    private static readonly AlisKanalDto Kanal2 = new(2, "PERAKENDE", true);

    private static AlisDto Alis(string durum = "Taslak", decimal odenen = 0) => new(
        7, 2, 3, "Ayşe", new(2026, 9, 21), "Tedarikçi", null, durum, null, 100m, odenen, 100m - odenen,
        new[] { new AlisKalemDto(1, "Mal alımı", 100m, new[] { new AlisDagilimDto(1, "MEZAT", 60m), new AlisDagilimDto(2, "PERAKENDE", 40m) }) },
        Array.Empty<AlisOdemeDto>());

    [Fact]
    public async Task Alici_yalniz_alis_menusunu_ve_kendi_api_yuzeyini_kullanir()
    {
        Assert.Equal(Rol.Alici, SekmeModeli.RolCoz("alici"));
        Assert.Equal(new[] { Bolum.Alislar }, SekmeModeli.Bolumler(Rol.Alici));
        Assert.DoesNotContain(Bolum.Alislar, SekmeModeli.Bolumler(Rol.Izleyici));
        Assert.Contains(Bolum.Alislar, SekmeModeli.Bolumler(Rol.Editor));
        var api = new SahteAlisApi();
        var vm = new AlislarViewModel(api, new SahteApi { YuklemeHatasi = new Exception("Alıcı finans çağrısı yapamaz") });
        await vm.YukleAsync();
        Assert.True(vm.VeriHazir);
        Assert.Null(vm.Hata);
        Assert.Equal(0, api.HesapOkuma);
    }

    [Fact]
    public async Task Cok_kalemli_cok_kanalli_taslak_tam_govdeyle_kaydedilir()
    {
        var api = new SahteAlisApi();
        var vm = new AlislarViewModel(api, new SahteApi());
        await vm.YukleAsync();
        vm.Tedarikci = "Firma";
        var ilk = vm.Kalemler[0]; ilk.Aciklama = "Birinci"; ilk.Tutar = 100m;
        ilk.Dagilimlar.Add(new(new[] { Kanal1, Kanal2 }) { Kanal = Kanal1, Tutar = 60m });
        ilk.Dagilimlar.Add(new(new[] { Kanal1, Kanal2 }) { Kanal = Kanal2, Tutar = 40m });
        vm.KalemEkleCommand.Execute(null);
        vm.Kalemler[1].Aciklama = "İkinci"; vm.Kalemler[1].Tutar = 25m;
        vm.DagilimEkleCommand.Execute(vm.Kalemler[1]);
        Assert.Equal(125m, vm.Toplam);
        Assert.Equal(125m, vm.Dagitilan);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.NotNull(api.SonYaz);
        Assert.Equal(2, api.SonYaz!.Kalemler.Count);
        Assert.Equal(2, api.SonYaz.Kalemler[0].Dagilimlar.Count);
        Assert.Equal(0, api.SonYaz.Surum);
        Assert.Equal(7, vm.Secili!.Id);
    }

    [Fact]
    public async Task Gonder_once_son_degisimleri_kaydeder_sonra_yeni_surumu_gonderir()
    {
        var api = new SahteAlisApi { Liste = new[] { Alis() } };
        var vm = new AlislarViewModel(api, new SahteApi());
        await vm.YukleAsync(); vm.SecCommand.Execute(vm.Alislar[0]);
        vm.Kalemler[0].Aciklama = "Düzeltilen açıklama";
        await vm.GonderCommand.ExecuteAsync(null);
        Assert.Equal("Düzeltilen açıklama", api.SonYaz!.Kalemler[0].Aciklama);
        Assert.Equal(3, api.SonDurum!.Surum);
        Assert.Equal("Incelemede", vm.Secili!.Durum);
        Assert.False(vm.Duzenlenebilir);
    }

    [Fact]
    public async Task Eksik_dagilim_onayi_engeller_ve_iade_nedeni_zorunludur()
    {
        var api = new SahteAlisApi { Liste = new[] { Alis("Incelemede") } };
        var vm = new AlislarViewModel(api, new SahteApi()) { EditorMu = true };
        await vm.YukleAsync(); vm.SecCommand.Execute(vm.Alislar[0]);
        vm.Kalemler[0].Dagilimlar.RemoveAt(1);
        await vm.OnaylaCommand.ExecuteAsync(null);
        Assert.Contains("tamamını", vm.Hata);
        Assert.Null(api.SonDurum);
        vm.DegisiklikleriBirakCommand.Execute(null);
        await vm.IadeCommand.ExecuteAsync(null);
        Assert.Contains("İade nedenini", vm.Hata);
        vm.IadeNedeni = "Kanal payını düzeltin";
        await vm.IadeCommand.ExecuteAsync(null);
        Assert.Equal("Kanal payını düzeltin", api.SonDurum!.Not);
        Assert.Equal("Taslak", vm.Secili!.Durum);
    }

    [Fact]
    public async Task Kismi_odeme_onizlemesi_kuruslari_korur_ve_yeni_odeme_bekleyen_olarak_gosterilir()
    {
        var api = new SahteAlisApi { Liste = new[] { Alis("Taslak", 20m) } };
        var vm = new AlislarViewModel(api, new SahteApi()) { EditorMu = true };
        await vm.YukleAsync(); vm.SecCommand.Execute(vm.Alislar[0]);
        vm.OdemeTutari = 0.01m;
        Assert.Equal(0.01m, vm.OdemeOnizleme.Sum(p => p.Tutar));
        Assert.All(vm.OdemeOnizleme, p => Assert.True(p.Tutar >= 0));
        Assert.Contains("onaylanana kadar", vm.OnizlemeAciklamasi);
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0.01m, api.SonOdeme!.Tutar);
        Assert.True(vm.DagilimBekliyor);
        Assert.Equal(0.01m, vm.DagilimBekleyenTutar);
        Assert.Contains("Dağılım bekliyor", vm.Odemeler[0].Dagilim);
    }

    [Fact]
    public async Task Ag_hatasinda_ayni_odeme_anahtari_ve_surumu_tekrar_kullanilir()
    {
        var api = new SahteAlisApi { Liste = new[] { Alis() }, OdemeHatasi = true };
        var vm = new AlislarViewModel(api, new SahteApi()) { EditorMu = true };
        await vm.YukleAsync(); vm.SecCommand.Execute(vm.Alislar[0]); vm.OdemeTutari = 30m;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        var ilk = api.SonOdeme;
        Assert.NotNull(vm.Hata);
        api.OdemeHatasi = false;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(ilk, api.SonOdeme);
        Assert.Equal(30m, vm.Odenen);
        Assert.Equal(70m, vm.Kalan);
    }

    [Fact]
    public async Task Mevcut_gider_tam_degerleriyle_baglanir_bagli_giderler_secilemez()
    {
        var gider = new IslemDto(91, new(2026, 9, 20), "Firma", 25m, "Ortak", GiderTipi.Cari, null);
        var bagli = gider with { Id = 92, AlisId = 100 };
        var api = new SahteAlisApi { Liste = new[] { Alis() } };
        var vm = new AlislarViewModel(api, new SahteApi { IslemlerListe = new[] { gider, bagli } }) { EditorMu = true };
        await vm.YukleAsync(); vm.SecCommand.Execute(vm.Alislar[0]);
        Assert.Single(vm.BaglanabilirGiderler);
        vm.MevcutGiderKullan = true; vm.SeciliGider = vm.BaglanabilirGiderler[0];
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(91, api.SonOdeme!.MevcutIslemId);
        Assert.Equal(gider.Tarih, api.SonOdeme.Tarih);
        Assert.Equal(gider.TutarTl, api.SonOdeme.Tutar);
    }

    [Fact]
    public async Task Alici_hesabi_sifreyi_bos_birakinca_korur_pasife_alinabilir()
    {
        var api = new SahteAlisApi();
        var vm = new AlislarViewModel(api, new SahteApi()) { EditorMu = true };
        await vm.YukleAsync();
        vm.AliciDuzenleCommand.Execute(new AliciDto(4, "ayse", "Ayşe", true));
        vm.AliciAktif = false;
        await vm.AliciKaydetCommand.ExecuteAsync(null);
        Assert.False(api.SonAlici!.Aktif);
        Assert.Null(api.SonAlici.Sifre);
        Assert.Equal("", vm.AliciSifre);
    }

    [Fact]
    public async Task Bagli_finans_gideri_normal_ekrandan_degistirilemez_ve_silinemez()
    {
        var api = new SahteApi(); var vm = new IslemlerViewModel(api);
        var gider = new IslemDto(9, new(2026, 9, 20), "Firma", 10m, "Dağılım bekliyor", GiderTipi.Cari, null, AlisId: 7);
        vm.Duzenle(gider);
        Assert.Equal(0, vm.DuzenId); Assert.Contains("Alışlar", vm.Hata);
        await vm.SilCommand.ExecuteAsync(gider);
        Assert.Null(api.SonIslemSil); Assert.Contains("Alışlar", vm.Hata);
    }

    [Fact]
    public async Task Kaydedilmemis_degisim_odeme_secim_yeni_ve_yenile_ile_kaybolmaz()
    {
        var api = new SahteAlisApi { Liste = new[] { Alis(), Alis() with { Id = 8 } } };
        var vm = new AlislarViewModel(api, new SahteApi()) { EditorMu = true };
        await vm.YukleAsync(); vm.SecCommand.Execute(vm.Alislar.First(a => a.Veri.Id == 7));
        vm.Tedarikci = "Kaydedilmemiş firma";
        vm.OdemeTutari = 10m;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonOdeme);
        Assert.Contains("önce", vm.Hata);
        vm.SecCommand.Execute(vm.Alislar.First(a => a.Veri.Id == 8));
        vm.YeniCommand.Execute(null);
        await vm.YukleAsync();
        Assert.Equal(7, vm.Secili!.Id);
        Assert.Equal("Kaydedilmemiş firma", vm.Tedarikci);
        Assert.True(vm.KaydedilmemisDegisiklikVar);
        vm.DegisiklikleriBirakCommand.Execute(null);
        Assert.Equal("Tedarikçi", vm.Tedarikci);
        Assert.False(vm.KaydedilmemisDegisiklikVar);
    }

    [Fact]
    public async Task Ayni_rolde_yeni_oturum_eski_yuklemenin_verisini_almaz()
    {
        var eski = new TaskCompletionSource<IReadOnlyList<AlisDto>>();
        var yeniKayit = Alis() with { Id = 22, Alici = "Yeni alıcı", Tedarikci = "Yeni firma" };
        var api = new SahteAlisApi { ListeGetir = () => eski.Task };
        var vm = new AlislarViewModel(api, new SahteApi());
        vm.OturumuAyarla(1, false);
        var ilk = vm.YukleAsync();
        vm.OturumuAyarla(2, false);
        api.ListeGetir = () => Task.FromResult<IReadOnlyList<AlisDto>>(new[] { yeniKayit });
        await vm.YukleAsync();
        eski.SetResult(new[] { Alis() });
        await ilk;
        Assert.Equal(22, Assert.Single(vm.Alislar).Veri.Id);
        Assert.True(vm.VeriHazir);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Eski_editor_odemesinin_yaniti_yeni_alici_oturumunu_degistirmez()
    {
        var bekleyen = new TaskCompletionSource<AlisDto>();
        var api = new SahteAlisApi { Liste = new[] { Alis() }, OdemeYaniti = bekleyen.Task };
        var vm = new AlislarViewModel(api, new SahteApi());
        vm.OturumuAyarla(1, true);
        await vm.YukleAsync(); vm.SecCommand.Execute(vm.Alislar[0]); vm.OdemeTutari = 10m;
        var odeme = vm.OdemeKaydetCommand.ExecuteAsync(null);
        vm.OturumuAyarla(2, false);
        api.Liste = new[] { Alis() with { Id = 22, Alici = "Yeni alıcı" } };
        await vm.YukleAsync();
        bekleyen.SetResult(Alis() with { Odenen = 10m });
        await odeme;
        Assert.Equal(22, Assert.Single(vm.Alislar).Veri.Id);
        Assert.False(vm.EditorMu);
        Assert.Null(vm.Mesaj);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Sifir_pay_iceren_gecerli_kayitta_onizleme_calismaya_devam_eder()
    {
        var alis = Alis() with { Kalemler = new[] { new AlisKalemDto(1, "Mal", 100m,
            new[] { new AlisDagilimDto(1, "MEZAT", 100m), new AlisDagilimDto(2, "PERAKENDE", 0m) }) } };
        var vm = new AlislarViewModel(new SahteAlisApi { Liste = new[] { alis } }, new SahteApi()) { EditorMu = true };
        await vm.YukleAsync(); vm.SecCommand.Execute(vm.Alislar[0]); vm.OdemeTutari = 10m;
        Assert.Equal(10m, Assert.Single(vm.OdemeOnizleme).Tutar);
    }

    private sealed class SahteAlisApi : IAlisApi
    {
        public IReadOnlyList<AlisDto> Liste = Array.Empty<AlisDto>();
        public AlisYaz? SonYaz;
        public AlisDurumYaz? SonDurum;
        public AlisOdemeYaz? SonOdeme;
        public AliciYaz? SonAlici;
        public bool OdemeHatasi;
        public Func<Task<IReadOnlyList<AlisDto>>>? ListeGetir;
        public Task<AlisDto>? OdemeYaniti;
        public int HesapOkuma;
        private AlisDto Kayit => Liste.FirstOrDefault() ?? Alis();
        public Task<IReadOnlyList<AlisKanalDto>> AlisKanallariAsync() => Task.FromResult<IReadOnlyList<AlisKanalDto>>(new[] { Kanal1, Kanal2 });
        public Task<IReadOnlyList<AlisDto>> AlislarAsync() => ListeGetir?.Invoke() ?? Task.FromResult(Liste);
        public Task<AlisDto> AlisOlusturAsync(AlisYaz g) { SonYaz = g; return Task.FromResult(Kayit with { Surum = 3 }); }
        public Task<AlisDto> AlisGuncelleAsync(int id, AlisYaz g) { SonYaz = g; return Task.FromResult(Kayit with { Surum = 3 }); }
        public Task<AlisDto> AlisGonderAsync(int id, AlisDurumYaz g) { SonDurum = g; return Task.FromResult(Kayit with { Durum = "Incelemede", Surum = 4 }); }
        public Task<AlisDto> AlisOnaylaAsync(int id, AlisDurumYaz g) { SonDurum = g; return Task.FromResult(Kayit with { Durum = "Onaylandi", Surum = 4 }); }
        public Task<AlisDto> AlisIadeAsync(int id, AlisDurumYaz g) { SonDurum = g; return Task.FromResult(Kayit with { Durum = "Taslak", EditorNotu = g.Not, Surum = 4 }); }
        public Task<AlisDto> AlisOdemeKaydetAsync(int id, AlisOdemeYaz g)
        {
            SonOdeme = g;
            if (OdemeYaniti is not null) return OdemeYaniti;
            if (OdemeHatasi) return Task.FromException<AlisDto>(new HttpRequestException("yanıt kayboldu"));
            return Task.FromResult(Kayit with { Surum = 3, Odenen = Kayit.Odenen + g.Tutar, Kalan = Kayit.Kalan - g.Tutar,
                Odemeler = new[] { new AlisOdemeDto(1, g.MevcutIslemId ?? 90, g.Tarih, g.Tutar, g.KrediKartiId, true, Array.Empty<AlisDagilimDto>()) } });
        }
        public Task<IReadOnlyList<AliciDto>> AlicilarAsync() { HesapOkuma++; return Task.FromResult<IReadOnlyList<AliciDto>>(Array.Empty<AliciDto>()); }
        public Task<AliciDto> AliciOlusturAsync(AliciYaz g) { SonAlici = g; return Task.FromResult(new AliciDto(4, g.Kullanici, g.Ad, g.Aktif)); }
        public Task<AliciDto> AliciGuncelleAsync(int id, AliciYaz g) { SonAlici = g; return Task.FromResult(new AliciDto(id, g.Kullanici, g.Ad, g.Aktif)); }
    }
}
