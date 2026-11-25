using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core;

/// <summary>Toplu girişte üretilen ya da yapıştırılan çek (docs/specs/2026-11-25-cekler-verilen-ve-toplu.md). <paramref name="Kisi"/>
/// boşsa formun ortak kişisi geçer.</summary>
public readonly record struct CekTaslagi(string No, string Kisi, decimal Tutar, DateOnly Vade);

/// <summary>Toplu giriş formunun satırı. <see cref="Hata"/> satırın altında görünür, alan değişince kalkar.</summary>
public partial class CekTopluSatiri : ObservableObject
{
    [ObservableProperty][NotifyPropertyChangedFor(nameof(Baslik))] private int _sira;
    [ObservableProperty] private string _no = "";
    [ObservableProperty] private string _kisi = "";
    [ObservableProperty] private decimal _tutar;
    [ObservableProperty] private DateTime _vade;
    [ObservableProperty] private string? _hata;

    public string Baslik => $"{Sira}. satır";

    /// <summary>Satırın girdileri değişti (toplam ve aynı çek uyarısı yenilenir).</summary>
    public event EventHandler? Degisti;

    partial void OnNoChanged(string value) => Yenile();
    partial void OnKisiChanged(string value) => Yenile();
    partial void OnTutarChanged(decimal value) => Yenile();
    partial void OnVadeChanged(DateTime value) => Yenile();

