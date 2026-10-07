namespace Kasa.App.Controls;

/// <summary>Sayfadan gezinmeden ek bir alan gösterir. Asıl içerik ve bağlamı aynı kalır; sayfa yaşam döngüsü tetiklenmez.</summary>
public sealed class SayfaUstuPencere : IDisposable
{
    private readonly ContentPage _sayfa;
    private readonly View _icerik;
    private readonly ContentView _alt;
    private readonly Grid _katman;
    private bool _kapandi;
    public event EventHandler? Kapandi;

    public SayfaUstuPencere(ContentPage sayfa, View alan)
    {
        _sayfa = sayfa;
        _icerik = sayfa.Content ?? throw new InvalidOperationException("Açık sayfanın içeriği bulunamadı.");
        _alt = new ContentView { IsEnabled = false };
        AutomationProperties.SetExcludedWithChildren(_alt, true);
        _katman = new Grid();
        // Devralınan bağlamı aynı sayfaya bağla: reparent sırasında null bağlam para taslağını sıfırlamasın.
        // Kaynağın kendi BindingContext değeri/bindingi varsa aynen kalır.
        if (!_icerik.IsSet(BindableObject.BindingContextProperty))
            _icerik.SetBinding(BindableObject.BindingContextProperty, new Binding(nameof(sayfa.BindingContext), source: sayfa));
        sayfa.Content = null;
        _alt.Content = _icerik;
        _katman.Add(_alt);
        _katman.Add(alan);
        sayfa.Content = _katman;
        sayfa.Disappearing += SayfaKayboldu;
    }
    private void SayfaKayboldu(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        if (_kapandi)
            return;
        _kapandi = true;
        _sayfa.Disappearing -= SayfaKayboldu;
        _alt.Content = null;
        if (ReferenceEquals(_sayfa.Content, _katman))
            _sayfa.Content = _icerik;
        _katman.Clear();
        Kapandi?.Invoke(this, EventArgs.Empty);
    }
}
