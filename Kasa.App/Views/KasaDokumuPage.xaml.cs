using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>"Kasa neden değişti?" (Haftalık/Aylık'tan <c>kasadokumu?baslangic=…&amp;bitis=…</c> ile açılır).</summary>
public partial class KasaDokumuPage : ContentPage, IQueryAttributable
{
    private readonly KasaDokumuViewModel _vm;

    public KasaDokumuPage(KasaDokumuViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (Rotalar.Aralik(query) is { } a) _vm.AralikUygula(a.Baslangic, a.Bitis);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
    }
}
