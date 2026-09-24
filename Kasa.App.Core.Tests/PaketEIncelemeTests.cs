using System.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Paket E inceleme düzeltmeleri (uygulama tarafı): iki adımı açmak ve kodları yenilemek şifre ister,
/// kurulum anahtarı QR koduyla gösterilir ve panoya kopyalanır, "Kod bekleniyor" kırmızı başarısızlık
/// değildir, toplanan denemeler sayısıyla yazılır, kendi satırınızda yıkıcı düğmeler yoktur.
/// </summary>
public class PaketEIncelemeTests
{
    private static readonly DateTime An = new(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc);
    private static SabitSaat Saat() => new(new DateTime(2026, 9, 24, 12, 0, 0));

    private sealed class SahtePano : IPano
    {
        public List<string> Yazilanlar { get; } = new();
        public Task YazAsync(string metin)
        {
            Yazilanlar.Add(metin);
            return Task.CompletedTask;
        }
    }

    // ── İki adım: şifre ──

    [Fact]
    public async Task Iki_adimi_acmak_sifresiz_sunucuya_gitmez()
    {
        var api = new SahteApi();
        var vm = new HesapGuvenligiViewModel(api, Saat());
        await vm.IkiAdimBaslatCommand.ExecuteAsync(null);
        vm.KurulumKodu = "123456";
        await vm.IkiAdimOnaylaCommand.ExecuteAsync(null);

        Assert.Equal("Şifrenizi yazın.", vm.Hata);
        Assert.Null(api.SonIkiAdimOnaySifre);
        Assert.True(vm.KurulumSurerken);
        Assert.False(vm.IkiAdimAcik);
    }

    [Fact]
    public async Task Yanlis_sifrede_kurulum_surer_sifre_sunucu_mesajiyla()
    {
        var api = new SahteApi();
        var vm = new HesapGuvenligiViewModel(api, Saat());
        await vm.IkiAdimBaslatCommand.ExecuteAsync(null);
        api.GuvenlikYazHatasi = new KasaApiException(System.Net.HttpStatusCode.BadRequest, "Şifre hatalı.");
        vm.KurulumKodu = "123456";
        vm.KurulumSifre = "yanlis";
        await vm.IkiAdimOnaylaCommand.ExecuteAsync(null);

        Assert.Equal("Şifre hatalı.", vm.Hata);
        Assert.True(vm.KurulumSurerken);
        Assert.True(vm.KurulumQrVar);
    }

    [Fact]
    public async Task Kurtarma_kodlarini_yenilemek_sifresiz_sunucuya_gitmez()
    {
        var api = new SahteApi { Hesap = new(1, "EMAR", "emar", "editor", false, false, true, 1, 30) };
        var vm = new HesapGuvenligiViewModel(api, Saat());
        await vm.YukleAsync();
        vm.YenileKod = "654321";
        await vm.KurtarmaKodlariYenileCommand.ExecuteAsync(null);

        Assert.Equal("Şifrenizi yazın.", vm.Hata);
        Assert.Null(api.SonKurtarmaYenileKodu);
        Assert.Empty(vm.KurtarmaKodlari);
    }

    [Fact]
    public async Task Vazgecince_yazilan_sifre_silinir()
    {
        var vm = new HesapGuvenligiViewModel(new SahteApi(), Saat());
        await vm.IkiAdimBaslatCommand.ExecuteAsync(null);
        vm.KurulumSifre = "sifre-123";
        vm.KurulumIptalCommand.Execute(null);
        Assert.Equal("", vm.KurulumSifre);
        Assert.False(vm.KurulumSurerken);
    }

    // ── QR kodu ──

