using Kasa.App.Core;

namespace Kasa.App.Views;

public sealed class GuvenlikAlani : ContentView
{
    private readonly GuvenlikViewModel _vm;
    /// <param name="guncellemeleriAc">"Uygulama güncellemeleri" düğmesi (kabuğun güncelleme penceresi); verilmezse düğme yoktur.</param>
    public GuvenlikAlani(GuvenlikViewModel vm, Page sayfa, Func<Task>? guncellemeleriAc = null)
    {
        _vm = vm;
        BindingContext = vm;
        var stack = new VerticalStackLayout { Spacing = 12 };
        stack.Add(Baslik("Hesap güvenliği"));
        stack.Add(Yazi("Şifre değişince yeniden giriş yaparsınız ve kurtarma kodu geçersiz olur."));
        stack.Add(TakipUi.Cerceve(Girdi("Mevcut şifre", nameof(vm.MevcutSifre))));
        var yeni = Girdi("Yeni şifre, en az 12 karakter", nameof(vm.YeniSifre));
        var tekrar = Girdi("Yeni şifre tekrar", nameof(vm.YeniSifreTekrar));
        stack.Add(TakipUi.Cerceve(yeni));
        stack.Add(TakipUi.Cerceve(tekrar));
        stack.Add(SifreyiGoster(nameof(vm.YeniSifreyiGoster), yeni, tekrar));
        // Düğmeler yan yana ve içerik genişliğinde (sadeleştirme §8); boş etiketler yer tutmaz.
        stack.Add(TakipUi.DugmeSatiri(Dugme("Şifremi değiştir", nameof(vm.SifreDegistirCommand)),
            Dugme("Kurtarma kodu oluştur", nameof(vm.KurtarmaKoduOlusturCommand))));
        var kod = new Label { FontSize = 22, FontAttributes = FontAttributes.Bold };
        kod.SetBinding(Label.TextProperty, nameof(vm.KurtarmaKodu));
        kod.SetBinding(IsVisibleProperty, nameof(vm.KurtarmaKodu), converter: new Converters.DoluIseConverter());
        stack.Add(kod);
        var kopyala = new Button { Text = "Kurtarma kodunu kopyala", HorizontalOptions = LayoutOptions.Start };
        kopyala.SetBinding(IsVisibleProperty, nameof(vm.KurtarmaKodu), converter: new Converters.DoluIseConverter());
        kopyala.Clicked += async (_, _) => { if (!string.IsNullOrEmpty(vm.KurtarmaKodu)) await Clipboard.Default.SetTextAsync(vm.KurtarmaKodu); };
        stack.Add(kopyala);
        var mesaj = Yazi("");
        mesaj.SetBinding(Label.TextProperty, nameof(vm.Mesaj));
        mesaj.SetBinding(IsVisibleProperty, nameof(vm.Mesaj), converter: new Converters.DoluIseConverter());
        stack.Add(mesaj);
        stack.Add(Baslik("Sürüm ve yedek"));
        var surum = Yazi("");
        surum.SetBinding(Label.TextProperty, nameof(vm.SurumBilgisi));
        stack.Add(surum);
        var indir = new Button();
        indir.SetBinding(Button.TextProperty, nameof(vm.IndirmeMetni));
        indir.SetBinding(IsVisibleProperty, nameof(vm.IndirmeAdresi), converter: new Converters.DoluIseConverter());
        indir.Clicked += async (_, _) => { if (vm.IndirmeAdresi is { } adres) await Launcher.Default.OpenAsync(adres); };
        // El ile güncelleme denetimi burada; menüdeki düğme yalnız yeni sürüm bulununca görünür.
        stack.Add(guncellemeleriAc is null ? TakipUi.DugmeSatiri(indir) : TakipUi.DugmeSatiri(TakipUi.Tikla("Uygulama güncellemeleri", guncellemeleriAc), indir));
        var yedek = Yazi("");
        yedek.SetBinding(Label.TextProperty, nameof(vm.YedekBilgisi));
        stack.Add(yedek);
        // Sunucunun yedek hatası ya da rotasyon uyarısı (silinemeyen eski yedek) ayrı ve kırmızı gösterilir.
        var yedekUyarisi = new Label { FontSize = 13, TextColor = (Color)Application.Current!.Resources["Neg"] };
        yedekUyarisi.SetBinding(Label.TextProperty, nameof(vm.YedekUyarisi));
        yedekUyarisi.SetBinding(IsVisibleProperty, nameof(vm.YedekUyarisi), converter: new Converters.DoluIseConverter());
        stack.Add(yedekUyarisi);
        var yedekDugmesi = new Button { Text = OperatingSystem.IsIOS() ? "Yedeği Dosyalar'a kaydet" : "Yedeği bilgisayara kaydet", HorizontalOptions = LayoutOptions.Start };
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
        var hata = new Label { TextColor = (Color)Application.Current!.Resources["Neg"] };
        hata.SetBinding(Label.TextProperty, nameof(vm.Hata));
        hata.SetBinding(IsVisibleProperty, nameof(vm.Hata), converter: new Converters.DoluIseConverter());
        stack.Add(hata);
        Content = new Border { Style = (Style)Application.Current!.Resources["CardForm"], Content = stack };
    }
    private static Label Baslik(string text) => new() { Text = text, Style = (Style)Application.Current!.Resources["LblSection"] };
    private static Label Yazi(string text) => new() { Text = text, Style = (Style)Application.Current!.Resources["LblTakipMetin"] };
    private static Entry Girdi(string baslik, string property) { var e = new Entry { Placeholder = baslik, IsPassword = true }; e.SetBinding(Entry.TextProperty, property); return e; }
    /// <summary>Yazım hatasını görmek için yalnız yeni şifre alanlarını açan kutu; mevcut şifre maskeli kalır. Durum görünüm
    /// modelindedir (<see cref="GuvenlikViewModel.YeniSifreyiGoster"/>): başarıda, ekrandan ayrılınca ve oturum sonunda kapanır.</summary>
    private static View SifreyiGoster(string ozellik, params Entry[] alanlar)
    {
        foreach (var alan in alanlar)
            alan.SetBinding(Entry.IsPasswordProperty, ozellik, converter: new Converters.TersIseConverter());
        // Sayfaların onay kutusuyla aynı (etiket kutunun hemen yanında).
        return TakipUi.Onay("Yeni şifreyi göster", ozellik);
    }
    private static Button Dugme(string text, string command) { var b = new Button { Text = text, HorizontalOptions = LayoutOptions.Start }; b.SetBinding(Button.CommandProperty, command); return b; }
}
