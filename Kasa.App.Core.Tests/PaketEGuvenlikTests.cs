using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket E: Ayarlar › Güvenlik (hesabım, kullanıcılar, oturumlar, giriş günlüğü) ve risk kartı.</summary>
public class PaketEGuvenlikTests
{
    // Yalnız test için; gerçek bir parola değil (en az 8 karakter kuralını karşılar).
    private const string TestSifresi = "xxxxxxxx";

    private static readonly DateTime An = new(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc);
    private static SabitSaat Saat() => new(new DateTime(2026, 9, 24, 12, 0, 0));

    // ── Hesabım: şifre ──

    [Fact]
    public async Task Sifre_degisir_alanlar_temizlenir()
    {
        var api = new SahteApi { Hesap = new(1, "EMAR", "editor", "editor", true, true, false, 0, 30) };
        var vm = new HesapGuvenligiViewModel(api, Saat());
        await vm.YukleAsync();
        Assert.Contains(".env", vm.SifreBilgisi);

        vm.MevcutSifre = "eski-sifre";
        vm.YeniSifre = vm.YeniSifreTekrar = "yeni-sifre-1";
        await vm.SifreDegistirCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal(("eski-sifre", "yeni-sifre-1"), api.SonSifreDegistir);
        Assert.Equal("", vm.MevcutSifre);
        Assert.Equal("", vm.YeniSifre);
        Assert.NotNull(vm.Bilgi);
        Assert.DoesNotContain(".env", vm.SifreBilgisi);
    }

    [Theory]
    [InlineData("", "yeni-sifre-1", "yeni-sifre-1", "Mevcut şifreyi yazın.")]
    [InlineData("eski", "kisa", "kisa", "Yeni şifre en az 8 karakter olmalı.")]
    [InlineData("eski", "yeni-sifre-1", "yeni-sifre-2", "Yeni şifreler aynı değil.")]
    [InlineData("ayni-sifre", "ayni-sifre", "ayni-sifre", "Yeni şifre eskisiyle aynı olamaz.")]
    public async Task Sifre_dogrulamasi(string mevcut, string yeni, string tekrar, string hata)
    {
        var api = new SahteApi();
        var vm = new HesapGuvenligiViewModel(api) { MevcutSifre = mevcut, YeniSifre = yeni, YeniSifreTekrar = tekrar };
        await vm.SifreDegistirCommand.ExecuteAsync(null);
        Assert.Equal(hata, vm.Hata);
        Assert.Null(api.SonSifreDegistir);
    }

    [Fact]
    public async Task Yanlis_mevcut_sifre_sunucu_mesaji()
    {
        var api = new SahteApi { GuvenlikYazHatasi = new KasaApiException(HttpStatusCode.BadRequest, "Mevcut şifre hatalı.") };
        var vm = new HesapGuvenligiViewModel(api) { MevcutSifre = "x", YeniSifre = "yeni-sifre-1", YeniSifreTekrar = "yeni-sifre-1" };
        await vm.SifreDegistirCommand.ExecuteAsync(null);
        Assert.Equal("Mevcut şifre hatalı.", vm.Hata);
    }

    // ── Hesabım: iki adımlı giriş ──

    [Fact]
    public async Task Iki_adim_kurulumu_onaylanir_kodlar_bir_kez_gosterilir()
    {
        var api = new SahteApi();
        var vm = new HesapGuvenligiViewModel(api, Saat());
        await vm.YukleAsync();
        Assert.True(vm.KurulumBaslatGorunur);
        Assert.Equal("Kapalı", vm.IkiAdimDurumu);

        await vm.IkiAdimBaslatCommand.ExecuteAsync(null);
        Assert.True(vm.KurulumSurerken);
        Assert.False(vm.KurulumBaslatGorunur);
        Assert.Equal("JBSW Y3DP EHPK 3PXP", vm.KurulumSirMetni);

        vm.KurulumKodu = "123 456";
        vm.KurulumSifre = "sifre-123";
        await vm.IkiAdimOnaylaCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal("sifre-123", api.SonIkiAdimOnaySifre);
        Assert.Equal("", vm.KurulumSifre);
        Assert.False(vm.KurulumSurerken);
        Assert.True(vm.IkiAdimAcik);
        Assert.Equal(api.UretilecekKodlar, vm.KurtarmaKodlari);
        Assert.True(vm.KodlarGorunur);
        Assert.Contains("23456-789AB", vm.KurtarmaKodlariMetni);
        Assert.Equal("Açık · 3 kurtarma kodu kaldı", vm.IkiAdimDurumu);

        vm.KodlariGizleCommand.Execute(null);
        Assert.False(vm.KodlarGorunur);
    }

