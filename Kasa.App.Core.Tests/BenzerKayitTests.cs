using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class BenzerKayitTests
{
    private static readonly DateOnly Tarih = new(2026, 9, 24);
    [Fact] public async Task Onay_yalniz_gosterilen_govdeye_aittir_form_degisince_yenilenir()
    {
        var api = new Fake(); var kontrol = new BenzerKayitKontrolu(api); var arama = new BenzerlikYaz("Gider", Tarih, 100, Kanal: "MEZAT");
        Assert.False(await kontrol.DevamEdilebilirAsync(arama, new { Not = "ilk" }, () => true));
        Assert.Contains("Gider #7", kontrol.Uyari); Assert.True(kontrol.Onayla());
        Assert.True(await kontrol.DevamEdilebilirAsync(arama, new { Not = "ilk" }, () => true)); Assert.Equal(1, api.Cagri);
        Assert.False(await kontrol.DevamEdilebilirAsync(arama, new { Not = "değişti" }, () => true)); Assert.Equal(2, api.Cagri);
        Assert.True(kontrol.UyariVar);
    }
    [Fact] public async Task Oturum_degistiginde_bekleyen_benzerlik_yaniti_gosterilmez()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<BenzerKayitDto>>(); var api = new Fake { Bekleyen = tcs.Task }; var kontrol = new BenzerKayitKontrolu(api); var gecerli = true;
        var islem = kontrol.DevamEdilebilirAsync(new("Gider", Tarih, 100), new { Tutar = 100 }, () => gecerli);
        gecerli = false; kontrol.Temizle(); tcs.SetResult(Fake.Eslesmeler); Assert.False(await islem); Assert.False(kontrol.UyariVar); Assert.False(kontrol.Onayla());
    }
    [Fact] public async Task Gider_uyariyi_onaylamadan_kaydedilmez_ve_hata_kayda_izin_vermez()
    {
        var finans = new SahteApi(); var lookup = new Fake(); var vm = new IslemlerViewModel(finans, lookup) { DuzenTarih = Tarih.ToDateTime(TimeOnly.MinValue), DuzenCari = "Mal", DuzenTutar = 100, DuzenKanal = "MEZAT" };
        await vm.KaydetCommand.ExecuteAsync(null); Assert.Null(finans.SonIslemOlustur); Assert.True(vm.GiderBenzerlik.UyariVar);
        await vm.GideriAyriKaydetCommand.ExecuteAsync(null); Assert.Equal(100, finans.SonIslemOlustur!.TutarTl); Assert.Equal(1, lookup.Cagri);
        var hatali = new IslemlerViewModel(new SahteApi(), new Fake { Hata = true }) { DuzenTutar = 100 };
        await hatali.KaydetCommand.ExecuteAsync(null); Assert.Contains("ulaşılamadı", hatali.Hata); Assert.False(hatali.GiderBenzerlik.Onayla());
    }
    [Fact] public async Task Mevcut_gider_duzeltmesinde_yeni_kayit_uyarisi_sorulmaz()
    {
        var finans = new SahteApi(); var lookup = new Fake(); var vm = new IslemlerViewModel(finans, lookup);
        vm.Duzenle(new(3, Tarih, "Mal", 100, "MEZAT", GiderTipi.Cari, null));
        await vm.KaydetCommand.ExecuteAsync(null); Assert.Equal(3, finans.SonIslemGuncelle!.Value.Id); Assert.Equal(0, lookup.Cagri);
    }
    [Fact] public async Task Devam_ve_kaydet_dugmeleri_eszamanli_ikinci_gider_yaratmaz()
    {
        var bekleyen = new TaskCompletionSource<IslemDto>(); var finans = new SahteApi { IslemKayitYaniti = bekleyen.Task };
        var vm = new IslemlerViewModel(finans, new Fake()) { DuzenTutar = 100, DuzenCari = "Mal", DuzenKanal = "MEZAT" };
        await vm.KaydetCommand.ExecuteAsync(null); var devam = vm.GideriAyriKaydetCommand.ExecuteAsync(null);
        Assert.True(vm.Mesgul); await vm.KaydetCommand.ExecuteAsync(null); Assert.Equal(1, finans.IslemOlusturCagri);
        bekleyen.SetResult(new(1, Tarih, "Mal", 100, "MEZAT", GiderTipi.Cari, null)); await devam;
        Assert.Equal(1, finans.IslemOlusturCagri);
    }
    [Theory]
    [InlineData(null, null, "girilmedi")]
    [InlineData(20, null, "alınamadı")]
    [InlineData(20, 0, "asgari tamamlandı")]
    [InlineData(20, 5, "asgariye kalan")]
    public void Asgari_etiketi_kalan_ekstre_borcunu_kapatilmis_saymaz(int? asgari, int? kalan, string metin)
    {
        var satir = new EkstreSatiri(new(1, Tarih, Tarih.AddDays(10), 100, 20, 80, asgari, kalan));
        Assert.Contains(metin, satir.AsgariDurumu); Assert.Contains("80,00", satir.Ozet);
    }
    [Fact] public void Alis_odeme_satirinda_kart_adi_ve_eski_kart_kimligi_gosterilir()
    {
        var odeme = new AlisOdemeDto(1, 2, Tarih, 100, 4, false, Array.Empty<AlisDagilimDto>(), KrediKartiAdi: "Banka Kartı");
        Assert.Contains("Banka Kartı", new AlisOdemeSatiri(odeme).Baslik); Assert.True(new AlisOdemeSatiri(odeme).KartVar);
        Assert.Contains("Kart #4", new AlisOdemeSatiri(odeme with { KrediKartiAdi = null }).Baslik);
    }
    public sealed class Fake : IBenzerKayitApi
    {
        public int Cagri; public bool Hata; public Task<IReadOnlyList<BenzerKayitDto>>? Bekleyen;
        public BenzerlikYaz? SonArama;
        public static IReadOnlyList<BenzerKayitDto> Eslesmeler => new[] { new BenzerKayitDto("Islem", 7, Tarih, 100, "Mal", null, null) };
        public Task<IReadOnlyList<BenzerKayitDto>> BenzerKayitlarAsync(BenzerlikYaz g) { Cagri++; SonArama = g; return Hata ? Task.FromException<IReadOnlyList<BenzerKayitDto>>(new HttpRequestException()) : Bekleyen ?? Task.FromResult(Eslesmeler); }
    }
}
