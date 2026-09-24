using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>İşlemler sayfası bulguları: O1, O2, O3, O4, O8, D2, D12, ileri tarih onayı, varsayılan filtre.</summary>
public class IslemlerDuzeltmeTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);   // Perşembe

    private static readonly DonemDto Eylul3 = new(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20), 2026, 9);
    private static readonly DonemDto Eylul4 = new(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 2026, 9);
    private static readonly DonemDto EylulIlk = new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 6), 2026, 9);

    private static IslemDto Islem(int id, DateOnly t, decimal tutar = 10m, string kanal = "MEZAT")
        => new(id, t, "x", tutar, kanal, GiderTipi.Cari, null);

    private static (SahteApi api, IslemlerViewModel vm, SabitSaat saat) Kur(Action<SahteApi>? ayar = null)
    {
        var api = new SahteApi
        {
            KanallarListe = new[] { new KanalDto(1, "MEZAT", true, 0, 0m) },
            DonemlerListe = new[] { EylulIlk, Eylul3, Eylul4 },
        };
        ayar?.Invoke(api);
        var saat = new SabitSaat(Bugun.AddHours(10));
        return (api, new IslemlerViewModel(api, saat), saat);
    }

    // ---- Varsayılan filtre + sıralama ----

    [Fact]
    public async Task Varsayilan_liste_filtresi_bu_ay()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();

        Assert.Equal(new DateOnly(2026, 9, 1), api.SonFiltreBaslangic);
        Assert.Equal(new DateOnly(2026, 9, 30), api.SonFiltreBitis);
        Assert.True(vm.FiltreZamanlar.Single(z => z.Ad == "Bu ay").Secili);
        Assert.Contains("1 Eyl 2026 – 30 Eyl 2026", vm.FiltreOzet);
    }

    [Fact]
    public async Task Bu_ay_filtresi_ay_degisince_sayfa_acilisinda_kayar()
    {
        var (api, vm, saat) = Kur();
        await vm.YukleAsync();

        saat.Ilerle(TimeSpan.FromDays(10));   // 4 Ekim
        await vm.YukleAsync();

        Assert.Equal(new DateOnly(2026, 10, 1), api.SonFiltreBaslangic);
        Assert.Equal(new DateOnly(2026, 10, 31), api.SonFiltreBitis);
    }

    [Fact]
    public async Task Son_islemler_en_yeni_once()
    {
        var (_, vm, _) = Kur(a => a.IslemlerListe = new[]
        {
            Islem(1, new DateOnly(2026, 9, 1)), Islem(3, new DateOnly(2026, 9, 20)), Islem(2, new DateOnly(2026, 9, 20)),
        });
        await vm.YukleAsync();

        Assert.Equal(new[] { 3, 2, 1 }, vm.Islemler.Select(i => i.Id));
    }

    [Fact]
    public async Task Tumu_filtresi_tarih_gondermez()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();

        await vm.SecFiltreZamanCommand.ExecuteAsync(vm.FiltreZamanlar.Single(z => z.Ad == "Tümü"));

        Assert.Null(api.SonFiltreBaslangic);
        Assert.Null(api.SonFiltreBitis);
        Assert.StartsWith("Tüm tarihler", vm.FiltreOzet);
    }

    // ---- O2: filtre yarışı ----

    [Fact]
    public async Task Gec_gelen_eski_filtre_yaniti_yeni_sonucu_ezmez_ve_mesgul_dogru()
    {
        var bekleyenler = new Queue<TaskCompletionSource<IReadOnlyList<IslemDto>>>();
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        api.IslemlerUret = (_, _, _) =>
        {
            var t = new TaskCompletionSource<IReadOnlyList<IslemDto>>();
            bekleyenler.Enqueue(t);
            return t.Task;
        };

        var eski = vm.SecFiltreZamanCommand.ExecuteAsync(vm.FiltreZamanlar.Single(z => z.Ad == "Tümü"));
        var yeni = vm.SecFiltreKanalCommand.ExecuteAsync(vm.FiltreKanallari.Single(k => k.Ad == "MEZAT"));
        var eskiIstek = bekleyenler.Dequeue();
        var yeniIstek = bekleyenler.Dequeue();

        yeniIstek.SetResult(new[] { Islem(2, new DateOnly(2026, 9, 10)) });
        await yeni;
        Assert.True(vm.Mesgul);                              // eski istek hâlâ sürüyor

        eskiIstek.SetResult(new[] { Islem(99, new DateOnly(2020, 1, 1), 5m) });
        await eski;

        Assert.Equal(new[] { 2 }, vm.Islemler.Select(i => i.Id));
        Assert.Equal(1, vm.FiltreSayi);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Eski_istegin_hatasi_yeni_sonucu_bozmaz()
    {
        var bekleyenler = new Queue<TaskCompletionSource<IReadOnlyList<IslemDto>>>();
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        api.IslemlerUret = (_, _, _) =>
        {
            var t = new TaskCompletionSource<IReadOnlyList<IslemDto>>();
            bekleyenler.Enqueue(t);
            return t.Task;
        };

        var eski = vm.SecFiltreKanalCommand.ExecuteAsync(vm.FiltreKanallari.Single(k => k.Ad == "Ortak"));
        var yeni = vm.SecFiltreKanalCommand.ExecuteAsync(vm.FiltreKanallari.Single(k => k.Ad == "MEZAT"));
        var eskiIstek = bekleyenler.Dequeue();
        var yeniIstek = bekleyenler.Dequeue();
        yeniIstek.SetResult(new[] { Islem(2, new DateOnly(2026, 9, 10)) });
        await yeni;
        eskiIstek.SetException(new HttpRequestException("geç kopma"));
        await eski;

        Assert.Null(vm.Hata);
        Assert.Single(vm.Islemler);
    }

    // ---- O3: dönem filtresi kayıttan / yeniden yüklemeden sonra görünür kalır ----

    [Fact]
    public async Task Donem_secimi_yeniden_yuklemede_korunur()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        vm.SeciliDonem = vm.FiltreDonemler.Single(d => d == Eylul3);

        api.DonemlerListe = new[] { EylulIlk, Eylul3, Eylul4, new DonemDto(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 30), 2026, 9) };
        await vm.YukleAsync();

        Assert.Equal(Eylul3, vm.SeciliDonem);
        Assert.Equal(Eylul3.Start, api.SonFiltreBaslangic);
        Assert.Equal(Eylul3.End, api.SonFiltreBitis);
        Assert.Contains("14 Eyl 2026 – 20 Eyl 2026", vm.FiltreOzet);
    }

    [Fact]
    public async Task Picker_in_ittigi_null_donem_filtresini_gorunmez_yapmaz()
    {
        var (_, vm, _) = Kur();
        await vm.YukleAsync();
        vm.SeciliDonem = vm.FiltreDonemler.Single(d => d == Eylul3);

        vm.SeciliDonem = null;   // gerçek Picker'ın kaynak değişiminde yaptığı

        Assert.Equal(Eylul3, vm.SeciliDonem);
        Assert.Equal(Eylul3.Start, vm.FiltreBaslangic);
    }

    [Fact]
    public async Task Zaman_cipi_secilince_donem_secimi_temizlenir()
    {
        var (_, vm, _) = Kur();
        await vm.YukleAsync();
        vm.SeciliDonem = vm.FiltreDonemler.Single(d => d == Eylul3);

        await vm.SecFiltreZamanCommand.ExecuteAsync(vm.FiltreZamanlar.Single(z => z.Ad == "Geçen ay"));

        Assert.Null(vm.SeciliDonem);
        Assert.Equal(new DateOnly(2026, 8, 1), vm.FiltreBaslangic);
        Assert.True(vm.FiltreZamanlar.Single(z => z.Ad == "Geçen ay").Secili);
    }

    [Fact]
    public async Task Kayittan_sonra_yalniz_liste_yenilenir_filtre_korunur()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        vm.SeciliDonem = vm.FiltreDonemler.Single(d => d == Eylul3);
        var kanalCagri = api.KanallarCagri;
        var kartCagri = api.KrediKartlariCagri;
        var donemCagri = api.DonemlerCagri;

        vm.DuzenTarih = new DateTime(2026, 9, 15);
        vm.DuzenCari = "a"; vm.DuzenTutar = 5m; vm.DuzenKanal = "MEZAT";
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonIslemOlustur);
        Assert.Equal(kanalCagri, api.KanallarCagri);     // D2: kanal/kart/dönem yeniden çekilmez
        Assert.Equal(kartCagri, api.KrediKartlariCagri);
        Assert.Equal(donemCagri, api.DonemlerCagri);
        Assert.Equal(Eylul3, vm.SeciliDonem);
        Assert.Equal(Eylul3.Start, api.SonFiltreBaslangic);
    }

    // ---- O4 ----

    [Fact]
    public async Task Duzenlenen_kayit_silinince_form_sifirlanir()
    {
        var (api, vm, _) = Kur();
        var i = Islem(42, new DateOnly(2026, 9, 10));
        vm.Duzenle(i);

        await vm.SilCommand.ExecuteAsync(i);

        Assert.Equal(42, api.SonIslemSil);
        Assert.Equal(0, vm.DuzenId);
        Assert.Equal("", vm.DuzenCari);
    }

    [Fact]
    public async Task Baska_kayit_silinince_form_korunur()
    {
        var (_, vm, _) = Kur();
        vm.Duzenle(Islem(42, new DateOnly(2026, 9, 10)));

        await vm.SilCommand.ExecuteAsync(Islem(7, new DateOnly(2026, 9, 11)));

        Assert.Equal(42, vm.DuzenId);
    }

    // ---- O8 ----

    [Fact]
    public async Task Kredi_karti_tipi_kart_secilmeden_kaydedilmez()
    {
        var (api, vm, _) = Kur(a => a.KrediKartlariListe = new[] { new KrediKartiDto(3, "Bonus", new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 25), 1000m, 0m) });
        await vm.YukleAsync();
        vm.DuzenTarih = new DateTime(2026, 9, 10);
        vm.DuzenCari = "Market"; vm.DuzenTutar = 50m; vm.DuzenKanal = "MEZAT";
        vm.SecTipCommand.Execute(new SecimCipi("Kredi kartı"));

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonIslemOlustur);
        Assert.Equal(IslemlerViewModel.KartSecinMesaji, vm.Hata);
        Assert.False(vm.KartYok);
    }

    [Fact]
    public async Task Hic_kart_yokken_kredi_karti_tipi_yonlendirme_mesaji_verir()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        vm.DuzenTarih = new DateTime(2026, 9, 10);
        vm.DuzenTip = GiderTipi.KrediKarti;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonIslemOlustur);
        Assert.True(vm.KartYok);
        Assert.Equal(IslemlerViewModel.KartYokMesaji, vm.Hata);
    }

    [Fact]
    public async Task Eski_kartsiz_KK_kaydi_duzenlenebilir()
    {
        var (api, vm, _) = Kur();
        vm.Duzenle(new IslemDto(11, new DateOnly(2026, 3, 5), "K.K", 10000m, "MEZAT", GiderTipi.KrediKarti, null));
        vm.DuzenTutar = 12000m;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonIslemGuncelle);
        Assert.Null(api.SonIslemGuncelle!.Value.G.KrediKartiId);
    }

    // ---- İleri tarih onayı ----

    [Fact]
    public async Task Ileri_tarihli_islem_onay_ister_ve_onayla_kaydedilir()
    {
        var (api, vm, _) = Kur();
        vm.DuzenTarih = Bugun.AddDays(3);
        vm.DuzenCari = "a"; vm.DuzenTutar = 5m; vm.DuzenKanal = "MEZAT";

        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonIslemOlustur);
        Assert.True(vm.IleriTarihOnayBekliyor);
        Assert.Contains("27 Eylül 2026", vm.IleriTarihUyarisi);

        await vm.IleriTarihOnaylaCommand.ExecuteAsync(null);
        Assert.NotNull(api.SonIslemOlustur);
        Assert.Equal(new DateOnly(2026, 9, 27), api.SonIslemOlustur!.Tarih);
        Assert.False(vm.IleriTarihOnayBekliyor);
    }

    [Fact]
    public async Task Ileri_tarih_uyarisi_vazgecilince_ya_da_tarih_degisince_kalkar()
    {
        var (api, vm, _) = Kur();
        vm.DuzenTarih = Bugun.AddDays(3);
        vm.DuzenCari = "a"; vm.DuzenTutar = 5m; vm.DuzenKanal = "MEZAT";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.True(vm.IleriTarihOnayBekliyor);

        vm.IleriTarihVazgecCommand.Execute(null);
        Assert.False(vm.IleriTarihOnayBekliyor);

        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.True(vm.IleriTarihOnayBekliyor);
        vm.DuzenTarih = Bugun;
        Assert.False(vm.IleriTarihOnayBekliyor);
        Assert.Null(api.SonIslemOlustur);
    }

    [Fact]
    public async Task Bugun_tarihli_islem_onay_istemez()
    {
        var (api, vm, _) = Kur();
        vm.DuzenTarih = Bugun;
        vm.DuzenCari = "a"; vm.DuzenTutar = 5m; vm.DuzenKanal = "MEZAT";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.NotNull(api.SonIslemOlustur);
        Assert.False(vm.IleriTarihOnayBekliyor);
    }

    // ---- O1: gelen tarihi dönem başına hizalanır, asla ham tarih gitmez ----

    [Fact]
    public async Task Gelen_donem_basina_hizalanir()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        vm.GelenTarih = new DateTime(2026, 9, 17); vm.GelenKanal = "MEZAT"; vm.GelenTutar = 1000m;

        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Equal(new DateOnly(2026, 9, 14), api.SonGelen!.DonemStart);
    }

    [Fact]
    public async Task Ilk_donemden_onceki_gelen_gonderilmez()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        vm.GelenTarih = new DateTime(2026, 8, 20); vm.GelenKanal = "MEZAT"; vm.GelenTutar = 1000m;

        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonGelen);
        Assert.Contains("takip başlangıcından", vm.Hata);
        Assert.Equal(1000m, vm.GelenTutar);    // form korunur
    }

    [Fact]
    public async Task Ileri_tarihli_gelen_donem_yoksa_gonderilmez()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        vm.GelenTarih = new DateTime(2026, 10, 7); vm.GelenKanal = "MEZAT"; vm.GelenTutar = 1000m;

        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonGelen);
        Assert.Contains("ileri tarihli", vm.Hata);
    }

    [Fact]
    public async Task Donem_listesi_bosken_gelen_gonderilmez()
    {
        var (api, vm, _) = Kur(a => a.DonemlerListe = Array.Empty<DonemDto>());
        vm.GelenTarih = Bugun; vm.GelenKanal = "MEZAT"; vm.GelenTutar = 1000m;

        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonGelen);
        Assert.NotNull(vm.Hata);
    }

    [Fact]
    public async Task Yeni_hafta_basladiysa_donemler_yenilenip_hizalanir()
    {
        var (api, vm, _) = Kur(a => a.DonemlerListe = new[] { EylulIlk, Eylul3 });
        await vm.YukleAsync();
        api.DonemlerListe = new[] { EylulIlk, Eylul3, Eylul4 };   // sayfa açıkken yeni dönem oluştu
        vm.GelenTarih = new DateTime(2026, 9, 23); vm.GelenKanal = "MEZAT"; vm.GelenTutar = 1000m;

        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Equal(new DateOnly(2026, 9, 21), api.SonGelen!.DonemStart);
    }

    // ---- D12: bayat "bugün" varsayılanları ----

    [Fact]
    public async Task Gece_yarisindan_sonra_dokunulmamis_tarihler_bugune_tasinir()
    {
        var (_, vm, saat) = Kur();
        Assert.Equal(Bugun, vm.DuzenTarih);
        Assert.Equal(Bugun, vm.GelenTarih);
        vm.GelenTarih = new DateTime(2026, 9, 15);   // kullanıcı değiştirdi → korunur

        saat.Ilerle(TimeSpan.FromDays(1));
        await vm.YukleAsync();

        Assert.Equal(Bugun.AddDays(1), vm.DuzenTarih);
        Assert.Equal(new DateTime(2026, 9, 15), vm.GelenTarih);
    }

    [Fact]
    public async Task Duzenlenen_kaydin_tarihi_tazelemede_degismez()
    {
        var (_, vm, saat) = Kur();
        vm.Duzenle(Islem(5, DateOnly.FromDateTime(Bugun)));

        saat.Ilerle(TimeSpan.FromDays(1));
        await vm.YukleAsync();

        Assert.Equal(Bugun, vm.DuzenTarih);
    }
}
