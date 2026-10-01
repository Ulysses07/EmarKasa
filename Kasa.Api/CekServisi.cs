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

    internal static List<Kayit> Oku(KasaDbContext db, int? id = null)
    {
        var cekler = (id is { } i ? db.Cekler.AsNoTracking().Where(c => c.Id == i) : db.Cekler.AsNoTracking()).OrderBy(c => c.Id).ToList();
        // id verilmişse (tek çek) yalnız o çekin hareketleri okunur; id yoksa (liste) hepsi okunur — büyük bir Id listesiyle
        // SQL IN'e gerek kalmaz.
        var hareketSorgusu = id is { } i2 ? db.CekHareketler.AsNoTracking().Where(h => h.CekId == i2) : db.CekHareketler.AsNoTracking();
        var hareketler = hareketSorgusu.ToList().ToLookup(h => h.CekId);
        return cekler.Select(c =>
        {
            var liste = hareketler[c.Id].OrderBy(h => h.Sira).ToList();
            return new Kayit(c, liste, CekKurallari.Durum(c.Yon, c.Tutar, liste.Select(Cekirdek).ToList()));
        }).ToList();
    }

    /// <summary>Tek çek; yoksa null.</summary>
    internal static CekDto? Tek(TakipHesapBaglami b, int id) => Oku(b.Db, id) is [var k] ? Dto(b, k, BenzerGruplari(b.Db)) : null;

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
