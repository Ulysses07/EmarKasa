using Kasa.App.Core;

namespace Kasa.App;

public partial class AppShell : Shell
{
    private readonly AuthViewModel _auth;
    /// <summary>Menünün içeriği (gruplar, öğeler, seçili öğe): Shell.FlyoutContent kökünün (MenuAlani) bağlamı.</summary>
    private readonly MenuModeli _menuModeli = new();
    /// <summary>Rol bölümü → sayfa öğesi (tek kaynak). Öğeler menüde çizilmez (menüyü MenuModeli çizer) ama rota kaynağıdır;
    /// yetkisi olmayan bölümün öğesi gizli kalır, doğrudan rotayla erişim kuralı önceki menüdekiyle aynıdır.</summary>
    private readonly IReadOnlyDictionary<Bolum, FlyoutItem> _menu;
    private bool _giriseDonuluyor;

    public AppShell(AuthViewModel auth)
    {
        InitializeComponent();
        _auth = auth;
        _menu = new Dictionary<Bolum, FlyoutItem>
        {
            [Bolum.Panel] = PanelItem,
            [Bolum.Haftalik] = HaftalikItem,
            [Bolum.Aylik] = AylikItem,
            [Bolum.Islemler] = IslemlerItem,
            [Bolum.AylikGiderler] = AylikGiderlerItem,
            [Bolum.Ayarlar] = AyarlarItem,
            [Bolum.Alislar] = AlislarItem,
            [Bolum.DisariAktar] = DisariAktarItem,
            [Bolum.Kartlar] = KartlarItem,
            [Bolum.Krediler] = KredilerItem,
            [Bolum.Bildirimler] = BildirimlerItem,
            [Bolum.EkstreAktar] = EkstreAktarItem,
        };
        MenuAlani.BindingContext = _menuModeli;
        _menuModeli.GitIstendi += async (_, rota) => await GoToAsync("//" + rota);
        _menuModeli.CikisIstendi += async (_, _) => await CikisAsync();
        _auth.OturumSonlandi += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await GiriseDonAsync());
        Loaded += async (_, _) => await AcilistaYonlendirAsync();
    }

    /// <summary>Her gezinmede (menü, sayfalar arası bağlantı, girişe dönüş) seçili menü öğesi yeni konumdan belirlenir.</summary>
    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        _menuModeli.RotaSecildi(args.Current?.Location?.OriginalString);
    }

    private async Task AcilistaYonlendirAsync()
    {
        var girildi = await _auth.AcilistaDogrulaAsync();
        if (girildi)
            MenuyuAc();
        else
            await GiriseDonAsync();
    }

    public void MenuyuAc()
    {
        MenuyuGoster(SekmeModeli.Bolumler(_auth.AktifRol));
        _ = GoToAsync(_auth.AktifRol == Rol.Alici ? "//alislar" : "//panel");
    }

    /// <summary>Yalnız verilen bölümlerin sayfa öğeleri erişilebilir ve menüde görünür (girişe dönüşte hiçbiri).</summary>
    private void MenuyuGoster(IReadOnlyCollection<Bolum> bolumler)
    {
        foreach (var (bolum, oge) in _menu)
            oge.IsVisible = bolumler.Contains(bolum);
        _menuModeli.Goster(bolumler);
    }

    private async Task CikisAsync()
    {
        await _auth.CikisAsync();
        await GiriseDonAsync();
    }

    private async Task GiriseDonAsync()
    {
        if (_giriseDonuluyor)
            return;
        _giriseDonuluyor = true;
        try
        {
            MenuyuGoster([]);
            await GoToAsync("//login");
        }
        finally { _giriseDonuluyor = false; }
    }
}
