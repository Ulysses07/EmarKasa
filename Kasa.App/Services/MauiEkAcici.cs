using System.Diagnostics;
using Kasa.App.Core;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Kasa.App.Services;

/// <summary>
/// Paket F: indirilen eki uygulamanın geçici klasörüne yazar ve cihazın varsayılan uygulamasıyla
/// (fotoğraf görüntüleyici / PDF okuyucu) açar. Yalnız izinli uzantılar olduğu gibi açılır;
/// diğerleri ".bin" alır (çalıştırılabilir dosya asla açılmaz).
/// </summary>
public sealed class MauiEkAcici : IEkAcici
{
    public async Task AcAsync(string dosyaAdi, byte[] icerik)
    {
        var klasor = Path.Combine(FileSystem.CacheDirectory, "ekler", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        var yol = Path.Combine(klasor, GuvenliAd(dosyaAdi));
        await File.WriteAllBytesAsync(yol, icerik);
        try
        {
            var acildi = await MainThread.InvokeOnMainThreadAsync(
                () => Launcher.Default.OpenAsync(new OpenFileRequest(dosyaAdi, new ReadOnlyFile(yol))));
            if (acildi) return;
        }
        catch (Exception) { /* paketsiz Windows uygulamasında Launcher başarısız olabilir: kabukla dene */ }
#if WINDOWS
        Process.Start(new ProcessStartInfo(yol) { UseShellExecute = true })?.Dispose();
#else
        throw new InvalidOperationException("Dosya açılamadı.");
#endif
    }

    /// <summary>Klasör parçası ve geçersiz karakterler atılır; izinli olmayan uzantıya ".bin" eklenir.</summary>
    public static string GuvenliAd(string ad)
    {
        var yalin = Path.GetFileName(ad.Replace('\\', '/').Split('/').LastOrDefault() ?? "");
        var gecersiz = Path.GetInvalidFileNameChars();
        yalin = new string(yalin.Select(c => gecersiz.Contains(c) || char.IsControl(c) ? '-' : c).ToArray()).Trim(' ', '.');
        if (yalin.Length == 0) yalin = "ek";
        if (yalin.Length > 100) yalin = yalin[..90] + Path.GetExtension(yalin);
        return EkKurallari.IzinliUzantilar.Contains(Path.GetExtension(yalin).ToLowerInvariant()) ? yalin : yalin + ".bin";
    }
}
