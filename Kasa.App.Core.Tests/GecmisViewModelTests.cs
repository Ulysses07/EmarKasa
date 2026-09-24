using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Geçmiş sayfası: sayfalı liste, tür filtresi, "Geri al" görünürlüğü ve akışı.</summary>
public class GecmisViewModelTests
{
    private static readonly DateTime Zaman = new(2026, 9, 24, 9, 30, 0, DateTimeKind.Utc);

    private static DegisiklikDto D(int id, string tur = "İşlem", string eylem = "Silindi", bool geriAlinabilir = true,
        bool geriAlindi = false, string rol = "editor")
        => new(id, Zaman, rol, tur, id * 10, eylem, $"{tur} {eylem.ToLowerInvariant()}: #{id}", "{}", null,
            geriAlindi, geriAlindi ? Zaman.AddMinutes(30) : null, geriAlinabilir);

    /// <summary>Sunucu sırası: en yeni (büyük Id) önce.</summary>
    private static List<DegisiklikDto> Liste(int adet, Func<int, string>? tur = null)
        => Enumerable.Range(1, adet).Reverse().Select(i => D(i, tur?.Invoke(i) ?? "İşlem")).ToList();

    private static (SahteApi api, GecmisViewModel vm) Kur(Action<SahteApi>? ayar = null)
    {
        var api = new SahteApi();
        ayar?.Invoke(api);
        return (api, new GecmisViewModel(api, new SabitSaat(new DateTime(2026, 9, 24, 12, 0, 0))));
    }

    [Fact]
    public async Task Yukle_en_yeni_sayfayi_ve_tur_ciplerini_kurar()
    {
        var (api, vm) = Kur(a =>
        {
            a.GecmisListe = Liste(120, i => i % 2 == 0 ? "İşlem" : "Cari");
            a.GecmisTurleriListe = ["Cari", "İşlem"];
        });
        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Equal((null, GecmisViewModel.SayfaBoyutu, 0), api.GecmisCagrilari.Single());
        Assert.Equal(GecmisViewModel.SayfaBoyutu, vm.Kayitlar.Count);
        Assert.Equal(120, vm.Kayitlar[0].Id);
        Assert.Equal(120, vm.ToplamKayit);
        Assert.True(vm.DahaFazlaVar);
        Assert.Equal("Tüm kayıtlar · 120 değişiklik (en yeni 50 gösteriliyor)", vm.Ozet);
        Assert.Equal(["Tümü", "Cari", "İşlem"], vm.TurCipleri.Select(c => c.Ad));
        Assert.True(vm.TurCipleri[0].Secili);

        var s = vm.Kayitlar[0];
        Assert.Equal("24.09.2026 09:30", s.Zaman);     // test saat dilimi UTC
        Assert.Equal("Editör", s.RolAdi);
        Assert.Equal("24.09.2026 09:30 · Editör · İşlem", s.Ayrinti);
        Assert.True(s.SilmeMi);
        Assert.False(s.EklemeMi);
        Assert.Null(s.GeriAlmaNotu);
    }

    [Fact]
    public async Task Daha_fazla_sonraki_sayfayi_ekler_ve_sonda_durur()
    {
        var (api, vm) = Kur(a => a.GecmisListe = Liste(120));
        await vm.YukleAsync();

        await vm.DahaFazlaYukleCommand.ExecuteAsync(null);
        Assert.Equal(100, vm.Kayitlar.Count);
        await vm.DahaFazlaYukleCommand.ExecuteAsync(null);
        Assert.Equal(120, vm.Kayitlar.Count);

        Assert.Equal([0, 50, 100], api.GecmisCagrilari.Select(c => c.Offset));
        Assert.Equal(Enumerable.Range(1, 120).Reverse(), vm.Kayitlar.Select(k => k.Id));
        Assert.False(vm.DahaFazlaVar);
        Assert.Equal("Tüm kayıtlar · 120 değişiklik", vm.Ozet);

        await vm.DahaFazlaYukleCommand.ExecuteAsync(null);   // hepsi yüklü: istek yok
        Assert.Equal(3, api.GecmisCagrilari.Count);
    }

