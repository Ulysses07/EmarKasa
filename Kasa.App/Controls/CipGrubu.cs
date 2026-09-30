using System.Windows.Input;
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;
using Microsoft.Maui.Layouts;

namespace Kasa.App.Controls;

/// <summary>Seçim çipleri grubu (İşlemler'deki altı kanal/tip/kart/zaman çip listesinin tek kopyası): satıra sığmayan çip
/// alta geçer. Öğeler BindableLayout.ItemsSource ile verilir; her öğe <c>Ad</c> (çip metni) ve <c>Secili</c> (Chip ve
/// ChipText stillerindeki DataTrigger) taşır (SecimCipi, KartCipi). Çipe dokunmak <see cref="SecCommand"/>'ı öğeyle
/// çalıştırır. Çipler arası boşluk <see cref="CipKenari"/> (varsayılan sağ ve alt 8; tek satırlık grupta alt 0).</summary>
public class CipGrubu : FlexLayout
{
    public static readonly BindableProperty SecCommandProperty = BindableProperty.Create(nameof(SecCommand), typeof(ICommand), typeof(CipGrubu));
    public static readonly BindableProperty CipKenariProperty = BindableProperty.Create(nameof(CipKenari), typeof(Thickness), typeof(CipGrubu), new Thickness(0, 0, 8, 8));

    public ICommand? SecCommand { get => (ICommand?)GetValue(SecCommandProperty); set => SetValue(SecCommandProperty, value); }
    public Thickness CipKenari { get => (Thickness)GetValue(CipKenariProperty); set => SetValue(CipKenariProperty, value); }

    public CipGrubu()
    {
        Wrap = FlexWrap.Wrap;
        AlignItems = FlexAlignItems.Center;
        BindableLayout.SetItemTemplate(this, new DataTemplate(Cip));
    }

    private Border Cip()
    {
        var metin = new Label { Style = (Style)Application.Current!.Resources["ChipText"] };
        metin.SetBinding(Label.TextProperty, "Ad");
        var dokunma = new TapGestureRecognizer();
        dokunma.SetBinding(TapGestureRecognizer.CommandProperty, new Binding(nameof(SecCommand), source: this));
        dokunma.SetBinding(TapGestureRecognizer.CommandParameterProperty, new Binding("."));
        var cip = new Border { Style = (Style)Application.Current!.Resources["Chip"], Content = metin };
        cip.SetBinding(MarginProperty, new Binding(nameof(CipKenari), source: this));
        cip.GestureRecognizers.Add(dokunma);
        return cip;
    }
}
