using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>POS sayfası (Paket F, madde 44): tanımlar, satışlar, önizleme ve özet.</summary>
public class PosViewModelTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    private static PosTanimDto Tanim(int id, string ad, decimal oran = 1.79m, int blokaj = 1, bool aktif = true, int? kanalId = 1)
        => new(id, ad, PosSaglayici.BankaPosu, kanalId, kanalId is null ? null : "MEZAT", oran, blokaj, aktif);

    private static PosSatisDto Satis(int id, int posId, decimal brut) => new(id, new DateOnly(2026, 9, 20), posId, "Garanti", "MEZAT", brut,
        1.79m, PosOnizleme.Komisyon(brut, 1.79m), PosOnizleme.Net(brut, 1.79m), 1, new DateOnly(2026, 9, 21), false, null);

    private static (SahteApi api, PosViewModel vm) Kur(params PosTanimDto[] tanimlar)
    {
        var api = new SahteApi
        {
            KanallarListe = [new KanalDto(1, "MEZAT", true, 1, 0m), new KanalDto(2, "ESKİ", false, 2, 0m), new KanalDto(3, "TOPTAN", true, 3, 0m)],
        };
        api.PosTanimlariListe.AddRange(tanimlar);
        return (api, new PosViewModel(api, new SabitSaat(Bugun.AddHours(10))) { EditorMu = true });
    }

    [Fact]
    public async Task Yukle_ayin_satislarini_ozeti_ve_tanimlari_getirir()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti"));
        api.PosSatislariListe.Add(Satis(5, 1, 1000m));
        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), (int?)null), api.SonPosSatisFiltresi);
        Assert.Equal((2026, 9), api.SonPosOzet);
        Assert.Single(vm.Tanimlar);
        Assert.Single(vm.Satislar);
        Assert.False(vm.TanimYok);
        Assert.NotNull(vm.Ozet);
        Assert.True(vm.ValorYok);
        // Kanal çipleri: Kanalsız + aktif kanallar (pasif gizli)
        Assert.Equal([PosViewModel.KanalsizAdi, "MEZAT", "TOPTAN"], vm.KanalCipleri.Select(k => k.Ad));
        // Tek aktif POS varsa satış formunda seçili gelir.
        Assert.Equal(1, vm.SatisPosId);
        Assert.True(Assert.Single(vm.PosCipleri).Secili);
    }

    [Fact]
    public async Task Valor_takvimi_doluysa_bos_mesaj_gizlenir()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti"));
        api.PosOzet = new PosOzetDto(2026, 9, new DateOnly(2026, 9, 24), 982.10m, 1,
            [new PosValorGunuDto(new DateOnly(2026, 9, 25), 982.10m, 1)], [], 1000m, 17.90m, 982.10m);
        await vm.YukleAsync();

        Assert.False(vm.ValorYok);
        Assert.Equal(982.10m, vm.Ozet!.BlokeNet);
    }

    [Theory]
    [InlineData(1000, "1,79", "Komisyon 17,90 ₺ · Net 982,10 ₺")]
    [InlineData(1234.56, "2,5", "Komisyon 30,86 ₺ · Net 1.203,70 ₺")]
    [InlineData(100, "0", "Komisyon 0,00 ₺ · Net 100,00 ₺")]
    [InlineData(0.01, "50", "Komisyon 0,01 ₺ · Net 0,00 ₺")]
    public async Task Satis_onizlemesi_sunucu_kuraliyla_ayni(decimal brut, string oran, string beklenen)
    {
        var (_, vm) = Kur(Tanim(1, "Garanti", oran: 3m, blokaj: 2));
        await vm.YukleAsync();

        vm.SatisBrut = brut;
        vm.SatisOran = oran;

        Assert.StartsWith(beklenen, vm.SatisOnizleme);
        Assert.EndsWith("Valör 26 Eylül 2026", vm.SatisOnizleme);
    }

    [Fact]
    public async Task Satis_onizlemesi_bos_oran_ve_blokajda_posunkini_kullanir()
    {
        var (_, vm) = Kur(Tanim(1, "Garanti", oran: 1.79m, blokaj: 30));
        await vm.YukleAsync();
        vm.SatisBrut = 1000m;

        Assert.Equal("Komisyon 17,90 ₺ · Net 982,10 ₺ · Valör 24 Ekim 2026", vm.SatisOnizleme);

        vm.SatisBrut = 0m;
        Assert.Equal("", vm.SatisOnizleme);
    }

    /// <summary>Kasa.Core.PosHesapTests ile aynı örnekler: istemci önizlemesi sunucu kuralından sapmaz.</summary>
    [Theory]
    [InlineData("1000", "1.79", "17.90", "982.10")]
    [InlineData("100", "0", "0.00", "100.00")]
    [InlineData("0.50", "1", "0.01", "0.49")]
    [InlineData("0.49", "1", "0.00", "0.49")]
    [InlineData("333.33", "2.5", "8.33", "325.00")]
    [InlineData("1234.567", "1", "12.35", "1222.22")]
    public void Pos_onizleme_kurus_yuvarlamasi_sunucu_ile_ayni(string brut, string oran, string komisyon, string net)
    {
        static decimal D(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(D(komisyon), PosOnizleme.Komisyon(D(brut), D(oran)));
        Assert.Equal(D(net), PosOnizleme.Net(D(brut), D(oran)));
    }

    [Fact]
    public async Task Satis_kaydet_bos_oran_ve_blokaji_null_gonderir_sunucu_posunkini_kullanir()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti"));
        await vm.YukleAsync();
        vm.SatisBrut = 2500m;
        vm.SatisNot = "  Z raporu  ";

        await vm.SatisKaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal(new PosSatisYaz(new DateOnly(2026, 9, 24), 1, 2500m, null, null, "Z raporu"), api.SonPosSatisOlustur);
        Assert.Equal(0m, vm.SatisBrut);   // form boşaldı
    }

    [Fact]
    public async Task Satis_kaydet_girilen_oran_ve_blokaji_gonderir()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti"));
        await vm.YukleAsync();
        vm.SatisBrut = 2500m;
        vm.SatisOran = "%2,25";
        vm.SatisBlokaj = "7";

        await vm.SatisKaydetCommand.ExecuteAsync(null);

        Assert.Equal(2.25m, api.SonPosSatisOlustur!.KomisyonOrani);
        Assert.Equal(7, api.SonPosSatisOlustur!.BlokajGunu);
    }

    [Fact]
    public async Task Satis_kaydet_dogrulamalari()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti"), Tanim(2, "iyzico"));
        await vm.YukleAsync();
        Assert.Null(vm.SatisPosId);   // iki POS varken otomatik seçilmez

        await vm.SatisKaydetCommand.ExecuteAsync(null);
        Assert.Equal(PosViewModel.PosSecinMesaji, vm.Hata);

        vm.SecPosCommand.Execute(vm.PosCipleri[1]);
        Assert.Equal(2, vm.SatisPosId);
        Assert.True(vm.PosCipleri[1].Secili);
        await vm.SatisKaydetCommand.ExecuteAsync(null);
        Assert.Equal(PosViewModel.BrutMesaji, vm.Hata);

        vm.SatisBrut = 10m;
        vm.SatisOran = "101";
        await vm.SatisKaydetCommand.ExecuteAsync(null);
        Assert.Equal(OranGiris.HataAralik, vm.Hata);

        vm.SatisOran = null;
        vm.SatisBlokaj = "366";
        await vm.SatisKaydetCommand.ExecuteAsync(null);
        Assert.Equal(BlokajGiris.HataGecersiz, vm.Hata);
        Assert.Null(api.SonPosSatisOlustur);
    }

    [Fact]
    public async Task Satis_duzenle_ve_guncelle()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti"));
        api.PosSatislariListe.Add(Satis(5, 1, 1000m));
        await vm.YukleAsync();

        vm.SatisDuzenleCommand.Execute(vm.Satislar[0]);
        Assert.Equal("POS satışını düzenle", vm.SatisFormBasligi);
        Assert.Equal("1,79", vm.SatisOran);
        Assert.Equal("1", vm.SatisBlokaj);
        vm.SatisBrut = 1100m;
        await vm.SatisKaydetCommand.ExecuteAsync(null);

        Assert.Equal(5, api.SonPosSatisGuncelle!.Value.Id);
        Assert.Equal(1100m, api.SonPosSatisGuncelle!.Value.G.BrutTutar);
        Assert.Equal(1.79m, api.SonPosSatisGuncelle!.Value.G.KomisyonOrani);
        Assert.Equal(0, vm.SatisId);
    }

    /// <summary>Bulgu: yanlış POS'la girilmiş satış düzenlenip doğru POS seçilince eski POS'un oranı/blokajı gönderilmemeli.</summary>
    [Fact]
    public async Task Satis_duzenlenirken_pos_degisince_eski_posun_orani_ve_blokaji_gonderilmez()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti", oran: 1.79m, blokaj: 1), Tanim(2, "iyzico", oran: 3.49m, blokaj: 7));
        api.PosSatislariListe.Add(Satis(5, 1, 1000m));
        await vm.YukleAsync();

        vm.SatisDuzenleCommand.Execute(vm.Satislar[0]);
        vm.SecPosCommand.Execute(vm.PosCipleri.Single(p => p.Id == 2));

        Assert.Null(vm.SatisOran);     // form "POS'unki"ne döner
        Assert.Null(vm.SatisBlokaj);
        Assert.Equal("Komisyon 34,90 ₺ · Net 965,10 ₺ · Valör 27 Eylül 2026", vm.SatisOnizleme);   // yeni POS'un değerleri
        await vm.SatisKaydetCommand.ExecuteAsync(null);

        var g = api.SonPosSatisGuncelle!.Value;
        Assert.Equal((5, 2, (decimal?)null, (int?)null), (g.Id, g.G.PosId, g.G.KomisyonOrani, g.G.BlokajGunu));
    }

    [Fact]
    public async Task Satis_duzenlenirken_elle_girilen_oran_pos_degisince_korunur_kendi_posuna_donunce_kayitli_degerler_gelir()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti", oran: 1.79m, blokaj: 1), Tanim(2, "iyzico", oran: 3.49m, blokaj: 7));
        api.PosSatislariListe.Add(Satis(5, 1, 1000m));
        await vm.YukleAsync();

        // Elle girilen oran kullanıcınındır: POS değişse de silinmez; elle değişmeyen blokaj boşalır.
        vm.SatisDuzenleCommand.Execute(vm.Satislar[0]);
        vm.SatisOran = "2,5";
        vm.SecPosCommand.Execute(vm.PosCipleri.Single(p => p.Id == 2));
        Assert.Equal(("2,5", (string?)null), (vm.SatisOran, vm.SatisBlokaj));

        // Satışın kendi POS'una dönülünce boşalan alan kayıtlı değerle dolar.
        vm.SecPosCommand.Execute(vm.PosCipleri.Single(p => p.Id == 1));
        Assert.Equal(("2,5", "1"), (vm.SatisOran, vm.SatisBlokaj));

        // Elle hiçbir şey değişmeden gidip dönmek kayıtlı değerleri aynen geri getirir.
        vm.SatisDuzenleCommand.Execute(vm.Satislar[0]);
        vm.SecPosCommand.Execute(vm.PosCipleri.Single(p => p.Id == 2));
        vm.SecPosCommand.Execute(vm.PosCipleri.Single(p => p.Id == 1));
        await vm.SatisKaydetCommand.ExecuteAsync(null);
        var g = api.SonPosSatisGuncelle!.Value;
        Assert.Equal((1, (decimal?)1.79m, (int?)1), (g.G.PosId, g.G.KomisyonOrani, g.G.BlokajGunu));
    }

    [Fact]
    public async Task Yeni_satista_pos_degisimi_girilen_orani_ellemez()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti"), Tanim(2, "iyzico", oran: 3.49m, blokaj: 7));
        await vm.YukleAsync();

        vm.SatisOran = "1,79";
        vm.SatisBlokaj = "1";
        vm.SecPosCommand.Execute(vm.PosCipleri.Single(p => p.Id == 2));
        Assert.Equal(("1,79", "1"), (vm.SatisOran, vm.SatisBlokaj));
    }

    [Fact]
    public async Task Satis_sil()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti"));
        api.PosSatislariListe.Add(Satis(5, 1, 1000m));
        await vm.YukleAsync();

        await vm.SatisSilCommand.ExecuteAsync(vm.Satislar[0]);

        Assert.Equal(5, api.SonPosSatisSil);
    }

    [Fact]
    public async Task Tanim_olustur_oran_turkce_virgullu()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        Assert.True(vm.TanimYok);
        vm.TanimAd = "  Garanti  ";
        vm.SecSaglayiciCommand.Execute(vm.SaglayiciCipleri.Single(c => c.Ad == "iyzico"));
        vm.SecTanimKanalCommand.Execute(vm.KanalCipleri.Single(c => c.Ad == "TOPTAN"));
        vm.TanimOran = "2,49";
        vm.TanimBlokaj = "";

        await vm.TanimKaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal(new PosTanimYaz("Garanti", PosSaglayici.Iyzico, 3, 2.49m, 0, true), api.SonPosTanimOlustur);
        Assert.Null(vm.TanimAd);   // form boşaldı
        Assert.Equal(PosSaglayici.BankaPosu, vm.TanimSaglayici);
    }

    [Fact]
    public async Task Tanim_dogrulamalari()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();

        await vm.TanimKaydetCommand.ExecuteAsync(null);
        Assert.Equal(PosViewModel.AdBosMesaji, vm.Hata);

        vm.TanimAd = "Garanti";
        await vm.TanimKaydetCommand.ExecuteAsync(null);
        Assert.Equal(PosViewModel.OranGerekliMesaji, vm.Hata);

        vm.TanimOran = "1,23456";
        await vm.TanimKaydetCommand.ExecuteAsync(null);
        Assert.Equal(OranGiris.HataOndalik, vm.Hata);
        Assert.Null(api.SonPosTanimOlustur);
    }

    [Fact]
    public async Task Tanim_duzenle_kanalsiz_pos_icin_kanalsiz_cipi_secili()
    {
        var (api, vm) = Kur(Tanim(1, "PayTR", oran: 2.5m, blokaj: 7, kanalId: null));
        await vm.YukleAsync();

        vm.TanimDuzenleCommand.Execute(vm.Tanimlar[0]);

        Assert.Equal("POS düzenle", vm.TanimFormBasligi);
        Assert.Equal("2,5", vm.TanimOran);
        Assert.Equal("7", vm.TanimBlokaj);
        Assert.True(vm.KanalCipleri.Single(k => k.Id is null).Secili);

        vm.TanimAktif = false;
        await vm.TanimKaydetCommand.ExecuteAsync(null);
        Assert.Equal(1, api.SonPosTanimGuncelle!.Value.Id);
        Assert.False(api.SonPosTanimGuncelle!.Value.G.Aktif);
    }

    [Fact]
    public async Task Tanim_kanali_degisince_eski_satislara_uygula_secenegi_gorunur_isaretlenirse_gonderilir()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti", kanalId: 1));
        await vm.YukleAsync();

        // Yeni POS'ta seçenek yok.
        vm.SecTanimKanalCommand.Execute(vm.KanalCipleri.Single(k => k.Id == 3));
        Assert.False(vm.TanimKanalDegisti);

        vm.TanimDuzenleCommand.Execute(vm.Tanimlar[0]);
        Assert.False(vm.TanimKanalDegisti);   // kanal aynı
        vm.SecTanimKanalCommand.Execute(vm.KanalCipleri.Single(k => k.Id == 3));
        Assert.True(vm.TanimKanalDegisti);
        vm.TanimEskiSatislaraUygula = true;

        // Eski kanala dönülürse seçenek kapanır ve sıfırlanır.
        vm.SecTanimKanalCommand.Execute(vm.KanalCipleri.Single(k => k.Id == 1));
        Assert.False(vm.TanimKanalDegisti);
        Assert.False(vm.TanimEskiSatislaraUygula);

        vm.SecTanimKanalCommand.Execute(vm.KanalCipleri.Single(k => k.Id == 3));
        await vm.TanimKaydetCommand.ExecuteAsync(null);
        Assert.False(api.SonPosTanimGuncelle!.Value.G.EskiSatislaraUygula);   // varsayılan: geçmiş korunur

        vm.TanimDuzenleCommand.Execute(vm.Tanimlar[0]);
        vm.SecTanimKanalCommand.Execute(vm.KanalCipleri.Single(k => k.Id is null));
        vm.TanimEskiSatislaraUygula = true;
        await vm.TanimKaydetCommand.ExecuteAsync(null);
        Assert.Equal((1, (int?)null, true), (api.SonPosTanimGuncelle!.Value.Id, api.SonPosTanimGuncelle!.Value.G.KanalId, api.SonPosTanimGuncelle!.Value.G.EskiSatislaraUygula));
        Assert.False(vm.TanimEskiSatislaraUygula);   // form sıfırlandı
    }

    [Fact]
    public async Task Ozet_bloke_parayi_kanal_kanal_gosterir_alan_yoksa_bos()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti"));
        await vm.YukleAsync();
        Assert.Empty(vm.BlokeKanallari);   // sunucu alanı göndermezse (eski sürüm) boş döküm
        Assert.False(vm.BlokeKanalVar);

        api.PosOzet = new PosOzetDto(2026, 9, new DateOnly(2026, 9, 24), 1020m, 2, [], [], 0m, 0m, 0m,
            [new PosKanalBlokeDto("TOPTAN", 980m, 1), new PosKanalBlokeDto("Kanalsız", 40m, 1)]);
        var degisenler = new List<string?>();
        vm.PropertyChanged += (_, e) => degisenler.Add(e.PropertyName);
        await vm.OncekiAyCommand.ExecuteAsync(null);   // ay değişse de bloke dökümü bugüne göre gelir

        Assert.Equal(["TOPTAN", "Kanalsız"], vm.BlokeKanallari.Select(k => k.Kanal));
        Assert.True(vm.BlokeKanalVar);
        Assert.Contains(nameof(PosViewModel.BlokeKanallari), degisenler);
    }

    [Fact]
    public async Task Tanim_silme_iki_adimli()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti"));
        await vm.YukleAsync();

        vm.TanimSilIsteCommand.Execute(vm.Tanimlar[0]);
        Assert.True(vm.TanimSilmeOnayiBekliyor);
        vm.TanimSilVazgecCommand.Execute(null);
        Assert.Null(api.SonPosTanimSil);

        vm.TanimSilIsteCommand.Execute(vm.Tanimlar[0]);
        await vm.TanimSilOnaylaCommand.ExecuteAsync(null);
        Assert.Equal(1, api.SonPosTanimSil);
        Assert.Null(vm.SatisPosId);
        Assert.True(vm.TanimYok);
    }

    [Fact]
    public async Task Pasif_pos_satis_ciplerinde_gorunmez_ama_duzenlenen_satisinki_gorunur()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti"), Tanim(2, "Eski POS", aktif: false));
        api.PosSatislariListe.Add(Satis(5, 2, 100m));
        await vm.YukleAsync();
        Assert.Equal(["Garanti"], vm.PosCipleri.Select(p => p.Ad));

        vm.SatisDuzenleCommand.Execute(vm.Satislar[0]);
        Assert.Equal(["Garanti", "Eski POS"], vm.PosCipleri.Select(p => p.Ad));
        Assert.True(vm.PosCipleri[1].Secili);
    }

    [Fact]
    public async Task Sunucu_hatasi_gosterilir_form_korunur()
    {
        var (api, vm) = Kur(Tanim(1, "Garanti"));
        await vm.YukleAsync();
        api.PosYazHatasi = new KasaApiException(System.Net.HttpStatusCode.Conflict, "Bu adla bir POS zaten var.");
        vm.TanimAd = "Garanti";
        vm.TanimOran = "1";

        await vm.TanimKaydetCommand.ExecuteAsync(null);

        Assert.Equal("Bu adla bir POS zaten var.", vm.Hata);
        Assert.Equal("Garanti", vm.TanimAd);
    }

    [Theory]
    [InlineData("1,79", 1.79)]
    [InlineData("1.79", 1.79)]
    [InlineData("%2", 2)]
    [InlineData(" 0 ", 0)]
    [InlineData("100", 100)]
    [InlineData("0,0001", 0.0001)]
    public void Oran_girisi_gecerli(string metin, decimal beklenen)
    {
        var s = OranGiris.Ayristir(metin);
        Assert.True(s.Gecerli);
        Assert.Equal(beklenen, s.Deger);
    }

    [Theory]
    [InlineData("abc", OranGiris.HataGecersiz)]
    [InlineData("1,2,3", OranGiris.HataGecersiz)]
    [InlineData("1.234,5", OranGiris.HataGecersiz)]
    [InlineData("-1", OranGiris.HataAralik)]
    [InlineData("100,01", OranGiris.HataAralik)]
    [InlineData("0,00001", OranGiris.HataOndalik)]
    public void Oran_girisi_gecersiz(string metin, string hata)
    {
        var s = OranGiris.Ayristir(metin);
        Assert.False(s.Gecerli);
        Assert.Equal(hata, s.Hata);
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("0", true, 0)]
    [InlineData("365", true, 365)]
    [InlineData("366", false, null)]
    [InlineData("-1", false, null)]
    [InlineData("1,5", false, null)]
    public void Blokaj_girisi(string metin, bool gecerli, int? deger)
    {
        var s = BlokajGiris.Ayristir(metin);
        Assert.Equal(gecerli, s.Gecerli);
        Assert.Equal(deger, s.Deger);
    }
}
