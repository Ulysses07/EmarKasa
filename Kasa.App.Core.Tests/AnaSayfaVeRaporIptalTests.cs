using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Ana sayfa: panel, kanal eşikleri ve takip özeti tek istekte ve aynı anlık görüntüden; eski sunucuda eski uçlar.
/// Rapor istekleri iptal edilebilir (yeni yükleme ve ekrandan ayrılma öncekini ağda da bırakır, hata sayılmaz); haftalık
/// raporun veri sağlığı uyarısı görünür; okuma zaman aşımı "sunucuda tamamlanmış olabilir" demez.</summary>
public class AnaSayfaVeRaporIptalTests
{
    private static readonly DateOnly Tarih = new(2026, 9, 28);
    private static AuthViewModel Editor() => new(new SahteApi()) { AktifRol = Rol.Editor };
    private static PanelDto Panel(decimal kasa = 900) => new(kasa, new[] { new KanalBakiyeDto("MEZAT", 600, 1), new KanalBakiyeDto("PERAKENDE", 300, 2) }, 10, -5);
    private static readonly KasaEsikDto Esik = new(1, "MEZAT", 2, 1000, true, 600, true);
    private static TakipOzetDto Ozet(decimal borc = 120) => new(Tarih, borc, 40, Array.Empty<TakipOlayDto>(), new[] { new TakipKanalPayi(1, "MEZAT", borc) });
    private static HaftalikOzetDto Hafta(int gun, string? uyari = null)
    {
        var bas = new DateOnly(2027, 9, gun);
        return new(new(bas, bas.AddDays(6), 2027, 9), Array.Empty<KanalHaftalikDto>(), 0, 0, 0, 900, 0, uyari);
    }

    [Fact]
    public async Task Panel_ana_sayfa_ucundan_tek_istekte_yuklenir_esik_ve_takip_ozeti_ayni_yanittan_gelir()
    {
        var api = new SahteApi { AnaSayfaGetir = (_, _) => Task.FromResult(new AnaSayfaDto(Panel(), new[] { Esik }, Ozet())) };
        var vm = new PanelViewModel(api) { TakipGunu = 7 };

        await vm.YukleAsync();

        Assert.Equal(new[] { 7 }, api.AnaSayfaIstekleri);
        Assert.True(vm.VeriVar); Assert.Equal(900, vm.GuncelKasa); Assert.Equal(2, vm.Kanallar.Count);
        Assert.Equal(Esik, Assert.Single(vm.KasaEsikleri!));
        Assert.Equal(120, vm.TakipOzeti!.KartBorcu); Assert.Equal(7, vm.TakipOzetiGunu);
    }

    [Fact]
    public async Task Takip_ozeti_panel_yanitindan_istek_atmadan_yansir_eski_sunucuda_ve_farkli_gunde_ayrica_istenir()
    {
        var takip = new FinansTakipTests.Fake { Ozet = Ozet(80) };
        var vm = new TakipOzetViewModel(takip, Editor());

        await vm.PaneldenYukleAsync(Ozet(120), 30);
        Assert.Equal(0, takip.OzetCagri);
        Assert.True(vm.VeriHazir); Assert.Contains("120,00", vm.Ozet); Assert.Equal(120, vm.KanalKartBorclari!.Single().Tutar);

        await vm.PaneldenYukleAsync(null, 30);                     // eski sunucu: ana sayfa yanıtında özet yok
        Assert.Equal(1, takip.OzetCagri); Assert.Contains("80,00", vm.Ozet);

        vm.Gun = 7;                                                 // panel yüklenirken gün değişti: 30 günlük özet yansıtılmaz
        await vm.PaneldenYukleAsync(Ozet(120), 30);
        Assert.Equal(2, takip.OzetCagri); Assert.Equal(7, takip.SonOzetGunu); Assert.Contains("80,00", vm.Ozet);
    }

    [Fact]
    public async Task Kasa_kontrolu_panelin_esikleriyle_yalniz_gecmisi_ister_esik_yoksa_ayrica_ister()
    {
        var kontrol = new KontrolSahtesi();
        var vm = new KasaKontrolViewModel(kontrol, Editor());

        await vm.YukleAsync(new[] { Esik });
        Assert.Equal((1, 0), (kontrol.GecmisCagri, kontrol.EsikCagri));
        Assert.Contains("MEZAT: bakiye 600,00 ₺", vm.EsikUyarilari); Assert.True(vm.VeriHazir);

        await vm.YukleAsync();
        Assert.Equal((2, 1), (kontrol.GecmisCagri, kontrol.EsikCagri));
        Assert.Contains("Açık uyarılarda alt limitin altında kanal yok.", vm.EsikUyarilari);
    }

    [Fact]
    public async Task Yeni_panel_yuklemesi_onceki_istegi_iptal_eder_ekrandan_ayrilinca_istek_birakilir_hata_gosterilmez()
    {
        var belirtecler = new List<CancellationToken>();
        var api = new SahteApi { AnaSayfaGetir = async (_, ct) => { belirtecler.Add(ct); await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); } };
        var vm = new PanelViewModel(api);

