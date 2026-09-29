using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Kasa.Api.Tests;

/// <summary>Uygulamanın yazdığı uyarı loglarını test için toplar.</summary>
internal sealed class UyariToplayici : ILoggerProvider, ILogger
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Uyarilar { get; } = new();
    // Veri koruma (Data Protection) anahtarı süreçte bir kez üretilip profil klasörüne yazılır; Linux'ta şifreleyici olmadığından
    // "No XML encryptor configured" uyarısı yalnız anahtarı İLK üreten fabrikanın logunda çıkar. Hangi testin ilk olduğu koşu
    // sırasına bağlı (Windows'ta DPAPI olduğu için hiç çıkmaz); uygulamanın kendi uyarısı olmadığından toplanmaz.
    public ILogger CreateLogger(string categoryName) =>
        categoryName.StartsWith("Microsoft.AspNetCore.DataProtection", StringComparison.Ordinal) ? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance : this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    { if (logLevel == LogLevel.Warning && !SorguDerlemeGurultusu(eventId)) Uyarilar.Enqueue(formatter(state, exception)); }
    public void Dispose() { }

    // EF'in derlenmiş sorgu önbelleği süreç genelinde paylaşılır: tek satırlı Ayarlar tablosundaki
    // sıralamasız First() uyarısı yalnız o sorguyu süreçte İLK derleyen fabrikanın logunda çıkar.
    // Hangi testin ilk olduğu paralel koşu sırasına bağlı olduğundan bu uyarı toplanmaz.
    private static bool SorguDerlemeGurultusu(EventId eventId) =>
        eventId.Id == CoreEventId.FirstWithoutOrderByAndFilterWarning.Id;
}
