using Kasa.App.Core;

namespace Kasa.App.Converters;

/// <summary>
/// Para giriş kutusu doğrulaması (ekli özellik; EntryMoney stilinde açılır). Geçersiz metinde yazı
/// kırmızı olur ve ipucu (tooltip) nedeni gösterir; odak kutudan çıkınca metin son geçerli değere
/// döner. Böylece ekranda görünen tutar ile kaydedilecek tutar her zaman aynıdır.
/// </summary>
public static class ParaGirisDogrulama
{
    private static readonly Color HataRengi = Color.FromArgb("#C13A2E");

    public static readonly BindableProperty EtkinProperty = BindableProperty.CreateAttached(
        "Etkin", typeof(bool), typeof(ParaGirisDogrulama), false, propertyChanged: EtkinDegisti);

    /// <summary>Son geçerli metin (odak çıkınca geri yüklenir).</summary>
    private static readonly BindableProperty SonGecerliProperty = BindableProperty.CreateAttached(
        "SonGecerli", typeof(string), typeof(ParaGirisDogrulama), string.Empty);

    /// <summary>Kutu şu an geçersiz metin içeriyor mu (sayfalar bağlanabilir).</summary>
    public static readonly BindableProperty GecersizProperty = BindableProperty.CreateAttached(
        "Gecersiz", typeof(bool), typeof(ParaGirisDogrulama), false);

    public static bool GetEtkin(BindableObject b) => (bool)b.GetValue(EtkinProperty);
    public static void SetEtkin(BindableObject b, bool deger) => b.SetValue(EtkinProperty, deger);
    public static bool GetGecersiz(BindableObject b) => (bool)b.GetValue(GecersizProperty);

    private static void EtkinDegisti(BindableObject b, object eski, object yeni)
    {
        if (b is not Entry e) return;
        e.TextChanged -= MetinDegisti;
        e.Unfocused -= OdakKayboldu;
        if (yeni is true)
        {
            e.TextChanged += MetinDegisti;
            e.Unfocused += OdakKayboldu;
        }
    }

    private static void MetinDegisti(object? sender, TextChangedEventArgs a)
    {
        if (sender is not Entry e) return;
        var sonuc = ParaGiris.Ayristir(a.NewTextValue);
        if (sonuc.Gecerli)
        {
            e.SetValue(SonGecerliProperty, a.NewTextValue ?? string.Empty);
            e.SetValue(GecersizProperty, false);
            e.ClearValue(Entry.TextColorProperty);          // stile (Ink) dön
            ToolTipProperties.SetText(e, null);
        }
        else
        {
            e.SetValue(GecersizProperty, true);
            e.TextColor = HataRengi;
            ToolTipProperties.SetText(e, sonuc.Hata);
        }
    }

    private static void OdakKayboldu(object? sender, FocusEventArgs a)
    {
        if (sender is not Entry e || !GetGecersiz(e)) return;
        // Bağlı değer değişmedi (DoNothing); kutuyu da ona geri getir ki yanlış tutar görünmesin.
        e.Text = (string)e.GetValue(SonGecerliProperty);
    }
}
