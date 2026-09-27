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
        if (GecersizKarakterIletisi(deger) is { } ileti) Kontrol(false, alan, ileti);
    }

    /// <summary>
    /// <see cref="Metin"/>'in kontrol karakteri kuralı, ondan geçmeyen serbest metinler (gerekçe, not) için: hata iletisi
    /// ya da geçerliyse null. Konum kullanıcının gördüğü karakterle sayılır (emoji tek karakter).
    /// </summary>
    public static string? GecersizKarakterIletisi(string? metin)
        => metin is not null && GecersizKarakterKonumu(metin) is var konum and >= 0
            ? $"{new StringInfo(metin[..konum]).LengthInTextElements + 1}. karakterde görünmeyen bir kontrol karakteri ya da geçersiz bir karakter var. Metni yeniden yazın."
            : null;

    /// <summary>Kullanıcının girdiği kayıt tarihinin alt sınırı.</summary>
    public static readonly DateOnly EnErkenTarih = new(2000, 1, 1);

    /// <summary>
    /// Kullanıcının girdiği kayıt tarihinin üst sınırı: kasa gününün bir yıl sonrası (dahil). Raporların dönem ufku en
    /// geç kayda kadar uzar; yazım hatalı uzak bir tarih (9026) yüz binlerce dönem ürettirirdi (host-auth-4). İleriye
    /// dönük yapıların türetilmiş tarihleri (aylık gider planı satırları, kredi taksitleri) bu pencereden geçmez.
    /// </summary>
    public static DateOnly EnGecTarih(DateOnly bugun) => bugun.AddYears(1);

    public static bool GecerliKayitTarihi(DateOnly tarih, DateOnly bugun) => tarih >= EnErkenTarih && tarih <= EnGecTarih(bugun);

    public void Para(decimal deger, string alan, bool negatifOlabilir = false)
    {
        Kontrol(deger >= (negatifOlabilir ? -EnBuyukTutar : 0m) && deger <= EnBuyukTutar,
            alan, negatifOlabilir ? "Tutar izin verilen aralığın dışında." : "Tutar negatif olamaz veya izin verilen sınırı aşamaz.");
        Kontrol(decimal.Round(deger, 2) == deger, alan, "Tutar en fazla iki ondalık basamak içerebilir.");
    }

    /// <summary>Kayıt tarihi <see cref="EnErkenTarih"/> ile <see cref="EnGecTarih"/> arasında olmalı; yalnız istek
    /// içinde çağrılır (kasa günü <see cref="KasaSaati.Bugun"/>).</summary>
    public void Tarih(DateOnly tarih, string alan)
    {
        if (tarih == default) { Kontrol(false, alan, "Geçerli bir tarih seçin."); return; }
        var bugun = KasaSaati.Bugun;
        Kontrol(GecerliKayitTarihi(tarih, bugun), alan, $"Tarih {EnErkenTarih:dd.MM.yyyy} ile {EnGecTarih(bugun):dd.MM.yyyy} arasında olmalıdır.");
    }

    /// <summary>Rapor ve plan sorgularının yılı kayıt tarihi penceresinin yıllarıyla sınırlıdır: daha ötesinde kayıt
    /// olamaz, çok büyük yıl ise raporun dönem ufkunu yüz binlerce döneme uzatırdı (host-auth-4).</summary>
    public void Yil(int yil, string alan)
    {
        var son = EnGecTarih(KasaSaati.Bugun).Year;
        Kontrol(yil >= EnErkenTarih.Year && yil <= son, alan, $"Yıl {EnErkenTarih.Year} ile {son} arasında olmalıdır.");
    }

    /// <summary>Aylık rapor/plan sorgusunun yıl ve ayı; geçerliyse null, değilse alan bazlı 400.</summary>
    public static IResult? RaporAyi(int yil, int ay)
    {
        var v = new GirdiDogrulama();
        v.Yil(yil, "yil");
        v.Kontrol(ay is >= 1 and <= 12, "ay", "Ay 1 ile 12 arasında olmalıdır.");
        return v.Sonuc();
    }

    public KanalEntity? Kanal(KasaDbContext db, string? ad, bool ortakOlabilir = true)
    {
        var temiz = ad?.Trim();
        if (ortakOlabilir && temiz == Kanallar.Ortak) return null;
        // Seçim önce kayıtlı kanallarla eşleşir: metin kuralından önce kaydedilmiş (kontrol karakterli) bir kanal adı
        // gider/gelir girişini düşürmez. Metin kuralı yeni ad oluşturmada ve yeniden adlandırmada uygulanır.
        var kanal = temiz is null ? null
            : db.Kanallar.FirstOrDefault(k => k.Ad == temiz) ?? (ad != temiz ? db.Kanallar.FirstOrDefault(k => k.Ad == ad) : null);
        if (kanal is not null) return kanal;
        Metin(ad, "kanal");
        Kontrol(false, "kanal", "Kayıtlı bir kanal seçin.");
        return null;
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
