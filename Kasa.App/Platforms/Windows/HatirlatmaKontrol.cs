using System.Diagnostics;
using Kasa.ApiClient;
using Kasa.App.Core;

namespace Kasa.App.Platforms.Windows;

public static class HatirlatmaKontrol
{
    public const string Arg = "--hatirlatma-kontrol";

    /// <summary>Headless: token'la veriyi çek, hatırlatmaları hesapla, göster. Pencere açmaz.</summary>
    public static async Task CalistirAsync(IServiceProvider sp)
    {
        try
        {
            var api = sp.GetService(typeof(IKasaApi)) as IKasaApi;
            if (api is null) return;
            var kartlarDto = await api.KrediKartlariAsync();     // token yoksa 401 → catch
            var gorunumler = new List<KrediKartiGorunum>();
            foreach (var k in kartlarDto)
            {
                var g = new KrediKartiGorunum(k);
                foreach (var o in await api.KartOdemelerAsync(k.Id)) g.Odemeler.Add(o);
                gorunumler.Add(g);
            }
            var hatirlatmalar = KartHatirlatici.VadesiGelenler(
                gorunumler, DateOnly.FromDateTime(DateTime.Today));
            if (hatirlatmalar.Count > 0)
            {
                var bildirim = new WindowsBildirimServisi();
                bildirim.KayitOl();
                await bildirim.GosterAsync(hatirlatmalar);
            }
        }
        catch { /* token yok/expired/ağ — sessizce atla */ }
    }

    /// <summary>İlk normal açılışta kullanıcı-düzeyi günlük görev yoksa oluştur.</summary>
    public static void GoreviGarantile()
    {
        try
        {
            const string ad = "EmarKasaHatirlatici";
            var exe = Environment.ProcessPath!;
            var sorgu = Process.Start(new ProcessStartInfo("schtasks",
                $"/Query /TN {ad}") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true })!;
            sorgu.WaitForExit();
            if (sorgu.ExitCode == 0) return;                     // görev zaten var
            Process.Start(new ProcessStartInfo("schtasks",
                $"/Create /TN {ad} /SC DAILY /ST 09:00 /F /TR \"\\\"{exe}\\\" {Arg}\"")
                { UseShellExecute = false, CreateNoWindow = true })!.WaitForExit();
        }
        catch { /* görev kurulamazsa uygulama-içi şerit yine çalışır */ }
    }
}