    [Fact]
    public async Task Iki_adim_kurulumunda_gecersiz_kod_sunucuya_gitmez()
    {
        var api = new SahteApi();
        var vm = new HesapGuvenligiViewModel(api);
        await vm.IkiAdimBaslatCommand.ExecuteAsync(null);
        vm.KurulumKodu = "12ab";
        await vm.IkiAdimOnaylaCommand.ExecuteAsync(null);
        Assert.Equal("Uygulamadaki 6 haneli kodu yazın.", vm.Hata);
        Assert.True(vm.KurulumSurerken);
    }

    [Fact]
    public async Task Iki_adim_yanlis_kod_sunucu_mesaji_kurulum_surer()
    {
        var api = new SahteApi();
        var vm = new HesapGuvenligiViewModel(api);
        await vm.IkiAdimBaslatCommand.ExecuteAsync(null);
        vm.KurulumKodu = "000000";
        vm.KurulumSifre = "sifre-123";
        await vm.IkiAdimOnaylaCommand.ExecuteAsync(null);
        Assert.Equal("Kod hatalı. Uygulamadaki güncel kodu girin.", vm.Hata);
        Assert.True(vm.KurulumSurerken);
        Assert.False(vm.KodlarGorunur);
    }

    [Fact]
    public async Task Iki_adim_kapatilir_sifre_ve_kodla()
    {
        var api = new SahteApi { Hesap = new(1, "EMAR", "emar", "editor", false, false, true, 5, 30) };
        var vm = new HesapGuvenligiViewModel(api);
        await vm.YukleAsync();
        Assert.True(vm.IkiAdimAcik);

        await vm.IkiAdimKapatCommand.ExecuteAsync(null);
        Assert.Equal("Şifrenizi yazın.", vm.Hata);

        vm.KapatSifre = "sifre-123";
        vm.KapatKod = " ABCDE-FGHJK ";
        await vm.IkiAdimKapatCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal(("sifre-123", "ABCDE-FGHJK"), api.SonIkiAdimKapat);
        Assert.False(vm.IkiAdimAcik);
        Assert.Equal("", vm.KapatSifre);
    }

    [Fact]
    public async Task Kurtarma_kodlari_yenilenir()
    {
        var api = new SahteApi { Hesap = new(1, "EMAR", "emar", "editor", false, false, true, 1, 30) };
        var vm = new HesapGuvenligiViewModel(api);
        await vm.YukleAsync();
        vm.YenileKod = "654321";
        vm.YenileSifre = "sifre-123";
        await vm.KurtarmaKodlariYenileCommand.ExecuteAsync(null);
        Assert.Equal("654321", api.SonKurtarmaYenileKodu);
        Assert.Equal("sifre-123", api.SonKurtarmaYenileSifre);
        Assert.Equal("", vm.YenileSifre);
        Assert.Equal(3, vm.KurtarmaKodlari.Count);
        Assert.Equal("Açık · 3 kurtarma kodu kaldı", vm.IkiAdimDurumu);
    }

    // ── Kullanıcılar ──

    private static KullaniciDto K(int id, string ad, string kullaniciAdi, string rol = "viewer", bool aktif = true, bool yerlesik = false)
        => new(id, ad, kullaniciAdi, rol, aktif, yerlesik, false, An, An.AddHours(1), "EMAR-LAPTOP", 1);

    [Fact]
    public async Task Kullanici_listesi_satir_metinleri()
    {
        var api = new SahteApi
        {
            KullanicilarListe = [K(1, "EMAR", "editor", "editor", yerlesik: true), K(2, "AHMET", "ahmet"), K(3, "PASİF", "pasif", aktif: false)],
        };
        var vm = new KullanicilarViewModel(api, Saat());
        await vm.YukleAsync();

        Assert.Equal(["EMAR", "AHMET", "PASİF"], vm.Kullanicilar.Select(k => k.AdSoyad));
        Assert.Equal("editor · Editör · sunucu hesabı", vm.Kullanicilar[0].Ayrinti);
        Assert.False(vm.Kullanicilar[0].Silinebilir);
        Assert.Equal("Son giriş 24.09.2026 10:00 · EMAR-LAPTOP · 1 açık oturum", vm.Kullanicilar[1].SonGiris);
    }

