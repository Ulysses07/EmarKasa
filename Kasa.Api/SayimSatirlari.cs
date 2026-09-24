using System.Text.Json;
using Kasa.Api.Data;

namespace Kasa.Api;

/// <summary>
/// Kasa sayımı satırları (nakit / banka / POS / diğer) ve nakit küpür sayacı. Satırlar sayımın
/// SatirlarJson sütununda saklanır; SayilanTutar her zaman satırların toplamıdır. Hiçbir kasa
/// hesabını değiştirmez.
/// </summary>
public static class SayimSatirlari
{
    public const int EnFazlaSatir = 20;

    /// <summary>Geçerli küpürler (kuruş): 200, 100, 50, 20, 10, 5 TL banknot; 1 TL, 50, 25, 10, 5 kuruş.</summary>
    public static readonly IReadOnlyList<int> Kupurler = [20000, 10000, 5000, 2000, 1000, 500, 100, 50, 25, 10, 5];

    /// <summary>Saklanan satırlar; boş/okunamayan JSON (eski tek tutarlı sayım) null.</summary>
    public static IReadOnlyList<SayimSatiriDto>? Oku(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<List<SayimSatiriDto>>(json, GecmisJson.Secenekler); }
        catch (JsonException) { return null; }
    }

    public static string? Yaz(IReadOnlyList<SayimSatiriDto>? satirlar)
        => satirlar is null || satirlar.Count == 0 ? null : JsonSerializer.Serialize(satirlar, GecmisJson.Secenekler);

    /// <summary>
    /// Satırları doğrular ve normalleştirir (ad kırpılır, sıfır adetli küpür atılır, küpürler büyükten
    /// küçüğe). Satır yoksa (null/boş) eski tip tek tutarlı sayımdır: normalleştirilmiş liste null döner.
    /// </summary>
    public static string? Hata(IReadOnlyList<SayimSatiriDto>? satirlar, decimal sayilanTutar, out IReadOnlyList<SayimSatiriDto>? normal)
    {
        normal = null;
        if (satirlar is null || satirlar.Count == 0) return null;
        if (satirlar.Count > EnFazlaSatir) return $"Sayım en fazla {EnFazlaSatir} satır olabilir.";
        var liste = new List<SayimSatiriDto>();
        foreach (var s in satirlar)
        {
            if (s is null) return "Boş sayım satırı olamaz.";
            if (!Enum.IsDefined(s.Tur)) return "Geçersiz sayım satırı türü.";
            var ad = s.Ad?.Trim() ?? "";
            if (ad.Length == 0) return "Her sayım satırının bir adı olmalı.";
            if (ad.Length > 100) return "Sayım satırı adı en fazla 100 karakter olabilir.";
            if (s.Tutar < 0) return $"'{Metin.Kisalt(ad)}' tutarı negatif olamaz.";
            if (s.Tutar > 100_000_000_000m) return $"'{Metin.Kisalt(ad)}' tutarı çok büyük.";
            if (decimal.Round(s.Tutar, 2) != s.Tutar) return $"'{Metin.Kisalt(ad)}' tutarı en fazla 2 ondalık basamak içerebilir.";
            List<KupurAdetDto>? kupurler = null;
            if (s.Kupurler is { Count: > 0 })
            {
                if (s.Tur != SayimSatirTuru.Nakit) return "Küpür sayımı yalnız nakit satırında yapılabilir.";
                if (s.Kupurler.Any(k => k is null)) return "Boş küpür satırı olamaz.";
                if (s.Kupurler.Select(k => k.Kurus).Distinct().Count() != s.Kupurler.Count) return "Aynı küpür iki kez yazılamaz.";
                foreach (var k in s.Kupurler)
                {
                    if (!Kupurler.Contains(k.Kurus)) return $"Geçersiz küpür: {k.Kurus} kuruş.";
                    if (k.Adet is < 0 or > 1_000_000) return "Küpür adedi 0 ile 1.000.000 arasında olmalı.";
                }
                kupurler = s.Kupurler.Where(k => k.Adet > 0).OrderByDescending(k => k.Kurus).ToList();
                var toplam = kupurler.Sum(k => (decimal)k.Kurus * k.Adet) / 100m;
                if (toplam != s.Tutar)
                    return $"'{Metin.Kisalt(ad)}' tutarı küpürlerin toplamına ({toplam.ToString("#,##0.00", Metin.Tr)} ₺) eşit olmalı.";
                if (kupurler.Count == 0) kupurler = null;
            }
            liste.Add(new SayimSatiriDto(s.Tur, ad, s.Tutar, kupurler));
        }
        var satirToplami = liste.Sum(s => s.Tutar);
        if (satirToplami != sayilanTutar)
            return $"Sayılan tutar satırların toplamına ({satirToplami.ToString("#,##0.00", Metin.Tr)} ₺) eşit olmalı.";
        normal = liste;
        return null;
    }
}
