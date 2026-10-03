using System.Windows.Input;
using Microsoft.Maui;            // Kasa.App.Core.Tests bu dosyayı MAUI örtük using'leri olmadan derler
using Microsoft.Maui.Controls;
using Microsoft.Maui.Layouts;

namespace Kasa.App.Controls;

/// <summary>Seçim çipleri grubu (İşlemler'deki altı kanal/tip/kart/zaman çip listesinin tek kopyası): satıra sığmayan çip
/// alta geçer. Öğeler BindableLayout.ItemsSource ile verilir; her öğe <c>Ad</c> (düğme metni) ve <c>Secili</c>
/// (ChipButton görünümü ve seçim tetikleyicisinin ekran okuyucu ipucu) taşır. Gerçek düğme fare, dokunma ve klavyeyle
/// <see cref="SecCommand"/>'ı öğeyle çalıştırır. Çipler arası boşluk <see cref="CipKenari"/> (varsayılan sağ ve alt 8;
/// tek satırlık grupta alt 0).</summary>
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

    private Button Cip()
    {
        var cip = new Button { Style = (Style)Application.Current!.Resources["ChipButton"] };
        cip.SetBinding(Button.TextProperty, "Ad");
        cip.SetBinding(SemanticProperties.DescriptionProperty, "Ad");
        cip.SetBinding(Button.CommandProperty, new Binding(nameof(SecCommand), source: this));
        cip.SetBinding(Button.CommandParameterProperty, new Binding("."));
        cip.SetBinding(MarginProperty, new Binding(nameof(CipKenari), source: this));
        var secili = new DataTrigger(typeof(Button)) { Binding = new Binding("Secili"), Value = true };
        var yesil = Application.Current!.Resources["Green"];
        secili.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = yesil });
        secili.Setters.Add(new Setter { Property = Button.BorderColorProperty, Value = yesil });
        secili.Setters.Add(new Setter { Property = Button.TextColorProperty, Value = Colors.White });
        secili.Setters.Add(new Setter { Property = SemanticProperties.HintProperty, Value = "Seçili" });
        cip.Triggers.Add(secili);
        return cip;
    }
}
