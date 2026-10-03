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
        var vm = new AlislarViewModel(api, new SahteApi { YuklemeHatasi = new Exception("Alıcı finans çağrısı yapamaz") }, TestOturumu.Ac(Rol.Alici));
        await vm.YukleAsync();
        Assert.True(vm.VeriHazir);
        Assert.Null(vm.Hata);
        Assert.Equal(0, api.HesapOkuma);
    }

    [Fact]
    public async Task Cok_kalemli_cok_kanalli_taslak_tam_govdeyle_kaydedilir()
    {
        var api = new SahteAlisApi();
        var vm = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac(Rol.Alici));
        await vm.YukleAsync();
        vm.Tedarikci = "Firma";
        var ilk = vm.Kalemler[0];
        ilk.Aciklama = "Birinci";
        ilk.Tutar = 100m;
        ilk.Dagilimlar.Add(new(new[] { Kanal1, Kanal2 }) { Kanal = Kanal1, Tutar = 60m });
        ilk.Dagilimlar.Add(new(new[] { Kanal1, Kanal2 }) { Kanal = Kanal2, Tutar = 40m });
        vm.KalemEkleCommand.Execute(null);
        vm.Kalemler[1].Aciklama = "İkinci";
        vm.Kalemler[1].Tutar = 25m;
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
        var vm = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac(Rol.Alici));
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar[0]);
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
        var vm = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac());
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar[0]);
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
        var vm = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac());
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar[0]);
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
        var vm = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac());
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar[0]);
        vm.OdemeTutari = 30m;
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
        var bagli = gider with { Id = 92 };
        // Sunucu bağlı gideri zaten listelemez; istemci yine de yüklü alışların ödemelerine bağlı gideri seçtirmez. Genel gider
        // listesi hiç çekilmez.
        var baska = Alis() with { Id = 8, Odemeler = new[] { new AlisOdemeDto(5, 92, new(2026, 9, 20), 25m, null, true, Array.Empty<AlisDagilimDto>()) } };
        var api = new SahteAlisApi { Liste = new[] { Alis(), baska }, Giderler = new[] { gider, bagli } };
        var finans = new SahteApi { IslemlerListe = new[] { gider with { Id = 93 } } };
        var vm = new AlislarViewModel(api, finans, TestOturumu.Ac());
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar.Single(a => a.Veri.Id == 7));
        Assert.Equal(91, Assert.Single(vm.BaglanabilirGiderler).Veri.Id);
        Assert.Equal(0, finans.IslemlerCagri);
        vm.MevcutGiderKullan = true;
        vm.SeciliGider = vm.BaglanabilirGiderler[0];
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(91, api.SonOdeme!.MevcutIslemId);
        Assert.Equal(gider.Tarih, api.SonOdeme.Tarih);
        Assert.Equal(gider.TutarTl, api.SonOdeme.Tutar);
    }

    // IST4 / K3: alış ödemesinin kart listesi yalnız yeni takipteki, açık kartlardır (sunucu kartlı yeni ödemeyi başka karta
    // bağlamaz). Bağlanan mevcut gider kendi (eski) kartıyla gönderilir; düzeltilen ödemenin kendi kartı listede korunur.
    private static IReadOnlyList<KrediKartiDto> Kartlar() =>
    [
        new KrediKartiDto(1, "Eski kart", new(2026, 1, 10), new(2026, 1, 20), 0, 0),
        new KrediKartiDto(2, "Takipli", new(2026, 1, 10), new(2026, 1, 20), 0, 0, YeniTakip: true, Aktif: true),
        new KrediKartiDto(3, "Kapalı", new(2026, 1, 10), new(2026, 1, 20), 0, 0, YeniTakip: true, Aktif: false),
    ];

    [Fact]
    public async Task Odeme_kart_listesi_yalniz_takipteki_acik_kartlardir_bagli_gider_kendi_kartiyla_gider()
    {
        var eskiKartliGider = new IslemDto(91, new(2026, 9, 20), "Firma", 25m, "Ortak", GiderTipi.KrediKarti, null, KrediKartiId: 1);
        // Bağlanabilir giderler ALS'den beri sayfalı uçtan (SahteAlisApi.Giderler) gelir.
        var api = new SahteAlisApi { Liste = new[] { Alis() }, Giderler = new[] { eskiKartliGider } };
        var vm = new AlislarViewModel(api, new SahteApi { KrediKartlariListe = Kartlar() }, TestOturumu.Ac());
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar[0]);

        Assert.Equal(new[] { "Nakit / banka", "Takipli" }, vm.OdemeKartlari.Select(k => k.Ad));
        vm.MevcutGiderKullan = true;
        vm.SeciliGider = vm.BaglanabilirGiderler.Single(g => g.Veri.Id == 91);
        await vm.OdemeKaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal(91, api.SonOdeme!.MevcutIslemId);
        Assert.Equal(1, api.SonOdeme.KrediKartiId);
    }

    // gap-coklu-giris-cift-sayim-mutabakat-1 (ters sıra): ekstreden önce girilmiş kart harcaması takipli kartla ödemeye bağlanır;
    // tarih ve tutar harcamadan gelir, ikinci harcama oluşmaz ve benzer kayıt sorulmaz. Banka ekstresi gideri de bağlanabilir.
    [Fact]
    public async Task Takipli_kartla_odeme_ekstreden_gelen_kart_harcamasina_baglanir()
    {
        var harcama = new BaglanabilirKartHarcamasiDto(31, 2, new(2026, 9, 19), "MEZAT", 60m, EkstreKayitId: 7);
        var banka = new IslemDto(95, new(2026, 9, 18), "PDF gider", 40m, "Genel kasa", GiderTipi.Cari, null, EkstreKayitId: 4);
        var api = new SahteAlisApi { Liste = new[] { Alis() }, KartHarcamalari = new[] { harcama, harcama with { Id = 32, Tutar = 10m } }, Giderler = new[] { banka } };
        var benzerlik = new BenzerKayitTests.Sahte();
        var vm = new AlislarViewModel(api, new SahteApi { KrediKartlariListe = Kartlar() }, TestOturumu.Ac(), benzerlikApi: benzerlik);
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar[0]);
        Assert.Contains("banka ekstresinden", Assert.Single(vm.BaglanabilirGiderler).Ad);

        Assert.False(vm.KartHarcamasiBaglanabilir);
        vm.OdemeKarti = vm.OdemeKartlari.Single(k => k.Id == 2);
        vm.OdemeTutari = 60m;
        Assert.True(vm.KartHarcamasiBaglanabilir);
        await vm.KartHarcamalariniGetirCommand.ExecuteAsync(null);
        Assert.Equal((2, (decimal?)60m), Assert.Single(api.KartHarcamaSorgulari));
        Assert.Equal(new[] { "Yeni kart harcaması oluştur", "#31 · 19.09.2026 · MEZAT · 60,00 ₺ · ekstreden" }, vm.BaglanabilirKartHarcamalari.Select(h => h.Ad));
        vm.SeciliKartHarcamasi = vm.BaglanabilirKartHarcamalari[1];
        Assert.False(vm.OdemeAlanlariAcik);
        Assert.Equal(new DateTime(2026, 9, 19), vm.OdemeTarihi);
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal((31, (int?)2, new DateOnly(2026, 9, 19), 60m, (int?)null), (api.SonOdeme!.MevcutKartHarcamaId, api.SonOdeme.KrediKartiId, api.SonOdeme.Tarih, api.SonOdeme.Tutar, api.SonOdeme.MevcutIslemId));
        Assert.Equal(0, benzerlik.Cagri);
        Assert.Contains("ikinci bir kart harcaması oluşturulmadı", vm.Mesaj);
        // Form temizlenir; kart değişince eski kartın harcamaları kalkar.
        Assert.Empty(vm.BaglanabilirKartHarcamalari);
        Assert.Null(vm.SeciliKartHarcamasi);
    }

    [Fact]
    public async Task Odeme_duzeltmesinde_odemenin_kendi_eski_karti_korunur_yeni_kart_takiptekilerden_secilir()
    {
        var odeme = new AlisOdemeDto(5, 91, new(2026, 9, 20), 25m, 3, false, Array.Empty<AlisDagilimDto>(), KrediKartiAdi: "Kapalı");
        var api = new SahteAlisApi { Liste = new[] { Alis(odenen: 25) with { Odemeler = new[] { odeme } } } };
        var vm = new AlislarViewModel(api, new SahteApi { KrediKartlariListe = Kartlar() }, TestOturumu.Ac());
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar[0]);

        vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]);

        Assert.Equal(new[] { "Nakit / banka", "Takipli", "Kapalı (eski kayıt)" }, vm.DuzeltmeKartlari.Select(k => k.Ad));
        Assert.Equal(3, vm.DuzeltmeKarti!.Id);
    }

    // gap-coklu-giris-cift-sayim-mutabakat-5: kart takibindeki ödemenin tarihi, tutarı ve kartı kilitlidir; yalnız hedef alışa taşınır
    // (form değerleri değil ödemenin kendi değerleri gider). Alıştan ayırma ödemenin bugünkü kanal paylarıyla başlar; pay toplamı
    // ödemeye eşit olmadan istek gitmez. Harcama korunmazsa kanal payı gönderilmez (ödenmemiş harcama gideriyle kalkar).
    [Fact]
    public async Task Takipli_kart_odemesi_yalniz_baska_alisa_tasinir_alistan_ayirma_kanal_paylarini_gonderir()
    {
        var odeme = new AlisOdemeDto(5, 91, new(2026, 9, 20), 60m, 2, false, new[] { new AlisDagilimDto(1, "MEZAT", 36m), new AlisDagilimDto(2, "PERAKENDE", 24m) }, KrediKartiAdi: "Takipli");
        var api = new SahteAlisApi { Liste = new[] { Alis(odenen: 60) with { Odemeler = new[] { odeme } }, Alis() with { Id = 8, Surum = 5 } } };
        var vm = new AlislarViewModel(api, new SahteApi { KrediKartlariListe = Kartlar() }, TestOturumu.Ac(), api);
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar.Single(a => a.Veri.Id == 7));

        vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]);
        Assert.True(vm.DuzeltmeTakipli);
        Assert.False(vm.DuzeltmeAlanlariAcik);
        vm.DuzeltmeTutari = 50m;
        vm.DuzeltmeAciklamasi = "Yanlış alış";
        await vm.OdemeDuzeltKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonDuzelt);
        Assert.Contains("hedef alış", vm.Hata);
        vm.HedefAlis = vm.DuzeltmeHedefleri.Single(a => a.Veri.Id == 8);
        await vm.OdemeDuzeltKaydetCommand.ExecuteAsync(null);
        Assert.Equal((new DateOnly(2026, 9, 20), 60m, (int?)2, (int?)8, (int?)5), (api.SonDuzelt!.Tarih, api.SonDuzelt.Tutar, api.SonDuzelt.KrediKartiId, api.SonDuzelt.HedefAlisId, api.SonDuzelt.HedefSurum));

        vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]);
        Assert.Equal(new[] { ("MEZAT", 36m), ("PERAKENDE", 24m) }, vm.AyirmaPaylari.Select(p => (p.Kanal, p.Tutar)));
        vm.DuzeltmeAciklamasi = "Başka alışın ödemesi";
        vm.HarcamayiKoru = true;
        Assert.True(vm.AyirmaPaylariGorunur);
        vm.AyirmaPaylari[0].Tutar = 30m;
        await vm.OdemeIptalAsync();
        Assert.Null(api.SonIptal);
        Assert.Contains("toplamı ödeme tutarına", vm.Hata);
        vm.AyirmaPaylari[0].Tutar = 36m;
        await vm.OdemeIptalAsync();
        Assert.Equal(new[] { (1, 36m), (2, 24m) }, api.SonIptal!.KanalDagilimlari!.Select(p => (p.KanalId, p.Tutar)));
        Assert.Contains("alıştan ayrıldı", vm.Mesaj);

        vm.OdemeDuzeltCommand.Execute(vm.Odemeler[0]);
        vm.DuzeltmeAciklamasi = "Harcama yapılmadı";
        await vm.OdemeIptalAsync();
        Assert.Null(api.SonIptal!.KanalDagilimlari);
        Assert.Contains("taksitleri de kaldırıldı", vm.Mesaj);
    }

    // gap-coklu-giris-cift-sayim-mutabakat-6: takipli kartla yeni kart harcaması oluşturan ödeme taksit sayısını ve ilk kesimi taşır;
    // nakit ödemede taksit gönderilmez.
    [Fact]
    public async Task Takipli_kartla_yeni_odeme_taksit_sayisini_ve_ilk_kesimi_gonderir()
    {
        var api = new SahteAlisApi { Liste = new[] { Alis() } };
        var benzerlik = new BenzerKayitTests.Sahte { Bekleyen = Task.FromResult<IReadOnlyList<BenzerKayitDto>>(Array.Empty<BenzerKayitDto>()) };
        var vm = new AlislarViewModel(api, new SahteApi { KrediKartlariListe = Kartlar() }, TestOturumu.Ac(), benzerlikApi: benzerlik);
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar[0]);

        vm.OdemeTutari = 30m;
        vm.OdemeTaksitSayisi = 3;
        Assert.False(vm.TaksitGirilebilir);
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Null(api.SonOdeme!.TaksitSayisi);

        vm.OdemeKarti = vm.OdemeKartlari.Single(k => k.Id == 2);
        vm.OdemeTutari = 30m;
        vm.OdemeTaksitSayisi = 61;
        Assert.True(vm.TaksitGirilebilir);
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Contains("1 ile 60", vm.Hata);
        vm.OdemeTaksitSayisi = 3;
        vm.OdemeIlkKesimVar = true;
        // Beklenen tarih formdaki ödeme tarihinden: DateTime.Today'e karşı karşılaştırma gece yarısını geçen koşuda kayardı.
        var ilkKesim = vm.OdemeTarihi.AddDays(16);
        vm.OdemeIlkKesimTarihi = ilkKesim;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal(((int?)2, (int?)3, (DateOnly?)DateOnly.FromDateTime(ilkKesim)), (api.SonOdeme!.KrediKartiId, api.SonOdeme.TaksitSayisi, api.SonOdeme.IlkKesimTarihi));
        Assert.Contains("3 taksitli", vm.Mesaj);
        // Form temizlenir: sonraki ödeme tek taksitle başlar.
        Assert.Equal(1, vm.OdemeTaksitSayisi);
        Assert.False(vm.OdemeIlkKesimVar);
    }

    [Fact]
    public async Task Takipli_kart_giderinde_taksit_yalniz_yeni_kayitta_gonderilir()
    {
        var api = new SahteApi { KrediKartlariListe = Kartlar() };
        var vm = new IslemlerViewModel(api, TestOturumu.Ac()) { DuzenTarih = new DateTime(2026, 9, 20), DuzenCari = "Telefon", DuzenTutar = 3000m, DuzenKanal = "MEZAT" };
        vm.DuzenTaksitSayisi = 6;
        Assert.False(vm.TaksitGirilebilir);
        vm.DuzenTip = GiderTipi.KrediKarti;
        vm.DuzenKrediKartiId = 2;
        Assert.True(vm.TaksitGirilebilir);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(((int?)6, (DateOnly?)null), (api.SonIslemOlustur!.TaksitSayisi, api.SonIslemOlustur.IlkKesimTarihi));
        Assert.Equal(1, vm.DuzenTaksitSayisi);

        vm.Duzenle(new IslemDto(11, new(2026, 9, 20), "Telefon", 3000m, "MEZAT", GiderTipi.KrediKarti, null, KrediKartiId: 2));
        vm.DuzenTaksitSayisi = 3;
        Assert.False(vm.TaksitGirilebilir);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonIslemGuncelle!.Value.G.TaksitSayisi);
    }

    [Fact]
    public async Task Alici_hesabi_sifreyi_bos_birakinca_korur_pasife_alinabilir()
    {
        var api = new SahteAlisApi();
        var vm = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac());
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
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api, TestOturumu.Ac());
        var gider = new IslemDto(9, new(2026, 9, 20), "Firma", 10m, "Dağılım bekliyor", GiderTipi.Cari, null, AlisId: 7);
        vm.Duzenle(gider);
        Assert.Equal(0, vm.DuzenId);
        Assert.Contains("Alışlar", vm.Hatalar.Genel);
        await vm.SilCommand.ExecuteAsync(gider);
        Assert.Null(api.SonIslemSil);
        Assert.Contains("Alışlar", vm.Hata);
    }

    [Fact]
    public async Task Kaydedilmemis_degisim_odeme_secim_yeni_ve_yenile_ile_kaybolmaz()
    {
        var api = new SahteAlisApi { Liste = new[] { Alis(), Alis() with { Id = 8 } } };
        var vm = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac());
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar.First(a => a.Veri.Id == 7));
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
        var auth = TestOturumu.Ac(Rol.Alici);
        var vm = new AlislarViewModel(api, new SahteApi(), auth);
        var ilk = vm.YukleAsync();
        auth.OturumSurumu++;
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
        var auth = TestOturumu.Ac();
        var vm = new AlislarViewModel(api, new SahteApi(), auth);
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar[0]);
        vm.OdemeTutari = 10m;
        var odeme = vm.OdemeKaydetCommand.ExecuteAsync(null);
        TestOturumu.YeniOturum(auth, Rol.Alici);
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
        var alis = Alis() with
        {
            Kalemler = new[] { new AlisKalemDto(1, "Mal", 100m,
            new[] { new AlisDagilimDto(1, "MEZAT", 100m), new AlisDagilimDto(2, "PERAKENDE", 0m) }) }
        };
        var vm = new AlislarViewModel(new SahteAlisApi { Liste = new[] { alis } }, new SahteApi(), TestOturumu.Ac());
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar[0]);
        vm.OdemeTutari = 10m;
        Assert.Equal(10m, Assert.Single(vm.OdemeOnizleme).Tutar);
    }

    [Fact]
    public async Task Baglanabilir_giderler_sunucu_sayfasindan_dolar_arama_ve_daha_eski_sayfa_ister()
    {
        var giderler = Enumerable.Range(1, 3).Select(i => new IslemDto(90 + i, new(2026, 9, 20 - i), i == 2 ? "Kargo" : "Firma", 10m * i, "MEZAT", GiderTipi.Cari, null)).ToArray();
        var api = new SahteAlisApi { Liste = new[] { Alis() }, Giderler = giderler, GiderSayfaBoyutu = 2 };
        var finans = new SahteApi();
        var vm = new AlislarViewModel(api, finans, TestOturumu.Ac());
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar[0]);
        Assert.Equal(new[] { 91, 92 }, vm.BaglanabilirGiderler.Select(g => g.Veri.Id));
        Assert.True(vm.DahaFazlaGiderVar);
        Assert.Equal(0, finans.IslemlerCagri);

        await vm.DahaFazlaGiderCommand.ExecuteAsync(null);
        Assert.Equal(new[] { 91, 92, 93 }, vm.BaglanabilirGiderler.Select(g => g.Veri.Id));
        Assert.False(vm.DahaFazlaGiderVar);
        Assert.Equal("2", api.GiderSorgulari[^1].Imlec);

        vm.GiderArama = "Kargo";
        await vm.GiderAraCommand.ExecuteAsync(null);
        Assert.Equal(("Kargo", (decimal?)null, (string?)null, (decimal?)null), api.GiderSorgulari[^1]);
        Assert.Equal(92, Assert.Single(vm.BaglanabilirGiderler).Veri.Id);
        // Tutar gibi okunan metin yalnız tutar süzgeci değildir: metin (fatura/sipariş numarası) ya da tutar olarak eşleşir.
        vm.GiderArama = "30,00";
        await vm.GiderAraCommand.ExecuteAsync(null);
        Assert.Equal(("30,00", (decimal?)null, (string?)null, (decimal?)30m), api.GiderSorgulari[^1]);
        Assert.Equal(93, Assert.Single(vm.BaglanabilirGiderler).Veri.Id);
    }

    [Fact]
    public async Task Daha_eski_sayfa_imleci_onu_ureten_aramaya_aittir_metin_degisince_arama_bastan_yapilir()
    {
        var giderler = Enumerable.Range(1, 4).Select(i => new IslemDto(90 + i, new(2026, 9, 20 - i), i % 2 == 0 ? "Kargo" : "Firma", 10m * i, "MEZAT", GiderTipi.Cari, null)).ToArray();
        var api = new SahteAlisApi { Liste = new[] { Alis() }, Giderler = giderler, GiderSayfaBoyutu = 1 };
        var vm = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac());
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar[0]);
        Assert.Equal(new[] { 91 }, vm.BaglanabilirGiderler.Select(g => g.Veri.Id));

        // Kullanıcı yeni metni yazıp aramadan 'Daha eski giderler'e basar: süzgeçsiz sorgunun imleci yeni metinle birleştirilmez
        // (Kargo'nun daha yeni eşleşmesi atlanırdı); arama bu metinle baştan yapılır.
        vm.GiderArama = " Kargo ";
        await vm.DahaFazlaGiderCommand.ExecuteAsync(null);
        Assert.Equal(("Kargo", (decimal?)null, (string?)null, (decimal?)null), api.GiderSorgulari[^1]);
        Assert.Equal(new[] { 92 }, vm.BaglanabilirGiderler.Select(g => g.Veri.Id));
        Assert.True(vm.DahaFazlaGiderVar);

        // Metin değişmedikçe imleç aynı aramanın sonraki sayfasıdır.
        await vm.DahaFazlaGiderCommand.ExecuteAsync(null);
        Assert.Equal(("Kargo", (decimal?)null, "1", (decimal?)null), api.GiderSorgulari[^1]);
        Assert.Equal(new[] { 92, 94 }, vm.BaglanabilirGiderler.Select(g => g.Veri.Id));
        Assert.False(vm.DahaFazlaGiderVar);
    }

    [Fact]
    public async Task Yeni_alis_zaman_asiminda_ayni_istek_kimligiyle_yeniden_gonderilir_duzenleme_kimlik_tasimaz()
    {
        var api = new SahteAlisApi { OlusturmaHatasi = new TimeoutException("Sunucu 15 sn içinde yanıt vermedi.") };
        var vm = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac(Rol.Alici));
        await vm.YukleAsync();
        void Doldur()
        { vm.Tedarikci = "Firma"; vm.Kalemler[0].Aciklama = "Mal"; vm.Kalemler[0].Tutar = 100m; }
        Doldur();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Hata);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.Olusturmalar.Count);
        Assert.NotNull(api.Olusturmalar[0].IstekId);
        Assert.Equal(api.Olusturmalar[0].IstekId, api.Olusturmalar[1].IstekId);

        // Kaydedilen alış düzenlenirken (PUT) istek kimliği gönderilmez; yeni formdaki aynı içerik yeni kimlik alır.
        vm.Tedarikci = "Firma A.Ş.";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonYaz!.IstekId);
        vm.YeniCommand.Execute(null);
        Doldur();
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(3, api.Olusturmalar.Count);
        Assert.NotEqual(api.Olusturmalar[0].IstekId, api.Olusturmalar[2].IstekId);
    }

    internal sealed class SahteAlisApi : IAlisApi, IAlisOdemeApi
    {
        public AlisOdemeDuzeltYaz? SonDuzelt;
        public AlisOdemeIptalYaz? SonIptal;
        public Task<AlisDto> AlisOdemeDuzeltAsync(int alisId, int odemeId, AlisOdemeDuzeltYaz g) { SonDuzelt = g; return Task.FromResult(Kayit); }
        public Task<AlisDto> AlisOdemeIptalAsync(int alisId, int odemeId, AlisOdemeIptalYaz g) { SonIptal = g; return Task.FromResult(Kayit); }
        public IReadOnlyList<AlisDto> Liste = Array.Empty<AlisDto>();
        public AlisYaz? SonYaz;
        public AlisDurumYaz? SonDurum;
        public AlisOdemeYaz? SonOdeme;
        public AliciYaz? SonAlici;
        public List<AlisYaz> Olusturmalar = new();
        public Exception? OlusturmaHatasi;
        public IReadOnlyList<IslemDto> Giderler = Array.Empty<IslemDto>();
        /// <summary>Bağlanabilir gider sorguları (arama, tutar, imleç, arama metninin tutar okuması); sayfa boyutu 1 ise imleçle sayfalanır.</summary>
        public List<(string? Arama, decimal? Tutar, string? Imlec, decimal? AramaTutari)> GiderSorgulari = new();
        public int GiderSayfaBoyutu = 50;
        public bool OdemeHatasi;
        /// <summary>Bütün ödeme istekleri (sırayla) ve ayarlanırsa ödeme isteğinin bitirileceği hata (ör. 409, zaman aşımı).</summary>
        public List<AlisOdemeYaz> OdemeIstekleri = new();
        public Exception? OdemeIstisnasi;
        public Func<Task<IReadOnlyList<AlisDto>>>? ListeGetir;
        public Task<AlisDto>? OdemeYaniti;
        public int HesapOkuma;
        private AlisDto Kayit => Liste.FirstOrDefault() ?? Alis();
        public Task<IReadOnlyList<AlisKanalDto>> AlisKanallariAsync() => Task.FromResult<IReadOnlyList<AlisKanalDto>>(new[] { Kanal1, Kanal2 });
        public Task<IReadOnlyList<AlisDto>> AlislarAsync() => ListeGetir?.Invoke() ?? Task.FromResult(Liste);
        public Task<AlisDto> AlisOlusturAsync(AlisYaz g)
        {
            SonYaz = g;
            Olusturmalar.Add(g);
            if (OlusturmaHatasi is { } hata)
            { OlusturmaHatasi = null; return Task.FromException<AlisDto>(hata); }
            return Task.FromResult(Kayit with { Surum = 3 });
        }
        public Task<BaglanabilirGiderSayfasi> BaglanabilirGiderlerAsync(string? arama = null, decimal? tutar = null, DateOnly? baslangic = null, DateOnly? bitis = null, string? imlec = null, int? limit = null, decimal? aramaTutari = null)
        {
            GiderSorgulari.Add((arama, tutar, imlec, aramaTutari));
            var uygun = Giderler.Where(g => (arama is null && aramaTutari is null || arama is not null && g.Cari.Contains(arama) || g.TutarTl == aramaTutari)
                && (tutar is null || g.TutarTl == tutar)).ToList();
            var bas = imlec is null ? 0 : int.Parse(imlec);
            var sayfa = uygun.Skip(bas).Take(GiderSayfaBoyutu).ToList();
            var devam = bas + sayfa.Count < uygun.Count;
            return Task.FromResult(new BaglanabilirGiderSayfasi(sayfa.Select(Baglanabilir).ToList(), devam ? (bas + sayfa.Count).ToString() : null, devam));
        }
        public Task<AlisDto> AlisGuncelleAsync(int id, AlisYaz g) { SonYaz = g; return Task.FromResult(Kayit with { Surum = 3 }); }
        public Task<AlisDto> AlisGonderAsync(int id, AlisDurumYaz g) { SonDurum = g; return Task.FromResult(Kayit with { Durum = "Incelemede", Surum = 4 }); }
        public Task<AlisDto> AlisOnaylaAsync(int id, AlisDurumYaz g) { SonDurum = g; return Task.FromResult(Kayit with { Durum = "Onaylandi", Surum = 4 }); }
        public Task<AlisDto> AlisIadeAsync(int id, AlisDurumYaz g) { SonDurum = g; return Task.FromResult(Kayit with { Durum = "Taslak", EditorNotu = g.Not, Surum = 4 }); }
        public Task<AlisDto> AlisOdemeKaydetAsync(int id, AlisOdemeYaz g)
        {
            SonOdeme = g;
            OdemeIstekleri.Add(g);
            if (OdemeYaniti is not null)
                return OdemeYaniti;
            if (OdemeHatasi)
                return Task.FromException<AlisDto>(new HttpRequestException("yanıt kayboldu"));
            if (OdemeIstisnasi is { } istisna)
                return Task.FromException<AlisDto>(istisna);
            return Task.FromResult(Kayit with
            {
                Surum = 3,
                Odenen = Kayit.Odenen + g.Tutar,
                Kalan = Kayit.Kalan - g.Tutar,
                Odemeler = new[] { new AlisOdemeDto(1, g.MevcutIslemId ?? 90, g.Tarih, g.Tutar, g.KrediKartiId, true, Array.Empty<AlisDagilimDto>()) }
            });
        }
        internal static BaglanabilirGiderDto Baglanabilir(IslemDto g) => new(g.Id, g.Tarih, g.Cari, g.TutarTl, g.Kanal, null, g.Tip, g.Not, g.KrediKartiId, g.EkstreKayitId);
        /// <summary>Bağlanabilir kart harcamaları ve sorguları (kart, tutar).</summary>
        public IReadOnlyList<BaglanabilirKartHarcamasiDto> KartHarcamalari = Array.Empty<BaglanabilirKartHarcamasiDto>();
        public List<(int Kart, decimal? Tutar)> KartHarcamaSorgulari = new();
        public Task<IReadOnlyList<BaglanabilirKartHarcamasiDto>> BaglanabilirKartHarcamalariAsync(int krediKartiId, decimal? tutar = null)
        {
            KartHarcamaSorgulari.Add((krediKartiId, tutar));
            return Task.FromResult<IReadOnlyList<BaglanabilirKartHarcamasiDto>>(KartHarcamalari.Where(h => h.KrediKartiId == krediKartiId && (tutar is null || h.Tutar == tutar)).ToList());
        }
        public Task<IReadOnlyList<AliciDto>> AlicilarAsync() { HesapOkuma++; return Task.FromResult<IReadOnlyList<AliciDto>>(Array.Empty<AliciDto>()); }
        public Task<AliciDto> AliciOlusturAsync(AliciYaz g) { SonAlici = g; return Task.FromResult(new AliciDto(4, g.Kullanici, g.Ad, g.Aktif)); }
        public Task<AliciDto> AliciGuncelleAsync(int id, AliciYaz g) { SonAlici = g; return Task.FromResult(new AliciDto(id, g.Kullanici, g.Ad, g.Aktif)); }
    }
}
