using Kasa.App.Core;
using Microsoft.Maui.Storage;

namespace Kasa.App.Services;

/// <summary>Paket F: cihazda saklanan küçük ayarlar (ERP12 sütun eşleştirmesi). Sunucuya gitmez.</summary>
public sealed class PreferencesAyarDeposu : IAyarDeposu
{
    public string? Oku(string anahtar)
    {
        try { return Preferences.Default.Get<string?>(anahtar, null); }
        catch (Exception) { return null; }
    }

    public void Yaz(string anahtar, string? deger)
    {
        try
        {
            if (deger is null) Preferences.Default.Remove(anahtar);
            else Preferences.Default.Set(anahtar, deger);
        }
        catch (Exception) { /* ayar yazılamazsa eşleştirme yalnız bu oturumda kalır */ }
    }
}
