namespace Kasa.ApiClient;

/// <summary>Geçersiz oturumun istemciye bildirilmesi; UI bağımlılığı içermez.</summary>
public interface IOturumBildirimleri
{
    /// <summary>Olay bağımsız değişkeni <see cref="OturumSonlandiEventArgs"/>'tır (neden taşır).</summary>
    event EventHandler? OturumSonlandi;
}

public enum OturumSonuNedeni
{
    /// <summary>Sunucu 401 döndürdü: token süresi doldu ya da oturum iptal edildi.</summary>
    OturumGecersiz,
    /// <summary>Kullanıcı kendi şifresini değiştirdi; eski oturumlar sunucuda kapandı.</summary>
    SifreDegisti,
}

public sealed class OturumSonlandiEventArgs(OturumSonuNedeni neden) : EventArgs
{
    public OturumSonuNedeni Neden { get; } = neden;
}
