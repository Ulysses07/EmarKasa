using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core;

/// <summary>Ortak Mesgul + Hata durumu ve güvenli çalıştırma sarmalayıcısı (nit c).</summary>
public partial class TemelViewModel : ObservableObject
{
    [ObservableProperty] private bool _mesgul;
    [ObservableProperty] private string? _hata;

    /// <summary>İşlemi Mesgul/Hata sarmalayıcısında çalıştırır; istisnada spinner iner, Hata yazılır.</summary>
    protected async Task CalistirAsync(Func<Task> islem)
    {
        Hata = null;
        Mesgul = true;
        try { await islem(); }
        catch (Exception) { Hata = "İşlem başarısız. Bağlantıyı kontrol edin."; }
        finally { Mesgul = false; }
    }
}
