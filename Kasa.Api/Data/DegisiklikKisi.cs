namespace Kasa.Api.Data;

/// <summary>Değişiklik geçmişi satırına kişi ve cihaz (kişisel hesaplardan sonra yazılır; eski satırlarda null).</summary>
public partial class DegisiklikEntity
{
    /// <summary>Değişikliği yapan kişinin adı (kişisel hesap ya da yerleşik editör); ortak izleyici şifresinde null.</summary>
    public string? Kullanici { get; set; }
    /// <summary>Giriş yapılan cihazın adı (ör. "EMAR-LAPTOP").</summary>
    public string? Cihaz { get; set; }
}
