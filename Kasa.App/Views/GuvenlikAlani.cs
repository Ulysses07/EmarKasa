using Kasa.App.Core;

namespace Kasa.App.Views;

public sealed class GuvenlikAlani : ContentView
{
    private readonly GuvenlikViewModel _vm;
    public GuvenlikAlani(GuvenlikViewModel vm, Page sayfa)
    {
        _vm = vm; BindingContext = vm;
        var stack = new VerticalStackLayout { Spacing = 12 };
        stack.Add(Baslik("Hesap güvenliği"));
        stack.Add(Yazi("Şifre değişince yeniden giriş yapmanız gerekir. Kurtarma kodunu uygulama dışında güvenli bir yerde saklayın."));
        stack.Add(Girdi("Mevcut şifre", nameof(vm.MevcutSifre)));
        stack.Add(Girdi("Yeni şifre (en az 12 karakter)", nameof(vm.YeniSifre)));
        stack.Add(Dugme("Şifremi değiştir", nameof(vm.SifreDegistirCommand)));
        stack.Add(Dugme("Tek kullanımlık kurtarma kodu oluştur", nameof(vm.KurtarmaKoduOlusturCommand)));
        var kod = new Label { FontSize = 22, FontAttributes = FontAttributes.Bold }; kod.SetBinding(Label.TextProperty, nameof(vm.KurtarmaKodu)); stack.Add(kod);
        var kopyala = new Button { Text = "Kurtarma kodunu kopyala" };
        kopyala.SetBinding(IsVisibleProperty, nameof(vm.KurtarmaKodu), converter: new Converters.DoluIseConverter());
        kopyala.Clicked += async (_, _) => { if (!string.IsNullOrEmpty(vm.KurtarmaKodu)) await Clipboard.Default.SetTextAsync(vm.KurtarmaKodu); };
        stack.Add(kopyala);
        var mesaj = Yazi(""); mesaj.SetBinding(Label.TextProperty, nameof(vm.Mesaj)); stack.Add(mesaj);
        stack.Add(Baslik("Sürüm ve yedek"));
        var surum = Yazi(""); surum.SetBinding(Label.TextProperty, nameof(vm.SurumBilgisi)); stack.Add(surum);
        var indir = new Button { Text = "Yeni sürümü indir" };
        indir.SetBinding(IsVisibleProperty, nameof(vm.IndirmeAdresi), converter: new Converters.DoluIseConverter());
        indir.Clicked += async (_, _) => { if (vm.IndirmeAdresi is { } adres) await Launcher.Default.OpenAsync(adres); };
        stack.Add(indir);
        var yedek = Yazi(""); yedek.SetBinding(Label.TextProperty, nameof(vm.YedekBilgisi)); stack.Add(yedek);
        var yedekDugmesi = new Button { Text = "Yedeği bilgisayara kaydet" };
        yedekDugmesi.Clicked += async (_, _) => { if (vm.Mesgul) return; var dosya = await vm.YedekIndirAsync(); if (dosya is not null) await DosyaIslemleri.KaydetAsync(sayfa, dosya); };
        stack.Add(yedekDugmesi);
        var hata = new Label { TextColor = Colors.DarkRed }; hata.SetBinding(Label.TextProperty, nameof(vm.Hata)); stack.Add(hata);
        Content = new Border { Style = (Style)Application.Current!.Resources["CardForm"], Content = stack };
    }
    private static Label Baslik(string text) => new() { Text = text, FontSize = 18, FontAttributes = FontAttributes.Bold };
    private static Label Yazi(string text) => new() { Text = text, FontSize = 13 };
    private static Entry Girdi(string baslik, string property) { var e = new Entry { Placeholder = baslik, IsPassword = true }; e.SetBinding(Entry.TextProperty, property); return e; }
    private static Button Dugme(string text, string command) { var b = new Button { Text = text }; b.SetBinding(Button.CommandProperty, command); return b; }
}
