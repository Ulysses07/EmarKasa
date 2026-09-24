using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>
/// Paket D (özellik 34): kart ekstresi mutabakatı. Kredi Kartları sayfasından
/// <c>kartmutabakat?kartId=7</c> ile açılır (rota AppShell'de kayıtlı).
/// </summary>
public partial class KartMutabakatPage : ContentPage, IQueryAttributable
{
    public const string Rota = "kartmutabakat";

    private readonly KartMutabakatViewModel _vm;
    private readonly AuthViewModel _auth;
    private int _kartId;

    public KartMutabakatPage(KartMutabakatViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("kartId", out var v) && int.TryParse(v?.ToString(), out var id)) _kartId = id;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;   // izleyici yalnız görür
        if (_kartId > 0) await _vm.YukleAsync(_kartId);
    }
}