    [Fact]
    public async Task Uste_yeni_satir_eklenince_sayfa_kayar_sira_bozulmaz_ilerleme_durmaz()
    {
        var sunucu = Liste(60);
        var (api, vm) = Kur(a => a.GecmisUret = (tur, limit, offset) =>
            Task.FromResult(new DegisiklikSayfasi(sunucu.Skip(offset).Take(limit).ToList(), sunucu.Count)));
        await vm.YukleAsync();
        Assert.Equal(50, vm.Kayitlar.Count);

        // Bu arada editör 55 değişiklik daha yaptı (Id 61–115, en üstte).
        sunucu.InsertRange(0, Enumerable.Range(61, 55).Reverse().Select(i => D(i)));

        await vm.DahaFazlaYukleCommand.ExecuteAsync(null);   // kaymış sayfa: hepsi yeni ya da tekrar
        Assert.Equal(50, vm.Kayitlar.Count);
        Assert.True(vm.DahaFazlaVar);
        await vm.DahaFazlaYukleCommand.ExecuteAsync(null);

        Assert.Equal(Enumerable.Range(1, 60).Reverse(), vm.Kayitlar.Select(k => k.Id));
        Assert.False(vm.DahaFazlaVar);
    }

    [Fact]
    public async Task Tur_secimi_filtreler_ve_cipi_vurgular()
    {
        var (api, vm) = Kur(a =>
        {
            a.GecmisListe = Liste(10, i => i <= 3 ? "Cari" : "İşlem");
            a.GecmisTurleriListe = ["Cari", "İşlem"];
        });
        await vm.YukleAsync();

        await vm.SecTurCommand.ExecuteAsync(vm.TurCipleri.Single(c => c.Ad == "Cari"));
        Assert.Equal(("Cari", GecmisViewModel.SayfaBoyutu, 0), api.GecmisCagrilari[^1]);
        Assert.Equal("Cari", vm.FiltreTur);
        Assert.All(vm.Kayitlar, k => Assert.Equal("Cari", k.Tur));
        Assert.Equal(3, vm.ToplamKayit);
        Assert.Equal("Cari · 3 değişiklik", vm.Ozet);
        Assert.Equal(["Cari"], vm.TurCipleri.Where(c => c.Secili).Select(c => c.Ad));

        await vm.SecTurCommand.ExecuteAsync(vm.TurCipleri.Single(c => c.Ad == GecmisViewModel.TumTurler));
        Assert.Null(vm.FiltreTur);
        Assert.Null(api.GecmisCagrilari[^1].Tur);
        Assert.Equal(10, vm.Kayitlar.Count);
        Assert.Equal([GecmisViewModel.TumTurler], vm.TurCipleri.Where(c => c.Secili).Select(c => c.Ad));
    }

    [Fact]
    public async Task Gec_gelen_eski_yanit_yeni_filtrenin_sonucunu_ezmez()
    {
        var ilk = new TaskCompletionSource<DegisiklikSayfasi>();
        var (api, vm) = Kur(a => a.GecmisUret = (tur, limit, offset) => tur is null
            ? ilk.Task
            : Task.FromResult(new DegisiklikSayfasi([D(5, "İşlem")], 1)));

        var yukleme = vm.YukleAsync();
        await vm.SecTurCommand.ExecuteAsync(new SecimCipi("İşlem"));
        ilk.SetResult(new DegisiklikSayfasi([D(9, "Cari"), D(8, "Cari")], 2));
        await yukleme;

        Assert.Equal([5], vm.Kayitlar.Select(k => k.Id));
        Assert.Equal(1, vm.ToplamKayit);
    }

