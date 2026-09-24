using System.Globalization;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Kredi kartları sayfası: D1 (N+1, kaybolan giriş), ödeme doğrulaması, açılış borcu ipucu.</summary>
public class KrediKartlariDuzeltmeTests
{
    private static KrediKartiDto Kart(int id, string ad) =>
        new(id, ad, new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 25), 10000m, 0m);

    [Fact]
    public async Task Tum_odemeler_tek_istekte_gelir_kart_basina_istek_yok()
    {
        var api = new SahteApi
        {
            KrediKartlariListe = Enumerable.Range(1, 5).Select(i => Kart(i, $"K{i}")).ToList(),
            KartOdemelerListe = new[]
            {
                new KartOdemeDto(1, 2, new DateOnly(2026, 9, 1), 100m, null),
                new KartOdemeDto(2, 2, new DateOnly(2026, 9, 10), 200m, null),
                new KartOdemeDto(3, 4, new DateOnly(2026, 9, 3), 300m, null),
            },
        };
        var vm = new KrediKartlariViewModel(api);

        await vm.YukleAsync();

        Assert.Equal(1, api.KrediKartlariCagri);
        Assert.Equal(1, api.TumKartOdemeleriCagri);
        Assert.Equal(0, api.KartOdemelerCagri);
        Assert.Equal(new[] { 2, 1 }, vm.Kartlar.Single(k => k.Id == 2).Odemeler.Select(o => o.Id));   // en yeni önce
        Assert.Single(vm.Kartlar.Single(k => k.Id == 4).Odemeler);
        Assert.Empty(vm.Kartlar.Single(k => k.Id == 1).Odemeler);
    }

    [Fact]
    public async Task Bir_karta_odeme_eklenince_diger_kartin_kaydedilmemis_girisi_korunur()
    {
        var api = new SahteApi { KrediKartlariListe = new[] { Kart(1, "A"), Kart(2, "B") } };
        var vm = new KrediKartlariViewModel(api);
        await vm.YukleAsync();
        var a = vm.Kartlar.Single(k => k.Id == 1);
        var b = vm.Kartlar.Single(k => k.Id == 2);
        b.OdemeTutarGiris = 250m;
        b.OdemeTarihGiris = new DateTime(2026, 9, 20);
        a.OdemeTutarGiris = 100m;

        await vm.OdemeEkleCommand.ExecuteAsync(a);

        Assert.Equal(100m, api.SonKartOdemeKaydet!.Tutar);
        var yeniA = vm.Kartlar.Single(k => k.Id == 1);
        var yeniB = vm.Kartlar.Single(k => k.Id == 2);
        Assert.Equal(0m, yeniA.OdemeTutarGiris);            // kaydedilen kartın girişi temizlendi
        Assert.Equal(250m, yeniB.OdemeTutarGiris);           // diğeri korunur (P09: eskiden 0 oluyordu)
        Assert.Equal(new DateTime(2026, 9, 20), yeniB.OdemeTarihGiris);
    }

    [Fact]
    public async Task Yukleme_hatasinda_mevcut_liste_yarim_kalmaz()
    {
        var api = new SahteApi { KrediKartlariListe = new[] { Kart(1, "A"), Kart(2, "B") } };
        var vm = new KrediKartlariViewModel(api);
        await vm.YukleAsync();

        api.YuklemeHatasi = new HttpRequestException("kopuk");
        await vm.YukleAsync();

        Assert.Equal(2, vm.Kartlar.Count);
        Assert.NotNull(vm.Hata);
    }

    [Fact]
    public async Task Sifir_tutarli_odeme_gonderilmez()
    {
        var api = new SahteApi { KrediKartlariListe = new[] { Kart(1, "A") } };
        var vm = new KrediKartlariViewModel(api);
        await vm.YukleAsync();

        await vm.OdemeEkleCommand.ExecuteAsync(vm.Kartlar[0]);

        Assert.Null(api.SonKartOdemeKaydet);
        Assert.Equal("Ödeme tutarı sıfırdan büyük olmalı.", vm.Hata);
    }

    [Fact]
    public async Task Odeme_tarihi_varsayilani_gunu_gelince_tazelenir()
    {
        var saat = new SabitSaat(new DateTime(2026, 9, 24, 23, 0, 0));
        var api = new SahteApi { KrediKartlariListe = new[] { Kart(1, "A") } };
        var vm = new KrediKartlariViewModel(api, saat);
        await vm.YukleAsync();
        Assert.Equal(new DateTime(2026, 9, 24), vm.Kartlar[0].OdemeTarihGiris);

        saat.Ilerle(TimeSpan.FromHours(2));
        await vm.YukleAsync();

        Assert.Equal(new DateTime(2026, 9, 25), vm.Kartlar[0].OdemeTarihGiris);
    }

    [Fact]
    public async Task Duzenlenen_kart_silinince_form_sifirlanir()
    {
        var api = new SahteApi();
        var vm = new KrediKartlariViewModel(api);
        var k = new KrediKartiGorunum(Kart(9, "M"));
        vm.Duzenle(k);

        await vm.SilCommand.ExecuteAsync(k);

        Assert.Equal(0, vm.DuzenId);
        Assert.Equal("", vm.DuzenAd);
    }

    [Fact]
    public void Acilis_borcu_ipucu_cift_dusum_riskini_anlatir()
    {
        Assert.Contains("kartsız", KrediKartlariViewModel.AcilisBorcuIpucu);
        Assert.Contains("iki kez", KrediKartlariViewModel.AcilisBorcuIpucu);
    }
}

