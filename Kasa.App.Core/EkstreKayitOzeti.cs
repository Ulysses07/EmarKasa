using System.Collections.ObjectModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Kayıt özetinin tablo satırı: solda başlık ve gri özet, sağda işaretli kasa etkisi (gelir artı, gider eksi, sıfır gri).</summary>
public sealed record EkstreOzetSatiri(string Baslik, string Ozet, decimal SatirTutari) : ITutarliSatir;

/// <summary>Kayıt özetinin uyarısı. Aynı uyarı birden çok satırda geçiyorsa bir kez yazılır, satır numaraları başlıktadır; satırsız
/// uyarı seçimin tümüne aittir.</summary>
public sealed record EkstreOzetUyarisi(string Ozet, IReadOnlyList<int> SatirNolar)
{
    private const int EnFazlaNumara = 5;
    public string Baslik => SatirNolar.Count == 0 ? "Tüm seçim"
        : "Satır " + string.Join(", ", SatirNolar.Take(EnFazlaNumara))
            + (SatirNolar.Count > EnFazlaNumara ? $" ve {SatirNolar.Count - EnFazlaNumara} satır daha" : "");
    /// <summary>"Satıra git"in götürdüğü satır (birden çoksa ilki); satırsız uyarıda null.</summary>
    public int? SatirNo => SatirNolar.Count > 0 ? SatirNolar[0] : null;
}

/// <summary>"Etkisini göster ve kaydet"in sonucu (docs/specs/2026-11-26-slop-temizligi.md §9): önce toplamlar, sonra kanal bazında kasa
/// etkisi, sonra varsa uyarılar. Satır ayrıntısı soldaki tabloda olduğundan özet satırları yeniden yazmaz.</summary>
public sealed record EkstreKayitOzeti(IReadOnlyList<EkstreOzetSatiri> Toplamlar, IReadOnlyList<EkstreOzetSatiri> Kanallar, IReadOnlyList<EkstreOzetUyarisi> Uyarilar)
{
    /// <summary>Kanala dağıtılmayan kasa etkisinin satırı (satır formundaki dağılım seçeneğiyle aynı ad).</summary>
    public const string GenelKasa = "Yalnız genel kasa";

    /// <param name="turler">Belgenin işlem türleri (satır formundaki sırayla): toplamlar bu sırayla yazılır.</param>
    /// <param name="kanallar">Etkin kanallar (Kanallar sayfasındaki sırayla): kanal etkisi bu sırayla yazılır.</param>
    public static EkstreKayitOzeti Kur(EkstreOnizlemeDto p, IReadOnlyList<EkstreSecenek> turler, IReadOnlyList<KanalDto> kanallar) =>
        new(Toplamlari(p, turler), KanalEtkileri(p, kanallar), UyarilariKur(p));

    /// <summary>İlk satır kaydedilecek satır sayısı ve genel kasa etkisi; ardından her işlem türünün satır sayısı ve kasa etkisi.</summary>
    private static List<EkstreOzetSatiri> Toplamlari(EkstreOnizlemeDto p, IReadOnlyList<EkstreSecenek> turler)
    {
        var toplamlar = new List<EkstreOzetSatiri> { new($"{p.Satirlar.Count} satır kaydedilecek", "Genel kasa etkisi", p.KasaEtkisi) };
        var sira = turler.Select((t, i) => (t.Kod, i)).DistinctBy(t => t.Kod).ToDictionary(t => t.Kod, t => t.i);
        foreach (var tur in p.Satirlar.GroupBy(s => s.IslemTuru).OrderBy(g => sira.GetValueOrDefault(g.Key, int.MaxValue)).ThenBy(g => g.Key, StringComparer.Ordinal))
            toplamlar.Add(new(turler.FirstOrDefault(t => t.Kod == tur.Key)?.Ad ?? tur.Key,
                $"{tur.Count()} satır" + (tur.All(s => s.KasaEtkisi == 0) ? " · kasa etkisi yok" : ""), tur.Sum(s => s.KasaEtkisi)));
        return toplamlar;
    }

