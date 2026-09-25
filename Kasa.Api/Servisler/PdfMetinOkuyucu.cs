using System.Diagnostics;
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
public sealed class PdfMetinOkuyucu(IConfiguration configuration) : IPdfMetinOkuyucu
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
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
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
            // The path is generated here, contains only input.pdf and is never supplied by the user.
            try { File.Delete(Path.Combine(directory, "input.pdf")); Directory.Delete(directory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            slots.Release();
        }
    }
    private async Task<string> Execute(string name, string[] arguments, int maxChars, CancellationToken ct)
    {
        var toolsDirectory = configuration["Pdf:AracDizini"];
        var executable = string.IsNullOrWhiteSpace(toolsDirectory) ? name : Path.Combine(toolsDirectory, name + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.Environment["LC_ALL"] = "C";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new PdfOkumaException("PDF okuma hizmeti başlatılamadı.", 503); }
        catch (System.ComponentModel.Win32Exception) { throw new PdfOkumaException("Sunucudaki PDF okuma aracı kullanılamıyor.", 503); }
        try
        {
            var output = Read(process, process.StandardOutput, maxChars, ct);
            var errors = Read(process, process.StandardError, 16_384, ct);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(ct));
            if (process.ExitCode != 0) throw new PdfOkumaException("PDF okunamadı. Dosya şifreli, bozuk veya desteklenmeyen bir yapıda olabilir.");
            return await output;
        }
        finally
        {
            if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
        }
    }
    private static async Task<string> Read(Process process, StreamReader reader, int limit, CancellationToken ct)
    {
        var result = new StringBuilder(); var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), ct); if (count == 0) return result.ToString();
            if (result.Length + count > limit)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw new PdfOkumaException("PDF çok fazla metin içeriyor. Daha kısa tarih aralığıyla yeniden indirin.");
            }
            result.Append(buffer, 0, count);
        }
    }
}
