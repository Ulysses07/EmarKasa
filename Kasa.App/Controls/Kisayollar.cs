using System.Diagnostics;
using System.Windows.Input;

namespace Kasa.App.Controls;

/// <summary>Masaüstü klavye kısayolları (docs/specs/2026-11-26-slop-temizligi.md §8): F5 yenile, Ctrl+N yeni kayıt, Ctrl+S kaydet,
/// Ctrl+F ara.</summary>
public enum Kisayol { Yenile, Yeni, Kaydet, Ara }

/// <summary>Kısayolu karşılayan sayfa ya da pencere. Komut tuşa basıldığı an eşzamanlı istenir; tuş yalnız komut varken yutulur.</summary>
public interface IKisayolHedefi
{
    /// <summary>Kısayolun komutu; sayfada bu kısayol yoksa ya da ilgili form kapalıysa null.</summary>
    ICommand? KisayolKomutu(Kisayol kisayol);
}

/// <summary>Kısayolun açık sayfaya uygulanması (platformdan bağımsız; tuş dinleyicisi Platforms/Windows/KlavyeKisayollari.cs).</summary>
public static class Kisayollar
{
    /// <summary>Kısayolu sayfaya uygular; uygulandıysa true (tuş basımı yutulur). Sayfa üstü pencere (silme onayı, güncellemeler)
    /// açıkken ya da sayfa meşgulken (kayıt ya da yükleme sürerken gövde kapalıdır; düğmeler de basılamaz) hiçbir şey yapılmaz; arama
    /// yalnız odak taşıdığı için meşgulken de çalışır. F5, sayfanın kendi komutu yoksa başlık satırının Yenile'sidir (düğmeyle aynı yol).</summary>
    public static bool Uygula(Page? sayfa, Kisayol kisayol)
    {
        if (sayfa is null || (sayfa is ContentPage icerik && SayfaUstuPencere.Acik(icerik)))
            return false;
        if (kisayol != Kisayol.Ara && sayfa.BindingContext is Kasa.App.Core.IYurutmeYuzeyi { Mesgul: true })
            return false;
        if (sayfa is IKisayolHedefi hedef && hedef.KisayolKomutu(kisayol) is { } komut)
        {
            if (komut.CanExecute(null))
                komut.Execute(null);
            return true;
        }
        if (kisayol == Kisayol.Yenile && sayfa.GetVisualTreeDescendants().OfType<SayfaBasligi>().FirstOrDefault() is { } baslik)
            return baslik.YenileTetikle();
        return false;
    }

    /// <summary>Async işi komut olarak verir (Ctrl+N ile bölme açmak gibi sayfa adımları için); hata günlüğe yazılır.</summary>
    public static ICommand Komut(Func<Task> adim)
        => new Command(async () =>
        {
            try
            { await adim(); }
            catch (Exception ex) { Debug.WriteLine($"Kısayol çalıştırılamadı: {ex}"); }
        });
}
