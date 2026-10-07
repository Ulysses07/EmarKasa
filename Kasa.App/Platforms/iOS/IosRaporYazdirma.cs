using CoreGraphics;
using Foundation;
using UIKit;

namespace Kasa.App.Platforms.iOS;

/// <summary>HTML raporu iOS'un yazdırma/PDF paylaşma ekranına verir; masaüstü klavye komutu gerektirmez.</summary>
internal static class IosRaporYazdirma
{
    public static Task YazdirAsync(string yol) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        var html = await File.ReadAllTextAsync(yol);
        var controller = UIPrintInteractionController.SharedPrintController
            ?? throw new InvalidOperationException("Yazdırma penceresi açılamadı.");
        using var formatter = new UIMarkupTextPrintFormatter(html);
        using var bilgi = UIPrintInfo.PrintInfo;
        bilgi.JobName = "Emar Kasa gider raporu";
        bilgi.OutputType = UIPrintInfoOutputType.General;
        controller.PrintInfo = bilgi;
        controller.PrintFormatter = formatter;
        controller.ShowsPageRange = true;
        var bitis = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Tamamlandi(UIPrintInteractionController? _, bool __, NSError? hata)
        {
            if (hata is null)
                bitis.TrySetResult();
            else
                bitis.TrySetException(new InvalidOperationException("Rapor yazdırılamadı."));
        }
        try
        {
            bool acildi;
            if (UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad)
            {
                var gorunum = Platform.GetCurrentUIViewController()?.View
                    ?? throw new InvalidOperationException("Rapor penceresi bulunamadı.");
                acildi = controller.PresentFromRectInView(
                    new CGRect(gorunum.Bounds.GetMidX(), gorunum.Bounds.GetMidY(), 1, 1), gorunum, true, Tamamlandi);
            }
            else
                acildi = controller.Present(true, Tamamlandi);
            if (!acildi)
                throw new InvalidOperationException("Yazdırma penceresi açılamadı.");
            await bitis.Task;
        }
        finally
        {
            controller.PrintFormatter = null!;
        }
    });
}
