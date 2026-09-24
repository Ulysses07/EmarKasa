using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>
/// Para kuralı korunur: paket B'den ÖNCEKİ motorun (master) haftalık/aylık çıktılarının özeti
/// sabitlenmiştir. Kasa dökümü (Kalemler) eklenirken hiçbir mevcut rakam — haftalık kasa, kanal
/// devirleri, aylık kanal sonuçları — değişmemelidir; bu test aynı tohumlu girdilerle üretilen
/// JSON'un SHA-256 özetinin master'dakiyle birebir aynı olduğunu doğrular.
/// </summary>
public class MotorAltinCiktiTests
{
    private const int TohumSayisi = 60;

    private static string Ozet(Func<MotorGirdisi, object> cikti)
    {
        var sb = new StringBuilder();
        for (int t = 1; t <= TohumSayisi; t++)
        {
            var g = MotorVeriUretici.Uret(t);
            sb.Append(JsonSerializer.Serialize(cikti(g))).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    [Fact]
    public void Haftalik_cikti_master_ile_birebir_ayni()
    {
        var ozet = Ozet(g => g.Haftalik());
        Assert.Equal("B9CCC5336FA9A347DB5AA66D09530836D657AD7DE4E8F805342835F3C079B25A", ozet);
    }

    [Fact]
    public void Aylik_cikti_master_ile_birebir_ayni()
    {
        var ozet = Ozet(g => g.Aylar()
            .Select(a => HesapMotoru.AylikHesapla(a.Yil, a.Ay, g.Kanallar, g.Islemler, g.Gelenler, g.Donemler, g.Baslangic, g.Cekler))
            .Append(HesapMotoru.AylikHesapla(g.Baslangic.Year, g.Baslangic.Month, g.Kanallar, g.Islemler, g.Gelenler, g.Donemler, null, g.Cekler))
            .ToList());
        Assert.Equal("D0223A20968321A278691DEE0620DF94C959672612F26D0F5A97D82DE49A2F15", ozet);
    }
}
