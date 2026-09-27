using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class FinansTakipTests
{
    private static readonly DateOnly Tarih = new(2026, 9, 23);
    private static SahteApi Finans() => new() { KanallarListe = new[] { new KanalDto(1, "MEZAT", true, 0, 0), new KanalDto(2, "PERAKENDE", true, 1, 0) } };
    private static AuthViewModel Auth() => new(new SahteApi()) { AktifRol = Rol.Editor };
    private static async Task<KartTakipViewModel> KartVm(Fake api, AuthViewModel? auth = null, IBenzerKayitApi? benzerlik = null)
    {
        var vm = new KartTakipViewModel(api, Finans(), auth ?? Auth(), benzerlik); await vm.YukleAsync(); vm.SecCommand.Execute(vm.Kartlar[0]); return vm;
    }
    private static async Task<KrediTakipViewModel> KrediVm(Fake api, AuthViewModel? auth = null)
    {
        var vm = new KrediTakipViewModel(api, Finans(), auth ?? Auth()); await vm.YukleAsync(); vm.SecCommand.Execute(vm.Krediler[0]); return vm;
    }
    [Fact] public async Task Kart_odeme_onizlemesi_degisen_tutarla_kaydedilemez()
    {
        var api = new Fake(); var vm = await KartVm(api); vm.OdemeTutari = 10;
        await vm.OdemeKaydetCommand.ExecuteAsync(null); Assert.Empty(api.OdemeIstekleri);
        await vm.OdemeOnizleCommand.ExecuteAsync(null); Assert.Contains("MEZAT", vm.OdemeOnizleme); Assert.NotEqual(Guid.Empty, api.OnizlenenOdeme!.IstekId);
        vm.OdemeTutari = 11; await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.OdemeIstekleri); Assert.Contains("önizlemeyi", vm.Hata);
    }
    [Fact] public async Task Kart_odeme_ag_hatasinda_onizlemedeki_ayni_anahtarla_tekrarlanir()
    {
        var api = new Fake { OdemeHata = true }; var vm = await KartVm(api); vm.OdemeTutari = 10;
        await vm.OdemeOnizleCommand.ExecuteAsync(null); await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Contains("ulaşılamadı", vm.Hata); api.OdemeHata = false; await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.OdemeIstekleri.Count); Assert.Equal(api.OnizlenenOdeme!.IstekId, api.OdemeIstekleri[0].IstekId);
        Assert.Equal(api.OdemeIstekleri[0], api.OdemeIstekleri[1]); Assert.Equal(0, vm.OdemeTutari);
    }
    [Fact] public async Task Kart_odeme_zaman_asiminda_ayni_istek_anahtariyla_yeniden_denenir_cift_kayit_olmaz()
    {
        var api = new Fake { OdemeHatasi = new TimeoutException(KasaZamanAsimlari.Ileti) }; var vm = await KartVm(api); vm.OdemeTutari = 10;
        await vm.OdemeOnizleCommand.ExecuteAsync(null); await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Contains("zamanında yanıt vermedi", vm.Hata); Assert.Equal(10, vm.OdemeTutari);
        api.OdemeHatasi = null; await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.OdemeIstekleri.Count); Assert.Equal(api.OdemeIstekleri[0].IstekId, api.OdemeIstekleri[1].IstekId);
        Assert.Equal(api.OdemeIstekleri[0], api.OdemeIstekleri[1]); Assert.Null(vm.Hata);
    }
    [Fact] public async Task Kart_gecis_paylari_degistiginde_eski_onay_gonderilmez()
    {
        var api = new Fake { Kart = Fake.OrnekKart() with { YeniTakip = false } }; var vm = await KartVm(api);
        vm.GecisAciklama = "Eski borç kontrol edildi"; vm.PayEkle(vm.GecisPaylari); vm.GecisPaylari[0].Kanal = vm.Kanallar[0]; vm.GecisPaylari[0].Tutar = 100;
        await vm.GecisOnizleCommand.ExecuteAsync(null); vm.GecisOnay = true; vm.GecisPaylari[0].Tutar = 90;
        await vm.GecisiOnaylaCommand.ExecuteAsync(null); Assert.Null(api.KartGecis); Assert.Contains("önizlemesini", vm.Hata);
        await vm.GecisOnizleCommand.ExecuteAsync(null); vm.GecisOnay = true; await vm.GecisiOnaylaCommand.ExecuteAsync(null);
        Assert.True(api.KartGecis!.Onay); Assert.NotEqual(Guid.Empty, api.KartGecis.IstekId); Assert.Equal(90, api.KartGecis.Dagilimlar.Single().Tutar);
    }
    [Fact] public async Task Kart_gecis_onizlemesi_elle_degismemis_tutari_onerilen_tutarla_doldurup_yeniden_onizler()
    {
        var api = new Fake { Kart = Fake.OrnekKart() with { YeniTakip = false }, KartGecisYaniti = Fake.SunucuGibi(80) }; var vm = await KartVm(api);
        vm.GecisAciklama = "Banka ekstresiyle kontrol edildi";
        Assert.Equal(100, vm.OncedenSayilan);                        // web gibi: kalan borç ile kart borcunun küçüğü
        await vm.GecisOnizleCommand.ExecuteAsync(null);
        Assert.Equal(80, vm.OncedenSayilan);
        Assert.Equal(new[] { 100m, 80m }, api.KartGecisOnizlemeleri.Select(g => g.KasadaOncedenSayilanTutar));
        Assert.Contains("önerilen tutarla (80,00 ₺) dolduruldu", vm.GecisOnizleme);
        Assert.Null(vm.GecisEngeli); Assert.True(vm.GecisOnaylanabilir); Assert.True(vm.GecisiOnaylaCommand.CanExecute(null));
        vm.GecisOnay = true; await vm.GecisiOnaylaCommand.ExecuteAsync(null);
        Assert.True(api.KartGecis!.Onay); Assert.Equal(80, api.KartGecis.KasadaOncedenSayilanTutar); Assert.Equal(api.KartGecisOnizlemeleri[1].IstekId, api.KartGecis.IstekId);
    }
    [Fact] public async Task Kart_gecisinde_elle_degismemis_tutar_kalan_borcu_izler_elle_girilen_korunur()
    {
        var api = new Fake { Kart = Fake.OrnekKart() with { YeniTakip = false } }; var vm = await KartVm(api);
        vm.GecisKalanBorc = 60; Assert.Equal(60, vm.OncedenSayilan);
        vm.GecisKalanBorc = 150; Assert.Equal(100, vm.OncedenSayilan);
        vm.GecisKalanBorc = ParaAyristirici.Gecersiz; Assert.Equal(100, vm.OncedenSayilan);
        vm.OncedenSayilan = 40; vm.GecisKalanBorc = 70; Assert.Equal(40, vm.OncedenSayilan);
        vm.SecCommand.Execute(vm.Kartlar[0]); Assert.Equal(100, vm.OncedenSayilan);   // yeniden seçim: öneri yeniden işler
        vm.GecisKalanBorc = 90; Assert.Equal(90, vm.OncedenSayilan);
    }
    [Fact] public async Task Kabul_edilemez_kart_gecisi_nedenini_gosterir_onay_gonderilmez_elle_tutar_ezilmez()
    {
        var api = new Fake { Kart = Fake.OrnekKart() with { YeniTakip = false }, KartGecisYaniti = Fake.SunucuGibi(80) }; var vm = await KartVm(api);
        vm.GecisAciklama = "Kontrol edildi"; vm.OncedenSayilan = 50;
        await vm.GecisOnizleCommand.ExecuteAsync(null);
        Assert.Equal(50, vm.OncedenSayilan); Assert.Single(api.KartGecisOnizlemeleri);
        Assert.Contains("en az 80,00 ₺", vm.GecisEngeli); Assert.Contains("ikinci kez", vm.GecisEngeli);
        Assert.False(vm.GecisOnaylanabilir); Assert.False(vm.GecisiOnaylaCommand.CanExecute(null));
        vm.GecisOnay = true; await vm.GecisiOnaylaCommand.ExecuteAsync(null); Assert.Null(api.KartGecis);

        vm.GecisKalanBorc = 90; vm.OncedenSayilan = 90;              // önerilenin (80) üstü
        await vm.GecisOnizleCommand.ExecuteAsync(null);
        Assert.Equal(90, vm.OncedenSayilan); Assert.Contains("aşamaz", vm.GecisEngeli); Assert.Contains("hiçbir zaman düşmez", vm.GecisEngeli);
        vm.GecisOnay = true; await vm.GecisiOnaylaCommand.ExecuteAsync(null); Assert.Null(api.KartGecis);

        vm.OncedenSayilan = 80; await vm.GecisOnizleCommand.ExecuteAsync(null);
        Assert.Null(vm.GecisEngeli); Assert.True(vm.GecisiOnaylaCommand.CanExecute(null));
        vm.GecisOnay = true; await vm.GecisiOnaylaCommand.ExecuteAsync(null); Assert.Equal(80, api.KartGecis!.KasadaOncedenSayilanTutar);
    }
    [Fact] public void Kart_gecis_onizleme_metni_sunucu_toplamlarini_ve_farkin_anlamini_yazar()
    {
        var kart = new TakipGecisDto("Kart", 1, new(2026, 10, 1), -100, -100, 600, new[] { "Sunucu açıklaması" }, true, 800, 300, 200, new DateOnly(2026, 10, 31), 700, 400);
        var metin = TakipMetni.Gecis(kart);
        foreach (var parca in new[] { "Geçiş: 01.10.2026", "Genel kasa farkı: -100,00 ₺ · kanal farkı: -100,00 ₺", "ödendiğinde kasadan düşer", "ikinci kez", "Kasada önceden sayılan: 600,00 ₺",
            "Sistem kart borcu: 800,00 ₺", "eski kuralla kasadan düşen/düşecek: 300,00 ₺", "Bekleyen eski düşüm: 200,00 ₺ · son düşüm 31.10.2026", "Önerilen kasada önceden sayılan: 700,00 ₺", "en az 400,00 ₺", "Sunucu açıklaması" })
            Assert.Contains(parca, metin);
        Assert.Contains("hiçbir zaman düşmez", TakipMetni.Gecis(kart with { GenelKasaAnlikFarki = 50, EnAzKasadaSayilanTutar = 700 }));
        var tutarli = TakipMetni.Gecis(kart with { GenelKasaAnlikFarki = 0, KanalAnlikFarki = 0, BekleyenEskiDusumTutari = 0, SonBekleyenDusumTarihi = null, EnAzKasadaSayilanTutar = 700 });
        Assert.Contains("tutarlı", tutarli); Assert.Contains("Bekleyen eski düşüm yok", tutarli); Assert.DoesNotContain("en az", tutarli);
        var kredi = TakipMetni.Gecis(new TakipGecisDto("Kredi", 2, new(2026, 10, 1), 0, 0, 0, new[] { "Geçmiş korunur" }, true));
        Assert.DoesNotContain("Sistem kart borcu", kredi); Assert.Contains("Geçmiş korunur", kredi);
    }
    [Fact] public async Task Kart_ayrintisi_ilk_surum_gecis_uyarisini_kaydi_ve_liste_rozetini_gosterir()
    {
        const string uyari = "İlk sürüm kuralıyla geçiş (01.09.2026): 500,00 TL kasadan hiçbir zaman düşmüyor. Tutarları banka/kasa kayıtlarıyla doğrulayın.";
        var ilk = new KartGecisDto("EtkiTarihi", "Banka ile kontrol", null, 1400, new(2026, 9, 30), new(2026, 10, 31), 500, uyari);
        var api = new Fake { Kart = Fake.OrnekKart() with { Gecis = ilk } }; var vm = await KartVm(api);
        Assert.Equal(uyari, vm.GecisUyarisi); Assert.True(vm.GecisUyarisiTehlikeli);
        Assert.Contains("ilk sürüm", vm.GecisKaydi); Assert.Contains("önizleme özeti saklanmadı", vm.GecisKaydi); Assert.Contains("Geçiş açıklaması: Banka ile kontrol", vm.GecisKaydi);
        Assert.Contains("geçiş farkını doğrulayın", vm.Kartlar[0].Baslik);

        // Yalnız düşüş tarihi farklı: uyarı bilgi düzeyinde kalır, rozet çıkmaz.
        api.Kart = Fake.OrnekKart() with { Gecis = ilk with { TahminiKasaFarki = 0, Uyari = "İlk sürüm kuralıyla geçiş: toplam kasa etkisi tutarlı." } }; await vm.YukleAsync();
        Assert.NotNull(vm.GecisUyarisi); Assert.False(vm.GecisUyarisiTehlikeli); Assert.DoesNotContain("doğrulayın", vm.Kartlar[0].Baslik);

        // Uyarı metni gelmese de (eski sunucu) rapor dışı tutar görünür kalır.
        api.Kart = Fake.OrnekKart() with { Gecis = ilk with { Uyari = null } }; await vm.YukleAsync();
        Assert.Contains("1.400,00 ₺", vm.GecisUyarisi); Assert.Contains("500,00 ₺", vm.GecisUyarisi); Assert.True(vm.GecisUyarisiTehlikeli);

        var kayit = new KartGecisKaydi(new(2026, 9, 27), 1000, 800, 800, 300, 200, new(2026, 10, 31), 800);
        api.Kart = Fake.OrnekKart() with { Gecis = new KartGecisDto("IslemTarihi", "Ekstre kontrol edildi", kayit) }; await vm.YukleAsync();
        Assert.Null(vm.GecisUyarisi); Assert.False(vm.GecisUyarisiTehlikeli);
        foreach (var parca in new[] { "Girilen kalan borç 1.000,00 ₺ · sistem kart borcu 800,00 ₺", "Kasada önceden sayılan 800,00 ₺ · önerilen 800,00 ₺",
            "Bekleyen eski düşüm 200,00 ₺ · son düşüm 31.10.2026", "27.09.2026 tarihinde onaylandı", "300,00 ₺", "Geçiş açıklaması: Ekstre kontrol edildi" })
            Assert.Contains(parca, vm.GecisKaydi);
        Assert.DoesNotContain("saklanmadı", vm.GecisKaydi);

        api.Kart = Fake.OrnekKart(); await vm.YukleAsync();
        Assert.Null(vm.GecisKaydi); Assert.Null(vm.GecisUyarisi);
    }
    [Fact] public async Task Geciken_odeme_yaniti_oturum_degistiginde_eski_karti_geri_getirmez()
    {
        var bekleyen = new TaskCompletionSource<KartTakipDto>(); var api = new Fake { OdemeYaniti = bekleyen.Task }; var auth = Auth(); var vm = await KartVm(api, auth);
        vm.OdemeTutari = 10; await vm.OdemeOnizleCommand.ExecuteAsync(null); var islem = vm.OdemeKaydetCommand.ExecuteAsync(null);
        auth.OturumSurumu++; bekleyen.SetResult(Fake.OrnekKart()); await islem;
        Assert.Empty(vm.Kartlar); Assert.Null(vm.Secili); Assert.False(vm.VeriHazir); Assert.Null(vm.Mesaj);
    }
    [Fact] public async Task Geciken_liste_oturum_degistiginde_yansitilmaz()
    {
        var bekleyen = new TaskCompletionSource<IReadOnlyList<KartTakipDto>>(); var api = new Fake { KartlarYaniti = bekleyen.Task }; var auth = Auth(); var vm = new KartTakipViewModel(api, Finans(), auth);
        var islem = vm.YukleAsync(); auth.OturumSurumu++; bekleyen.SetResult(new[] { Fake.OrnekKart() }); await islem;
        Assert.Empty(vm.Kartlar); Assert.False(vm.VeriHazir);
    }
    [Fact] public async Task Izleyici_mutasyon_gonderemez_alici_menuye_erismez()
    {
        var api = new Fake(); var vm = await KartVm(api, new(new SahteApi()) { AktifRol = Rol.Izleyici });
        vm.OdemeTutari = 10; await vm.OdemeOnizleCommand.ExecuteAsync(null); await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.OnizlenenOdeme); Assert.Empty(api.OdemeIstekleri); Assert.Equal(new[] { Bolum.Alislar }, SekmeModeli.Bolumler(Rol.Alici));
        Assert.DoesNotContain(Bolum.Bildirimler, SekmeModeli.Bolumler(Rol.Izleyici));
    }
    [Fact] public async Task Mevcut_kredi_yeni_giris_olmadan_sabit_kanallarla_gonderilir()
    {
        var api = new Fake(); var vm = await KrediVm(api); vm.YeniCommand.Execute(null);
        vm.Ad = "Banka"; vm.CekilenTutar = 100; vm.AylikOdeme = 12; vm.TaksitSayisi = 10; vm.MevcutKredi = true; vm.Kanallar[0].Secili = vm.Kanallar[1].Secili = true;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.True(api.KrediKayit!.MevcutKredi); Assert.Equal(new[] { 1, 2 }, api.KrediKayit.KanalIdleri); Assert.NotEqual(Guid.Empty, api.KrediKayit.IstekId);
        Assert.Contains("ikinci kredi girişi", vm.Mesaj);
    }
    [Fact] public async Task Kasaya_islenmis_taksitte_tutar_degisemez_not_eklenebilir()
    {
        var api = new Fake(); var vm = await KrediVm(api); vm.TaksitSecCommand.Execute(vm.Taksitler[0]); vm.Gerekce = "Dekont eklendi";
        Assert.False(vm.TaksitDuzenlenebilir); vm.TaksitTutari = 99; await vm.TaksitKaydetCommand.ExecuteAsync(null); Assert.Null(api.Taksit);
        vm.TaksitTutari = 10; vm.TaksitNotu = "Bankadan kontrol edildi"; await vm.TaksitKaydetCommand.ExecuteAsync(null);
        Assert.Equal(10, api.Taksit!.Tutar); Assert.Equal("Bankadan kontrol edildi", api.Taksit.Not);
    }
    [Fact] public async Task Kredi_gecisinde_kanal_degistirmek_yeniden_onizleme_gerektirir()
    {
        var api = new Fake { Kredi = Fake.OrnekKredi() with { YeniTakip = false } }; var vm = await KrediVm(api); vm.GecisAciklama = "Kontrol edildi";
        await vm.GecisOnizleCommand.ExecuteAsync(null); vm.GecisOnay = true; vm.Kanallar[1].Secili = true;
        await vm.GecisiOnaylaCommand.ExecuteAsync(null); Assert.Null(api.KrediGecis);
        await vm.GecisOnizleCommand.ExecuteAsync(null); vm.GecisOnay = true; await vm.GecisiOnaylaCommand.ExecuteAsync(null);
        Assert.True(api.KrediGecis!.Onay); Assert.Equal(new[] { 1, 2 }, api.KrediGecis.KanalIdleri);
    }
    [Fact] public async Task Erken_kapama_tutar_tarih_degisince_onay_iptal_edilir()
    {
        var api = new Fake(); var vm = await KrediVm(api); vm.KapatmaTutari = 100; vm.Gerekce = "Banka kapama"; vm.KapatmaOnay = true; vm.KapatmaTutari = 90;
        Assert.False(vm.KapatmaOnay); await vm.KapatCommand.ExecuteAsync(null); Assert.Null(api.Kapatma);
        vm.KapatmaOnay = true; vm.KapatmaTarihi = vm.KapatmaTarihi.AddDays(1); Assert.False(vm.KapatmaOnay);
        vm.KapatmaOnay = true; await vm.KapatCommand.ExecuteAsync(null); Assert.Equal(90, api.Kapatma!.Tutar);
    }
    [Fact] public void Dagilimda_yinelenen_kanal_ve_kurus_alti_tutar_acik_hatadir()
    {
        var kanal = Finans().KanallarListe[0];
        var pay = new TakipPayEditor(new[] { kanal }) { Kanal = kanal, Tutar = 0.001m };
        Assert.Contains("kuruş", Assert.Throws<KasaApiException>(() => TakipMetni.Paylar(new[] { pay })).Message);
        pay.Tutar = 10; Assert.Contains("iki kez", Assert.Throws<KasaApiException>(() => TakipMetni.Paylar(new[] { pay, pay })).Message);
    }
    [Fact] public async Task Kart_iadesi_kaynak_harcama_gerektirir_kanal_dagilimi_kaynakta_kalir()
    {
        var api = new Fake { Kart = Fake.OrnekKart() with { Harcamalar = new[]
        {
            new KartHarcamaDto(10, null, Tarih, "MEZAT malı", 100, 1, false, new[] { new TakipKanalPayi(1, "MEZAT", 100) }),
            new KartHarcamaDto(11, null, Tarih, "PERAKENDE malı", 50, 1, false, new[] { new TakipKanalPayi(2, "PERAKENDE", 50) }),
            new KartHarcamaDto(12, null, Tarih, "Eski iptal", 10, 1, true, Array.Empty<TakipKanalPayi>())
        } } };
        var vm = await KartVm(api); vm.HarcamaTutari = -10; vm.HarcamaAciklama = "İade";
        await vm.HarcamaKaydetCommand.ExecuteAsync(null); Assert.Null(api.Harcama); Assert.Contains("harcamayı seçin", vm.Hata); Assert.Equal(2, vm.IadeKaynaklari.Count);
        vm.IadeKaynagi = vm.IadeKaynaklari.Single(x => x.Veri.Id == 11);
        vm.PayEkle(vm.HarcamaPaylari); vm.HarcamaPaylari[0].Kanal = vm.Kanallar[0]; vm.HarcamaPaylari[0].Tutar = 10;
        await vm.HarcamaKaydetCommand.ExecuteAsync(null);
        Assert.Equal(11, api.Harcama!.KaynakHarcamaId); Assert.Empty(api.Harcama.Dagilimlar); Assert.Equal(-10, api.Harcama.Tutar);
    }
    [Fact] public void Geciken_kart_tarihi_sunucu_rapor_gunune_gore_acikca_gosterilir()
    {
        var olay = new TakipOlayDto("Kart", 1, 3, "Kart", Tarih.AddDays(-1), 10, "SonOdeme", false);
        Assert.Contains("tarihi geçti", new TakipOlaySatiri(olay, Tarih).Ozet);
        Assert.DoesNotContain("tarihi geçti", new TakipOlaySatiri(olay, Tarih.AddDays(-2)).Ozet);
        Assert.Contains("otomatik", new TakipOlaySatiri(olay with { Kaynak = "Kredi", Tur = "Taksit", OtomatikKasa = true }, Tarih).Ozet);
    }
    [Fact] public async Task Kart_odeme_benzerligi_onaydan_sonra_ayni_anahtarla_tekrarlanabilir()
    {
        var api = new Fake { OdemeHata = true }; var lookup = new BenzerKayitTests.Fake(); var vm = await KartVm(api, benzerlik: lookup);
        vm.OdemeTutari = 10; await vm.OdemeOnizleCommand.ExecuteAsync(null); await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.OdemeIstekleri); Assert.Equal("KartOdeme", lookup.SonArama!.Tur); Assert.Equal(1, lookup.SonArama.KrediKartiId);
        await vm.OdemeyiAyriKaydetCommand.ExecuteAsync(null); Assert.Single(api.OdemeIstekleri);
        api.OdemeHata = false; await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, api.OdemeIstekleri.Count); Assert.Equal(api.OdemeIstekleri[0].IstekId, api.OdemeIstekleri[1].IstekId); Assert.Equal(1, lookup.Cagri);
    }
    [Fact] public async Task Kart_harcama_uyarisi_degisik_tutar_icin_yeni_onay_ister()
    {
        var api = new Fake(); var lookup = new BenzerKayitTests.Fake(); var vm = await KartVm(api, benzerlik: lookup);
        vm.HarcamaTutari = 10; vm.HarcamaAciklama = "Mal"; await vm.HarcamaKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.Harcama); Assert.Equal("KartHarcama", lookup.SonArama!.Tur);
        vm.HarcamaTutari = 20; await vm.HarcamayiAyriKaydetCommand.ExecuteAsync(null); Assert.Null(api.Harcama); Assert.True(vm.HarcamaBenzerlik.UyariVar);
        await vm.HarcamayiAyriKaydetCommand.ExecuteAsync(null); Assert.Equal(20, api.Harcama!.Tutar); Assert.Equal(2, lookup.Cagri);
    }
    [Fact] public async Task Kart_detayi_kanal_borcunu_ve_belirsiz_payi_gosterir_alici_karta_gecemez()
    {
        var api = new Fake { Kart = Fake.OrnekKart() with { KanalKartBorclari = new[] { new TakipKanalPayi(1, "MEZAT", 70), new TakipKanalPayi(null, "", 30) } } };
        var auth = Auth(); var vm = await KartVm(api, auth); Assert.Contains("70,00", vm.KanalBorcOzeti); Assert.Contains("Dağılım bekliyor", vm.KanalBorcOzeti);
        Assert.True(vm.IdIleSec(1)); auth.AktifRol = Rol.Alici; Assert.False(vm.IdIleSec(1));
    }
    [Fact] public async Task Ozet_farkli_kart_alacagini_borctan_dusmez_belirsiz_payi_ayirir()
    {
        var api = new Fake { Ozet = new(Tarih, 100, 20, Array.Empty<TakipOlayDto>(), new[] { new TakipKanalPayi(1, "MEZAT", 70), new TakipKanalPayi(null, "", 30) }, 40) };
        var vm = new TakipOzetViewModel(api, Auth()); await vm.YukleAsync();
        Assert.Contains("Toplam kart borcu 100,00", vm.Ozet); Assert.Contains("alacak bakiyesi: 40,00", vm.Ozet); Assert.Contains("30,00", vm.BelirsizBorcOzeti); Assert.Equal(100, vm.KanalKartBorclari!.Sum(k => k.Tutar));
    }

    internal sealed class Fake : IFinansTakipApi
    {
        public static KartTakipDto OrnekKart() => new(1, 3, "Kart", true, true, Tarih, 1, 10, 1000, 100, 100, new[] { new KartEkstreDto(7, Tarih, Tarih.AddDays(10), 100, 0, 100, null) }, Array.Empty<KartHarcamaDto>(), Array.Empty<KartTakipOdemeDto>());
        public static KrediTakipDto OrnekKredi() => new(2, 3, "Kredi", true, true, Tarih, 100, Tarih, 20, new[] { new TakipKanalPayi(1, "MEZAT", 100) }, new[] { new KrediPlanTaksitDto(3, 1, Tarih, 10, "KasayaIslendi", null, new[] { new TakipKanalPayi(1, "MEZAT", 10) }) });
        public KartTakipDto Kart = OrnekKart(); public KrediTakipDto Kredi = OrnekKredi();
        public TakipOzetDto? Ozet;
        public Task<IReadOnlyList<KartTakipDto>>? KartlarYaniti; public Task<KartTakipDto>? OdemeYaniti;
        public bool OdemeHata; public Exception? OdemeHatasi; public KartTakipOdemeYaz? OnizlenenOdeme; public List<KartTakipOdemeYaz> OdemeIstekleri = new();
        public KartGecisYaz? KartGecis; public KrediGecisYaz? KrediGecis; public KrediTakipYaz? KrediKayit; public KrediTaksitYaz? Taksit; public KrediKapatYaz? Kapatma; public KartHarcamaYaz? Harcama;
        public int KartKayitSayisi, EkstreKayitSayisi, KartGecisOnizlemeSayisi;
        public Task<IReadOnlyList<KartTakipDto>> TakipKartlarAsync() => KartlarYaniti ?? Task.FromResult<IReadOnlyList<KartTakipDto>>(new[] { Kart });
        public Task<KartTakipDto> TakipKartAsync(int id) => Task.FromResult(Kart);
        public Task<KartTakipDto> TakipKartKaydetAsync(int? id, KartTakipYaz g) { KartKayitSayisi++; return Task.FromResult(Kart); }
        public Task<KartTakipDto> TakipKartDurumAsync(int id, TakipDurumYaz g) => Task.FromResult(Kart);
        public Task<KartTakipDto> TakipHarcamaKaydetAsync(int id, KartHarcamaYaz g) { Harcama = g; return Task.FromResult(Kart); }
        public Task<KartTakipDto> TakipHarcamaIptalAsync(int id, int hid, TakipIptalYaz g) => Task.FromResult(Kart);
        public Task<KartTakipDto> TakipEkstreKaydetAsync(int id, int eid, KartEkstreYaz g) { EkstreKayitSayisi++; return Task.FromResult(Kart); }
        public Task<KartOdemeOnizlemeDto> TakipOdemeOnizlemeAsync(int id, KartTakipOdemeYaz g) { OnizlenenOdeme = g; return Task.FromResult(new KartOdemeOnizlemeDto(g.Tutar, g.Tutar, new[] { new TakipKanalPayi(1, "MEZAT", g.Tutar) }, new[] { new KartEkstreOdemePayi(7, g.Tutar) })); }
        public Task<KartTakipDto> TakipOdemeKaydetAsync(int id, KartTakipOdemeYaz g) { OdemeIstekleri.Add(g); return OdemeHatasi is { } hata ? Task.FromException<KartTakipDto>(hata) : OdemeHata ? Task.FromException<KartTakipDto>(new HttpRequestException()) : OdemeYaniti ?? Task.FromResult(Kart); }
        public Task<KartTakipDto> TakipOdemeIptalAsync(int id, int oid, TakipIptalYaz g) => Task.FromResult(Kart);
        private static TakipGecisDto Preview(string kaynak, int id) => new(kaynak, id, Tarih, 0, 0, 30, new[] { "Geçmiş korunur" }, true);
        /// <summary>Sunucu CardPreview'ın sade aynası: önerilen = max(0, min(kalan borç, sistem borcu)), en az = önerilen − açılış borcu.</summary>
        public static Func<KartGecisYaz, TakipGecisDto> SunucuGibi(decimal sistem, decimal acilis = 0) => g =>
        {
            var oneri = Math.Max(0, Math.Min(g.KalanBorc, sistem)); var enAz = Math.Max(0, oneri - Math.Max(0, acilis)); var fark = g.KasadaOncedenSayilanTutar - oneri;
            return new("Kart", 1, g.Baslangic, fark, 0, g.KasadaOncedenSayilanTutar, new[] { "Sunucu açıklaması" }, fark <= 0 && g.KasadaOncedenSayilanTutar >= enAz,
                sistem, 300, 200, new DateOnly(2026, 10, 31), oneri, enAz);
        };
        public Func<KartGecisYaz, TakipGecisDto>? KartGecisYaniti; public List<KartGecisYaz> KartGecisOnizlemeleri = new();
        public Task<TakipGecisDto> TakipKartGecisOnizlemeAsync(int id, KartGecisYaz g) { KartGecisOnizlemeSayisi++; KartGecisOnizlemeleri.Add(g); return Task.FromResult(KartGecisYaniti?.Invoke(g) ?? Preview("Kart", id)); }
        public Task<KartTakipDto> TakipKartGecisAsync(int id, KartGecisYaz g) { KartGecis = g; return Task.FromResult(Kart with { YeniTakip = true }); }
        public Task<IReadOnlyList<KrediTakipDto>> TakipKredilerAsync() => Task.FromResult<IReadOnlyList<KrediTakipDto>>(new[] { Kredi });
        public Task<KrediTakipDto> TakipKrediAsync(int id) => Task.FromResult(Kredi);
        public Task<KrediTakipDto> TakipKrediKaydetAsync(KrediTakipYaz g) { KrediKayit = g; return Task.FromResult(Kredi); }
        public Task<KrediTakipDto> TakipKrediDurumAsync(int id, TakipDurumYaz g) => Task.FromResult(Kredi);
        public Task<KrediTakipDto> TakipTaksitKaydetAsync(int id, int tid, KrediTaksitYaz g) { Taksit = g; return Task.FromResult(Kredi); }
        public Task<KrediTakipDto> TakipKrediKapatAsync(int id, KrediKapatYaz g) { Kapatma = g; return Task.FromResult(Kredi); }
        public Task<TakipGecisDto> TakipKrediGecisOnizlemeAsync(int id, KrediGecisYaz g) => Task.FromResult(Preview("Kredi", id));
        public Task<KrediTakipDto> TakipKrediGecisAsync(int id, KrediGecisYaz g) { KrediGecis = g; return Task.FromResult(Kredi with { YeniTakip = true }); }
        public Task<TakipOzetDto> TakipOzetAsync(int gun = 30) => Task.FromResult(Ozet ?? new TakipOzetDto(Tarih, 100, 20, Array.Empty<TakipOlayDto>()));
    }
}
