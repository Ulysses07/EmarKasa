namespace Kasa.App.Views;

/// <summary>Windows uzantı, iOS UTType tanımlayıcısı ister; içerik ve boyut denetimi sunucu/modelde kalır.</summary>
internal static class DosyaSecimTurleri
{
    public static FilePickerFileType Pdf { get; } = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = [".pdf"],
        [DevicePlatform.iOS] = ["com.adobe.pdf"]
    });

    public static FilePickerFileType Belge { get; } = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = [".pdf", ".png", ".jpg", ".jpeg"],
        [DevicePlatform.iOS] = ["com.adobe.pdf", "public.png", "public.jpeg"]
    });
}