    [Fact]
    public async Task Kullanici_eklenir_varsayilan_rol_izleyici()
    {
        var api = new SahteApi();
        var vm = new KullanicilarViewModel(api, Saat())
        {
            YeniAdSoyad = " AHMET YILMAZ ", YeniKullaniciAdi = "ahmet", YeniSifre = TestSifresi,
        };
        await vm.EkleCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal(new KullaniciEkle("AHMET YILMAZ", "ahmet", "viewer", TestSifresi), api.SonKullaniciEkle);
        Assert.Contains(vm.Kullanicilar, k => k.KullaniciAdi == "ahmet");
        Assert.Null(vm.YeniKullaniciAdi);
        Assert.Equal("", vm.YeniSifre);
    }

    [Fact]
    public async Task Editor_rolu_secilerek_eklenir()
    {
        var api = new SahteApi();
        var vm = new KullanicilarViewModel(api) { YeniAdSoyad = "MEHMET", YeniKullaniciAdi = "mehmet", YeniSifre = TestSifresi };
        vm.YeniRolSecCommand.Execute(vm.YeniRoller.Single(r => r.Ad == KullanicilarViewModel.RolEditor));
        await vm.EkleCommand.ExecuteAsync(null);
        Assert.Equal("editor", api.SonKullaniciEkle!.Rol);
        Assert.True(vm.YeniRoller[0].Secili);               // form sıfırlanınca izleyiciye döner
    }

    [Theory]
    [InlineData("", "ahmet", TestSifresi, "Ad soyad yazın.")]
    [InlineData("A", "ah", TestSifresi, "Kullanıcı adı 3–50 karakter olmalı; harf, rakam, nokta, tire ve alt çizgi kullanılabilir.")]
    [InlineData("A", "ah met", TestSifresi, "Kullanıcı adı 3–50 karakter olmalı; harf, rakam, nokta, tire ve alt çizgi kullanılabilir.")]
    [InlineData("A", "ahmet", "kisa", "Şifre en az 8 karakter olmalı.")]
    public async Task Kullanici_ekleme_dogrulamasi(string ad, string kullaniciAdi, string sifre, string hata)
    {
        var api = new SahteApi();
        var vm = new KullanicilarViewModel(api) { YeniAdSoyad = ad, YeniKullaniciAdi = kullaniciAdi, YeniSifre = sifre };
        await vm.EkleCommand.ExecuteAsync(null);
        Assert.Equal(hata, vm.Hata);
        Assert.Null(api.SonKullaniciEkle);
    }

    [Fact]
    public async Task Turkce_harfli_kullanici_adi_gecerli()
    {
        var api = new SahteApi();
        var vm = new KullanicilarViewModel(api) { YeniAdSoyad = "Şükrü", YeniKullaniciAdi = "şükrü.ö", YeniSifre = TestSifresi };
        await vm.EkleCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal("şükrü.ö", api.SonKullaniciEkle!.KullaniciAdi);
    }

    [Fact]
    public async Task Kullanici_duzenlenir_rol_aktiflik_ve_sifre()
    {
        var api = new SahteApi { KullanicilarListe = [K(1, "EMAR", "editor", "editor", yerlesik: true), K(2, "AHMET", "ahmet")] };
        var vm = new KullanicilarViewModel(api, Saat());
        await vm.YukleAsync();
        var ahmet = vm.Kullanicilar.Single(k => k.Id == 2);

        vm.DuzenleCommand.Execute(ahmet);
        Assert.True(vm.DuzenAcik);
        Assert.True(vm.DuzenRolDegisebilir);
        Assert.True(vm.DuzenRoller[0].Secili);
        vm.DuzenAdSoyad = "AHMET Y.";
        vm.DuzenRolSecCommand.Execute(vm.DuzenRoller[1]);
        vm.DuzenAktif = false;
        vm.DuzenSifre = "yeni-sifre-9";
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(vm.Hata);
        Assert.Equal((2, new KullaniciGuncelle("AHMET Y.", "editor", false)), api.SonKullaniciGuncelle);
        Assert.Equal((2, "yeni-sifre-9"), api.SonKullaniciSifre);
        Assert.False(vm.DuzenAcik);
    }

