using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kasa.App.Core;

/// <summary>Menü simgeleri: Segoe Fluent Icons (Windows 11) ile Segoe MDL2 Assets'in (Windows 10) ortak kod noktaları. İki yazı
/// tipi de Windows'la gelir, uygulama paketlemez (Styles.xaml SimgeAilesi). Yorumdaki adlar Microsoft Learn'deki
/// segoe-fluent-icons-font ve segoe-ui-symbol-font sayfalarındaki simge adlarıdır; her kod noktası iki sayfada aynı simgedir.</summary>
public static class MenuSimgeleri
{
    public const string Kasalar = "";        // Home
    public const string Haftalik = "";       // CalendarWeek
    public const string Aylik = "";          // Calendar
    public const string Islemler = "";       // BulletedList
    public const string Alislar = "";        // ShoppingCart
    public const string AylikGiderler = "";  // RepeatAll
    public const string EkstreAktar = "";    // Import
    public const string Kartlar = "";        // PaymentCard
    public const string Krediler = "";       // Bank
    public const string Bildirimler = "";    // Ringer
    public const string DisariAktar = "";    // Export
    public const string Ayarlar = "";        // Settings
    public const string Cikis = "";          // SignOut
}

/// <summary>Menü öğesinin tanımı: rol bölümü, başlık, simge ve Shell rotası (AppShell.xaml FlyoutItem Route).</summary>
public sealed record MenuTanimi(Bolum Bolum, string Baslik, string Simge, string Rota);

/// <summary>Menü grubunun tanımı: başlık ve sıralı öğeler.</summary>
public sealed record MenuGrupTanimi(string Baslik, IReadOnlyList<MenuTanimi> Ogeler);

/// <summary>Çizilen menü öğesi. Zemini iki durumdan biri belirler: <see cref="Secili"/> (SidebarActive) ya da
/// <see cref="UzerindeVurgu"/> (SidebarHover); ikisi aynı anda doğru olmaz, şablondaki iki DataTrigger çakışmaz.</summary>
public sealed partial class MenuOgesi : ObservableObject
{
    private readonly Action<MenuOgesi> _sec;

    /// <param name="bolum">Rol bölümü; Çıkış öğesinde null.</param>
    /// <param name="sec">Öğeye tıklanınca çağrılır (gezinme ya da çıkış).</param>
    public MenuOgesi(Bolum? bolum, string baslik, string simge, string rota, Action<MenuOgesi> sec)
    {
        Bolum = bolum;
        Baslik = baslik;
        Simge = simge;
        Rota = rota;
        _sec = sec;
    }

    public Bolum? Bolum { get; }
    public string Baslik { get; }
    public string Simge { get; }
    public string Rota { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UzerindeVurgu))]
    private bool _secili;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UzerindeVurgu))]
    private bool _uzerinde;

    /// <summary>Fare üzerinde ve öğe seçili değil: seçili öğe fareyle de seçili zemininde kalır.</summary>
    public bool UzerindeVurgu => Uzerinde && !Secili;

    [RelayCommand]
    private void Sec() => _sec(this);

    [RelayCommand]
    private void UzerineGel() => Uzerinde = true;

    [RelayCommand]
    private void Ayril() => Uzerinde = false;
}

/// <summary>Çizilen grup. Başlıksız grup (Çıkış) üstündeki ayırıcı çizgiyle ayrı durur.</summary>
public sealed record MenuGrubu(string? Baslik, IReadOnlyList<MenuOgesi> Ogeler)
{
    public bool BaslikVar => Baslik is not null;
    public bool Ayri => Baslik is null;
}

/// <summary>
/// Masaüstü menüsünün tek kaynağı (tasarım 2026-09-30 §1): grup ve öğe sırası, başlık, simge ve rota. Görünen öğeler
/// bugünkü rol kuralıyla (<see cref="SekmeModeli.Bolumler"/>) verilen bölümlerdir; öğesi kalmayan grup çizilmez, Çıkış
/// grupların altında ayrı durur. Girişe dönüşte (boş bölüm listesi) menü boşalır. Seçili öğe Shell'in konumundan gelir
/// (<see cref="RotaSecildi"/>). Gezinme ve çıkış kabuğun işidir: model yalnız olay bildirir (Windows'tan bağımsız sınanır).
/// </summary>
public sealed partial class MenuModeli : ObservableObject
{
    public const string CikisBasligi = "Çıkış";

