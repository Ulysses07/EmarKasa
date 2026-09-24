using System.Globalization;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Sayfalar arası gezinme (MAUI'de Shell.GoToAsync; testlerde çağrı kaydı).</summary>
public interface IGezinti
{
    Task GitAsync(string rota);
}

/// <summary>Paket B sayfalarının Shell rotaları ve sorgu biçimi (tarih: yyyy-MM-dd, değişmez kültür).</summary>
public static class Rotalar
{
    public const string Islemler = "//islemler";
    public const string Grafikler = "//grafikler";
    public const string CariOzeti = "//cariozeti";
    /// <summary>Yığına itilen (geri düğmeli) sayfalar.</summary>
    public const string KasaDokumu = "kasadokumu";
    public const string HedefButce = "hedefbutce";

    private const string TarihBicimi = "yyyy-MM-dd";

    public static string Tarih(DateOnly d) => d.ToString(TarihBicimi, CultureInfo.InvariantCulture);

    /// <summary>"Kasa neden değişti?" sayfası: verilen aralık (hafta ya da ay).</summary>
    public static string KasaDokumuRotasi(DateOnly baslangic, DateOnly bitis)
        => $"{KasaDokumu}?baslangic={Tarih(baslangic)}&bitis={Tarih(bitis)}";

    public static string HedefButceRotasi(int yil, int ay)
        => $"{HedefButce}?yil={yil.ToString(CultureInfo.InvariantCulture)}&ay={ay.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Shell sorgu değerini okur (MAUI IQueryAttributable değerleri URL kodlu gelebilir).</summary>
    public static string? Deger(IDictionary<string, object>? sorgu, string ad)
    {
        if (sorgu is null || !sorgu.TryGetValue(ad, out var o) || o is null) return null;
        var s = o.ToString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { s = Uri.UnescapeDataString(s); }
        catch (UriFormatException) { return null; }
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }

    public static DateOnly? TarihDegeri(IDictionary<string, object>? sorgu, string ad)
        => Deger(sorgu, ad) is { } s && DateOnly.TryParseExact(s, TarihBicimi, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    public static int? SayiDegeri(IDictionary<string, object>? sorgu, string ad)
        => Deger(sorgu, ad) is { } s && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>Sorgudaki aralık (ikisi de geçerli ve bitiş ≥ başlangıç ise).</summary>
    public static (DateOnly Baslangic, DateOnly Bitis)? Aralik(IDictionary<string, object>? sorgu)
        => TarihDegeri(sorgu, "baslangic") is { } b && TarihDegeri(sorgu, "bitis") is { } s && s >= b ? (b, s) : null;
}

/// <summary>
/// Rapordan İşlemler'e iniş (drill-down) süzgeci: dönem + kanal (+ isteğe bağlı gider tipi).
/// Rota: <c>//islemler?baslangic=2026-08-01&amp;bitis=2026-08-31&amp;kanal=MEZAT&amp;tip=SabitGider</c>.
/// </summary>
public sealed record IslemSuzgeci(DateOnly Baslangic, DateOnly Bitis, string? Kanal = null, GiderTipi? Tip = null)
{
    /// <summary>Kanal adı üst sınırı (sorgudan gelen değer için).</summary>
    public const int EnUzunKanal = 100;

    public string Rota()
    {
        var q = $"baslangic={Rotalar.Tarih(Baslangic)}&bitis={Rotalar.Tarih(Bitis)}";
        if (!string.IsNullOrWhiteSpace(Kanal)) q += "&kanal=" + Uri.EscapeDataString(Kanal);
        if (Tip is { } t) q += "&tip=" + t;
        return $"{Rotalar.Islemler}?{q}";
    }

    /// <summary>Sorgudan süzgeç; tarih yoksa/bozuksa null (süzgeç uygulanmaz). Bilinmeyen tip yok sayılır.</summary>
    public static IslemSuzgeci? Coz(IDictionary<string, object>? sorgu)
    {
        if (Rotalar.Aralik(sorgu) is not { } a) return null;
        var kanal = Rotalar.Deger(sorgu, "kanal");
        if (kanal is { Length: > EnUzunKanal }) kanal = null;
        GiderTipi? tip = Rotalar.Deger(sorgu, "tip") is { } t
                         && Enum.TryParse<GiderTipi>(t, ignoreCase: false, out var g) && Enum.IsDefined(g) && !int.TryParse(t, out _)
            ? g : null;
        return new IslemSuzgeci(a.Baslangic, a.Bitis, kanal, tip);
    }

    /// <summary>Gider tipinin Türkçe adı (İşlemler çipleriyle aynı).</summary>
    public static string TipAdi(GiderTipi t) => t switch
    {
        GiderTipi.SabitGider => "Sabit gider",
        GiderTipi.KrediKarti => "Kredi kartı",
        _ => "Cari",
    };
}

/// <summary>Rapordaki dokunulabilir rakam: etiket, tutar ve İşlemler süzgeci (null = inilecek işlem yok).</summary>
public sealed record DrillRakam(string Etiket, decimal Tutar, IslemSuzgeci? Suzgec)
{
    public string TutarMetni => Bicim.Tl(Tutar);
    public bool Inilebilir => Suzgec is not null;
}
