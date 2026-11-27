namespace Kasa.App.Core;

/// <summary>Liste satırının tutar sütunu (docs/specs/2026-11-26-slop-temizligi.md §6): kasa etkisi yönüyle, gider eksi ve gelir artı.
/// Tutar sağa hizalı, işaretli ve renkli ayrı sütunda durur; satırın başlığı ya da özeti aynı tutarı yeniden yazmaz.</summary>
public interface ITutarliSatir
{
    decimal SatirTutari { get; }
}
