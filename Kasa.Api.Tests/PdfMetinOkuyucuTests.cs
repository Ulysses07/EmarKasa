using System.Collections.Concurrent;
using System.Diagnostics;
using Kasa.Api.Servisler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kasa.Api.Tests;

/// <summary>
/// Poppler yerine enjekte edilen sahte araçlarla süreç yönetimi (statement-4). Komutlar Windows'ta cmd, diğer
/// sistemlerde /bin/sh ile çalışır; asıl iş bir alt süreçte yapılır ki süreç ağacının tamamının öldürüldüğü sınansın.
/// </summary>
public class PdfMetinOkuyucuTests
{
    private static readonly byte[] Pdf = "%PDF-1.7 sahte belge"u8.ToArray();

    private static IConfiguration Ayar(string? zamanAsimi) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Pdf:ZamanAsimiSaniye"] = zamanAsimi }).Build();

    private static ProcessStartInfo Kabuk(string windows, string unix) => OperatingSystem.IsWindows()
        ? new ProcessStartInfo("cmd.exe") { Arguments = "/d /s /c \"" + windows + "\"" }
        : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", unix } };

    // Başarılı sahte Poppler: pdfinfo tek sayfa bildirir, pdftotext tek hareket satırı basar.
    private static ProcessStartInfo Basarili(string arac) => arac == "pdfinfo"
        ? Kabuk("echo Pages: 1", "echo 'Pages: 1'")
        : Kabuk("echo 01.09.2026 Test 10,00 TL", "echo '01.09.2026 Test 10,00 TL'");

    [Fact]
    public async Task Cikti_uretmeden_takilan_arac_zaman_sinirinda_sonlandirilir_slot_ve_gecici_dosya_birakmaz()
    {
        var log = new LogToplayici();
        var girdiler = new ConcurrentQueue<string>();
        // Uyuyan alt süreç stdout borusunu açık tutar ve hiç çıktı üretmez.
        var okuyucu = new PdfMetinOkuyucu(Ayar("1"), log, (arac, argumanlar) =>
        {
            girdiler.Enqueue(argumanlar.Single(a => a.EndsWith(".pdf", StringComparison.Ordinal)));
            return Kabuk("ping -n 61 127.0.0.1 >nul", "sleep 60; true");
        });
        var sure = Stopwatch.StartNew();
        var cagrilar = Task.Run(async () =>
        {
            // İki eşzamanlı okuma slotu var: slot bırakılmasaydı üçüncü çağrı 429 alırdı.
            for (var i = 0; i < 3; i++)
            {
                var hata = await Assert.ThrowsAsync<PdfOkumaException>(() => okuyucu.OkuAsync(Pdf));
                Assert.Equal(422, hata.StatusCode);
                Assert.Contains("zaman sınırı", hata.Message);
            }
        });
        // Sahte araç 60 sn uyur: öldürülmeseydi ilk çağrı tek başına 60 sn sürerdi. Eşikler bundan belirgin biçimde kısa,
        // 3 × 1 sn'lik zaman sınırından ise yük altındaki makinede süreç başlatma gecikmesini kaldıracak kadar geniştir.
        Assert.Same(cagrilar, await Task.WhenAny(cagrilar, Task.Delay(TimeSpan.FromSeconds(45))));
        await cagrilar;
        Assert.True(sure.Elapsed < TimeSpan.FromSeconds(30), $"Üç çağrı {sure.Elapsed} sürdü.");
        Assert.Equal(3, girdiler.Count);
        Assert.All(girdiler, girdi => { Assert.False(File.Exists(girdi)); Assert.False(Directory.Exists(Path.GetDirectoryName(girdi))); });
        Assert.Equal(3, log.Uyarilar.Count(u => u.Contains("pdfinfo") && u.Contains("sonlandırıldı")));
    }

