using System.Text;
using System.Text.RegularExpressions;

namespace Kasa.Api.YapayZeka;

/// <summary>
/// Bir isteğin maskesi (docs/specs/2026-11-25-yapay-zeka-onerileri.md): sözlükte olmayan her sözcük numaralı yer tutucuya, rakam içeren her sözcük
/// '#'e dönüşür; aynı sözcük aynı istekte aynı numaradır. Yanıttaki yer tutucular yalnız o satırda geçen sözcüklere geri çevrilir.
/// </summary>
public sealed partial class YapayZekaMaskesi
{
    private readonly Dictionary<string, int> _numaralar = new(StringComparer.Ordinal);

    [GeneratedRegex(@"[\p{L}\p{M}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex SozcukDeseni();
    [GeneratedRegex(@"\[(\d{1,4})\]", RegexOptions.CultureInvariant)]
    private static partial Regex YerTutucuDeseni();
    [GeneratedRegex(@"^[\p{L}\p{M} .,&/'()-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AciklamaDeseni();

    /// <summary>Maskelenmiş metin ve metindeki yer tutucuların gerçek sözcükleri.</summary>
    public sealed record Maskeli(string Metin, IReadOnlyDictionary<int, string> Sozcukler);

    public Maskeli Maskele(string metin)
    {
        var ham = SozcukDeseni().Matches(metin).Select(m => m.Value).Select(h => (Ham: h, Norm: EkstreKuralServisi.Normalize(h))).Where(s => s.Norm.Length > 0).ToList();
        var acik = new bool[ham.Count];
        for (var i = 0; i < ham.Count; i++)
        {
            acik[i] |= YapayZekaSozlugu.Sozcukler.Contains(ham[i].Norm);
            if (i + 1 < ham.Count && YapayZekaSozlugu.Ikililer.Contains((ham[i].Norm, ham[i + 1].Norm)))
                acik[i] = acik[i + 1] = true;
        }
        var parcalar = new List<string>();
        var sozcukler = new Dictionary<int, string>();
        for (var i = 0; i < ham.Count; i++)
        {
            var (h, norm) = ham[i];
            if (acik[i])
                parcalar.Add(norm);
            else if (norm.Any(char.IsDigit))
            {
                if (parcalar.Count == 0 || parcalar[^1] != "#")
                    parcalar.Add("#");
            }
            // Tek harf (adın baş harfi olabilir) bilgi taşımaz.
            else if (norm.Length > 1)
            {
                if (!_numaralar.TryGetValue(norm, out var no))
                    _numaralar[norm] = no = _numaralar.Count + 1;
                sozcukler[no] = h;
                parcalar.Add($"[{no}]");
            }
        }
        return new(string.Join(' ', parcalar), sozcukler);
    }

    /// <summary>Modelin önerdiği açıklamayı gerçek sözcüklerle yazar. Satırda geçmeyen yer tutucu, rakam, beklenmeyen karakter ya da 60 karakterden
    /// uzun metin varsa null.</summary>
    public static string? GeriCevir(string? aciklama, Maskeli satir)
    {
        if (string.IsNullOrWhiteSpace(aciklama) || aciklama.Length > 120)
            return null;
        var gecersiz = false;
        var sonuc = YerTutucuDeseni().Replace(aciklama, m =>
        {
            if (satir.Sozcukler.TryGetValue(int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), out var sozcuk))
                return sozcuk;
            gecersiz = true;
            return "";
        });
        sonuc = GirdiDogrulama.Temizle(sonuc).Trim();
        return gecersiz || sonuc.Length is 0 or > 60 || !AciklamaDeseni().IsMatch(sonuc) ? null : sonuc;
    }

    /// <summary>Kesin tutar gönderilmez; yalnız büyüklük aralığı.</summary>
    internal static string TutarAraligi(decimal? tutar) => tutar switch
    {
        null => "?",
        < 100 => "0-100",
        < 1_000 => "100-1000",
        < 10_000 => "1000-10000",
        < 100_000 => "10000-100000",
        _ => "100000+"
    };
}
