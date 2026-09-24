using Kasa.App.Core;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Media;
using Microsoft.Maui.Storage;

namespace Kasa.App.Services;

/// <summary>
/// Paket F: fiş/fatura (JPG, PNG, WEBP, HEIC, PDF) ve ERP12 CSV seçici; kamerası olan cihazda
/// fotoğraf çekme. Sınırı aşan dosyanın içeriği belleğe okunmaz (boyut doğrulamada görünür).
/// </summary>
public sealed class MauiDosyaSecici : IDosyaSecici
{
    private static readonly FilePickerFileType BelgeTurleri = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = [".jpg", ".jpeg", ".png", ".webp", ".heic", ".pdf"],
        [DevicePlatform.Android] = ["image/jpeg", "image/png", "image/webp", "image/heic", "application/pdf"],
        [DevicePlatform.iOS] = ["public.jpeg", "public.png", "org.webmproject.webp", "public.heic", "com.adobe.pdf"],
        [DevicePlatform.MacCatalyst] = ["public.jpeg", "public.png", "org.webmproject.webp", "public.heic", "com.adobe.pdf"],
    });

    private static readonly FilePickerFileType CsvTurleri = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = [".csv", ".txt"],
        [DevicePlatform.Android] = ["text/csv", "text/comma-separated-values", "text/plain"],
        [DevicePlatform.iOS] = ["public.comma-separated-values-text", "public.plain-text"],
        [DevicePlatform.MacCatalyst] = ["public.comma-separated-values-text", "public.plain-text"],
    });

    public bool KameraVar
    {
        get
        {
            try { return MediaPicker.Default.IsCaptureSupported; }
            catch (Exception) { return false; }
        }
    }

    public async Task<IReadOnlyList<SecilenDosya>> BelgeSecAsync()
    {
        var secilen = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickMultipleAsync(
            new PickOptions { PickerTitle = "Fiş ya da fatura seçin", FileTypes = BelgeTurleri }));
        var sonuc = new List<SecilenDosya>();
        foreach (var f in secilen?.OfType<FileResult>() ?? [])
            sonuc.Add(await OkuAsync(f, EkKurallari.EnFazlaBoyut));
        return sonuc;
    }

    public async Task<SecilenDosya?> FotografCekAsync()
    {
        if (!KameraVar) return null;
        var f = await MainThread.InvokeOnMainThreadAsync(() => MediaPicker.Default.CapturePhotoAsync());
        return f is null ? null : await OkuAsync(f, EkKurallari.EnFazlaBoyut);
    }

    public async Task<SecilenDosya?> CsvSecAsync()
    {
        var f = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(
            new PickOptions { PickerTitle = "ERP12 dışa aktarımını (CSV) seçin", FileTypes = CsvTurleri }));
        return f is null ? null : await OkuAsync(f, Erp12KarsilastirmaViewModel.EnFazlaCsvBoyutu);
    }

    /// <summary>En fazla <paramref name="sinir"/>+1 bayt okur; aşan dosya boş içerik ve gerçek (ya da sınır+1) boyutla döner.</summary>
    private static async Task<SecilenDosya> OkuAsync(FileResult f, long sinir)
    {
        await using var akis = await f.OpenReadAsync();
        if (akis.CanSeek && akis.Length > sinir) return new SecilenDosya(f.FileName, [], akis.Length);
        using var bellek = new MemoryStream();
        var tampon = new byte[81920];
        int n;
        while ((n = await akis.ReadAsync(tampon)) > 0)
        {
            bellek.Write(tampon, 0, n);
            if (bellek.Length > sinir) return new SecilenDosya(f.FileName, [], bellek.Length);
        }
        return new SecilenDosya(f.FileName, bellek.ToArray());
    }
}
