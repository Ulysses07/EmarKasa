using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket C · 32: sayfalardaki Sil düğmeleri ikinci basışta siler; geri alınabilenlerde şerit çıkar.</summary>
public class SilmeOnayiSayfaTests
{
    private static SabitSaat Saat() => new(new DateTime(2026, 9, 24, 10, 0, 0));

    [Fact]
    public async Task Cekler_ikinci_basista_siler_serit_geri_alir()
    {
        var api = new SahteApi
        {
            KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0m)],
            CeklerListe = [new CekDto(2, CekYonu.Alinan, "1", "Ziraat", "Ahmet", 250m, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), "MEZAT", CekDurumu.Portfoyde, null, null)],
            CekOzeti = new CekOzetDto(0m, 0, 0m, 0, 30, [], []),
        };
        var vm = new CeklerViewModel(api, Saat()) { EditorMu = true };
        await vm.YukleAsync();
        var cek = vm.Cekler.Single();

        await vm.OnayliSilCommand.ExecuteAsync(cek);
        Assert.Null(api.SonCekSil);
        Assert.Same(cek, vm.Silme.Bekleyen);
        Assert.Equal(SilmeOnayi.OnayDugmesiGeriAlinir, vm.Silme.OnayDugmesi);

        await vm.OnayliSilCommand.ExecuteAsync(cek);
        Assert.Equal(2, api.SonCekSil);
        Assert.Equal((GecmisTurAdlari.Cek, 2), api.SonSilmeCagrilari.Single());
        Assert.True(vm.Silme.SeritGorunur);
        Assert.Contains("Ahmet", vm.Silme.SeritMetni);
        Assert.Contains("250,00 ₺", vm.Silme.SeritMetni);

        await vm.SilmeGeriAlCommand.ExecuteAsync(null);
        Assert.Equal(902, api.SonGeriAl);
        Assert.False(vm.Silme.SeritGorunur);
    }

    [Fact]
    public async Task Kasa_sayimi_ikinci_basista_siler_serit_kapatilabilir()
    {
        var api = new SahteApi
        {
            KasaSayimlariListe = [new KasaSayimDto(4, new DateOnly(2026, 9, 22), 1_500m, 1_500m, 0m, 1_500m, null, new DateTime(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc))],
        };
        var vm = new KasaSayimiViewModel(api, Saat()) { EditorMu = true };
        await vm.YukleAsync();

        await vm.OnayliSilCommand.ExecuteAsync(vm.Sayimlar[0]);
        Assert.Null(api.SonKasaSayimSil);
        await vm.OnayliSilCommand.ExecuteAsync(vm.Sayimlar[0]);
        Assert.Equal(4, api.SonKasaSayimSil);
        Assert.Equal((GecmisTurAdlari.KasaSayimi, 4), api.SonSilmeCagrilari.Single());
        Assert.Equal("Silindi: 22 Eyl 2026 sayımı · 1.500,00 ₺", vm.Silme.SeritMetni);
        Assert.Empty(vm.Sayimlar);

        vm.SilmeSeridiKapatCommand.Execute(null);
        Assert.False(vm.Silme.SeritGorunur);
    }

    [Fact]
    public async Task Kredi_karti_silmesi_geri_alinamaz_odeme_silmesi_geri_alinir()
    {
        var kart = new KrediKartiDto(3, "Bonus", new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 20), 10_000m, 0m);
        var odeme = new KartOdemeDto(8, 3, new DateOnly(2026, 9, 5), 750m, null);
        var api = new SahteApi { KrediKartlariListe = [kart], KartOdemelerListe = [odeme] };
        var vm = new KrediKartlariViewModel(api, Saat()) { EditorMu = true };
        await vm.YukleAsync();
        var g = vm.Kartlar.Single();

        await vm.OnayliSilCommand.ExecuteAsync(g);
        Assert.Equal(SilmeOnayi.OnayDugmesiGeriAlinmaz, vm.Silme.OnayDugmesi);
        Assert.Contains("geri alınamaz", vm.Silme.OnayMetni);
        Assert.Null(api.SonKartSil);

        // Başka kayda basmak önceki onayı düşürür.
        await vm.OnayliOdemeSilCommand.ExecuteAsync(odeme);
        Assert.Equal(odeme, vm.Silme.Bekleyen);
        await vm.OnayliSilCommand.ExecuteAsync(g);
        Assert.Null(api.SonKartSil);

        await vm.OnayliSilCommand.ExecuteAsync(g);
        Assert.Equal(3, api.SonKartSil);
        Assert.Empty(api.SonSilmeCagrilari);                         // geri alınamaz: şerit yok
        Assert.False(vm.Silme.SeritGorunur);

        await vm.OnayliOdemeSilCommand.ExecuteAsync(odeme);
        await vm.OnayliOdemeSilCommand.ExecuteAsync(odeme);
        Assert.Equal(8, api.SonKartOdemeSil);
        Assert.Equal((GecmisTurAdlari.KartOdemesi, 8), api.SonSilmeCagrilari.Single());
        Assert.Equal("Silindi: Bonus ödemesi · 5 Eyl 2026 · 750,00 ₺", vm.Silme.SeritMetni);
    }

    [Fact]
    public async Task Ayarlar_kanal_ve_tekrarlayan_geri_alinamaz_kalem_geri_alinir()
    {
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false),
            KanallarListe = [new KanalDto(4, "PERAKENDE", true, 1, 0m)],
            TekrarlayanListe = [new TekrarlayanGiderDto(6, "Kira", "PERAKENDE", 5_000m, 1, true, new DateOnly(2026, 1, 1))],
        };
        var vm = new AyarlarViewModel(api, Saat());
        await vm.YukleAsync();

        var kanal = vm.Kanallar.Single();
        await vm.OnayliKanalSilCommand.ExecuteAsync(kanal);
        Assert.Equal(SilmeOnayi.OnayDugmesiGeriAlinmaz, vm.Silme.OnayDugmesi);
        await vm.OnayliKanalSilCommand.ExecuteAsync(kanal);
        Assert.Equal(4, api.SonKanalSil);

        var kalem = vm.GiderKalemleri.Single(k => k.Ad == "Kira");
        await vm.OnayliKalemSilCommand.ExecuteAsync(kalem);
        Assert.Equal(SilmeOnayi.OnayDugmesiGeriAlinir, vm.Silme.OnayDugmesi);
        await vm.OnayliKalemSilCommand.ExecuteAsync(kalem);
        Assert.Equal(802, api.SonKalemSil);
        Assert.Equal((GecmisTurAdlari.GiderKalemi, 802), api.SonSilmeCagrilari.Single());
        Assert.Equal("Silindi: gider kalemi Kira", vm.Silme.SeritMetni);

        var tekrar = vm.TekrarlayanGiderler.Single();
        await vm.OnayliTekrarSilCommand.ExecuteAsync(tekrar);
        Assert.Null(api.SonTekrarSil);
        await vm.OnayliTekrarSilCommand.ExecuteAsync(tekrar);
        Assert.Equal(6, api.SonTekrarSil);
        Assert.Single(api.SonSilmeCagrilari);
        Assert.True(vm.Silme.SeritGorunur);                          // önceki (kalem) şeridi kalır
    }

    [Fact]
    public async Task Silme_reddedilirse_hata_gosterilir_serit_cikmaz()
    {
        var api = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false),
            TekrarlayanListe = [new TekrarlayanGiderDto(6, "Kira", "MEZAT", 5_000m, 1, true, new DateOnly(2026, 1, 1))],
        };
        var vm = new AyarlarViewModel(api, Saat());
        await vm.YukleAsync();
        api.TekrarlayanYazHatasi = new KasaApiException(System.Net.HttpStatusCode.Conflict, "Silinemedi.");

        await vm.OnayliTekrarSilCommand.ExecuteAsync(vm.TekrarlayanGiderler[0]);
        await vm.OnayliTekrarSilCommand.ExecuteAsync(vm.TekrarlayanGiderler[0]);
        Assert.Equal("Silinemedi.", vm.Hata);
        Assert.Null(vm.Silme.Bekleyen);
        Assert.False(vm.Silme.SeritGorunur);
    }

    [Fact]
    public async Task Cariler_sil_dugmesi_onayli_siler_formdaki_cari_temizlenir_geri_alinir()
    {
        var api = new SahteApi();
        var vm = new CarilerViewModel(api, Saat()) { EditorMu = true };
        await vm.YukleAsync();
        var market = vm.Cariler.Single(c => c.Ad == "Market");
        vm.Duzenle(market);

        await vm.OnayliSilCommand.ExecuteAsync(market);
        Assert.Null(api.SonCariSil);
        await vm.OnayliSilCommand.ExecuteAsync(market);
        Assert.Equal(902, api.SonCariSil);
        Assert.Equal(0, vm.DuzenId);
        Assert.Equal((GecmisTurAdlari.Cari, 902), api.SonSilmeCagrilari.Single());
        Assert.Equal("Silindi: cari Market", vm.Silme.SeritMetni);

        await vm.SilmeGeriAlCommand.ExecuteAsync(null);
        Assert.Equal(1802, api.SonGeriAl);
    }

    [Fact]
    public async Task Onay_suresi_gecince_ikinci_basis_yeniden_onay_ister()
    {
        var api = new SahteApi { KasaSayimlariListe = [new KasaSayimDto(4, new DateOnly(2026, 9, 22), 1m, 1m, 0m, 1m, null, DateTime.UtcNow)] };
        var saat = Saat();
        var vm = new KasaSayimiViewModel(api, saat) { EditorMu = true };
        await vm.YukleAsync();
        await vm.OnayliSilCommand.ExecuteAsync(vm.Sayimlar[0]);
        saat.Ilerle(SilmeOnayi.OnaySuresi + TimeSpan.FromSeconds(1));
        await vm.OnayliSilCommand.ExecuteAsync(vm.Sayimlar[0]);
        Assert.Null(api.SonKasaSayimSil);
        Assert.True(vm.Silme.OnayBekliyor);
    }

    // ---- Silme başarılı, liste tazelemesi hatalı: şerit yine çıkar ----

    [Fact]
    public async Task Tazeleme_hatasinda_cek_sayim_cari_kalem_ve_odeme_silmesi_seridi_gosterir()
    {
        var hata = new HttpRequestException("ağ koptu");

        var cekApi = new SahteApi
        {
            KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0m)],
            CeklerListe = [new CekDto(2, CekYonu.Alinan, "1", "Ziraat", "Ahmet", 250m, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), "MEZAT", CekDurumu.Portfoyde, null, null)],
            CekOzeti = new CekOzetDto(0m, 0, 0m, 0, 30, [], []),
        };
        var cekler = new CeklerViewModel(cekApi, Saat()) { EditorMu = true };
        await cekler.YukleAsync();
        await cekler.OnayliSilCommand.ExecuteAsync(cekler.Cekler[0]);
        cekApi.YuklemeHatasi = hata;
        await cekler.OnayliSilCommand.ExecuteAsync(cekler.Cekler[0]);
        Assert.Equal(2, cekApi.SonCekSil);
        Assert.NotNull(cekler.Hata);
        Assert.True(cekler.Silme.SeritGorunur);

        var sayimApi = new SahteApi
        {
            KasaSayimlariListe = [new KasaSayimDto(4, new DateOnly(2026, 9, 22), 1_500m, 1_500m, 0m, 1_500m, null, new DateTime(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc))],
        };
        var sayim = new KasaSayimiViewModel(sayimApi, Saat()) { EditorMu = true };
        await sayim.YukleAsync();
        await sayim.OnayliSilCommand.ExecuteAsync(sayim.Sayimlar[0]);
        sayimApi.YuklemeHatasi = hata;
        await sayim.OnayliSilCommand.ExecuteAsync(sayim.Sayimlar[0]);
        Assert.Equal(4, sayimApi.SonKasaSayimSil);
        Assert.NotNull(sayim.Hata);
        Assert.True(sayim.Silme.SeritGorunur);

        var cariApi = new SahteApi();
        var cariler = new CarilerViewModel(cariApi, Saat()) { EditorMu = true };
        await cariler.YukleAsync();
        var market = cariler.Cariler.Single(c => c.Ad == "Market");
        await cariler.OnayliSilCommand.ExecuteAsync(market);
        cariApi.YuklemeHatasi = hata;
        await cariler.OnayliSilCommand.ExecuteAsync(market);
        Assert.Equal(902, cariApi.SonCariSil);
        Assert.NotNull(cariler.Hata);
        Assert.Equal("Silindi: cari Market", cariler.Silme.SeritMetni);

        var ayarApi = new SahteApi
        {
            AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false),
            TekrarlayanListe = [new TekrarlayanGiderDto(6, "Kira", "MEZAT", 5_000m, 1, true, new DateOnly(2026, 1, 1))],
        };
        var ayarlar = new AyarlarViewModel(ayarApi, Saat());
        await ayarlar.YukleAsync();
        var kalem = ayarlar.GiderKalemleri.Single(k => k.Ad == "Kira");
        await ayarlar.OnayliKalemSilCommand.ExecuteAsync(kalem);
        ayarApi.YuklemeHatasi = hata;
        await ayarlar.OnayliKalemSilCommand.ExecuteAsync(kalem);
        Assert.Equal(802, ayarApi.SonKalemSil);
        Assert.NotNull(ayarlar.Hata);
        Assert.True(ayarlar.Silme.SeritGorunur);

        var odeme = new KartOdemeDto(8, 3, new DateOnly(2026, 9, 5), 750m, null);
        var kartApi = new SahteApi
        {
            KrediKartlariListe = [new KrediKartiDto(3, "Bonus", new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 20), 10_000m, 0m)],
            KartOdemelerListe = [odeme],
        };
        var kartlar = new KrediKartlariViewModel(kartApi, Saat()) { EditorMu = true };
        await kartlar.YukleAsync();
        await kartlar.OnayliOdemeSilCommand.ExecuteAsync(odeme);
        kartApi.YuklemeHatasi = hata;
        await kartlar.OnayliOdemeSilCommand.ExecuteAsync(odeme);
        Assert.Equal(8, kartApi.SonKartOdemeSil);
        Assert.NotNull(kartlar.Hata);
        Assert.Equal("Silindi: Bonus ödemesi · 5 Eyl 2026 · 750,00 ₺", kartlar.Silme.SeritMetni);
    }

    private sealed class DenemeVm() : TemelViewModel(null)
    {
        public Task<bool> Calistir(Func<Task> sil, Func<Task> tazele) => SilVeTazeleAsync(sil, tazele);
    }

    [Fact]
    public async Task Sil_ve_tazele_silme_hatasinda_false_tazeleme_hatasinda_true_doner()
    {
        var vm = new DenemeVm();
        var tazelendi = false;
        var sonuc = await vm.Calistir(() => throw new KasaApiException(System.Net.HttpStatusCode.Conflict, "Silinemedi."),
            () => { tazelendi = true; return Task.CompletedTask; });
        Assert.False(sonuc);
        Assert.False(tazelendi);
        Assert.Equal("Silinemedi.", vm.Hata);

        sonuc = await vm.Calistir(() => Task.CompletedTask, () => throw new HttpRequestException("ağ"));
        Assert.True(sonuc);
        Assert.NotNull(vm.Hata);
        Assert.False(vm.Mesgul);

        sonuc = await vm.Calistir(() => Task.CompletedTask, () => Task.CompletedTask);
        Assert.True(sonuc);
        Assert.Null(vm.Hata);
    }
}
