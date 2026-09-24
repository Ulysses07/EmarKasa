using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket B: .html/.zip dosya adı güvenliği, yazdır/ay paketi/Excel'e aktar komutları, İşlemler süzgeci.</summary>
public class PaketBDosyaVeSuzgecTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    [Theory]
    [InlineData("kasa-aylik-rapor-2026-08.html", "kasa-aylik-rapor-2026-08.html")]
    [InlineData("kasa-ay-paketi-2026-08.zip", "kasa-ay-paketi-2026-08.zip")]
    [InlineData("RAPOR.HTML", "RAPOR.html")]
    [InlineData("../../x.html", "x.html")]
    [InlineData(@"..\x.zip", "x.zip")]
    [InlineData("CON.html", "kasa-CON.html")]
    [InlineData("a:b.zip", "a-b.zip")]
    [InlineData("rapor.exe", "rapor.exe.csv")]
    [InlineData("rapor.html.exe", "rapor.html.exe.csv")]
    [InlineData("rapor.htm", "rapor.htm.csv")]
    [InlineData("rapor.csv", "rapor.csv")]
    [InlineData("rapor.html.csv", "rapor.html.csv")]
    [InlineData("rapor.html. .", "rapor.html")]
    [InlineData("", DosyaKayit.VarsayilanAd)]
    [InlineData(null, DosyaKayit.VarsayilanAd)]
    public void Html_ve_zip_korunur_digerleri_csv_olur(string? girdi, string beklenen)
        => Assert.Equal(beklenen, DosyaAktarma.GuvenliAd(girdi));

    [Fact]
    public void Csv_adlarinda_eski_kuralla_ayni()
    {
        foreach (var ad in new[] { "kasa-islemler-2026-09.csv", "a:b*?<>|\".csv", "nul", "satır\nsonu.csv", new string('a', 500) + ".csv" })
            Assert.Equal(DosyaKayit.GuvenliAd(ad), DosyaAktarma.GuvenliAd(ad));
    }

    [Fact]
    public async Task Yazdir_html_kaydeder_ay_paketi_zip_kaydeder()
    {
        var api = new SahteApi { AylikRapor = new AylikRaporDto(2026, 8, new List<KanalAylikDto>()) };
        var k = new SahteKaydedici();
        var vm = new AylikViewModel(api, new SabitSaat(Bugun), k) { Yil = 2026, Ay = 8 };

        await vm.YazdirCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal((2026, 8), api.SonYazdir);
        Assert.Equal("kasa-aylik-rapor-2026-08.html", k.Kaydedilenler[^1].Ad);
        Assert.Equal(SahteKaydedici.Klasor + "kasa-aylik-rapor-2026-08.html", vm.AktarilanDosya);

        await vm.AyPaketiniIndirCommand.ExecuteAsync(null);
        Assert.Equal((2026, 8), api.SonAyPaketi);
        Assert.Equal("kasa-ay-paketi-2026-08.zip", k.Kaydedilenler[^1].Ad);
        Assert.Equal(api.ZipDosyasi.Icerik, k.Kaydedilenler[^1].Icerik);
    }

    [Fact]
    public async Task Yazdir_kaydedici_yoksa_ve_disk_hatasinda_anlasilir_mesaj()
    {
        var api = new SahteApi { AylikRapor = new AylikRaporDto(2026, 8, new List<KanalAylikDto>()) };
        var vm = new AylikViewModel(api, new SabitSaat(Bugun));
        await vm.YazdirCommand.ExecuteAsync(null);
        Assert.Equal(ExcelAktarma.KaydediciYok, vm.Hata);

        var k = new SahteKaydedici { Hata = new IOException("dolu") };
        vm = new AylikViewModel(api, new SabitSaat(Bugun), k);
        await vm.AyPaketiniIndirCommand.ExecuteAsync(null);
        Assert.Equal(ExcelAktarma.KaydedilemediMesaji, vm.Hata);
        Assert.Null(vm.AktarilanDosya);
    }

    [Fact]
    public async Task Cekler_kasa_sayimi_gecmis_excele_aktarir()
    {
        var api = new SahteApi();
        var k = new SahteKaydedici();
        var saat = new SabitSaat(Bugun);

        var cek = new CeklerViewModel(api, saat, k) { FiltreYon = CekYonu.Verilen, FiltreDurum = CekDurumu.Portfoyde };
        await cek.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Null(cek.Hata);
        Assert.Equal((CekYonu.Verilen, CekDurumu.Portfoyde, (DateOnly?)null, (DateOnly?)null), api.SonCekCsv);
        Assert.Equal(SahteKaydedici.Klasor + "kasa-rapor.csv", cek.AktarilanDosya);

        var sayim = new KasaSayimiViewModel(api, saat, k);
        await sayim.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal(1, api.KasaSayimCsvCagri);
        Assert.NotNull(sayim.AktarilanDosya);

        var gecmis = new GecmisViewModel(api, saat, kaydedici: k) { FiltreTur = "Çek" };
        await gecmis.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal("Çek", api.SonGecmisCsvTur);
        Assert.NotNull(gecmis.AktarilanDosya);
        Assert.Equal(3, k.Kaydedilenler.Count);

        // Kaydedicisiz kurucu (eski) anlaşılır hata verir.
        var eski = new CeklerViewModel(api, saat);
        await eski.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal(ExcelAktarma.KaydediciYok, eski.Hata);
    }

    [Fact]
    public async Task Excel_hatasi_sunucu_mesajiyla()
    {
        var api = new SahteApi { CsvHatasi = new KasaApiException(HttpStatusCode.InternalServerError, null) };
        var vm = new GecmisViewModel(api, new SabitSaat(Bugun), kaydedici: new SahteKaydedici());
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal(HataMesaji.SunucuHatasi, vm.Hata);
    }

    // ---------------------------------------------------------------- İşlemler süzgeci (22)

    private static Dictionary<string, object> Sorgu(string rota)
    {
        var q = rota[(rota.IndexOf('?') + 1)..];
        return q.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => (object)p[1]);
    }

    [Fact]
    public void Suzgec_rotasi_gidip_gelir_ozel_karakterli_kanalla()
    {
        var s = new IslemSuzgeci(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "MEZAT & Co / %20 +", IslemTipSuzgeci.SabitGider);
        var rota = s.Rota();
        Assert.StartsWith("//islemler?baslangic=2026-08-01&bitis=2026-08-31&kanal=", rota);
        Assert.EndsWith("&tip=SabitGider", rota);
        Assert.Equal(4, rota.Split('&').Length);                 // kanal içindeki & kodlandı
        Assert.Equal(s, IslemSuzgeci.Coz(Sorgu(rota)));

        // Shell değeri çözülmüş verirse de aynı sonuç (tek kodlama katmanı).
        var cozulmus = Sorgu(new IslemSuzgeci(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9), "PERAKENDE").Rota());
        Assert.Equal(new IslemSuzgeci(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9), "PERAKENDE"), IslemSuzgeci.Coz(cozulmus));
    }

    [Fact]
    public void Suzgec_bozuk_sorguyu_reddeder_bilinmeyen_tipi_yok_sayar()
    {
        Assert.Null(IslemSuzgeci.Coz(null));
        Assert.Null(IslemSuzgeci.Coz(new Dictionary<string, object> { ["baslangic"] = "2026-08-01" }));
        Assert.Null(IslemSuzgeci.Coz(new Dictionary<string, object> { ["baslangic"] = "2026-08-31", ["bitis"] = "2026-08-01" }));
        Assert.Null(IslemSuzgeci.Coz(new Dictionary<string, object> { ["baslangic"] = "01.08.2026", ["bitis"] = "2026-08-31" }));
        var s = IslemSuzgeci.Coz(new Dictionary<string, object> { ["baslangic"] = "2026-08-01", ["bitis"] = "2026-08-31", ["tip"] = "7", ["kanal"] = " " });
        Assert.Equal(new IslemSuzgeci(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), s);
        s = IslemSuzgeci.Coz(new Dictionary<string, object> { ["baslangic"] = "2026-08-01", ["bitis"] = "2026-08-31", ["tip"] = "Yok", ["kanal"] = new string('x', 101) });
        Assert.Null(s!.Tip);
        Assert.Null(s.Kanal);
    }

    [Fact]
    public async Task Islemler_suzgeci_uygular_tipi_sunucuda_suzer_ve_kaldirir()
    {
        var api = new SahteApi
        {
            KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 1, 0m) },
            IslemlerListe = new List<IslemDto>
            {
                new(1, new DateOnly(2026, 8, 5), "Kira", 45_000m, "MEZAT", GiderTipi.SabitGider, null),
                new(2, new DateOnly(2026, 8, 6), "Market", 1_000m, "MEZAT", GiderTipi.Cari, null),
                new(3, new DateOnly(2026, 8, 7), "SGK", 5_000m, "MEZAT", GiderTipi.SabitGider, null),
                // Karta bağlı eski kayıt: kayıtlı tipi sabit gider ama hesapta K.K'dır (sabit gider rakamında yok).
                new(4, new DateOnly(2026, 8, 8), "Sigorta", 700m, "MEZAT", GiderTipi.SabitGider, null, KrediKartiId: 9),
            },
        };
        var k = new SahteKaydedici();
        var vm = new IslemlerViewModel(api, new SabitSaat(Bugun), k);
        await vm.YukleAsync();
        Assert.Equal(new DateOnly(2026, 9, 1), api.SonFiltreBaslangic);   // varsayılan: bu ay
        Assert.Empty(api.TipliSayfaCagrilari);

        await vm.SuzgecUygulaAsync(new IslemSuzgeci(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "MEZAT", IslemTipSuzgeci.SabitGider));
        Assert.Null(vm.Hata);
        Assert.Equal(new DateOnly(2026, 8, 1), api.SonFiltreBaslangic);
        Assert.Equal(new DateOnly(2026, 8, 31), api.SonFiltreBitis);
        Assert.Equal("MEZAT", api.SonFiltreKanal);
        Assert.Equal(IslemTipSuzgeci.SabitGider, api.TipliSayfaCagrilari[^1].Tip);
        Assert.Equal(new[] { 3, 1 }, vm.Islemler.Select(i => i.Id));
        Assert.True(vm.TipSuzgeciVar);
        Assert.Equal("Yalnız sabit gider işlemleri", vm.TipSuzgeciMetni);
        Assert.Equal(50_000m, vm.FiltreToplam);
        Assert.Contains("2 işlem", vm.FiltreOzet);
        Assert.Equal("MEZAT", vm.FiltreKanallari.Single(c => c.Secili).Ad);
        Assert.All(vm.FiltreZamanlar, z => Assert.False(z.Secili));

        // Excel'e aktar listeyle aynı süzgeci (tip dahil) kullanır.
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal(((DateOnly?)new DateOnly(2026, 8, 1), (DateOnly?)new DateOnly(2026, 8, 31), (string?)"MEZAT", IslemTipSuzgeci.SabitGider),
            api.SonTipliIslemCsv!.Value);
        Assert.Null(api.SonIslemCsv);

        // Sayfa yeniden açılınca (OnAppearing → YukleAsync) süzgeç "bu ay"a dönmez.
        await vm.YukleAsync();
        Assert.Equal(new DateOnly(2026, 8, 1), api.SonFiltreBaslangic);
        Assert.Equal(2, vm.Islemler.Count);

        // K.K süzgeci karta bağlı kaydı da getirir (etkin tip).
        await vm.SuzgecUygulaAsync(new IslemSuzgeci(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "MEZAT", IslemTipSuzgeci.KrediKarti));
        Assert.Equal(new[] { 4 }, vm.Islemler.Select(i => i.Id));
        Assert.Equal("Yalnız kredi kartı işlemleri", vm.TipSuzgeciMetni);
        await vm.SuzgecUygulaAsync(new IslemSuzgeci(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "MEZAT", IslemTipSuzgeci.Nakit));
        Assert.Equal(new[] { 3, 2, 1 }, vm.Islemler.Select(i => i.Id));
        Assert.Equal("Yalnız kart dışı işlemleri", vm.TipSuzgeciMetni);

        await vm.TipSuzgeciniKaldirCommand.ExecuteAsync(null);
        Assert.False(vm.TipSuzgeciVar);
        Assert.Equal(4, vm.Islemler.Count);
        Assert.Equal("MEZAT", api.SonFiltreKanal);
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal(((DateOnly?)new DateOnly(2026, 8, 1), (DateOnly?)new DateOnly(2026, 8, 31), (string?)"MEZAT", (string?)null),
            api.SonIslemCsv!.Value);
    }

    [Fact]
    public async Task Tip_suzgecinde_500_ustu_sayfalama_ve_toplam_suzulmus_listeye_gore()
    {
        // 600 sabit gider + 400 Cari: tip süzgeciyle toplam 600 (sunucunun süzülmüş sayısı), en yeni 500 yüklenir.
        var liste = new List<IslemDto>();
        for (int i = 1; i <= 1000; i++)
            liste.Add(new IslemDto(i, new DateOnly(2026, 8, 1).AddDays(i % 31), "C", 1m, "MEZAT", i % 5 < 3 ? GiderTipi.SabitGider : GiderTipi.Cari, null));
        var api = new SahteApi { KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 1, 0m) }, IslemlerListe = liste };
        var vm = new IslemlerViewModel(api, new SabitSaat(Bugun));
        await vm.YukleAsync();
        await vm.SuzgecUygulaAsync(new IslemSuzgeci(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "MEZAT", IslemTipSuzgeci.SabitGider));
        Assert.Equal(600, vm.FiltreToplamKayit);
        Assert.Equal(IslemlerViewModel.SayfaBoyutu, vm.Islemler.Count);
        Assert.All(vm.Islemler, i => Assert.Equal(GiderTipi.SabitGider, i.Tip));
        Assert.True(vm.DahaEskiVar);
        Assert.Contains("600 işlem (en yeni 500 gösteriliyor)", vm.FiltreOzet);
        Assert.Equal((IslemTipSuzgeci.SabitGider, 500, 100), api.TipliSayfaCagrilari[^1]);

        await vm.DahaEskiYukleCommand.ExecuteAsync(null);
        Assert.Equal(600, vm.Islemler.Count);
        Assert.False(vm.DahaEskiVar);
        Assert.Equal((IslemTipSuzgeci.SabitGider, 100, 0), api.TipliSayfaCagrilari[^1]);
        Assert.Contains("600 işlem · toplam", vm.FiltreOzet);
    }

    [Fact]
    public void Suzgec_nakit_tipini_cozer()
    {
        var s = IslemSuzgeci.Coz(new Dictionary<string, object> { ["baslangic"] = "2026-08-01", ["bitis"] = "2026-08-31", ["kanal"] = "Ortak", ["tip"] = "Nakit" });
        Assert.Equal(new IslemSuzgeci(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "Ortak", IslemTipSuzgeci.Nakit), s);
        Assert.Equal("//islemler?baslangic=2026-08-01&bitis=2026-08-31&kanal=Ortak&tip=Nakit", s!.Rota());
        Assert.Equal("Kart dışı", IslemSuzgeci.TipAdi(IslemTipSuzgeci.Nakit));
        // Küçük harf ya da eski adlar yok sayılır (tip süzgeci uygulanmaz).
        Assert.Null(IslemSuzgeci.Coz(new Dictionary<string, object> { ["baslangic"] = "2026-08-01", ["bitis"] = "2026-08-31", ["tip"] = "nakit" })!.Tip);
    }
}
