using System.Text;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>ERP12 tediye karşılaştırma sayfası (Paket F, madde 42): salt okunur, eşleştirme cihazda hatırlanır.</summary>
public class Erp12KarsilastirmaViewModelTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    private const string Csv =
        "ERP12 Tediye Listesi\r\n" +
        "Belge No;Tarih;Cari Kodu;Cari Ünvanı;Açıklama;Döviz Tutarı;Tutar\r\n" +
        "T-1;10.09.2026;120.01;YILMAZ GIDA LTD. ŞTİ.;Eylül;0;1.234,56\r\n" +
        "T-2;12.09.2026;120.02;KAYA AMBALAJ;;0;500,00\r\n" +
        "T-3;15.09.2026;120.03;DEMİR NAKLİYAT;;0;750,00\r\n" +
        "T-4;;120.04;BOZUK SATIR;;0;10,00\r\n" +
        "T-5;16.09.2026;120.05;SIFIR TUTAR;;0;0\r\n";

    private static SecilenDosya Dosya(string metin = Csv, string ad = "tediye.csv") => new(ad, Encoding.UTF8.GetBytes(metin));

    private static IslemDto K(int id, int gun, string cari, decimal tutar, GiderTipi tip = GiderTipi.Cari)
        => new(id, new DateOnly(2026, 9, gun), cari, tutar, "MEZAT", tip, null);

    private static (SahteApi api, Erp12KarsilastirmaViewModel vm, SahteDosyaSecici secici, BellekAyarDeposu ayar) Kur(BellekAyarDeposu? ayar = null)
    {
        var api = new SahteApi
        {
            IslemlerListe =
            [
                K(1, 11, "Yılmaz Gıda", 1234.56m),       // eşleşir (+1 gün)
                K(2, 12, "Kaya Ambalaj", 499.99m),       // tutar farklı → iki tarafta tekil + ipucu
                K(3, 20, "Market", 300m),                 // yalnız kasada
                K(4, 15, "Demir Nakliyat", 750m, GiderTipi.SabitGider), // Cari değil: varsayılan dışarıda
                K(5, 7, "Aralık dışı", 100m),             // aralıktan önce (±3 gün tamponunda): yalnız kasa listesine girmez
            ],
        };
        var secici = new SahteDosyaSecici();
        ayar ??= new BellekAyarDeposu();
        var vm = new Erp12KarsilastirmaViewModel(api, new SabitSaat(Bugun.AddHours(10)), ayar) { DosyaSecici = secici };
        return (api, vm, secici, ayar);
    }

    [Fact]
    public async Task Dosya_secilince_sutunlar_basliktan_tahmin_edilir_ve_aralik_dosyadan_gelir()
    {
        var (_, vm, secici, _) = Kur();
        secici.Csvler.Enqueue(Dosya());

        await vm.DosyaSecCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.True(vm.DosyaVar);
        Assert.Equal("tediye.csv · 5 satır · ayırıcı ';' · UTF-8", vm.DosyaBilgisi);
        Assert.Equal(1, vm.TarihSutunu);    // "Tarih"
        Assert.Equal(3, vm.CariSutunu);     // "Cari Ünvanı" (Cari Kodu değil)
        Assert.Equal(6, vm.TutarSutunu);    // "Tutar" (Döviz Tutarı değil)
        Assert.True(vm.TarihCipleri[1].Secili);
        Assert.True(vm.CariCipleri[3].Secili);
        Assert.True(vm.TutarCipleri[6].Secili);
        Assert.Equal(new DateTime(2026, 9, 10), vm.Baslangic);
        Assert.Equal(new DateTime(2026, 9, 16), vm.Bitis);
        Assert.Equal(3, vm.Onizleme.Count);
    }

    [Fact]
    public async Task Vazgecilirse_bir_sey_olmaz_secici_yoksa_hata()
    {
        var (_, vm, secici, _) = Kur();
        await vm.DosyaSecCommand.ExecuteAsync(null);
        Assert.Equal(1, secici.CsvCagri);
        Assert.False(vm.DosyaVar);
        Assert.Null(vm.Hata);

        vm.DosyaSecici = null;
        await vm.DosyaSecCommand.ExecuteAsync(null);
        Assert.Equal(Erp12KarsilastirmaViewModel.SeciciYokMesaji, vm.Hata);
    }

    [Fact]
    public async Task Karsilastir_uc_liste_ve_toplamlar_verir_hicbir_sey_yazmaz()
    {
        var (api, vm, secici, _) = Kur();
        secici.Csvler.Enqueue(Dosya());
        await vm.DosyaSecCommand.ExecuteAsync(null);

        await vm.KarsilastirCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.True(vm.SonucVar);
        // Kasa tarafı ±3 gün geniş istenir.
        Assert.Equal(new DateOnly(2026, 9, 7), api.SonFiltreBaslangic);
        Assert.Equal(new DateOnly(2026, 9, 19), api.SonFiltreBitis);

        var eslesen = Assert.Single(vm.Eslesenler);
        Assert.Equal("YILMAZ GIDA LTD. ŞTİ.", eslesen.Baslik);
        Assert.Equal("10.09.2026 (sıra 1) · MEZAT · kasada 1 gün sonra · kasada \"Yılmaz Gıda\"", eslesen.Ayrinti);
        Assert.Equal("1.234,56 ₺", eslesen.Tutar);
        Assert.Equal("1 kayıt · 1.234,56 ₺", vm.EslesenOzeti);

        Assert.Equal(["KAYA AMBALAJ", "DEMİR NAKLİYAT"], vm.YalnizErp12.Select(s => s.Baslik));
        Assert.EndsWith("(tutar 0,01 ₺ farklı)", vm.YalnizErp12[0].Ipucu);
        Assert.True(vm.YalnizErp12[0].IpucuVar);
        Assert.Equal("2 kayıt · 1.250,00 ₺", vm.YalnizErp12Ozeti);

        // Aralık dışı (7 Eylül) ve Cari olmayan işlem yalnız kasa listesinde yok; Market (20 Eylül) de aralık dışı.
        Assert.Equal(["Kaya Ambalaj"], vm.YalnizKasa.Select(s => s.Baslik));
        Assert.Equal("1 kayıt · 499,99 ₺", vm.YalnizKasaOzeti);

        Assert.False(vm.EslesenYok);
        Assert.False(vm.YalnizErp12Yok);
        Assert.False(vm.YalnizKasaYok);
        Assert.Equal("2 satır okunamadı (tarih, tutar ya da cari boş/geçersiz; sıra: 4, 5).", vm.OkunamayanOzeti);
        Assert.True(vm.OkunamayanVar);

        // Salt okunur: hiçbir yazma çağrısı yok.
        Assert.Null(api.SonIslemOlustur);
        Assert.Null(api.SonIslemGuncelle);
        Assert.Empty(api.BelgeGuncellemeleri);
    }

    [Fact]
    public async Task Tum_tipler_acilinca_cari_olmayan_islemler_de_karsilastirilir()
    {
        var (_, vm, secici, _) = Kur();
        secici.Csvler.Enqueue(Dosya());
        await vm.DosyaSecCommand.ExecuteAsync(null);
        vm.TumTipler = true;

        await vm.KarsilastirCommand.ExecuteAsync(null);

        Assert.Equal(["YILMAZ GIDA LTD. ŞTİ.", "DEMİR NAKLİYAT"], vm.Eslesenler.Select(e => e.Baslik));
    }

    [Fact]
    public async Task Aralik_daraltilinca_disindaki_erp_satirlari_ve_kasa_kayitlari_girmez()
    {
        var (api, vm, secici, _) = Kur();
        secici.Csvler.Enqueue(Dosya());
        await vm.DosyaSecCommand.ExecuteAsync(null);
        vm.Baslangic = new DateTime(2026, 9, 12);
        vm.Bitis = new DateTime(2026, 9, 20);

        await vm.KarsilastirCommand.ExecuteAsync(null);

        Assert.Equal(new DateOnly(2026, 9, 9), api.SonFiltreBaslangic);
        Assert.Equal(new DateOnly(2026, 9, 23), api.SonFiltreBitis);
        Assert.Empty(vm.Eslesenler);   // Yılmaz satırı (10 Eylül) aralık dışında
        Assert.True(vm.EslesenYok);
        Assert.Equal(["KAYA AMBALAJ", "DEMİR NAKLİYAT"], vm.YalnizErp12.Select(s => s.Baslik));
        // Yılmaz (11 Eylül) aralık dışı; Market (20 Eylül) aralıkta.
        Assert.Equal(["Kaya Ambalaj", "Market"], vm.YalnizKasa.Select(s => s.Baslik));
    }

    [Fact]
    public async Task Eslestirme_cihazda_basliklarla_hatirlanir_ve_sonraki_dosyada_kullanilir()
    {
        var ayar = new BellekAyarDeposu();
        var (_, vm, secici, _) = Kur(ayar);
        secici.Csvler.Enqueue(Dosya());
        await vm.DosyaSecCommand.ExecuteAsync(null);
        // Kullanıcı carinin "Açıklama" sütununda olduğunu seçer (tahmin dışı).
        vm.SecCariSutunuCommand.Execute(vm.CariCipleri[4]);
        Assert.Equal(4, vm.CariSutunu);
        Assert.False(vm.CariCipleri[3].Secili);
        await vm.KarsilastirCommand.ExecuteAsync(null);
        Assert.NotNull(ayar.Oku(Erp12KarsilastirmaViewModel.AyarAnahtari));

        // Aynı başlıklı, sütun sırası farklı yeni dosya: eşleştirme adla bulunur.
        var (_, vm2, secici2, _) = Kur(ayar);
        secici2.Csvler.Enqueue(Dosya("Açıklama;Tutar;Tarih;Cari Ünvanı\r\nx;100;01.09.2026;A\r\n"));
        await vm2.DosyaSecCommand.ExecuteAsync(null);
        Assert.Equal((2, 0, 1), (vm2.TarihSutunu!.Value, vm2.CariSutunu!.Value, vm2.TutarSutunu!.Value));

        // Başlıkları tutmayan dosyada tahmine dönülür.
        var (_, vm3, secici3, _) = Kur(ayar);
        secici3.Csvler.Enqueue(Dosya("Tarih;Firma;Tutar\r\n01.09.2026;A;100\r\n"));
        await vm3.DosyaSecCommand.ExecuteAsync(null);
        Assert.Equal((0, 1, 2), (vm3.TarihSutunu!.Value, vm3.CariSutunu!.Value, vm3.TutarSutunu!.Value));
    }

    [Fact]
    public async Task Bozuk_ayar_yok_sayilir()
    {
        var ayar = new BellekAyarDeposu();
        ayar.Yaz(Erp12KarsilastirmaViewModel.AyarAnahtari, "{bozuk");
        var (_, vm, secici, _) = Kur(ayar);
        secici.Csvler.Enqueue(Dosya());

        await vm.DosyaSecCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal(3, vm.CariSutunu);
    }

    [Fact]
    public void Basliksiz_dosyada_icerikten_tahmin()
    {
        var t = Erp12Csv.Oku(Encoding.UTF8.GetBytes(
            "1;YILMAZ GIDA;10.09.2026;1.234,56\n2;KAYA AMBALAJ;12.09.2026;500,00\n3;DEMİR;15.09.2026;750,00\n"));
        Assert.False(t.BaslikVar);
        Assert.Equal((2, 1, 3), Erp12SutunTahmini.Tahmin(t));
    }

    [Fact]
    public async Task Dogrulamalar()
    {
        var (_, vm, secici, _) = Kur();
        await vm.KarsilastirCommand.ExecuteAsync(null);
        Assert.Equal(Erp12KarsilastirmaViewModel.DosyaSecinMesaji, vm.Hata);

        secici.Csvler.Enqueue(Dosya());
        await vm.DosyaSecCommand.ExecuteAsync(null);
        vm.TutarSutunu = null;
        await vm.KarsilastirCommand.ExecuteAsync(null);
        Assert.Equal(Erp12KarsilastirmaViewModel.SutunSecinMesaji, vm.Hata);

        vm.TutarSutunu = vm.CariSutunu;
        await vm.KarsilastirCommand.ExecuteAsync(null);
        Assert.Equal(Erp12KarsilastirmaViewModel.AyniSutunMesaji, vm.Hata);

        vm.TutarSutunu = 6;
        vm.Baslangic = new DateTime(2026, 10, 1);
        await vm.KarsilastirCommand.ExecuteAsync(null);
        Assert.Equal(Erp12KarsilastirmaViewModel.AralikMesaji, vm.Hata);

        vm.Baslangic = new DateTime(2026, 1, 1);
        vm.Bitis = new DateTime(2026, 1, 31);
        await vm.KarsilastirCommand.ExecuteAsync(null);
        Assert.StartsWith(Erp12KarsilastirmaViewModel.SatirYokMesaji, vm.Hata);
        Assert.False(vm.SonucVar);
    }

    [Fact]
    public async Task Buyuk_ya_da_bos_dosya_anlasilir_hata()
    {
        var (_, vm, secici, _) = Kur();
        secici.Csvler.Enqueue(new SecilenDosya("dev.csv", [], Erp12KarsilastirmaViewModel.EnFazlaCsvBoyutu + 1));
        await vm.DosyaSecCommand.ExecuteAsync(null);
        Assert.Equal(Erp12KarsilastirmaViewModel.BoyutMesaji, vm.Hata);

        secici.Csvler.Enqueue(new SecilenDosya("bos.csv", []));
        await vm.DosyaSecCommand.ExecuteAsync(null);
        Assert.Equal(Erp12Csv.BosDosyaMesaji, vm.Hata);
        Assert.False(vm.DosyaVar);
    }

    [Fact]
    public async Task Sutun_ya_da_secenek_degisince_eski_sonuc_temizlenir_temizle_hepsini_bosaltir()
    {
        var (_, vm, secici, _) = Kur();
        secici.Csvler.Enqueue(Dosya());
        await vm.DosyaSecCommand.ExecuteAsync(null);
        await vm.KarsilastirCommand.ExecuteAsync(null);
        Assert.True(vm.SonucVar);

        vm.TumTipler = true;
        Assert.False(vm.SonucVar);
        Assert.Empty(vm.Eslesenler);

        await vm.KarsilastirCommand.ExecuteAsync(null);
        vm.SecTutarSutunuCommand.Execute(vm.TutarCipleri[5]);
        Assert.False(vm.SonucVar);

        vm.TemizleCommand.Execute(null);
        Assert.False(vm.DosyaVar);
        Assert.Empty(vm.TarihCipleri);
        Assert.Null(vm.TarihSutunu);
        Assert.Equal("Dosya seçilmedi.", vm.DosyaBilgisi);
    }

    [Fact]
    public async Task Windows_1254_dosya_ve_satir_listesi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var (_, vm, secici, _) = Kur();
        secici.Csvler.Enqueue(new SecilenDosya("erp.csv", Encoding.GetEncoding(1254).GetBytes(Csv)));

        await vm.DosyaSecCommand.ExecuteAsync(null);
        await vm.KarsilastirCommand.ExecuteAsync(null);

        Assert.EndsWith("Windows-1254", vm.DosyaBilgisi);
        Assert.Equal("YILMAZ GIDA LTD. ŞTİ.", Assert.Single(vm.Eslesenler).Baslik);
    }
}
