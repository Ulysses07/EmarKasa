namespace Kasa.App.Core;

/// <summary>Geçmiş satırında kim ve hangi cihaz (paket E).</summary>
public partial class GecmisSatiri
{
    /// <summary>"Editör · EMAR · EMAR-LAPTOP"; eski kayıtlarda ve ortak izleyici şifresinde yalnız rol.</summary>
    public string Kim => KimMetni(RolAdi, Dto.Kullanici, Dto.Cihaz);

    public static string KimMetni(string rolAdi, string? kisi, string? cihaz)
        => string.Join(" · ", new[] { rolAdi, kisi, cihaz }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
}