    [Fact]
    public async Task Yerlesik_hesapta_rol_ve_aktiflik_degismez()
    {
        var api = new SahteApi { KullanicilarListe = [K(1, "EMAR", "editor", "editor", yerlesik: true)] };
        var vm = new KullanicilarViewModel(api);
        await vm.YukleAsync();
        vm.DuzenleCommand.Execute(vm.Kullanicilar[0]);
        Assert.False(vm.DuzenRolDegisebilir);

        vm.DuzenRolSecCommand.Execute(vm.DuzenRoller[0]);     // yok sayılır
        Assert.True(vm.DuzenRoller[1].Secili);
        vm.DuzenAktif = false;
        vm.DuzenAdSoyad = "EMAR GLOBAL";
        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal((1, new KullaniciGuncelle("EMAR GLOBAL", "editor", true)), api.SonKullaniciGuncelle);
    }

    [Fact]
    public async Task Yerlesik_hesabin_sifresi_buradan_degismez()
    {
        var api = new SahteApi { KullanicilarListe = [K(1, "EMAR", "editor", "editor", yerlesik: true)] };
        var vm = new KullanicilarViewModel(api);
        await vm.YukleAsync();
        vm.DuzenleCommand.Execute(vm.Kullanicilar[0]);
        vm.DuzenSifre = "yeni-sifre-9";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal("Bu hesabın şifresi yalnız kendi Hesabım bölümünden değişir.", vm.Hata);
        Assert.Null(api.SonKullaniciGuncelle);
    }

    [Fact]
    public async Task Satir_eylemleri_oturum_iki_adim_ve_iki_basisli_silme()
    {
        var api = new SahteApi { KullanicilarListe = [K(1, "EMAR", "editor", "editor", yerlesik: true), K(2, "AHMET", "ahmet")] };
        var vm = new KullanicilarViewModel(api);
        await vm.YukleAsync();
        var ahmet = vm.Kullanicilar.Single(k => k.Id == 2);

        await vm.OturumlariniKapatCommand.ExecuteAsync(ahmet);
        Assert.Equal(2, api.SonOturumlariKapatilan);
        await vm.IkiAdimKapatCommand.ExecuteAsync(vm.Kullanicilar.Single(k => k.Id == 2));
        Assert.Equal(2, api.SonIkiAdimKapatilan);

        ahmet = vm.Kullanicilar.Single(k => k.Id == 2);
        await vm.SilCommand.ExecuteAsync(ahmet);
        Assert.Null(api.SonSilinenKullanici);
        Assert.Equal("Emin misiniz? Sil", ahmet.SilMetni);
        await vm.SilCommand.ExecuteAsync(ahmet);
        Assert.Equal(2, api.SonSilinenKullanici);
        Assert.DoesNotContain(vm.Kullanicilar, k => k.Id == 2);
    }

    [Fact]
    public async Task Ortak_izleyici_sifresi_iki_basista_kaldirilir()
    {
        var api = new SahteApi();
        var vm = new KullanicilarViewModel(api);
        await vm.IzleyiciSifresiniKaldirCommand.ExecuteAsync(null);
        Assert.False(api.IzleyiciSifresiKaldirildi);
        Assert.StartsWith("Emin misiniz?", vm.IzleyiciKaldirMetni);
        await vm.IzleyiciSifresiniKaldirCommand.ExecuteAsync(null);
        Assert.True(api.IzleyiciSifresiKaldirildi);
        Assert.False(vm.IzleyiciKaldirOnayBekliyor);
    }

    // ── Oturumlar ve giriş günlüğü ──

    private static OturumDto O(string id, string? ad, string rol, bool bu = false, string? cihaz = "EMAR-LAPTOP")
        => new(id, 1, ad, rol, cihaz, "88.1.2.3", An, An.AddMinutes(30), An.AddDays(30), false, bu);

    private static GirisKaydiDto G(int id, bool basarili, string? neden = null)
        => new(id, An.AddMinutes(-id), "emar", "EMAR", "editor", basarili, neden, "88.1.2.3", "EMAR-LAPTOP");

