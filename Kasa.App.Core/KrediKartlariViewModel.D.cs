using CommunityToolkit.Mvvm.Input;

namespace Kasa.App.Core;

/// <summary>
/// Paket D (özellik 30): "Ekstreyi öde" / "Tamamını öde" ödeme formunu sayfanın gösterdiği borçla
/// ve bugünün tarihiyle doldurur; kayıt yine "Ekle" ile onaylanır (mevcut kart ödemesi kaydı).
/// </summary>
public partial class KrediKartlariViewModel
{
    [RelayCommand]
    private void EkstreyiOde(KrediKartiGorunum k) => OdemeFormunuDoldur(k, k.EkstreBorc);

    [RelayCommand]
    private void TamaminiOde(KrediKartiGorunum k) => OdemeFormunuDoldur(k, k.GuncelBorc);

    private void OdemeFormunuDoldur(KrediKartiGorunum k, decimal tutar)
    {
        Hata = null;
        if (tutar <= 0m) { Hata = "Ödenecek borç yok."; return; }
        k.OdemeTutarGiris = tutar;
        k.OdemeTarihGiris = Bugun;
    }
}

public sealed partial class KrediKartiGorunum
{
    /// <summary>"Ekstreyi öde" yalnız ödenmemiş ekstre borcu varken.</summary>
    public bool EkstreOdenebilir => EkstreBorc > 0m;
    /// <summary>"Tamamını öde" yalnız güncel borç varken.</summary>
    public bool TamamiOdenebilir => GuncelBorc > 0m;
    /// <summary>Düğme metinleri tutarı da gösterir: "Ekstreyi öde · 4.250,00".</summary>
    public string EkstreOdeMetni => $"Ekstreyi öde · {Bicim.Tl(EkstreBorc)}";
    public string TamaminiOdeMetni => $"Tamamını öde · {Bicim.Tl(GuncelBorc)}";
}
