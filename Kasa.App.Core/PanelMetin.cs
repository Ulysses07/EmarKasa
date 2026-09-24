using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Panel kartlarının kısa metinleri (tutar işaretli, "₺" ile; tarih "14 Kasım").</summary>
public static class PanelMetin
{
    /// <summary>"12.500,00 ₺" / "−12.500,00 ₺" (gerçek eksi işareti).</summary>
    public static string Tutar(decimal n) => (n < 0 ? "−" : "") + Bicim.Tl(Math.Abs(n)) + " ₺";

    /// <summary>"+12.500,00 ₺" / "−12.500,00 ₺".</summary>
    public static string IsaretliTutar(decimal n) => (n < 0 ? "−" : "+") + Bicim.Tl(Math.Abs(n)) + " ₺";

    /// <summary>"14 Kasım"; başka yıldaysa "14 Kasım 2027".</summary>
    public static string Gun(DateOnly t, DateOnly bugun)
        => t.ToString(t.Year == bugun.Year ? "d MMMM" : "d MMMM yyyy", Kultur.Turkce);

    /// <summary>"bugün", "yarın", "dün" ya da <see cref="Gun"/>.</summary>
    public static string GoreliGun(DateOnly t, DateOnly bugun) => (t.DayNumber - bugun.DayNumber) switch
    {
        0 => "bugün",
        1 => "yarın",
        -1 => "dün",
        _ => Gun(t, bugun),
    };

    /// <summary>"14–20 Eylül" (ay farklıysa "29 Eylül – 5 Ekim").</summary>
    public static string Aralik(DateOnly bas, DateOnly son)
        => bas.Month == son.Month && bas.Year == son.Year
            ? $"{bas.Day}–{son.ToString("d MMMM", Kultur.Turkce)}"
            : $"{bas.ToString("d MMMM", Kultur.Turkce)} – {son.ToString("d MMMM", Kultur.Turkce)}";

    /// <summary>
    /// Çeklerin yöne göre ayrı toplamı: "tahsil edilecek +5.000,00 ₺ · ödenecek −3.000,00 ₺" (yalnız olan yön
    /// yazılır; çek yoksa boş). Alınan çek bize gelecek, verilen çek bizden çıkacak paradır: ikisi tek bir
    /// toplamda birleştirilmez (Bugün yapılacaklar'daki +/− işaretleriyle aynı).
    /// </summary>
    public static string CekYonToplamlari(IEnumerable<CekDto> cekler)
    {
        var l = cekler.ToList();
        var parcalar = new List<string>(2);
        if (l.Any(c => c.Yon == CekYonu.Alinan))
            parcalar.Add($"tahsil edilecek {IsaretliTutar(l.Where(c => c.Yon == CekYonu.Alinan).Sum(c => c.Tutar))}");
        if (l.Any(c => c.Yon == CekYonu.Verilen))
            parcalar.Add($"ödenecek {IsaretliTutar(-l.Where(c => c.Yon == CekYonu.Verilen).Sum(c => c.Tutar))}");
        return string.Join(" · ", parcalar);
    }

    /// <summary>
    /// Sayıdan sonra gelen iyelik eki (okunuşun son sesine göre): 1'i, 2'si, 3'ü, 6'sı, 9'u, 10'u,
    /// 40'ı, 100'ü, 1000'i … "12 değişiklik, 2'si", "Limitin %85'i".
    /// </summary>
    public static string SayiEki(int n)
    {
        n = Math.Abs(n);
        if (n == 0) return "ı";                                   // sıfır
        if (n % 10 is var birler && birler != 0)
            return birler switch { 1 => "i", 2 => "si", 3 => "ü", 4 => "ü", 5 => "i", 6 => "sı", 7 => "si", 8 => "i", _ => "u" };
        if (n % 100 / 10 is var onlar && onlar != 0)
            return onlar switch { 1 => "u", 2 => "si", 3 => "u", 4 => "ı", 5 => "si", 6 => "ı", 7 => "i", 8 => "i", _ => "ı" };
        if (n % 1000 != 0) return "ü";                            // yüz, iki yüz …
        return n % 1_000_000 != 0 ? "i" : "u";                    // bin · milyon
    }
}
