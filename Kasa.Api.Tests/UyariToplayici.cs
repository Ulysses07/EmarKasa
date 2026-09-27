using Microsoft.Extensions.Logging;

namespace Kasa.Api.Tests;

/// <summary>Uygulamanın yazdığı uyarı loglarını test için toplar.</summary>
internal sealed class UyariToplayici : ILoggerProvider, ILogger
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Uyarilar { get; } = new();
    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    { if (logLevel == LogLevel.Warning) Uyarilar.Enqueue(formatter(state, exception)); }
    public void Dispose() { }
}
