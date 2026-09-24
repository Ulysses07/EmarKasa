using System.Diagnostics;
using Kasa.App.Core;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Kasa.App.Platforms.Windows;

/// <summary>
/// "Excel'e aktar", "Yazdır" ve "Ay paketini indir" dosyalarını Belgeler\Emar Kasa klasörüne kaydeder
/// ve varsayılan uygulamayla (CSV → Excel, HTML → tarayıcı, ZIP → Gezgin) açar. Ad güvenliği ve aynı ad
/// çakışması platformdan bağımsız <see cref="DosyaAktarma"/> / <see cref="DosyaKayit"/>'ta.
/// </summary>
public sealed class WindowsDosyaKaydedici : IDosyaKaydedici
{
    public async Task<string> KaydetAsync(string dosyaAdi, byte[] icerik)
    {
        var klasor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DosyaKayit.KlasorAdi);
        Directory.CreateDirectory(klasor);
        var yol = DosyaKayit.BenzersizYol(klasor, DosyaAktarma.GuvenliAd(dosyaAdi), File.Exists);
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
                () => Launcher.Default.OpenAsync(new OpenFileRequest("Aç", new ReadOnlyFile(yol))));
            if (acildi) return;
        }
        catch (Exception) { /* paketsiz uygulamada Launcher başarısız olabilir: kabukla dene */ }
        try
        {
            // Uzantı yalnız .csv, .html ya da .zip olabilir (DosyaAktarma.GuvenliAd): çalıştırılabilir dosya açılmaz.
            Process.Start(new ProcessStartInfo(yol) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception) { /* dosya ilişkilendirmesi yok: kullanıcı dosyayı yoldan açar */ }
    }
}
