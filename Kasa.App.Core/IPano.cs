namespace Kasa.App.Core;

/// <summary>
/// Panoya metin yazar (Windows uygulamasında MAUI Clipboard). Yoksa (testler, desteklenmeyen
/// platform) kopyalama düğmeleri anlaşılır bir hata gösterir.
/// </summary>
public interface IPano
{
    Task YazAsync(string metin);
}
