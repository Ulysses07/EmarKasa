using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Ortak form kaydı (TemelViewModel.FormIsleAsync) ve okuma hatasının bağlantı kopukken sayfaya yazılmaması.</summary>
public partial class FormIsleTests
{
    private sealed partial class DenemeVm : TemelViewModel
    {
        public AlanHatalari Hatalar { get; } = new();
        [ObservableProperty] private string _ad = "";
        [ObservableProperty] private decimal _tutar;
        public bool Kopuk { get; set; }
        protected override bool BaglantiKopuk => Kopuk;
        protected override IEnumerable<AlanHatalari> Formlar => [Hatalar];
        public static readonly Dictionary<string, string> Eslem = new() { ["ad"] = nameof(Ad), ["tutar"] = nameof(Tutar) };

        public Task KaydetAsync(Func<Task> istek) => FormIsleAsync(Hatalar, async _ =>
        {
            Hatalar.Denetle(!string.IsNullOrWhiteSpace(Ad), nameof(Ad), "Ad boş olamaz.");
            Hatalar.Denetle(Tutar > 0, nameof(Tutar), "Tutar sıfırdan büyük olmalı.");
            if (Hatalar.Var)
                return;
            await istek();
        }, Eslem);

        public Task OkuAsync(Exception hata) => new SonIstekHatti(Yurutucu).YukleAsync<int>(_ => Task.FromException<int>(hata), _ => { });
    }

    private static (DenemeVm Vm, List<int> Gosterim) Kur()
    {
        var vm = new DenemeVm();
        var gosterim = new List<int>();
        vm.Hatalar.GosterIstendi += (_, _) => gosterim.Add(1);
        return (vm, gosterim);
    }

    [Fact]
    public async Task On_dogrulama_istek_gondermez_alanlara_yazar_ve_kaydirma_ister()
    {
        var (vm, gosterim) = Kur();
        var gonderildi = false;

        await vm.KaydetAsync(() => { gonderildi = true; return Task.CompletedTask; });

        Assert.False(gonderildi);
        Assert.Equal("Ad boş olamaz.", vm.Hatalar[nameof(DenemeVm.Ad)]);
        Assert.Equal("Tutar sıfırdan büyük olmalı.", vm.Hatalar[nameof(DenemeVm.Tutar)]);
        Assert.Null(vm.Hata);
        Assert.False(vm.Mesgul);
        Assert.Single(gosterim);
    }

    [Fact]
    public async Task Alan_degisince_o_alanin_hatasi_kalkar_kayit_basinda_hepsi_kalkar()
    {
        var (vm, _) = Kur();
        await vm.KaydetAsync(() => Task.CompletedTask);

        vm.Ad = "Ege Gıda";
        Assert.Null(vm.Hatalar[nameof(DenemeVm.Ad)]);
        Assert.NotNull(vm.Hatalar[nameof(DenemeVm.Tutar)]);

        vm.Hatalar.Genel = "eski";
        vm.Tutar = 5m;
        await vm.KaydetAsync(() => Task.CompletedTask);
        Assert.False(vm.Hatalar.Var);
    }

    [Fact]
    public async Task Sunucu_alan_hatalari_eslenir_eslenmeyen_genel_hataya_gider()
    {
        var (vm, gosterim) = Kur();
        vm.Ad = "x";
        vm.Tutar = 1m;
        var alanlar = new Dictionary<string, string> { ["ad"] = "Bu alan boş olamaz.", ["istekid"] = "Geçerli bir istek kimliği gerekir." };

        await vm.KaydetAsync(() => throw new KasaApiException(HttpStatusCode.BadRequest, "birleşik", alanHatalari: alanlar));

        Assert.Equal("Bu alan boş olamaz.", vm.Hatalar[nameof(DenemeVm.Ad)]);
        Assert.Equal("Geçerli bir istek kimliği gerekir.", vm.Hatalar.Genel);
        Assert.Null(vm.Hata);
        Assert.Single(gosterim);
    }

    [Fact]
    public async Task Alan_sozlugu_olmayan_ret_genel_hataya_iletisiyle_gider()
    {
        var (vm, _) = Kur();
        vm.Ad = "x";
        vm.Tutar = 1m;

        await vm.KaydetAsync(() => throw new KasaApiException(HttpStatusCode.Conflict, "Kayıt başka oturumda değişti."));

        Assert.Equal("Kayıt başka oturumda değişti.", vm.Hatalar.Genel);
    }

    [Fact]
    public async Task Ag_hatasinda_kayit_yapilmadi_zaman_asiminda_kontrol_istenir_form_degerleri_korunur()
    {
        var (vm, _) = Kur();
        vm.Ad = "Ege Gıda";
        vm.Tutar = 150m;

        await vm.KaydetAsync(() => throw new HttpRequestException());
        Assert.Equal(Yurutucu.KayitBaglantiIletisi, vm.Hatalar.Genel);
        Assert.Equal(("Ege Gıda", 150m), (vm.Ad, vm.Tutar));

        await vm.KaydetAsync(() => throw new TimeoutException(KasaZamanAsimlari.Ileti));
        Assert.Contains("tamamlanmış olabilir", vm.Hatalar.Genel);
    }

    [Fact]
    public async Task Baglanti_kopukken_okumanin_baglanti_hatasi_sayfaya_yazilmaz_diger_hatalar_yazilir()
    {
        var vm = new DenemeVm { Kopuk = true };
        await vm.OkuAsync(new HttpRequestException());
        Assert.Null(vm.Hata);
        await vm.OkuAsync(new TimeoutException(KasaZamanAsimlari.Ileti));
        Assert.Null(vm.Hata);
        await vm.OkuAsync(new KasaApiException(HttpStatusCode.Forbidden));
        Assert.Equal("Bu işlem için yetkiniz yok.", vm.Hata);

        vm.Kopuk = false;
        await vm.OkuAsync(new HttpRequestException());
        Assert.Equal("Sunucuya ulaşılamadı. Bağlantıyı kontrol edip yeniden deneyin.", vm.Hata);
    }
}
