using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class IslemEditorTests
{
    [Fact]
    public async Task Yeni_islem_olustur_cagirir()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api)
        {
            DuzenTarih = new DateTime(2026, 3, 5),
            DuzenCari = "MEZAT alış",
            DuzenTutar = 2500m,
            DuzenKanal = "MEZAT",
            DuzenTip = GiderTipi.Cari,
        };

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonIslemOlustur);
        Assert.Equal("MEZAT", api.SonIslemOlustur!.Kanal);
        Assert.Equal(2500m, api.SonIslemOlustur!.TutarTl);
        Assert.Equal(new DateOnly(2026, 3, 5), api.SonIslemOlustur!.Tarih);
    }

    [Fact]
    public async Task Kart_harcamasi_krediKartiId_ve_tip_ile_kaydeder()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api)
        {
            DuzenTarih = new DateTime(2026, 7, 10),
            DuzenCari = "Market",
            DuzenTutar = 500m,
            DuzenKanal = "MEZAT",
            DuzenTip = GiderTipi.KrediKarti,
            DuzenKrediKartiId = 7,
        };

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonIslemOlustur);
        Assert.Equal(7, api.SonIslemOlustur!.KrediKartiId);
        Assert.Equal(GiderTipi.KrediKarti, api.SonIslemOlustur!.Tip);
    }

    [Fact]
    public void SecTip_kredi_karti_secince_kart_secicisi_gorunur()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);

        Assert.False(vm.KartSeciciGorunur);
        vm.SecTipCommand.Execute(new SecimCipi("Kredi kartı"));

        Assert.Equal(GiderTipi.KrediKarti, vm.DuzenTip);
        Assert.True(vm.KartSeciciGorunur);
    }

    [Fact]
    public void SecKart_secilen_kart_idsini_atar_tip_disina_donunce_temizler()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api) { DuzenTip = GiderTipi.KrediKarti };

        vm.SecKartCommand.Execute(new KartCipi(9, "Bonus"));
        Assert.Equal(9, vm.DuzenKrediKartiId);

        vm.SecTipCommand.Execute(new SecimCipi("Diğer gider"));
        Assert.Null(vm.DuzenKrediKartiId);
    }

    [Fact]
    public async Task Mevcut_islem_guncelle_cagirir()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);
        vm.Duzenle(new IslemDto(11, new DateOnly(2026,3,5), "K.K", 10000m, "MEZAT", GiderTipi.KrediKarti, null));
        vm.DuzenTutar = 12000m;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonIslemGuncelle);
        Assert.Equal(11, api.SonIslemGuncelle!.Value.Id);
        Assert.Equal(12000m, api.SonIslemGuncelle!.Value.G.TutarTl);
    }

    [Fact]
    public async Task Sil_islem_silme_cagirir()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);

        await vm.SilCommand.ExecuteAsync(new IslemDto(5, new DateOnly(2026,3,5), "x", 1m, "MEZAT", GiderTipi.Cari, null));

        Assert.Equal(5, api.SonIslemSil);
    }

    // ---- Gelen · dönem toplamı formu (web incomeDialog ile aynı davranış) ----
    private static readonly DonemDto Hafta38 = new(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20), 2026, 9);
    private static readonly DonemDto Hafta39 = new(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 2026, 9);
    private static readonly DonemDto Hafta40 = new(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 30), 2026, 9);
    private static readonly KanalDto Mezat = new(1, "MEZAT", true, 0, 0m), Perakende = new(2, "PERAKENDE", true, 1, 0m);

    internal sealed class SabitZaman(DateOnly gun) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(gun.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static async Task<(IslemlerViewModel Vm, SahteApi Api)> GelirFormu(SahteApi? api = null, params GelenDto[] gelenler)
    {
        api ??= new SahteApi();
        api.KanallarListe = new[] { Mezat, Perakende }; api.DonemlerListe = new[] { Hafta38, Hafta39, Hafta40 };
        if (gelenler.Length > 0) api.GelenlerListe = gelenler;
        var vm = new IslemlerViewModel(api, zaman: new SabitZaman(new DateOnly(2026, 9, 23)));
        await vm.YukleAsync();
        return (vm, api);
    }
    private static void KanalSec(IslemlerViewModel vm, string ad) => vm.SecGelenKanalCommand.Execute(vm.GelenKanallari.First(c => c.Ad == ad));

    [Fact]
    public async Task Gelir_formu_bugunu_iceren_donemi_secer_ve_kanalin_mevcut_toplamini_doldurur()
    {
        var (vm, api) = await GelirFormu(null, new GelenDto(5, Hafta39.Start, "MEZAT", 5000m, KanalId: 1), new GelenDto(6, Hafta39.Start, "PERAKENDE", 700m, KanalId: 2));

        Assert.Equal(Hafta39, vm.GelenDonem);
        Assert.Equal(Hafta39.Start, api.SonGelenlerDonem);
        Assert.Equal(new[] { Hafta40, Hafta39, Hafta38 }, vm.GelenDonemler);   // yeniden eskiye
        Assert.False(vm.GelenKaydedilebilir);                                   // kanal seçilmedi

        KanalSec(vm, "MEZAT");
        Assert.Equal(5000m, vm.GelenTutar);
        Assert.Equal(5000m, vm.GelenMevcutToplam);
        Assert.Contains("yeni tutar öncekinin yerine geçer; üzerine eklenmez", vm.GelenBilgi);
        Assert.True(vm.GelenKaydedilebilir);

        KanalSec(vm, "PERAKENDE");
        Assert.Equal(700m, vm.GelenTutar);
    }

    [Fact]
    public async Task Ayni_doneme_ikinci_giris_donem_toplamini_yerine_koyar_ve_oncekini_soyler()
    {
        var (vm, api) = await GelirFormu(null, new GelenDto(5, Hafta39.Start, "MEZAT", 5000m, KanalId: 1));
        KanalSec(vm, "MEZAT");
        var istek = api.GelenlerIstekleri.Count;
        vm.GelenTutar = 8000m;

        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Equal(new GelenYaz(Hafta39.Start, "MEZAT", 8000m), api.SonGelen);
        Assert.Null(vm.Hata);
        Assert.Equal("Kanal geliri kaydedildi: MEZAT · 21.09.2026–27.09.2026 dönem toplamı 8.000,00 ₺ (önceki 5.000,00 ₺).", vm.GelenBilgi);
        Assert.Equal("MEZAT", vm.GelenKanal);                       // kanal seçili kalır
        Assert.Equal(istek + 1, api.GelenlerIstekleri.Count);       // liste yeniden yüklendi
    }

    [Fact]
    public async Task Kayit_yokken_gelir_eklenir_sifir_tutar_istenmez()
    {
        var (vm, api) = await GelirFormu();
        KanalSec(vm, "PERAKENDE");
        Assert.Null(vm.GelenMevcutToplam);
        Assert.Equal(0m, vm.GelenTutar);

        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.GelenKaydetCagri);
        Assert.Equal("Dönem toplam gelirini girin.", vm.Hata);

        vm.GelenTutar = 5000m;
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal(new GelenYaz(Hafta39.Start, "PERAKENDE", 5000m), api.SonGelen);
        Assert.Contains("(önceki 0,00 ₺)", vm.GelenBilgi);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task Eski_yinelenen_gelir_grubu_salt_okunurdur_kayit_gonderilmez(bool eskiGrup, int satir)
    {
        var satirlar = Enumerable.Range(0, satir).Select(i => new GelenDto(10 + i, Hafta39.Start, "MEZAT", 100m, KanalId: i == 0 ? 1 : null, EskiYinelenenGrup: eskiGrup)).ToArray();
        var (vm, api) = await GelirFormu(null, satirlar);
        KanalSec(vm, "MEZAT");

        Assert.True(vm.GelenSaltOkunur);
        Assert.False(vm.GelenKaydedilebilir);
        Assert.Contains($"{satir} eski gelir kaydı var", vm.GelenBilgi);
        vm.GelenTutar = 900m;
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.GelenKaydetCagri);
        Assert.Equal("Bu eski gelir grubu geçmiş tutarları korumak için değiştirilemez.", vm.Hata);
    }

    [Fact]
    public async Task Sunucu_409_mesaji_gosterilir_form_korunur()
    {
        const string mesaj = "Hesaba bağlı gelir tutarı buradan değiştirilemez.";
        var (vm, _) = await GelirFormu(new SahteApi { GelenKaydetHatasi = new KasaApiException(System.Net.HttpStatusCode.Conflict, mesaj) });
        KanalSec(vm, "MEZAT"); vm.GelenTutar = 123.45m;

        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Equal(mesaj, vm.Hata);
        Assert.Equal("MEZAT", vm.GelenKanal);
        Assert.Equal(123.45m, vm.GelenTutar);
        Assert.Equal(Hafta39, vm.GelenDonem);
        Assert.False(vm.Mesgul);
    }

    [Fact]
    public async Task Donem_gelirleri_yuklenemezse_kayit_yapilmaz()
    {
        var (vm, api) = await GelirFormu(new SahteApi { GelenlerHatasi = new HttpRequestException() });
        KanalSec(vm, "MEZAT"); vm.GelenTutar = 5000m;

        Assert.True(vm.GelenYuklemeHatasi);
        Assert.False(vm.GelenKaydedilebilir);
        Assert.Equal("Dönem gelirleri yüklenemedi. Başka dönem seçip yeniden deneyin; mevcut bilgilerle kayıt yapılamaz.", vm.GelenBilgi);
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.GelenKaydetCagri);
        Assert.Equal("Dönem gelirleri yüklenmeden kayıt yapılamaz. Lütfen yeniden deneyin.", vm.Hata);
    }

    [Fact]
    public async Task Mevcut_toplami_sifira_indirmek_ikinci_basista_onaylanir()
    {
        var (vm, api) = await GelirFormu(null, new GelenDto(5, Hafta39.Start, "MEZAT", 5000m, KanalId: 1));
        KanalSec(vm, "MEZAT");
        vm.GelenTutar = 0m;

        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.GelenKaydetCagri);
        Assert.Contains("Onaylamak için yeniden kaydedin", vm.GelenBilgi);

        vm.GelenTutar = 1m; vm.GelenTutar = 0m;                     // tutar değişti: onay sıfırlanır
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.GelenKaydetCagri);

        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal(new GelenYaz(Hafta39.Start, "MEZAT", 0m), api.SonGelen);
    }

    [Fact]
    public async Task Sifir_onayi_donem_veya_kanal_degisince_sifirlanir()
    {
        var (vm, api) = await GelirFormu(null, new GelenDto(5, Hafta39.Start, "MEZAT", 5000m, KanalId: 1), new GelenDto(6, Hafta39.Start, "PERAKENDE", 700m, KanalId: 2));
        KanalSec(vm, "MEZAT"); vm.GelenTutar = 0m;
        await vm.GelenKaydetCommand.ExecuteAsync(null);             // onay kuruldu
        KanalSec(vm, "PERAKENDE"); KanalSec(vm, "MEZAT"); vm.GelenTutar = 0m;
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.GelenKaydetCagri);

        vm.GelenDonem = Hafta38; await vm.GelenYuklemesi; vm.GelenDonem = Hafta39; await vm.GelenYuklemesi; vm.GelenTutar = 0m;
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.GelenKaydetCagri);
    }

    [Fact]
    public async Task Tutar_degismediyse_cagri_yapilmaz()
    {
        var (vm, api) = await GelirFormu(null, new GelenDto(5, Hafta39.Start, "MEZAT", 5000m, KanalId: 1));
        KanalSec(vm, "MEZAT");

        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Equal(0, api.GelenKaydetCagri);
        Assert.Equal("Tutar değişmedi.", vm.GelenBilgi);
    }

    [Fact]
    public async Task Gecersiz_gelir_tutari_gonderilmez()
    {
        var (vm, api) = await GelirFormu(null, new GelenDto(5, Hafta39.Start, "MEZAT", 5000m, KanalId: 1));
        KanalSec(vm, "MEZAT"); vm.GelenTutar = ParaAyristirici.Gecersiz;

        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Equal(0, api.GelenKaydetCagri);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);
    }

    [Fact]
    public async Task Kanal_secilmeden_gelir_kaydedilmez()
    {
        var (vm, api) = await GelirFormu(); vm.GelenTutar = 100m;
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.GelenKaydetCagri);
        Assert.Equal("Kanal seçin.", vm.Hata);
    }

    [Fact]
    public async Task Donem_degisince_o_donemin_gelirleri_yuklenir_eski_yanit_uygulanmaz()
    {
        var gecikenHafta38 = new TaskCompletionSource<IReadOnlyList<GelenDto>>();
        var api = new SahteApi
        {
            GelenlerGetir = d => d == Hafta38.Start ? gecikenHafta38.Task
                : Task.FromResult<IReadOnlyList<GelenDto>>(d == Hafta40.Start ? new[] { new GelenDto(9, Hafta40.Start, "MEZAT", 700m, KanalId: 1) } : Array.Empty<GelenDto>()),
        };
        var (vm, _) = await GelirFormu(api);
        KanalSec(vm, "MEZAT");

        vm.GelenDonem = Hafta38; var eskiYukleme = vm.GelenYuklemesi;
        Assert.True(vm.GelenYukleniyor);
        Assert.False(vm.GelenKaydedilebilir);
        Assert.Equal("Dönem gelirleri yükleniyor…", vm.GelenBilgi);

        vm.GelenDonem = Hafta40; await vm.GelenYuklemesi;
        Assert.Equal(Hafta40.Start, api.SonGelenlerDonem);
        Assert.Equal(700m, vm.GelenTutar);

        gecikenHafta38.SetResult(new[] { new GelenDto(8, Hafta38.Start, "MEZAT", 9999m, KanalId: 1) });
        await eskiYukleme;

        Assert.Equal(700m, vm.GelenTutar);
        Assert.Equal(700m, vm.GelenMevcutToplam);
        Assert.False(vm.GelenYukleniyor);
        Assert.True(vm.GelenKaydedilebilir);
    }

    [Fact]
    public async Task Oturum_degisince_gelir_formu_temizlenir_bekleyen_yanit_uygulanmaz()
    {
        var geciken = new TaskCompletionSource<IReadOnlyList<GelenDto>>();
        var api = new SahteApi { KanallarListe = new[] { Mezat }, DonemlerListe = new[] { Hafta38, Hafta39 }, GelenlerListe = new[] { new GelenDto(5, Hafta39.Start, "MEZAT", 5000m, KanalId: 1) } };
        var auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor };
        var vm = new IslemlerViewModel(api, null, auth, new SabitZaman(new DateOnly(2026, 9, 23)));
        await vm.YukleAsync(); KanalSec(vm, "MEZAT");
        api.GelenlerGetir = _ => geciken.Task;
        vm.GelenDonem = Hafta38; var bekleyen = vm.GelenYuklemesi;

        auth.OturumSurumu++;
        geciken.SetResult(new[] { new GelenDto(8, Hafta38.Start, "MEZAT", 9999m, KanalId: 1) });
        await bekleyen;

        Assert.Null(vm.GelenDonem);
        Assert.Equal("", vm.GelenKanal);
        Assert.Equal(0m, vm.GelenTutar);
        Assert.Null(vm.GelenMevcutToplam);
        Assert.False(vm.GelenYukleniyor);
        Assert.False(vm.GelenKaydedilebilir);
    }

    [Fact]
    public async Task Kanal_filtresi_secilince_o_kanalla_listeler()
    {
        var api = new SahteApi
        {
            KanallarListe = new[] { new KanalDto(1, "MEZAT", true, 0, 0m), new KanalDto(2, "TOPTAN", true, 1, 0m) },
        };
        var vm = new IslemlerViewModel(api);
        await vm.YukleAsync();

        var mezatCipi = vm.FiltreKanallari.First(c => c.Ad == "MEZAT");
        await vm.SecFiltreKanalCommand.ExecuteAsync(mezatCipi);

        Assert.Equal("MEZAT", api.SonFiltreKanal);
        Assert.True(mezatCipi.Secili);
        Assert.True(vm.FiltreKanallari.First(c => c.Ad == "Tümü").Secili == false);
    }

    [Fact]
    public async Task Tum_kanal_cipi_filtreyi_temizler()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);
        await vm.YukleAsync();

        await vm.SecFiltreKanalCommand.ExecuteAsync(vm.FiltreKanallari.First(c => c.Ad == "Ortak"));
        await vm.SecFiltreKanalCommand.ExecuteAsync(vm.FiltreKanallari.First(c => c.Ad == "Tümü"));

        Assert.Null(api.SonFiltreKanal);
        Assert.Null(vm.FiltreKanal);
    }

    [Fact]
    public async Task Donem_secilince_o_haftanin_tarih_araligiyla_listeler()
    {
        var donem = new DonemDto(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 21), 2026, 6);
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api);
        await vm.YukleAsync();

        vm.SeciliDonem = donem;

        Assert.Equal(new DateOnly(2026, 6, 15), vm.FiltreBaslangic);
        Assert.Equal(new DateOnly(2026, 6, 21), vm.FiltreBitis);
    }

    [Fact]
    public async Task Ozet_islem_sayisi_ve_toplami_gosterir()
    {
        var api = new SahteApi
        {
            IslemlerListe = new[]
            {
                new IslemDto(1, new DateOnly(2026, 6, 15), "a", 100m, "MEZAT", GiderTipi.Cari, null),
                new IslemDto(2, new DateOnly(2026, 6, 16), "b", 250m, "MEZAT", GiderTipi.Cari, null),
            },
        };
        var vm = new IslemlerViewModel(api);
        await vm.YukleAsync();

        Assert.Equal(2, vm.FiltreSayi);
        Assert.Equal(350m, vm.FiltreToplam);
        Assert.Contains("2 işlem", vm.FiltreOzet);
    }
}