    [Fact]
    public async Task Kurulumda_adresin_QR_kodu_uretilir_kapaninca_kalkar()
    {
        var api = new SahteApi();
        var vm = new HesapGuvenligiViewModel(api, Saat());
        var bildirilen = new List<string?>();
        vm.PropertyChanged += (_, e) => bildirilen.Add(e.PropertyName);
        Assert.Null(vm.KurulumQr);
        Assert.False(vm.KurulumQrVar);

        await vm.IkiAdimBaslatCommand.ExecuteAsync(null);
        Assert.True(vm.KurulumQrVar);
        Assert.NotNull(vm.KurulumQr);
        Assert.Equal(QrKodu.Olustur(api.Kurulum.Adres, QrSeviye.M).Satirlar(), vm.KurulumQr!.Satirlar());
        Assert.Contains(nameof(vm.KurulumQr), bildirilen);
        Assert.Contains(nameof(vm.KurulumQrVar), bildirilen);

        vm.KurulumKodu = "123456";
        vm.KurulumSifre = "sifre-123";
        await vm.IkiAdimOnaylaCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Null(vm.KurulumQr);
        Assert.False(vm.KurulumQrVar);
    }

    [Fact]
    public async Task Sigmayan_adreste_QR_yok_anahtar_elle_yazilir()
    {
        var api = new SahteApi { Kurulum = new("JBSWY3DPEHPK3PXP", "otpauth://totp/x?secret=" + new string('A', 3000)) };
        var vm = new HesapGuvenligiViewModel(api, Saat());
        await vm.IkiAdimBaslatCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.True(vm.KurulumSurerken);
        Assert.False(vm.KurulumQrVar);
        Assert.Equal("JBSW Y3DP EHPK 3PXP", vm.KurulumSirMetni);
    }

    // ── Panoya kopyalama ──

    [Fact]
    public async Task Anahtar_adres_ve_kodlar_panoya_kopyalanir()
    {
        var api = new SahteApi();
        var pano = new SahtePano();
        var vm = new HesapGuvenligiViewModel(api, Saat(), pano);

        await vm.SirKopyalaCommand.ExecuteAsync(null);         // kurulum yokken etkisiz
        Assert.Empty(pano.Yazilanlar);

        await vm.IkiAdimBaslatCommand.ExecuteAsync(null);
        await vm.SirKopyalaCommand.ExecuteAsync(null);
        Assert.Equal("JBSWY3DPEHPK3PXP", pano.Yazilanlar[^1]);
        Assert.StartsWith("Anahtar panoya kopyalandı", vm.Bilgi);

        await vm.AdresKopyalaCommand.ExecuteAsync(null);
        Assert.Equal(api.Kurulum.Adres, pano.Yazilanlar[^1]);
        Assert.Equal("Kurulum adresi panoya kopyalandı.", vm.Bilgi);

        vm.KurulumKodu = "123456";
        vm.KurulumSifre = "sifre-123";
        await vm.IkiAdimOnaylaCommand.ExecuteAsync(null);
        await vm.KodlariKopyalaCommand.ExecuteAsync(null);
        Assert.Equal(string.Join(Environment.NewLine, api.UretilecekKodlar), pano.Yazilanlar[^1]);
        Assert.StartsWith("Kurtarma kodları panoya kopyalandı", vm.Bilgi);
        Assert.Null(vm.Hata);

        vm.KodlariGizleCommand.Execute(null);
        await vm.KodlariKopyalaCommand.ExecuteAsync(null);     // kodlar gizlendikten sonra kopyalanacak bir şey yok
        Assert.Equal(3, pano.Yazilanlar.Count);
    }

    [Fact]
    public async Task Pano_yoksa_anlasilir_hata()
    {
        var vm = new HesapGuvenligiViewModel(new SahteApi(), Saat());
        await vm.IkiAdimBaslatCommand.ExecuteAsync(null);
        await vm.SirKopyalaCommand.ExecuteAsync(null);
        Assert.Equal(HesapGuvenligiViewModel.PanoYokMesaji, vm.Hata);
        Assert.Null(vm.Bilgi);
    }

    // ── Giriş günlüğü satırı ──

