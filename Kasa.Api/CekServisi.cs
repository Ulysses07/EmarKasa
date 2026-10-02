using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

/// <summary>Çek okumaları (liste, tek kayıt; bildirim ve panel özeti de buradan okur): kayıtlar ve hareketler tek sorguda okunur, durum
/// <see cref="CekKurallari"/>'ndan hesaplanır. Girdi doğrulaması uçtadır (FinansTakipEndpoints.MapCekEndpoints).</summary>
internal static class CekServisi
{
    internal sealed record Kayit(CekEntity Cek, IReadOnlyList<CekHareketEntity> Hareketler, CekDurumu Durum);

    /// <summary>Aynı çek uyarısının karşılaştırdığı alanlar (<see cref="BenzerGruplari"/>'nın girdisi).</summary>
    private readonly record struct BenzerBilgi(int Id, string Yon, string? Banka, string No);

    /// <summary>Aynı çek grubunun anahtarı: <see cref="CekKurallari.AyniCek"/> ile birebir aynı karşılaştırma (yön ve
    /// AramaAnahtari'yle katlanmış banka/numara). Katlama deterministik olduğundan bu anahtarla gruplamak, her çifti
    /// <c>AyniCek</c> ile tek tek karşılaştırmaya eşdeğerdir (denklik ilişkisi korunur) ama O(n²) değil O(n) sürer.</summary>
    private static (string Yon, string Banka, string No) BenzerAnahtari(BenzerBilgi c) =>
        (c.Yon, CekKurallari.AramaAnahtari(c.Banka), CekKurallari.AramaAnahtari(c.No));

    /// <summary>Verilen çek kümesini aynı çek gruplarına ayırır; her grubun kimlikleri Id sırasıyla.</summary>
    private static Dictionary<(string Yon, string Banka, string No), List<int>> BenzerGruplari(IEnumerable<BenzerBilgi> cekler)
    {
        var gruplar = new Dictionary<(string Yon, string Banka, string No), List<int>>();
        foreach (var c in cekler.OrderBy(c => c.Id))
        {
            var anahtar = BenzerAnahtari(c);
            if (!gruplar.TryGetValue(anahtar, out var liste))
                gruplar[anahtar] = liste = [];
            liste.Add(c.Id);
        }
        return gruplar;
    }

    private static Dictionary<(string Yon, string Banka, string No), List<int>> BenzerGruplari(KasaDbContext db) =>
        BenzerGruplari(db.Cekler.AsNoTracking().Select(c => new BenzerBilgi(c.Id, c.Yon, c.Banka, c.No)).AsEnumerable());

    /// <summary>Verilen çekle aynı grupta olup kendisi olmayan çeklerin "#Id" biçimindeki listesi.</summary>
    private static List<string> Ayni(IReadOnlyDictionary<(string Yon, string Banka, string No), List<int>> gruplar, CekEntity c) =>
        gruplar.TryGetValue(BenzerAnahtari(new BenzerBilgi(c.Id, c.Yon, c.Banka, c.No)), out var liste)
            ? liste.Where(id => id != c.Id).Select(id => "#" + id).ToList()
            : [];

    internal static CekHareketi Cekirdek(CekHareketEntity h) => new(h.Id, h.Sira, h.Tur, h.Tarih, h.Tutar, h.NetTutar, null, h.Karsi);

    /// <summary><paramref name="id"/> verilmişse (tek çek) yalnız o çekin, <paramref name="adaylar"/> verilmişse yalnız o
    /// id'lerin (ör. CekBildirimleri'ndeki aday seçimi) kaydı ve hareketleri okunur; ikisi de yoksa (liste) hepsi okunur.</summary>
    internal static List<Kayit> Oku(KasaDbContext db, int? id = null, IReadOnlyCollection<int>? adaylar = null)
    {
        var cekSorgusu = id is { } i ? db.Cekler.AsNoTracking().Where(c => c.Id == i)
            : adaylar is not null ? db.Cekler.AsNoTracking().Where(c => adaylar.Contains(c.Id))
            : db.Cekler.AsNoTracking();
        var cekler = cekSorgusu.OrderBy(c => c.Id).ToList();
        var hareketSorgusu = id is { } i2 ? db.CekHareketler.AsNoTracking().Where(h => h.CekId == i2)
            : adaylar is not null ? db.CekHareketler.AsNoTracking().Where(h => adaylar.Contains(h.CekId))
            : db.CekHareketler.AsNoTracking();
        var hareketler = hareketSorgusu.ToList().ToLookup(h => h.CekId);
        return cekler.Select(c =>
        {
            var liste = hareketler[c.Id].OrderBy(h => h.Sira).ToList();
            return new Kayit(c, liste, CekKurallari.Durum(c.Yon, c.Tutar, liste.Select(Cekirdek).ToList()));
        }).ToList();
    }