    // Zaman sınırı elle tetiklenir, gerçek saatle değil: eski sürümde alt süreç 2 sn sonra işaret dosyası yazıyor, sınır 1 sn'lik
    // gerçek zamanlayıcıyla doluyordu. Test ana makinesi uzun GC duraklamasındayken (bütün yönetilen iş parçacıkları durur, dış
    // süreçler çalışır) zamanlayıcı ve öldürme 2,6–8,7 sn gecikiyor, alt süreç işini öldürülmeden bitirip test "alt süreç çalışmaya
    // devam etti" diye düşüyordu; süreç kaçmıyordu. Şimdi sınır, alt süreç çalıştığı kesinleştikten sonra dolar ve alt sürecin
    // kendisinin öldüğü (kimliğiyle) denetlenir.
    [Fact]
    public async Task Zaman_asiminda_aracin_alt_surecleri_de_olur()
    {
        var kimlikDosyasi = Path.Combine(Path.GetTempPath(), "kasa-pdf-test-" + Guid.NewGuid().ToString("N") + ".pid");
        var saat = new ElleZamanSiniri();
        // Araç (kabuk) bir alt süreç başlatır; alt süreç kimliğini yazar ve 60 sn uyur. Yalnız araç öldürülseydi alt süreç yaşardı.
        var okuyucu = new PdfMetinOkuyucu(Ayar("1"), null, (_, _) => Kabuk(
            $"powershell -NoProfile -NonInteractive -Command \"Set-Content -LiteralPath '{kimlikDosyasi}' -Value $PID; Start-Sleep -Seconds 60\"",
            $"sleep 60 & echo $! > '{kimlikDosyasi}'; wait"), saat);
        var okuma = okuyucu.OkuAsync(Pdf);
        Process? altSurec = null;
        try
        {
            altSurec = await AltSureciBekle(kimlikDosyasi, okuma);
            Assert.Equal(1, saat.KurulanSayisi);
            saat.Tetikle();
            var hata = await Assert.ThrowsAsync<PdfOkumaException>(() => okuma);
            Assert.Equal(422, hata.StatusCode);
            Assert.Contains("zaman sınırı", hata.Message);
            // Öldürme eşzamansız tamamlanır (TerminateProcess/SIGKILL); süre yalnız askıda kalmayı önleyen üst sınırdır.
            Assert.True(altSurec.WaitForExit(TimeSpan.FromSeconds(10)), "Zaman aşımından sonra aracın alt süreci çalışmaya devam etti.");
        }
        finally
        {
            saat.Tetikle();
            if (altSurec is { HasExited: false })
                altSurec.Kill();
            altSurec?.Dispose();
            File.Delete(kimlikDosyasi);
        }
    }

    // İptal kaydı, belirteç araç başlarken dolmuşsa aracı hemen öldürür. WaitForExitAsync çıkmış süreçte iptali denetlemediği için
    // öldürülen araç eskiden "çıkış kodu sıfır değil" yolundan "PDF okunamadı (şifreli/bozuk)" diye bildiriliyordu; istek iptali de
    // iptal yerine 422 dönüyordu. Yük altında (test ana makinesinin uzun duraklamalarında) 1 sn'lik gerçek sınırla da oluyordu.
    [Fact]
    public async Task Zaman_siniri_arac_baslarken_dolsa_da_zaman_asimi_olarak_bildirilir()
    {
        var saat = new ElleZamanSiniri();
        var log = new LogToplayici();
        var okuyucu = new PdfMetinOkuyucu(Ayar("1"), log, (_, _) =>
        {
            saat.Tetikle();
            return Kabuk("ping -n 61 127.0.0.1 >nul", "sleep 60; true");
        }, saat);
        var hata = await Assert.ThrowsAsync<PdfOkumaException>(() => okuyucu.OkuAsync(Pdf));
        Assert.Equal(422, hata.StatusCode);
        Assert.Contains("zaman sınırı", hata.Message);
        Assert.Contains(log.Uyarilar, u => u.Contains("pdfinfo") && u.Contains("zaman sınırı veya istek iptali"));
    }

