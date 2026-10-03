using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>ParaGirisi geçersiz metni ParaAyristirici.Gecersiz olarak yazar. Hiçbir VM bu değeri 0 ya da
/// sayı olarak göndermemeli; API çağrılmadan ortak mesaj gösterilmeli.</summary>
public class GecersizTutarTests
{
    private const decimal G = ParaAyristirici.Gecersiz;
    private static AuthViewModel Editor() => new(new SahteApi()) { AktifRol = Rol.Editor };
    private static SahteApi Finans() => new() { KanallarListe = new[] { new KanalDto(1, "MEZAT", true, 0, 0), new KanalDto(2, "PERAKENDE", true, 1, 0) } };

    [Fact]
    public async Task Islem_gider_tutari_gecersizken_kaydedilmez()
    {
        var api = new SahteApi();
        var vm = new IslemlerViewModel(api, TestOturumu.Ac()) { DuzenTarih = new(2026, 9, 21), DuzenCari = "Kira", DuzenTutar = G, DuzenKanal = "MEZAT" };
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.IslemOlusturCagri);
        Assert.Null(api.SonIslemOlustur);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hatalar[nameof(IslemlerViewModel.DuzenTutar)]);
    }

    [Fact]
    public async Task Ayarlar_acilis_devirleri_gecersizken_kaydedilmez()
    {
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 0m, false) };
        var vm = new AyarlarViewModel(api, TestOturumu.Ac()) { KasaAcilisDevri = G };
        await vm.AyarKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonAyar);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);

        vm.DuzenKanalAd = "TOPTAN";
        vm.DuzenKanalAcilisDevri = G;
        await vm.KanalKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonKanalOlustur);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.KanalHatalari[nameof(vm.DuzenKanalAcilisDevri)]);
    }

    [Fact]
    public async Task Alis_kalem_ve_pay_tutari_gecersizken_kaydedilmez_ozetler_sayi_gostermez()
    {
        var api = new AlislarViewModelTests.SahteAlisApi();
        var vm = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac(Rol.Alici));
        await vm.YukleAsync();
        vm.Tedarikci = "Firma";
        vm.Kalemler[0].Aciklama = "Mal";
        vm.Kalemler[0].Tutar = G;
        Assert.Equal(ParaAyristirici.GecersizGosterim, vm.DagilimOzeti);
        Assert.Equal(ParaAyristirici.GecersizGosterim, vm.OdemeOzeti);
        Assert.Equal(ParaAyristirici.GecersizGosterim, vm.ToplamMetni);
        Assert.Equal(ParaAyristirici.GecersizGosterim, vm.Kalemler[0].DagilimOzeti);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonYaz);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hatalar.Genel);

        vm.Kalemler[0].Tutar = 100m;
        vm.Kalemler[0].Dagilimlar.Add(new(vm.Kanallar.ToList()) { Kanal = vm.Kanallar[0], Tutar = G });
        Assert.Equal(ParaAyristirici.GecersizGosterim, vm.Kalemler[0].DagilimOzeti);
        Assert.Equal(ParaAyristirici.GecersizGosterim, vm.DagilimOzeti);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonYaz);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hatalar.Genel);
    }

    [Fact]
    public async Task Alis_odeme_tutari_gecersizken_gonderilmez()
    {
        var api = new AlislarViewModelTests.SahteAlisApi
        {
            Liste = new[] { new AlisDto(7, 2, 3, "Ayşe", new(2026, 9, 21), "Tedarikçi", null, "Taslak", null, 100m, 0m, 100m,
            new[] { new AlisKalemDto(1, "Mal alımı", 100m, new[] { new AlisDagilimDto(1, "MEZAT", 100m) }) }, Array.Empty<AlisOdemeDto>()) }
        };
        var vm = new AlislarViewModel(api, new SahteApi(), TestOturumu.Ac());
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Alislar[0]);
        vm.OdemeTutari = G;
        Assert.Empty(vm.OdemeOnizleme);
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonOdeme);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);
    }

    private static async Task<KartTakipViewModel> KartVm(FinansTakipTests.Sahte api, IKasaKontrolApi? kontrol = null)
    {
        var vm = new KartTakipViewModel(api, Finans(), Editor(), null, kontrol);
        await vm.YukleAsync();
        vm.Sec(vm.Kartlar[0]);
        return vm;
    }

    [Fact]
    public async Task Kart_limit_ve_acilis_borcu_gecersizken_kart_kaydedilmez()
    {
        var api = new FinansTakipTests.Sahte();
        var vm = await KartVm(api);
        vm.Yeni();
        vm.Ad = "Yeni kart";
        vm.Limit = G;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.KartKayitSayisi);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.KartHatalari[nameof(vm.Limit)]);
        vm.Limit = 1000;
        vm.AcilisBorc = G;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.KartKayitSayisi);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.KartHatalari[nameof(vm.AcilisBorc)]);
    }

    [Fact]
    public async Task Kart_harcama_tutari_gecersizken_iade_arayuzu_acilmaz_ve_kaydedilmez()
    {
        var api = new FinansTakipTests.Sahte();
        var vm = await KartVm(api);
        vm.HarcamaAciklama = "Market";
        vm.HarcamaTutari = G;
        Assert.False(vm.IadeGirisi);
        Assert.True(vm.HarcamaGirisi);
        await vm.HarcamaKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.Harcama);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);
        vm.HarcamaTutari = -5;
        Assert.True(vm.IadeGirisi);
    }

    [Fact]
    public async Task Kart_odeme_ekstre_gecis_ve_masraf_tutari_gecersizken_api_cagrilmaz()
    {
        var api = new FinansTakipTests.Sahte();
        var kontrol = new KasaKontrolVeAylikGiderTests.Sahte();
        var vm = await KartVm(api, kontrol);
        vm.OdemeTutari = G;
        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        Assert.Null(api.OnizlenenOdeme);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.OdemeHatalari[nameof(vm.OdemeTutari)]);

        vm.EkstreSecCommand.Execute(vm.Ekstreler[0]);
        vm.AsgariVar = true;
        vm.AsgariTutar = G;
        vm.Gerekce = "Banka ekstresi";
        await vm.EkstreKaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.EkstreKayitSayisi);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);

        vm.MasrafEkstresi = vm.Ekstreler[0];
        vm.MasrafTutari = G;
        vm.MasrafAciklama = "Faiz";
        await vm.MasrafOnizleCommand.ExecuteAsync(null);
        Assert.Equal(0, kontrol.MasrafOnizlemeSayisi);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);

        var eski = new FinansTakipTests.Sahte { Kart = FinansTakipTests.Sahte.OrnekKart() with { YeniTakip = false } };
        var gecis = await KartVm(eski);
        gecis.GecisAciklama = "Geçiş";
        gecis.GecisKalanBorc = G;
        await gecis.GecisOnizleCommand.ExecuteAsync(null);
        Assert.Equal(0, eski.KartGecisOnizlemeSayisi);
        Assert.Equal(ParaAyristirici.GecersizMesaji, gecis.Hata);
        gecis.GecisKalanBorc = 100;
        gecis.PayEkle(gecis.GecisPaylari);
        gecis.GecisPaylari[0].Kanal = gecis.Kanallar[0];
        gecis.GecisPaylari[0].Tutar = G;
        await gecis.GecisOnizleCommand.ExecuteAsync(null);
        Assert.Equal(0, eski.KartGecisOnizlemeSayisi);
        Assert.Equal(ParaAyristirici.GecersizMesaji, gecis.Hata);
    }

    [Fact]
    public async Task Kredi_tutarlari_gecersizken_kaydedilmez_kapama_ozeti_sayi_gostermez()
    {
        var api = new FinansTakipTests.Sahte();
        var vm = new KrediTakipViewModel(api, Finans(), Editor());
        await vm.YukleAsync();
        vm.Ad = "Kredi";
        vm.CekilenTutar = 1000;
        vm.AylikOdeme = G;
        vm.TumKanallariSecCommand.Execute(null);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(api.KrediKayit);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hatalar[nameof(vm.AylikOdeme)]);

        vm.BirakmaOnayi = _ => Task.FromResult(true);   // yazılmış yeni kredi formu bırakılır
        vm.SecCommand.Execute(vm.Krediler[0]);
        vm.Gerekce = "Banka yazısı";
        vm.KapatmaTutari = G;
        vm.KapatmaOnay = true;
        Assert.Contains(ParaAyristirici.GecersizGosterim, vm.KapatmaOzeti);
        await vm.KapatCommand.ExecuteAsync(null);
        Assert.Null(api.Kapatma);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);

        vm.TaksitSecCommand.Execute(vm.Taksitler[0]);
        vm.Gerekce = "Banka yazısı";
        vm.TaksitTutari = G;
        await vm.TaksitKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.Taksit);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.Hata);
    }

    [Fact]
    public async Task Kasa_kontrolu_ve_alt_limit_gecersizken_api_cagrilmaz()
    {
        var f = new KasaKontrolVeAylikGiderTests.Sahte();
        var kontrol = new KasaKontrolViewModel(f, Editor());
        await kontrol.YukleAsync();
        kontrol.GercekBakiye = G;
        await kontrol.OnizleCommand.ExecuteAsync(null);
        Assert.Equal(0, f.KontrolOnizlemeSayisi);
        Assert.Equal(ParaAyristirici.GecersizMesaji, kontrol.Hata);

        var esik = new KasaEsikViewModel(f, Editor());
        await esik.YukleAsync();
        esik.Secili = esik.Kanallar[0];
        esik.Tutar = G;
        await esik.KaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Esik);
        Assert.Equal(ParaAyristirici.GecersizMesaji, esik.Hata);
    }

    [Fact]
    public async Task Aylik_gider_tutari_ve_paylari_gecersizken_sablon_kaydedilmez()
    {
        var f = new KasaKontrolVeAylikGiderTests.Sahte();
        var vm = new AylikGiderViewModel(f, Finans(), Editor());
        await vm.YukleAsync();
        vm.Ad = "Kira";
        vm.Tur = vm.Turler[0];
        vm.DagilimTuru = vm.DagilimTurleri[0];
        vm.Tutar = G;
        await vm.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Sablon);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.SablonHatalari[nameof(vm.Tutar)]);

        vm.Tutar = 100;
        vm.DagilimTuru = vm.DagilimTurleri[2];
        vm.PayEkle();
        vm.Paylar[0].Kanal = vm.Kanallar[0];
        vm.Paylar[0].Tutar = G;
        await vm.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Sablon);
        Assert.Equal(ParaAyristirici.GecersizMesaji, vm.SablonHatalari.Genel);
    }

    [Fact]
    public void Takip_paylari_gecersiz_tutari_sayi_olarak_gondermez()
    {
        var pay = new TakipPayEditor(new[] { new KanalDto(1, "MEZAT", true, 0, 0) }) { Tutar = G };
        pay.Kanal = pay.Kanallar[0];
        var hata = Assert.Throws<DogrulamaHatasi>(() => TakipMetni.Paylar(new[] { pay }));
        Assert.Equal(ParaAyristirici.GecersizMesaji, hata.Message);
    }
}
