using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket C yardımcıları: klavye kısayol eşlemesi, cari benzerliği, silme onayı.</summary>
public class HizliGirisYardimciTests
{
    // ---------- 26: KlavyeKisayollari ----------

    [Theory]
    [InlineData("Enter", false, false, KisayolEylemi.Kaydet)]
    [InlineData("S", true, false, KisayolEylemi.Kaydet)]
    [InlineData("s", true, false, KisayolEylemi.Kaydet)]
    [InlineData("N", true, false, KisayolEylemi.YeniSatir)]
    [InlineData("Escape", false, false, KisayolEylemi.Vazgec)]
    [InlineData("Esc", false, false, KisayolEylemi.Vazgec)]
    [InlineData("Number1", false, true, KisayolEylemi.Kanal1)]
    [InlineData("NumberPad2", false, true, KisayolEylemi.Kanal2)]
    [InlineData("D3", false, true, KisayolEylemi.Kanal3)]
    [InlineData("4", false, true, KisayolEylemi.Kanal4)]
    [InlineData("S", false, false, KisayolEylemi.Yok)]       // Ctrl'siz S yazıdır
    [InlineData("N", false, true, KisayolEylemi.Yok)]
    [InlineData("1", true, false, KisayolEylemi.Yok)]        // Ctrl+1 kanal değil
    [InlineData("5", false, true, KisayolEylemi.Yok)]
    [InlineData("", false, false, KisayolEylemi.Yok)]
    public void Kisayol_eslemesi(string tus, bool ctrl, bool alt, KisayolEylemi beklenen)
        => Assert.Equal(beklenen, KlavyeKisayollari.Coz(tus, ctrl, alt));

    [Fact]
    public void Shift_ile_kisayol_calismaz_kanal_sirasi_sifir_tabanli()
    {
        Assert.Equal(KisayolEylemi.Yok, KlavyeKisayollari.Coz("S", ctrl: true, alt: false, shift: true));
        Assert.Equal(0, KlavyeKisayollari.KanalSirasi(KisayolEylemi.Kanal1));
        Assert.Equal(3, KlavyeKisayollari.KanalSirasi(KisayolEylemi.Kanal4));
        Assert.Null(KlavyeKisayollari.KanalSirasi(KisayolEylemi.Kaydet));
        Assert.Equal(8, KlavyeKisayollari.Liste.Count);
    }

    [Theory]
    [InlineData("B", "2026-09-24")]
    [InlineData("b", "2026-09-24")]
    [InlineData("D", "2026-09-23")]
    [InlineData("d", "2026-09-23")]
    public void Tarih_kutusunda_B_bugun_D_dun(string tus, string beklenen)
        => Assert.Equal(DateOnly.Parse(beklenen), KlavyeKisayollari.TarihTusu(tus, new DateOnly(2026, 9, 24)));

    [Fact]
    public void Tarih_kutusunda_diger_tuslar_yok_sayilir_ay_basinda_dun_onceki_ay()
    {
        Assert.Null(KlavyeKisayollari.TarihTusu("X", new DateOnly(2026, 9, 24)));
        Assert.Null(KlavyeKisayollari.TarihTusu(null, new DateOnly(2026, 9, 24)));
        Assert.Equal(new DateOnly(2026, 2, 28), KlavyeKisayollari.TarihTusu("D", new DateOnly(2026, 3, 1)));
    }

    // ---------- 27: CariBenzerlik ----------

    [Theory]
    [InlineData("ABC Ltd. Şti.", "abc")]
    [InlineData("  İNCİ   Gıda  A.Ş. ", "inci gida")]
    [InlineData("IŞIK Tic. San. Ltd.Şti", "isik")]
    [InlineData("Çağrı-Öz/Ünal & Co", "cagri oz unal")]
    [InlineData("Ltd. Şti.", "ltd sti")]     // yalnız ekten oluşan ad kendisi kalır
    [InlineData("", "")]
    public void Normallestirme_turkce_harfleri_noktalamayi_ve_ekleri_kaldirir(string ad, string beklenen)
        => Assert.Equal(beklenen, CariBenzerlik.Normallestir(ad));

