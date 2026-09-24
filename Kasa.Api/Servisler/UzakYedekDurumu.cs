using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kasa.Api.Servisler;

/// <summary>
/// Sunucu dışı yedeğin (deploy/uzak-yedek, kasa-yedek konteyneri) yazdığı durum dosyasını
/// okur; /health bunu <c>uzakYedek</c> alanında gösterir. Dosya yoksa "yapılandırılmadı".
/// /health kimlik istemediği için rclone'un ham hata çıktısı (uzak adres, kullanıcı adı
/// içerebilir) buraya konmaz; yalnız betiğin kısa açıklaması gösterilir. Ayrıntı için
/// sunucuda <c>docker compose run --rm kasa-yedek durum</c>.
/// </summary>
public static class UzakYedekDurumu
{
    public const string Yapilandirilmadi = "yapılandırılmadı";
    public const string Tamam = "ok";
    public const string Eski = "eski";
    public const string Hatali = "hata";
    public const string Okunamadi = "okunamadı";

    /// <summary>Bu kadar saattir başarılı gönderim yoksa durum "eski" olur (varsayılan).</summary>
    public const double VarsayilanEskiSaat = 36;

    /// <summary>Durum dosyası bundan büyükse okunmaz (bozuk/yanlış dosya).</summary>
    private const long EnBuyukBoyut = 64 * 1024;

    /// <summary>/health'teki özet; boş alanlar yanıta yazılmaz.</summary>
    public sealed record Ozet(
        string Durum,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? SonBasari = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? SonBasariYasSaat = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? SonDeneme = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Dosya = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? BoyutBayt = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Hata = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Uyari = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DogrulamaZamani = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DogrulamaSonucu = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DogrulamaDosyasi = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DogrulamaHatasi = null);

    /// <summary>
    /// Durum dosyasını okuyup özetler. Hiçbir durumda istisna fırlatmaz.
    /// Durum: son deneme ya da son doğrulama hatalıysa "hata"; başarılı gönderim hiç yoksa ya da
    /// <paramref name="eskiSaat"/>'ten eskiyse "eski" (konteyner çalışmıyor olabilir); yoksa "ok".
    /// </summary>
    public static Ozet Oku(string? dosya, DateTimeOffset simdi, double eskiSaat = VarsayilanEskiSaat)
    {
        if (string.IsNullOrWhiteSpace(dosya)) return new Ozet(Yapilandirilmadi);
        JsonElement kok;
        try
        {
            var bilgi = new FileInfo(dosya);
            if (!bilgi.Exists) return new Ozet(Yapilandirilmadi);
            if (bilgi.Length > EnBuyukBoyut) return new Ozet(Okunamadi, Hata: "Durum dosyası beklenenden büyük.");
            using var belge = JsonDocument.Parse(File.ReadAllBytes(dosya));
            kok = belge.RootElement.Clone();
        }
        catch (Exception)
        {
            return new Ozet(Okunamadi, Hata: "Durum dosyası okunamadı.");
        }
        if (kok.ValueKind != JsonValueKind.Object) return new Ozet(Okunamadi, Hata: "Durum dosyası okunamadı.");

        var sonBasari = ZamanOku(kok, "sonBasari");
        var hata = MetinOku(kok, "hata");
        var dogrulamaSonucu = MetinOku(kok, "dogrulamaSonucu");
        double? yas = sonBasari is { } b ? Math.Round((simdi - b).TotalHours, 1) : null;

        var durum = hata is not null || dogrulamaSonucu == Hatali ? Hatali
            : yas is null || yas > eskiSaat ? Eski
            : Tamam;

        return new Ozet(
            durum,
            SonBasari: sonBasari,
            SonBasariYasSaat: yas,
            SonDeneme: ZamanOku(kok, "sonDeneme"),
            Dosya: MetinOku(kok, "dosya"),
            BoyutBayt: kok.TryGetProperty("boyutBayt", out var boyut) && boyut.ValueKind == JsonValueKind.Number
                       && boyut.TryGetInt64(out var bayt) ? bayt : null,
            Hata: hata,
            Uyari: MetinOku(kok, "uyari"),
            DogrulamaZamani: ZamanOku(kok, "dogrulamaZamani"),
            DogrulamaSonucu: dogrulamaSonucu,
            DogrulamaDosyasi: MetinOku(kok, "dogrulamaDosyasi"),
            DogrulamaHatasi: MetinOku(kok, "dogrulamaHatasi"));
    }

    private static string? MetinOku(JsonElement kok, string ad)
        => kok.TryGetProperty(ad, out var d) && d.ValueKind == JsonValueKind.String && d.GetString() is { Length: > 0 } s
            ? Metin.Kisalt(s, 300)
            : null;

    private static DateTimeOffset? ZamanOku(JsonElement kok, string ad)
        => kok.TryGetProperty(ad, out var d) && d.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(d.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : null;
}