    [Fact]
    public async Task Oturumlar_ve_giris_gunlugu_yuklenir()
    {
        var api = new SahteApi
        {
            OturumlarListe = [O("a", "AHMET", "viewer"), O("b", "EMAR", "editor", bu: true)],
            GirisKayitlariListe = Enumerable.Range(1, 120).Select(i => G(i, i % 3 != 0, i % 3 == 0 ? "Hatalı şifre" : null)).ToList(),
            GuvenlikAyari = new(7, 30, 180),
        };
        var vm = new OturumlarViewModel(api, Saat());
        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Equal("Açık oturumlar (2)", vm.OturumBaslik);
        Assert.True(vm.Oturumlar[0].BuOturum);                 // bu cihaz önce
        Assert.False(vm.Oturumlar[0].Kapatilabilir);
        Assert.Equal("AHMET · İzleyici", vm.Oturumlar[1].Baslik);
        Assert.Equal("EMAR-LAPTOP · 88.1.2.3 · son görülme 24.09.2026 09:30 · bitiş 24.10.2026", vm.Oturumlar[1].Ayrinti);

        Assert.Equal(OturumlarViewModel.SayfaBoyu, vm.Girisler.Count);
        Assert.Equal("1–50 / 120", vm.SayfaMetni);
        Assert.False(vm.OncekiVar);
        Assert.True(vm.SonrakiVar);
        Assert.Equal("EMAR (emar)", vm.Girisler[0].Kim);
        Assert.Equal("24.09.2026 08:57 · Editör · EMAR-LAPTOP · 88.1.2.3 · Hatalı şifre", vm.Girisler[2].Ayrinti);
        Assert.Equal("Başarısız", vm.Girisler[2].SonucAdi);

        Assert.True(vm.EditorOturumuKisa);
        Assert.Contains("editör oturumu 7 gün", vm.OturumSuresiMetni);
    }

    [Fact]
    public async Task Giris_gunlugu_sayfalanir_ve_basarisizlar_suzulur()
    {
        var api = new SahteApi { GirisKayitlariListe = Enumerable.Range(1, 120).Select(i => G(i, i % 3 != 0)).ToList() };
        var vm = new OturumlarViewModel(api, Saat());
        await vm.YukleAsync();

        await vm.SonrakiCommand.ExecuteAsync(null);
        await vm.SonrakiCommand.ExecuteAsync(null);
        Assert.Equal("101–120 / 120", vm.SayfaMetni);
        Assert.False(vm.SonrakiVar);
        Assert.Equal(20, vm.Girisler.Count);
        await vm.SonrakiCommand.ExecuteAsync(null);             // son sayfada etkisiz
        Assert.Equal((false, 50, 100), api.GirisKaydiCagrilari[^1]);

        await vm.OncekiCommand.ExecuteAsync(null);
        Assert.Equal("51–100 / 120", vm.SayfaMetni);

        vm.YalnizBasarisiz = true;
        Assert.Equal((true, 50, 0), api.GirisKaydiCagrilari[^1]);
        Assert.Equal(40, vm.Toplam);
        Assert.All(vm.Girisler, g => Assert.False(g.Basarili));
    }

    [Fact]
    public async Task Tek_oturum_kapatilir_bu_oturum_kapatilamaz()
    {
        var api = new SahteApi { OturumlarListe = [O("a", "AHMET", "viewer"), O("b", "EMAR", "editor", bu: true)] };
        var vm = new OturumlarViewModel(api, Saat());
        await vm.YukleAsync();

        await vm.OturumKapatCommand.ExecuteAsync(vm.Oturumlar.Single(o => o.Id == "b"));
        Assert.NotNull(vm.Hata);
        Assert.Null(api.SonKapatilanOturum);

        await vm.OturumKapatCommand.ExecuteAsync(vm.Oturumlar.Single(o => o.Id == "a"));
        Assert.Null(vm.Hata);
        Assert.Equal("a", api.SonKapatilanOturum);
        Assert.Equal("Açık oturumlar (1)", vm.OturumBaslik);
    }

    [Fact]
    public async Task Ortak_sifreli_oturum_ve_cihazsiz_oturum_metni()
    {
        var api = new SahteApi { OturumlarListe = [O("a", "Ortak izleyici şifresi", "viewer", cihaz: null)] };
        var vm = new OturumlarViewModel(api, Saat());
        await vm.YukleAsync();
        Assert.Equal("Ortak izleyici şifresi · İzleyici", vm.Oturumlar[0].Baslik);
        Assert.StartsWith("cihaz bilinmiyor · ", vm.Oturumlar[0].Ayrinti);
    }