        var ilk = vm.YukleAsync();
        var ikinci = vm.YukleAsync();
        Assert.True(belirtecler[0].IsCancellationRequested); Assert.False(belirtecler[1].IsCancellationRequested);
        Assert.True(vm.Mesgul);

        vm.EkrandanAyril();
        await Task.WhenAll(ilk, ikinci);

        Assert.True(belirtecler[1].IsCancellationRequested);
        Assert.Null(vm.Hata); Assert.False(vm.Mesgul); Assert.False(vm.VeriVar);
    }

    [Fact]
    public async Task Iptal_edilen_eski_istegin_gec_yaniti_yeni_paneli_ezmez()
    {
        var eski = new TaskCompletionSource<AnaSayfaDto>();
        var cagri = 0;
        var api = new SahteApi { AnaSayfaGetir = (_, _) => ++cagri == 1 ? eski.Task : Task.FromResult(new AnaSayfaDto(Panel(500), null, null)) };
        var vm = new PanelViewModel(api);

        var ilk = vm.YukleAsync();
        await vm.YukleAsync();
        eski.SetResult(new AnaSayfaDto(Panel(900), new[] { Esik }, Ozet()));   // sahte iptali dinlemese de sonuç uygulanmaz
        await ilk;

        Assert.Equal(500, vm.GuncelKasa); Assert.Null(vm.KasaEsikleri); Assert.Null(vm.TakipOzeti); Assert.True(vm.VeriVar);
    }

    [Fact]
    public async Task Haftalik_rapor_iptal_belirteciyle_istenir_ve_veri_sagligi_uyarisini_gosterir()
    {
        CancellationToken gorulen = default;
        var api = new SahteApi { HaftalikGetir = ct => { gorulen = ct; return Task.FromResult<IReadOnlyList<HaftalikOzetDto>>(new[] { Hafta(20), Hafta(27, "Rapor ufkunun (30.09.2027) ötesinde 1 kayıt var.") }); } };
        var vm = new HaftalikViewModel(api);

        await vm.YukleAsync();

        Assert.True(gorulen.CanBeCanceled);
        Assert.Equal("Rapor ufkunun (30.09.2027) ötesinde 1 kayıt var.", vm.VeriSagligiUyarisi);

        api.HaftalikGetir = _ => Task.FromResult<IReadOnlyList<HaftalikOzetDto>>(new[] { Hafta(20), Hafta(27) });
        await vm.YukleAsync();
        Assert.Null(vm.VeriSagligiUyarisi);

        api.HaftalikGetir = _ => Task.FromException<IReadOnlyList<HaftalikOzetDto>>(new HttpRequestException());
        await vm.YukleAsync();
        Assert.Null(vm.VeriSagligiUyarisi); Assert.False(vm.VeriVar);
    }

    [Fact]
    public async Task Haftalik_ekrandan_ayrilinca_istek_iptal_edilir()
    {
        CancellationToken gorulen = default;
        var api = new SahteApi { HaftalikGetir = async ct => { gorulen = ct; await Task.Delay(Timeout.Infinite, ct); return Array.Empty<HaftalikOzetDto>(); } };
        var vm = new HaftalikViewModel(api);

        var yukleme = vm.YukleAsync();
        vm.EkrandanAyril();
        await yukleme;

        Assert.True(gorulen.IsCancellationRequested); Assert.Null(vm.Hata); Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Rapor_okumasinin_zaman_asimi_islem_tamamlanmis_olabilir_demez()
    {
        var vm = new PanelViewModel(new SahteApi { YuklemeHatasi = new TimeoutException(KasaZamanAsimlari.Ileti) });

        await vm.YukleAsync();

        Assert.Contains("zamanında yanıt vermedi", vm.Hata);
        Assert.DoesNotContain("tamamlanmış olabilir", vm.Hata);
        Assert.DoesNotContain("ulaşılamadı", vm.Hata);
    }

    private sealed class KontrolSahtesi : IKasaKontrolApi
    {
        public int GecmisCagri, EsikCagri;
        public Task<IReadOnlyList<KasaKontrolDto>> KasaKontrolleriAsync() { GecmisCagri++; return Task.FromResult<IReadOnlyList<KasaKontrolDto>>(Array.Empty<KasaKontrolDto>()); }
        public Task<IReadOnlyList<KasaEsikDto>> KasaEsikleriAsync() { EsikCagri++; return Task.FromResult<IReadOnlyList<KasaEsikDto>>(new[] { Esik with { EsikAltinda = false } }); }
        public Task<KasaEsikDto> KasaEsigiKaydetAsync(int kanalId, KasaEsikYaz girdi) => throw new NotSupportedException();
        public Task<KasaKontrolOnizlemeDto> KasaKontrolOnizleAsync(KasaKontrolOnizle girdi) => throw new NotSupportedException();
        public Task<KasaKontrolDto> KasaKontrolKaydetAsync(KasaKontrolYaz girdi) => throw new NotSupportedException();
        public Task<KartMasrafOnizlemeDto> KartMasrafOnizleAsync(int kartId, KartMasrafYaz girdi) => throw new NotSupportedException();
        public Task<KartTakipDto> KartMasrafKaydetAsync(int kartId, KartMasrafYaz girdi) => throw new NotSupportedException();
    }
}