    [Fact]
    public async Task Geri_al_yalniz_editorde_ve_geri_alinabilir_satirda_gorunur()
    {
        var (_, vm) = Kur(a => a.GecmisListe =
        [
            D(3, geriAlinabilir: true),
            D(2, eylem: "Güncellendi", geriAlinabilir: false),
            D(1, geriAlinabilir: false, geriAlindi: true),
        ]);
        await vm.YukleAsync();
        Assert.All(vm.Kayitlar, k => Assert.False(k.GeriAlGorunur));   // izleyici (varsayılan)

        vm.EditorMu = true;
        Assert.Equal([true, false, false], vm.Kayitlar.Select(k => k.GeriAlGorunur));
        Assert.Equal("Geri alındı · 24.09.2026 10:00", vm.Kayitlar[2].GeriAlmaNotu);

        vm.EditorMu = false;
        Assert.All(vm.Kayitlar, k => Assert.False(k.GeriAlGorunur));

        // Sayfa editör olarak açılırsa satırlar baştan doğru kurulur.
        var (_, vm2) = Kur(a => a.GecmisListe = [D(3)]);
        vm2.EditorMu = true;
        await vm2.YukleAsync();
        Assert.True(vm2.Kayitlar.Single().GeriAlGorunur);
    }

    [Fact]
    public async Task Geri_al_api_cagirir_bilgi_yazar_ve_listeyi_yeniler()
    {
        var (api, vm) = Kur(a => a.GecmisListe = [D(7, "Cari")]);
        vm.EditorMu = true;
        await vm.YukleAsync();

        api.GecmisListe = [D(8, "Cari", eylem: "Eklendi (geri alındı)", geriAlinabilir: false), D(7, "Cari", geriAlindi: true, geriAlinabilir: false)];
        await vm.GeriAlCommand.ExecuteAsync(vm.Kayitlar.Single());

        Assert.Null(vm.Hata);
        Assert.Equal(7, api.SonGeriAl);
        Assert.Equal(GecmisViewModel.GeriAlindiMesaji("Cari"), vm.Bilgi);
        Assert.Equal([8, 7], vm.Kayitlar.Select(k => k.Id));
        Assert.True(vm.Kayitlar[0].EklemeMi);
        Assert.All(vm.Kayitlar, k => Assert.False(k.GeriAlGorunur));
    }

    [Fact]
    public async Task Sunucu_geri_almayi_reddederse_mesaji_gosterilir()
    {
        const string mesaj = "Geri alınamadı: 'Market' adında bir cari yok. Önce Cariler sayfasından ekleyin.";
        var (api, vm) = Kur(a =>
        {
            a.GecmisListe = [D(4)];
            a.GeriAlHatasi = new KasaApiException(HttpStatusCode.BadRequest, mesaj);
        });
        vm.EditorMu = true;
        await vm.YukleAsync();

        await vm.GeriAlCommand.ExecuteAsync(vm.Kayitlar.Single());
        Assert.Equal(mesaj, vm.Hata);
        Assert.Null(vm.Bilgi);
    }

    [Fact]
    public async Task Izleyici_ve_geri_alinamaz_satir_istemcide_durdurulur()
    {
        var (api, vm) = Kur(a => a.GecmisListe = [D(2), D(1, eylem: "Güncellendi", geriAlinabilir: false)]);
        await vm.YukleAsync();

        await vm.GeriAlCommand.ExecuteAsync(vm.Kayitlar[0]);
        Assert.Equal(HataMesaji.Yetkisiz, vm.Hata);
        Assert.Null(api.SonGeriAl);

        vm.EditorMu = true;
        await vm.GeriAlCommand.ExecuteAsync(vm.Kayitlar[1]);
        Assert.Equal(GecmisViewModel.GeriAlinamazMesaji, vm.Hata);
        Assert.Null(api.SonGeriAl);
    }

    [Fact]
    public async Task Yukleme_hatasi_gosterilir()
    {
        var (_, vm) = Kur(a => a.YuklemeHatasi = new KasaApiException(HttpStatusCode.ServiceUnavailable));
        await vm.YukleAsync();
        Assert.Equal(HataMesaji.Ulasilamadi, vm.Hata);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public void Rol_metinleri()
    {
        Assert.Equal("Editör", GecmisSatiri.RolMetni("editor"));
        Assert.Equal("İzleyici", GecmisSatiri.RolMetni("viewer"));
        Assert.Equal("sistem", GecmisSatiri.RolMetni("sistem"));
        Assert.Equal("—", GecmisSatiri.RolMetni(""));
    }
}
