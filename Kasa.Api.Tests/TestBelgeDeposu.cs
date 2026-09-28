using System.Security.Cryptography;
using Kasa.Api.Servisler;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>Testte doğrudan eklenen belge satırları için içerik: uygulamanın belge deposuna yazar ya da yalnız özetini hesaplar.</summary>
internal static class TestBelgeDeposu
{
    /// <summary>İçeriği uygulamanın belge deposuna yazar; satırın IcerikOzeti'ni döner (indirme ve yedek testleri).</summary>
    public static string Yaz(IServiceProvider servisler, byte[] icerik) => servisler.GetRequiredService<BelgeDeposu>().Yaz(icerik).Ozet;

    /// <summary>İçeriğin özeti (dosya yazılmaz; yalnız satır gerektiren testler).</summary>
    public static string Ozet(byte[] icerik) => Convert.ToHexString(SHA256.HashData(icerik));
}
