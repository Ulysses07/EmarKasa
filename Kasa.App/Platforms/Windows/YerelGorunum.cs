using Microsoft.Maui.Handlers;

namespace Kasa.App.WinUI;

/// <summary>Windows'ta yerel görünüm (docs/specs/2026-11-26-slop-temizligi.md §7):
/// <list type="bullet">
/// <item>Windows tip ölçeğinde Bold yoktur: kalın yazı (FontAttributes.Bold) etiket ve düğmede Semibold çizilir.</item>
/// <item>Form alanlarının tek görünümü: metin, seçim ve tarih kutusunun görünür sınırı FieldBorder çerçevesidir. WinUI denetiminin kendi
/// kenarı ve zemini kaldırılır (çerçevenin içinde ikinci bir kutu görünüyordu); odakta yalnız alt çizgi kalır.</item>
/// </list>
/// Kaynak anahtarları WinUI'nin hafif biçimlendirme (lightweight styling) anahtarlarıdır; denetimin kendi kaynaklarına yazılır, uygulamanın
/// geri kalanını etkilemez.</summary>
internal static class YerelGorunum
{
    private static readonly Microsoft.UI.Xaml.Thickness Sifir = new(0);
    private static readonly Microsoft.UI.Xaml.Thickness OdakCizgisi = new(0, 0, 0, 2);

    public static void Kur()
    {
        LabelHandler.Mapper.AppendToMapping(nameof(ITextStyle.Font), (h, v) =>
        {
            if (v.Font.Weight >= FontWeight.Bold)
                h.PlatformView.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        });
        ButtonHandler.Mapper.AppendToMapping(nameof(ITextStyle.Font), (h, v) =>
        {
            if (v is ITextStyle yazi && yazi.Font.Weight >= FontWeight.Bold)
                h.PlatformView.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        });
        EntryHandler.Mapper.AppendToMapping("TekCerceve", (h, _) => Sade(h.PlatformView, "TextControl", odak: true));
        PickerHandler.Mapper.AppendToMapping("TekCerceve", (h, _) => Sade(h.PlatformView, "ComboBox", odak: false));
        // MAUI seçicinin başlığını (Title) WinUI'de kutunun üstündeki başlık satırı yapar: kutu iki satır yükseliyordu. Başlık, metin
        // girişindeki gibi yer tutucu olur; alan etiketi zaten kutunun üstündedir.
        // Başlık rengi eşlemesi (TitleColor) başlık satırını yeniden kurar: ikisinden sonra da kaldırılır.
        foreach (var anahtar in new[] { nameof(IPicker.Title), nameof(IPicker.TitleColor) })
            PickerHandler.Mapper.AppendToMapping(anahtar, (h, v) =>
            {
                h.PlatformView.Header = null;
                h.PlatformView.HeaderTemplate = null;
                h.PlatformView.PlaceholderText = v.Title ?? "";
            });
        DatePickerHandler.Mapper.AppendToMapping("TekCerceve", (h, _) => Sade(h.PlatformView, "CalendarDatePicker", odak: false));
    }

    private static void Sade(Microsoft.UI.Xaml.Controls.Control denetim, string onek, bool odak)
    {
        var r = denetim.Resources;
        Yaz(r, onek + "BorderThemeThickness", Sifir);
        if (odak)
        {
            Yaz(r, onek + "BorderThemeThicknessFocused", OdakCizgisi);
            // Odak çizgisi Windows vurgu mavisi değil, uygulamanın yeşili (seçim ve odak tek renk).
            if (Application.Current?.Resources.TryGetValue("Green", out var yesil) == true && yesil is Color renk)
                Yaz(r, onek + "BorderBrushFocused", new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb((byte)(renk.Alpha * 255), (byte)(renk.Red * 255), (byte)(renk.Green * 255), (byte)(renk.Blue * 255))));
        }
        // Her anahtar kendi fırçasını alır: aynı fırça nesnesi birden çok anahtara yazılınca WinUI COM hatası veriyordu.
        foreach (var durum in new[] { "", "PointerOver", "Pressed", "Focused", "Disabled" })
            Yaz(r, onek + "Background" + durum, Saydam());
        denetim.BorderThickness = Sifir;
        denetim.Background = Saydam();
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush Saydam() => new(Microsoft.UI.Colors.Transparent);

    /// <summary>Görünüm ayarı sayfanın açılmasını hiçbir zaman engellemez: yazılamayan anahtar atlanır (denetim varsayılan görünümünde kalır).</summary>
    private static void Yaz(Microsoft.UI.Xaml.ResourceDictionary r, string anahtar, object deger)
    {
        try
        {
            r[anahtar] = deger;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Yerel görünüm anahtarı yazılamadı ({anahtar}): {ex.Message}");
        }
    }
}