    /// <summary>Kasa etkisinin kanallara dağılımı: sunucunun satır başına kanal payları kasa etkisinin yönüyle (gider eksi) toplanır.
    /// Payların karşılamadığı etki (yalnız genel kasa) <see cref="GenelKasa"/> satırındadır; satırların toplamı genel kasa etkisidir.
    /// Kasa etkisi olmayan satırın (kart harcaması, eşleştirme) kanal payı kart borcunundur, burada sayılmaz.</summary>
    private static List<EkstreOzetSatiri> KanalEtkileri(EkstreOnizlemeDto p, IReadOnlyList<KanalDto> kanallar)
    {
        var etkiler = new Dictionary<(int? Id, string Ad), (decimal Tutar, HashSet<int> Satirlar)>();
        void Ekle(int? id, string ad, decimal tutar, int satirNo)
        {
            var (toplam, satirlar) = etkiler.GetValueOrDefault((id, ad), (0m, []));
            satirlar.Add(satirNo);
            etkiler[(id, ad)] = (toplam + tutar, satirlar);
        }
        foreach (var s in p.Satirlar.Where(s => s.KasaEtkisi != 0))
        {
            var yon = Math.Sign(s.KasaEtkisi);
            var paylar = s.Dagilimlar.Where(d => d.Tutar != 0).ToList();
            foreach (var pay in paylar)
                Ekle(pay.KanalId, pay.Kanal, yon * pay.Tutar, s.SatirNo);
            if (s.KasaEtkisi - yon * paylar.Sum(d => d.Tutar) is var kalan and not 0)
                Ekle(null, GenelKasa, kalan, s.SatirNo);
        }
        // Önce yalnız genel kasa, sonra kanallar kendi sırasıyla; listede olmayan (pasif ya da silinmiş kanal, "Dağılım bekliyor") ada göre sonda.
        var sira = kanallar.Select((k, i) => (k.Id, i)).DistinctBy(k => k.Id).ToDictionary(k => k.Id, k => k.i);
        int Sira((int? Id, string Ad) k) => k is (null, GenelKasa) ? -1 : k.Id is { } id && sira.TryGetValue(id, out var i) ? i : int.MaxValue;
        return etkiler.OrderBy(e => Sira(e.Key)).ThenBy(e => e.Key.Ad, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), false))
            .Select(e => new EkstreOzetSatiri(e.Key.Ad, $"{e.Value.Satirlar.Count} satır", e.Value.Tutar)).ToList();
    }

    /// <summary>Satır uyarıları metne göre toplanır (ilk satır sırasıyla); seçimin tümüne ait uyarılar sondadır.</summary>
    private static List<EkstreOzetUyarisi> UyarilariKur(EkstreOnizlemeDto p) =>
        p.Satirlar.SelectMany(s => s.Uyarilar.Select(u => (Metin: u, s.SatirNo)))
            .GroupBy(u => u.Metin)
            .Select(g => new EkstreOzetUyarisi(g.Key, g.Select(u => u.SatirNo).Distinct().Order().ToList()))
            .OrderBy(u => u.SatirNolar[0])
            .Concat(p.Uyarilar.Distinct().Select(u => new EkstreOzetUyarisi(u, [])))
            .ToList();
}

// Kayıt özetinin görünüm modeli yanı: önizleme gelince dolar, girdi değişince boşalır.
public partial class EkstreAktarmaViewModel
{
    public ObservableCollection<EkstreOzetSatiri> OzetToplamlari { get; } = new();
    public ObservableCollection<EkstreOzetSatiri> KanalEtkileri { get; } = new();
    public ObservableCollection<EkstreOzetUyarisi> OzetUyarilari { get; } = new();
    /// <summary>Kanal etkisi tablosu yalnız kasa etkisi olan satır varken görünür (yalnız kart harcamasında boştur).</summary>
    public bool KanalEtkisiVar => KanalEtkileri.Count > 0;
    public bool OzetUyarisiVar => OzetUyarilari.Count > 0;

    private void OzetiYansit(EkstreKayitOzeti? ozet)
    {
        TakipMetni.Doldur(OzetToplamlari, ozet?.Toplamlar ?? []);
        TakipMetni.Doldur(KanalEtkileri, ozet?.Kanallar ?? []);
        TakipMetni.Doldur(OzetUyarilari, ozet?.Uyarilar ?? []);
        OnPropertyChanged(nameof(KanalEtkisiVar));
        OnPropertyChanged(nameof(OzetUyarisiVar));
    }

    /// <summary>Özetteki uyarıdan satıra (birden çok satırın uyarısında ilkine).</summary>
    public void SatiraGit(EkstreOzetUyarisi uyari) => SatiraGit(uyari.SatirNo);
}
