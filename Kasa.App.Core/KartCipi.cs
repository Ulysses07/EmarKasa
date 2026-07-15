using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core;

/// <summary>Kart harcaması için seçilebilir kart çipi: Id + ad + seçili durumu.</summary>
public partial class KartCipi : ObservableObject
{
    public int Id { get; }
    public string Ad { get; }
    public KartCipi(int id, string ad) { Id = id; Ad = ad; }
    [ObservableProperty] private bool _secili;
}
