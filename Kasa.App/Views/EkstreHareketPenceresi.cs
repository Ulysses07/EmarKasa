using Kasa.App.Converters;
using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

/// <summary>
/// Ekstre hareketlerinin tam pencere açılan inceleme sayfası (docs/specs/2026-11-26-ekstre-hareket-penceresi.md §2): başlık ve ⋯ menüsü, süzgeç
/// çipleri ve arama, tek kaydırıcılı tablo, sağda ayrıntı paneli, altta hep görünen kaydet çubuğu. Görünüm modeli Ekstre İçe Aktar sayfasınınkidir.
/// </summary>
public sealed partial class EkstreHareketPenceresi : ContentPage
{
    private readonly EkstreAktarmaViewModel _vm;
    private readonly EkstreAktarmaPage _sayfa;
    private static Style Stil(string anahtar) => (Style)Application.Current!.Resources[anahtar];
    private static Color Renk(string anahtar) => (Color)Application.Current!.Resources[anahtar];

    private EkstreHareketPenceresi(EkstreAktarmaViewModel vm, EkstreAktarmaPage sayfa)
    {
        _vm = vm;
        _sayfa = sayfa;
        BindingContext = vm;
        Title = "Ekstre hareketleri";
        BackgroundColor = Renk("AppBg");
        var govde = new Grid
        {
            ColumnSpacing = 0,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
        };
        govde.Add(TabloBolumu());
        govde.Add(PaneliKur(), 1);
        govde.SetBinding(IsEnabledProperty, nameof(vm.Mesgul), converter: new TersIseConverter());
        var alt = AltCubuk();
        alt.SetBinding(IsEnabledProperty, nameof(vm.Mesgul), converter: new TersIseConverter());
        Content = new Grid
        {
            Padding = new Thickness(24, 16, 24, 0),
            RowSpacing = 10,
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
            Children = { Baslik(), SuzgecSatiri(), DurumSatiri(), govde, alt },
        };
        var cocuklar = ((Grid)Content).Children.OfType<View>().ToList();
        for (var i = 0; i < cocuklar.Count; i++)
            Grid.SetRow(cocuklar[i], i);
        SizeChanged += (_, _) => DarDuzeniUygula();
        vm.SatirGosterIstendi += SatiraGit;
        vm.SonucGosterIstendi += SonucuGoster;
    }

    /// <summary>Pencereyi açar; zaten açıksa yeniden açmaz.</summary>
    public static async Task AcAsync(INavigation nav, EkstreAktarmaViewModel vm, EkstreAktarmaPage sayfa)
    {
        if (nav.ModalStack.OfType<EkstreHareketPenceresi>().Any())
            return;
        await nav.PushModalAsync(new EkstreHareketPenceresi(vm, sayfa), animated: false);
    }

    public Task KapatAsync()
    {
        _vm.SatirGosterIstendi -= SatiraGit;
        _vm.SonucGosterIstendi -= SonucuGoster;
        _vm.SeciliSatir = null;
        return Navigation.ModalStack.Contains(this) ? Navigation.PopModalAsync(animated: false) : Task.CompletedTask;
    }

    protected override bool OnBackButtonPressed()
    {
        if (PanelAcik)
            PaneliKapat();
        else
            _ = KapatAsync();
        return true;
    }

