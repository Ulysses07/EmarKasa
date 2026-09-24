using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket C · 28: kaydetmeden önce sunucu uyarıları ("Yine de kaydet" / "Vazgeç"). Kaydı engellemez.
public partial class IslemlerViewModel
{
    /// <summary>Bekleyen uyarılar (aynı tutar, olağan dışı tutar, eski tarih, çek çift düşme).</summary>
    public ObservableCollection<IslemUyariDto> Uyarilar { get; } = new();

    [ObservableProperty] private bool _uyariBekliyor;

    /// <summary>
    /// Taslak için uyarıları sorar; uyarı varsa gösterir ve true döner (kayıt durur). Uyarılar
    /// alınamazsa (ağ, eski sunucu) kayıt engellenmez.
    /// </summary>
    private async Task<bool> UyarilariGosterAsync(IslemYaz g)
    {
        IReadOnlyList<IslemUyariDto> liste;
        try { liste = await _api.IslemUyarilariAsync(g, DuzenId == 0 ? null : DuzenId); }
        catch (Exception) { return false; }
        Uyarilar.Clear();
        foreach (var u in liste) Uyarilar.Add(u);
        UyariBekliyor = Uyarilar.Count > 0;
        return UyariBekliyor;
    }

    /// <summary>Uyarı kutusundaki "Yine de kaydet" (ileri tarih onayı bu noktaya gelmeden alınmıştır).</summary>
    [RelayCommand]
    private Task UyariYineDeKaydetAsync() => KaydetIcAsync(ileriTarihOnayli: true, uyariOnayli: true);

    [RelayCommand]
    private void UyariVazgec() => UyarilariTemizle();

    private void UyarilariTemizle()
    {
        if (Uyarilar.Count > 0) Uyarilar.Clear();
        UyariBekliyor = false;
    }
}
