using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>Sayfa üstündeki güncelleme alanı; kaynak finansal sayfayı gezinmeden ve yeniden yüklemeden açılır.</summary>
public sealed class UygulamaGuncellemeAlani : ContentView
{
    private readonly UygulamaGuncellemeViewModel _vm;
    public event EventHandler? KapatIstendi;
    public UygulamaGuncellemeAlani(UygulamaGuncellemeViewModel vm)
    {
        BindingContext = _vm = vm;

        BackgroundColor = (Color)Application.Current!.Resources["AppBg"];
        var root = new VerticalStackLayout { Spacing = 16, Padding = new Thickness(16, 20), MaximumWidthRequest = 680 };
        root.Add(new Label { Text = "Uygulama güncellemeleri", Style = (Style)Application.Current.Resources["LblPageTitle"] });
        var mevcut = new Label { Style = (Style)Application.Current.Resources["LblPageSub"] };
        mevcut.SetBinding(Label.TextProperty, nameof(vm.MevcutSurum), stringFormat: "Bu uygulama: {0}");
        root.Add(mevcut);
        root.Add(Bagli(nameof(vm.KanalAciklamasi)));
        root.Add(Bagli(nameof(vm.Durum)));
        var paket = Bagli(nameof(vm.GuncellemeMetni));
        paket.FontAttributes = FontAttributes.Bold;
        paket.SetBinding(IsVisibleProperty, nameof(vm.GuncellemeVar));
        root.Add(paket);
        var hata = Bagli(nameof(vm.Hata));
        hata.Style = (Style)Application.Current.Resources["LblError"];
        hata.SetBinding(IsVisibleProperty, nameof(vm.Hata), converter: new Converters.DoluIseConverter());
        root.Add(hata);
        var ilerleme = new VerticalStackLayout { Spacing = 6 };
        ilerleme.SetBinding(IsVisibleProperty, nameof(vm.Indiriliyor));
        ilerleme.Add(Bagli(nameof(vm.IlerlemeMetni)));
        var cubuk = new ProgressBar();
        cubuk.SetBinding(ProgressBar.ProgressProperty, nameof(vm.IndirmeOrani));
        ilerleme.Add(cubuk);
        root.Add(ilerleme);
        var mesgul = new ActivityIndicator();
        mesgul.SetBinding(ActivityIndicator.IsRunningProperty, nameof(vm.Mesgul));
        mesgul.SetBinding(IsVisibleProperty, nameof(vm.Mesgul));
        root.Add(mesgul);
        var kontrol = Dugme("Güncellemeleri kontrol et", nameof(vm.KontrolCommand));
        kontrol.SetBinding(IsVisibleProperty, nameof(vm.UygulamaIciKurulum));
        root.Add(kontrol);
        var indir = Dugme("Güncellemeyi indir", nameof(vm.IndirCommand));
        indir.SetBinding(IsVisibleProperty, nameof(vm.GuncellemeVar));
        root.Add(indir);
        var kur = Dugme("Kur ve yeniden başlat", nameof(vm.KurCommand));
        kur.SetBinding(IsVisibleProperty, nameof(vm.GuncellemeVar));
        root.Add(kur);
        var kanal = Dugme("", nameof(vm.HariciKanalCommand));
        kanal.Style = (Style)Application.Current.Resources["BtnSecondary"];
        kanal.SetBinding(Button.TextProperty, nameof(vm.HariciKanalMetni));
        root.Add(kanal);
        var iptal = Dugme("İşlemi iptal et", nameof(vm.IptalCommand));
        iptal.SetBinding(IsVisibleProperty, nameof(vm.Mesgul));
        iptal.Style = (Style)Application.Current.Resources["BtnSecondary"];
        root.Add(iptal);
        root.Add(new Label { Text = "Pencereyi kapatmak süren indirmeyi iptal eder. İndirilmiş paket ve açık formunuz korunur.", Style = (Style)Application.Current.Resources["LblPageSub"] });
        var kapat = new Button { Text = "Kapat", Style = (Style)Application.Current.Resources["BtnSecondary"] };
        kapat.Clicked += (_, _) => KapatIstendi?.Invoke(this, EventArgs.Empty);
        root.Add(kapat);
        Content = new ScrollView { Content = root };
    }
    private static Label Bagli(string yol)
    {
        var label = new Label();
        label.SetBinding(Label.TextProperty, yol);
        return label;
    }
    private static Button Dugme(string metin, string komut)
    {
        var button = new Button { Text = metin, MinimumHeightRequest = 44 };
        button.SetBinding(Button.CommandProperty, komut);
        return button;
    }
}
