using Kasa.App.Core;

namespace Kasa.App.Views;

/// <summary>
/// 22 · Rapordan iniş: <c>//islemler?baslangic=…&amp;bitis=…&amp;kanal=…(&amp;tip=…)</c> sorgusu listeyi o dönem,
/// kanal ve tiple süzer. Sorgu bir kez uygulanır (menüden yeniden gelince tekrar uygulanmaz).
/// </summary>
public partial class IslemlerPage : IQueryAttributable
{
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var suzgec = IslemSuzgeci.Coz(query);
        try { query.Clear(); }
        catch (NotSupportedException) { /* salt okunur sözlük */ }
        if (suzgec is not null && BindingContext is IslemlerViewModel vm) _ = vm.SuzgecUygulaAsync(suzgec);
    }
}
