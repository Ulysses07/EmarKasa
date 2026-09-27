namespace Kasa.ApiClient;

/// <summary>
/// Token'ın platforma bağımsız saklanması. MAUI head SecureStorage'lı impl verir (Plan 3).
/// Oturum (JWT) ile tanıdık cihaz belirteçleri ayrı tutulur: çıkış ve oturum sonu (<see cref="TemizleAsync"/>) yalnız
/// JWT'yi siler; cihaz belirteçleri cihazda kalır ve sonraki girişte X-Kasa-Cihaz başlığıyla gönderilir.
/// </summary>
public interface ITokenStore
{
    Task<string?> OkuAsync();
    Task YazAsync(string token);
    Task TemizleAsync();

    /// <summary>Sunucunun bu rol ("editor", "viewer", "alici") için verdiği tanıdık cihaz belirteci (yoksa null).
    /// Rol başına ayrı tutulur: aynı cihazda sonradan başka rolle giriş öncekinin belirtecini ezmez.</summary>
    Task<string?> CihazOkuAsync(string rol);
    Task CihazYazAsync(string rol, string belirtec);
}

/// <summary>Bellek-içi varsayılan (testler + geçici). Kalıcı değildir.</summary>
public sealed class BellekTokenStore : ITokenStore
{
    private string? _token;
    private readonly Dictionary<string, string> _cihazlar = new(StringComparer.Ordinal);
    public Task<string?> OkuAsync() => Task.FromResult(_token);
    public Task YazAsync(string token) { _token = token; return Task.CompletedTask; }
    public Task TemizleAsync() { _token = null; return Task.CompletedTask; }
    public Task<string?> CihazOkuAsync(string rol) => Task.FromResult(_cihazlar.GetValueOrDefault(rol));
    public Task CihazYazAsync(string rol, string belirtec) { _cihazlar[rol] = belirtec; return Task.CompletedTask; }
}
