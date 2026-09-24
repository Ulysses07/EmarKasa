using Kasa.Api;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>DB'den veriyi yükler, dönem takvimini üretir ve HesapMotoru'nu çağırır.</summary>
public class HesapServisi
{
    private readonly KasaDbContext _db;
    private readonly TimeProvider _saat;
    public HesapServisi(KasaDbContext db, TimeProvider saat) { _db = db; _saat = saat; }

    private DateOnly Bugun => Saat.Bugun(_saat);

    private record Yuk(
        IReadOnlyList<Kanal> Kanallar,
        IReadOnlyList<Islem> Islemler,
        IReadOnlyList<Gelen> Gelenler,
        IReadOnlyList<Donem> Donemler,
        decimal KasaAcilis,
        IReadOnlyList<KartOdeme> KartOdemeleri,
        DateOnly TakipBaslangic,
        IReadOnlyList<Cek> Cekler);

    private (DateOnly Baslangic, decimal KasaAcilis) AyarOku()
    {
        var a = _db.Ayarlar.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.TakipBaslangic, x.KasaAcilisDevri }).First();
        return (a.TakipBaslangic, a.KasaAcilisDevri);
    }

    // Takvim bugünde biter: ileri tarihli kayıtlar, tarihleri gelene kadar hiçbir döneme girmez.
    private IReadOnlyList<Donem> TakvimUret(DateOnly baslangic)
        => DonemUretici.Uret(baslangic, Takvim.Bitis(baslangic, Bugun));

    private IReadOnlyList<Kanal> KanallariYukle()
        => _db.Kanallar.AsNoTracking().OrderBy(k => k.Sira).ThenBy(k => k.Id)
            .Select(e => new Kanal(e.Ad, e.AcilisDevri, e.Aktif, e.Sira)).ToList();

    /// <summary>
    /// Kasaya etkisi olabilecek çekler (tahsil edilen / ödenen), işlem tarihi [baslangic, bitis]
    /// aralığında. İleri tarihli tahsil/ödeme, ileri tarihli işlem gibi, tarihi gelene kadar yüklenmez.
    /// </summary>
    private IReadOnlyList<Cek> CekleriYukle(DateOnly? baslangic, DateOnly bitis)
    {
        var q = _db.Cekler.AsNoTracking()
            .Where(c => c.IslemTarihi != null && c.IslemTarihi <= bitis
                        && (c.Durum == CekDurumu.TahsilEdildi || c.Durum == CekDurumu.Odendi));
        if (baslangic is { } b) q = q.Where(c => c.IslemTarihi >= b);
        return q.Select(e => new Cek(e.Yon, e.Tutar, e.Kanal, e.Durum, e.IslemTarihi)).ToList();
    }

    /// <summary>
    /// Tüm hesap verisini izlemesiz (AsNoTracking projeksiyon) yükler. Takvim sonundan
    /// sonraki kayıtlar hiçbir döneme düşmeyeceği için hiç yüklenmez.
    /// </summary>
    private Yuk Yukle()
    {
        var (baslangic, kasaAcilis) = AyarOku();
        var bitis = Takvim.Bitis(baslangic, Bugun);
        var islemler = _db.Islemler.AsNoTracking().Where(i => i.Tarih <= bitis)
            .Select(e => new Islem(e.Tarih, e.Cari, e.TutarTl, e.Kanal, e.Tip, e.Not, e.KrediKartiId)).ToList();
        var gelenler = _db.Gelenler.AsNoTracking().Where(g => g.DonemStart <= bitis)
            .Select(e => new Gelen(e.DonemStart, e.Kanal, e.TutarTl)).ToList();
        var kartOdemeleri = _db.KartOdemeler.AsNoTracking().Where(o => o.Tarih <= bitis)
            .Select(e => new KartOdeme(e.Tarih, e.Tutar)).ToList();
        return new Yuk(KanallariYukle(), islemler, gelenler, TakvimUret(baslangic), kasaAcilis, kartOdemeleri, baslangic,
            CekleriYukle(null, bitis));
    }

    public IReadOnlyList<HaftalikOzet> Haftalik()
    {
        var y = Yukle();
        return HaftalikAylaraBolerek(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler, y.KartOdemeleri, y.Cekler);
    }

    /// <summary>
    /// <see cref="HesapMotoru.HaftalikHesapla"/> ile birebir aynı sonucu, motoru ay ay çağırarak
    /// üretir. Motor her dönem için tüm listeleri taradığından (dönem × işlem) tek çağrı veri
    /// büyüdükçe pahalılaşır; burada her ay yalnız o ayın dönemleri, o ayın ve bir önceki ayın
    /// (ertelenen K.K için) işlemleri, o ayın gelen/ödemeleri ve işlem tarihi o aya düşen çekler
    /// verilir. Açılış bakiyeleri bir önceki ayın kapanış devirleridir (kasa ve kanal başına).
    /// </summary>
    public static IReadOnlyList<HaftalikOzet> HaftalikAylaraBolerek(
        decimal kasaAcilis,
        IReadOnlyList<Kanal> kanallar,
        IReadOnlyList<Islem> islemler,
        IReadOnlyList<Gelen> gelenler,
        IReadOnlyList<Donem> donemler,
        IReadOnlyList<KartOdeme> kartOdemeleri,
        IReadOnlyList<Cek>? cekler = null)
    {
        static int AyAnahtari(DateOnly d) => d.Year * 12 + d.Month - 1;
        var islemAy = islemler.ToLookup(i => AyAnahtari(i.Tarih));
        var gelenAy = gelenler.ToLookup(g => AyAnahtari(g.DonemStart));
        var odemeAy = kartOdemeleri.ToLookup(o => AyAnahtari(o.Tarih));
        // İşlem tarihi olmayan çekin kasaya etkisi yoktur (CekKurali); hiçbir aya konmaz.
        var cekAy = (cekler ?? Array.Empty<Cek>()).Where(c => c.IslemTarihi is not null)
            .ToLookup(c => AyAnahtari(c.IslemTarihi!.Value));

        var sonuc = new List<HaftalikOzet>(donemler.Count);
        var kasa = kasaAcilis;
        var kanalDurum = kanallar;
        foreach (var ay in donemler.OrderBy(d => d.Start).GroupBy(d => AyAnahtari(d.Start)))
        {
            var ayIslem = islemAy[ay.Key - 1].Concat(islemAy[ay.Key]).ToList();
            var parca = HesapMotoru.HaftalikHesapla(kasa, kanalDurum, ayIslem, gelenAy[ay.Key].ToList(), ay.ToList(), odemeAy[ay.Key].ToList(),
                cekAy[ay.Key].ToList());
            if (parca.Count == 0) continue;
            sonuc.AddRange(parca);
            var son = parca[^1];
            kasa = son.KasaDevir;
            var devir = son.Kanallar.ToDictionary(k => k.Kanal, k => k.Devir);
            kanalDurum = kanalDurum.Select(k => k with { AcilisDevri = devir[k.Ad] }).ToList();
        }
        return sonuc;
    }

    /// <summary>
    /// Aylık rapor yalnız o ayın ve (ertelenen K.K için) bir önceki ayın işlemlerine, o ayın
    /// gelenlerine ve işlem tarihi o aya düşen çeklere bakar; bu yüzden yalnız bu aralık yüklenir.
    /// </summary>
    public AylikRapor Aylik(int yil, int ay)
    {
        var (baslangic, _) = AyarOku();
        var ayBasi = new DateOnly(yil, ay, 1);
        var aySonu = ayBasi.AddMonths(1).AddDays(-1);
        var oncekiAyBasi = ayBasi.AddMonths(-1);
        var bitis = Takvim.Bitis(baslangic, Bugun);
        var islemler = _db.Islemler.AsNoTracking()
            .Where(i => i.Tarih >= oncekiAyBasi && i.Tarih <= aySonu && i.Tarih <= bitis)
            .Select(e => new Islem(e.Tarih, e.Cari, e.TutarTl, e.Kanal, e.Tip, e.Not, e.KrediKartiId)).ToList();
        var gelenler = _db.Gelenler.AsNoTracking()
            .Where(g => g.DonemStart >= ayBasi && g.DonemStart <= aySonu)
            .Select(e => new Gelen(e.DonemStart, e.Kanal, e.TutarTl)).ToList();
        var cekler = CekleriYukle(ayBasi, aySonu < bitis ? aySonu : bitis);
        var donemler = TakvimUret(baslangic).Where(d => d.Yil == yil && d.Ay == ay).ToList();
        // Dönem listesi yalnız bu ayı kapsar; takip başlangıcı açıkça verilmezse motor önceki ayın
        // K.K'sını "takipten önce" sayıp düşürür.
        return HesapMotoru.AylikHesapla(yil, ay, KanallariYukle(), islemler, gelenler, donemler, baslangic, cekler);
    }

    /// <summary>Takvim yalnız ayarlardan türetilir; işlem tablolarına dokunmaz.</summary>
    public IReadOnlyList<Donem> Donemler() => TakvimUret(AyarOku().Baslangic);

    public PanelDto Panel()
    {
        var y = Yukle();
        var haftalik = HaftalikAylaraBolerek(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler, y.KartOdemeleri, y.Cekler);
        var son = haftalik.Count > 0 ? haftalik[^1] : null;

        var guncelKasa = son?.KasaDevir ?? y.KasaAcilis;
        var kanalBakiyeleri = son is not null
            ? son.Kanallar.Select(k => new KanalBakiye(k.Kanal, k.Devir)).ToList()
            : y.Kanallar.Select(k => new KanalBakiye(k.Ad, k.AcilisDevri)).ToList();

        // "Bu hafta" = içinde bulunulan Mon–Sun haftası. Hafta ay sonunda ikiye bölündüyse
        // (ör. 29–30 Eyl + 1–2 Eki) iki dönemin sonucu toplanır.
        var bugun = Bugun;
        var haftaBasi = Takvim.Pazartesi(bugun);
        var buHafta = haftalik.Where(h => h.Donem.Start >= haftaBasi).Sum(h => h.KasaSonucu);

        var buAyBasi = new DateOnly(bugun.Year, bugun.Month, 1);
        var ilgiliIslem = y.Islemler.Where(i => i.Tarih >= buAyBasi.AddMonths(-1)).ToList();
        var ilgiliCek = y.Cekler.Where(c => c.IslemTarihi >= buAyBasi).ToList();
        var buAyRapor = HesapMotoru.AylikHesapla(bugun.Year, bugun.Month, y.Kanallar, ilgiliIslem, y.Gelenler, y.Donemler, y.TakipBaslangic, ilgiliCek);
        var buAy = buAyRapor.Kanallar.Sum(k => k.AySonucu);

        return new PanelDto(guncelKasa, kanalBakiyeleri, buHafta, buAy);
    }
}
