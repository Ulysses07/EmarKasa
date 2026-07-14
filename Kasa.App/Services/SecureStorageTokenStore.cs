using Kasa.ApiClient;

namespace Kasa.App.Services;

/// <summary>Token'ı MAUI SecureStorage'da tutar (platform güvenli deposu).</summary>
public sealed class SecureStorageTokenStore : ITokenStore
{
    private const string Anahtar = "kasa_token";

    public async Task<string?> OkuAsync() => await SecureStorage.Default.GetAsync(Anahtar);
    public async Task YazAsync(string token) => await SecureStorage.Default.SetAsync(Anahtar, token);
    public Task TemizleAsync() { SecureStorage.Default.Remove(Anahtar); return Task.CompletedTask; }
}
