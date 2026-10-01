using System.Globalization;

namespace Kasa.App.Core;

public enum YoklamaDurumu { Basarili, SunucuyaUlasilamadi, SunucuHatasi, OturumGecersiz, YerelKayitHatasi }

/// <summary>Bir bakmanın sonucu; <see cref="Metin"/> Bildirimler ekranındaki durum satırıdır ("Son kontrol 14:05 · 2 yeni bildirim").
/// Zaman yerel saattir.</summary>
public sealed record YoklamaSonucu(DateTimeOffset Zaman, YoklamaDurumu Durum, int YeniSayisi)
{
    public string Metin => "Son kontrol " + Zaman.ToString("HH:mm", CultureInfo.InvariantCulture) + " · " + Durum switch
    {
        YoklamaDurumu.Basarili when YeniSayisi > 0 => $"{YeniSayisi} yeni bildirim",
        YoklamaDurumu.Basarili => "yeni bildirim yok",
        YoklamaDurumu.SunucuyaUlasilamadi => "sunucuya ulaşılamadı",
        YoklamaDurumu.SunucuHatasi => "sunucu yanıt veremedi",
        YoklamaDurumu.OturumGecersiz => "oturum geçersiz, yeniden giriş yapın",
        _ => "bu bilgisayardaki kayıt dosyası açılamadı",
    };
}
