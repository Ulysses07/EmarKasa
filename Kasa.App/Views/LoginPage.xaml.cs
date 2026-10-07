using Kasa.App.Core;

namespace Kasa.App.Views;

public partial class LoginPage : ContentPage
{
    private readonly AuthViewModel _vm;

    public LoginPage(AuthViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AuthViewModel.GirisYapildi) && _vm.GirisYapildi)
                ((AppShell)Shell.Current).MenuyuAc();
        };
    }
    private async void GuncellemelerTiklandi(object? sender, EventArgs e)
    {
        if (Shell.Current is AppShell shell)
            await shell.GuncellemeleriAcAsync();
    }
}