    private static GirisKaydiDto G(bool basarili, string? neden, int tekrar = 1, DateTime? son = null)
        => new(1, An, "emar", "EMAR", "editor", basarili, neden, "88.1.2.3", "EMAR-LAPTOP", tekrar, son);

    [Fact]
    public void Kod_bekleniyor_gri_ara_adim_olarak_yazilir()
    {
        var s = new GirisSatiri(G(false, "Kod bekleniyor"), TimeZoneInfo.Utc);
        Assert.True(s.AraAdim);
        Assert.Equal("Kod bekleniyor", s.SonucAdi);
        Assert.Equal(GirisSatiri.TonAra, s.Ton);
        Assert.Equal("24.09.2026 09:00 · Editör · EMAR-LAPTOP · 88.1.2.3", s.Ayrinti);   // neden çipte, tekrar yok

        var hata = new GirisSatiri(G(false, "Hatalı şifre"), TimeZoneInfo.Utc);
        Assert.False(hata.AraAdim);
        Assert.Equal("Başarısız", hata.SonucAdi);
        Assert.Equal(GirisSatiri.TonBasarisiz, hata.Ton);
        Assert.EndsWith("· Hatalı şifre", hata.Ayrinti);

        var ok = new GirisSatiri(G(true, null), TimeZoneInfo.Utc);
        Assert.Equal("Başarılı", ok.SonucAdi);
        Assert.Equal(GirisSatiri.TonBasarili, ok.Ton);
    }

    [Fact]
    public void Toplanan_denemeler_sayi_ve_son_saatle_yazilir()
    {
        var tz = TimeZoneInfo.CreateCustomTimeZone("TR", TimeSpan.FromHours(3), "TR", "TR");
        var s = new GirisSatiri(G(false, "Hatalı şifre", 25, An.AddMinutes(40)), tz);
        Assert.Equal("24.09.2026 12:00 · 25 deneme, son 12:40 · Editör · EMAR-LAPTOP · 88.1.2.3 · Hatalı şifre", s.Ayrinti);

        var sonsuz = new GirisSatiri(G(false, "Hatalı şifre", 3), TimeZoneInfo.Utc);
        Assert.Contains("· 3 deneme ·", sonsuz.Ayrinti);
        Assert.DoesNotContain("deneme", new GirisSatiri(G(false, "Hatalı şifre"), TimeZoneInfo.Utc).Ayrinti);
    }

    [Fact]
    public async Task Yalniz_basarisizlar_kod_beklemeyi_gostermez()
    {
        var api = new SahteApi { GirisKayitlariListe = [G(false, "Kod bekleniyor"), G(false, "Hatalı şifre"), G(true, null)] };
        var vm = new OturumlarViewModel(api, Saat());
        await vm.YukleAsync();
        Assert.Equal(3, vm.Girisler.Count);
        vm.YalnizBasarisiz = true;
        await Task.Yield();
        Assert.Equal(["Başarısız"], vm.Girisler.Select(g => g.SonucAdi));
    }

    [Fact]
    public async Task Oturum_suresi_mesaji_suresini_asan_oturumlardan_soz_eder()
    {
        var vm = new OturumlarViewModel(new SahteApi(), Saat()) { EditorOturumuKisa = true };
        await vm.OturumSuresiKaydetCommand.ExecuteAsync(null);
        Assert.Equal("Editör oturumu artık 7 gün sürer; bu süreyi aşmış açık oturumlar bir sonraki istekte kapanır.", vm.Bilgi);
    }

    // ── Kullanıcılar: kendi satırı ──

    private static KullaniciDto K(int id, string ad, string rol, bool ben = false, bool ikiAdim = false, bool yerlesik = false)
        => new(id, ad, ad.ToLowerInvariant(), rol, true, yerlesik, ikiAdim, An, An, "EMAR-LAPTOP", 2, ben);