/// <summary>D9: "Tüm oturumları kapat" onayı ~5 sn sonra geçersiz olur.</summary>
public class OturumKapatOnayTests
{
    [Fact]
    public async Task Onay_5_saniye_icinde_ikinci_basista_calisir()
    {
        var saat = new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0));
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api, saat);

        await vm.OturumlariKapatCommand.ExecuteAsync(null);
        saat.Ilerle(TimeSpan.FromSeconds(3));
        await vm.OturumlariKapatCommand.ExecuteAsync(null);

        Assert.Equal(1, api.OturumKapatSayisi);
    }

    [Fact]
    public async Task Sure_dolduktan_sonraki_basis_yeniden_onay_ister()
    {
        var saat = new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0));
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api, saat);

        await vm.OturumlariKapatCommand.ExecuteAsync(null);
        saat.Ilerle(TimeSpan.FromDays(2));                  // sayfa önbellekte günlerce kaldı
        await vm.OturumlariKapatCommand.ExecuteAsync(null);

        Assert.Equal(0, api.OturumKapatSayisi);
        Assert.True(vm.OturumKapatOnayBekliyor);
    }

    [Fact]
    public async Task Onay_bir_sure_sonra_kendiliginden_kalkar()
    {
        var vm = new AyarlarViewModel(new SahteApi());
        await vm.OturumlariKapatCommand.ExecuteAsync(null);
        Assert.True(vm.OturumKapatOnayBekliyor);

        var bitis = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (vm.OturumKapatOnayBekliyor && DateTime.UtcNow < bitis) await Task.Delay(100);

        Assert.False(vm.OturumKapatOnayBekliyor);
        Assert.Equal("Tüm oturumları kapat", vm.OturumKapatMetni);
    }

    [Fact]
    public async Task Sayfa_yeniden_acilinca_onay_sifirlanir()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false) };
        var vm = new AyarlarViewModel(api);
        await vm.OturumlariKapatCommand.ExecuteAsync(null);

        await vm.YukleAsync();

        Assert.False(vm.OturumKapatOnayBekliyor);
    }
}

/// <summary>O2 (aylık): hızlı ay değişiminde eski ay raporu yenisini ezmez.</summary>
public class AylikYarisTests
{
    [Fact]
    public async Task Gec_gelen_eski_ay_raporu_yok_sayilir()
    {
        var bekleyen = new Dictionary<int, TaskCompletionSource<AylikRaporDto>>();
        var api = new SahteApi
        {
            AylikUret = (_, ay) => (bekleyen[ay] = new TaskCompletionSource<AylikRaporDto>()).Task,
        };
        var vm = new AylikViewModel(api, new SabitSaat(new DateTime(2026, 9, 24)));

        var t1 = vm.OncekiAyCommand.ExecuteAsync(null);   // Ağustos
        var t2 = vm.OncekiAyCommand.ExecuteAsync(null);   // Temmuz
        bekleyen[7].SetResult(new AylikRaporDto(2026, 7, new List<KanalAylikDto>()));
        await t2;
        bekleyen[8].SetResult(new AylikRaporDto(2026, 8, new List<KanalAylikDto>()));
        await t1;

        Assert.Equal(7, vm.Ay);
        Assert.Equal(7, vm.Rapor!.Ay);
        Assert.False(vm.Mesgul);
    }
}

