using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>ERP12 karşılaştırma: her iki rol kullanabilir (salt okunur; sunucuya yazmaz).</summary>
public partial class Erp12Page : ContentPage
{
    public Erp12Page(Erp12KarsilastirmaViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
