using Kasa.ApiClient;

namespace Kasa.App.Views;

internal static class DosyaIslemleri
{
    /// <summary>Dosyayı önce önbellekteki geçici dosyaya akışla indirir (yedek yüzlerce MB olabilir; bellekte tutulmaz),
    /// tamamlanınca kullanıcının seçtiği yere kopyalar. İndirme başarısızsa hata ilgili ekranda gösterilir; seçilen hedefe
    /// yalnız tamamlanmış dosya yazılır, yarım geçici dosya her durumda silinir. Kaydedilen dosyanın adı ve uzantısı sunucunun
    /// bildirdiği içerik türünden kurulur (<see cref="DosyaTurleri.GuvenliAd"/>): alıcının yüklediği '.hta' gibi uzantı ya da yön
    /// işareti kaydetme penceresine geçmez. <paramref name="disKaynak"/> (kullanıcının yüklediği belge) Windows'ta dosyaya
    /// internet kaynağı işareti (Mark-of-the-Web) ekler: işletim sistemi dosyayı açarken güvenlik uyarısını gösterir.</summary>
    public static async Task IndirVeKaydetAsync(Page sayfa, Func<Stream, Task<IndirmeBilgisi?>> indir, bool yazdir = false, bool disKaynak = false)
    {
        var gecici = Path.Combine(FileSystem.CacheDirectory, $"indirme-{Guid.NewGuid():N}.tmp");
        try
        {
            IndirmeBilgisi? bilgi;
            await using (var akis = new FileStream(gecici, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                bilgi = await indir(akis);
            if (bilgi is null) return;
            if (yazdir)
            {
                // Tarayıcı dosyayı açılıştan sonra okur; rapor önbellekte kalır.
                var rapor = Path.Combine(FileSystem.CacheDirectory, $"rapor-{Guid.NewGuid():N}.html");
                File.Move(gecici, rapor);
                await Launcher.Default.OpenAsync(new OpenFileRequest("Raporu yazdır / PDF kaydet", new ReadOnlyFile(rapor, "text/html")));
                await sayfa.DisplayAlertAsync("Yazdır / PDF", "Açılan raporda Ctrl+P tuşlarına basın. Yazıcı olarak 'Microsoft Print to PDF' seçerek PDF kaydedebilirsiniz.", "Tamam");
                return;
            }
            var ad = DosyaTurleri.GuvenliAd(bilgi.DosyaAdi, bilgi.IcerikTuru, "dosya");
#if WINDOWS
            var secici = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(ad) };
            secici.FileTypeChoices.Add(DosyaTurleri.Aciklama(bilgi.IcerikTuru), new List<string> { Path.GetExtension(ad) });
            var pencere = (Microsoft.UI.Xaml.Window)Application.Current!.Windows[0].Handler!.PlatformView!;
            WinRT.Interop.InitializeWithWindow.Initialize(secici, WinRT.Interop.WindowNative.GetWindowHandle(pencere));
            var hedef = await secici.PickSaveFileAsync();
            if (hedef is null) return;
            var kaynak = await Windows.Storage.StorageFile.GetFileFromPathAsync(gecici);
            await kaynak.CopyAndReplaceAsync(hedef);
            if (disKaynak) InternetKaynagiIsaretle(hedef.Path);
            await sayfa.DisplayAlertAsync("Dosya kaydedildi", hedef.Path, "Tamam");
#else
            var yol = Path.Combine(FileSystem.CacheDirectory, ad);
            File.Move(gecici, yol, overwrite: true);
            await Share.Default.RequestAsync(new ShareFileRequest("Dosyayı kaydet", new ShareFile(yol, bilgi.IcerikTuru)));
#endif
        }
        catch (Exception) { await sayfa.DisplayAlertAsync("Dosya kaydedilemedi", "Hedef klasörü ve disk alanını kontrol edip yeniden deneyin.", "Tamam"); }
        finally
        {
            try { File.Delete(gecici); }
            catch (IOException) { /* önbellek temizliği işletim sistemine kalır */ }
            catch (UnauthorizedAccessException) { /* önbellek temizliği işletim sistemine kalır */ }
        }
    }

#if WINDOWS
    /// <summary>En iyi çaba: NTFS dışı ya da yazılamayan hedefte (ör. ağ sürücüsü) işaret eklenmez, kayıt yine başarılıdır.</summary>
    private static void InternetKaynagiIsaretle(string yol)
    {
        try { File.WriteAllText(yol + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { /* işaret isteğe bağlıdır */ }
    }
#endif
}
