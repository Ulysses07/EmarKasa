using System.Globalization;
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

    private static readonly CompareInfo Tr = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

    internal static CekHareketi Cekirdek(CekHareketEntity h) => new(h.Id, h.Sira, h.Tur, h.Tarih, h.Tutar, h.NetTutar, null, h.Karsi);

    internal static List<Kayit> Oku(KasaDbContext db, int? id = null)
    {
        var cekler = (id is { } i ? db.Cekler.AsNoTracking().Where(c => c.Id == i) : db.Cekler.AsNoTracking()).OrderBy(c => c.Id).ToList();
        var kimlikler = cekler.Select(c => c.Id).ToArray();
        var hareketler = db.CekHareketler.AsNoTracking().Where(h => kimlikler.Contains(h.CekId)).ToList().ToLookup(h => h.CekId);
        return cekler.Select(c =>
        {
            var liste = hareketler[c.Id].OrderBy(h => h.Sira).ToList();
            return new Kayit(c, liste, CekKurallari.Durum(c.Yon, c.Tutar, liste.Select(Cekirdek).ToList()));
        }).ToList();
    }

    /// <summary>Tek çek; yoksa null.</summary>
    internal static CekDto? Tek(TakipHesapBaglami b, int id) => Oku(b.Db, id) is [var k] ? Dto(b, k, Benzerler(b.Db)) : null;

    internal static List<CekDto> Liste(TakipHesapBaglami b, string? yon, string? durum, string? ara, DateOnly? vadeBas, DateOnly? vadeSon)
    {
        var aranan = string.IsNullOrWhiteSpace(ara) ? null : ara.Trim();
        bool Icerir(string? alan) => alan is not null && Tr.IndexOf(alan, aranan!, CompareOptions.IgnoreCase) >= 0;
        var benzerler = Benzerler(b.Db);
        return Oku(b.Db)
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
            .Select(k => Dto(b, k, benzerler)).ToList();
    }

    private static List<(int Id, string Yon, string? Banka, string No)> Benzerler(KasaDbContext db) =>
        db.Cekler.AsNoTracking().Select(c => new { c.Id, c.Yon, c.Banka, c.No }).AsEnumerable().Select(c => (c.Id, c.Yon, c.Banka, c.No)).ToList();

    private static CekDto Dto(TakipHesapBaglami b, Kayit k, IReadOnlyList<(int Id, string Yon, string? Banka, string No)> benzerler)
    {
        var c = k.Cek;
        string? KanalAdi(int? kanalId) => kanalId is { } i ? b.KanalAdlari.GetValueOrDefault(i, $"Silinmiş kanal #{i}") : null;
        var verilen = c.Yon == CekYonleri.Verilen;
        var ayni = benzerler.Where(x => x.Id != c.Id && CekKurallari.AyniCek(c.Yon, c.Banka, c.No, x.Yon, x.Banka, x.No)).Select(x => "#" + x.Id).ToList();
        return new CekDto(c.Id, c.Surum, c.Tur, c.Yon, c.No, c.Banka, c.Kisi, c.Tutar, c.VadeTarihi, c.KanalId,
            verilen ? KanalAdi(c.KanalId) ?? KanalEtiketleri.Ortak : null, c.Teminat, c.Konum, c.Not, k.Durum.Durum, k.Durum.Kalan,
            CekKurallari.IzinliHareketler(c.Yon, c.Tutar, k.Hareketler.Select(Cekirdek).ToList()),
            k.Hareketler.Select(h => new CekHareketDto(h.Id, h.Sira, h.Tur, h.Tarih, h.Tutar, h.NetTutar, h.KanalId,
                KanalAdi(h.KanalId) ?? (verilen && h.Tur == CekHareketTurleri.Odeme ? KanalEtiketleri.Ortak : null), h.Karsi)).ToList(),
            ayni.Count == 0 ? null : $"Aynı yön, banka ve numarayla kayıtlı başka çek var: {string.Join(", ", ayni)}.");
    }
}
