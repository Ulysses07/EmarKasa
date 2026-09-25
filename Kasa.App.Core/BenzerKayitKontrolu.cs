using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Onay yalnız gösterilen kayıt gövdesi içindir; form değişikliği yeni kontrol ister.</summary>
public partial class BenzerKayitKontrolu(IBenzerKayitApi? api) : ObservableObject
{
    private string? _bekleyen;
    private string? _onaylanan;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UyariVar))]
    private string? _uyari;
    public bool UyariVar => !string.IsNullOrWhiteSpace(Uyari);

    public async Task<bool> DevamEdilebilirAsync(BenzerlikYaz arama, object govde, Func<bool> gecerli)
    {
        if (api is null) return gecerli();
        var anahtar = JsonSerializer.Serialize(govde);
        if (_onaylanan == anahtar) return gecerli();
        Temizle();
        var eslesmeler = await api.BenzerKayitlarAsync(arama);
        if (!gecerli()) return false;
        if (eslesmeler.Count == 0) return true;
        _bekleyen = anahtar;
        Uyari = "Benzer kayıt bulundu. Aynı işlemi yeniden girmediğinizi kontrol edin:\n" +
            string.Join("\n", eslesmeler.Select(k => $"{KaynakAdi(k.Kaynak)} #{k.Id} · {k.Tarih:dd.MM.yyyy} · {Bicim.Tl(k.Tutar)} ₺ · {k.Aciklama}"));
        return false;
    }
    private static string KaynakAdi(string kaynak) => kaynak switch { "KartHarcama" => "Kart harcaması", "KartOdeme" or "EskiKartOdeme" => "Kart ödemesi", _ => "Gider" };
    public bool Onayla()
    {
        if (_bekleyen is null) return false;
        _onaylanan = _bekleyen; _bekleyen = null; Uyari = null; return true;
    }
    public void Temizle() { _bekleyen = _onaylanan = null; Uyari = null; }
}