    [Theory]
    [InlineData("kitap", "kitap", 0)]
    [InlineData("kitap", "ktiap", 1)]   // bitişik yer değiştirme
    [InlineData("kitap", "kitapp", 1)]
    [InlineData("kitap", "kira", 2)]
    [InlineData("", "abc", 3)]
    public void Damerau_Levenshtein_mesafesi(string a, string b, int beklenen)
        => Assert.Equal(beklenen, CariBenzerlik.Mesafe(a, b));

    [Fact]
    public void Benzer_adlar_en_benzer_once_gelir_birebir_ayni_ad_listeye_girmez()
    {
        string[] kayitli = ["ABC Ltd. Şti.", "ABD Ticaret", "Market", "Mehmet Yılmaz", "Zeta Gıda"];
        Assert.Equal(["ABC Ltd. Şti."], CariBenzerlik.Benzerler("abc ltd", kayitli));
        Assert.Equal(["Mehmet Yılmaz"], CariBenzerlik.Benzerler("MEHMET YILMAZ LTD", kayitli));
        Assert.Equal(["Mehmet Yılmaz"], CariBenzerlik.Benzerler("Mehmet Yilmaz", kayitli));
        Assert.Equal(["Market"], CariBenzerlik.Benzerler("Makret", kayitli));
        Assert.Empty(CariBenzerlik.Benzerler("Tamamen Farklı Firma", kayitli));
        Assert.Empty(CariBenzerlik.Benzerler("market", kayitli));        // zaten kayıtlı (harf farkı)
        Assert.Empty(CariBenzerlik.Benzerler("  ", kayitli));
        // "ABC" kısa: bir harf farkı benzer sayılmaz (ABD ≠ ABC).
        Assert.DoesNotContain("ABD Ticaret", CariBenzerlik.Benzerler("abc", kayitli));
    }

    [Fact]
    public void Benzerler_en_fazla_uc_tane_ve_kelime_icerme()
    {
        string[] kayitli = ["Demir Yapı", "Demir Yapi Ltd", "DEMİR YAPI A.Ş.", "Demir Yapı Market", "Başka"];
        var b = CariBenzerlik.Benzerler("Demir Yapı Ltd Şti", kayitli);
        Assert.Equal(3, b.Count);
        Assert.DoesNotContain("Başka", b);
        Assert.Contains("Gürsoy Otomotiv", CariBenzerlik.Benzerler("Gürsoy", ["Gürsoy Otomotiv"]));
    }

    // ---------- 32: SilmeOnayi ----------

    private static (SilmeOnayi s, SahteApi api, SabitSaat saat) Onay()
    {
        var api = new SahteApi();
        var saat = new SabitSaat(new DateTime(2026, 9, 24, 10, 0, 0));
        return (new SilmeOnayi(api, saat), api, saat);
    }

    [Fact]
    public void Ilk_basis_onay_ister_ikinci_basis_sure_icinde_onaylar()
    {
        var (s, _, saat) = Onay();
        var kayit = new IslemDto(5, new DateOnly(2026, 9, 1), "x", 1m, "MEZAT", GiderTipi.Cari, null);
        Assert.False(s.OnayIste(kayit, geriAlinabilir: true));
        Assert.Equal(kayit, s.Bekleyen);
        Assert.True(s.OnayBekliyor);
        Assert.Equal(SilmeOnayi.OnayDugmesiGeriAlinir, s.OnayDugmesi);
        saat.Ilerle(TimeSpan.FromSeconds(4));
        Assert.True(s.OnayIste(kayit with { }, geriAlinabilir: true));   // aynı kayıt (değer eşitliği)
        Assert.Null(s.Bekleyen);
        Assert.Null(s.OnayMetni);
    }

    [Fact]
    public void Sure_gecince_ya_da_baska_kayda_basinca_onay_yeniden_istenir()
    {
        var (s, _, saat) = Onay();
        Assert.False(s.OnayIste("a", true));
        saat.Ilerle(TimeSpan.FromSeconds(6));
        Assert.False(s.OnayIste("a", true));   // süre doldu: yeniden bekleyen
        Assert.False(s.OnayIste("b", true));   // başka kayıt: onu bekler
        Assert.Equal("b", s.Bekleyen);
        Assert.False(s.OnayIste("a", true));
        s.Vazgec();
        Assert.False(s.OnayBekliyor);
        Assert.False(s.OnayIste("a", true));
    }

