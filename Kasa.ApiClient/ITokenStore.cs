namespace Kasa.ApiClient;

/// <summary>Token'ın platforma bağımsız saklanması. MAUI head SecureStorage'lı impl verir (Plan 3).</summary>
public interface ITokenStore
{
    Task<string?> OkuAsync();
    Task YazAsync(string token);
    Task TemizleAsync();
}

/// <summary>Bellek-içi varsayılan (testler + geçici). Kalıcı değildir.</summary>
public sealed class BellekTokenStore : ITokenStore
{
    private string? _token;
    public Task<string?> OkuAsync() => Task.FromResult(_token);
    public Task YazAsync(string token) { _token = token; return Task.CompletedTask; }
    public Task TemizleAsync() { _token = null; return Task.CompletedTask; }
}
