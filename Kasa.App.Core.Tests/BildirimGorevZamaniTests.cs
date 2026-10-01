using System.Globalization;

namespace Kasa.App.Core.Tests;

/// <summary>Zamanlanmış görevin günlük saati sunucu bildirim saatinden 5 dakika sonradır; gece yarısını geçen saat aynı günlük
/// tetikleyicinin saatidir (23:58 → 00:03, tarih yok).</summary>
public class BildirimGorevZamaniTests
{
    [Theory]
    [InlineData(9, 0, "09:05")]
    [InlineData(23, 58, "00:03")]
    [InlineData(23, 55, "00:00")]
    [InlineData(0, 0, "00:05")]
    [InlineData(14, 57, "15:02")]
    public void Gorev_saati_bildirim_saatinden_bes_dakika_sonradir(int saat, int dakika, string beklenen)
        => Assert.Equal(beklenen, BildirimGorevZamani.Hesapla(saat, dakika).ToString("HH:mm", CultureInfo.InvariantCulture));
}
