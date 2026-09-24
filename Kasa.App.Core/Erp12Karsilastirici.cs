using System.Globalization;
using System.Text;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>ERP12 dosyasından okunan tediye satırı (tutar mutlak değer, kuruşa yuvarlı).</summary>
public sealed record Erp12Satir(int SatirNo, DateOnly Tarih, string Cari, decimal Tutar);

/// <summary>Eşleşen çift: gün farkı (kasa − ERP12) ve cari benzerliği (0–1).</summary>
public sealed record Erp12Eslesme(Erp12Satir Erp, IslemDto Islem, int GunFarki, double Benzerlik);

/// <summary>Eşleşmeyen kayıt + (varsa) en yakın aday açıklaması.</summary>
public sealed record Erp12Tekil<T>(T Kayit, string? Ipucu);

public sealed record Erp12Sonuc(
    IReadOnlyList<Erp12Eslesme> Eslesenler,
    IReadOnlyList<Erp12Tekil<Erp12Satir>> YalnizErp12,
    IReadOnlyList<Erp12Tekil<IslemDto>> YalnizKasa);

/// <summary>
/// ERP12 tediye listesiyle kasadaki işlemleri karşılaştırır (SALT OKUNUR; hiçbir şey yazmaz).
/// Bir ERP12 satırı ile bir işlem eşleşir, eğer:
/// <list type="bullet">
/// <item>tutarlar kuruşu kuruşuna aynıysa (mutlak değer, kuruşa yuvarlanmış),</item>
/// <item>tarihler en fazla <see cref="GunToleransi"/> gün farklıysa,</item>
/// <item>cari adları Türkçe normalleştirmeyle yeterince benziyorsa (<see cref="EnAzBenzerlik"/>).</item>
/// </list>
/// Her kayıt en fazla bir kez eşleşir; adaylar önce benzerliğe (yüksek), sonra gün farkına (küçük)
/// göre açgözlü seçilir. Eşleşmeyenler için en yakın aday "olası eşleşme" ipucu olarak yazılır.
/// </summary>
public static class Erp12Karsilastirici
{
    public const int GunToleransi = 3;
    public const double EnAzBenzerlik = 0.5;

    /// <summary>Şirket unvanlarında ayırt edici olmayan kelimeler.</summary>
    private static readonly HashSet<string> DolguKelimeler =
    [
        "ltd", "sti", "limited", "sirketi", "sirket", "as", "anonim", "a", "s", "san", "sanayi", "tic", "ticaret",
        "ve", "ith", "ihr", "ithalat", "ihracat", "paz", "pazarlama", "insaat", "co", "inc", "gmbh", "llc",
        "turizm", "hizmetleri", "hiz", "ltdsti", "tas", "ortakligi", "sube", "subesi",
    ];

    public static Erp12Sonuc Karsilastir(IReadOnlyList<Erp12Satir> erp, IReadOnlyList<IslemDto> islemler)
    {
        var adaylar = new List<(int E, int I, double Benzerlik, int Gun)>();
        for (int e = 0; e < erp.Count; e++)
            for (int i = 0; i < islemler.Count; i++)
            {
                var gun = GunFarki(erp[e].Tarih, islemler[i].Tarih);
                if (Math.Abs(gun) > GunToleransi || Kurus(erp[e].Tutar) != Kurus(islemler[i].TutarTl)) continue;
                var b = Benzerlik(erp[e].Cari, islemler[i].Cari);
                if (b >= EnAzBenzerlik) adaylar.Add((e, i, b, gun));
            }

        var erpKullanildi = new bool[erp.Count];
        var islemKullanildi = new bool[islemler.Count];
        var eslesenler = new List<Erp12Eslesme>();
        foreach (var a in adaylar.OrderByDescending(a => a.Benzerlik).ThenBy(a => Math.Abs(a.Gun)).ThenBy(a => a.E).ThenBy(a => a.I))
        {
            if (erpKullanildi[a.E] || islemKullanildi[a.I]) continue;
            erpKullanildi[a.E] = islemKullanildi[a.I] = true;
            eslesenler.Add(new Erp12Eslesme(erp[a.E], islemler[a.I], a.Gun, a.Benzerlik));
        }

        var kalanErp = Enumerable.Range(0, erp.Count).Where(e => !erpKullanildi[e]).ToList();
        var kalanIslem = Enumerable.Range(0, islemler.Count).Where(i => !islemKullanildi[i]).ToList();

        var yalnizErp = kalanErp
            .Select(e => new Erp12Tekil<Erp12Satir>(erp[e], Ipucu(erp[e], kalanIslem.Select(i => islemler[i]))))
            .OrderBy(x => x.Kayit.Tarih).ThenBy(x => x.Kayit.SatirNo).ToList();
        var yalnizKasa = kalanIslem
            .Select(i => new Erp12Tekil<IslemDto>(islemler[i], Ipucu(islemler[i], kalanErp.Select(e => erp[e]))))
            .OrderBy(x => x.Kayit.Tarih).ThenBy(x => x.Kayit.Id).ToList();
        return new Erp12Sonuc(eslesenler.OrderBy(x => x.Erp.Tarih).ThenBy(x => x.Erp.SatirNo).ToList(), yalnizErp, yalnizKasa);
    }