    /// <summary>Tek çek; yoksa null.</summary>
    internal static CekDto? Tek(TakipHesapBaglami b, int id) => Oku(b.Db, id) is [var k] ? Dto(b, k, BenzerGruplari(b.Db)) : null;

    internal static CekOzetDto Ozet(TakipHesapBaglami b)
    {
        var bugun = b.Bugun;
        var sinir = bugun.AddDays(30);
        var acik = Oku(b.Db).Where(k => !k.Cek.Teminat && k.Durum.Acik).ToList();
        var alinan = acik.Where(k => k.Cek.Yon == CekYonleri.Alinan).ToList();
        return new(bugun, Kalem(alinan), Kalem(alinan.Where(k => k.Cek.VadeTarihi >= bugun && k.Cek.VadeTarihi <= sinir)),
            Kalem(acik.Where(k => k.Cek.Yon == CekYonleri.Verilen && k.Cek.VadeTarihi >= bugun && k.Cek.VadeTarihi <= sinir)),
            Kalem(alinan.Where(k => k.Cek.VadeTarihi < bugun)));
    }

    private static CekOzetKalemi Kalem(IEnumerable<Kayit> kayitlar)
    {
        var liste = kayitlar.ToList();
        return new(liste.Count, liste.Sum(k => k.Durum.Kalan));
    }

    internal static List<CekDto> Liste(TakipHesapBaglami b, string? yon, string? durum, string? ara, DateOnly? vadeBas, DateOnly? vadeSon)
    {
        var aranan = string.IsNullOrWhiteSpace(ara) ? null : CekKurallari.AramaAnahtari(ara);
        bool Icerir(string? alan) => CekKurallari.AramaAnahtari(alan).Contains(aranan!, StringComparison.Ordinal);
        var kayitlar = Oku(b.Db);
        // Benzer grupları aynı okumadan türetilir; ikinci bir Cekler sorgusu atılmaz.
        var gruplar = BenzerGruplari(kayitlar.Select(k => new BenzerBilgi(k.Cek.Id, k.Cek.Yon, k.Cek.Banka, k.Cek.No)));
        return kayitlar
            .Where(k => yon is null || k.Cek.Yon == yon)
            .Where(k => (durum ?? CekSuzgecleri.Hepsi) switch
            {
                CekSuzgecleri.Portfoyde => k.Durum.Acik,
                CekSuzgecleri.Karsiliksiz => k.Durum.Durum == CekDurumlari.Karsiliksiz,
                CekSuzgecleri.Kapanan => k.Durum.Kapali,
                _ => true,
            })
            .Where(k => aranan is null || Icerir(k.Cek.Kisi) || Icerir(k.Cek.Banka) || Icerir(k.Cek.No))
            .Where(k => (vadeBas is null || k.Cek.VadeTarihi >= vadeBas) && (vadeSon is null || k.Cek.VadeTarihi <= vadeSon))
            .OrderBy(k => k.Cek.VadeTarihi).ThenBy(k => k.Cek.Id)
            .Select(k => Dto(b, k, gruplar)).ToList();
    }

    private static CekDto Dto(TakipHesapBaglami b, Kayit k, IReadOnlyDictionary<(string Yon, string Banka, string No), List<int>> gruplar)
    {
        var c = k.Cek;
        string? KanalAdi(int? kanalId) => kanalId is { } i ? b.KanalAdlari.GetValueOrDefault(i, $"Silinmiş kanal #{i}") : null;
        var verilen = c.Yon == CekYonleri.Verilen;
        var ayni = Ayni(gruplar, c);
        return new CekDto(c.Id, c.Surum, c.Tur, c.Yon, c.No, c.Banka, c.Kisi, c.Tutar, c.VadeTarihi, c.KanalId,
            verilen ? KanalAdi(c.KanalId) ?? KanalEtiketleri.Ortak : null, c.Teminat, c.Konum, c.Not, k.Durum.Durum, k.Durum.Kalan,
            CekKurallari.IzinliHareketler(c.Yon, c.Tutar, k.Hareketler.Select(Cekirdek).ToList()),
            k.Hareketler.Select(h => new CekHareketDto(h.Id, h.Sira, h.Tur, h.Tarih, h.Tutar, h.NetTutar, h.KanalId,
                KanalAdi(h.KanalId) ?? (verilen && h.Tur == CekHareketTurleri.Odeme ? KanalEtiketleri.Ortak : null), h.Karsi)).ToList(),
            ayni.Count == 0 ? null : $"Aynı yön, banka ve numarayla kayıtlı başka çek var: {string.Join(", ", ayni)}.");
    }
}
