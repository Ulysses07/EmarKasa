using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>Hedef ve bütçe (Aylık'tan <c>hedefbutce?yil=…&amp;ay=…</c> ile açılır).</summary>
public partial class HedefButcePage : ContentPage, IQueryAttributable
{
    private readonly HedefButceViewModel _vm;
    private readonly AuthViewModel _auth;

    public HedefButcePage(HedefButceViewModel vm, AuthViewModel auth)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _auth = auth;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (Rotalar.SayiDegeri(query, "yil") is { } yil && Rotalar.SayiDegeri(query, "ay") is { } ay) _vm.AyUygula(yil, ay);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.EditorMu = _auth.AktifRol == Rol.Editor;
        await _vm.YukleAsync();
    }
}
