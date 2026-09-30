using Kasa.Core;
using Kasa.Core.Kodlar;

namespace Kasa.Api.Servisler;

/// <summary>
/// Kasa hareket dökümü (gap-denetim-izi-gozlemlenebilirlik-3): panelle aynı yükten (<see cref="HesapServisi.Dokum"/>) genel kasayı
/// ve kanal bakiyelerini oluşturan bütün hareketler (<see cref="HesapMotoru.KasaHareketleri"/>). Satırların etki tarihi en geç
/// <see cref="Bugun"/>'dür; açılış + bütün satırlar panelin bugünkü bakiyesidir. Bir gün itibarıyla bakiye, o gün hesaplanan panelin
/// bugünkü veriyle yeniden hesabıdır: o günden sonra girilen, silinen ya da düzeltilen geçmiş tarihli kayıtlar onu değiştirir
/// (kasa kontrolünün "sonradan değişti" işareti).
/// </summary>
public sealed class KasaDokumu(DateOnly bugun, decimal kasaAcilis, IReadOnlyList<Kanal> kanallar, IReadOnlyDictionary<string, int?> kanalIdleri,
    IReadOnlyList<KasaHareketi> hareketler)
{
    private readonly Dictionary<int, Kanal> _kanallar = kanallar.Where(k => kanalIdleri.GetValueOrDefault(k.Ad) is not null)
        .ToDictionary(k => kanalIdleri[k.Ad]!.Value);

    /// <summary>Hesabın günü (panelle aynı).</summary>
    public DateOnly Bugun { get; } = bugun;
    public decimal KasaAcilis { get; } = kasaAcilis;
    /// <summary>Etki tarihine göre sıralı satırlar.</summary>
    public IReadOnlyList<KasaHareketi> Hareketler { get; } = hareketler;

    /// <summary>Günün sonunda genel kasa: açılış + etki tarihi o gün ya da öncesi olan satırlar.</summary>
    public decimal GenelKasa(DateOnly gun) => KasaAcilis + Hareketler.Where(h => h.EtkiTarihi <= gun).Sum(h => h.GenelKasaEtkisi);
    /// <summary>Günün başında genel kasa (etki tarihi günden önce olan satırlar).</summary>
    public decimal GenelKasaOnce(DateOnly gun) => KasaAcilis + Hareketler.Where(h => h.EtkiTarihi < gun).Sum(h => h.GenelKasaEtkisi);

    /// <summary>Kanalın bugünkü adı; kanal yoksa (silinmiş) null.</summary>
    public string? KanalAdi(int kanalId) => _kanallar.TryGetValue(kanalId, out var k) ? k.Ad : null;
    /// <summary>Günün sonunda kanal bakiyesi (panelin kanal kasası kuralı); kanal yoksa null.</summary>
    public decimal? KanalBakiyesi(int kanalId, DateOnly gun) => Kanal(kanalId, h => h.EtkiTarihi <= gun);
    /// <summary>Günün başında kanal bakiyesi; kanal yoksa null.</summary>
    public decimal? KanalBakiyesiOnce(int kanalId, DateOnly gun) => Kanal(kanalId, h => h.EtkiTarihi < gun);
    private decimal? Kanal(int kanalId, Func<KasaHareketi, bool> kosul) => _kanallar.TryGetValue(kanalId, out var k)
        ? k.AcilisDevri + Hareketler.Where(h => h.Kanal == k.Ad && kosul(h)).Sum(h => h.KanalEtkisi) : null;

    /// <summary>Satırın API görünümü: türü, açıklaması, gösterilen kanal etiketi ve kimliği.</summary>
    public KasaHareketiDto Dto(KasaHareketi h)
    {
        var kanal = h.Kanal == KrediTuretici.KrediKanal ? KanalEtiketleri.GenelKasa : h.Kanal;
        var tur = Tur(h);
        return new(h.EtkiTarihi, h.KayitTarihi, tur, Aciklama(h, tur), kanal, kanalIdleri.GetValueOrDefault(h.Kanal), h.GenelKasaEtkisi, h.KanalEtkisi,
            h.KaynakAnahtari, Otomatik(tur));
    }

    /// <summary>Satır türü: Gelir (dönem geliri), EkstreGeliri, EkGelir, KrediCekimi; Gider, SabitGider, AylikGider, KartOdemesi,
    /// KartIadesi (önceden sayılan kart borcunun kasaya dönüşü), KrediTaksidi, KartAySonu (eski kartın ay sonu düşümü).</summary>
    public static string Tur(KasaHareketi h)
    {
        var anahtar = h.KaynakAnahtari ?? "";
        if (h.Gelen is not null)
            return anahtar.StartsWith("Kredi:", StringComparison.Ordinal) ? "KrediCekimi"
                : anahtar.StartsWith("EkstreKayit:", StringComparison.Ordinal) ? "EkstreGeliri"
                : anahtar.StartsWith("HesapHareket:", StringComparison.Ordinal) ? "EkGelir" : "Gelir";
        var i = h.Islem!;
        if (h.KartAySonu)
            return "KartAySonu";
        if (anahtar.StartsWith("Kredi:", StringComparison.Ordinal) || anahtar.StartsWith("TakipKrediTaksit:", StringComparison.Ordinal))
            return "KrediTaksidi";
        if (anahtar.StartsWith("TakipHarcama:", StringComparison.Ordinal))
            return "KartIadesi";
        if (i.NakitKartOdemesi)
            return "KartOdemesi";
        if (i.AylikGider)
            return "AylikGider";
        return i.Tip == GiderTipi.SabitGider ? "SabitGider" : "Gider";
    }

    /// <summary>Yazma olmadan, tarihi gelince kendiliğinden işleyen etki: kredi taksidi ve eski kartın ay sonu düşümü.</summary>
    public static bool Otomatik(string tur) => tur is "KrediTaksidi" or "KartAySonu";

    /// <summary>Satırı üreten genel giderin (Islemler) kimliği; başka kaynaklı satırda null.</summary>
    public static int? IslemId(KasaHareketi h) =>
        h.Islem?.KaynakAnahtari is { } a && a.StartsWith("Islem:", StringComparison.Ordinal) && int.TryParse(a.AsSpan(6), out var id) ? id : null;

    private static string Aciklama(KasaHareketi h, string tur)
    {
        if (h.Gelen is { } g)
            return g.Aciklama is { Length: > 0 } a ? a : tur switch { "KrediCekimi" => "Kredi çekimi", "EkGelir" => "Ek gelir", "EkstreGeliri" => "Ekstre geliri", _ => "Dönem geliri" };
        var i = h.Islem!;
        return string.IsNullOrWhiteSpace(i.Not) || i.Not == i.Cari ? i.Cari : $"{i.Cari} · {i.Not}";
    }
}
