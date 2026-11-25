namespace Kasa.App.Views;

/// <summary>Windows uzantı, iOS UTType tanımlayıcısı ister; içerik ve boyut denetimi sunucu/modelde kalır.</summary>
internal static class DosyaSecimTurleri
{
    /// <summary>Ekstre: PDF ya da Excel (.xlsx, .xls).</summary>
    public static FilePickerFileType Ekstre { get; } = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = [".pdf", ".xlsx", ".xls"],
        [DevicePlatform.iOS] = ["com.adobe.pdf", "org.openxmlformats.spreadsheetml.sheet", "com.microsoft.excel.xls"]
    });

    public static FilePickerFileType Belge { get; } = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = [".pdf", ".png", ".jpg", ".jpeg"],
        [DevicePlatform.iOS] = ["com.adobe.pdf", "public.png", "public.jpeg"]
    });
}
