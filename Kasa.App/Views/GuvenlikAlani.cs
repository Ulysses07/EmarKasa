using Kasa.App.Core;

namespace Kasa.App.Views;

public sealed class GuvenlikAlani : ContentView
{
    private readonly GuvenlikViewModel _vm;
    public GuvenlikAlani(GuvenlikViewModel vm, Page sayfa)
    {
        _vm = vm;
        BindingContext = vm;
        var stack = new VerticalStackLayout { Spacing = 12 };
        stack.Add(Baslik("Hesap güvenliği"));
        stack.Add(Yazi("Şifre değişince yeniden giriş yapmanız gerekir. Şifre değişince mevcut kurtarma kodu da geçersiz olur; yeni kod oluşturun. Kurtarma kodunu uygulama dışında güvenli bir yerde saklayın."));
        stack.Add(Girdi("Mevcut şifre", nameof(vm.MevcutSifre)));
        var yeni = Girdi("Yeni şifre (en az 12 karakter)", nameof(vm.YeniSifre));
        var tekrar = Girdi("Yeni şifre (tekrar)", nameof(vm.YeniSifreTekrar));
        stack.Add(yeni);
        stack.Add(tekrar);
        stack.Add(SifreyiGoster(nameof(vm.YeniSifreyiGoster), yeni, tekrar));
        stack.Add(Dugme("Şifremi değiştir", nameof(vm.SifreDegistirCommand)));
        stack.Add(Dugme("Tek kullanımlık kurtarma kodu oluştur", nameof(vm.KurtarmaKoduOlusturCommand)));
        var kod = new Label { FontSize = 22, FontAttributes = FontAttributes.Bold };
        kod.SetBinding(Label.TextProperty, nameof(vm.KurtarmaKodu));
        stack.Add(kod);
        var kopyala = new Button { Text = "Kurtarma kodunu kopyala" };
        kopyala.SetBinding(IsVisibleProperty, nameof(vm.KurtarmaKodu), converter: new Converters.DoluIseConverter());
        kopyala.Clicked += async (_, _) => { if (!string.IsNullOrEmpty(vm.KurtarmaKodu)) await Clipboard.Default.SetTextAsync(vm.KurtarmaKodu); };
        stack.Add(kopyala);
        var mesaj = Yazi("");
        mesaj.SetBinding(Label.TextProperty, nameof(vm.Mesaj));
        stack.Add(mesaj);
        stack.Add(Baslik("Sürüm ve yedek"));
        var surum = Yazi("");
        surum.SetBinding(Label.TextProperty, nameof(vm.SurumBilgisi));
        stack.Add(surum);
        var indir = new Button();
        indir.SetBinding(Button.TextProperty, nameof(vm.IndirmeMetni));
        indir.SetBinding(IsVisibleProperty, nameof(vm.IndirmeAdresi), converter: new Converters.DoluIseConverter());
        indir.Clicked += async (_, _) => { if (vm.IndirmeAdresi is { } adres) await Launcher.Default.OpenAsync(adres); };
        stack.Add(indir);
        var yedek = Yazi("");
        yedek.SetBinding(Label.TextProperty, nameof(vm.YedekBilgisi));
        stack.Add(yedek);
        // Sunucunun yedek hatası ya da rotasyon uyarısı (silinemeyen eski yedek) ayrı ve kırmızı gösterilir.
        var yedekUyarisi = new Label { FontSize = 13, TextColor = Colors.DarkRed };
        yedekUyarisi.SetBinding(Label.TextProperty, nameof(vm.YedekUyarisi));
        yedekUyarisi.SetBinding(IsVisibleProperty, nameof(vm.YedekUyarisi), converter: new Converters.DoluIseConverter());
        stack.Add(yedekUyarisi);
        var yedekDugmesi = new Button { Text = OperatingSystem.IsIOS() ? "Yedeği Dosyalar'a kaydet" : "Yedeği bilgisayara kaydet" };
        yedekDugmesi.Clicked += async (_, _) => { if (vm.Mesgul) return; await DosyaIslemleri.IndirVeKaydetAsync(sayfa, vm.YedekIndirAsync); };
        yedekDugmesi.SetBinding(IsEnabledProperty, nameof(vm.YedekIndiriliyor), converter: new Converters.TersIseConverter());
        stack.Add(yedekDugmesi);
        var yedekSuruyor = Yazi("Yedek sunucuda hazırlanıp indiriliyor. Büyük veritabanında birkaç dakika sürebilir.");
        yedekSuruyor.SetBinding(IsVisibleProperty, nameof(vm.YedekIndiriliyor));
        stack.Add(yedekSuruyor);
        var yedekIptal = new Button { Text = "Yedek indirmeyi iptal et", HorizontalOptions = LayoutOptions.Start };
        yedekIptal.SetBinding(Button.CommandProperty, nameof(vm.YedekIptalCommand));
        yedekIptal.SetBinding(IsVisibleProperty, nameof(vm.YedekIndiriliyor));
        stack.Add(yedekIptal);
        var hata = new Label { TextColor = Colors.DarkRed };
        hata.SetBinding(Label.TextProperty, nameof(vm.Hata));
        stack.Add(hata);
        Content = new Border { Style = (Style)Application.Current!.Resources["CardForm"], Content = stack };
    }
    private static Label Baslik(string text) => new() { Text = text, FontSize = 18, FontAttributes = FontAttributes.Bold };
    private static Label Yazi(string text) => new() { Text = text, FontSize = 13 };
    private static Entry Girdi(string baslik, string property) { var e = new Entry { Placeholder = baslik, IsPassword = true }; e.SetBinding(Entry.TextProperty, property); return e; }
    /// <summary>Yazım hatasını görmek için yalnız yeni şifre alanlarını açan kutu; mevcut şifre maskeli kalır. Durum görünüm
    /// modelindedir (<see cref="GuvenlikViewModel.YeniSifreyiGoster"/>): başarıda, ekrandan ayrılınca ve oturum sonunda kapanır.</summary>
    private static View SifreyiGoster(string ozellik, params Entry[] alanlar)
    {
        var kutu = new CheckBox();
        kutu.SetBinding(CheckBox.IsCheckedProperty, ozellik, BindingMode.TwoWay);
        foreach (var alan in alanlar)
            alan.SetBinding(Entry.IsPasswordProperty, ozellik, converter: new Converters.TersIseConverter());
        return new HorizontalStackLayout { Spacing = 6, Children = { kutu, new Label { Text = "Yeni şifreyi göster", VerticalOptions = LayoutOptions.Center } } };
    }
    private static Button Dugme(string text, string command) { var b = new Button { Text = text }; b.SetBinding(Button.CommandProperty, command); return b; }
}
