using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

public class KasaKontrolVeAylikGiderTests
{
    private static AuthViewModel Auth(Rol rol = Rol.Editor) => new(new SahteApi()) { AktifRol = rol };
    private static SahteApi Finans() => new() { KanallarListe = new[] { new KanalDto(1, "MEZAT", true, 0, 0), new KanalDto(2, "PERAKENDE", true, 1, 0) } };
    private static async Task<AylikGiderViewModel> Aylik(Fake f, AuthViewModel? auth = null) { var v = new AylikGiderViewModel(f, Finans(), auth ?? Auth()); await v.YukleAsync(); return v; }
    private static void SablonFormu(AylikGiderViewModel v) { v.Ad = "Kira"; v.Tur = v.Turler[0]; v.Tutar = 100; }
    [Fact]
    public async Task Sablon_okumasi_ve_kaydi_odeme_yaratmaz_dagilim_acikca_secilir()
    {
        var f = new Fake();
        var v = await Aylik(f);
        Assert.Empty(f.Odemeler);
        SablonFormu(v);
        await v.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Sablon);
        Assert.Contains("dağılım", v.Hata);
        v.DagilimTuru = v.DagilimTurleri[0];
        await v.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Equal("Genel", f.Sablon!.DagilimTuru);
        Assert.Empty(f.Sablon.Dagilimlar);
        Assert.Empty(f.Odemeler);
        Assert.Contains("oluşturulmadı", v.Mesaj);
    }
    [Fact]
    public async Task Esit_dagilim_secilen_kanallari_sifir_tutarla_sunucuya_gonderir()
    {
        var f = new Fake();
        var v = await Aylik(f);
        SablonFormu(v);
        v.DagilimTuru = v.DagilimTurleri[1];
        v.KanalSecimleri[1].Secili = true;
        await v.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Equal(new KanalPayYaz(2, 0), Assert.Single(f.Sablon!.Dagilimlar));
    }
    [Fact]
    public async Task Ozel_dagilim_toplami_uyusmazsa_kaydetmez()
    {
        var f = new Fake();
        var v = await Aylik(f);
        SablonFormu(v);
        v.DagilimTuru = v.DagilimTurleri[2];
        v.PayEkle();
        v.Paylar[0].Kanal = v.Kanallar[0];
        v.Paylar[0].Tutar = 99;
        await v.SablonKaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Sablon);
        Assert.Contains("toplamı", v.Hata);
    }
    [Fact]
    public async Task Aylik_odeme_onay_ister_ve_ag_hatasinda_ayni_istegi_tekrarlar()
    {
        var f = new Fake { OdemeHata = true };
        var v = await Aylik(f);
        v.OdemeSec(v.Kayitlar[0]);
        await v.OdeCommand.ExecuteAsync(null);
        Assert.Empty(f.Odemeler);
        v.OdemeOnay = true;
        await v.OdeCommand.ExecuteAsync(null);
        f.OdemeHata = false;
        await v.OdeCommand.ExecuteAsync(null);
        Assert.Equal(2, f.Odemeler.Count);
        Assert.Equal(f.Odemeler[0], f.Odemeler[1]);
        Assert.NotEqual(Guid.Empty, f.Odemeler[0].IstekId);
        Assert.Null(v.SeciliOdeme);
    }
    [Fact]
    public async Task Ay_secimi_degistiginde_eski_plan_odenemez()
    {
        var f = new Fake();
        var v = await Aylik(f);
        var satir = v.Kayitlar[0];
        v.AyTarihi = v.AyTarihi.AddMonths(1);
        v.OdemeSec(satir);
        v.OdemeOnay = true;
        await v.OdeCommand.ExecuteAsync(null);
        Assert.Empty(f.Odemeler);
        Assert.Null(v.SeciliOdeme);
        Assert.True(v.AySecimiDegisti);
    }
    [Fact]
    public async Task Gec_donen_baska_ayin_yaniti_yeni_baslik_altinda_gosterilmez()
    {
        var t = new TaskCompletionSource<AylikGiderAyDto>();
        var f = new Fake { BekleyenAy = t.Task };
        var v = new AylikGiderViewModel(f, Finans(), Auth());
        var eski = v.AyTarihi;
        var yukle = v.YukleAsync();
        v.AyTarihi = eski.AddMonths(1);
        t.SetResult(f.Ay(eski.Year, eski.Month));
        await yukle;
        Assert.False(v.VeriHazir);
        Assert.Empty(v.Kayitlar);
    }
    [Fact]
    public async Task Oturum_degisiminde_bekleyen_ay_ve_bakiye_yaniti_yok_sayilir()
    {
        var ay = new TaskCompletionSource<AylikGiderAyDto>();
        var f = new Fake { BekleyenAy = ay.Task };
        var auth = Auth();
        var v = new AylikGiderViewModel(f, Finans(), auth);
        var islem = v.YukleAsync();
        auth.OturumSurumu++;
        ay.SetResult(f.Ay(2026, 9));
        await islem;
        Assert.Empty(v.Kayitlar);
        Assert.False(v.VeriHazir);
        var preview = new TaskCompletionSource<KasaKontrolOnizlemeDto>();
        f.BekleyenKontrol = preview.Task;
        var k = new KasaKontrolViewModel(f, auth) { GercekBakiye = 90 };
        var p = k.OnizleCommand.ExecuteAsync(null);
        auth.OturumSurumu++;
        preview.SetResult(new(100, 90, -10, "eski"));
        await p;
        Assert.Null(k.Karsilastirma);
    }
    [Fact]
    public async Task Gercek_bakiye_degistiginde_onizleme_tekrar_alinmalidir()
    {
        var f = new Fake();
        var v = new KasaKontrolViewModel(f, Auth()) { GercekBakiye = -20, Not = "Sayım" };
        await v.OnizleCommand.ExecuteAsync(null);
        v.GercekBakiye = -21;
        await v.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(f.Kontroller);
        Assert.Contains("önce", v.Hata!.ToLowerInvariant());
        await v.OnizleCommand.ExecuteAsync(null);
        await v.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(-21, Assert.Single(f.Kontroller).GercekBakiye);
        Assert.Equal("hash", f.Kontroller[0].KontrolOzeti);
        Assert.Contains("değiştirilmedi", v.Mesaj);
    }
    [Fact]
    public async Task Bakiye_409_onizlemeyi_gecersizlestirir_ag_hatasi_anahtari_korur()
    {
        // Sıfırdan farklı bakiye: 0 ile kayıt ayrıca ikinci basışta onaylanır (aşağıdaki test).
        var f = new Fake { KontrolHata = new HttpRequestException() };
        var v = new KasaKontrolViewModel(f, Auth()) { GercekBakiye = 50, Not = "Sayım" };
        await v.OnizleCommand.ExecuteAsync(null);
        await v.KaydetCommand.ExecuteAsync(null);
        f.KontrolHata = new KasaApiException(HttpStatusCode.Conflict, "Bakiye değişti");
        await v.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(f.Kontroller[0], f.Kontroller[1]);
        f.KontrolHata = null;
        await v.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, f.Kontroller.Count);
        Assert.Null(v.Karsilastirma);
    }
    [Fact]
    public async Task Gercek_bakiye_sifirla_kayit_ikinci_basista_onaylanir_deger_degisince_sifirlanir()
    {
        var f = new Fake();
        var v = new KasaKontrolViewModel(f, Auth());   // alan boş: 0
        await v.OnizleCommand.ExecuteAsync(null);
        await v.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(f.Kontroller);
        Assert.Contains("0,00 ₺", v.KayitUyarisi);
        Assert.Contains("Onaylamak için yeniden kaydedin", v.KayitUyarisi);

        v.GercekBakiye = 5;
        v.GercekBakiye = 0;
        Assert.Null(v.KayitUyarisi);          // değer değişti: onay sıfırlanır
        await v.OnizleCommand.ExecuteAsync(null);
        await v.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(f.Kontroller);
        v.Not = "Kasa sayıldı";
        Assert.Null(v.KayitUyarisi);                         // diğer alan değişti: onay sıfırlanır
        await v.OnizleCommand.ExecuteAsync(null);
        await v.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(f.Kontroller);

        await v.KaydetCommand.ExecuteAsync(null);
        var kayit = Assert.Single(f.Kontroller);
        Assert.Equal(0, kayit.GercekBakiye);
        Assert.Equal("Kasa sayıldı", kayit.Not);
        Assert.Null(v.KayitUyarisi);
    }
    [Fact]
    public async Task Esik_sifir_degeri_ve_kapali_durum_acikca_kaydedilir()
    {
        var f = new Fake();
        var v = new KasaEsikViewModel(f, Auth());
        await v.YukleAsync();
        v.Secili = v.Kanallar[0];
        v.Tutar = 0;
        v.Etkin = false;
        await v.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(new KasaEsikYaz(2, 0, false), f.Esik);
        var panel = new KasaKontrolViewModel(f, Auth());
        await panel.YukleAsync();
        Assert.Contains("MEZAT", panel.EsikUyarilari);
        Assert.DoesNotContain("PERAKENDE", panel.EsikUyarilari);
    }
    [Fact]
    public async Task Negatif_esik_kaydedilmez()
    {
        var f = new Fake();
        var v = new KasaEsikViewModel(f, Auth());
        await v.YukleAsync();
        v.Secili = v.Kanallar[0];
        v.Tutar = -1;
        await v.KaydetCommand.ExecuteAsync(null);
        Assert.Null(f.Esik);
        Assert.Contains("sıfır veya pozitif", v.Hata);
    }
    [Fact]
    public async Task Basarili_odeme_sonrasi_yenileme_hatasi_eski_plani_guncel_gostermez()
    {
        var f = new Fake();
        var v = await Aylik(f);
        v.OdemeSec(v.Kayitlar[0]);
        v.OdemeOnay = true;
        f.BekleyenAy = Task.FromException<AylikGiderAyDto>(new HttpRequestException());
        await v.OdeCommand.ExecuteAsync(null);
        Assert.Single(f.Odemeler);
        Assert.False(v.VeriHazir);
        Assert.Null(v.SeciliOdeme);
        Assert.Contains("ulaşılamadı", v.Hata);
    }
    [Fact]
    public async Task Izleyici_okuyabilir_ama_yeni_mali_akislara_yazamaz()
    {
        var f = new Fake();
        var auth = Auth(Rol.Izleyici);
        var aylik = await Aylik(f, auth);
        SablonFormu(aylik);
        aylik.DagilimTuru = aylik.DagilimTurleri[0];
        await aylik.SablonKaydetCommand.ExecuteAsync(null);
        var kontrol = new KasaKontrolViewModel(f, auth);
        await kontrol.OnizleCommand.ExecuteAsync(null);
        var esik = new KasaEsikViewModel(f, auth);
        await esik.YukleAsync();
        esik.Secili = esik.Kanallar[0];
        await esik.KaydetCommand.ExecuteAsync(null);
        var kilit = new AyKilidiViewModel(f, auth);
        await kilit.YukleAsync();
        await kilit.DegistirAsync(true, 2025, 1, "Kontrol", kilit.OturumNesli, 3);
        Assert.Null(f.Sablon);
        Assert.Empty(f.Odemeler);
        Assert.Empty(f.Kontroller);
        Assert.Null(f.Esik);
        Assert.Null(f.KilitGirdi);
        Assert.Contains(Bolum.AylikGiderler, SekmeModeli.Bolumler(Rol.Izleyici));
        Assert.DoesNotContain(Bolum.AylikGiderler, SekmeModeli.Bolumler(Rol.Alici));
    }
    [Fact]
    public async Task Ay_kilidi_tamamlanmamis_ayi_kapatmaz_ve_acma_yonunu_korur()
    {
        var f = new Fake();
        var v = new AyKilidiViewModel(f, Auth());
        await v.YukleAsync();
        var bugun = DateTime.Today;
        await v.DegistirAsync(true, bugun.Year, bugun.Month, "Kontrol", v.OturumNesli, 3);
        Assert.Null(f.KilitGirdi);
        await v.DegistirAsync(false, 2025, 12, "Düzeltme", v.OturumNesli, 3);
        Assert.False(f.Kapat);
        Assert.Equal(3, f.KilitGirdi!.Surum);
        Assert.Equal(12, f.KilitGirdi.Ay);
        Assert.Contains("sonraki", v.Mesaj);
    }
    [Fact]
    public async Task Eski_oturumun_acik_iptal_penceresi_yeni_editor_adina_islem_yapmaz()
    {
        var f = new Fake { Odendi = true };
        var auth = Auth();
        var v = await Aylik(f, auth);
        var eskiSatir = v.Kayitlar[0];
        var eskiOturum = v.OturumNesli;
        auth.OturumSurumu++;
        await v.YukleAsync();
        await v.IptalAsync(eskiSatir, "Eski pencere", eskiOturum);
        Assert.Equal(0, f.IptalSayisi);
        var yeniSatir = v.Kayitlar[0];
        v.AyTarihi = v.AyTarihi.AddMonths(1);
        await v.IptalAsync(yeniSatir, "Ay değişti", v.OturumNesli);
        Assert.Equal(0, f.IptalSayisi);
    }
    /// <summary>Ödenmiş satır "Ödendi" yazar ve ödeme formunu açmaz (sayfada iptal penceresini açar); bekleyen satır formu açar.</summary>
    [Fact]
    public async Task Odenmis_aylik_gider_odendi_yazilir_odeme_formu_acilmaz_bekleyen_acilir()
    {
        var odenmis = await Aylik(new Fake { Odendi = true });
        Assert.EndsWith("· Ödendi", odenmis.Kayitlar[0].Baslik);
        odenmis.OdemeSec(odenmis.Kayitlar[0]);
        Assert.Null(odenmis.SeciliOdeme);
        var bekleyen = await Aylik(new Fake());
        Assert.EndsWith("· Ödeme bekliyor", bekleyen.Kayitlar[0].Baslik);
        bekleyen.OdemeSec(bekleyen.Kayitlar[0]);
        Assert.Same(bekleyen.Kayitlar[0], bekleyen.SeciliOdeme);
    }
    [Fact]
    public async Task Yenilenen_listede_artik_olmayan_odeme_iptal_edilemez()
    {
        var f = new Fake { Odendi = true };
        var v = await Aylik(f);
        var eski = v.Kayitlar[0];
        f.Odendi = false;
        await v.YukleAsync();
        await v.IptalAsync(eski, "Eski pencere", v.OturumNesli);
        Assert.Equal(0, f.IptalSayisi);
        Assert.Contains("yenileyip", v.Hata);
    }
    [Fact]
    public async Task Ay_kilidi_onayi_oturuma_ve_gosterilen_surume_baglidir()
    {
        var f = new Fake();
        var auth = Auth();
        var v = new AyKilidiViewModel(f, auth);
        await v.YukleAsync();
        var oturum = v.OturumNesli;
        auth.OturumSurumu++;
        await v.YukleAsync();
        await v.DegistirAsync(false, 2025, 12, "Eski pencere", oturum, 3);
        Assert.Null(f.KilitGirdi);
        f.KilitSurumu = 4;
        await v.YukleAsync();
        await v.DegistirAsync(false, 2025, 12, "Eski sürüm", v.OturumNesli, 3);
        Assert.Null(f.KilitGirdi);
        Assert.Contains("yeniden onaylayın", v.Hata);
    }
    // maui-8: kasa kontrolü sayfasındaki ay kilidi onay metni ve "rapor ayı/oturum değişti mi" koşulu görünüm modelindedir.
    // IST4: kural 1 ile dondurulmuş ay açılırken güncel kuralla yeniden hesaplanacağı söylenir (web frozenRuleUnlockWarning).
    [Fact]
    public void Ay_kilidi_onay_metni_ay_ve_yonu_soyler_kural_1_ile_dondurulmus_ay_acilirken_uyarir()
    {
        var v = new AyKilidiViewModel(new Fake(), Auth());
        var kapat = v.OnayMetni(true, 2026, 3);
        Assert.Contains("03.2026", kapat);
        Assert.Contains("kilitlenecek", kapat);
        var ac = v.OnayMetni(false, 2026, 3);
        Assert.Contains("03.2026", ac);
        Assert.Contains("yeniden açılacak", ac);
        Assert.DoesNotContain("kural 1", ac);

        var eski = new AylikRaporDto(2026, 8, Array.Empty<KanalAylikDto>(), KuralSurumu: 1, Dondurulmus: true);
        var uyari = v.OnayMetni(false, 2026, 8, eski);
        Assert.Contains("eski kuralla (kural 1) kapatılmış", uyari);
        Assert.Contains("yeniden kapatınca da güncel kuralla", uyari);
        Assert.Contains("takipli kredi çekimi Gelen ve Ay sonucundan çıkar", uyari);
        Assert.Contains("kural 1", v.OnayMetni(false, 2026, 8, eski with { KuralSurumu = null }));
        Assert.DoesNotContain("kural 1", v.OnayMetni(false, 2026, 8, eski with { KuralSurumu = 2 }));
        Assert.DoesNotContain("kural 1", v.OnayMetni(false, 2026, 8, eski with { Dondurulmus = false }));
        Assert.DoesNotContain("kural 1", v.OnayMetni(false, 2026, 7, eski));   // başka ayın raporu
        Assert.DoesNotContain("kural 1", v.OnayMetni(true, 2026, 8, eski));    // kapatma onayı
    }
    [Fact]
    public async Task Ay_kilidi_istegi_rapor_ayi_ya_da_oturum_degisince_gecersizdir()
    {
        var auth = Auth();
        var v = new AyKilidiViewModel(new Fake(), auth);
        await v.YukleAsync();
        var oturum = v.OturumNesli;
        Assert.True(v.IstekHalaGecerli(oturum, 2026, 8, 2026, 8));
        Assert.False(v.IstekHalaGecerli(oturum, 2026, 8, 2026, 9));
        Assert.False(v.IstekHalaGecerli(oturum, 2026, 8, 2027, 8));
        auth.OturumSurumu++;
        Assert.False(v.IstekHalaGecerli(oturum, 2026, 8, 2026, 8));
    }
    [Fact]
    public async Task Aylik_gider_gider_editorunden_degistirilemez_ve_genel_gider_raporda_bir_kez_duser()
    {
        var finans = Finans();
        var v = new IslemlerViewModel(finans, TestOturumu.Ac());
        var i = new IslemDto(2, new(2026, 9, 1), "Kira", 100, "", GiderTipi.Cari, null, AylikGiderOdemeId: 8);
        v.Duzenle(i);
        await v.SilCommand.ExecuteAsync(i);
        Assert.Equal(0, v.DuzenId);
        Assert.Null(finans.SonIslemSil);
        Assert.Contains("Aylık Giderler", v.Hata);
        var rapor = new AylikViewModel(finans) { Rapor = new(2026, 9, new[] { new KanalAylikDto("A", 0, 0, 0, 0, 0, 500) }, 20, 100) };
        Assert.Equal(380, rapor.GenelAySonucu);
        Assert.True(rapor.GenelGiderVar);
    }
    [Fact]
    public async Task Kart_masrafi_hash_ile_onizlenir_degisen_form_kaydedilmez_ve_retry_id_sabittir()
    {
        var f = new Fake();
        var kart = new FinansTakipTests.Fake();
        var v = new KartTakipViewModel(kart, Finans(), Auth(), kontrolApi: f);
        await v.YukleAsync();
        v.SecCommand.Execute(v.Kartlar[0]);
        v.MasrafEkstresi = v.MasrafEkstreleri[0];
        v.MasrafTutari = 10;
        v.MasrafAciklama = "Banka faizi";
        await v.MasrafOnizleCommand.ExecuteAsync(null);
        v.MasrafTutari = 11;
        await v.MasrafKaydetCommand.ExecuteAsync(null);
        Assert.Empty(f.Masraflar);
        await v.MasrafOnizleCommand.ExecuteAsync(null);
        f.MasrafHata = true;
        await v.MasrafKaydetCommand.ExecuteAsync(null);
        f.MasrafHata = false;
        await v.MasrafKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, f.Masraflar.Count);
        Assert.Equal(f.Masraflar[0], f.Masraflar[1]);
        Assert.Equal("pay-hash", f.Masraflar[0].DagilimOzeti);
        Assert.NotEmpty(v.MasrafEkstreleri);
        Assert.Equal(0, v.MasrafTutari);
    }
    // gap-denetim-izi-gozlemlenebilirlik-3: fark varken açıklama zorunlu; açıklama özete girmediği için farkı gördükten sonra yazılır.
    [Fact]
    public async Task Fark_varken_aciklama_zorunlu_farki_gorduktan_sonra_yazilabilir()
    {
        var f = new Fake();
        var v = new KasaKontrolViewModel(f, Auth()) { GercekBakiye = 90 };
        await v.OnizleCommand.ExecuteAsync(null);
        Assert.Contains("Kanal kasaları: MEZAT 60,00 ₺ · PERAKENDE 40,00 ₺", v.Karsilastirma);
        Assert.Contains("açıklama zorunludur", v.Karsilastirma);
        await v.KaydetCommand.ExecuteAsync(null);
        Assert.Empty(f.Kontroller);
        Assert.Equal(KasaKontrolViewModel.NotZorunluMesaji, v.Hata);
        v.Not = "  Sayım farkı ";
        await v.KaydetCommand.ExecuteAsync(null);
        var kayit = Assert.Single(f.Kontroller);
        Assert.Equal((90m, "Sayım farkı", "hash"), (kayit.GercekBakiye, kayit.Not, kayit.KontrolOzeti));
        v.GercekBakiye = 100;
        v.Not = "";
        await v.OnizleCommand.ExecuteAsync(null);
        Assert.DoesNotContain("zorunludur", v.Karsilastirma);
        await v.KaydetCommand.ExecuteAsync(null);
        Assert.Equal((100m, ""), (f.Kontroller[1].GercekBakiye, f.Kontroller[1].Not));
    }
    private static KasaKontrolDto Degisen => new(2, new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(3)), 1000, 900, -100, "Sayım", 1, new DateOnly(2026, 9, 20),
        new[] { new KasaKontrolKanalDto(1, "MEZAT", 600, 1100), new KasaKontrolKanalDto(2, "PERAKENDE", 400, 400) }, null, null, 1500, -600, true);
    private static KasaKontrolDto Eski => new(1, new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(3)), 800, 800, 0, null, GuncelSistemBakiye: 800, GuncelFark: 0);
    // gap-coklu-giris-cift-sayim-mutabakat-17: sonradan değişen kontrol güncel sistem bakiyesi ve güncel farkla işaretlenir; eski kayıt belirtilir.
    [Fact]
    public async Task Gecmis_sonradan_degisen_ve_filigransiz_eski_kayitlari_isaretler()
    {
        var f = new Fake { Gecmis = new[] { Degisen, Eski } };
        var v = new KasaKontrolViewModel(f, Auth());
        await v.YukleAsync();
        Assert.EndsWith("fark -100,00 ₺ · sonradan değişti", v.Gecmis[0].Baslik);
        Assert.Contains("Sonradan değişti: güncel sistem 1.500,00 ₺, güncel fark -600,00 ₺", v.Gecmis[0].Ozet);
        Assert.Contains("Kanallar: MEZAT 600,00 ₺ (güncel 1.100,00 ₺) · PERAKENDE 400,00 ₺", v.Gecmis[0].Ozet);
        Assert.DoesNotContain("sonradan", v.Gecmis[1].Baslik);
        Assert.Contains("Kayıttan sonra değişmedi · Eski kayıt, filigran yok", v.Gecmis[1].Ozet);
        Assert.Equal(new[] { "Genel kasa", "MEZAT", "PERAKENDE" }, v.DokumKasalari.Select(k => k.Ad));
    }
    [Fact]
    public async Task Inceleme_kontrolden_beri_degisenleri_verir_fark_aciklamasi_surumle_ve_sabit_anahtarla_kaydedilir()
    {
        var f = new Fake { Gecmis = new[] { Degisen, Eski } };
        f.Sonrasi = new(2, Degisen.Kaydedildi, new DateOnly(2026, 9, 20), true, 1000, 1500, 500,
            [new DenetimOlayDto(9, new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.FromHours(3)), "editor", null, null, "Sil", "Islem", "812", """{"Tarih":"2026-09-18","TutarTl":500,"Iptal":false}""", null, null, null, null, null)],
            [new KasaKontrolIstekDto(Guid.NewGuid(), "KartOdemeIptal", 4)],
            [new KasaHareketiDto(new(2026, 9, 19), new(2026, 9, 19), "Gider", "Unutulan fatura", "MEZAT", 1, -70, -70, "Islem:900", false),
             new KasaHareketiDto(new(2026, 9, 22), new(2026, 9, 22), "KrediTaksidi", "Kredi / 1. taksit", "MEZAT", 1, -1000, -1000, "TakipKrediTaksit:9", true)], false);
        var izleyici = new KasaKontrolViewModel(f, Auth(Rol.Izleyici));
        await izleyici.YukleAsync();
        await izleyici.IncelemeAsync(izleyici.Gecmis[0]);
        Assert.Empty(f.SonrasiIstekleri);
        Assert.Null(izleyici.Secili);

        var v = new KasaKontrolViewModel(f, Auth());
        await v.YukleAsync();
        await v.IncelemeAsync(v.Gecmis[0]);
        Assert.Equal(2, v.Secili!.Veri.Id);
        Assert.Equal(new[] { 2 }, f.SonrasiIstekleri);
        Assert.Contains("geriye dönük değişim +500,00 ₺", v.SonrasiOzeti);
        Assert.Contains("kontrol gününden sonra -1.000,00 ₺", v.SonrasiOzeti);
        Assert.Contains("Mali istekler: KartOdemeIptal #4", v.SonrasiOzeti);
        Assert.EndsWith("Silindi · Gider #812", Assert.Single(v.Degisiklikler).Baslik);
        Assert.Equal("Tarih: 2026-09-18 · TutarTl: 500 · Iptal: Hayır", v.Degisiklikler[0].Ozet);
        Assert.EndsWith("Kontrol gününe ya da öncesine sonradan girildi.", v.SonrasiHareketler[0].Ozet);
        Assert.Equal("22.09.2026 · Kredi taksidi (kendiliğinden) · -1.000,00 ₺", v.SonrasiHareketler[1].Baslik);
        Assert.DoesNotContain("sonradan girildi", v.SonrasiHareketler[1].Ozet);

        // Ağ hatasında aynı istek kimliğiyle yeniden gönderilir; başarıda tutarlar ve güncel durum korunur, sürüm ilerler.
        v.FarkAciklamasi = " Mükerrer gider silindi ";
        f.AciklamaHata = new HttpRequestException();
        await v.AciklaCommand.ExecuteAsync(null);
        f.AciklamaHata = null;
        await v.AciklaCommand.ExecuteAsync(null);
        Assert.Equal(2, f.Aciklamalar.Count);
        Assert.Equal(f.Aciklamalar[0], f.Aciklamalar[1]);
        Assert.NotEqual(Guid.Empty, f.Aciklamalar[0].Girdi.IstekId);
        Assert.Equal((2, 1, "Mükerrer gider silindi"), (f.Aciklamalar[0].Id, f.Aciklamalar[0].Girdi.Surum, f.Aciklamalar[0].Girdi.Aciklama));
        var satir = v.Gecmis[0].Veri;
        Assert.Equal((2, "Mükerrer gider silindi", -100m, true, (decimal?)1500), (satir.Surum, satir.FarkAciklamasi, satir.Fark, satir.SonradanDegisti, satir.GuncelSistemBakiye));
        Assert.Same(v.Gecmis[0], v.Secili);
        Assert.Contains("değişmedi", v.Mesaj);
        v.FarkAciklamasi = " ";
        await v.AciklaCommand.ExecuteAsync(null);
        Assert.Equal(2, f.Aciklamalar.Count);
        Assert.Contains("açıklamasını yazın", v.Hata);
    }
    [Fact]
    public async Task Dokum_secilen_aralik_ve_kasayla_istenir_acilis_kapanis_ve_satirlar_gosterilir()
    {
        var f = new Fake
        {
            Dokum = new(new(2026, 9, 1), new(2026, 9, 26), 1, "MEZAT", 1200, 780.25m,
            [new KasaHareketiDto(new(2026, 9, 30), new(2026, 8, 20), "KartAySonu", "Eski kart", "MEZAT", 1, -400, 0, "Islem:5", true),
             new KasaHareketiDto(new(2026, 9, 2), new(2026, 9, 2), "Gider", "Nakliye", "MEZAT", 1, -419.75m, -419.75m, "Islem:6", false)])
        };
        var v = new KasaKontrolViewModel(f, Auth());
        await v.YukleAsync();
        v.DokumBaslangic = new(2026, 9, 26);
        v.DokumBitis = new(2026, 9, 1);
        await v.DokumGetirCommand.ExecuteAsync(null);
        Assert.Empty(f.DokumIstekleri);
        Assert.Contains("Bitiş tarihi başlangıçtan önce olamaz", v.Hata);
        v.DokumBaslangic = new(2026, 9, 1);
        v.DokumBitis = new(2026, 9, 26);
        v.DokumKasa = v.DokumKasalari[1];
        await v.DokumGetirCommand.ExecuteAsync(null);
        Assert.Equal(((DateOnly?)new DateOnly(2026, 9, 1), (DateOnly?)new DateOnly(2026, 9, 26), (int?)1), Assert.Single(f.DokumIstekleri));
        Assert.Equal("MEZAT · 01.09.2026 – 26.09.2026\nAçılış 1.200,00 ₺ · kapanış 780,25 ₺ · 2 hareket", v.DokumOzeti);
        Assert.Equal("30.09.2026 · Eski kart ay sonu düşümü (kendiliğinden) · +0,00 ₺", v.DokumSatirlari[0].Baslik);
        Assert.Contains("kayıt 20.08.2026", v.DokumSatirlari[0].Ozet);
        Assert.Equal("02.09.2026 · Gider · -419,75 ₺", v.DokumSatirlari[1].Baslik);
    }
    internal sealed class Fake : IKasaKontrolApi, IAylikGiderApi
    {
        public AylikGiderSablonYaz? Sablon; public List<AylikGiderOdemeYaz> Odemeler = new(); public bool OdemeHata; public Task<AylikGiderAyDto>? BekleyenAy; public bool Odendi; public int IptalSayisi; public int KilitSurumu = 3;
        public Task<KasaKontrolOnizlemeDto>? BekleyenKontrol; public List<KasaKontrolYaz> Kontroller = new(); public Exception? KontrolHata; public KasaEsikYaz? Esik; public AyKilidiYaz? KilitGirdi; public bool Kapat;
        public List<KartMasrafYaz> Masraflar = new(); public bool MasrafHata; public int KontrolOnizlemeSayisi, MasrafOnizlemeSayisi;
        public AylikGiderAyDto Ay(int y, int a) => new(y, a, 100, Odendi ? 100 : 0, new[] { new AylikGiderSatirDto(1, 4, "Kira", "Kira", 100, new(y, a, 1), "Genel", Array.Empty<TakipKanalPayi>(), Odendi ? "Odendi" : "Planlandi", Odendi ? 2 : null) });
        public Task<IReadOnlyList<AylikGiderSablonDto>> AylikGiderSablonlariAsync() => Task.FromResult<IReadOnlyList<AylikGiderSablonDto>>(Array.Empty<AylikGiderSablonDto>());
        public Task<AylikGiderSablonDto> AylikGiderSablonKaydetAsync(int? id, AylikGiderSablonYaz g) { Sablon = g; return Task.FromResult(new AylikGiderSablonDto(1, 1, g.Ad, g.Tur, g.Tutar, g.OdemeGunu, g.DagilimTuru, Array.Empty<TakipKanalPayi>(), g.GecerliAy, g.Aktif)); }
        public Task<AylikGiderAyDto> AylikGiderlerAsync(int y, int a) => BekleyenAy ?? Task.FromResult(Ay(y, a));
        public Task<AylikGiderSatirDto> AylikGiderOdeAsync(int id, AylikGiderOdemeYaz g) { Odemeler.Add(g); return OdemeHata ? Task.FromException<AylikGiderSatirDto>(new HttpRequestException()) : Task.FromResult(Ay(g.Yil, g.Ay).Kayitlar[0] with { Durum = "Odendi", OdemeId = 2 }); }
        public Task<AylikGiderSatirDto> AylikGiderIptalAsync(int id, AylikGiderIptalYaz g) { IptalSayisi++; return Task.FromResult(Ay(2026, 9).Kayitlar[0] with { Durum = "Iptal" }); }
        public Task<AyKilidiDto> AyKilidiAsync() => Task.FromResult(new AyKilidiDto(KilitSurumu, null, Array.Empty<AyKilidiOlayDto>()));
        public Task<AyKilidiDto> AyKilidiDegistirAsync(bool kapat, AyKilidiYaz g) { Kapat = kapat; KilitGirdi = g; return AyKilidiAsync(); }
        public Task<IReadOnlyList<KasaEsikDto>> KasaEsikleriAsync() => Task.FromResult<IReadOnlyList<KasaEsikDto>>(new[] { new KasaEsikDto(1, "MEZAT", 2, 10, true, -20, true), new KasaEsikDto(2, "PERAKENDE", 0, 100, false, 0, true) });
        public Task<KasaEsikDto> KasaEsigiKaydetAsync(int id, KasaEsikYaz g) { Esik = g; return Task.FromResult(new KasaEsikDto(id, "MEZAT", 3, g.Tutar, g.Etkin, -20, true)); }
        // Kasa kontrolü filigranı, fark açıklaması, "kontrolden beri değişenler" ve hareket dökümü (gap-denetim-izi-gozlemlenebilirlik-3).
        public IReadOnlyList<KasaKontrolDto> Gecmis = Array.Empty<KasaKontrolDto>(); public List<(int Id, KasaKontrolAciklamaYaz Girdi)> Aciklamalar = new(); public Exception? AciklamaHata { get; set; }
        public KasaKontrolSonrasiDto? Sonrasi { get; set; }
        public List<int> SonrasiIstekleri = new(); public KasaHareketleriDto? Dokum { get; set; }
        public List<(DateOnly? Baslangic, DateOnly? Bitis, int? KanalId)> DokumIstekleri = new();
        public Task<IReadOnlyList<KasaKontrolDto>> KasaKontrolleriAsync() => Task.FromResult(Gecmis);
        public Task<KasaKontrolOnizlemeDto> KasaKontrolOnizleAsync(KasaKontrolOnizle g)
        {
            KontrolOnizlemeSayisi++;
            return BekleyenKontrol ?? Task.FromResult(new KasaKontrolOnizlemeDto(100, g.GercekBakiye, g.GercekBakiye - 100, "hash",
            new[] { new KasaKontrolKanalDto(1, "MEZAT", 60), new KasaKontrolKanalDto(2, "PERAKENDE", 40) }, new DateOnly(2026, 9, 26)));
        }
        public Task<KasaKontrolDto> KasaKontrolAciklaAsync(int id, KasaKontrolAciklamaYaz g)
        {
            Aciklamalar.Add((id, g));
            if (AciklamaHata is { } e)
                return Task.FromException<KasaKontrolDto>(e);
            // Sunucu gibi: tutarlar aynen, sürüm artar, güncel karşılaştırma alanları boş döner.
            return Task.FromResult(Gecmis.Single(k => k.Id == id) with { Surum = g.Surum + 1, FarkAciklamasi = g.Aciklama.Trim(), FarkAciklamaZamani = DateTimeOffset.Now, GuncelSistemBakiye = null, GuncelFark = null, SonradanDegisti = false });
        }
        public Task<KasaKontrolSonrasiDto> KasaKontrolSonrasiAsync(int id) { SonrasiIstekleri.Add(id); return Task.FromResult(Sonrasi ?? new KasaKontrolSonrasiDto(id, DateTimeOffset.Now, new DateOnly(2026, 9, 26), true, 100, 100, 100, [], [], [], false)); }
        public Task<KasaHareketleriDto> KasaHareketleriAsync(DateOnly? baslangic = null, DateOnly? bitis = null, int? kanalId = null)
        { DokumIstekleri.Add((baslangic, bitis, kanalId)); return Task.FromResult(Dokum ?? new KasaHareketleriDto(baslangic ?? new DateOnly(2026, 9, 1), bitis ?? new DateOnly(2026, 9, 26), kanalId, null, 0, 0, [])); }
        public Task<KasaKontrolDto> KasaKontrolKaydetAsync(KasaKontrolYaz g) { Kontroller.Add(g); return KontrolHata is { } e ? Task.FromException<KasaKontrolDto>(e) : Task.FromResult(new KasaKontrolDto(1, DateTimeOffset.Now, 100, g.GercekBakiye, g.GercekBakiye - 100, g.Not)); }
        /// <summary>Ayarlanırsa masraf önizlemesi yanıt vermeden önce bunu bekler; <see cref="MasrafIstisnasi"/> kaydı o hatayla bitirir (ör. 409).</summary>
        public Task? MasrafOnizlemeKapisi; public Exception? MasrafIstisnasi;
        public async Task<KartMasrafOnizlemeDto> KartMasrafOnizleAsync(int id, KartMasrafYaz g) { MasrafOnizlemeSayisi++; if (MasrafOnizlemeKapisi is { } kapi) await kapi; return new KartMasrafOnizlemeDto(id, g.EkstreId, g.Tarih, g.Tutar, 100, new[] { new TakipKanalPayi(1, "MEZAT", g.Tutar) }, "pay-hash"); }
        public Task<KartTakipDto> KartMasrafKaydetAsync(int id, KartMasrafYaz g) { Masraflar.Add(g); return MasrafIstisnasi is { } istisna ? Task.FromException<KartTakipDto>(istisna) : MasrafHata ? Task.FromException<KartTakipDto>(new HttpRequestException()) : Task.FromResult(FinansTakipTests.Fake.OrnekKart() with { Surum = 4 }); }
    }
}
