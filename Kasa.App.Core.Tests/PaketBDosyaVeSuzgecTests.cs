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

        var gecmis = new GecmisViewModel(api, saat, k) { FiltreTur = "Çek" };
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
        var vm = new GecmisViewModel(api, new SabitSaat(Bugun), new SahteKaydedici());
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
        var s = new IslemSuzgeci(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "MEZAT & Co / %20 +", GiderTipi.SabitGider);
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
    public async Task Islemler_suzgeci_uygular_tipi_istemcide_suzer_ve_kaldirir()
    {
        var api = new SahteApi
        {
            KanallarListe = new List<KanalDto> { new(1, "MEZAT", true, 1, 0m) },
            IslemlerListe = new List<IslemDto>
            {
                new(1, new DateOnly(2026, 8, 5), "Kira", 45_000m, "MEZAT", GiderTipi.SabitGider, null),
                new(2, new DateOnly(2026, 8, 6), "Market", 1_000m, "MEZAT", GiderTipi.Cari, null),
                new(3, new DateOnly(2026, 8, 7), "SGK", 5_000m, "MEZAT", GiderTipi.SabitGider, null),
            },
        };
        var vm = new IslemlerViewModel(api, new SabitSaat(Bugun));
        await vm.YukleAsync();
        Assert.Equal(new DateOnly(2026, 9, 1), api.SonFiltreBaslangic);   // varsayılan: bu ay

        await vm.SuzgecUygulaAsync(new IslemSuzgeci(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "MEZAT", GiderTipi.SabitGider));
        Assert.Null(vm.Hata);
        Assert.Equal(new DateOnly(2026, 8, 1), api.SonFiltreBaslangic);
        Assert.Equal(new DateOnly(2026, 8, 31), api.SonFiltreBitis);
        Assert.Equal("MEZAT", api.SonFiltreKanal);
        Assert.Equal(new[] { 3, 1 }, vm.Islemler.Select(i => i.Id));
        Assert.True(vm.TipSuzgeciVar);
        Assert.Equal("Yalnız sabit gider işlemleri", vm.TipSuzgeciMetni);
        Assert.Equal(50_000m, vm.FiltreToplam);
        Assert.Contains("2 işlem", vm.FiltreOzet);
        Assert.Equal("MEZAT", vm.FiltreKanallari.Single(c => c.Secili).Ad);
        Assert.All(vm.FiltreZamanlar, z => Assert.False(z.Secili));

        // Sayfa yeniden açılınca (OnAppearing → YukleAsync) süzgeç "bu ay"a dönmez.
        await vm.YukleAsync();
        Assert.Equal(new DateOnly(2026, 8, 1), api.SonFiltreBaslangic);
        Assert.Equal(2, vm.Islemler.Count);

        await vm.TipSuzgeciniKaldirCommand.ExecuteAsync(null);
        Assert.False(vm.TipSuzgeciVar);
        Assert.Equal(3, vm.Islemler.Count);
        Assert.Equal("MEZAT", api.SonFiltreKanal);
    }
}
