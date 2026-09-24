using System.Text;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket C · 16: Excel'den toplu yükleme (ayrıştırma, satır doğrulama, önizleme ve kayıt).</summary>
public class TopluGirisTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 24);

    // ---------- TopluMetin: ayırıcı ve ayrıştırma ----------

    [Theory]
    [InlineData("a\tb;c,d", '\t')]
    [InlineData("a;b,c", ';')]
    [InlineData("a,b", ',')]
    [InlineData("abc", '\t')]
    [InlineData("\r\n  \r\nTarih;Tutar", ';')]   // boş satırlar atlanır
    public void Ayirici_ilk_dolu_satirdan_bulunur(string metin, char beklenen)
        => Assert.Equal(beklenen, TopluMetin.AyiriciBul(metin));

    [Fact]
    public void Ayristirma_tirnak_bom_bos_satir_ve_kirpma()
    {
        var metin = "﻿Tarih;Cari;Tutar\r\n01.09.2026; \"ABC; Ltd\" ;1.500,50\r\n\r\n;;\r\n"
                    + "02.09.2026;\"Satır\nsonu \"\"x\"\"\";10\r\n";
        var t = TopluMetin.Ayristir(metin);
        Assert.Equal(3, t.Count);
        Assert.Equal(["Tarih", "Cari", "Tutar"], t[0]);
        Assert.Equal(["01.09.2026", "ABC; Ltd", "1.500,50"], t[1]);
        Assert.Equal(["02.09.2026", "Satır\nsonu \"x\"", "10"], t[2]);
    }

    [Fact]
    public void Excel_yapistirmasi_sekmeyle_ayrilir_virgullu_tutar_bolunmez()
    {
        var t = TopluMetin.Ayristir("01.09.2026\tMarket\t1.234,56\tMEZAT\r\n02.09.2026\tKira\t5.000\t\tSabit\r\n");
        Assert.Equal(2, t.Count);
        Assert.Equal(["01.09.2026", "Market", "1.234,56", "MEZAT"], t[0]);
        Assert.Equal(["02.09.2026", "Kira", "5.000", "", "Sabit"], t[1]);
        Assert.Empty(TopluMetin.Ayristir(""));
        Assert.Empty(TopluMetin.Ayristir(null));
        Assert.Empty(TopluMetin.Ayristir("\t\t\r\n"));
    }

    [Fact]
    public void Dosya_icerigi_utf8_bomlu_bomsuz_ya_da_turkce_windows_kod_sayfasi()
    {
        var metin = "Işık;Şükrü Öğüt;Çağ";
        Assert.Equal(metin, TopluMetin.Coz([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(metin)]));
        Assert.Equal(metin, TopluMetin.Coz(Encoding.UTF8.GetBytes(metin)));
        // Excel'in "CSV (virgülle ayrılmış)" çıktısı: Windows-1254 (ı=FD, ş=FE, ğ=F0, Ş=DE, ü=FC, Ö=D6, Ç=C7).
        byte[] cp1254 = [0x49, 0xFE, 0xFD, 0x6B, 0x3B, 0xDE, 0xFC, 0x6B, 0x72, 0xFC, 0x20, 0xD6, 0xF0, 0xFC, 0x74, 0x3B, 0xC7, 0x61, 0xF0];
        Assert.Equal(metin, TopluMetin.Coz(cp1254));
    }

    [Theory]
    [InlineData("24.09.2026", "2026-09-24")]
    [InlineData("4.9.2026", "2026-09-04")]
    [InlineData("24/09/2026", "2026-09-24")]
    [InlineData("24-09-2026", "2026-09-24")]
    [InlineData("2026-09-24", "2026-09-24")]
    [InlineData("24.09.26", "2026-09-24")]
    [InlineData(" 24.09.2026 00:00:00 ", "2026-09-24")]   // Excel saat ekler
    [InlineData("2026-09-24T00:00:00", "2026-09-24")]
    public void Tarih_bicimleri(string metin, string beklenen)
        => Assert.Equal(DateOnly.Parse(beklenen), TopluMetin.TarihCoz(metin));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("31.02.2026")]
    [InlineData("09/24/2026")]   // Amerikan sırası kabul edilmez
    public void Gecersiz_tarih_null(string metin) => Assert.Null(TopluMetin.TarihCoz(metin));

    [Theory]
    [InlineData("1.500,50", 1500.50)]
    [InlineData(" 1.234,5 ", 1234.5)]
    [InlineData("-250", 250)]
    [InlineData("−250", 250)]
    [InlineData("(1.000)", 1000)]
    [InlineData("250-", 250)]
    [InlineData("0,01", 0.01)]
    public void Tutar_turkce_bicim_eksi_ve_parantez_mutlak_deger(string metin, double beklenen)
    {
        var (t, h) = TopluMetin.TutarCoz(metin);
        Assert.Null(h);
        Assert.Equal((decimal)beklenen, t);
    }

    [Theory]
    [InlineData("", "Tutar boş.")]
    [InlineData("0", "Tutar sıfırdan büyük olmalı.")]
    [InlineData("abc", ParaGiris.HataGecersiz)]
    [InlineData("1,555", ParaGiris.HataKurus)]
    public void Gecersiz_tutar_hatasi(string metin, string hata)
    {
        var (t, h) = TopluMetin.TutarCoz(metin);
        Assert.Null(t);
        Assert.Equal(hata, h);
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("Cari", true, GiderTipi.Cari)]
    [InlineData("c", true, GiderTipi.Cari)]
    [InlineData("Sabit Gider", true, GiderTipi.SabitGider)]
    [InlineData("SG", true, GiderTipi.SabitGider)]
    [InlineData("K.K", true, GiderTipi.KrediKarti)]
    [InlineData("Kredi Kartı", true, GiderTipi.KrediKarti)]
    [InlineData("KART", true, GiderTipi.KrediKarti)]
    [InlineData("nakit", false, null)]
    public void Tip_takma_adlari(string metin, bool gecerli, GiderTipi? tip)
        => Assert.Equal((gecerli, tip), TopluMetin.TipCoz(metin));

    [Theory]
    [InlineData(100, 3, new[] { 33.34, 33.33, 33.33 })]
    [InlineData(10, 2, new[] { 5.0, 5.0 })]
    [InlineData(0.05, 3, new[] { 0.03, 0.01, 0.01 })]
    [InlineData(1234.57, 1, new[] { 1234.57 })]
    [InlineData(0.01, 2, new[] { 0.01, 0.0 })]
    public void Bolme_kurus_artigi_ilk_parcaya_toplam_korunur(double tutar, int parca, double[] beklenen)
    {
        var p = TopluMetin.Bol((decimal)tutar, parca);
        Assert.Equal(beklenen.Select(b => (decimal)b), p);
        Assert.Equal((decimal)tutar, p.Sum());
    }

    [Fact]
    public void Sifir_parcaya_bolunemez()
        => Assert.Throws<ArgumentOutOfRangeException>(() => TopluMetin.Bol(10m, 0));

    // ---------- TopluBaslik ----------

    [Fact]
    public void Baslik_satiri_taninir_aciklama_cari_ya_da_not_olur()
    {
        var h = TopluBaslik.Coz(["İşlem Tarihi", "Açıklama", "Tutar (TL)", "Şube"])!;
        Assert.Equal(0, h[TopluSutun.Tarih]);
        Assert.Equal(1, h[TopluSutun.Cari]);
        Assert.Equal(2, h[TopluSutun.Tutar]);
        Assert.Equal(3, h[TopluSutun.Kanal]);

        var h2 = TopluBaslik.Coz(["Tarih", "Firma", "Açıklama", "Tutar", "Ödeme Tipi", "Kart"])!;
        Assert.Equal(1, h2[TopluSutun.Cari]);
        Assert.Equal(2, h2[TopluSutun.Not]);
        Assert.Equal(4, h2[TopluSutun.Tip]);
        Assert.Equal(5, h2[TopluSutun.Kart]);
    }

    [Fact]
    public void Veri_satiri_ya_da_tarihsiz_tutarsiz_satir_baslik_sayilmaz()
    {
        Assert.Null(TopluBaslik.Coz(["01.09.2026", "Market", "100"]));
        Assert.Null(TopluBaslik.Coz(["Kanal", "Not"]));
        Assert.Null(TopluBaslik.Coz(["Tarih"]));
    }

    [Fact]
    public void Eksik_sutun_bos_kalir()
    {
        var s = TopluSatir.Olustur(["01.09.2026", "Market"], TopluBaslik.Varsayilan);
        Assert.Equal(("01.09.2026", "Market", "", "", ""), (s.TarihMetni, s.Cari, s.TutarMetni, s.Kanal, s.KartMetni));
    }

    // ---------- TopluDogrulama ----------

    private static readonly KanalDto Mezat = new(1, "MEZAT", true, 1, 0m);
    private static readonly KanalDto Toptan = new(2, "TOPTAN", true, 2, 0m);
    private static readonly KanalDto Eski = new(3, "ESKİ", false, 3, 0m);
    private static readonly KrediKartiDto Bonus = new(7, "Bonus", new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 20), 50_000m, 0m);
    private static readonly KrediKartiDto World = new(8, "World", new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 20), 50_000m, 0m);

    private static TopluBaglam Baglam(bool yeniCari = true, string? varsayilan = null, params KrediKartiDto[] kartlar)
        => new([Mezat, Toptan, Eski], ["Market", "ABC Ltd. Şti."], ["Kira", "SGK", "Market"],
            kartlar.Length == 0 ? [Bonus] : kartlar, Bugun, yeniCari, varsayilan);

    private static TopluSatir Satir(string tarih = "01.09.2026", string cari = "market", string tutar = "1.500",
        string kanal = "mezat", string tip = "", string not = "", string kart = "")
        => new() { TarihMetni = tarih, Cari = cari, TutarMetni = tutar, Kanal = kanal, TipMetni = tip, Not = not, KartMetni = kart };

    private static TopluSatir Dogrulanmis(TopluSatir s, TopluBaglam? b = null)
    {
        TopluDogrulama.Dogrula(s, b ?? Baglam());
        return s;
    }

    [Fact]
    public void Gecerli_satir_kayitli_yazimlara_cozulur()
    {
        var s = Dogrulanmis(Satir(not: "  fiş 12 "));
        Assert.True(s.Gecerli);
        Assert.Null(s.Bilgi);
        Assert.False(s.YeniCari);
        // Market hem cari hem kalem: tip boşsa Cari.
        Assert.Equal(new IslemYaz(new DateOnly(2026, 9, 1), "Market", 1500m, "MEZAT", GiderTipi.Cari, "fiş 12", null),
            TopluDogrulama.Islem(s));
    }

    [Fact]
    public void Bos_tipte_yalniz_kalem_olan_ad_sabit_gider_olur()
    {
        var s = Dogrulanmis(Satir(cari: "kira"));
        Assert.Equal((GiderTipi.SabitGider, "Kira"), (s.Tip!.Value, s.CariAdi));
        var sg = Dogrulanmis(Satir(cari: "Elektrik", tip: "SG"));
        Assert.Equal("'Elektrik' adında bir sabit gider kalemi yok.", sg.Hata);
    }

    [Fact]
    public void Yeni_cari_bilgi_olarak_isaretlenir_benzeri_varsa_soylenir()
    {
        var yeni = Dogrulanmis(Satir(cari: "Kira", tip: "cari"));   // kalem adı ama tip Cari: yeni cari
        Assert.True(yeni.Gecerli);
        Assert.True(yeni.YeniCari);
        Assert.Equal("Yeni cari eklenecek", yeni.Bilgi);

        var benzer = Dogrulanmis(Satir(cari: "ABC Ltd"));
        Assert.True(benzer.YeniCari);
        Assert.Equal("Yeni cari (benzer: ABC Ltd. Şti.)", benzer.Bilgi);

        var kapali = Dogrulanmis(Satir(cari: "Yeni Firma"), Baglam(yeniCari: false));
        Assert.False(kapali.YeniCari);
        Assert.Equal("'Yeni Firma' adında bir cari yok.", kapali.Hata);
    }

    [Fact]
    public void Kart_sutunu_kk_yapar_tek_kart_varsayilan_birden_coksa_ad_gerekir()
    {
        var kart = Dogrulanmis(Satir(kart: "bonus"));
        Assert.Equal((GiderTipi.KrediKarti, (int?)7), (kart.Tip!.Value, kart.KartId));

        var tek = Dogrulanmis(Satir(tip: "KK"));
        Assert.Equal(7, tek.KartId);

        var cok = Dogrulanmis(Satir(tip: "KK"), Baglam(kartlar: [Bonus, World]));
        Assert.Equal("Kart adını yazın (K.K satırı).", cok.Hata);

        var yok = Dogrulanmis(Satir(kart: "Axess"));
        Assert.Equal("'Axess' adında bir kart yok.", yok.Hata);

        var bosListe = new TopluBaglam([Mezat], ["Market"], [], [], Bugun, true, null);
        Assert.Equal("Önce Kredi Kartları sayfasından kart ekleyin.", Dogrulanmis(Satir(tip: "kk"), bosListe).Hata);
    }

    [Fact]
    public void Kanal_bos_ise_varsayilan_ortak_ve_pasif_kanal()
    {
        Assert.Equal("Kanal boş.", Dogrulanmis(Satir(kanal: "")).Hata);
        Assert.Equal("TOPTAN", Dogrulanmis(Satir(kanal: ""), Baglam(varsayilan: "TOPTAN")).KanalAdi);
        Assert.Equal("MEZAT", Dogrulanmis(Satir(kanal: "Mezat"), Baglam(varsayilan: "TOPTAN")).KanalAdi);
        Assert.Equal("Ortak", Dogrulanmis(Satir(kanal: "ORTAK")).KanalAdi);

        var pasif = Dogrulanmis(Satir(kanal: "eski"));
        Assert.True(pasif.Gecerli);
        Assert.Equal(("ESKİ", "Pasif kanal"), (pasif.KanalAdi, pasif.Bilgi));

        Assert.Equal("'BAYİ' adında bir kanal yok.", Dogrulanmis(Satir(kanal: "BAYİ")).Hata);
    }

    [Fact]
    public void Tarih_kurallari_ileri_tarih_bilgidir()
    {
        var ileri = Dogrulanmis(Satir(tarih: "30.09.2026"));
        Assert.True(ileri.Gecerli);
        Assert.StartsWith("İleri tarih", ileri.Bilgi);
        Assert.Equal("Tarih boş.", Dogrulanmis(Satir(tarih: " ")).Hata);
        Assert.Equal("Tarih geçersiz (gg.aa.yyyy).", Dogrulanmis(Satir(tarih: "32.01.2026")).Hata);
        Assert.Equal("Tarih 2000 ile 2100 arasında olmalı.", Dogrulanmis(Satir(tarih: "01.01.1999")).Hata);
    }

    [Fact]
    public void Birden_cok_hata_birlikte_yazilir_uzun_not_ve_gecersiz_tip()
    {
        var s = Dogrulanmis(Satir(tarih: "", cari: "", tutar: "0", kanal: "X", tip: "nakit"));
        Assert.Equal("Tarih boş. Tutar sıfırdan büyük olmalı. 'X' adında bir kanal yok. Tip geçersiz (Cari, Sabit gider ya da K.K). Cari boş.", s.Hata);
        Assert.False(s.Gecerli);
        Assert.Null(s.Tarih);
        Assert.Equal("Not en fazla 1000 karakter olabilir.", Dogrulanmis(Satir(not: new string('n', 1001))).Hata);
        Assert.Equal("Cari adı en fazla 200 karakter olabilir.", Dogrulanmis(Satir(cari: new string('c', 201))).Hata);
    }

    // ---------- TopluGirisViewModel ----------

    private sealed class SahteSecici : ITabloSecici
    {
        public SecilenDosya? Dosya;
        public int Cagri;
        public Task<SecilenDosya?> SecAsync() { Cagri++; return Task.FromResult(Dosya); }
    }

    private static async Task<(SahteApi api, TopluGirisViewModel vm, SahteSecici secici)> KurAsync(bool seciciVar = true)
    {
        var api = new SahteApi
        {
            KanallarListe = [Mezat, Toptan, Eski],
            KrediKartlariListe = [Bonus],
        };
        var secici = new SahteSecici();
        var vm = new TopluGirisViewModel(api, new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0)), seciciVar ? secici : null)
        {
            EditorMu = true,
        };
        await vm.YukleAsync();
        return (api, vm, secici);
    }

    [Fact]
    public async Task Yukleme_kanal_ciplerini_kurar_ortak_yalniz_varsayilanda()
    {
        var (_, vm, _) = await KurAsync();
        Assert.Equal(["MEZAT", "TOPTAN", "Ortak"], vm.KanalCipleri.Select(c => c.Ad));
        Assert.Equal(["MEZAT", "TOPTAN"], vm.BolmeKanallari.Select(c => c.Ad));
        Assert.Equal("", vm.Ozet);
        Assert.False(vm.Kaydedilebilir);
    }

    [Fact]
    public async Task Yapistirilan_basliklı_metin_onizlemeye_dokulur_ozet_hesaplanir()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "Tarih\tFirma\tTutar\tKanal\r\n01.09.2026\tMarket\t1.000\tMEZAT\r\n02.09.2026\tYeni Firma\t250,50\tTOPTAN\r\n";

        Assert.Equal(2, vm.Satirlar.Count);
        Assert.Equal([1, 2], vm.Satirlar.Select(s => s.Sira));
        Assert.Equal("2 satır · 2 geçerli · 0 hatalı · toplam 1.250,50 ₺ · 1 yeni cari", vm.Ozet);
        Assert.True(vm.Kaydedilebilir);
    }

    [Fact]
    public async Task Basliksiz_metin_varsayilan_sutun_sirasiyla_okunur()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026;Kira;5.000;TOPTAN;;Eylül kirası\n";
        var s = Assert.Single(vm.Satirlar);
        Assert.Equal((GiderTipi.SabitGider, "Kira", 5000m, "TOPTAN", "Eylül kirası"), (s.Tip!.Value, s.CariAdi, s.Tutar!.Value, s.KanalAdi, s.Not));
    }

    [Fact]
    public async Task Hucre_duzenlenince_satir_yeniden_dogrulanir()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026\tMarket\tabc\tMEZAT\n01.09.2026\tMarket\t10\tYOK\n";
        Assert.Equal(2, vm.HataliSayisi);
        Assert.False(vm.Kaydedilebilir);

        var degisenler = new List<string?>();
        vm.PropertyChanged += (_, e) => degisenler.Add(e.PropertyName);
        vm.Satirlar[0].TutarMetni = "1.500";
        Assert.True(vm.Satirlar[0].Gecerli);
        Assert.Equal(1, vm.HataliSayisi);
        Assert.Contains(nameof(TopluGirisViewModel.Kaydedilebilir), degisenler);

        vm.Satirlar[1].Kanal = "toptan";
        Assert.Equal(0, vm.HataliSayisi);
        Assert.True(vm.Kaydedilebilir);
        Assert.Equal("TOPTAN", vm.Satirlar[1].KanalAdi);
    }

    [Fact]
    public async Task Hatali_satir_varken_hic_gonderilmez_satir_kaldirinca_kaydedilir()
    {
        var (api, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026\tMarket\t10\tMEZAT\n01.09.2026\tMarket\t-\tMEZAT\n";
        await vm.HepsiniKaydetCommand.ExecuteAsync(null);
        Assert.Equal(TopluGirisViewModel.HataliSatirMesaji(1), vm.Hata);
        Assert.Null(api.SonToplu);

        vm.SatirKaldirCommand.Execute(vm.Satirlar[1]);
        Assert.Single(vm.Satirlar);
        Assert.True(vm.Kaydedilebilir);
        await vm.HepsiniKaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Single(api.SonToplu!.Value.Satirlar);
    }

    [Fact]
    public async Task Satir_yokken_kayit_uyarir()
    {
        var (api, vm, _) = await KurAsync();
        await vm.HepsiniKaydetCommand.ExecuteAsync(null);
        Assert.Equal(TopluGirisViewModel.SatirYokMesaji, vm.Hata);
        Assert.Null(api.SonToplu);
    }

    [Fact]
    public async Task Hepsini_kaydet_tek_istekte_gonderir_yeni_cari_bayragi_ve_ozet()
    {
        var (api, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026\tMarket\t1.000\tMEZAT\n02.09.2026\tYeni Firma\t250\tTOPTAN\tCari\tnot\n03.09.2026\tMarket\t99,90\tMEZAT\tKK\n";
        await vm.HepsiniKaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        var (satirlar, yeni) = api.SonToplu!.Value;
        Assert.True(yeni);
        Assert.Equal(
        [
            new IslemYaz(new DateOnly(2026, 9, 1), "Market", 1000m, "MEZAT", GiderTipi.Cari, null, null),
            new IslemYaz(new DateOnly(2026, 9, 2), "Yeni Firma", 250m, "TOPTAN", GiderTipi.Cari, "not", null),
            new IslemYaz(new DateOnly(2026, 9, 3), "Market", 99.90m, "MEZAT", GiderTipi.KrediKarti, null, 7),
        ], satirlar);
        Assert.Equal("3 işlem kaydedildi · toplam 1.349,90 ₺ · 1 yeni cari eklendi", vm.Sonuc);
        Assert.Empty(vm.Satirlar);
        Assert.Equal("", vm.Yapistirilan);
        Assert.Equal("", vm.Ozet);
    }

    [Fact]
    public async Task Yeni_cari_yoksa_bayrak_kapali_kapatilinca_yeni_cari_satiri_hatali()
    {
        var (api, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026\tMarket\t10\tMEZAT\n";
        await vm.HepsiniKaydetCommand.ExecuteAsync(null);
        Assert.False(api.SonToplu!.Value.YeniCariler);

        vm.Yapistirilan = "01.09.2026\tYeni Firma\t10\tMEZAT\n";
        Assert.True(vm.Kaydedilebilir);
        vm.YeniCarileriEkle = false;
        Assert.False(vm.Kaydedilebilir);
        Assert.Equal("'Yeni Firma' adında bir cari yok.", vm.Satirlar[0].Hata);
    }

    [Fact]
    public async Task Sunucu_satir_reddederse_hata_o_satira_yazilir_satirlar_kalir()
    {
        var (api, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026\tMarket\t10\tMEZAT\n02.09.2026\tMarket\t20\tMEZAT\n";
        api.TopluSonuc = new TopluIslemSonucu(false, 0, 0m, [], [],
            "1 satırda hata var; hiçbir satır kaydedilmedi. 2. satır: Kanal bulunamadı.", [new TopluSatirHatasi(2, "Kanal bulunamadı."), new TopluSatirHatasi(9, "yok sayılır")]);
        await vm.HepsiniKaydetCommand.ExecuteAsync(null);

        Assert.StartsWith("1 satırda hata var", vm.Hata);
        Assert.Equal(2, vm.Satirlar.Count);
        Assert.Null(vm.Satirlar[0].Hata);
        Assert.Equal("Kanal bulunamadı.", vm.Satirlar[1].Hata);
        Assert.Equal(1, vm.HataliSayisi);
        Assert.False(vm.Kaydedilebilir);
        Assert.Null(vm.Sonuc);

        vm.Satirlar[1].Not = "düzeltildi";   // düzenleyince yeniden doğrulanır
        Assert.True(vm.Kaydedilebilir);
    }

    [Fact]
    public async Task Sunucu_hatasi_mesaj_olarak_gosterilir()
    {
        var (api, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026\tMarket\t10\tMEZAT\n";
        api.TopluSonuc = new TopluIslemSonucu(false, 0, 0m, [], [], null, [new TopluSatirHatasi(1, "x")]);
        await vm.HepsiniKaydetCommand.ExecuteAsync(null);
        Assert.Equal(TopluGirisViewModel.HataliSatirMesaji(1), vm.Hata);
    }

    [Fact]
    public async Task Bolme_secili_kanallara_esit_kurus_artigi_ilk_kanala()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026\tSGK\t100,01\t\tSabit\tEylül\n02.09.2026\tMarket\t5\tMEZAT\n";
        vm.BolCommand.Execute(vm.Satirlar[0]);
        Assert.Equal(TopluGirisViewModel.BolmeKanalMesaji, vm.Hata);

        foreach (var c in vm.BolmeKanallari) vm.SecBolmeKanalCommand.Execute(c);
        vm.BolCommand.Execute(vm.Satirlar[0]);
        Assert.Null(vm.Hata);
        Assert.Equal(3, vm.Satirlar.Count);
        Assert.Equal([1, 2, 3], vm.Satirlar.Select(s => s.Sira));
        Assert.Equal(("MEZAT", 50.01m, "SGK", "Eylül"), (vm.Satirlar[0].KanalAdi, vm.Satirlar[0].Tutar!.Value, vm.Satirlar[0].CariAdi, vm.Satirlar[0].Not));
        Assert.Equal(("TOPTAN", 50.00m), (vm.Satirlar[1].KanalAdi, vm.Satirlar[1].Tutar!.Value));
        Assert.Equal("50,01", vm.Satirlar[0].TutarMetni);
        Assert.Equal("Market", vm.Satirlar[2].CariAdi);
        Assert.Equal(0, vm.HataliSayisi);

        // Bölünen yeni satırlar da canlı doğrulanır.
        vm.Satirlar[1].TutarMetni = "x";
        Assert.Equal(1, vm.HataliSayisi);
    }

    [Fact]
    public async Task Gecersiz_tutarli_satir_bolunmez()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026\tMarket\tabc\tMEZAT\n";
        foreach (var c in vm.BolmeKanallari) vm.SecBolmeKanalCommand.Execute(c);
        vm.BolCommand.Execute(vm.Satirlar[0]);
        Assert.Equal($"1. satır bölünemedi: {ParaGiris.HataGecersiz}", vm.Hata);
        Assert.Single(vm.Satirlar);
    }

    [Fact]
    public async Task Varsayilan_kanal_bos_kanal_hucrelerini_doldurur_ikinci_basis_kaldirir()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026\tMarket\t10\n01.09.2026\tMarket\t10\tMEZAT\n";
        Assert.Equal(1, vm.HataliSayisi);

        var toptan = vm.KanalCipleri.Single(c => c.Ad == "TOPTAN");
        vm.SecVarsayilanKanalCommand.Execute(toptan);
        Assert.Equal("TOPTAN", vm.VarsayilanKanal);
        Assert.True(toptan.Secili);
        Assert.Equal(0, vm.HataliSayisi);
        Assert.Equal(("TOPTAN", "MEZAT"), (vm.Satirlar[0].KanalAdi, vm.Satirlar[1].KanalAdi));

        vm.SecVarsayilanKanalCommand.Execute(toptan);
        Assert.Null(vm.VarsayilanKanal);
        Assert.False(toptan.Secili);
        Assert.Equal(1, vm.HataliSayisi);
    }

    [Fact]
    public async Task Bin_satirdan_fazlasi_kaydedilemez()
    {
        var (api, vm, _) = await KurAsync();
        var sb = new StringBuilder();
        for (var i = 0; i < TopluMetin.EnFazlaSatir + 1; i++) sb.Append("01.09.2026\tMarket\t1\tMEZAT\n");
        vm.Yapistirilan = sb.ToString();
        Assert.Equal(TopluGirisViewModel.CokSatirMesaji, vm.Hata);
        Assert.False(vm.Kaydedilebilir);
        await vm.HepsiniKaydetCommand.ExecuteAsync(null);
        Assert.Equal(TopluGirisViewModel.CokSatirMesaji, vm.Hata);
        Assert.Null(api.SonToplu);
    }

    [Fact]
    public async Task Dosya_secimi_csv_okur_kod_sayfasi_ve_ad()
    {
        var (_, vm, secici) = await KurAsync();
        secici.Dosya = new SecilenDosya("eylul.csv", [.. Encoding.Latin1.GetBytes("Tarih;Cari;Tutar;Kanal\r\n01.09.2026;Market;1.000,50;MEZAT\r\n")]);
        await vm.DosyaSecCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal("eylul.csv", vm.DosyaAdi);
        var s = Assert.Single(vm.Satirlar);
        Assert.Equal(1000.50m, s.Tutar);

        secici.Dosya = null;   // vazgeçti: önizleme kalır
        await vm.DosyaSecCommand.ExecuteAsync(null);
        Assert.Single(vm.Satirlar);
        Assert.Equal("eylul.csv", vm.DosyaAdi);

        secici.Dosya = new SecilenDosya("buyuk.csv", new byte[TopluMetin.EnBuyukDosya + 1]);
        await vm.DosyaSecCommand.ExecuteAsync(null);
        Assert.Equal(TopluGirisViewModel.DosyaBuyukMesaji, vm.Hata);
        Assert.Equal("eylul.csv", vm.DosyaAdi);
    }

    [Fact]
    public async Task Secici_yoksa_yapistirma_onerilir()
    {
        var (_, vm, _) = await KurAsync(seciciVar: false);
        await vm.DosyaSecCommand.ExecuteAsync(null);
        Assert.Equal(TopluGirisViewModel.SeciciYokMesaji, vm.Hata);
    }

    [Fact]
    public async Task Temizle_her_seyi_bosaltir()
    {
        var (_, vm, secici) = await KurAsync();
        secici.Dosya = new SecilenDosya("a.csv", Encoding.UTF8.GetBytes("01.09.2026;Market;10;MEZAT"));
        await vm.DosyaSecCommand.ExecuteAsync(null);
        vm.TemizleCommand.Execute(null);
        Assert.Empty(vm.Satirlar);
        Assert.Equal("", vm.Yapistirilan);
        Assert.Null(vm.DosyaAdi);
        Assert.Equal("", vm.Ozet);
        Assert.False(vm.Kaydedilebilir);
    }

    [Fact]
    public async Task Baglam_yuklenmeden_yapistirilan_yukleme_sonrasi_yeniden_dogrulanir()
    {
        var api = new SahteApi { KanallarListe = [Mezat] };
        var vm = new TopluGirisViewModel(api, new SabitSaat(new DateTime(2026, 9, 24)));
        vm.Yapistirilan = "01.09.2026\tMarket\t10\tMEZAT\n";
        Assert.Equal(1, vm.HataliSayisi);   // kanal listesi henüz yok
        await vm.YukleAsync();
        Assert.Equal(0, vm.HataliSayisi);
    }
}
