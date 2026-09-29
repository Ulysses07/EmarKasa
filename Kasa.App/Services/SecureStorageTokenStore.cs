using Kasa.ApiClient;

namespace Kasa.App.Services;

/// <summary>Token'ı MAUI SecureStorage'da tutar (platform güvenli deposu). Tanıdık cihaz belirteçleri rol başına ayrı
/// anahtardadır (kasa_cihaz_editor, kasa_cihaz_viewer, kasa_cihaz_alici): çıkış ve oturum sonu (TemizleAsync) yalnız
/// oturum token'ını siler, cihaz tanıdık kalır; aynı bilgisayarda başka rolle giriş öncekinin belirtecini ezmez.
/// Okuma/yazma hatalarını KasaApiClient yutar: belirteç isteğe bağlıdır, girişi engellemez.</summary>
public sealed class SecureStorageTokenStore : ITokenStore
{
    private const string Anahtar = "kasa_token";
    private const string CihazOneki = "kasa_cihaz_";

    public async Task<string?> OkuAsync() => await SecureStorage.Default.GetAsync(Anahtar);
    public async Task YazAsync(string token) => await SecureStorage.Default.SetAsync(Anahtar, token);
    public Task TemizleAsync() { SecureStorage.Default.Remove(Anahtar); return Task.CompletedTask; }
    public async Task<string?> CihazOkuAsync(string rol) => await SecureStorage.Default.GetAsync(CihazOneki + rol);
    public async Task CihazYazAsync(string rol, string belirtec) => await SecureStorage.Default.SetAsync(CihazOneki + rol, belirtec);
}