    [Fact]
    public async Task Istek_arac_baslarken_iptal_edilirse_iptal_olarak_doner()
    {
        using var iptal = new CancellationTokenSource();
        var okuyucu = new PdfMetinOkuyucu(Ayar("20"), null, (_, _) =>
        {
            iptal.Cancel();
            return Kabuk("ping -n 61 127.0.0.1 >nul", "sleep 60; true");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => okuyucu.OkuAsync(Pdf, iptal.Token));
    }

    /// <summary>Alt sürecin yazdığı kimlikten süreç nesnesi; alt süreç o an çalışıyordur. Araç bu arada biterse test hemen düşer.</summary>
    private static async Task<Process> AltSureciBekle(string kimlikDosyasi, Task okuma)
    {
        // Üst sınır yalnız askıda kalmayı önler: zaman sınırı bu bekleme bitmeden dolmaz, yük altında yavaş açılan powershell yarışmaz.
        var sure = Stopwatch.StartNew();
        while (sure.Elapsed < TimeSpan.FromSeconds(60))
        {
            Assert.False(okuma.IsCompleted, "Araç, zaman sınırı dolmadan bitti.");
            try
            {
                if (int.TryParse(File.ReadAllText(kimlikDosyasi).Trim(), out var kimlik))
                {
                    var surec = Process.GetProcessById(kimlik);
                    if (OperatingSystem.IsWindows())
                        _ = surec.SafeHandle; // tanıtıcı süreç canlıyken açılır: bekleme kimliği yeniden kullanılan başka sürece kaymaz
                    return surec;
                }
            }
            catch (IOException) { } // dosya henüz yok ya da yazılıyor
            await Task.Delay(50);
        }
        throw new TimeoutException("Aracın alt süreci 60 sn içinde başlamadı.");
    }

    /// <summary>Elle dolan zaman sınırı: okuyucunun kurduğu zamanlayıcılar yalnız <see cref="Tetikle"/> ile çalışır.</summary>
    private sealed class ElleZamanSiniri : TimeProvider
    {
        private readonly ConcurrentQueue<Zamanlayici> _kurulan = new();
        public int KurulanSayisi => _kurulan.Count;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var zamanlayici = new Zamanlayici(callback, state);
            _kurulan.Enqueue(zamanlayici);
            return zamanlayici;
        }

        public void Tetikle()
        {
            foreach (var zamanlayici in _kurulan)
                zamanlayici.Calistir();
        }

        private sealed class Zamanlayici(TimerCallback geriCagri, object? durum) : ITimer
        {
            private int _bitti;
            public void Calistir()
            {
                if (Interlocked.Exchange(ref _bitti, 1) == 0)
                    geriCagri(durum);
            }
            public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref _bitti) == 0;
            public void Dispose() => Interlocked.Exchange(ref _bitti, 1);
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    [Fact]
    public async Task Basarisiz_aracin_stderr_ozeti_uyari_olarak_loglanir_pdf_metni_loglanmaz()
    {
        var log = new LogToplayici();
        var okuyucu = new PdfMetinOkuyucu(Ayar(null), log, (_, _) => Kabuk(
            "echo GIZLI-PDF-METNI& echo Syntax Error: xref bozuk 1>&2& exit /b 3",
            "echo GIZLI-PDF-METNI; echo 'Syntax Error: xref bozuk' >&2; exit 3"));
        var hata = await Assert.ThrowsAsync<PdfOkumaException>(() => okuyucu.OkuAsync(Pdf));
        Assert.Equal(422, hata.StatusCode);
        Assert.Contains("PDF okunamadı", hata.Message);
        Assert.DoesNotContain("Syntax", hata.Message);
        var uyari = Assert.Single(log.Uyarilar);
        Assert.Contains("pdfinfo", uyari);
        Assert.Contains("3", uyari);
        Assert.Contains("Syntax Error: xref bozuk", uyari);
        Assert.DoesNotContain("GIZLI", uyari);
    }

    [Fact]
    public async Task Cok_stderr_yazan_arac_kilitlenmez_ve_okunan_metin_doner()
    {
        // stderr sınırı aşılınca boru boşaltılmaya devam eder; dolan boru aracı bekletmez. Zaman sınırı hiç dolmaz (elle): yük
        // altında yavaşlayan araç sınıra takılıp yanlış düşmez; kilitlenme varsa 60 sn'lik bekleme sınırı testi düşürür.
        var satir = new string('x', 60);
        var saat = new ElleZamanSiniri();
        var okuyucu = new PdfMetinOkuyucu(Ayar("20"), null, (arac, _) => arac == "pdfinfo"
            ? Kabuk($"echo Pages: 1& for /l %i in (1,1,1500) do @echo {satir} 1>&2", $"echo 'Pages: 1'; yes {satir} | head -n 3000 >&2")
            : Basarili(arac), saat);
        try
        {
            var metin = await okuyucu.OkuAsync(Pdf).WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Contains("01.09.2026 Test 10,00 TL", metin);
        }
        finally { saat.Tetikle(); } // bekleme sınırı dolduysa askıdaki aracı sonlandırır; bitmiş okumada etkisizdir
    }

    [Theory]
    [InlineData(null, 25)]
    [InlineData("", 25)]
    [InlineData("  ", 25)]
    [InlineData("0", 25)]
    [InlineData("-3", 25)]
    [InlineData("abc", 25)]
    [InlineData("1.5", 25)]
    [InlineData("121", 25)]
    [InlineData("500", 25)]
    [InlineData("1", 1)]
    [InlineData("40", 40)]
    [InlineData("120", 120)]
    public void Zaman_siniri_1_120_sn_arasindadir_gecersiz_deger_varsayilan_25_sn(string? deger, int saniye)
    {
        // Sınır dışı değer 120 sn'ye kırpılmaz, varsayılana döner: yanlış ayar okuma slotunu uzun süre tutmamalı.
        Assert.Equal(TimeSpan.FromSeconds(saniye), PdfMetinOkuyucu.ZamanSiniri(Ayar(deger)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("500")]
    public async Task Gecersiz_zaman_siniri_ile_okuma_calisir(string deger)
    {
        var okuyucu = new PdfMetinOkuyucu(Ayar(deger), null, (arac, _) => Basarili(arac));
        Assert.Contains("01.09.2026 Test 10,00 TL", await okuyucu.OkuAsync(Pdf));
    }

    private sealed class LogToplayici : ILogger<PdfMetinOkuyucu>
    {
        public ConcurrentQueue<string> Uyarilar { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (logLevel == LogLevel.Warning) Uyarilar.Enqueue(formatter(state, exception)); }
    }
}
