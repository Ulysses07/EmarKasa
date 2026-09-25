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
    [Fact] public async Task Kart_gecis_paylari_degistiginde_eski_onay_gonderilmez()
    {
        var api = new Fake { Kart = Fake.OrnekKart() with { YeniTakip = false } }; var vm = await KartVm(api);
        vm.GecisAciklama = "Eski borç kontrol edildi"; vm.PayEkle(vm.GecisPaylari); vm.GecisPaylari[0].Kanal = vm.Kanallar[0]; vm.GecisPaylari[0].Tutar = 100;
        await vm.GecisOnizleCommand.ExecuteAsync(null); vm.GecisOnay = true; vm.GecisPaylari[0].Tutar = 90;
        await vm.GecisiOnaylaCommand.ExecuteAsync(null); Assert.Null(api.KartGecis); Assert.Contains("önizlemesini", vm.Hata);
        await vm.GecisOnizleCommand.ExecuteAsync(null); vm.GecisOnay = true; await vm.GecisiOnaylaCommand.ExecuteAsync(null);
        Assert.True(api.KartGecis!.Onay); Assert.NotEqual(Guid.Empty, api.KartGecis.IstekId); Assert.Equal(90, api.KartGecis.Dagilimlar.Single().Tutar);
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
        public bool OdemeHata; public KartTakipOdemeYaz? OnizlenenOdeme; public List<KartTakipOdemeYaz> OdemeIstekleri = new();
        public KartGecisYaz? KartGecis; public KrediGecisYaz? KrediGecis; public KrediTakipYaz? KrediKayit; public KrediTaksitYaz? Taksit; public KrediKapatYaz? Kapatma; public KartHarcamaYaz? Harcama;
        public Task<IReadOnlyList<KartTakipDto>> TakipKartlarAsync() => KartlarYaniti ?? Task.FromResult<IReadOnlyList<KartTakipDto>>(new[] { Kart });
        public Task<KartTakipDto> TakipKartAsync(int id) => Task.FromResult(Kart);
        public Task<KartTakipDto> TakipKartKaydetAsync(int? id, KartTakipYaz g) => Task.FromResult(Kart);
        public Task<KartTakipDto> TakipKartDurumAsync(int id, TakipDurumYaz g) => Task.FromResult(Kart);
        public Task<KartTakipDto> TakipHarcamaKaydetAsync(int id, KartHarcamaYaz g) { Harcama = g; return Task.FromResult(Kart); }
        public Task<KartTakipDto> TakipHarcamaIptalAsync(int id, int hid, TakipIptalYaz g) => Task.FromResult(Kart);
        public Task<KartTakipDto> TakipEkstreKaydetAsync(int id, int eid, KartEkstreYaz g) => Task.FromResult(Kart);
        public Task<KartOdemeOnizlemeDto> TakipOdemeOnizlemeAsync(int id, KartTakipOdemeYaz g) { OnizlenenOdeme = g; return Task.FromResult(new KartOdemeOnizlemeDto(g.Tutar, g.Tutar, new[] { new TakipKanalPayi(1, "MEZAT", g.Tutar) }, new[] { new KartEkstreOdemePayi(7, g.Tutar) })); }
        public Task<KartTakipDto> TakipOdemeKaydetAsync(int id, KartTakipOdemeYaz g) { OdemeIstekleri.Add(g); return OdemeHata ? Task.FromException<KartTakipDto>(new HttpRequestException()) : OdemeYaniti ?? Task.FromResult(Kart); }
        public Task<KartTakipDto> TakipOdemeIptalAsync(int id, int oid, TakipIptalYaz g) => Task.FromResult(Kart);
        private static TakipGecisDto Preview(string kaynak, int id) => new(kaynak, id, Tarih, 0, 0, 30, new[] { "Geçmiş korunur" }, true);
        public Task<TakipGecisDto> TakipKartGecisOnizlemeAsync(int id, KartGecisYaz g) => Task.FromResult(Preview("Kart", id));
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
