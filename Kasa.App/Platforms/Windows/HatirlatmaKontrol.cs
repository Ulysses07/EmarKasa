using System.Diagnostics;
using System.Net;
using System.Text;
using Kasa.ApiClient;
using Kasa.App.Core;

namespace Kasa.App.Platforms.Windows;

public static class HatirlatmaKontrol
{
    public const string Arg = "--hatirlatma-kontrol";
    private const string GorevAdi = "EmarKasaHatirlatici";

    /// <summary>Headless: token'la veriyi çek, hatırlatmaları hesapla, göster. Pencere açmaz.</summary>
    public static async Task CalistirAsync(IServiceProvider sp)
    {
        var durum = HatirlatmaDurumu.Varsayilan();
        var bugun = DateOnly.FromDateTime(DateTime.Today);
        try
        {
            var api = sp.GetService(typeof(IKasaApi)) as IKasaApi;
            if (api is null) return;
            var kartlarDto = await api.KrediKartlariAsync();     // token yoksa/dolmuşsa 401
            var gorunumler = new List<KrediKartiGorunum>();
            foreach (var k in kartlarDto)
            {
                var g = new KrediKartiGorunum(k);
                foreach (var o in await api.KartOdemelerAsync(k.Id)) g.Odemeler.Add(o);
                gorunumler.Add(g);
            }
            // Son kontrolden bu yana kaçan günler de taranır (bilgisayar kapalı olabilir).
            var hatirlatmalar = KartHatirlatici.VadesiGelenler(gorunumler, bugun, durum.SonKontrol);
            if (hatirlatmalar.Count > 0)
            {
                var bildirim = new WindowsBildirimServisi();
                bildirim.KayitOl();
                await bildirim.GosterAsync(hatirlatmalar);
            }
            durum.SonKontrol = bugun;
        }
        catch (KasaApiException ex) when (ex.DurumKodu == HttpStatusCode.Unauthorized)
        {
            // Oturum dolmuş/kapatılmış: hatırlatmalar sessizce durmasın, günde bir kez uyar.
            if (durum.SonOturumUyarisi != bugun)
            {
                var bildirim = new WindowsBildirimServisi();
                bildirim.KayitOl();
                bildirim.MetinGoster("Emar Kasa", "Oturum süresi doldu. Kart hatırlatmaları için uygulamayı açıp giriş yapın.");
                durum.SonOturumUyarisi = bugun;
            }
        }
        catch { /* ağ hatası — bir sonraki çalıştırmada kaçan günler telafi edilir */ }
    }

    /// <summary>
    /// Kullanıcı-düzeyi günlük görevi oluşturur/günceller. "Kaçırılırsa ilk fırsatta çalıştır"
    /// (StartWhenAvailable) açık olduğundan 09:00'da kapalı olan bilgisayar açılınca çalışır.
    /// </summary>
    public static void GoreviGarantile()
    {
        try
        {
            var exe = Environment.ProcessPath!;
            var xml = $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <Triggers>
                    <CalendarTrigger>
                      <StartBoundary>2026-01-01T09:00:00</StartBoundary>
                      <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
                    </CalendarTrigger>
                  </Triggers>
                  <Settings>
                    <StartWhenAvailable>true</StartWhenAvailable>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>
                  </Settings>
                  <Actions>
                    <Exec>
                      <Command>{System.Security.SecurityElement.Escape(exe)}</Command>
                      <Arguments>{Arg}</Arguments>
                    </Exec>
                  </Actions>
                </Task>
                """;
            var dosya = Path.Combine(Path.GetTempPath(), "EmarKasaHatirlatici.xml");
            File.WriteAllText(dosya, xml, Encoding.Unicode);
            var p = Process.Start(new ProcessStartInfo("schtasks", $"/Create /TN {GorevAdi} /XML \"{dosya}\" /F")
                { UseShellExecute = false, CreateNoWindow = true,
                  RedirectStandardOutput = true, RedirectStandardError = true })!;
            p.WaitForExit();
            try { File.Delete(dosya); } catch { }
            if (p.ExitCode == 0) return;

            // XML reddedilirse eski basit günlük göreve düş.
            Process.Start(new ProcessStartInfo("schtasks",
                $"/Create /TN {GorevAdi} /SC DAILY /ST 09:00 /F /TR \"\\\"{exe}\\\" {Arg}\"")
                { UseShellExecute = false, CreateNoWindow = true })!.WaitForExit();
        }
        catch { /* görev kurulamazsa uygulama-içi şerit yine çalışır */ }
    }
}