    public static IReadOnlyList<MenuGrupTanimi> Duzen { get; } =
    [
        new("Özet",
        [
            new(Bolum.Panel, "Kasalar", MenuSimgeleri.Kasalar, "panel"),
            new(Bolum.Haftalik, "Haftalık", MenuSimgeleri.Haftalik, "haftalik"),
            new(Bolum.Aylik, "Aylık", MenuSimgeleri.Aylik, "aylik"),
        ]),
        new("Kayıtlar",
        [
            new(Bolum.Islemler, "İşlemler", MenuSimgeleri.Islemler, "islemler"),
            new(Bolum.Alislar, "Alışlar", MenuSimgeleri.Alislar, "alislar"),
            new(Bolum.AylikGiderler, "Aylık giderler", MenuSimgeleri.AylikGiderler, "aylikgiderler"),
            new(Bolum.EkstreAktar, "Ekstre içe aktar", MenuSimgeleri.EkstreAktar, "ekstreaktar"),
        ]),
        new("Kart ve kredi",
        [
            new(Bolum.Kartlar, "Kartlar", MenuSimgeleri.Kartlar, "kartlar"),
            new(Bolum.Krediler, "Krediler", MenuSimgeleri.Krediler, "krediler"),
        ]),
        new("Diğer",
        [
            new(Bolum.Bildirimler, "Bildirimler", MenuSimgeleri.Bildirimler, "bildirimler"),
            new(Bolum.DisariAktar, "Rapor dışa aktar", MenuSimgeleri.DisariAktar, "disariaktar"),
            new(Bolum.Ayarlar, "Ayarlar", MenuSimgeleri.Ayarlar, "ayarlar"),
        ]),
    ];

    private string? _seciliRota;

    [ObservableProperty]
    private IReadOnlyList<MenuGrubu> _gruplar = [];

    /// <summary>Bir öğeye tıklandı: kabuk bu rotaya (//rota) gider.</summary>
    public event EventHandler<string>? GitIstendi;

    /// <summary>Çıkış'a tıklandı: kabuk oturumu kapatıp girişe döner.</summary>
    public event EventHandler? CikisIstendi;

    public IEnumerable<MenuOgesi> Ogeler => Gruplar.SelectMany(g => g.Ogeler);

    /// <summary>Menüyü verilen bölümlerle kurar (AppShell.MenuyuGoster: role göre ya da girişe dönüşte boş).</summary>
    public void Goster(IReadOnlyCollection<Bolum> bolumler)
    {
        var gruplar = new List<MenuGrubu>();
        foreach (var grup in Duzen)
        {
            var ogeler = grup.Ogeler.Where(o => bolumler.Contains(o.Bolum))
                .Select(o => new MenuOgesi(o.Bolum, o.Baslik, o.Simge, o.Rota, Sec)).ToList();
            if (ogeler.Count > 0)
                gruplar.Add(new MenuGrubu(grup.Baslik, ogeler));
        }
        if (gruplar.Count > 0)
            gruplar.Add(new MenuGrubu(null, [new MenuOgesi(null, CikisBasligi, MenuSimgeleri.Cikis, "", _ => CikisIstendi?.Invoke(this, EventArgs.Empty))]));
        Gruplar = gruplar;
        SeciliyiYansit();
    }

    /// <summary>Shell'in yeni konumu (ShellNavigatedEventArgs.Current.Location): o rotanın öğesi seçili olur.</summary>
    public void RotaSecildi(string? konum)
    {
        _seciliRota = RotaAdi(konum);
        SeciliyiYansit();
    }

    /// <summary>"//kartlar?KartId=3" → "kartlar": baştaki eğik çizgiler atılır, ilk '/' ya da '?' işaretine kadar alınır.</summary>
    public static string? RotaAdi(string? konum)
    {
        if (string.IsNullOrWhiteSpace(konum))
            return null;
        var ad = konum.TrimStart('/');
        var son = ad.IndexOfAny(['/', '?']);
        return son < 0 ? ad : ad[..son];
    }

    /// <summary>Bölümün Shell rotası (AppShell.xaml FlyoutItem Route ile aynı; MauiKayitTutarliligiTests sınar).</summary>
    public static string Rota(Bolum bolum) => Duzen.SelectMany(g => g.Ogeler).Single(o => o.Bolum == bolum).Rota;

    private void SeciliyiYansit()
    {
        foreach (var oge in Ogeler)
            oge.Secili = oge.Bolum is not null && oge.Rota == _seciliRota;
    }

    private void Sec(MenuOgesi oge) => GitIstendi?.Invoke(this, oge.Rota);
}
