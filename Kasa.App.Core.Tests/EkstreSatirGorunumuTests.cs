using Kasa.App.Views;
using Microsoft.Maui;

namespace Kasa.App.Core.Tests;

public partial class EkstreAktarmaTests
{
    private static View SatirGorunumu(CollectionView liste, EkstreSatirEditor satir)
    {
        var gorunum = (View)liste.ItemTemplate.CreateContent();
        gorunum.BindingContext = satir;
        return gorunum;
    }
    private static Entry[] SatirGirdileri(View gorunum) => gorunum.GetVisualTreeDescendants().OfType<Entry>().ToArray();

    [Fact]
    public async Task Duzenleyici_yalniz_kendi_satirinin_altinda_acilir_secimden_bagimsizdir()
    {
        GorunumOrtami.Kur();
        var (vm, api, _) = await Hazir();
        var page = new EkstreAktarmaPage(vm);
        var liste = page.GetVisualTreeDescendants().OfType<CollectionView>().Single();
        var ilk = SatirGorunumu(liste, vm.Satirlar[0]);
        var ikinci = SatirGorunumu(liste, vm.Satirlar[1]);
        Assert.Empty(SatirGirdileri(ilk));
        Assert.Empty(SatirGirdileri(ikinci));

        // Kutunun anlamı mali seçimdir; düzenleyicinin açılması ayrı bir eylemdir.
        ilk.GetVisualTreeDescendants().OfType<CheckBox>().Single().IsChecked = true;
        Assert.True(vm.Satirlar[0].Secili);
        Assert.Empty(SatirGirdileri(ilk));
        ((IButtonController)ilk.GetVisualTreeDescendants().OfType<Button>().Single(b => b.Text == "İncele / düzenle")).SendClicked();
        Assert.NotEmpty(SatirGirdileri(ilk));
        Assert.Empty(SatirGirdileri(ikinci));
        var aciklama = SatirGirdileri(ilk).Single(e => e.Text == vm.Satirlar[0].Aciklama);
        aciklama.Text = "Satır içinde düzeltilen açıklama";
        Assert.Equal(aciklama.Text, vm.Satirlar[0].Aciklama);

        ((IButtonController)ikinci.GetVisualTreeDescendants().OfType<Button>().Single(b => b.Text == "İncele / düzenle")).SendClicked();
        Assert.Empty(SatirGirdileri(ilk));
        Assert.NotEmpty(SatirGirdileri(ikinci));
        Assert.True(vm.Satirlar[0].Secili);
        Assert.False(vm.Satirlar[1].Secili);
        Assert.Empty(api.KaydetIstekleri);
        Assert.Equal(0, api.OnizlemeSayisi);
    }

    [Fact]
    public async Task Satir_alanlari_genis_ekranda_yanyana_dar_ekranda_altalta_yerlesir()
    {
        GorunumOrtami.Kur();
        var (vm, _, _) = await Hazir();
        var page = new EkstreAktarmaPage(vm);
        var liste = page.GetVisualTreeDescendants().OfType<CollectionView>().Single();
        var satir = SatirGorunumu(liste, vm.Satirlar[0]);
        vm.SeciliSatir = vm.Satirlar[0];
        var alanlar = satir.GetVisualTreeDescendants().OfType<Grid>()
            .Single(g => g.GetVisualTreeDescendants().OfType<Entry>().Count() == 2);
        var tarih = alanlar.Children[0];
        var tutar = alanlar.Children[1];
        ((IView)alanlar).Arrange(new Rect(0, 0, 800, 160));
        Assert.True(tutar.Frame.Left >= tarih.Frame.Right);
        Assert.Equal(tarih.Frame.Top, tutar.Frame.Top);
        ((IView)alanlar).Arrange(new Rect(0, 0, 280, 160));
        Assert.Equal(tarih.Frame.Left, tutar.Frame.Left);
        Assert.True(tutar.Frame.Top >= tarih.Frame.Bottom);
        Assert.True(tutar.Frame.Right <= 280);
    }

    [Fact]
    public async Task Ac_kapat_ve_hucre_yeniden_kullanimi_taslagi_onayi_ve_onizlemeyi_korur()
    {
        GorunumOrtami.Kur();
        var (vm, api, _) = await Hazir();
        Sec(vm.Satirlar[0]);
        vm.Satirlar[0].Aciklama = "Düzenlenmiş taslak";
        await vm.OnizleCommand.ExecuteAsync(null);
        vm.Onay = true;
        var onizleme = vm.OnizlemeMetni;
        var page = new EkstreAktarmaPage(vm);
        var liste = page.GetVisualTreeDescendants().OfType<CollectionView>().Single();
        var hucre = SatirGorunumu(liste, vm.Satirlar[0]);
        vm.SeciliSatir = vm.Satirlar[0];
        Assert.Contains(SatirGirdileri(hucre), e => e.Text == "Düzenlenmiş taslak");

        ((IButtonController)hucre.GetVisualTreeDescendants().OfType<Button>().Single(b => b.Text == "Kapat")).SendClicked();
        Assert.Null(vm.SeciliSatir);
        Assert.Empty(SatirGirdileri(hucre));
        vm.SeciliSatir = vm.Satirlar[0];
        Assert.Contains(SatirGirdileri(hucre), e => e.Text == "Düzenlenmiş taslak");
        hucre.BindingContext = vm.Satirlar[1];
        Assert.Empty(SatirGirdileri(hucre));
        vm.SeciliSatir = vm.Satirlar[1];
        Assert.Contains(SatirGirdileri(hucre), e => e.Text == vm.Satirlar[1].Aciklama);
        Assert.DoesNotContain(SatirGirdileri(hucre), e => e.Text == "Düzenlenmiş taslak");
        Assert.Equal("Düzenlenmiş taslak", vm.Satirlar[0].Aciklama);
        Assert.True(vm.OnizlemeVar);
        Assert.True(vm.Onay);
        Assert.Equal(onizleme, vm.OnizlemeMetni);
        Assert.Equal(1, api.OnizlemeSayisi);
        Assert.Empty(api.KaydetIstekleri);
    }
}
