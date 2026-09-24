namespace Kasa.App.Core;

/// <summary>
/// Paket D (özellik 36) — düzenlemeyi de geri alma: "Güncellendi" satırında düğme "Önceki haline
/// döndür" olur. Sunucu eski değeri normal kayıt kontrollerinden geçirerek yazar, kayıt o arada
/// yeniden değiştiyse reddeder (409) ve dönüşü geçmişe "Güncellendi (geri alındı)" olarak işler.
/// </summary>
public partial class GecmisViewModel
{
    public const string GuncellemeEylemi = "Güncellendi";

    public static string OncekiHaleDonduMesaji(string tur) =>
        $"{tur} önceki haline döndürüldü; geçmişe \"Güncellendi (geri alındı)\" olarak yazıldı.";

    private static string GeriAlmaMesaji(GecmisSatiri s)
        => s.GuncellemeGeriAlmasi ? OncekiHaleDonduMesaji(s.Tur) : GeriAlindiMesaji(s.Tur);
}

public partial class GecmisSatiri
{
    /// <summary>Geri alma bu satırda bir düzenlemeyi mi geri çevirir?</summary>
    public bool GuncellemeGeriAlmasi => Eylem == GecmisViewModel.GuncellemeEylemi;
    /// <summary>"Önceki haline döndür" (güncelleme) ya da "Geri al" (silme).</summary>
    public string GeriAlMetni => GuncellemeGeriAlmasi ? "Önceki haline döndür" : "Geri al";
}
