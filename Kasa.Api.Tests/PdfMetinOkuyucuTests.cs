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
        var log = new LogToplayici(); var girdiler = new ConcurrentQueue<string>();
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
                Assert.Equal(422, hata.StatusCode); Assert.Contains("zaman sınırı", hata.Message);
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

    [Fact]
    public async Task Zaman_asiminda_aracin_alt_surecleri_de_olur()
    {
        var isaret = Path.Combine(Path.GetTempPath(), "kasa-pdf-test-" + Guid.NewGuid().ToString("N") + ".txt");
        // Alt süreç 2 sn sonra işaret dosyası yazar; yalnız üst süreç öldürülseydi dosya oluşurdu.
        var okuyucu = new PdfMetinOkuyucu(Ayar("1"), null, (_, _) => Kabuk(
            $"powershell -NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 2; Set-Content -LiteralPath '{isaret}' -Value x\"",
            $"(sleep 2; touch '{isaret}') & wait"));
        try
        {
            var hata = await Assert.ThrowsAsync<PdfOkumaException>(() => okuyucu.OkuAsync(Pdf));
            Assert.Contains("zaman sınırı", hata.Message);
            await Task.Delay(TimeSpan.FromSeconds(5));
            Assert.False(File.Exists(isaret), "Zaman aşımından sonra alt süreç çalışmaya devam etti.");
        }
        finally { File.Delete(isaret); }
    }

    [Fact]
    public async Task Basarisiz_aracin_stderr_ozeti_uyari_olarak_loglanir_pdf_metni_loglanmaz()
    {
        var log = new LogToplayici();
        var okuyucu = new PdfMetinOkuyucu(Ayar(null), log, (_, _) => Kabuk(
            "echo GIZLI-PDF-METNI& echo Syntax Error: xref bozuk 1>&2& exit /b 3",
            "echo GIZLI-PDF-METNI; echo 'Syntax Error: xref bozuk' >&2; exit 3"));
        var hata = await Assert.ThrowsAsync<PdfOkumaException>(() => okuyucu.OkuAsync(Pdf));
        Assert.Equal(422, hata.StatusCode); Assert.Contains("PDF okunamadı", hata.Message);
        Assert.DoesNotContain("Syntax", hata.Message);
        var uyari = Assert.Single(log.Uyarilar);
        Assert.Contains("pdfinfo", uyari); Assert.Contains("3", uyari); Assert.Contains("Syntax Error: xref bozuk", uyari);
        Assert.DoesNotContain("GIZLI", uyari);
    }

    [Fact]
    public async Task Cok_stderr_yazan_arac_kilitlenmez_ve_okunan_metin_doner()
    {
        // stderr sınırı aşılınca boru boşaltılmaya devam eder; dolan boru aracı bekletip zaman aşımına düşürmez.
        var satir = new string('x', 60);
        var okuyucu = new PdfMetinOkuyucu(Ayar("20"), null, (arac, _) => arac == "pdfinfo"
            ? Kabuk($"echo Pages: 1& for /l %i in (1,1,1500) do @echo {satir} 1>&2", $"echo 'Pages: 1'; yes {satir} | head -n 3000 >&2")
            : Basarili(arac));
        var metin = await okuyucu.OkuAsync(Pdf);
        Assert.Contains("01.09.2026 Test 10,00 TL", metin);
    }

    [Theory]
    [InlineData(null, 25)][InlineData("", 25)][InlineData("  ", 25)][InlineData("0", 25)][InlineData("-3", 25)][InlineData("abc", 25)]
    [InlineData("1.5", 25)][InlineData("121", 25)][InlineData("500", 25)][InlineData("1", 1)][InlineData("40", 40)][InlineData("120", 120)]
    public void Zaman_siniri_1_120_sn_arasindadir_gecersiz_deger_varsayilan_25_sn(string? deger, int saniye)
    {
        // Sınır dışı değer 120 sn'ye kırpılmaz, varsayılana döner: yanlış ayar okuma slotunu uzun süre tutmamalı.
        Assert.Equal(TimeSpan.FromSeconds(saniye), PdfMetinOkuyucu.ZamanSiniri(Ayar(deger)));
    }

    [Theory]
    [InlineData("0")][InlineData("abc")][InlineData("500")]
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
