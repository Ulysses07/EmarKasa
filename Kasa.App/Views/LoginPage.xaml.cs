using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>Giriş ekranı. Başarılı girişte menüyü AppShell açar (GirisYapildi değişimini dinler).</summary>
public partial class LoginPage : ContentPage
{
    public LoginPage(AuthViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
