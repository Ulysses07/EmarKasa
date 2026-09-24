using Kasa.App.Core;

namespace Kasa.App.Services;

/// <summary>
/// Toplu yükleme için Excel (xlsx) ya da CSV dosyası seçer (MAUI FilePicker). Kullanıcı vazgeçerse
/// null; dosya <see cref="TopluMetin.EnBuyukDosya"/>'dan büyükse içerik okunmadan boyutu aşan bir dizi
/// döner ki VM "dosya çok büyük" desin. Biçimi VM içerikten anlar (xlsx bir ZIP'tir).
/// </summary>
public sealed class MauiDosyaSecici : IDosyaSecici
{
    private static readonly FilePickerFileType TabloTuru = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = [".xlsx", ".csv", ".txt", ".tsv"],
        [DevicePlatform.MacCatalyst] = ["org.openxmlformats.spreadsheetml.sheet", "public.comma-separated-values-text", "public.plain-text"],
        [DevicePlatform.Android] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "text/csv", "text/plain", "text/comma-separated-values"],
        [DevicePlatform.iOS] = ["org.openxmlformats.spreadsheetml.sheet", "public.comma-separated-values-text", "public.plain-text"],
    });

    public async Task<SecilenDosya?> SecAsync()
    {
        var sonuc = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Excel (xlsx) ya da CSV dosyası seçin",
            FileTypes = TabloTuru,
        });
        if (sonuc is null) return null;

        await using var akis = await sonuc.OpenReadAsync();
        using var bellek = new MemoryStream();
        var tampon = new byte[81920];
        int okunan;
        while ((okunan = await akis.ReadAsync(tampon)) > 0)
        {
            bellek.Write(tampon, 0, okunan);
            if (bellek.Length > TopluMetin.EnBuyukDosya) break;   // fazlasını okumaya gerek yok
        }
        return new SecilenDosya(sonuc.FileName, bellek.ToArray());
    }
}
