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
    /// <param name="sonGun">Takvimin son günü; verilmezse bugün (<see cref="Takvim.Bitis"/>).
    /// Verilirse takvim o günde biter ve son dönem o güne kırpılır (kasa sayımı).</param>
    private Yuk Yukle(DateOnly? sonGun = null)
    {
        var (baslangic, kasaAcilis) = AyarOku();
        var bitis = sonGun ?? Takvim.Bitis(baslangic, Bugun);
        var islemler = _db.Islemler.AsNoTracking().Where(i => i.Tarih <= bitis)
            .Select(e => new Islem(e.Tarih, e.Cari, e.TutarTl, e.Kanal, e.Tip, e.Not, e.KrediKartiId)).ToList();
        var gelenler = _db.Gelenler.AsNoTracking().Where(g => g.DonemStart <= bitis)
            .Select(e => new Gelen(e.DonemStart, e.Kanal, e.TutarTl)).ToList();
        var kartOdemeleri = _db.KartOdemeler.AsNoTracking().Where(o => o.Tarih <= bitis)
            .Select(e => new KartOdeme(e.Tarih, e.Tutar)).ToList();
        return new Yuk(KanallariYukle(), islemler, gelenler, DonemUretici.Uret(baslangic, bitis), kasaAcilis, kartOdemeleri, baslangic,
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

    /// <summary>Kasa sayımının girilebileceği aralık: takip başlangıcı – bugün (başlangıç ilerideyse boş).</summary>
    public (DateOnly Ilk, DateOnly Son) SayimAraligi() => (AyarOku().Baslangic, Bugun);

    /// <summary>
    /// <paramref name="tarih"/> gününün SONUNDA defterdeki kasa. Panelin güncel kasasıyla aynı
    /// yoldan hesaplanır, yalnız takvim bu günde biter: tarih bugünse sonuç panelin GuncelKasa'sına
    /// eşittir. Tarih bir dönemin ortasındaysa o dönem bu güne kırpılır; kurallar paneldekiyle
    /// aynıdır: işlem ve kart ödemesi tarihine göre gün gün sayılır (karta bağlı K.K kasadan ödeme
    /// gününde çıkar), gelen dönem başına tek rakam olduğundan dönemin geleni tümüyle sayılır,
    /// kartsız eski K.K bir sonraki ayın son döneminde (o dönemin içindeki her gün için) düşülür.
    /// Takvim aralığı dışındaki tarih (takipten önce) için açılış devri döner; çağıran doğrular.
    /// </summary>
    public decimal KasaTarihte(DateOnly tarih)
    {
        var y = Yukle(tarih);
        var haftalik = HaftalikAylaraBolerek(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler, y.KartOdemeleri, y.Cekler);
        return haftalik.Count > 0 ? haftalik[^1].KasaDevir : y.KasaAcilis;
    }

    /// <summary>
    /// Birden çok tarih için <see cref="KasaTarihte"/> (sayım geçmişinin bugünkü defter değeri).
    /// Veri bir kez yüklenir, tam takvim bir kez hesaplanır; her tarih için yalnız o tarihin ayı,
    /// önceki ayın kapanış devirlerinden başlayıp takvim o güne kırpılarak yeniden hesaplanır.
    /// Bu, <see cref="HaftalikAylaraBolerek"/>'in takvimi o günde biten hesapta son ay için
    /// yaptığının aynısıdır (motor nedenseldir: önceki aylar sonraki kayıtlardan etkilenmez).
    /// Takvim aralığı (takip başlangıcı – bugün) dışındaki tarihler sonuçta yer almaz.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, decimal> KasaTarihlerde(IEnumerable<DateOnly> tarihler)
    {
        var sonuc = new Dictionary<DateOnly, decimal>();
        var istenen = tarihler.Distinct().ToList();
        if (istenen.Count == 0) return sonuc;

        var y = Yukle();
        if (y.Donemler.Count == 0) return sonuc;
        var takvimSonu = y.Donemler[^1].End;
        var tam = HaftalikAylaraBolerek(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler, y.KartOdemeleri, y.Cekler);

        static int AyAnahtari(DateOnly d) => d.Year * 12 + d.Month - 1;
        // Her ayın kapanışı (o ayın son döneminin kasa ve kanal devirleri).
        var kapanis = tam.GroupBy(h => AyAnahtari(h.Donem.Start)).ToDictionary(g => g.Key, g => g.Last());
        var islemAy = y.Islemler.ToLookup(i => AyAnahtari(i.Tarih));
        var gelenAy = y.Gelenler.ToLookup(g => AyAnahtari(g.DonemStart));
        var odemeAy = y.KartOdemeleri.ToLookup(o => AyAnahtari(o.Tarih));
        var cekAy = y.Cekler.Where(c => c.IslemTarihi is not null).ToLookup(c => AyAnahtari(c.IslemTarihi!.Value));

        foreach (var d in istenen)
        {
            if (d < y.TakipBaslangic || d > takvimSonu) continue;
            var ay = AyAnahtari(d);
            var ayBasi = new DateOnly(d.Year, d.Month, 1);

            var kasa = y.KasaAcilis;
            var kanallar = y.Kanallar;
            if (kapanis.TryGetValue(ay - 1, out var onceki))
            {
                kasa = onceki.KasaDevir;
                var devir = onceki.Kanallar.ToDictionary(k => k.Kanal, k => k.Devir);
                kanallar = y.Kanallar.Select(k => k with { AcilisDevri = devir[k.Ad] }).ToList();
            }

            var donemler = DonemUretici.Uret(y.TakipBaslangic > ayBasi ? y.TakipBaslangic : ayBasi, d);
            var islemler = islemAy[ay - 1].Concat(islemAy[ay].Where(i => i.Tarih <= d)).ToList();
            var gelenler = gelenAy[ay].Where(g => g.DonemStart <= d).ToList();
            var odemeler = odemeAy[ay].Where(o => o.Tarih <= d).ToList();
            var cekler = cekAy[ay].Where(c => c.IslemTarihi <= d).ToList();
            var parca = HaftalikAylaraBolerek(kasa, kanallar, islemler, gelenler, donemler, odemeler, cekler);
            sonuc[d] = parca.Count > 0 ? parca[^1].KasaDevir : kasa;
        }
        return sonuc;
    }

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