/// <summary>D11: cari arama.</summary>
public class CariAramaTests
{
    [Fact]
    public async Task Ara_komutu_kirpilmis_metinle_arar()
    {
        var api = new SahteApi { CarilerListe = new[] { new CariDto(1, "Ahmet", true) } };
        var vm = new CarilerViewModel(api);
        vm.Ara = "  Ahm ";

        await vm.AraCommand.ExecuteAsync(null);

        Assert.Equal("Ahm", api.SonAra);
        Assert.Single(vm.Cariler);
    }

    [Fact]
    public async Task Bos_arama_tum_listeyi_ister()
    {
        var api = new SahteApi();
        var vm = new CarilerViewModel(api) { Ara = "   " };
        await vm.AraCommand.ExecuteAsync(null);
        Assert.Null(api.SonAra);
    }

    [Fact]
    public async Task Yazarken_eski_arama_sonucu_yenisini_ezmez()
    {
        var bekleyen = new Dictionary<string, TaskCompletionSource<IReadOnlyList<CariDto>>>();
        var api = new SahteApi { CarilerUret = a => (bekleyen[a ?? ""] = new TaskCompletionSource<IReadOnlyList<CariDto>>()).Task };
        var vm = new CarilerViewModel(api);

        vm.Ara = "A";
        var t1 = vm.AraCommand.ExecuteAsync(null);
        vm.Ara = "Ah";
        var t2 = vm.AraCommand.ExecuteAsync(null);
        bekleyen["Ah"].SetResult(new[] { new CariDto(1, "Ahmet", true) });
        await t2;
        bekleyen["A"].SetResult(new[] { new CariDto(1, "Ahmet", true), new CariDto(2, "Ali", true) });
        await t1;

        Assert.Single(vm.Cariler);
    }

    [Fact]
    public async Task Yazma_bitince_otomatik_arar()
    {
        var api = new SahteApi { CarilerListe = new[] { new CariDto(1, "Ahmet", true) } };
        var vm = new CarilerViewModel(api);

        vm.Ara = "Ahm";
        var bitis = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (api.SonAra is null && DateTime.UtcNow < bitis) await Task.Delay(50);

        Assert.Equal("Ahm", api.SonAra);
    }
}

/// <summary>O7: bildirim hedefi oturum açılana kadar bekletilir.</summary>
public class YonlendirmeTests
{
    [Theory]
    [InlineData("kredikartlari", "//kartlar")]
    [InlineData("kartlar", "//kartlar")]
    [InlineData("panel", "//panel")]
    [InlineData("ayarlar", null)]
    [InlineData("//login", null)]
    [InlineData(null, null)]
    public void Rota_cozumu(string? hedef, string? rota) => Assert.Equal(rota, Yonlendirme.RotaCoz(hedef));

    [Fact]
    public void Istek_bir_kez_alinir_ve_olay_tetikler()
    {
        var y = new Yonlendirme();
        var olay = 0;
        y.Istendi += (_, _) => olay++;

        y.Iste("kredikartlari");

        Assert.Equal(1, olay);
        Assert.True(y.BekleyenVar);
        Assert.Equal("//kartlar", y.Al());
        Assert.Null(y.Al());
        Assert.False(y.BekleyenVar);
    }

    [Fact]
    public void Bilinmeyen_hedef_yok_sayilir()
    {
        var y = new Yonlendirme();
        var olay = 0;
        y.Istendi += (_, _) => olay++;
        y.Iste("ayarlar");
        Assert.Equal(0, olay);
        Assert.Null(y.Al());
    }
}

/// <summary>D7: uygulama kültürü tr-TR.</summary>
public class KulturTests
{
    [Fact]
    public async Task Uygula_tarih_ve_sayi_bicimini_turkce_yapar()
    {
        var eskiC = CultureInfo.CurrentCulture;
        var eskiU = CultureInfo.CurrentUICulture;
        var eskiDC = CultureInfo.DefaultThreadCurrentCulture;
        var eskiDU = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Kultur.Uygula();

            Assert.Equal("24 Eyl", string.Format("{0:dd MMM}", new DateOnly(2026, 9, 24)));
            Assert.Equal("1.234,50", string.Format("{0:N2}", 1234.5m));
            var baskaThread = await Task.Run(() => CultureInfo.CurrentCulture.Name);
            Assert.Equal("tr-TR", baskaThread);
        }
        finally
        {
            CultureInfo.CurrentCulture = eskiC;
            CultureInfo.CurrentUICulture = eskiU;
            CultureInfo.DefaultThreadCurrentCulture = eskiDC;
            CultureInfo.DefaultThreadCurrentUICulture = eskiDU;
        }
    }
}
