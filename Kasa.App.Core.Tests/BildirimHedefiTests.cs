namespace Kasa.App.Core.Tests;

/// <summary>Sunucu bildiriminin Hedef alanı (web rotası) masaüstü Shell rotasına çevrilir (tasarım 2026-09-30 masaüstü bildirimleri
/// §1 Tıklama): kart → Kartlar'da o kart, kredi → Krediler'de o kredi; tanınmayan ya da boş hedef Bildirimler'i açar.</summary>
public class BildirimHedefiTests
{
    [Theory]
    [InlineData("/#cards/3", "//kartlar?KartId=3")]
    [InlineData("/#loans/7", "//krediler?KrediId=7")]
    [InlineData("/#cards", "//kartlar")]
    [InlineData("/#loans", "//krediler")]
    [InlineData("/#home", "//bildirimler")]
    [InlineData("/#notifications", "//bildirimler")]
    [InlineData("/#cards/0", "//bildirimler")]
    [InlineData("/#cards/abc", "//bildirimler")]
    [InlineData("https://kasa.emarglobal.com/#cards/3", "//bildirimler")]
    [InlineData("", "//bildirimler")]
    [InlineData(null, "//bildirimler")]
    public void Hedef_uygulama_rotasina_cevrilir(string? hedef, string rota) => Assert.Equal(rota, BildirimHedefi.Rota(hedef));
}