    /// <summary>Kasa tarihi − ERP12 tarihi (gün).</summary>
    private static int GunFarki(DateOnly erp, DateOnly kasa) => kasa.DayNumber - erp.DayNumber;

    private static decimal Kurus(decimal d) => decimal.Round(Math.Abs(d), 2, MidpointRounding.AwayFromZero);

    // ---------------------------------------------------------------- ipucu

    private static string? Ipucu(Erp12Satir e, IEnumerable<IslemDto> adaylar)
        => EnYakin(adaylar.Select(i => (Metin: $"{i.Cari} · {i.Tarih:dd.MM.yyyy} · {Bicim.Tl(i.TutarTl)} ₺", i.Cari, i.Tarih, i.TutarTl)),
            e.Cari, e.Tarih, e.Tutar, "kasada");

    private static string? Ipucu(IslemDto i, IEnumerable<Erp12Satir> adaylar)
        => EnYakin(adaylar.Select(e => (Metin: $"{e.Cari} · {e.Tarih:dd.MM.yyyy} · {Bicim.Tl(e.Tutar)} ₺ (satır {e.SatirNo})", e.Cari, e.Tarih, e.Tutar)),
            i.Cari, i.Tarih, i.TutarTl, "ERP12'de");

    /// <summary>
    /// Eşleşmeyi kıl payı kaçıran en yakın aday: (1) tutar ve tarih tutuyor, cari farklı yazılmış;
    /// (2) cari ve tutar tutuyor, tarih 31 güne kadar farklı; (3) cari ve tarih tutuyor, tutar %1'e kadar farklı.
    /// </summary>
    private static string? EnYakin(IEnumerable<(string Metin, string Cari, DateOnly Tarih, decimal Tutar)> adaylar,
        string cari, DateOnly tarih, decimal tutar, string yer)
    {
        var liste = adaylar.ToList();
        var ayniTutarTarih = liste.Where(a => Kurus(a.Tutar) == Kurus(tutar) && Math.Abs(a.Tarih.DayNumber - tarih.DayNumber) <= GunToleransi)
            .OrderByDescending(a => Benzerlik(cari, a.Cari)).FirstOrDefault();
        if (ayniTutarTarih.Metin is not null) return $"Olası: {yer} {ayniTutarTarih.Metin} (tutar ve tarih tutuyor, cari farklı yazılmış)";

        var ayniCariTutar = liste.Where(a => Kurus(a.Tutar) == Kurus(tutar) && Benzerlik(cari, a.Cari) >= EnAzBenzerlik
                                             && Math.Abs(a.Tarih.DayNumber - tarih.DayNumber) <= 31)
            .OrderBy(a => Math.Abs(a.Tarih.DayNumber - tarih.DayNumber)).FirstOrDefault();
        if (ayniCariTutar.Metin is not null)
            return $"Olası: {yer} {ayniCariTutar.Metin} (tarih {Math.Abs(ayniCariTutar.Tarih.DayNumber - tarih.DayNumber)} gün farklı)";

        var ayniCariTarih = liste.Where(a => Benzerlik(cari, a.Cari) >= EnAzBenzerlik
                                             && Math.Abs(a.Tarih.DayNumber - tarih.DayNumber) <= GunToleransi
                                             && Math.Abs(Kurus(a.Tutar) - Kurus(tutar)) <= Math.Max(0.01m, Kurus(tutar) * 0.01m))
            .OrderBy(a => Math.Abs(Kurus(a.Tutar) - Kurus(tutar))).FirstOrDefault();
        if (ayniCariTarih.Metin is not null)
            return $"Olası: {yer} {ayniCariTarih.Metin} (tutar {Bicim.Tl(Math.Abs(Kurus(ayniCariTarih.Tutar) - Kurus(tutar)))} ₺ farklı)";
        return null;
    }