    private void Yenile()
    {
        Hata = null;
        Degisti?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Seri çek üretimi: numaranın sonundaki rakamlar artar (baştaki sıfırlar korunur), vade verilen ay aralığıyla ilerler (ayın
/// son günü taşmaz: 31.01 → 28.02).</summary>
public static partial class CekSerisi
{
    [GeneratedRegex("[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SondakiRakamlar();

    public static IReadOnlyList<CekTaslagi> Uret(int adet, string ilkNo, string kisi, decimal tutar, DateOnly ilkVade, int aralikAy)
    {
        var no = ilkNo.Trim();
        var m = SondakiRakamlar().Match(no);
        if (adet > 1 && !m.Success)
            throw new DogrulamaHatasi("İlk numara rakamla bitmeli: seri onu birer artırır.");
        var bas = m.Success ? no[..m.Index] : no;
        var sayi = m.Success ? BigInteger.Parse(m.Value, CultureInfo.InvariantCulture) : BigInteger.Zero;
        return Enumerable.Range(0, adet).Select(i => new CekTaslagi(
            m.Success ? bas + (sayi + i).ToString(CultureInfo.InvariantCulture).PadLeft(m.Length, '0') : no,
            kisi.Trim(), tutar, ilkVade.AddMonths(i * aralikAy))).ToList();
    }
}

/// <summary>
/// Excel'den yapıştırılan çekler: sekmeyle (yoksa noktalı virgülle) ayrılmış Numara, Kişi, Tutar, Vade. İlk satır başlıksa sütunlar
/// başlığa göre bulunur; değilse sıra sabittir (üç sütunluysa Numara, Tutar, Vade). Tahmin yok: belirsiz tutar, okunamayan tarih ya da
/// tanınmayan başlık hatadır; hata varsa hiçbir satır dönmez (docs/specs/2026-11-25-cekler-verilen-ve-toplu.md).
/// </summary>
public static partial class CekYapistirma
{
    public sealed record Sonuc(IReadOnlyList<CekTaslagi> Satirlar, IReadOnlyList<string> Hatalar);

    private enum Sutun { No, Kisi, Tutar, Vade }

    private static readonly Dictionary<string, Sutun> Basliklar = new(StringComparer.Ordinal)
    {
        ["no"] = Sutun.No,
        ["numara"] = Sutun.No,
        ["cek no"] = Sutun.No,
        ["cek numarasi"] = Sutun.No,
        ["senet no"] = Sutun.No,
        ["senet numarasi"] = Sutun.No,
        ["cek / senet numarasi"] = Sutun.No,
        ["seri no"] = Sutun.No,
        ["kisi"] = Sutun.Kisi,
        ["kime"] = Sutun.Kisi,
        ["kimden"] = Sutun.Kisi,
        ["cari"] = Sutun.Kisi,
        ["firma"] = Sutun.Kisi,
        ["unvan"] = Sutun.Kisi,
        ["ad"] = Sutun.Kisi,
        ["lehtar"] = Sutun.Kisi,
        ["tutar"] = Sutun.Tutar,
        ["tutar (tl)"] = Sutun.Tutar,
        ["meblag"] = Sutun.Tutar,
        ["bedel"] = Sutun.Tutar,
        ["vade"] = Sutun.Vade,
        ["vade tarihi"] = Sutun.Vade,
        ["tarih"] = Sutun.Vade,
    };

    private static readonly string[] TarihBicimleri = ["d.M.yyyy", "d/M/yyyy", "d-M-yyyy", "yyyy-M-d"];

    // Türkçe Excel'in binlik ayırıcısı noktadır ("12.500" = 12500; kuruş en çok iki hanedir). Virgülden sonra üç hane ise belirsizdir.
    [GeneratedRegex(@"^[0-9]{1,3}(?:\.[0-9]{3})+(?:,[0-9]{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex NoktaBinlik();
    [GeneratedRegex(@"^[0-9]{1,3}(?:,[0-9]{3})+\.[0-9]{1,2}$|^[0-9]{1,3}(?:,[0-9]{3}){2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex VirgulBinlik();
    [GeneratedRegex(@"^[0-9]{1,3},[0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex BelirsizBinlik();

    /// <param name="ortakKisi">Formun ortak kişisi: kişi sütunu yoksa ya da hücresi boşsa o geçer; o da boşsa satır hatalıdır.</param>
    public static Sonuc Coz(string? metin, string ortakKisi)
    {
        var satirlar = (metin ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select((s, i) => (No: i + 1, Hucreler: Hucreler(s))).Where(s => s.Hucreler.Any(h => h.Length > 0)).ToList();
        if (satirlar.Count == 0)
            return new([], ["Yapıştırılacak satır yok."]);
        var hatalar = new List<string>();
        List<Sutun>? duzen = null;
        if (Baslik(satirlar[0].Hucreler))
        {
            duzen = [];
            foreach (var h in satirlar[0].Hucreler)
            {
                if (!Basliklar.TryGetValue(Anahtar(h), out var sutun))
                    return new([], [$"Satır {satirlar[0].No}: \"{h}\" sütunu tanınmadı. Numara, Kişi, Tutar ve Vade sütunları okunur; banka ve kasa üstteki alanlardan seçilir."]);
                if (duzen.Contains(sutun))
                    return new([], [$"Satır {satirlar[0].No}: \"{h}\" sütunu iki kez var."]);
                duzen.Add(sutun);
            }
            foreach (var (gerekli, ad) in new[] { (Sutun.No, "Numara"), (Sutun.Tutar, "Tutar"), (Sutun.Vade, "Vade") })
                if (!duzen.Contains(gerekli))
                    return new([], [$"Satır {satirlar[0].No}: {ad} sütunu yok."]);
            satirlar.RemoveAt(0);
            if (satirlar.Count == 0)
                return new([], ["Başlığın altında satır yok."]);
        }
        var sonuc = new List<CekTaslagi>();
        foreach (var (no, hucreler) in satirlar)
        {
            var sira = duzen ?? (hucreler.Count == 3 ? [Sutun.No, Sutun.Tutar, Sutun.Vade] : [Sutun.No, Sutun.Kisi, Sutun.Tutar, Sutun.Vade]);
            if (hucreler.Count > sira.Count || (duzen is null && hucreler.Count < sira.Count))
            {
                hatalar.Add($"Satır {no}: {sira.Count} sütun bekleniyor ({string.Join(", ", sira.Select(Ad))}), {hucreler.Count} sütun var.");
                continue;
            }
            string Hucre(Sutun s) => sira.IndexOf(s) is var i && i >= 0 && i < hucreler.Count ? hucreler[i] : "";
            if (Satir(Hucre(Sutun.No), Hucre(Sutun.Kisi), Hucre(Sutun.Tutar), Hucre(Sutun.Vade), ortakKisi, out var taslak) is { } hata)
                hatalar.Add($"Satır {no}: {hata}");
            else
                sonuc.Add(taslak);
        }
        return hatalar.Count > 0 ? new([], hatalar) : new(sonuc, []);
    }

    private static string? Satir(string no, string kisi, string tutar, string vade, string ortakKisi, out CekTaslagi taslak)
    {
        taslak = default;
        if (no.Length == 0)
            return "numara boş.";
        if (kisi.Length == 0 && string.IsNullOrWhiteSpace(ortakKisi))
            return "kişi boş (üstteki ortak kişiyi de doldurabilirsiniz).";
        if (TutarHatasi(tutar, out var para) is { } tutarHatasi)
            return tutarHatasi;
        if (!DateOnly.TryParseExact(vade, TarihBicimleri, CultureInfo.InvariantCulture, DateTimeStyles.None, out var gun))
            return $"vade okunamadı (\"{vade}\"); 31.01.2027 gibi yazın.";
        taslak = new(no, kisi, para, gun);
        return null;
    }

    private static string? TutarHatasi(string metin, out decimal tutar)
    {
        tutar = 0m;
        var sade = new StringBuilder(metin.Length);
        foreach (var c in metin)
            if (!char.IsWhiteSpace(c) && c != '₺')
                sade.Append(c);
        var t = sade.ToString();
        foreach (var ek in new[] { "TL", "TRY" })
            if (t.EndsWith(ek, StringComparison.OrdinalIgnoreCase))
                t = t[..^ek.Length];
        if (t.Length == 0)
            return "tutar boş.";
        if (BelirsizBinlik().IsMatch(t))
            return $"tutar belirsiz (\"{metin}\"): {t.Replace(",", "")} ise binlik ayırıcı kullanmadan yazın, kuruşluysa iki hane yazın.";
        t = NoktaBinlik().IsMatch(t) ? t.Replace(".", "") : VirgulBinlik().IsMatch(t) ? t.Replace(",", "") : t;
        if (!ParaAyristirici.Coz(t, out tutar, out _))
            return $"tutar okunamadı (\"{metin}\").";
        return tutar > 0 ? null : "tutar sıfırdan büyük olmalı.";
    }

    /// <summary>Satırın hücreleri; sondaki boş hücreler (Excel'in fazladan sütunları) düşer, tırnak kalkar.</summary>
    private static List<string> Hucreler(string satir)
    {
        var hucreler = satir.Split(satir.Contains('\t') ? '\t' : ';').Select(h => h.Trim().Trim('"').Trim()).ToList();
        while (hucreler.Count > 0 && hucreler[^1].Length == 0)
            hucreler.RemoveAt(hucreler.Count - 1);
        return hucreler;
    }

    /// <summary>Başlık satırı: en az iki hücresi bilinen sütun adıdır.</summary>
    private static bool Baslik(List<string> hucreler) => hucreler.Count(h => Basliklar.ContainsKey(Anahtar(h))) >= 2;

    private static string Ad(Sutun s) => s switch { Sutun.No => "Numara", Sutun.Kisi => "Kişi", Sutun.Tutar => "Tutar", _ => "Vade" };

    /// <summary>Başlığın karşılaştırma anahtarı: küçük harf, Türkçe harfler sadeleşir, çoklu boşluk teke iner.</summary>
    private static string Anahtar(string baslik) => string.Join(' ', baslik.ToLower(CultureInfo.GetCultureInfo("tr-TR"))
        .Replace('ı', 'i').Replace('ç', 'c').Replace('ş', 's').Replace('ğ', 'g').Replace('ü', 'u').Replace('ö', 'o')
        .Replace(".", "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