    private Grid Baslik()
    {
        var baslik = Bagli(nameof(_vm.PencereBasligi));
        baslik.Style = Stil("LblTakipKartBaslik");
        var alt = Bagli(nameof(_vm.PencereAltBilgisi));
        alt.Style = Stil("LblTakipKucuk");
        var menu = Ikincil(Tikla("⋯", MenuAsync));
        SemanticProperties.SetDescription(menu, "Belge işlemleri");
        var kapat = Ikincil(Tikla("×", KapatAsync));
        SemanticProperties.SetDescription(kapat, "Pencereyi kapat");
        var satir = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) },
        };
        satir.Add(new VerticalStackLayout { Spacing = 2, Children = { baslik, alt } });
        satir.Add(menu, 1);
        satir.Add(kapat, 2);
        return satir;
    }

    private Grid SuzgecSatiri()
    {
        var cipler = new Controls.CipGrubu { CipKenari = new Thickness(0, 0, 8, 0) };
        cipler.SetBinding(BindableLayout.ItemsSourceProperty, nameof(_vm.SuzgecCipleri));
        cipler.SetBinding(Controls.CipGrubu.SecCommandProperty, nameof(_vm.SuzgecSecCommand));
        var ara = Girdi(nameof(_vm.Arama));
        ara.Placeholder = "Açıklamada ara";
        ara.WidthRequest = 240;
        SemanticProperties.SetDescription(ara, "Açıklamada ara");
        var satir = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        satir.Add(cipler);
        satir.Add(ara, 1);
        return satir;
    }

    /// <summary>Yapay zekânın ilerlemesi ya da hatası ve son işlemin iletisi; boşken yer kaplamaz.</summary>
    private VerticalStackLayout DurumSatiri() => new()
    {
        Spacing = 2,
        Children =
        {
            Dolu(Etiket("LblTakipKucuk"), nameof(_vm.YapayZekaIlerleme)), Dolu(Etiket("LblTakipHata"), nameof(_vm.YapayZekaHatasi)),
            Dolu(Etiket("LblTakipMesaj"), nameof(_vm.SatirMesaji)), Dolu(Etiket("LblTakipHata"), nameof(_vm.SatirHatasi)),
            Dolu(Etiket("LblTakipHata"), nameof(_vm.Hata)),
        }
    };

    private Grid AltCubuk()
    {
        var ozet = Bagli(nameof(_vm.SecimOzeti));
        ozet.Style = Stil("LblTakipMetin");
        ozet.VerticalOptions = LayoutOptions.Center;
        var toplu = Ikincil(Tikla("Toplu tür / kanal", () => { PaneliAc(PanelKipi.Toplu); return Task.CompletedTask; }));
        var yokSay = Ikincil(Tikla("Yok say", SecilenleriYokSayAsync));
        yokSay.SetBinding(IsVisibleProperty, nameof(_vm.YokSayDestekleniyor));
        var kaydet = Tikla("Etkisini göster ve kaydet", () => _vm.OnizleCommand.ExecuteAsync(null));
        var cubuk = new Grid
        {
            Padding = new Thickness(0, 10, 0, 14),
            ColumnSpacing = 8,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) },
        };
        cubuk.Add(ozet);
        cubuk.Add(toplu, 1);
        cubuk.Add(yokSay, 2);
        cubuk.Add(kaydet, 3);
        return new Grid { RowDefinitions = { new(1), new(GridLength.Auto) }, Children = { new BoxView { Style = Stil("TakipAyirici") }, cubuk } }
            .Satirla(cubuk, 1);
    }

    private async Task MenuAsync()
    {
        var secenekler = new List<(string Ad, Func<Task> Is)> { ("Kaynak dosyayı indir", () => DosyaIslemleri.IndirVeKaydetAsync(this, _vm.DosyaAsync)) };
        if (_vm.YenidenOkunabilir)
            secenekler.Add(("Dosyayı yeniden oku", YenidenOkuAsync));
        if (_vm.ErpSilinebilir)
            secenekler.Add(("Bu ERP12 dosyasını sil", ErpBelgeSilAsync));
        if (_vm.AtlananVar)
            secenekler.Add(($"Atlanan satırlar ({_vm.Atlananlar.Count})", () => DisplayAlertAsync("Atlanan satırlar",
                string.Join("\n", _vm.Atlananlar.Select(a => $"Sayfa {a.Sayfa} · {a.Neden}: {a.Metin}")), "Kapat")));
        if (_vm.BelgeUyarilariMetni.Length > 0)
            secenekler.Add((_vm.BelgeUyarilariBasligi, () => DisplayAlertAsync("Belge uyarıları", _vm.BelgeUyarilariMetni, "Kapat")));
        if (_vm.YapayZekaAcik)
            secenekler.Add(("Yapay zekâya gönderilen metni göster", GonderilenMetinAsync));
        if (_vm.YanitsizSorulabilir)
            secenekler.Add(("Yanıtsız satırları yeniden sor", YanitsizlariSorAsync));
        if (_vm.YokSayOneriVar)
            secenekler.Add((_vm.YokSayOneriDugmesi, YokSayOnerileriniUygulaAsync));
        var secim = await DisplayActionSheetAsync("Belge işlemleri", "Vazgeç", null, [.. secenekler.Select(s => s.Ad)]);
        if (secenekler.FirstOrDefault(s => s.Ad == secim) is { Is: { } calistir })
            await calistir();
    }

    private async Task YenidenOkuAsync()
    {
        if (await DisplayAlertAsync("Dosyayı yeniden oku", "Saklanan dosya baştan okunur. Bu belgede satırlara yazdıklarınız ve işaretleriniz silinir; kasa değişmez.",
            "Yeniden oku", "Vazgeç"))
            await _vm.YenidenOkuAsync();
    }

    private async Task ErpBelgeSilAsync()
    {
        if (await DisplayAlertAsync("ERP12 dosyasını sil", "Bu dosyadan aktarılan bütün cari hareketler silinir. Eşleşen ya da kaydedilen satırı yoktur; kasa değişmez.",
            "Sil", "Vazgeç"))
        {
            await _vm.ErpBelgeSilAsync();
            if (_vm.Belge is null)
                await KapatAsync();
        }
    }

    private async Task GonderilenMetinAsync()
    {
        await _vm.GonderilecekAcKapatCommand.ExecuteAsync(null);
        if (!_vm.GonderilecekAcik)
            return;
        await DisplayAlertAsync("Yapay zekâya gönderilen metin", string.Join("\n", _vm.GonderilecekSatirlar), "Kapat");
        _vm.GonderilecekAcik = false;
    }

    private async Task YanitsizlariSorAsync()
    {
        if (await _vm.YapayZekaOnizleAsync() is { } o && await DisplayAlertAsync("Yanıtsız satırları yeniden sor", _vm.YapayZekaOnayi(o), "Gönder", "Vazgeç"))
            await _vm.YanitsizlariSorAsync();
    }

    private async Task YokSayOnerileriniUygulaAsync()
    {
        if (await DisplayAlertAsync("Satırları yok say", $"Daha önce yok saydığınız hareketlerle aynı {_vm.YokSayOneriSayisi} satır kaydedilmeden kapanacak; "
            + "kasa değişmez. Kapanan süzgecinden geri alabilirsiniz.", "Yok say", "Vazgeç"))
            await _vm.OnerilenleriYokSayAsync();
    }

    private async Task SecilenleriYokSayAsync()
    {
        var sayi = _vm.Satirlar.Count(s => s.Secili);
        if (sayi < 2 || await DisplayAlertAsync("Satırları yok say", $"{sayi} işaretli satır kaydedilmeden kapanacak; kasa değişmez. Kapanan süzgecinden geri alabilirsiniz.",
            "Yok say", "Vazgeç"))
            await _vm.SeciliyiYokSayCommand.ExecuteAsync(null);
    }

    private static View Dolu(Label etiket, string yol)
    {
        etiket.SetBinding(Label.TextProperty, yol);
        etiket.SetBinding(IsVisibleProperty, yol, converter: new DoluIseConverter());
        return etiket;
    }
    private static Label Etiket(string stil) => new() { Style = Stil(stil) };
    private static Button Ikincil(Button b)
    {
        b.Style = Stil("BtnSecondary");
        return b;
    }
}

internal static class GridUzantilari
{
    /// <summary>Kurucu ilklendiricisinde eklenen çocuğun satırını ayarlar.</summary>
    public static Grid Satirla(this Grid grid, View cocuk, int satir)
    {
        Grid.SetRow(cocuk, satir);
        return grid;
    }
}
