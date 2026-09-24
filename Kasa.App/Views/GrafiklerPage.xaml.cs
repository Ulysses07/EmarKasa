using System.ComponentModel;
using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class GrafiklerPage : ContentPage
{
    private readonly GrafiklerViewModel _vm;

    public GrafiklerPage(GrafiklerViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        Cizim.Drawable = new GrafikCizimi(vm);
        _vm.PropertyChanged += CizimDegisti;
    }

    private void CizimDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GrafiklerViewModel.CizimSurumu)) Cizim.Invalidate();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.YukleAsync();
    }
}