    [Fact]
    public void Geri_alinamayan_silmede_onay_metni_bunu_soyler()
    {
        var (s, _, _) = Onay();
        s.OnayIste("kanal", geriAlinabilir: false);
        Assert.Equal(SilmeOnayi.OnayDugmesiGeriAlinmaz, s.OnayDugmesi);
        Assert.Contains("geri alınamaz", s.OnayMetni);
        s.OnayIste("islem", geriAlinabilir: true);
        Assert.Equal(SilmeOnayi.OnayDugmesiGeriAlinir, s.OnayDugmesi);
        Assert.Contains("30 gün", s.OnayMetni);
    }

    [Fact]
    public void Sil_dugmesi_metni_yalniz_bekleyen_satirda_onay_olur_gorunurluk_bildirilir()
    {
        var (s, _, _) = Onay();
        var a = new CariDto(1, "A", true);
        var b = new CariDto(2, "B", true);
        Assert.Equal("Sil", SilmeOnayi.DugmeMetni(a, s.Bekleyen, s.OnayDugmesi));
        Assert.False(s.Gorunur);

        var degisen = new List<string?>();
        s.PropertyChanged += (_, e) => degisen.Add(e.PropertyName);
        s.OnayIste(a, geriAlinabilir: false);
        Assert.Contains(nameof(SilmeOnayi.Gorunur), degisen);
        Assert.True(s.Gorunur);
        Assert.Equal(SilmeOnayi.OnayDugmesiGeriAlinmaz, SilmeOnayi.DugmeMetni(a with { }, s.Bekleyen, s.OnayDugmesi));
        Assert.Equal("Sil", SilmeOnayi.DugmeMetni(b, s.Bekleyen, s.OnayDugmesi));
        Assert.Equal("Kaldır", SilmeOnayi.DugmeMetni(b, s.Bekleyen, s.OnayDugmesi, "Kaldır"));
        Assert.Equal("Sil", SilmeOnayi.DugmeMetni(null, null, s.OnayDugmesi));
        s.Vazgec();
        Assert.False(s.Gorunur);
    }

    [Fact]
    public async Task Silindikten_sonra_serit_son_silme_satirindan_kurulur_geri_al_cagrilir()
    {
        var (s, api, _) = Onay();
        await s.SilindiAsync(GecmisTurAdlari.Islem, 42, "1 Eyl · x · 1,00 ₺");
        Assert.Equal((GecmisTurAdlari.Islem, 42), api.SonSilmeCagrilari.Single());
        Assert.True(s.SeritGorunur);
        Assert.True(s.Gorunur);
        Assert.Equal("Silindi: 1 Eyl · x · 1,00 ₺", s.SeritMetni);

        await s.GeriAlAsync();
        Assert.Equal(942, api.SonGeriAl);
        Assert.False(s.SeritGorunur);
        Assert.Null(s.SeritMetni);
    }

    [Fact]
    public async Task Geri_alinamayan_bulunamayan_ya_da_hatali_aramada_serit_gorunmez()
    {
        var (s, api, _) = Onay();
        api.SonSilmeUret = (_, _) => null;
        await s.SilindiAsync(GecmisTurAdlari.Cari, 1, "x");
        Assert.False(s.SeritGorunur);

        api.SonSilmeUret = (t, id) => new DegisiklikDto(3, DateTime.UtcNow, "editor", t, id, "Silindi", "", "{}", null, true, null, false);
        await s.SilindiAsync(GecmisTurAdlari.Cari, 1, "x");
        Assert.False(s.SeritGorunur);

        api.SonSilmeUret = (_, _) => throw new HttpRequestException("ağ yok");
        await s.SilindiAsync(GecmisTurAdlari.Cari, 1, "x");
        Assert.False(s.SeritGorunur);

        await s.GeriAlAsync();   // şerit yokken hiçbir şey yapmaz
        Assert.Null(api.SonGeriAl);
    }

    [Fact]
    public async Task Geri_al_hatasi_yukselir_serit_kalir()
    {
        var (s, api, _) = Onay();
        await s.SilindiAsync(GecmisTurAdlari.Cek, 7, "çek");
        api.GeriAlHatasi = new KasaApiException(System.Net.HttpStatusCode.Conflict, "Bu silme zaten geri alındı.");
        await Assert.ThrowsAsync<KasaApiException>(s.GeriAlAsync);
        Assert.True(s.SeritGorunur);
        s.Kapat();
        Assert.False(s.SeritGorunur);
    }
}