    [Fact]
    public async Task Editor_oturum_suresi_7_ya_da_30_gun_kaydedilir()
    {
        var api = new SahteApi();
        var vm = new OturumlarViewModel(api, Saat());
        await vm.YukleAsync();
        Assert.False(vm.EditorOturumuKisa);

        vm.EditorOturumuKisa = true;
        await vm.OturumSuresiKaydetCommand.ExecuteAsync(null);
        Assert.Equal(7, api.SonEditorOturumGun);
        Assert.Equal(7, vm.EditorOturumGun);

        vm.EditorOturumuKisa = false;
        await vm.OturumSuresiKaydetCommand.ExecuteAsync(null);
        Assert.Equal(30, api.SonEditorOturumGun);
    }

    [Fact]
    public async Task Guvenlik_bolumu_uc_alt_modeli_birlikte_yukler()
    {
        var api = new SahteApi { OturumlarListe = [O("a", "AHMET", "viewer")] };
        var vm = new GuvenlikViewModel(new HesapGuvenligiViewModel(api), new KullanicilarViewModel(api), new OturumlarViewModel(api));
        await vm.YukleAsync();
        Assert.Equal("EMAR · emar", vm.Hesap.HesapMetni);
        Assert.Single(vm.Kullanicilar.Kullanicilar);
        Assert.Single(vm.Oturumlar.Oturumlar);
        Assert.Equal(1, api.KullanicilarCagri);
    }

    // ── Sistem ve risk kartı ──

    [Fact]
    public async Task Risk_karti_izleyicide_sunucuya_sormaz()
    {
        var api = new SahteApi();
        var vm = new RiskKartiViewModel(api) { EditorMu = false };
        await vm.YukleAsync();
        Assert.False(vm.Gorunur);
        Assert.Equal(0, api.SistemRiskiCagri);
    }

    [Fact]
    public async Task Risk_karti_uyarilari_kirmizi_once_gosterir()
    {
        var api = new SahteApi
        {
            SistemRiski = new("kirmizi",
                [
                    new(RiskSeviyesi.Sari, "disk", "Disk azalıyor", "Boş alan 900 MB."),
                    new(RiskSeviyesi.Kirmizi, "yedek", "Sunucu dışı yedek 3 gündür yok", "Son başarılı yükleme 72 saat önce."),
                ],
                An, new YedekDogrulamaDto(An.AddHours(-6), "kasa-2026-09-24.db", An.AddHours(-7), false, "Cariler: yedekte 0, canlıda 3"),
                900, 6),
        };
        var vm = new RiskKartiViewModel(api, Saat()) { EditorMu = true };
        await vm.YukleAsync();

        Assert.True(vm.Gorunur);
        Assert.Equal("kirmizi", vm.Durum);
        Assert.Equal("Dikkat", vm.DurumAdi);
        Assert.Equal("1 kırmızı, 1 sarı uyarı", vm.DurumMetni);
        Assert.True(vm.Maddeler[0].KirmiziMi);
        Assert.Equal("kirmizi", vm.Maddeler[0].Ton);
        Assert.Equal("Disk azalıyor", vm.Maddeler[1].Baslik);
        Assert.Equal("Son yedek doğrulaması: 24.09.2026 03:00 · BAŞARISIZ", vm.DogrulamaMetni);
        Assert.Equal("Boş disk: 900 MB", vm.DiskMetni);
        Assert.Equal("Dün başarısız giriş: 6", vm.GirisMetni);
        Assert.False(vm.SorunYok);
    }

    [Fact]
    public async Task Risk_karti_sorun_yoksa_yolunda()
    {
        var api = new SahteApi();
        var vm = new RiskKartiViewModel(api, Saat()) { EditorMu = true };
        await vm.YukleAsync();
        Assert.True(vm.SorunYok);
        Assert.Equal("Yolunda", vm.DurumAdi);
        Assert.Equal("Her şey yolunda.", vm.DurumMetni);
        Assert.Equal("Yedek doğrulaması henüz yapılmadı.", vm.DogrulamaMetni);
        Assert.Equal("Boş disk: 48,8 GB", vm.DiskMetni);
    }

    [Fact]
    public async Task Risk_karti_hatasi_gosterilir()
    {
        var vm = new RiskKartiViewModel(new SahteApi { YuklemeHatasi = new KasaApiException(HttpStatusCode.InternalServerError) }) { EditorMu = true };
        await vm.YukleAsync();
        Assert.Equal(HataMesaji.SunucuHatasi, vm.Hata);
        Assert.False(vm.SorunYok);
    }
}
