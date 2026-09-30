namespace Kasa.Api.Tests;

// Düz WebApplicationFactory<Program> değil: o, JWT anahtarını ve editör kimliğini başka fabrikaların süreç ortamına yazdığı
// değişkenlerden alıyordu (tek başına koşunca "Kasa:JwtKey en az 32 bayt" ile düşüyordu) ve appsettings'teki
// 'Data Source=kasa.db' ile test çıktı dizininde kalıcı bir veritabanı dosyası açıyordu.
public class HealthTests(KasaWebFactory factory) : IClassFixture<KasaWebFactory>
{
    [Fact]
    public async Task Health_ok_doner()
    {
        using var client = factory.CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.True(resp.IsSuccessStatusCode);
    }
}