    // ---------------------------------------------------------------- cari benzerliği

    /// <summary>
    /// Türkçe normalleştirilmiş cari benzerliği (0–1): büyük/küçük harf ve Türkçe karakter farkı
    /// (ç/c, ğ/g, ı/i, İ/i, ö/o, ş/s, ü/u), noktalama ve unvan ekleri (Ltd. Şti., A.Ş., San. Tic. …)
    /// yok sayılır. Kısa addaki kelimelerin uzun adda bulunma oranıdır; bir kelime diğerinin en az 3
    /// harflik başı ise ("mrk" değil, "migr" → "migros") bulunmuş sayılır. Normal hâlleri aynıysa 1.
    /// </summary>
    public static double Benzerlik(string? a, string? b)
    {
        var na = Normal(a);
        var nb = Normal(b);
        if (na.Length == 0 || nb.Length == 0) return 0;
        if (na == nb) return 1;
        var ta = Kelimeler(na);
        var tb = Kelimeler(nb);
        if (ta.Count == 0 || tb.Count == 0)
        {
            // Yalnız dolgu kelimelerinden oluşan ad ("A.Ş."): bitişik yazımı karşılaştır.
            return na.Replace(" ", "") == nb.Replace(" ", "") ? 1 : 0;
        }
        var (kisa, uzun) = ta.Count <= tb.Count ? (ta, tb) : (tb, ta);
        var bulunan = kisa.Count(k => uzun.Any(u => KelimeUyar(k, u)));
        var oran = (double)bulunan / kisa.Count;
        // Bitişik/ayrı yazım: "yilmazgida" ~ "yilmaz gida".
        if (oran < 1 && string.Concat(ta) == string.Concat(tb)) oran = 1;
        return oran;
    }

    private static bool KelimeUyar(string a, string b)
    {
        if (a == b) return true;
        var (kisa, uzun) = a.Length <= b.Length ? (a, b) : (b, a);
        return kisa.Length >= 3 && uzun.StartsWith(kisa, StringComparison.Ordinal);
    }

    private static List<string> Kelimeler(string normal)
        => normal.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(k => !DolguKelimeler.Contains(k)).Distinct().ToList();

    /// <summary>Küçük harf (Türkçe), Türkçe harfler sadeleşir, harf/rakam dışı her şey boşluk olur.</summary>
    public static string Normal(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var kucuk = s.ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        var sb = new StringBuilder(kucuk.Length);
        foreach (var c in kucuk.Normalize(NormalizationForm.FormC))
        {
            var d = c switch
            {
                'ç' => 'c', 'ğ' => 'g', 'ı' => 'i', 'ö' => 'o', 'ş' => 's', 'ü' => 'u',
                'â' => 'a', 'î' => 'i', 'û' => 'u',
                _ => c,
            };
            if (char.IsLetterOrDigit(d)) sb.Append(d);
            else if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        // "i̇" (i + birleşik nokta; bazı "İ" küçültmeleri) → i
        return sb.ToString().Replace("i̇", "i").Trim();
    }
}
