namespace Kasa.ApiClient;

/// <summary>
/// Token'ın platforma bağımsız saklanması. MAUI head SecureStorage'lı impl verir (Plan 3).
/// Oturum (JWT) ile tanıdık cihaz belirteci ayrı tutulur: çıkış ve oturum sonu (<see cref="TemizleAsync"/>) yalnız
/// JWT'yi siler; cihaz belirteci cihazda kalır ve sonraki girişte X-Kasa-Cihaz başlığıyla gönderilir.
/// </summary>
public interface ITokenStore
{
    Task<string?> OkuAsync();
    Task YazAsync(string token);
    Task TemizleAsync();

    /// <summary>Sunucunun başarılı girişte verdiği tanıdık cihaz belirteci (yoksa null).</summary>
    Task<string?> CihazOkuAsync();
    Task CihazYazAsync(string belirtec);
}

/// <summary>Bellek-içi varsayılan (testler + geçici). Kalıcı değildir.</summary>
public sealed class BellekTokenStore : ITokenStore
{
    private string? _token;
    private string? _cihaz;
    public Task<string?> OkuAsync() => Task.FromResult(_token);
    public Task YazAsync(string token) { _token = token; return Task.CompletedTask; }
    public Task TemizleAsync() { _token = null; return Task.CompletedTask; }
    public Task<string?> CihazOkuAsync() => Task.FromResult(_cihaz);
    public Task CihazYazAsync(string belirtec) { _cihaz = belirtec; return Task.CompletedTask; }
}
