using System.ComponentModel;
using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class KasaSayimiViewModelTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);
    private static readonly DateOnly BugunT = DateOnly.FromDateTime(Bugun);

    private static KasaSayimDto Sayim(int id, DateOnly t, decimal sayilan, decimal hesaplanan, decimal? guncel, string? not = null)
        => new(id, t, sayilan, hesaplanan, sayilan - hesaplanan, guncel, not, new DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc));

    private static (SahteApi api, KasaSayimiViewModel vm, SabitSaat saat) Kur(Action<SahteApi>? ayar = null)
    {
        var api = new SahteApi { KasaHesapSonuc = 10_900m };
        ayar?.Invoke(api);
        var saat = new SabitSaat(Bugun.AddHours(10));
        return (api, new KasaSayimiViewModel(api, saat) { EditorMu = true }, saat);
    }

    [Fact]
    public void Kasa_sayimi_her_iki_rolde_panelden_hemen_sonra()
    {
        foreach (var rol in new[] { Rol.Izleyici, Rol.Editor })
        {
            var b = SekmeModeli.Bolumler(rol);
            Assert.Equal(Bolum.Panel, b[0]);
            Assert.Equal(Bolum.KasaSayimi, b[1]);
        }
    }

    [Fact]
    public async Task Yukle_gecmisi_ve_bugunun_defterini_getirir()
    {
        var (api, vm, _) = Kur(a => a.KasaSayimlariListe = new[]
        {
            Sayim(2, new(2026, 9, 22), 11_550.50m, 11_600m, 11_600m, "Akşam"),
            Sayim(1, new(2026, 9, 1), 100m, 100m, 100m),
        });

        await vm.YukleAsync();

        Assert.Equal([2, 1], vm.Sayimlar.Select(s => s.Id));
        Assert.Equal([BugunT], api.KasaHesaplaCagrilari);
        Assert.Equal(10_900m, vm.DefterTutari);
        Assert.Equal("10.900,00", vm.DefterYazi);
        Assert.Equal(Bugun, vm.FormTarih);
        Assert.Null(vm.Hata);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Fark_canli_hesaplanir_sifirsa_yesil_degilse_kirmizi()
    {
        var (_, vm, _) = Kur();
        await vm.YukleAsync();
        var degisen = new List<string?>();
        vm.PropertyChanged += (_, e) => degisen.Add(e.PropertyName);

        vm.SayilanTutar = 10_900m;
        Assert.Equal(0m, vm.Fark);
        Assert.Equal(0m, vm.FarkRenkDegeri);          // ParaRenk: 0 → yeşil
        Assert.Equal("0,00", vm.FarkYazi);
        Assert.Equal("Kasa defterle uyuşuyor.", vm.FarkMetni);
        Assert.Contains(nameof(vm.Fark), degisen);
        Assert.Contains(nameof(vm.FarkRenkDegeri), degisen);

        vm.SayilanTutar = 10_850m;
        Assert.Equal(-50m, vm.Fark);
        Assert.True(vm.FarkRenkDegeri < 0);            // eksik → kırmızı
        Assert.Equal("-50,00", vm.FarkYazi);
        Assert.Equal("Kasada defterden 50,00 ₺ eksik var.", vm.FarkMetni);

        vm.SayilanTutar = 11_000.25m;
        Assert.Equal(100.25m, vm.Fark);
        Assert.True(vm.FarkRenkDegeri < 0);            // fazla da kırmızı
        Assert.Equal("+100,25", vm.FarkYazi);
        Assert.Equal("Kasada defterden 100,25 ₺ fazla var.", vm.FarkMetni);
    }

    [Fact]
    public void Defter_bilinmeden_fark_gosterilmez()
    {
        var (_, vm, _) = Kur(a => a.KasaHesaplaUret = _ => new TaskCompletionSource<KasaHesapDto>().Task);
        _ = vm.YukleAsync();
        vm.SayilanTutar = 5m;
        Assert.Null(vm.DefterTutari);
        Assert.False(vm.FarkGorunur);
        Assert.Equal("", vm.FarkYazi);
        Assert.Equal("—", vm.DefterYazi);
    }

    [Fact]
    public async Task Tarih_degisince_o_gunun_defteri_istenir()
    {
        var (api, vm, _) = Kur(a => a.KasaHesaplaUret = t => Task.FromResult(new KasaHesapDto(t, t.Day * 100m)));
        await vm.YukleAsync();
        vm.SayilanTutar = 2_000m;

        vm.FormTarih = new DateTime(2026, 9, 22);

        Assert.Equal(new DateOnly(2026, 9, 22), api.KasaHesaplaCagrilari[^1]);
        Assert.Equal(2_200m, vm.DefterTutari);
        Assert.Equal(-200m, vm.Fark);
    }

    [Fact]
    public async Task Gec_gelen_eski_tarih_yaniti_yeni_tarihi_ezmez()
    {
        var bekleyen = new Dictionary<DateOnly, TaskCompletionSource<KasaHesapDto>>();
        var (api, vm, _) = Kur(a => a.KasaHesaplaUret = t =>
        {
            var tcs = new TaskCompletionSource<KasaHesapDto>();
            bekleyen[t] = tcs;
            return tcs.Task;
        });

        vm.FormTarih = new DateTime(2026, 9, 20);
        vm.FormTarih = new DateTime(2026, 9, 21);
        bekleyen[new(2026, 9, 21)].SetResult(new KasaHesapDto(new(2026, 9, 21), 21m));
        bekleyen[new(2026, 9, 20)].SetResult(new KasaHesapDto(new(2026, 9, 20), 20m));
        await Task.Yield();

        Assert.Equal(21m, vm.DefterTutari);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Ileri_tarih_sunucuya_sorulmaz_kaydedilmez()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        var once = api.KasaHesaplaCagrilari.Count;

        vm.FormTarih = Bugun.AddDays(1);
        Assert.Equal(KasaSayimiViewModel.IleriTarihMesaji, vm.Hata);
        Assert.Null(vm.DefterTutari);
        Assert.Equal(once, api.KasaHesaplaCagrilari.Count);

        vm.SayilanTutar = 5m;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(KasaSayimiViewModel.IleriTarihMesaji, vm.Hata);
        Assert.Null(api.SonKasaSayimKaydet);
    }

    [Fact]
    public async Task Takip_oncesi_tarihte_sunucu_mesaji_gosterilir()
    {
        var (api, vm, _) = Kur(a => a.KasaHesaplaUret = t => t < new DateOnly(2026, 7, 1)
            ? Task.FromException<KasaHesapDto>(new KasaApiException(HttpStatusCode.BadRequest, "Sayım tarihi takip başlangıcından (01.07.2026) önce olamaz."))
            : Task.FromResult(new KasaHesapDto(t, 1m)));
        await vm.YukleAsync();

        vm.FormTarih = new DateTime(2026, 6, 30);

        Assert.Equal("Sayım tarihi takip başlangıcından (01.07.2026) önce olamaz.", vm.Hata);
        Assert.Null(vm.DefterTutari);

        vm.FormTarih = new DateTime(2026, 7, 1);   // geçerli tarih seçilince hata kalkar
        Assert.Null(vm.Hata);
        Assert.Equal(1m, vm.DefterTutari);
    }

    [Fact]
    public async Task Kaydet_gonderir_formu_sifirlar_gecmisi_yeniler()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        vm.FormTarih = new DateTime(2026, 9, 22);
        vm.SayilanTutar = 11_550.50m;
        vm.Not = "  Akşam sayımı  ";
        var listeOnce = api.KasaSayimlariCagri;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal(new KasaSayimYaz(new DateOnly(2026, 9, 22), 11_550.50m, "Akşam sayımı"), api.SonKasaSayimKaydet);
        Assert.Equal(listeOnce + 1, api.KasaSayimlariCagri);
        var s = Assert.Single(vm.Sayimlar);
        Assert.Equal(new DateOnly(2026, 9, 22), s.Tarih);
        // Form: bugün, boş tutar ve not; bugünün defteri yeniden istendi.
        Assert.Equal(Bugun, vm.FormTarih);
        Assert.Equal(0m, vm.SayilanTutar);
        Assert.Null(vm.Not);
        Assert.Equal(BugunT, api.KasaHesaplaCagrilari[^1]);
        Assert.Equal(10_900m, vm.DefterTutari);
    }

    [Fact]
    public async Task Bos_not_null_gider()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        vm.SayilanTutar = 1m;
        vm.Not = "   ";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonKasaSayimKaydet!.Not);
    }

    [Fact]
    public async Task Kayit_hatasinda_form_korunur()
    {
        var (api, vm, _) = Kur(a => a.KasaSayimYazHatasi = new KasaApiException(HttpStatusCode.Forbidden));
        await vm.YukleAsync();
        vm.SayilanTutar = 123m;
        vm.Not = "n";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(HataMesaji.Yetkisiz, vm.Hata);
        Assert.Equal(123m, vm.SayilanTutar);
        Assert.Equal("n", vm.Not);
    }

    [Fact]
    public async Task Sil_gecmisten_kaldirir()
    {
        var (api, vm, _) = Kur(a => a.KasaSayimlariListe = new[]
        {
            Sayim(2, new(2026, 9, 22), 1m, 1m, 1m), Sayim(1, new(2026, 9, 1), 1m, 1m, 1m),
        });
        await vm.YukleAsync();

        await vm.SilCommand.ExecuteAsync(vm.Sayimlar[0]);

        Assert.Equal(2, api.SonKasaSayimSil);
        Assert.Equal([1], vm.Sayimlar.Select(s => s.Id));
    }

    [Fact]
    public void Satir_metinleri_ve_defter_degisti_uyarisi()
    {
        var ayni = new KasaSayimSatiri(Sayim(1, new(2026, 9, 22), 11_550.50m, 11_600m, 11_600m, "Akşam"));
        Assert.Equal(-49.50m, ayni.Fark);
        Assert.Equal("-49,50", ayni.FarkYazi);
        Assert.True(ayni.FarkRenkDegeri < 0);
        Assert.True(ayni.NotVar);
        Assert.Equal("", ayni.DefterDegistiMetni);

        var degisti = new KasaSayimSatiri(Sayim(2, new(2026, 9, 22), 11_600m, 11_600m, 11_500m));
        Assert.Equal(0m, degisti.FarkRenkDegeri);   // kayıttaki fark 0: yeşil
        Assert.Equal("Defter sonradan değişti: bugünkü değer 11.500,00 ₺, güncel fark +100,00 ₺.", degisti.DefterDegistiMetni);
        Assert.False(degisti.NotVar);

        var disarida = new KasaSayimSatiri(Sayim(3, new(2026, 6, 30), 1m, 1m, null));
        Assert.Contains("takip döneminin dışında", disarida.DefterDegistiMetni);
    }

    [Fact]
    public async Task Gece_yarisindan_sonra_dokunulmamis_tarih_bugune_kayar()
    {
        var (api, vm, saat) = Kur();
        await vm.YukleAsync();
        saat.Ilerle(TimeSpan.FromDays(1));

        await vm.YukleAsync();

        Assert.Equal(Bugun.AddDays(1), vm.FormTarih);
        Assert.Equal(BugunT.AddDays(1), api.KasaHesaplaCagrilari[^1]);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Yukleme_hatasi_gosterilir()
    {
        var (api, vm, _) = Kur(a => a.YuklemeHatasi = new HttpRequestException("yok"));
        await vm.YukleAsync();
        Assert.Equal(HataMesaji.Ulasilamadi, vm.Hata);
        Assert.False(vm.Mesgul);
    }
}
