using System.Globalization;
using System.Text.Json;
using Kasa.Core;

namespace Kasa.Api.Data;

/// <summary>
/// "Geçmişe dönük" düzeltme: değişikliğin yapıldığı aydan ÖNCEKİ bir ayın rakamını değiştiren geçmiş
/// satırı. Ortaklar kapanmış bir ayın sessizce değiştiğini kaçırmasın diye Geçmiş'te ve Panel'de öne
/// çıkarılır. Kural geçmiş satırının kendisinden (tür, eski/yeni JSON, zaman) okunur; ayrı bir sütun
/// tutulmaz, bu yüzden eski satırlar için de geçerlidir.
/// <list type="bullet">
/// <item>Tarihli para kayıtları (işlem, gelen, kart ödemesi, çek): kaydın rakamları etkilediği ay — eski ya da
/// yeni hali — değişikliğin ayından (Türkiye saati) önceki bir aydaysa. Bu ay çoğunlukla kaydın tarihinin
/// ayıdır; K.K harcamasında (karta bağlı ya da eski usul; <see cref="HesapMotoru.EtkinTip"/>) bir sonraki
/// aydır: aylık kârda ertesi ayın K.K'sı olarak, kasada ertesi ayın son döneminde ya da kartın ödendiği gün
/// düşer, kendi ayının rakamı değişmez. Böylece geçen ayın ekstresi bu ay girilince uyarı çıkmaz; iki ay
/// önceki bir K.K ise kapanmış (geçen) ayı değiştirdiği için geçmişe dönüktür. Çekte tarih, kasayı
/// etkilediği işlem tarihidir ve yalnız tahsil edildi / ödendi durumunda sayılır (portföy, ciro, iade kasaya
/// dokunmaz).</item>
/// <item>Güncellemede ayrıca paraya dokunan bir alanın değişmesi gerekir (yalnız not ya da cari adı
/// düzeltmesi rakam değiştirmez).</item>
/// <item>Açılış devirleri (kasa açılış devri, takip başlangıcı, kanal açılış devri) değişince geçmiş tüm
/// ayların devri değişir: bu güncellemeler her zaman geçmişe dönüktür.</item>
/// <item>Kasa sayımı, kart tanımı, cari/kalem adları ve toplu ad değişimi özetleri geçmişe dönük sayılmaz.</item>
/// </list>
/// Bilinen sınır: K.K harcaması kendi ayında kanalı "hareketli" yapar (<see cref="HesapMotoru.AylikHesapla"/>);
/// o ay başka hiç hareketi olmayan bir kanala geçen ay tarihli K.K girilirse geçen ayın Ortak pay dağılımı
/// değişebilir. Bu, satırdan (veritabanına bakmadan) anlaşılamaz ve işaretlenmez.
/// </summary>
public static class GecmiseDonukKurali
{
    /// <param name="AyKaymasi">Kaydın rakamları tarihinden kaç ay sonra etkilediği (K.K için 1).</param>
    private sealed record Kural(string TarihAlani, string[] ParaAlanlari, Func<JsonElement, bool>? KasayaEtkili = null,
        Func<JsonElement, int>? AyKaymasi = null);

    private static readonly Dictionary<string, Kural> TarihliTurler = new()
    {
        [GecmisTurleri.Islem] = new("tarih", ["tarih", "tutarTl", "kanal", "tip", "krediKartiId"], AyKaymasi: KkErtelemesi),
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
        var utc = zamanUtc.Kind == DateTimeKind.Local ? zamanUtc.ToUniversalTime() : DateTime.SpecifyKind(zamanUtc, DateTimeKind.Utc);
        return Mi(tur, eskiJson, yeniJson, DateOnly.FromDateTime(Saat.Simdi(utc)));
    }

    /// <summary>
    /// Aynı kural, değişikliğin gününe (Türkiye) göre. Kayıttan önce de sorulabilir: işlem uyarıları
    /// (<c>POST /api/islemler/uyarilar</c>, <c>EskiTarih</c>) kaydedilecek hali bununla değerlendirir, böylece
    /// kaydetmeden önceki uyarı ile Geçmiş'teki "Geçmişe dönük" işareti aynı kuraldan gelir.
    /// </summary>
    /// <param name="degisimGunu">Değişikliğin günü (Türkiye saatiyle).</param>
    public static bool Mi(string tur, string? eskiJson, string? yeniJson, DateOnly degisimGunu)
    {
        if (!Oku(eskiJson, out var eski) || !Oku(yeniJson, out var yeni)) return false;   // toplu özet (dizi) vb.

        if (AcilisAlanlari.TryGetValue(tur, out var acilis))
            return eski is { } e0 && yeni is { } y0 && acilis.Any(a => !Esit(e0, y0, a));

        if (!TarihliTurler.TryGetValue(tur, out var kural) || (eski is null && yeni is null)) return false;
        if (eski is { } e && yeni is { } y && kural.ParaAlanlari.All(a => Esit(e, y, a))) return false;

        var degisimAyi = degisimGunu.Year * 12 + degisimGunu.Month;
        return OncekiAyda(eski) || OncekiAyda(yeni);

        bool OncekiAyda(JsonElement? kayit)
            => kayit is { } k
               && (kural.KasayaEtkili?.Invoke(k) ?? true)
               && Tarih(k, kural.TarihAlani) is { } t
               && t.Year * 12 + t.Month + (kural.AyKaymasi?.Invoke(k) ?? 0) < degisimAyi;
    }

    // K.K harcaması (karta bağlı ya da tipi KrediKarti; HesapMotoru.EtkinTip) rakamları ertesi ay etkiler.
    private static int KkErtelemesi(JsonElement islem)
    {
        if (islem.TryGetProperty("krediKartiId", out var kart) && kart.ValueKind == JsonValueKind.Number) return 1;
        if (!islem.TryGetProperty("tip", out var tip)) return 0;
        return tip.ValueKind switch
        {
            JsonValueKind.String => tip.GetString() == nameof(GiderTipi.KrediKarti) ? 1 : 0,
            JsonValueKind.Number => tip.TryGetInt32(out var n) && n == (int)GiderTipi.KrediKarti ? 1 : 0,
            _ => 0,
        };
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
