using System.Globalization;
using System.Text.Json;

namespace Kasa.Api.Data;

/// <summary>
/// "Geçmişe dönük" düzeltme: değişikliğin yapıldığı aydan ÖNCEKİ bir ayın rakamını değiştiren geçmiş
/// satırı. Ortaklar kapanmış bir ayın sessizce değiştiğini kaçırmasın diye Geçmiş'te ve Panel'de öne
/// çıkarılır. Kural geçmiş satırının kendisinden (tür, eski/yeni JSON, zaman) okunur; ayrı bir sütun
/// tutulmaz, bu yüzden eski satırlar için de geçerlidir.
/// <list type="bullet">
/// <item>Tarihli para kayıtları (işlem, gelen, kart ödemesi, çek): kaydın tarihi — eski ya da yeni hali —
/// değişikliğin ayından (Türkiye saati) önceki bir aydaysa. Çekte tarih, kasayı etkilediği işlem tarihidir
/// ve yalnız tahsil edildi / ödendi durumunda sayılır (portföy, ciro, iade kasaya dokunmaz).</item>
/// <item>Güncellemede ayrıca paraya dokunan bir alanın değişmesi gerekir (yalnız not ya da cari adı
/// düzeltmesi rakam değiştirmez).</item>
/// <item>Açılış devirleri (kasa açılış devri, takip başlangıcı, kanal açılış devri) değişince geçmiş tüm
/// ayların devri değişir: bu güncellemeler her zaman geçmişe dönüktür.</item>
/// <item>Kasa sayımı, kart tanımı, cari/kalem adları ve toplu ad değişimi özetleri geçmişe dönük sayılmaz.</item>
/// </list>
/// </summary>
public static class GecmiseDonukKurali
{
    private sealed record Kural(string TarihAlani, string[] ParaAlanlari, Func<JsonElement, bool>? KasayaEtkili = null);

    private static readonly Dictionary<string, Kural> TarihliTurler = new()
    {
        [GecmisTurleri.Islem] = new("tarih", ["tarih", "tutarTl", "kanal", "tip", "krediKartiId"]),
        [GecmisTurleri.Gelen] = new("donemStart", ["donemStart", "kanal", "tutarTl"]),
        [GecmisTurleri.KartOdemesi] = new("tarih", ["tarih", "tutar"]),
        [GecmisTurleri.Cek] = new("islemTarihi", ["yon", "tutar", "kanal", "durum", "islemTarihi"], CekKasayaEtkili),
    };

    private static readonly Dictionary<string, string[]> AcilisAlanlari = new()
    {
        [GecmisTurleri.Ayar] = ["kasaAcilisDevri", "takipBaslangic"],
        [GecmisTurleri.Kanal] = ["acilisDevri"],
    };

    /// <summary>Geçmişe dönük olabilecek türler (sorguyu daraltmak için).</summary>
    public static IReadOnlyList<string> IlgiliTurler { get; } = TarihliTurler.Keys.Concat(AcilisAlanlari.Keys).ToList();

    public static bool Mi(DegisiklikEntity d) => Mi(d.Tur, d.EskiJson, d.YeniJson, d.ZamanUtc);

    /// <param name="zamanUtc">Değişikliğin zamanı (UTC; türü belirtilmemişse UTC kabul edilir).</param>
    public static bool Mi(string tur, string? eskiJson, string? yeniJson, DateTime zamanUtc)
    {
        if (!Oku(eskiJson, out var eski) || !Oku(yeniJson, out var yeni)) return false;   // toplu özet (dizi) vb.

        if (AcilisAlanlari.TryGetValue(tur, out var acilis))
            return eski is { } e0 && yeni is { } y0 && acilis.Any(a => !Esit(e0, y0, a));

        if (!TarihliTurler.TryGetValue(tur, out var kural) || (eski is null && yeni is null)) return false;
        if (eski is { } e && yeni is { } y && kural.ParaAlanlari.All(a => Esit(e, y, a))) return false;

        var utc = zamanUtc.Kind == DateTimeKind.Local ? zamanUtc.ToUniversalTime() : DateTime.SpecifyKind(zamanUtc, DateTimeKind.Utc);
        var degisim = DateOnly.FromDateTime(Saat.Simdi(utc));
        var degisimAyi = degisim.Year * 12 + degisim.Month;
        return OncekiAyda(eski) || OncekiAyda(yeni);

        bool OncekiAyda(JsonElement? kayit)
            => kayit is { } k
               && (kural.KasayaEtkili?.Invoke(k) ?? true)
               && Tarih(k, kural.TarihAlani) is { } t
               && t.Year * 12 + t.Month < degisimAyi;
    }

    // Tahsil edilen / ödenen çek kasayı işlem tarihinde etkiler (Kasa.Core.CekKurali).
    private static bool CekKasayaEtkili(JsonElement cek)
        => cek.TryGetProperty("durum", out var d) && d.ValueKind == JsonValueKind.String
           && d.GetString() is "TahsilEdildi" or "Odendi";

    // null/boş → (true, null); nesne → (true, kopya); başka biçim (dizi, bozuk) → false.
    private static bool Oku(string? json, out JsonElement? sonuc)
    {
        sonuc = null;
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var belge = JsonDocument.Parse(json);
            if (belge.RootElement.ValueKind != JsonValueKind.Object) return false;
            sonuc = belge.RootElement.Clone();
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static DateOnly? Tarih(JsonElement kayit, string alan)
        => kayit.TryGetProperty(alan, out var v) && v.ValueKind == JsonValueKind.String
           && DateOnly.TryParseExact(v.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t : null;

    // Alan değeri aynı mı? Sayılar değerce (100 = 100.0), eksik alan null sayılır.
    private static bool Esit(JsonElement a, JsonElement b, string alan)
    {
        var va = a.TryGetProperty(alan, out var x) ? x : default;
        var vb = b.TryGetProperty(alan, out var y) ? y : default;
        var ka = va.ValueKind is JsonValueKind.Undefined ? JsonValueKind.Null : va.ValueKind;
        var kb = vb.ValueKind is JsonValueKind.Undefined ? JsonValueKind.Null : vb.ValueKind;
        if (ka != kb) return false;
        return ka switch
        {
            JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False => true,
            JsonValueKind.Number => va.TryGetDecimal(out var da) && vb.TryGetDecimal(out var db) ? da == db : va.GetRawText() == vb.GetRawText(),
            JsonValueKind.String => va.GetString() == vb.GetString(),
            _ => va.GetRawText() == vb.GetRawText(),
        };
    }
}
