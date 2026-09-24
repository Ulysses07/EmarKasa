using System.IO.Compression;
using System.Text;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Paket C · 16 (inceleme düzeltmeleri): banka dökümü (işaretli tutar, borç/alacak, başlık öncesi
/// satırlar, açıklamadan cari), virgüllü CSV'de bölünen kuruş ve xlsx dosyası okuma.
/// </summary>
public class TopluGirisDokumTests
{
    private static readonly KanalDto Mezat = new(1, "MEZAT", true, 1, 0m);
    private static readonly KanalDto Toptan = new(2, "TOPTAN", true, 2, 0m);
    private static readonly KrediKartiDto Bonus = new(7, "Bonus", new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 20), 50_000m, 0m);

    private sealed class SahteSecici : ITabloSecici
    {
        public SecilenDosya? Dosya;
        public Task<SecilenDosya?> SecAsync() => Task.FromResult(Dosya);
    }

    private static async Task<(SahteApi api, TopluGirisViewModel vm, SahteSecici secici)> KurAsync()
    {
        var api = new SahteApi { KanallarListe = [Mezat, Toptan], KrediKartlariListe = [Bonus] };
        var secici = new SahteSecici();
        var vm = new TopluGirisViewModel(api, new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0)), secici) { EditorMu = true };
        await vm.YukleAsync();
        return (api, vm, secici);
    }

    // ---------- İşaret: iki işaretli tabloda yalnız gider tarafı ----------

    [Fact]
    public async Task Karisik_isaretli_tabloda_artilar_gider_sayilmaz_toplam_yalniz_giderler()
    {
        var (api, vm, _) = await KurAsync();
        vm.Yapistirilan = "Tarih\tCari\tTutar\tKanal\n"
                          + "01.09.2026\tMarket\t-1.500,00\tMEZAT\n"
                          + "02.09.2026\tMarket\t2.000,00\tMEZAT\n"     // müşteri havalesi: gelen para
                          + "03.09.2026\tMarket\t(250)\tMEZAT\n";

        Assert.True(vm.IsaretAnlamli);
        Assert.True(vm.EksilerGider);
        Assert.True(vm.Satirlar[0].Gecerli);
        Assert.Equal(TopluDogrulama.ArtiGiderDegilMesaji, vm.Satirlar[1].Hata);
        Assert.True(vm.Satirlar[1].GiderDegil);
        Assert.True(vm.Satirlar[2].Gecerli);
        Assert.Equal(1, vm.GiderOlmayanSayisi);
        Assert.True(vm.GiderOlmayanVar);
        Assert.Contains("toplam 1.750,00 ₺", vm.Ozet);
        Assert.False(vm.Kaydedilebilir);

        await vm.HepsiniKaydetCommand.ExecuteAsync(null);
        Assert.Null(api.SonToplu);                                  // artı satır sessizce gider olmaz

        vm.GiderOlmayanlariKaldirCommand.Execute(null);
        Assert.Equal(2, vm.Satirlar.Count);
        Assert.Equal([1, 2], vm.Satirlar.Select(s => s.Sira));
        Assert.False(vm.IsaretAnlamli);                             // kalanların hepsi eksi: tek işaret
        await vm.HepsiniKaydetCommand.ExecuteAsync(null);
        Assert.Equal([1_500m, 250m], api.SonToplu!.Value.Satirlar.Select(g => g.TutarTl));
    }

    [Fact]
    public async Task Isaret_secenegi_degisince_artilar_gider_eksiler_iade_olur()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026\tMarket\t1.000\tMEZAT\n02.09.2026\tMarket\t-100\tMEZAT\n";   // eski tablo + iade
        Assert.True(vm.IsaretAnlamli);
        Assert.False(vm.Satirlar[0].Gecerli);

        vm.EksilerGider = false;
        Assert.True(vm.Satirlar[0].Gecerli);
        Assert.Equal(TopluDogrulama.EksiGiderDegilMesaji, vm.Satirlar[1].Hata);
        Assert.StartsWith("Artı tutarlar gider", vm.IsaretAciklamasi);
        Assert.Contains("toplam 1.000,00 ₺", vm.Ozet);
    }

    [Fact]
    public async Task Tek_isaretli_tablo_eskisi_gibi_hepsi_gider()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026\tMarket\t-1.000\tMEZAT\n02.09.2026\tMarket\t-(5)\tMEZAT\n03.09.2026\tMarket\t250-\tMEZAT\n";
        Assert.False(vm.IsaretAnlamli);
        Assert.Equal(1, vm.HataliSayisi);                           // "-(5)" geçersiz; işaret sayımına girmez
        Assert.Equal(0, vm.GiderOlmayanSayisi);

        vm.Yapistirilan = "01.09.2026\tMarket\t1.000\tMEZAT\n02.09.2026\tMarket\t5\tMEZAT\n";
        Assert.False(vm.IsaretAnlamli);
        Assert.True(vm.Kaydedilebilir);
    }

    [Fact]
    public async Task Hucre_duzenlenip_isaret_degisince_butun_satirlar_yeniden_dogrulanir()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026\tMarket\t100\tMEZAT\n02.09.2026\tMarket\t200\tMEZAT\n";
        Assert.True(vm.Kaydedilebilir);

        vm.Satirlar[1].TutarMetni = "-200";                         // artık iki işaret: eksi gider
        Assert.True(vm.IsaretAnlamli);
        Assert.True(vm.Satirlar[0].GiderDegil);                     // dokunulmayan satır da yeniden doğrulandı
        Assert.True(vm.Satirlar[1].Gecerli);

        vm.Satirlar[1].TutarMetni = "200";
        Assert.False(vm.IsaretAnlamli);
        Assert.Equal(0, vm.HataliSayisi);
    }

    [Fact]
    public async Task Bolme_eksi_tutarin_isaretini_korur()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "01.09.2026\tMarket\t-100,01\tMEZAT\n02.09.2026\tMarket\t50\tMEZAT\n";
        foreach (var c in vm.BolmeKanallari) vm.SecBolmeKanalCommand.Execute(c);
        vm.BolCommand.Execute(vm.Satirlar[0]);
        Assert.Equal(["-50,01", "-50,00", "50"], vm.Satirlar.Select(s => s.TutarMetni));
        Assert.True(vm.Satirlar[0].Gecerli);
        Assert.True(vm.Satirlar[1].Gecerli);
        Assert.True(vm.Satirlar[2].GiderDegil);
    }

    // ---------- Banka dökümü başlıkları ----------

    [Fact]
    public void Banka_basliklari_taninir_borc_alacak_bakiye()
    {
        var h = TopluBaslik.Coz(["İşlem Tarihi", "Açıklama", "İşlem Tutarı", "Bakiye"])!;
        Assert.Equal((0, 1, 2, 3), (h[TopluSutun.Tarih], h[TopluSutun.Cari], h[TopluSutun.Tutar], h[TopluSutun.Bakiye]));
        Assert.True(TopluBaslik.BankaDokumu(h));

        var ba = TopluBaslik.Coz(["Tarih", "İşlem Açıklaması", "Borç", "Alacak"])!;
        Assert.Equal((1, 2, 3), (ba[TopluSutun.Cari], ba[TopluSutun.Borc], ba[TopluSutun.Alacak]));
        Assert.NotNull(TopluBaslik.Coz(["Açıklama", "Borç"]));      // tarihsiz ama borçlu başlık
        Assert.False(TopluBaslik.BankaDokumu(TopluBaslik.Coz(["Tarih", "Cari", "Tutar"])!));
    }

    [Theory]
    [InlineData("1.500,00", "", "-1.500,00", null)]
    [InlineData("-1.500,00", "", "-1.500,00", null)]                  // borç sütunu zaten eksi yazılmış
    [InlineData("", "2.000", "2.000,00", null)]
    [InlineData("0,00", "2.000", "2.000,00", null)]                   // sıfır dolu borç yok sayılır
    [InlineData("10", "20", "-10,00", TopluSatir.BorcAlacakBirlikteMesaji)]
    [InlineData("", "", "", null)]
    [InlineData("abc", "", "abc", null)]
    public void Borc_alacak_tek_isaretli_tutara_cevrilir(string borc, string alacak, string tutar, string? yapi)
    {
        var harita = TopluBaslik.Coz(["Tarih", "Açıklama", "Borç", "Alacak"])!;
        var s = TopluSatir.Olustur(["01.09.2026", "Market", borc, alacak], harita, ';', 4);
        Assert.Equal(tutar, s.TutarMetni);
        Assert.Equal(yapi, s.YapiHatasi);
    }

    [Fact]
    public async Task Banka_dokumu_basliktan_oncekileri_atlar_alacaklari_ayirir_yeni_cari_eklemez()
    {
        var (api, vm, _) = await KurAsync();
        vm.SecVarsayilanKanalCommand.Execute(vm.KanalCipleri.Single(c => c.Ad == "MEZAT"));
        Assert.True(vm.YeniCarileriEkle);
        vm.Yapistirilan = "HESAP HAREKETLERİ;;;;\n"
                          + "Hesap No: 123456;;;;\n"
                          + "Tarih;Açıklama;Borç;Alacak;Bakiye\n"
                          + "01.09.2026;Market;1.500,00;;8.500,00\n"
                          + "02.09.2026;EFT ABC TİC;;2.000,00;10.500,00\n"
                          + "03.09.2026;Yeni Tedarikçi;300,00;;10.200,00\n";

        Assert.Equal(3, vm.Satirlar.Count);                         // başlık öncesi iki satır atlandı
        Assert.False(vm.YeniCarileriEkle);                          // açıklamadan cari: kendiliğinden kapandı
        Assert.Contains(TopluGirisViewModel.AtlananSatirNotu(2), vm.KaynakNotu);
        Assert.Contains(TopluGirisViewModel.BankaDokumuNotu, vm.KaynakNotu);
        Assert.Contains(TopluGirisViewModel.YeniCariKapaliNotu, vm.KaynakNotu);
        Assert.True(vm.IsaretAnlamli);

        Assert.True(vm.Satirlar[0].Gecerli);
        Assert.Equal(1_500m, vm.Satirlar[0].Tutar);
        Assert.True(vm.Satirlar[1].GiderDegil);                     // alacak: gider değil
        Assert.Equal("'Yeni Tedarikçi' adında bir cari yok.", vm.Satirlar[2].Hata);

        vm.Satirlar[2].Cari = "Market";                             // kayıtlı cariyle düzeltildi
        vm.GiderOlmayanlariKaldirCommand.Execute(null);
        Assert.True(vm.IsaretAnlamli);                              // banka dökümü: işaret hep anlamlı
        await vm.HepsiniKaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        var (satirlar, yeni) = api.SonToplu!.Value;
        Assert.False(yeni);
        Assert.Equal([(1_500m, "Market"), (300m, "Market")], satirlar.Select(g => (g.TutarTl, g.Cari)));
    }

    [Fact]
    public async Task Yalniz_alacakli_banka_dokumu_gider_saymaz()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "Tarih\tAçıklama\tİşlem Tutarı\tBakiye\tKanal\n01.09.2026\tMarket\t2.000,00\t9.000,00\tMEZAT\n";
        Assert.True(vm.IsaretAnlamli);                              // bakiye sütunu: banka dökümü
        Assert.True(vm.Satirlar[0].GiderDegil);
        Assert.False(vm.Kaydedilebilir);
    }

    [Fact]
    public async Task Aciklamadan_cari_yeni_cari_eklemeyi_kapatir_kullanici_acarsa_sonraki_yapistirmada_acik_kalir()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "Tarih\tAçıklama\tTutar\tKanal\n01.09.2026\tYeni Firma\t10\tMEZAT\n";
        Assert.False(vm.YeniCarileriEkle);
        Assert.Equal("'Yeni Firma' adında bir cari yok.", vm.Satirlar[0].Hata);
        Assert.Contains(TopluGirisViewModel.AciklamaCariNotu, vm.KaynakNotu);

        vm.YeniCarileriEkle = true;                                 // kullanıcı bilerek açtı
        Assert.True(vm.Satirlar[0].YeniCari);
        vm.Yapistirilan += "02.09.2026\tMarket\t5\tMEZAT\n";        // aynı kaynak türü: seçim korunur
        Assert.True(vm.YeniCarileriEkle);
        Assert.DoesNotContain(TopluGirisViewModel.YeniCariKapaliNotu, vm.KaynakNotu);

        vm.Yapistirilan = "Tarih\tCari\tTutar\tKanal\n01.09.2026\tYeni Firma\t10\tMEZAT\n";
        Assert.True(vm.YeniCarileriEkle);
        Assert.Null(vm.KaynakNotu);
    }

    [Fact]
    public void Kayitli_olmayan_cari_hatasi_benzer_adi_soyler()
    {
        var s = new TopluSatir { TarihMetni = "01.09.2026", Cari = "Markett", TutarMetni = "10", Kanal = "MEZAT" };
        TopluDogrulama.Dogrula(s, new TopluBaglam([Mezat], ["Market"], [], [], new DateOnly(2026, 9, 24), false, null));
        Assert.Equal("'Markett' adında bir cari yok. Benzer: Market.", s.Hata);
    }

    // ---------- Virgüllü CSV: bölünen kuruş ve fazla sütun ----------

    [Fact]
    public async Task Virgullu_csvde_tirnaksiz_kurus_fazla_sutun_olarak_yakalanir()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "Tarih,Cari,Kanal,Tutar\r\n01.09.2026,Market,MEZAT,1.500,50\r\n02.09.2026,Market,MEZAT,\"1.500,50\"\r\n";
        Assert.Equal(TopluSatir.FazlaSutunMesaji, vm.Satirlar[0].YapiHatasi);
        Assert.StartsWith(TopluSatir.FazlaSutunMesaji, vm.Satirlar[0].Hata);
        Assert.False(vm.Satirlar[0].Gecerli);
        Assert.True(vm.Satirlar[1].Gecerli);                        // tırnaklı tutar doğru okunur
        Assert.Equal(1_500.50m, vm.Satirlar[1].Tutar);
        Assert.False(vm.Kaydedilebilir);

        vm.Satirlar[0].TutarMetni = "1.500,50";                     // kullanıcı tutarı düzeltti
        Assert.Null(vm.Satirlar[0].YapiHatasi);
        Assert.True(vm.Satirlar[0].Gecerli);
        Assert.True(vm.Kaydedilebilir);
    }

    [Fact]
    public async Task Virgullu_csvde_kurus_yan_sutuna_duserse_not_sutunu_olsa_da_yakalanir()
    {
        var (_, vm, _) = await KurAsync();
        vm.Yapistirilan = "Tarih,Cari,Kanal,Tutar,Not\r\n01.09.2026,Market,MEZAT,1.500,50,\r\n02.09.2026,Market,MEZAT,1.500,fiş 12\r\n";
        Assert.Equal(TopluSatir.KurusAyrildiMesaji("1.500", "50"), vm.Satirlar[0].YapiHatasi);
        Assert.True(vm.Satirlar[1].Gecerli);                        // not metin: kuruş değil

        foreach (var c in vm.BolmeKanallari) vm.SecBolmeKanalCommand.Execute(c);
        vm.BolCommand.Execute(vm.Satirlar[0]);                      // yapı hatalı satır bölünmez
        Assert.StartsWith("1. satır bölünemedi", vm.Hata);
        Assert.Equal(2, vm.Satirlar.Count);
    }

    [Theory]
    [InlineData("01.09.2026,Market,1,500.50,MEZAT")]                 // İngilizce binlik: 1 TL olmasın
    [InlineData("01.09.2026,Market,1.500,50,MEZAT")]                 // başlıksız, Türkçe ondalık
    public void Basliksiz_virgullu_satirda_kurus_yakalanir(string satir)
    {
        var hucreler = TopluMetin.Ayristir(satir)[0];
        var s = TopluSatir.Olustur(hucreler, TopluBaslik.Varsayilan, ',');
        Assert.StartsWith("Tutarın kuruşu yan sütuna düşmüş olabilir", s.YapiHatasi);
    }

    [Fact]
    public void Noktali_virgul_ve_sekme_ayiricida_yapi_denetimi_yok_bakiye_yanindaki_sayi_kurus_sayilmaz()
    {
        var h = TopluBaslik.Coz(["Tarih", "Cari", "Tutar", "Bakiye"])!;
        Assert.Null(TopluSatir.Olustur(["01.09.2026", "Market", "-150", "50"], h, ',', 4).YapiHatasi);
        Assert.Null(TopluSatir.Olustur(["01.09.2026", "Market", "1.500", "50", "x", "y"], TopluBaslik.Varsayilan, ';').YapiHatasi);
        Assert.Null(TopluSatir.Olustur(["01.09.2026", "Market", "1.500", "50"], TopluBaslik.Varsayilan, '\t').YapiHatasi);
    }

    [Theory]
    [InlineData("HESAP EKSTRESİ\nTarih;Tutar\n01.09.2026;5", ';')]     // tek hücreli başlık öncesi satır
    [InlineData("Tarih,Cari,Tutar\n01.09.2026,\"ABC; Ltd\",5", ',')]  // tırnak içindeki ; sayılmaz
    [InlineData("a,b\n\"x\ty\",c", ',')]
    public void Ayirici_ilk_satirlara_tirnak_disinda_bakar(string metin, char beklenen)
        => Assert.Equal(beklenen, TopluMetin.AyiriciBul(metin));

    [Fact]
    public void Tsv_yazimi_ayristirmayla_geri_okunur()
    {
        IReadOnlyList<string>[] tablo = [["Tarih", "Açıklama"], ["01.09.2026", "sekme\tiçi"], ["02.09.2026", "\"tırnaklı\" ad"], ["03.09.2026", "satır\nsonu"]];
        var geri = TopluMetin.Ayristir(TopluMetin.TsvYaz(tablo));
        Assert.Equal(tablo.Select(s => string.Join("|", s)), geri.Select(s => string.Join("|", s)));
    }

    // ---------- xlsx ----------

    [Fact]
    public void Xlsx_ilk_sayfa_paylasilan_ve_satir_ici_metin_sayi_tarih_ve_bosluklu_sutunlar()
    {
        var xlsx = Xlsx();
        Assert.True(TopluXlsx.XlsxMi(xlsx));
        var t = TopluXlsx.Oku(xlsx);
        Assert.Equal(4, t.Count);                                   // boş satır atlanır
        Assert.Equal(["Tarih", "Açıklama", "Tutar", "Kanal"], t[0]);
        Assert.Equal(["01.09.2026", "Market", "-1500,5", "MEZAT"], t[1]);
        Assert.Equal(["02.09.2026", "Zengin metin", "0,3", "TOPTAN"], t[2]);   // fonetik atıldı, kayan nokta artığı yok
        Assert.Equal(["", "", "", "", "sağda"], t[3]);              // A–D boş, E dolu
    }

    [Fact]
    public async Task Xlsx_dosyasi_secilince_onizlemeye_dokulur()
    {
        var (_, vm, secici) = await KurAsync();
        secici.Dosya = new SecilenDosya("ekstre.xlsx", Xlsx());
        await vm.DosyaSecCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal("ekstre.xlsx", vm.DosyaAdi);
        Assert.Equal(3, vm.Satirlar.Count);
        Assert.Equal((new DateOnly(2026, 9, 1), 1_500.5m, "MEZAT"), (vm.Satirlar[0].Tarih!.Value, vm.Satirlar[0].Tutar!.Value, vm.Satirlar[0].KanalAdi));
        Assert.True(vm.Satirlar[1].GiderDegil);                     // 0,3 artı: iki işaretli tablo
    }

    [Fact]
    public async Task Bozuk_xlsx_ve_eski_xls_anlasilir_hata_verir()
    {
        var (_, vm, secici) = await KurAsync();
        secici.Dosya = new SecilenDosya("bozuk.xlsx", [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5]);
        await vm.DosyaSecCommand.ExecuteAsync(null);
        Assert.Equal(TopluGirisViewModel.XlsxOkunamadiMesaji, vm.Hata);
        Assert.Null(vm.DosyaAdi);

        secici.Dosya = new SecilenDosya("eski.xls", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]);
        await vm.DosyaSecCommand.ExecuteAsync(null);
        Assert.Equal(TopluGirisViewModel.EskiXlsMesaji, vm.Hata);

        var sayfasiz = XlsxParcalar(("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"/>"));
        secici.Dosya = new SecilenDosya("bos.xlsx", sayfasiz);
        await vm.DosyaSecCommand.ExecuteAsync(null);
        Assert.Equal(TopluGirisViewModel.XlsxOkunamadiMesaji, vm.Hata);
    }

    [Theory]
    [InlineData(14, null, true)]
    [InlineData(22, null, true)]
    [InlineData(0, null, false)]
    [InlineData(4, null, false)]
    [InlineData(20, null, false)]                                   // h:mm
    [InlineData(164, "dd\\.mm\\.yyyy", true)]
    [InlineData(165, "[$-41F]d mmmm yyyy", true)]
    [InlineData(166, "#,##0.00 \"TL\"", false)]
    [InlineData(167, "[h]:mm:ss", false)]
    [InlineData(168, "[Red]#,##0.00", false)]
    public void Tarih_bicimi_tanima(int id, string? kod, bool tarih) => Assert.Equal(tarih, TopluXlsx.TarihBicimi(id, kod));

    [Fact]
    public void Seri_gun_tarihe_1904_sistemi_ve_sutun_adlari()
    {
        Assert.Equal(new DateOnly(2026, 9, 24), TopluXlsx.Tarih(46289));
        Assert.Equal(new DateOnly(2026, 9, 24), TopluXlsx.Tarih(46289 - 1462, tarih1904: true));
        Assert.Null(TopluXlsx.Tarih(-5));
        Assert.Equal(0, TopluXlsx.SutunNo("A1"));
        Assert.Equal(27, TopluXlsx.SutunNo("AB12"));
        Assert.Null(TopluXlsx.SutunNo("12"));
    }

    /// <summary>Küçük bir xlsx: paylaşılan metin (zengin + fonetik), satır içi metin, tarih stili, formül, boşluklu sütun.</summary>
    private static byte[] Xlsx() => XlsxParcalar(
        ("xl/workbook.xml",
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + "<sheets><sheet name=\"Ekstre\" sheetId=\"1\" r:id=\"rId7\"/><sheet name=\"İkinci\" sheetId=\"2\" r:id=\"rId8\"/></sheets></workbook>"),
        ("xl/_rels/workbook.xml.rels",
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId8\" Type=\"worksheet\" Target=\"worksheets/sheet1.xml\"/>"
            + "<Relationship Id=\"rId7\" Type=\"worksheet\" Target=\"/xl/worksheets/sheet2.xml\"/></Relationships>"),
        ("xl/sharedStrings.xml",
            "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + "<si><t>Tarih</t></si><si><t>Açıklama</t></si><si><t>Tutar</t></si><si><t>Kanal</t></si>"
            + "<si><t>Market</t></si><si><t>MEZAT</t></si>"
            + "<si><r><t>Zengin</t></r><r><rPr><b/></rPr><t xml:space=\"preserve\"> metin</t></r><rPh sb=\"0\" eb=\"1\"><t>FONETİK</t></rPh></si>"
            + "</sst>"),
        ("xl/styles.xml",
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
            + "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"dd\\.mm\\.yyyy\"/></numFmts>"
            + "<cellXfs count=\"3\"><xf numFmtId=\"0\"/><xf numFmtId=\"164\"/><xf numFmtId=\"4\"/></cellXfs></styleSheet>"),
        ("xl/worksheets/sheet1.xml",   // ikinci sayfa: okunmamalı
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>YANLIŞ SAYFA</t></is></c></row></sheetData></worksheet>"),
        ("xl/worksheets/sheet2.xml",
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"
            + "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"C1\" t=\"s\"><v>2</v></c><c r=\"D1\" t=\"s\"><v>3</v></c></row>"
            + "<row r=\"2\"><c r=\"A2\" s=\"1\"><v>46266</v></c><c r=\"B2\" t=\"s\"><v>4</v></c><c r=\"C2\" s=\"2\"><v>-1500.5</v></c><c r=\"D2\" t=\"s\"><v>5</v></c></row>"
            + "<row r=\"3\"/>"
            + "<row r=\"4\"><c r=\"A4\" s=\"1\"><f>A2+1</f><v>46267</v></c><c r=\"B4\" t=\"s\"><v>6</v></c><c r=\"C4\"><f>0.1+0.2</f><v>0.30000000000000004</v></c>"
            + "<c r=\"D4\" t=\"inlineStr\"><is><t>TOPTAN</t></is></c><c r=\"E4\"/></row>"
            + "<row r=\"5\"><c r=\"A5\" t=\"s\"/><c r=\"E5\" t=\"str\"><f>\"sa\"&amp;\"ğda\"</f><v>sağda</v></c><c r=\"BZ5\"><v>9</v></c></row>"
            + "</sheetData></worksheet>"));

    private static byte[] XlsxParcalar(params (string Ad, string Xml)[] parcalar)
    {
        using var bellek = new MemoryStream();
        using (var zip = new ZipArchive(bellek, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (ad, xml) in parcalar)
            {
                using var w = new StreamWriter(zip.CreateEntry(ad).Open(), new UTF8Encoding(false));
                w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml);
            }
        return bellek.ToArray();
    }
}
