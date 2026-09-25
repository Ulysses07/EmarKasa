using Kasa.ApiClient;

namespace Kasa.App.Views;

internal static class DosyaIslemleri
{
    public static async Task KaydetAsync(Page sayfa, IndirilenDosya dosya, bool yazdir = false)
    {
        try
        {
            var ad = Path.GetFileName(dosya.DosyaAdi);
            if (yazdir)
            {
                var yol = Path.Combine(FileSystem.CacheDirectory, $"rapor-{Guid.NewGuid():N}.html");
                await File.WriteAllBytesAsync(yol, dosya.Icerik);
                await Launcher.Default.OpenAsync(new OpenFileRequest("Raporu yazdır / PDF kaydet", new ReadOnlyFile(yol, "text/html")));
                await sayfa.DisplayAlertAsync("Yazdır / PDF", "Açılan raporda Ctrl+P tuşlarına basın. Yazıcı olarak 'Microsoft Print to PDF' seçerek PDF kaydedebilirsiniz.", "Tamam");
                return;
            }
#if WINDOWS
            var secici = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(ad) };
            var uzanti = Path.GetExtension(ad);
            secici.FileTypeChoices.Add("Dosya", new List<string> { string.IsNullOrEmpty(uzanti) ? ".bin" : uzanti });
            var pencere = (Microsoft.UI.Xaml.Window)Application.Current!.Windows[0].Handler!.PlatformView!;
            WinRT.Interop.InitializeWithWindow.Initialize(secici, WinRT.Interop.WindowNative.GetWindowHandle(pencere));
            var hedef = await secici.PickSaveFileAsync();
            if (hedef is null) return;
            await Windows.Storage.FileIO.WriteBytesAsync(hedef, dosya.Icerik);
            await sayfa.DisplayAlertAsync("Dosya kaydedildi", hedef.Path, "Tamam");
#else
            var yol = Path.Combine(FileSystem.CacheDirectory, ad);
            await File.WriteAllBytesAsync(yol, dosya.Icerik);
            await Share.Default.RequestAsync(new ShareFileRequest("Dosyayı kaydet", new ShareFile(yol, dosya.IcerikTuru)));
#endif
        }
        catch (Exception) { await sayfa.DisplayAlertAsync("Dosya kaydedilemedi", "Hedef klasörü kontrol edip yeniden deneyin.", "Tamam"); }
    }
}