    private static async Task<(SahteApi Api, KullanicilarViewModel Vm)> Kullanicilar()
    {
        var api = new SahteApi { KullanicilarListe = [K(1, "EMAR", "editor", ben: true, ikiAdim: true), K(2, "AHMET", "editor", ikiAdim: true)] };
        var vm = new KullanicilarViewModel(api, Saat());
        await vm.YukleAsync();
        return (api, vm);
    }

    [Fact]
    public async Task Kendi_satirinda_yikici_dugmeler_yok()
    {
        var (_, vm) = await Kullanicilar();
        var ben = vm.Kullanicilar.Single(k => k.Id == 1);
        var ahmet = vm.Kullanicilar.Single(k => k.Id == 2);

        Assert.True(ben.Ben);
        Assert.Equal("emar · Editör · siz · iki adımlı giriş açık", ben.Ayrinti);
        Assert.False(ben.Silinebilir);
        Assert.False(ben.OturumlariKapatilabilir);
        Assert.False(ben.IkiAdimKapatilabilir);

        Assert.False(ahmet.Ben);
        Assert.DoesNotContain("siz", ahmet.Ayrinti);
        Assert.True(ahmet.Silinebilir);
        Assert.True(ahmet.OturumlariKapatilabilir);
        Assert.True(ahmet.IkiAdimKapatilabilir);
    }

    [Fact]
    public async Task Kendi_hesabina_yikici_komutlar_sunucuya_gitmez()
    {
        var (api, vm) = await Kullanicilar();
        var ben = vm.Kullanicilar.Single(k => k.Id == 1);

        await vm.OturumlariniKapatCommand.ExecuteAsync(ben);
        Assert.Equal(KullanicilarViewModel.KendiOturumlariMesaji, vm.Hata);
        Assert.Null(api.SonOturumlariKapatilan);

        await vm.IkiAdimKapatCommand.ExecuteAsync(ben);
        Assert.Equal(KullanicilarViewModel.KendiIkiAdimMesaji, vm.Hata);
        Assert.Null(api.SonIkiAdimKapatilan);

        await vm.SilCommand.ExecuteAsync(ben);
        await vm.SilCommand.ExecuteAsync(ben);
        Assert.Equal(KullanicilarViewModel.KendiSilMesaji, vm.Hata);
        Assert.False(ben.SilOnayBekliyor);
        Assert.Null(api.SonSilinenKullanici);

        var ahmet = vm.Kullanicilar.Single(k => k.Id == 2);
        await vm.OturumlariniKapatCommand.ExecuteAsync(ahmet);
        Assert.Null(vm.Hata);
        Assert.Equal(2, api.SonOturumlariKapatilan);
    }

    [Fact]
    public async Task Kendi_hesabini_duzenlerken_rol_aktiflik_ve_sifre_degismez()
    {
        var (api, vm) = await Kullanicilar();
        vm.DuzenleCommand.Execute(vm.Kullanicilar.Single(k => k.Id == 1));
        Assert.False(vm.DuzenRolDegisebilir);
        Assert.False(vm.DuzenSifreDegisebilir);

        vm.DuzenRolSecCommand.Execute(vm.DuzenRoller[0]);        // izleyici seçilemez
        Assert.True(vm.DuzenRoller[1].Secili);

        vm.DuzenSifre = "yeni-sifre-1";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(KullanicilarViewModel.KendiSifreMesaji, vm.Hata);
        Assert.Null(api.SonKullaniciSifre);
        Assert.Null(api.SonKullaniciGuncelle);

        vm.DuzenSifre = "";
        vm.DuzenAktif = false;
        vm.DuzenAdSoyad = "EMAR SEVİNÇ";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal((1, new KullaniciGuncelle("EMAR SEVİNÇ", "editor", true)), api.SonKullaniciGuncelle);

        vm.DuzenleCommand.Execute(vm.Kullanicilar.Single(k => k.Id == 2));
        Assert.True(vm.DuzenRolDegisebilir);
        Assert.True(vm.DuzenSifreDegisebilir);
    }
}
