using Microsoft.Extensions.Logging;

namespace Kasa.Api.Tests;

public class UyariToplayiciTests
{
    // CI (Linux) kırmızısı: veri koruma anahtarını ilk üreten testte çerçevenin "No XML encryptor configured" uyarısı
    // toplanıyor ve 'uyarı yok' beklentisi koşu sırasına bağlı olarak düşüyordu. Toplayıcı çerçevenin bu kategorisini almaz.
    [Fact]
    public void Veri_koruma_uyarilari_toplanmaz_uygulama_uyarilari_toplanir()
    {
        using var logs = new UyariToplayici();
        logs.CreateLogger("Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager")
            .LogWarning("No XML encryptor configured. Key {KeyId} may be persisted to storage in unencrypted form.", Guid.NewGuid());
        logs.CreateLogger("Kasa.Api.Servisler.HesapServisi").LogWarning("Uygulama uyarısı");
        Assert.Equal(new[] { "Uygulama uyarısı" }, logs.Uyarilar.ToArray());
    }
}
