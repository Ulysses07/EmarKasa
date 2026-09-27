using System.Globalization;
using System.Text;
using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api;

/// <summary>Alan hatalarını, kayıt üzerinde değişiklik yapmadan toplar.</summary>
public sealed class GirdiDogrulama
{
    private readonly Dictionary<string, string[]> _hatalar = new();
    private const decimal EnBuyukTutar = 999_999_999_999.99m;

    public void Kontrol(bool gecerli, string alan, string mesaj)
    {
        if (!gecerli) _hatalar[alan] = [mesaj];
    }

    public void Metin(string? deger, string alan, int sinir = 200, bool zorunlu = true)
    {
        Kontrol(!zorunlu || !string.IsNullOrWhiteSpace(deger), alan, "Bu alan boş olamaz.");
        Kontrol(deger is null || deger.Length <= sinir, alan, $"En fazla {sinir} karakter girilebilir.");
        // Kaydedilen metin dışa aktarımda (XLSX/XML) ve ekranlarda bozulmasın: görünmeyen karakter hiç saklanmaz.
        // Konum kullanıcının gördüğü karakterle sayılır (emoji tek karakter).
        if (deger is not null && GecersizKarakterKonumu(deger) is var konum and >= 0)
            Kontrol(false, alan, $"{new StringInfo(deger[..konum]).LengthInTextElements + 1}. karakterde görünmeyen bir kontrol karakteri ya da geçersiz bir karakter var. Metni yeniden yazın.");
    }

    public void Para(decimal deger, string alan, bool negatifOlabilir = false)
    {
        Kontrol(deger >= (negatifOlabilir ? -EnBuyukTutar : 0m) && deger <= EnBuyukTutar,
            alan, negatifOlabilir ? "Tutar izin verilen aralığın dışında." : "Tutar negatif olamaz veya izin verilen sınırı aşamaz.");
        Kontrol(decimal.Round(deger, 2) == deger, alan, "Tutar en fazla iki ondalık basamak içerebilir.");
    }

    public void Tarih(DateOnly tarih, string alan)
        => Kontrol(tarih != default && tarih.Year < 9999, alan, "Geçerli bir tarih seçin.");

    public KanalEntity? Kanal(KasaDbContext db, string? ad, bool ortakOlabilir = true)
    {
        Metin(ad, "kanal");
        var temiz = ad?.Trim();
        if (ortakOlabilir && temiz == Kanallar.Ortak) return null;
        var kanal = db.Kanallar.FirstOrDefault(k => k.Ad == temiz);
        Kontrol(kanal is not null, "kanal", "Kayıtlı bir kanal seçin.");
        return kanal;
    }

    public void Kart(KasaDbContext db, int? id, bool zorunlu = false)
    {
        Kontrol(id is null ? !zorunlu : db.KrediKartlari.Any(k => k.Id == id), "krediKartiId", "Kayıtlı bir kredi kartı seçin.");
        if (id is not null) Kontrol(!db.TakipKartlar.Any(k => k.KrediKartiId == id && !k.Aktif), "krediKartiId", "Bu kart yeni kullanıma kapalı.");
    }

    /// <summary>
    /// Metinde saklanmaması gereken ilk karakterin konumu, yoksa -1: \t, \r ve \n dışındaki kontrol karakterleri
    /// (C0, DEL, C1), U+FFFE/U+FFFF ve eşi olmayan vekiller. Bunlar XML 1.0'da (XLSX) geçersizdir ya da görünmez.
    /// Geçerli vekil çiftleri (emoji vb.) kabul edilir.
    /// </summary>
    public static int GecersizKarakterKonumu(string metin)
    {
        for (var i = 0; i < metin.Length; i++)
        {
            if (char.IsHighSurrogate(metin[i]) && i + 1 < metin.Length && char.IsLowSurrogate(metin[i + 1])) { i++; continue; }
            if (GecersizKarakter(metin[i])) return i;
        }
        return -1;
    }

    /// <summary>
    /// Kullanıcı girdisi olmayan metni (PDF'ten okunan açıklama) reddetmeden temizler: her geçersiz karakter
    /// (<see cref="GecersizKarakterKonumu"/>) boşluğa döner; komşusu zaten boşluksa eklenmez, böylece kelimeler
    /// birleşmez ve boşluk çoğalmaz. Geçerli metin aynen (aynı nesne) döner.
    /// </summary>
    public static string Temizle(string metin)
    {
        if (GecersizKarakterKonumu(metin) < 0) return metin;
        var temiz = new StringBuilder(metin.Length);
        for (var i = 0; i < metin.Length; i++)
        {
            var c = metin[i];
            if (char.IsHighSurrogate(c) && i + 1 < metin.Length && char.IsLowSurrogate(metin[i + 1])) { temiz.Append(c).Append(metin[++i]); continue; }
            if (!GecersizKarakter(c)) { temiz.Append(c); continue; }
            var oncekiBosluk = temiz.Length > 0 && char.IsWhiteSpace(temiz[^1]);
            var sonrakiBosluk = i + 1 < metin.Length && char.IsWhiteSpace(metin[i + 1]);
            if (!oncekiBosluk && !sonrakiBosluk) temiz.Append(' ');
        }
        return temiz.ToString();
    }

    // Tek başına değerlendirilen karakter: vekil buraya yalnız eşi yokken gelir.
    private static bool GecersizKarakter(char c) =>
        char.IsSurrogate(c) || c is '\uFFFE' or '\uFFFF' || char.IsControl(c) && c is not ('\t' or '\r' or '\n');

    public IResult? Sonuc() => _hatalar.Count == 0 ? null : Results.ValidationProblem(_hatalar);

    public static bool AyrilmisKanalAdi(string ad)
        => string.Equals(ad, Kanallar.Ortak, StringComparison.OrdinalIgnoreCase)
           || string.Equals(ad, Kanallar.DagilimBekliyor, StringComparison.OrdinalIgnoreCase)
           || string.Equals(ad, KrediTuretici.KrediKanal, StringComparison.OrdinalIgnoreCase);
}
