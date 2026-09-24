using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// İşlemler sayfası paket C davranışları: kısayollar (26), formdan cari ekleme (27), kayıt öncesi
/// uyarılar (28), korumalı gelen (29), silme onayı (32), gelişmiş arama (06), kopyala/öneri/seri (15).
/// </summary>
public class IslemHizliGirisTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    private static readonly DonemDto Eylul3 = new(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20), 2026, 9);
    private static readonly DonemDto Eylul4 = new(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 2026, 9);

    private static (SahteApi api, IslemlerViewModel vm, SabitSaat saat) Kur(Action<SahteApi>? ayar = null)
    {
        var api = new SahteApi
        {
            KanallarListe = new[]
            {
                new KanalDto(1, "MEZAT", true, 0, 0m), new KanalDto(2, "PERAKENDE", true, 1, 0m),
                new KanalDto(3, "TOPTAN", true, 2, 0m),
            },
            DonemlerListe = new[] { Eylul3, Eylul4 },
            KrediKartlariListe = new[] { new KrediKartiDto(7, "Bonus", new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 20), 10_000m, 0m) },
        };
        ayar?.Invoke(api);
        var saat = new SabitSaat(Bugun.AddHours(10));
        var vm = new IslemlerViewModel(api, saat) { EditorMu = true };
        return (api, vm, saat);
    }

    private static void Doldur(IslemlerViewModel vm, string cari = "Market", decimal tutar = 100m, string kanal = "MEZAT")
    {
        vm.DuzenTarih = Bugun;
        vm.DuzenCari = cari;
        vm.DuzenTutar = tutar;
        vm.DuzenKanal = kanal;
    }

    private static IslemDto Islem(int id, string cari = "Market", decimal tutar = 100m, string kanal = "MEZAT",
        GiderTipi tip = GiderTipi.Cari, int? kart = null, string? not = null)
        => new(id, new DateOnly(2026, 9, 10), cari, tutar, kanal, tip, not, kart);

    // ---------- 26: Kısayollar ----------

    [Fact]
    public async Task Ctrl_S_kaydeder_Ctrl_N_formu_temizleyip_cariye_odaklar()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        var odaklar = new List<string>();
        vm.OdakIstendi += (_, a) => odaklar.Add(a);
        Doldur(vm);

        Assert.True(vm.KisayolCalistir(KlavyeKisayollari.Coz("S", ctrl: true, alt: false)));
        Assert.Equal("Market", api.SonIslemOlustur!.Cari);

        vm.DuzenCari = "yarım";
        Assert.True(vm.KisayolCalistir(KisayolEylemi.YeniSatir));
        Assert.Equal("", vm.DuzenCari);
        Assert.Equal(IslemlerViewModel.OdakCari, odaklar.Last());
    }

    [Fact]
    public async Task Tutar_ve_notta_Enter_kaydeder_uyari_bekliyorsa_gecmez()
    {
        var (api, vm, _) = Kur(a => a.UyariListe = new[] { new IslemUyariDto(IslemUyariKodlari.AyniTutar, "aynı") });
        await vm.YukleAsync();
        Doldur(vm);
        vm.EnterKaydetCommand.Execute(null);
        await Task.Yield();
        Assert.True(vm.UyariBekliyor);
        Assert.Null(api.SonIslemOlustur);
        var cagri = api.UyariCagrilari.Count;

        vm.EnterKaydetCommand.Execute(null);                        // ikinci Enter uyarıyı geçmez
        Assert.Equal(cagri, api.UyariCagrilari.Count);
        Assert.Null(api.SonIslemOlustur);

        api.UyariListe = [];
        vm.UyariVazgecCommand.Execute(null);
        vm.EnterKaydetCommand.Execute(null);
        await Task.Yield();
        Assert.Equal("Market", api.SonIslemOlustur!.Cari);
    }

    [Fact]
    public async Task Alt_rakam_kanal_cipini_secer_olmayan_sira_islenmez()
    {
        var (_, vm, _) = Kur();
        await vm.YukleAsync();
        Assert.True(vm.KisayolCalistir(KisayolEylemi.Kanal2));
        Assert.Equal("PERAKENDE", vm.DuzenKanal);
        Assert.True(vm.GiderKanallari.Single(k => k.Ad == "PERAKENDE").Secili);
        Assert.True(vm.KisayolCalistir(KisayolEylemi.Kanal4));
        Assert.Equal("Ortak", vm.DuzenKanal);   // 4. çip: 3 kanal + Ortak
    }

    [Fact]
    public async Task Izleyicide_kisayol_calismaz()
    {
        var (api, vm, _) = Kur();
        vm.EditorMu = false;
        Doldur(vm);
        Assert.False(vm.KisayolCalistir(KisayolEylemi.Kaydet));
        await Task.Yield();
        Assert.Null(api.SonIslemOlustur);
    }

    [Fact]
    public async Task Esc_once_uyariyi_kapatir_sonra_duzenlemeden_cikar_yeni_kayitta_formu_silmez()
    {
        var (api, vm, _) = Kur(a => a.UyariListe = new[] { new IslemUyariDto(IslemUyariKodlari.EskiTarih, "eski") });
        vm.Duzenle(Islem(5));
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.True(vm.UyariBekliyor);

        Assert.True(vm.KisayolCalistir(KisayolEylemi.Vazgec));
        Assert.False(vm.UyariBekliyor);
        Assert.Equal(5, vm.DuzenId);                          // önce yalnız uyarı kapandı
        Assert.True(vm.KisayolCalistir(KisayolEylemi.Vazgec));
        Assert.Equal(0, vm.DuzenId);                          // sonra düzenlemeden çıktı

        vm.DuzenCari = "yazılıyor";
        Assert.False(vm.KisayolCalistir(KisayolEylemi.Vazgec));
        Assert.Equal("yazılıyor", vm.DuzenCari);
        Assert.Null(api.SonIslemGuncelle);
    }

    [Fact]
    public void Tarih_kutusunda_B_ve_D()
    {
        var (_, vm, _) = Kur();
        vm.DuzenTarih = new DateTime(2026, 1, 1);
        Assert.True(vm.TarihKisayolu("D"));
        Assert.Equal(new DateTime(2026, 9, 23), vm.DuzenTarih);
        Assert.True(vm.TarihKisayolu("b"));
        Assert.Equal(Bugun, vm.DuzenTarih);
        Assert.False(vm.TarihKisayolu("7"));
        Assert.True(vm.GelenTarihKisayolu("D"));
        Assert.Equal(new DateTime(2026, 9, 23), vm.GelenTarih);
    }

    [Fact]
    public async Task Cari_kutusunda_Enter_once_ilk_oneriyi_secer_sonra_kaydeder()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        var odaklar = new List<string>();
        vm.OdakIstendi += (_, a) => odaklar.Add(a);
        vm.DuzenTarih = Bugun; vm.DuzenTutar = 5m; vm.DuzenKanal = "MEZAT";
        vm.DuzenCari = "mark";
        vm.CariEnterCommand.Execute(null);
        Assert.Equal("Market", vm.DuzenCari);
        Assert.Equal(IslemlerViewModel.OdakTutar, odaklar.Last());
        Assert.Null(api.SonIslemOlustur);

        vm.CariEnterCommand.Execute(null);
        await Task.Yield();
        Assert.Equal("Market", api.SonIslemOlustur!.Cari);
    }

    [Fact]
    public async Task Cari_kutusunda_Enter_tutar_bosken_kaydetmez_tutara_gecer_kayitli_yazima_cevirir()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        var odaklar = new List<string>();
        vm.OdakIstendi += (_, a) => odaklar.Add(a);
        vm.DuzenTarih = Bugun; vm.DuzenKanal = "MEZAT";
        vm.DuzenCari = "market";                                   // kayıtlı ad, tam yazılmış; tutar yok

        vm.CariEnterCommand.Execute(null);
        await Task.Yield();
        Assert.Equal("Market", vm.DuzenCari);
        Assert.Equal(IslemlerViewModel.OdakTutar, odaklar.Last());
        Assert.Null(api.SonIslemOlustur);
        Assert.Empty(api.UyariCagrilari);                          // sunucuya hiç gidilmez
        Assert.Null(vm.Hata);

        vm.DuzenCari = "Yeni Tedarikçi";                           // kayıtlı değil, öneri yok: yine tutara
        vm.CariEnterCommand.Execute(null);
        Assert.Equal(("Yeni Tedarikçi", IslemlerViewModel.OdakTutar), (vm.DuzenCari, odaklar.Last()));

        vm.DuzenCari = "Market"; vm.DuzenTutar = 40m;             // tutar girildi: Enter kaydeder
        vm.CariEnterCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(40m, api.SonIslemOlustur!.TutarTl);
    }

    [Theory]
    [InlineData("1500o")]       // 15.000 yazarken son 0 yerine o
    [InlineData("1.250.00")]    // 1.250.000 yazarken bir grup eksik
    [InlineData("-50")]
    public async Task Tutar_kutusunda_gecersiz_metin_varken_Enter_ve_Ctrl_S_kaydetmez(string metin)
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        Doldur(vm, tutar: 1_500m);
        vm.TutarKutusuMetni = metin;                               // kutu kırmızı; bağlı tutar hâlâ 1.500

        vm.EnterKaydetCommand.Execute(null);
        Assert.True(vm.KisayolCalistir(KisayolEylemi.Kaydet));     // tuş yutulur ama kaydedilmez
        vm.CariEnterCommand.Execute(null);
        await Task.Yield();
        Assert.Null(api.SonIslemOlustur);
        Assert.Empty(api.UyariCagrilari);
        Assert.StartsWith("Tutar geçersiz:", vm.Hata);
        Assert.Contains("Kaydedilmedi", vm.Hata);

        vm.TutarKutusuMetni = "15.000";                            // düzeltildi (dönüştürücü tutarı da yazar)
        vm.DuzenTutar = 15_000m;
        vm.EnterKaydetCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(15_000m, api.SonIslemOlustur!.TutarTl);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Tutar_kutusu_metni_bilinmiyorsa_ya_da_bossa_kayit_eskisi_gibi()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        Doldur(vm, tutar: 75m);
        vm.TutarKutusuMetni = null;                                // sayfa henüz itmedi
        vm.EnterKaydetCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(75m, api.SonIslemOlustur!.TutarTl);

        api.SonIslemOlustur = null;
        Doldur(vm, tutar: 80m);
        vm.TutarKutusuMetni = "80";
        Assert.True(vm.KisayolCalistir(KisayolEylemi.Kaydet));
        await Task.Yield();
        Assert.Equal(80m, api.SonIslemOlustur!.TutarTl);
    }

    [Fact]
    public async Task Gelen_kutusunda_gecersiz_metinle_Enter_kaydetmez_soru_acikken_bir_sey_yapmaz()
    {
        var (api, vm, _) = Kur(a => a.GelenDeposu[(new DateOnly(2026, 9, 21), "MEZAT")] = 1_000m);
        await vm.YukleAsync();
        vm.GelenTarih = new DateTime(2026, 9, 23); vm.GelenKanal = "TOPTAN"; vm.GelenTutar = 250m;
        vm.GelenTutarKutusuMetni = "2500o";

        vm.GelenEnterCommand.Execute(null);
        await Task.Yield();
        Assert.Empty(api.KorumaliGelenCagrilari);
        Assert.StartsWith("Gelen tutarı geçersiz:", vm.Hata);

        vm.GelenTutarKutusuMetni = "250";
        vm.GelenEnterCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(250m, api.GelenDeposu[(new DateOnly(2026, 9, 21), "TOPTAN")]);

        vm.GelenKanal = "MEZAT"; vm.GelenTutar = 90m; vm.GelenTutarKutusuMetni = "90";
        vm.GelenEnterCommand.Execute(null);
        await Task.Yield();
        Assert.True(vm.GelenCakismaVar);
        var cagri = api.KorumaliGelenCagrilari.Count;
        vm.GelenEnterCommand.Execute(null);                        // soru açıkken Enter soruyu geçmez
        await Task.Yield();
        Assert.True(vm.GelenCakismaVar);
        Assert.Equal(cagri, api.KorumaliGelenCagrilari.Count);
        Assert.Equal(1_000m, api.GelenDeposu[(new DateOnly(2026, 9, 21), "MEZAT")]);
    }

    // ---------- 27: Formdan cari ekleme ----------

    [Fact]
    public async Task Cari_olarak_ekle_kayitli_olmayan_adda_gorunur_benzer_varsa_once_sorar()
    {
        var (api, vm, _) = Kur(a => a.CarilerListe = new[] { new CariDto(1, "ABC Ltd. Şti.", true), new CariDto(2, "Market", true) });
        await vm.YukleAsync();
        vm.DuzenCari = "market";
        Assert.False(vm.CariEklenebilir);               // kayıtlı (harf farkı)
        vm.DuzenCari = "abc ltd";
        Assert.True(vm.CariEklenebilir);
        vm.DuzenTip = GiderTipi.SabitGider;
        Assert.False(vm.CariEklenebilir);               // sabit giderde kalem düğmesi var
        vm.DuzenTip = GiderTipi.Cari;

        await vm.CariEkleCommand.ExecuteAsync(null);
        Assert.Null(api.SonCariOlustur);
        Assert.True(vm.BenzerCariSoruluyor);
        Assert.Equal("Bunu mu demek istediniz: ABC Ltd. Şti.?", vm.BenzerCariSorusu);
        Assert.Equal(["ABC Ltd. Şti."], vm.BenzerCariler);

        vm.BenzerCariSecCommand.Execute("ABC Ltd. Şti.");
        Assert.Equal("ABC Ltd. Şti.", vm.DuzenCari);
        Assert.False(vm.BenzerCariSoruluyor);
        Assert.Null(api.SonCariOlustur);
    }

    [Fact]
    public async Task Yine_de_ekle_ve_benzersiz_ad_mevcut_cari_ekleme_cagrisini_kullanir()
    {
        var (api, vm, _) = Kur(a => a.CarilerListe = new[] { new CariDto(1, "ABC Ltd. Şti.", true) });
        await vm.YukleAsync();
        vm.DuzenCari = "ABC Limited";
        await vm.CariEkleCommand.ExecuteAsync(null);
        Assert.True(vm.BenzerCariSoruluyor);
        api.CarilerListe = new[] { new CariDto(1, "ABC Ltd. Şti.", true), new CariDto(2, "ABC Limited", true) };
        await vm.CariYineDeEkleCommand.ExecuteAsync(null);
        Assert.Equal(new CariYaz("ABC Limited", true), api.SonCariOlustur);
        Assert.False(vm.BenzerCariSoruluyor);
        Assert.False(vm.CariEklenebilir);

        vm.DuzenCari = "  Yepyeni Firma ";
        await vm.CariEkleCommand.ExecuteAsync(null);
        Assert.Equal(new CariYaz("Yepyeni Firma", true), api.SonCariOlustur);
        Assert.False(vm.BenzerCariSoruluyor);
    }

    [Fact]
    public async Task Cari_ekleme_bos_ve_uzun_adi_reddeder_ad_degisince_soru_kapanir()
    {
        var (api, vm, _) = Kur(a => a.CarilerListe = new[] { new CariDto(1, "Market", true) });
        await vm.YukleAsync();
        vm.DuzenCari = "  ";
        await vm.CariEkleCommand.ExecuteAsync(null);
        Assert.Equal(IslemlerViewModel.CariBosMesaji, vm.Hata);
        vm.DuzenCari = new string('a', 201);
        await vm.CariEkleCommand.ExecuteAsync(null);
        Assert.Equal(IslemlerViewModel.CariUzunMesaji, vm.Hata);
        Assert.Null(api.SonCariOlustur);

        vm.DuzenCari = "Markett";
        await vm.CariEkleCommand.ExecuteAsync(null);
        Assert.True(vm.BenzerCariSoruluyor);
        vm.DuzenCari = "Başka";
        Assert.False(vm.BenzerCariSoruluyor);
    }

    // ---------- 28: Kayıt öncesi uyarılar ----------

    [Fact]
    public async Task Uyari_varsa_kaydetmez_yine_de_kaydet_ile_kaydeder()
    {
        var (api, vm, _) = Kur(a => a.UyariListe = new[]
        {
            new IslemUyariDto(IslemUyariKodlari.AyniTutar, "Aynı cariye aynı tutar…"),
            new IslemUyariDto(IslemUyariKodlari.OlaganDisiTutar, "Tutar olağan tutarın 10 katı…"),
        });
        Doldur(vm, tutar: 1_000m);
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonIslemOlustur);
        Assert.True(vm.UyariBekliyor);
        Assert.Equal(2, vm.Uyarilar.Count);
        var (g, haric) = api.UyariCagrilari.Single();
        Assert.Equal((1_000m, "Market", (int?)null), (g.TutarTl, g.Cari, haric));

        await vm.UyariYineDeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(1_000m, api.SonIslemOlustur!.TutarTl);
        Assert.False(vm.UyariBekliyor);
        Assert.Single(api.UyariCagrilari);              // onaydan sonra yeniden sorulmaz
    }

    [Fact]
    public async Task Uyari_vazgec_ve_form_degisince_uyari_kapanir()
    {
        var (api, vm, _) = Kur(a => a.UyariListe = new[] { new IslemUyariDto(IslemUyariKodlari.EskiTarih, "eski") });
        Doldur(vm);
        await vm.KaydetCommand.ExecuteAsync(null);
        vm.UyariVazgecCommand.Execute(null);
        Assert.False(vm.UyariBekliyor);
        Assert.Empty(vm.Uyarilar);

        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.True(vm.UyariBekliyor);
        vm.DuzenTutar = 200m;
        Assert.False(vm.UyariBekliyor);
        Assert.Null(api.SonIslemOlustur);
    }

    [Fact]
    public async Task Uyari_alinamazsa_kayit_engellenmez_duzenlemede_haric_id_gider()
    {
        var (api, vm, _) = Kur(a => a.UyariHatasi = new HttpRequestException("ağ"));
        Doldur(vm);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.NotNull(api.SonIslemOlustur);
        Assert.Null(vm.Hata);

        api.UyariHatasi = null;
        vm.Duzenle(Islem(12));
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(12, api.UyariCagrilari.Last().HaricId);
        Assert.Equal(12, api.SonIslemGuncelle!.Value.Id);
    }

    [Fact]
    public async Task Ileri_tarih_onayindan_sonra_uyarilar_sorulur()
    {
        var (api, vm, _) = Kur(a => a.UyariListe = new[] { new IslemUyariDto(IslemUyariKodlari.AyniTutar, "aynı") });
        Doldur(vm);
        vm.DuzenTarih = Bugun.AddDays(3);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.True(vm.IleriTarihOnayBekliyor);
        Assert.Empty(api.UyariCagrilari);

        await vm.IleriTarihOnaylaCommand.ExecuteAsync(null);
        Assert.True(vm.UyariBekliyor);
        Assert.Null(api.SonIslemOlustur);
        vm.KisayolCalistir(KisayolEylemi.Kaydet);                   // Ctrl+S uyarıyı geçmez
        Assert.Null(api.SonIslemOlustur);

        await vm.UyariYineDeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(new DateOnly(2026, 9, 27), api.SonIslemOlustur!.Tarih);
    }

    // ---------- 15: Kopyala, öneri, seri giriş, kaydedilen şerit ----------

    [Fact]
    public async Task Kopyala_yeni_kayit_olarak_forma_alir_tarih_bugun()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        string? odak = null;
        vm.OdakIstendi += (_, a) => odak = a;
        vm.KopyalaCommand.Execute(Islem(9, cari: "Market", tutar: 250m, kanal: "TOPTAN", tip: GiderTipi.KrediKarti, kart: 7, not: "fiş"));

        Assert.Equal(0, vm.DuzenId);
        Assert.Equal(Bugun, vm.DuzenTarih);
        Assert.Equal(("Market", 250m, "TOPTAN", GiderTipi.KrediKarti, (int?)7, "fiş"),
            (vm.DuzenCari, vm.DuzenTutar, vm.DuzenKanal, vm.DuzenTip, vm.DuzenKrediKartiId, vm.DuzenNot));
        Assert.Equal(IslemlerViewModel.OdakTarih, odak);
        Assert.Empty(api.OneriCagrilari);   // kopyada öneri kanalı/tipi ezmez

        vm.DuzenTarih = new DateTime(2026, 9, 20);
        await vm.KaydetCommand.ExecuteAsync(null);
        // Kopya belge bilgisini almaz: form boş belgeyle gönderir (paket F).
        Assert.Equal(new IslemYaz(new DateOnly(2026, 9, 20), "Market", 250m, "TOPTAN", GiderTipi.KrediKarti, "fiş", 7)
            { Belge = new BelgeBilgisi(null, null, false) }, api.SonIslemOlustur);
    }

    [Fact]
    public async Task Cari_secilince_son_kanal_ve_tip_doldurulur_elle_secilen_ezilmez()
    {
        var (api, vm, _) = Kur(a =>
        {
            a.OneriListe["Market"] = new IslemOneriDto("Market", new DateOnly(2026, 9, 10), 40m, "TOPTAN", GiderTipi.KrediKarti, 7);
            a.OneriListe["MEZAT alış"] = new IslemOneriDto("MEZAT alış", new DateOnly(2026, 9, 1), 5m, "PERAKENDE", GiderTipi.Cari, null);
        });
        await vm.YukleAsync();

        vm.SecCariCommand.Execute("Market");
        Assert.Equal("TOPTAN", vm.DuzenKanal);
        Assert.Equal(GiderTipi.KrediKarti, vm.DuzenTip);
        Assert.Equal(7, vm.DuzenKrediKartiId);
        Assert.Contains("Son işlem: 10 Eyl 2026 · TOPTAN", vm.CariOnerisiMetni);

        // Öneriyle gelen tip/kanal kullanıcı seçimi sayılmaz: başka cari seçilince yenisi gelir.
        vm.DuzenCari = "MEZAT alış";
        Assert.Equal(("PERAKENDE", GiderTipi.Cari, (int?)null), (vm.DuzenKanal, vm.DuzenTip, vm.DuzenKrediKartiId));

        vm.YeniCommand.Execute(null);
        vm.SecGiderKanalCommand.Execute(new SecimCipi("MEZAT"));   // kullanıcı kanalı seçti
        vm.DuzenCari = "market";                                   // yazarak (kayıtlı, harf farkı)
        Assert.Equal("MEZAT", vm.DuzenKanal);
        Assert.Equal(GiderTipi.KrediKarti, vm.DuzenTip);            // tip seçilmemişti: öneri gelir
    }

    [Fact]
    public async Task Oneri_duzenlemede_kayitsiz_adda_ve_hatada_uygulanmaz()
    {
        var (api, vm, _) = Kur(a => a.OneriListe["Market"] = new IslemOneriDto("Market", new DateOnly(2026, 9, 10), 1m, "TOPTAN", GiderTipi.Cari, null));
        await vm.YukleAsync();
        vm.Duzenle(Islem(3, cari: "MEZAT alış", kanal: "MEZAT"));
        vm.DuzenCari = "Market";
        Assert.Equal("MEZAT", vm.DuzenKanal);
        Assert.Empty(api.OneriCagrilari);

        vm.YeniCommand.Execute(null);
        vm.DuzenCari = "Kayıtsız";
        Assert.Empty(api.OneriCagrilari);

        api.OneriHatasi = new HttpRequestException("ağ");
        vm.DuzenCari = "Market";
        Assert.Equal("", vm.DuzenKanal);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Seri_giriste_tarih_kanal_tip_not_kalir_yalniz_cari_ve_tutar_temizlenir()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        var odaklar = new List<string>();
        vm.OdakIstendi += (_, a) => odaklar.Add(a);
        vm.SeriGiris = true;
        vm.DuzenTarih = new DateTime(2026, 9, 22);
        vm.DuzenCari = "Market"; vm.DuzenTutar = 10m; vm.DuzenKanal = "PERAKENDE"; vm.DuzenNot = "not";
        vm.DuzenTip = GiderTipi.KrediKarti; vm.DuzenKrediKartiId = 7;

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.NotNull(api.SonIslemOlustur);
        Assert.Equal((new DateTime(2026, 9, 22), "PERAKENDE", GiderTipi.KrediKarti, (int?)7), (vm.DuzenTarih, vm.DuzenKanal, vm.DuzenTip, vm.DuzenKrediKartiId));
        Assert.Equal(("", 0m, "not", 0), (vm.DuzenCari, vm.DuzenTutar, vm.DuzenNot, vm.DuzenId));   // spec: yalnız cari ve tutar
        Assert.Equal(IslemlerViewModel.OdakCari, odaklar.Last());

        vm.DuzenCari = "Market"; vm.DuzenTutar = 11m;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal("not", api.SonIslemOlustur!.Not);            // kalan not ikinci kayda da yazılır

        vm.SeriGiris = false;
        vm.DuzenCari = "Market"; vm.DuzenTutar = 20m;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(("", "", (string?)null), (vm.DuzenCari, vm.DuzenKanal, vm.DuzenNot));   // normal kayıt formu sıfırlar
    }

    [Fact]
    public async Task Kaydedilen_satir_seridi_duzelt_ve_sil()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        Doldur(vm, tutar: 1_250m);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.True(vm.SonKaydedilenGorunur);
        Assert.Equal("Kaydedildi: 24 Eyl · Market · 1.250,00 ₺ · MEZAT", vm.SonKaydedilenMetni);

        vm.SonKaydedileniDuzeltCommand.Execute(null);
        Assert.Equal(1_250m, vm.DuzenTutar);
        Assert.Equal("Market", vm.DuzenCari);

        await vm.SonKaydedileniSilCommand.ExecuteAsync(null);   // ilk basış: onay
        Assert.Null(api.SonIslemSil);
        await vm.SonKaydedileniSilCommand.ExecuteAsync(null);
        Assert.Equal(0, api.SonIslemSil);                       // sahte API yeni işleme Id 0 verir
        Assert.False(vm.SonKaydedilenGorunur);

        Doldur(vm);
        await vm.KaydetCommand.ExecuteAsync(null);
        vm.SonKaydedilenKapatCommand.Execute(null);
        Assert.False(vm.SonKaydedilenGorunur);
    }

    // ---------- 32: Silme onayı ve geri al ----------

    [Fact]
    public async Task Sil_ikinci_basista_siler_geri_al_seridi_mevcut_ucu_kullanir()
    {
        var (api, vm, _) = Kur(a => a.IslemlerListe = new[] { Islem(5) });
        await vm.YukleAsync();
        var i = vm.Islemler.Single();

        await vm.OnayliSilCommand.ExecuteAsync(i);
        Assert.Null(api.SonIslemSil);
        Assert.Equal(i, vm.Silme.Bekleyen);
        await vm.OnayliSilCommand.ExecuteAsync(i);
        Assert.Equal(5, api.SonIslemSil);
        Assert.True(vm.Silme.SeritGorunur);
        Assert.Equal("Silindi: 10 Eyl · Market · 100,00 ₺", vm.Silme.SeritMetni);
        Assert.Equal((GecmisTurAdlari.Islem, 5), api.SonSilmeCagrilari.Single());

        var once = api.SayfaCagrilari.Count;
        await vm.SilmeGeriAlCommand.ExecuteAsync(null);
        Assert.Equal(905, api.SonGeriAl);
        Assert.False(vm.Silme.SeritGorunur);
        Assert.True(api.SayfaCagrilari.Count > once);            // liste yenilendi
    }

    [Fact]
    public async Task Sure_gecince_ikinci_basis_yeniden_onay_ister_silme_hatasinda_serit_yok()
    {
        var (api, vm, saat) = Kur(a => a.IslemlerListe = new[] { Islem(5) });
        await vm.YukleAsync();
        var i = vm.Islemler.Single();
        await vm.OnayliSilCommand.ExecuteAsync(i);
        saat.Ilerle(TimeSpan.FromSeconds(6));
        await vm.OnayliSilCommand.ExecuteAsync(i);
        Assert.Null(api.SonIslemSil);

        api.SonSilmeUret = (_, _) => throw new InvalidOperationException("çağrılmamalı");
        vm.Silme.Vazgec();
        vm.SilmeSeridiKapatCommand.Execute(null);
        // Eski SilCommand aynen: onaysız siler (başka akışlar ve testler için).
        await vm.SilCommand.ExecuteAsync(i);
        Assert.Equal(5, api.SonIslemSil);
    }

    [Fact]
    public async Task Silme_basarili_liste_tazelemesi_hata_verse_de_geri_al_seridi_cikar()
    {
        var (api, vm, _) = Kur(a => a.IslemlerListe = new[] { Islem(5) });
        await vm.YukleAsync();
        var i = vm.Islemler.Single();
        vm.Duzenle(i);
        await vm.OnayliSilCommand.ExecuteAsync(i);
        api.YuklemeHatasi = new HttpRequestException("ağ koptu");      // silmeden sonraki tazeleme düşer

        await vm.OnayliSilCommand.ExecuteAsync(i);
        Assert.Equal(5, api.SonIslemSil);
        Assert.NotNull(vm.Hata);                                       // tazeleme hatası görünür
        Assert.Equal(0, vm.DuzenId);                                   // silinen kayıt formda kalmaz
        Assert.True(vm.Silme.SeritGorunur);                            // ama geri alma imkânı kaybolmaz
        Assert.Equal((GecmisTurAdlari.Islem, 5), api.SonSilmeCagrilari.Single());

        api.YuklemeHatasi = null;
        await vm.SilmeGeriAlCommand.ExecuteAsync(null);
        Assert.Equal(905, api.SonGeriAl);
        Assert.Null(vm.Hata);
    }

    // ---------- 06: Gelişmiş arama ----------

    [Fact]
    public async Task Gelismis_arama_uygulaninca_yeni_uc_kullanilir_temizleyince_eski_istek()
    {
        var (api, vm, _) = Kur(a => a.IslemlerListe = new[]
        {
            Islem(1, tutar: 50m, not: "İade faturası"), Islem(2, tutar: 500m, not: "nakliye"),
            Islem(3, tutar: 700m, tip: GiderTipi.KrediKarti, kart: 7, not: "iade"),
        });
        await vm.YukleAsync();
        Assert.Empty(api.AramaCagrilari);
        Assert.Equal(3, vm.Islemler.Count);

        vm.GelismisAramaAcKapatCommand.Execute(null);
        Assert.Equal(4, vm.AramaTipCipleri.Count);
        Assert.Single(vm.AramaKartCipleri);
        vm.AramaNot = " iade ";
        vm.AramaMin = "100";
        vm.SecAramaTipCommand.Execute(vm.AramaTipCipleri.Single(c => c.Ad == "Kredi kartı"));
        vm.SecAramaKartCommand.Execute(vm.AramaKartCipleri.Single());
        await vm.AramayiUygulaCommand.ExecuteAsync(null);

        var (a, limit, offset) = api.AramaCagrilari.Last();
        Assert.Equal(("iade", GiderTipi.KrediKarti, (int?)7, (decimal?)100m, (decimal?)null), (a.NotAra, a.Tip, a.KartId, a.MinTutar, a.MaxTutar));
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), (a.Baslangic!.Value, a.Bitis!.Value));   // "Bu ay" korunur
        Assert.Equal(IslemlerViewModel.SayfaBoyutu, limit);
        Assert.Equal([3], vm.Islemler.Select(i => i.Id));
        Assert.Equal("Gelişmiş süzgeç: Not: \"iade\" · Tip: Kredi kartı · Kart: Bonus · ≥ 100,00 ₺", vm.AramaOzeti);
        Assert.True(vm.AramaEtkin);

        // Kaydedicisi olmayan VM'de Excel'e aktarma hata verir, istek atmaz.
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal(ExcelAktarma.KaydediciYok, vm.Hata);
        Assert.Null(api.SonAramaCsv);

        var sayfaOnce = api.SayfaCagrilari.Count;
        await vm.AramayiTemizleCommand.ExecuteAsync(null);
        Assert.False(vm.AramaEtkin);
        Assert.True(api.SayfaCagrilari.Count > sayfaOnce);       // eski uç
        Assert.Equal(3, vm.Islemler.Count);
    }

    [Theory]
    [InlineData("abc", "", "En az tutar")]
    [InlineData("", "-5", "En çok tutar")]
    [InlineData("500", "100", "En az tutar en çok tutardan büyük olamaz.")]
    public async Task Gelismis_arama_gecersiz_tutarda_istek_atmaz(string min, string max, string beklenen)
    {
        var (api, vm, _) = Kur();
        vm.AramaMin = min;
        vm.AramaMax = max;
        await vm.AramayiUygulaCommand.ExecuteAsync(null);
        Assert.Contains(beklenen, vm.Hata);
        Assert.Empty(api.AramaCagrilari);
        Assert.False(vm.AramaEtkin);
    }

    [Fact]
    public async Task Gelismis_arama_excel_aktarimi_ayni_suzgecle()
    {
        var api = new SahteApi { DonemlerListe = new[] { Eylul4 } };
        var kaydedici = new SahteKaydedici();
        var vm = new IslemlerViewModel(api, new SabitSaat(Bugun), kaydedici) { EditorMu = true, AramaMax = "1.000" };
        await vm.AramayiUygulaCommand.ExecuteAsync(null);
        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal(1_000m, api.SonAramaCsv!.MaxTutar);
        Assert.Null(api.SonIslemCsv);
        Assert.NotNull(vm.AktarilanDosya);
    }

    // ---------- 29: İşlemler gelen formu (korumalı) ----------

    [Fact]
    public async Task Gelen_formu_kayitli_geleni_sessizce_ezmez_uzerine_yaz_ya_da_ustune_ekle_sorar()
    {
        var (api, vm, _) = Kur(a => a.GelenDeposu[(new DateOnly(2026, 9, 21), "MEZAT")] = 1_000m);
        await vm.YukleAsync();
        vm.GelenTarih = new DateTime(2026, 9, 23); vm.GelenKanal = "MEZAT"; vm.GelenTutar = 250m;

        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.True(vm.GelenCakismaVar);
        Assert.Contains("1.000,00 ₺", vm.GelenCakismaSorusu);
        Assert.Empty(api.KorumaliGelenCagrilari);

        await vm.GelenUstuneEkleCommand.ExecuteAsync(null);
        Assert.Equal((1_250m, 1_000m), (api.KorumaliGelenCagrilari.Single().G.TutarTl, api.KorumaliGelenCagrilari.Single().Beklenen));
        Assert.False(vm.GelenCakismaVar);
        Assert.Equal(("", 0m), (vm.GelenKanal, vm.GelenTutar));
        Assert.Contains("1.250,00", vm.GelenSonuc);

        vm.GelenKanal = "MEZAT"; vm.GelenTutar = 90m;
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        await vm.GelenUzerineYazCommand.ExecuteAsync(null);
        Assert.Equal(90m, api.GelenDeposu[(new DateOnly(2026, 9, 21), "MEZAT")]);
    }

    [Fact]
    public async Task Gelen_formu_bu_arada_degisen_kayitta_yeniden_sorar_vazgec_gondermez()
    {
        var (api, vm, _) = Kur();
        await vm.YukleAsync();
        vm.GelenTarih = new DateTime(2026, 9, 23); vm.GelenKanal = "TOPTAN"; vm.GelenTutar = 10m;
        // Okuma boş döndü ama yazarken başka biri 40 girmiş (sahte: okuma sonrası depoya yaz).
        api.GelenDeposu[(new DateOnly(2026, 9, 21), "TOPTAN")] = 0m;
        await vm.GelenKaydetCommand.ExecuteAsync(null);                // kayıt yok → doğrudan yazar
        Assert.Equal(10m, api.GelenDeposu[(new DateOnly(2026, 9, 21), "TOPTAN")]);

        vm.GelenKanal = "TOPTAN"; vm.GelenTutar = 30m;
        await vm.GelenKaydetCommand.ExecuteAsync(null);                // 10 kayıtlı → soru
        Assert.True(vm.GelenCakismaVar);
        api.GelenDeposu[(new DateOnly(2026, 9, 21), "TOPTAN")] = 40m;  // bu arada değişti
        await vm.GelenUzerineYazCommand.ExecuteAsync(null);
        Assert.True(vm.GelenCakismaVar);                               // 409 → yeniden sorar
        Assert.Contains("40,00 ₺", vm.GelenCakismaSorusu);
        Assert.Equal(40m, api.GelenDeposu[(new DateOnly(2026, 9, 21), "TOPTAN")]);

        vm.GelenCakismaKapatCommand.Execute(null);
        Assert.False(vm.GelenCakismaVar);
        Assert.Equal(30m, vm.GelenTutar);                              // form korunur
    }

    private sealed class SahteKaydedici : IDosyaKaydedici
    {
        public Task<string> KaydetAsync(string dosyaAdi, byte[] icerik) => Task.FromResult("/tmp/" + dosyaAdi);
    }
}
