namespace Kasa.App.Core;

/// <summary>Zamanlanmış görevin (BildirimGorevi) günlük saati: sunucunun bildirim saatinden (BildirimAyarDto.Saat/Dakika) 5 dakika
/// sonra. Sunucu bildirimleri o saatte üretir; görev onları hazır bulur. Gece yarısını geçen saat aynı günlük tetikleyicinin saatidir
/// (23:58 → 00:03); tarih yoktur.</summary>
public static class BildirimGorevZamani
{
    public static readonly TimeSpan Gecikme = TimeSpan.FromMinutes(5);

    public static TimeOnly Hesapla(int saat, int dakika) => new TimeOnly(saat, dakika).Add(Gecikme);
}
