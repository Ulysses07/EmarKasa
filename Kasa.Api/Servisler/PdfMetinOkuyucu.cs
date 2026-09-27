using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kasa.Api.Servisler;

public interface IPdfMetinOkuyucu
{
    Task<string> OkuAsync(byte[] pdf, CancellationToken ct = default);
}
public sealed class PdfOkumaException(string message, int statusCode = 422) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Untrusted PDFs stay on the server and run in a bounded, separate Poppler process.</summary>
/// <remarks><paramref name="baslatici"/> yalnız testler içindir: araç adı ve argümanlarından çalıştırılacak süreci verir.
/// Boşsa <c>Pdf:AracDizini</c>'ndeki (yoksa PATH'teki) Poppler araçları kullanılır. Yönlendirme, kodlama ve ortam
/// ayarları her iki yolda da burada uygulanır.</remarks>
public sealed class PdfMetinOkuyucu(IConfiguration configuration, ILogger<PdfMetinOkuyucu>? logger = null,
    Func<string, IReadOnlyList<string>, ProcessStartInfo>? baslatici = null) : IPdfMetinOkuyucu
{
    private readonly SemaphoreSlim slots = new(2, 2);
    public async Task<string> OkuAsync(byte[] pdf, CancellationToken ct = default)
    {
        if (pdf.Length is 0 or > 10 * 1024 * 1024 || pdf.Length < 5 || !pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw new PdfOkumaException("En fazla 10 MB büyüklüğünde geçerli bir PDF seçin.", 400);
        if (!await slots.WaitAsync(0, ct)) throw new PdfOkumaException("Başka belgeler okunuyor. Kısa süre sonra tekrar deneyin.", 429);
        var directory = Path.Combine(Path.GetTempPath(), "kasa-pdf-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var input = Path.Combine(directory, "input.pdf");
            await File.WriteAllBytesAsync(input, pdf, ct);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(input, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ZamanSiniri(configuration));
            var info = await Execute("pdfinfo", ["-enc", "UTF-8", input], 32_768, timeout.Token);
            var pages = Regex.Match(info, @"(?m)^Pages:\s*(\d+)\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!pages.Success || !int.TryParse(pages.Groups[1].Value, out var count) || count is < 1 or > 50)
                throw new PdfOkumaException("PDF en fazla 50 sayfa olmalı.");
            if (Regex.IsMatch(info, @"(?m)^Encrypted:\s*yes", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                throw new PdfOkumaException("Şifreli PDF okunamıyor. Bankadan şifresiz PDF olarak yeniden indirin.");
            var text = await Execute("pdftotext", ["-f", "1", "-l", "50", "-layout", "-enc", "UTF-8", input, "-"], 1_000_000, timeout.Token);
            if (string.IsNullOrWhiteSpace(text)) throw new PdfOkumaException("PDF'de okunabilir metin bulunamadı. Fotoğraf veya tarama yerine bankadan indirilen metin içeren PDF'yi seçin.");
            return text;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new PdfOkumaException("PDF zaman sınırı içinde okunamadı. Daha kısa bir tarih aralığı veya daha küçük dosya deneyin."); }
        finally
        {
            // The path is generated here and is never supplied by the user. Execute araç süreçlerini sonlandırıp
            // beklediği için dosyalar artık açık değildir; dizin her yolda (hata, zaman aşımı, iptal) içeriğiyle silinir.
            try { Directory.Delete(directory, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { if (Directory.Exists(directory)) logger?.LogWarning("Geçici PDF dizini silinemedi ({Hata}): {Dizin}", e.GetType().Name, directory); }
            slots.Release();
        }
    }
    // Pdf:ZamanAsimiSaniye 1–120 sn olabilir; boş, geçersiz veya sınır dışı değer 120'ye kırpılmaz, varsayılan 25 sn'ye döner.
    public static TimeSpan ZamanSiniri(IConfiguration configuration) =>
        TimeSpan.FromSeconds(int.TryParse(configuration["Pdf:ZamanAsimiSaniye"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var saniye) && saniye is >= 1 and <= 120 ? saniye : 25);
    private ProcessStartInfo PopplerSureci(string name, IReadOnlyList<string> arguments)
    {
        var toolsDirectory = configuration["Pdf:AracDizini"];
        var start = new ProcessStartInfo(string.IsNullOrWhiteSpace(toolsDirectory) ? name : Path.Combine(toolsDirectory, name + (OperatingSystem.IsWindows() ? ".exe" : "")));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
    private async Task<string> Execute(string name, string[] arguments, int maxChars, CancellationToken ct)
    {
        var start = baslatici?.Invoke(name, arguments) ?? PopplerSureci(name, arguments);
        start.UseShellExecute = false; start.CreateNoWindow = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8; start.StandardErrorEncoding = Encoding.UTF8;
        start.Environment["LC_ALL"] = "C";
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new PdfOkumaException("PDF okuma hizmeti başlatılamadı.", 503); }
        catch (System.ComponentModel.Win32Exception) { throw new PdfOkumaException("Sunucudaki PDF okuma aracı kullanılamıyor.", 503); }
        // Zaman aşımı yalnız bir belirteçtir; senkron borudaki bekleyen okumayı iptal edemez. Süreç ağacı iptal anında
        // öldürülür: borular kapanır, okumalar biter, slot bırakılır. Kayıt süreçten önce çözülür (ters bildirim sırası).
        using var iptalKaydi = ct.Register(static p => Sonlandir((Process)p!), process);
        // stdout ve stderr ayrı görevlerle eşzamanlı boşaltılır; biri dolarsa araç yazamayıp kilitlenmez.
        var output = Read(process, process.StandardOutput, maxChars, sinirdaDurdur: true);
        var errors = Read(process, process.StandardError, 16_384, sinirdaDurdur: false);
        try
        {
            await process.WaitForExitAsync(ct);
            // Çıkan aracın boruyu devralmış bir alt süreci kalsa bile bekleme zaman sınırını aşmaz.
            await Task.WhenAll(output, errors).WaitAsync(ct);
        }
        catch (Exception e) when (e is OperationCanceledException or PdfOkumaException)
        {
            Sonlandir(process);
            logger?.LogWarning("PDF aracı {Arac} tamamlanmadan sonlandırıldı ({Neden}); stderr özeti: {Ozet}", name,
                e is PdfOkumaException ? "çıktı sınırı aşıldı" : "zaman sınırı veya istek iptali", await StderrOzeti(errors));
            throw;
        }
        finally
        {
            Sonlandir(process);
            // Öldürülen süreç biçilmeden geçici PDF'i tutabilir (Windows); dizin silinmeden önce kısa süre beklenir.
            try { process.WaitForExit(2000); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        }
        if (process.ExitCode != 0)
        {
            logger?.LogWarning("PDF aracı {Arac} {Kod} çıkış koduyla başarısız oldu; stderr özeti: {Ozet}", name, process.ExitCode, Ozet(await errors));
            throw new PdfOkumaException("PDF okunamadı. Dosya şifreli, bozuk veya desteklenmeyen bir yapıda olabilir.");
        }
        return await output;
    }
    // İptal geri çağrısından da (zamanlayıcı iş parçacığı) çağrılır: buradan istisna kaçarsa uygulama düşer.
    private static void Sonlandir(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or AggregateException) { }
    }
    // Belirteç verilmez: okuma, süreç çıkınca ya da öldürülünce boru kapandığında biter.
    private static async Task<string> Read(Process process, StreamReader reader, int limit, bool sinirdaDurdur)
    {
        var result = new StringBuilder(); var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory()); if (count == 0) return result.ToString();
            if (result.Length + count > limit)
            {
                // stderr yalnız günlük özeti içindir: fazlası atılır ama boru boşaltılmaya devam eder.
                if (!sinirdaDurdur) { result.Append(buffer, 0, limit - result.Length); continue; }
                Sonlandir(process);
                throw new PdfOkumaException("PDF çok fazla metin içeriyor. Daha kısa tarih aralığıyla yeniden indirin.");
            }
            result.Append(buffer, 0, count);
        }
    }
    private static async Task<string> StderrOzeti(Task<string> errors)
    {
        try { return Ozet(await errors.WaitAsync(TimeSpan.FromSeconds(2))); }
        catch (Exception e) when (e is TimeoutException or IOException or InvalidOperationException) { return "(okunamadı)"; }
    }
    // Günlüğe yalnız aracın kendi hata iletisinin kısa özeti yazılır; PDF'ten okunan metin (stdout) yazılmaz.
    private static string Ozet(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return "(boş)";
        var first = new string(lines[0].Where(c => !char.IsControl(c)).Take(200).ToArray());
        return lines.Length == 1 ? first : $"{first} (+{lines.Length - 1} satır)";
    }
}
