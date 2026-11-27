using Kasa.ApiClient;
using Kasa.App.Controls;
using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Kasa kontrolü sayfası (docs/specs/2026-11-26-slop-temizligi.md §8; Kasalar'dan ayrıldı): alt limit uyarıları, karşılaştırma
/// geçmişi ve hareket dökümü her zaman; gerçek bakiye karşılaştırması yalnız "Yeni karşılaştırma" ile açılır, kayıt ve "Vazgeç" kapatır,
/// "Vazgeç" temizler. "Bu kontrolden beri değişenler" yalnız istenince görünür ve kapatılır. Editöre özgü parçalar izleyicide yoktur.
/// Ana düğme sayısı ve içi gizli sarmalayıcılar DugmeVeRenkKurallariTests.TakipSayfalari ile, kısayollar YerlesimVeKisayolTests ile
/// sınanır.</summary>
public class KasaKontrolGorunumTests
{
    private static readonly KasaKontrolDto Kayit = new(1, new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(3)), 100, 90, -10, "Sayım");

    private static async Task<(KasaKontrolPage Sayfa, KasaKontrolViewModel Vm, KasaKontrolVeAylikGiderTests.Sahte Api)> Kur(Rol rol = Rol.Editor)
    {
        GorunumOrtami.Kur();
        var api = new KasaKontrolVeAylikGiderTests.Sahte { Gecmis = [Kayit] };
        var vm = new KasaKontrolViewModel(api, TestOturumu.Ac(rol));
        var sayfa = new KasaKontrolPage(vm);
        await vm.YukleAsync();
        return (sayfa, vm, api);
    }

    private static List<string> KartBasliklari(Page sayfa) => sayfa.GetVisualTreeDescendants().OfType<Label>()
        .Where(l => DugmeVeRenkKurallariTests.Gorunur(l) && ReferenceEquals(l.Style, Application.Current!.Resources["LblSection"])).Select(l => l.Text).ToList();

    private static List<string> Dugmeler(Page sayfa) => sayfa.GetVisualTreeDescendants().OfType<Button>()
        .Where(b => DugmeVeRenkKurallariTests.Gorunur(b)).Select(b => b.Text).ToList();

    private static List<string?> Metinler(Page sayfa) => sayfa.GetVisualTreeDescendants().OfType<Label>()
        .Where(l => DugmeVeRenkKurallariTests.Gorunur(l)).Select(l => l.Text).ToList();

    private static async Task Bekle(Func<bool> kosul)
    {
        for (var i = 0; i < 100 && !kosul(); i++)
            await Task.Delay(10);
        Assert.True(kosul());
    }

    [Fact]
    public async Task Karsilastirma_formu_istenince_acilir_vazgec_temizleyip_kapatir()
    {
        var (sayfa, vm, _) = await Kur();
        Assert.Equal(["Alt limit uyarıları", "Karşılaştırma geçmişi", "Kasa hareket dökümü"], KartBasliklari(sayfa));
        Assert.Contains("MEZAT: bakiye -20,00 ₺, alt limit 10,00 ₺", Metinler(sayfa));
        Assert.Contains("Yeni karşılaştırma", Dugmeler(sayfa));
        Assert.Contains("Alt limitleri düzenle", Dugmeler(sayfa));
        Assert.Contains("Değişenleri göster", Dugmeler(sayfa));

        ((IButtonController)sayfa.GetVisualTreeDescendants().OfType<Button>().Single(b => b.Text == "Yeni karşılaştırma")).SendClicked();
        Assert.True(vm.FormAcik);
        Assert.Equal(["Alt limit uyarıları", "Karşılaştırma geçmişi", "Gerçek bakiye karşılaştırması", "Kasa hareket dökümü"], KartBasliklari(sayfa));
        vm.GercekBakiye = 90;
        vm.Not = "Sayım";
        await vm.OnizleCommand.ExecuteAsync(null);
        Assert.Contains(Metinler(sayfa), m => m?.StartsWith("Sistemdeki genel kasa: 100,00 ₺", StringComparison.Ordinal) == true);
        vm.Not = "";
        await vm.KaydetCommand.ExecuteAsync(null);              // fark var, açıklama yok: hata başlıkta
        Assert.Equal(KasaKontrolViewModel.NotZorunluMesaji, vm.Hata);

        vm.FormKapatCommand.Execute(null);                      // kapalı formun hatası kalmaz (ekran denemesi)
        Assert.False(vm.FormAcik);
        Assert.Null(vm.Hata);
        Assert.Equal((0m, "", (string?)null), (vm.GercekBakiye, vm.Not, vm.Karsilastirma));
        Assert.DoesNotContain("Gerçek bakiye karşılaştırması", KartBasliklari(sayfa));
    }

    [Fact]
    public async Task Kayit_formu_kapatir_hata_varsa_form_acik_kalir()
    {
        var (sayfa, vm, api) = await Kur();
        vm.FormAcCommand.Execute(null);
        vm.GercekBakiye = 90;
        await vm.OnizleCommand.ExecuteAsync(null);
        await vm.KaydetCommand.ExecuteAsync(null);              // fark var, açıklama yok
        Assert.Empty(api.Kontroller);
        Assert.True(vm.FormAcik);
        Assert.Equal(KasaKontrolViewModel.NotZorunluMesaji, vm.Hata);

        vm.Not = "Sayım farkı";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Single(api.Kontroller);
        Assert.False(vm.FormAcik);
        Assert.Equal((0m, ""), (vm.GercekBakiye, vm.Not));
        Assert.Equal(2, vm.Gecmis.Count);
        Assert.Contains("değiştirilmedi", vm.Mesaj);
        Assert.DoesNotContain("Gerçek bakiye karşılaştırması", KartBasliklari(sayfa));
    }

    [Fact]
    public async Task Degisenler_istenince_acilir_kapat_ile_kapanir()
    {
        var (sayfa, vm, api) = await Kur();
        Assert.DoesNotContain("Bu kontrolden beri değişenler", KartBasliklari(sayfa));

        ((IButtonController)sayfa.GetVisualTreeDescendants().OfType<Button>().Single(b => b.Text == "Değişenleri göster")).SendClicked();
        await Bekle(() => vm.Secili is not null);
        Assert.Equal([1], api.SonrasiIstekleri);
        Assert.Contains("Bu kontrolden beri değişenler", KartBasliklari(sayfa));
        Assert.Contains("Açıklamayı kaydet", Dugmeler(sayfa));

        vm.FarkAciklamasi = "Sayım hatası";
        ((IButtonController)sayfa.GetVisualTreeDescendants().OfType<Button>().Single(b => b.Text == "Kapat" && DugmeVeRenkKurallariTests.Gorunur(b))).SendClicked();
        Assert.Null(vm.Secili);
        Assert.Equal("", vm.FarkAciklamasi);
        Assert.DoesNotContain("Bu kontrolden beri değişenler", KartBasliklari(sayfa));
    }

    /// <summary>İzleyici Kasalar'da gördüğünü burada da görür (uyarılar, geçmiş, döküm); karşılaştırma, açıklama ve Ayarlar bağlantısı
    /// yalnız editörde. Ctrl+N izleyicide yutulmaz.</summary>
    [Fact]
    public async Task Izleyici_gecmisi_uyarilari_ve_dokumu_gorur_karsilastirma_yapamaz()
    {
        var (sayfa, vm, _) = await Kur(Rol.Izleyici);
        Assert.Equal(["Alt limit uyarıları", "Karşılaştırma geçmişi", "Kasa hareket dökümü"], KartBasliklari(sayfa));
        var dugmeler = Dugmeler(sayfa);
        Assert.DoesNotContain("Yeni karşılaştırma", dugmeler);
        Assert.DoesNotContain("Alt limitleri düzenle", dugmeler);
        Assert.DoesNotContain("Değişenleri göster", dugmeler);
        Assert.Contains("Dökümü göster", dugmeler);
        Assert.Null(((IKisayolHedefi)sayfa).KisayolKomutu(Kisayol.Yeni));
        vm.FormAcCommand.Execute(null);
        Assert.False(vm.FormAcik);
    }

    /// <summary>Başlık menü adıyla aynı; F5 (başlığın Yenile'siyle aynı yol) sayfayı yeniler. Döküm istenmeden boş liste metni yazılmaz,
    /// sonuçtaki hareket sayısı özettedir.</summary>
    [Fact]
    public async Task Baslik_menu_adiyla_ayni_F5_yeniler_dokum_istenince_gelir()
    {
        var (sayfa, vm, api) = await Kur();
        var menuAdi = MenuModeli.Duzen.SelectMany(g => g.Ogeler).Single(o => o.Bolum == Bolum.KasaKontrolu).Baslik;
        Assert.Equal((menuAdi, menuAdi), (sayfa.Title, sayfa.GetVisualTreeDescendants().OfType<SayfaBasligi>().Single().Baslik));

        api.Gecmis = [Kayit with { Id = 2 }, Kayit];
        Assert.True(Kisayollar.Uygula(sayfa, Kisayol.Yenile));
        await Bekle(() => vm.Gecmis.Count == 2 && !vm.Mesgul);

        Assert.DoesNotContain("Gösterilecek kayıt yok.", Metinler(sayfa));
        await vm.DokumGetirCommand.ExecuteAsync(null);
        Assert.Contains(Metinler(sayfa), m => m?.EndsWith("· 0 hareket", StringComparison.Ordinal) == true);
        Assert.DoesNotContain("Gösterilecek kayıt yok.", Metinler(sayfa));
    }
}
