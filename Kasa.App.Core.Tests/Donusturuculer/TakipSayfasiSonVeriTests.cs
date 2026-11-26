using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

/// <summary>Kodla yazılmış takip sayfalarında son veri (tasarım 2026-10-02 §3): yükleme hata verse de son veri görünür ve soluk; başlıkta
/// "Son güncelleme" yalnız veri eskiyken görünür ve "güncel olmayabilir" der (docs/specs/2026-11-26-slop-temizligi.md §3).</summary>
public class TakipSayfasiSonVeriTests
{
    private sealed class DenemeSayfasi(AyarlarViewModel vm) : TakipSayfasi<AyarlarViewModel>(vm, "Deneme", () => Task.CompletedTask)
    {
        public VerticalStackLayout GovdeAlani => Govde;
    }

    [Fact]
    public void Govde_son_veri_varken_gorunur_eski_veride_soluk_son_guncelleme_yalniz_eskiyken()
    {
        GorunumOrtami.Kur();
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api, new AuthViewModel(api));
        var sayfa = new DenemeSayfasi(vm);
        static bool Gorunur(Element e) => e is not VisualElement { IsVisible: false } && (e.Parent is null || Gorunur(e.Parent));
        string? Zaman() => sayfa.GetVisualTreeDescendants().OfType<Label>().SingleOrDefault(l => Gorunur(l) && l.Text?.StartsWith("Son güncelleme") == true)?.Text;

        Assert.Null(Zaman());
        Assert.False(sayfa.GovdeAlani.IsVisible);

        vm.SonGuncelleme = new DateTimeOffset(2026, 10, 2, 14, 5, 0, TimeSpan.FromHours(3));
        vm.VeriEski = true;
        Assert.True(sayfa.GovdeAlani.IsVisible);
        Assert.Equal(TakipUi.EskiVeriOpakligi, sayfa.GovdeAlani.Opacity);
        Assert.Equal("Son güncelleme: 02.10.2026 14:05 · güncel olmayabilir", Zaman());

        vm.VeriEski = false;
        Assert.Equal(1, sayfa.GovdeAlani.Opacity);
        Assert.Null(Zaman());
    }

    /// <summary>Kasalar alt bölümlerinin gövdesi: hiç yükleme yokken gizli, son veri varken (yükleme hata verse de) görünür ve
    /// eski veride soluk.</summary>
    [Fact]
    public void Son_veri_govdesi_govde_gorunurken_gorunur_eski_veride_soluktur()
    {
        GorunumOrtami.Kur();
        var api = new SahteApi();
        var vm = new KasaKontrolViewModel(new KasaKontrolVeAylikGiderTests.Sahte(), new AuthViewModel(api));
        var govde = TakipUi.SonVeriGovdesi(new VerticalStackLayout());
        govde.BindingContext = vm;

        Assert.False(govde.IsVisible);

        vm.SonGuncelleme = new DateTimeOffset(2026, 10, 2, 14, 5, 0, TimeSpan.FromHours(3));
        vm.VeriEski = true;
        Assert.True(govde.IsVisible);
        Assert.Equal(TakipUi.EskiVeriOpakligi, govde.Opacity);

        vm.VeriEski = false;
        Assert.Equal(1, govde.Opacity);
    }

    /// <summary>Yeniden inceleme B2: boş liste metni, verilen yol doğruyken (Çekler: gösterilen sorgu yüklenmedi) gizlenir.</summary>
    [Fact]
    public void Bos_liste_metni_sorgu_yuklenmediyse_gizlenir()
    {
        GorunumOrtami.Kur();
        var api = new SahteApi();
        var vm = new CekTakipViewModel(new CekTakipViewModelTests.Sahte(), api, new AuthViewModel(api));
        var liste = (VerticalStackLayout)TakipUi.Liste<CekSatiri>(nameof(vm.Cekler), bosMetinGizleYolu: nameof(vm.SorguYuklenmedi));
        liste.BindingContext = vm;
        var bos = Assert.IsAssignableFrom<View>(BindableLayout.GetEmptyView(liste));

        Assert.True(bos.IsVisible);
        vm.SorguYuklenmedi = true;
        Assert.False(bos.IsVisible);
    }

    /// <summary>Ekran denemesi G-1: etiketli onay kutusu (Aylık giderler ödeme onayı) ekran okuyucuda etiketin metniyle adlanır.</summary>
    [Fact]
    public void Onay_kutusu_etiketinin_metniyle_adlanir()
    {
        GorunumOrtami.Kur();
        const string metin = "Ödeme gerçekleşti; gösterilen tutar ve kanal paylarını onaylıyorum.";
        var kutu = TakipUi.Onay(metin, "Secili").GetVisualTreeDescendants().OfType<CheckBox>().Single();

        Assert.Equal(metin, SemanticProperties.GetDescription(kutu));
    }
}
