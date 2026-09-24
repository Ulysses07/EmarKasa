using System.Diagnostics;
using Kasa.App.Core;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Kasa.App.Platforms.Windows;

/// <summary>
/// "Excel'e aktar" dosyalarını Belgeler\Emar Kasa klasörüne kaydeder ve varsayılan uygulamayla
/// (Excel) açar. Ad güvenliği ve aynı ad çakışması platformdan bağımsız <see cref="DosyaKayit"/>'ta.
/// </summary>
public sealed class WindowsDosyaKaydedici : IDosyaKaydedici
{
    public async Task<string> KaydetAsync(string dosyaAdi, byte[] icerik)
    {
        var klasor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DosyaKayit.KlasorAdi);
        Directory.CreateDirectory(klasor);
        var yol = DosyaKayit.BenzersizYol(klasor, DosyaKayit.GuvenliAd(dosyaAdi), File.Exists);
        await using (var akis = new FileStream(yol, FileMode.CreateNew, FileAccess.Write))
            await akis.WriteAsync(icerik);
        await AcAsync(yol);
        return yol;
    }

    /// <summary>Dosyayı varsayılan uygulamayla açar; açılamazsa dosya yine kaydedilmiştir (yol sayfada görünür).</summary>
    private static async Task AcAsync(string yol)
    {
        try
        {
            var acildi = await MainThread.InvokeOnMainThreadAsync(
                () => Launcher.Default.OpenAsync(new OpenFileRequest("Excel'de aç", new ReadOnlyFile(yol))));
            if (acildi) return;
        }
        catch (Exception) { /* paketsiz uygulamada Launcher başarısız olabilir: kabukla dene */ }
        try
        {
            // Uzantı her zaman .csv (DosyaKayit.GuvenliAd): kabuk yalnız CSV'nin varsayılan uygulamasını açar.
            Process.Start(new ProcessStartInfo(yol) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception) { /* Excel / CSV ilişkilendirmesi yok: kullanıcı dosyayı yoldan açar */ }
    }
}
