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

    /// <summary>Kurtarmada yalnız yeni şifre alanları görünür yapılır; kurtarma kodu maskeli kalır.</summary>
    private void YeniSifreyiGoster(object? sender, CheckedChangedEventArgs e)
    {
        KurtarmaYeniSifreAlani.IsPassword = !e.Value;
        KurtarmaYeniSifreTekrarAlani.IsPassword = !e.Value;
    }
}
