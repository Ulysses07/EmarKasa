using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>
/// Nakit tahmini (Panel · 30/60/90 gün). Bugünkü kasadan (<see cref="HesapServisi.Panel"/>'in
/// GuncelKasa'sı, hesap motorunun ürettiği rakam) başlar; yalnız bilinen/planlanmış hareketleri
/// gün gün ekler. Hiçbir kaydı değiştirmez, hesap motorunu da değiştirmez: kurallar
/// <see cref="NakitTahmini"/>'ndedir, burada yalnız veri yüklenir.
/// <list type="bullet">
/// <item>Portföydeki alınan çek vadesinde +, ödenecek (portföydeki) verilen çek vadesinde −. Vadesi geçmiş
/// portföy çeki hâlâ bekliyor sayılır (yarına yazılır). <c>haricCekler</c>'deki çekler hesaba girmez,
/// ayrı listede döner (riskli çek).</item>
/// <item>Tahsil edildi / ödendi olarak ileri tarihle girilmiş çek, işlem tarihinde.</item>
/// <item>Kart ekstreleri son ödeme gününde − (<see cref="NakitTahmini.KartOdemeleri"/>); ileri tarihli
/// girilmiş kart ödemesi kendi tarihinde −.</item>
/// <item>Onay bekleyen tekrarlayan giderler yarın −, ufuk içinde vadesi gelecek aylar vadede −.</item>
/// <item>İleri tarihli girilmiş gider işlemleri (Cari / sabit gider) tarihinde −.</item>
/// <item>Karta bağlı olmayan eski K.K: sonraki ayın son döneminin ilk günü −.</item>
/// </list>
/// Gelecek gelenler (satış) bilinmediği için tahmine girmez.
/// </summary>
public class TahminServisi
{
    private readonly KasaDbContext _db;
    private readonly TimeProvider _saat;
    private readonly HesapServisi _hesap;

    public TahminServisi(KasaDbContext db, TimeProvider saat, HesapServisi hesap)
    {
        _db = db; _saat = saat; _hesap = hesap;
    }

    /// <param name="gun">Ufuk (1–<see cref="NakitTahmini.EnFazlaGun"/>).</param>
    /// <param name="haricCekler">Hesaptan çıkarılacak çek Id'leri (olmayan Id yok sayılır).</param>
    public NakitTahminSonucu Hesapla(int gun, IReadOnlyCollection<int>? haricCekler = null)
    {
        var bugun = Saat.Bugun(_saat);
        var bitis = bugun.AddDays(gun);
        var takip = _db.Ayarlar.AsNoTracking().OrderBy(x => x.Id).Select(x => x.TakipBaslangic).First();
        // Bugünkü kasaya giren son gün (HesapServisi ile aynı sınır); sonrası tahmindir.
        var sinir = Takvim.Bitis(takip, bugun);
        var baslangicKasa = _hesap.Panel().GuncelKasa;

        var haric = haricCekler?.ToHashSet() ?? [];
        var kalemler = new List<TahminKalemi>();
        var haricKalemler = new List<TahminKalemi>();

        foreach (var (kalem, cekId) in CekKalemleri(sinir, bitis))
            (haric.Contains(cekId) ? haricKalemler : kalemler).Add(kalem);
        kalemler.AddRange(KartKalemleri(bugun, bitis));
        kalemler.AddRange(TekrarlayanKalemleri(bugun, bitis));
        kalemler.AddRange(IleriTarihliIslemler(sinir, bitis));
        kalemler.AddRange(KartsizKrediKarti(bugun, bitis));

        return NakitTahmini.Hesapla(bugun, baslangicKasa, gun, kalemler, haricKalemler);
    }

    private IEnumerable<(TahminKalemi Kalem, int CekId)> CekKalemleri(DateOnly sinir, DateOnly bitis)
    {
        var cekler = _db.Cekler.AsNoTracking()
            .Where(c => (c.Durum == CekDurumu.Portfoyde && c.VadeTarihi <= bitis)
                        || ((c.Durum == CekDurumu.TahsilEdildi || c.Durum == CekDurumu.Odendi)
                            && c.IslemTarihi != null && c.IslemTarihi > sinir && c.IslemTarihi <= bitis))
            .ToList();
        foreach (var c in cekler)
        {
            var alinan = c.Yon == CekYonu.Alinan;
            var tarih = c.Durum == CekDurumu.Portfoyde ? c.VadeTarihi : c.IslemTarihi!.Value;
            var aciklama = $"{Metin.Kisalt(c.Kisi, 40)} · {(alinan ? "alınan çek" : "verilen çek")}";
            yield return (new TahminKalemi(tarih, alinan ? TahminKalemTuru.AlinanCek : TahminKalemTuru.VerilenCek,
                aciklama, alinan ? c.Tutar : -c.Tutar, CekId: c.Id), c.Id);
        }
    }

    private IEnumerable<TahminKalemi> KartKalemleri(DateOnly bugun, DateOnly bitis)
    {
        // Kart durumu GET /api/kredikartlari ile aynı yoldan (KartHesap.Durum) türetilir.
        var kartlar = _db.KrediKartlari.AsNoTracking().ToList();
        if (kartlar.Count == 0) return [];
        var harcamalar = _db.Islemler.AsNoTracking().Where(i => i.KrediKartiId != null)
            .Select(i => new { Id = i.KrediKartiId!.Value, i.Tarih, i.TutarTl }).ToList()
            .ToLookup(x => x.Id, x => new KartHarcama(x.Tarih, x.TutarTl));
        var odemeler = _db.KartOdemeler.AsNoTracking()
            .Select(o => new { o.KrediKartiId, o.Tarih, o.Tutar }).ToList()
            .ToLookup(o => o.KrediKartiId, o => new KartOdeme(o.Tarih, o.Tutar));

        var sonuc = new List<TahminKalemi>();
        foreach (var k in kartlar.OrderBy(k => k.Ad, Metin.Sirala).ThenBy(k => k.Id))
        {
            var h = harcamalar[k.Id].ToList();
            var o = odemeler[k.Id].ToList();
            var d = KartHesap.Durum(k.Borc, h, o, k.KesimTarihi.Day, bugun);
            sonuc.AddRange(NakitTahmini.KartOdemeleri(k.Ad, k.KesimTarihi.Day, k.SonOdemeTarihi.Day,
                d.EkstreBorc, d.GuncelBorc, h.Where(x => x.Tarih > bugun), o.Where(x => x.Tarih > bugun), bugun, bitis));
        }
        return sonuc;
    }

    private IEnumerable<TahminKalemi> TekrarlayanKalemleri(DateOnly bugun, DateOnly bitis)
    {
        var sablonlar = _db.TekrarlayanGiderler.AsNoTracking().Where(t => t.Aktif).ToList();
        if (sablonlar.Count == 0) return [];
        var buAy = TekrarlayanTakvim.AyBasi(bugun);
        var enErken = buAy.AddMonths(-TekrarlayanTakvim.GeriyeAy);
        var kararlar = _db.TekrarlayanGirisler.AsNoTracking().Where(g => g.Ay >= enErken)
            .Select(g => new { g.TekrarlayanGiderId, g.Ay }).AsEnumerable()
            .Select(g => (g.TekrarlayanGiderId, g.Ay)).ToHashSet();

        var sonuc = new List<TahminKalemi>();
        // Vadesi gelmiş, kararı verilmemiş aylar (Panel'deki "bekleyen" listesiyle aynı).
        var sablonKayitlari = sablonlar.Select(t => new TekrarlayanSablon(t.Id, t.Kalem, t.Kanal, t.Tutar, t.AyinGunu, t.Aktif, t.BaslangicAyi));
        foreach (var b in TekrarlayanTakvim.Bekleyenler(sablonKayitlari, kararlar, bugun))
            sonuc.Add(new TahminKalemi(b.Vade, TahminKalemTuru.TekrarlayanGider,
                $"{Metin.Kisalt(b.Kalem, 40)} · tekrarlayan gider (onay bekliyor)", -b.Tutar));
        // Ufuk içinde vadesi gelecek aylar.
        var sonAy = TekrarlayanTakvim.AyBasi(bitis);
        foreach (var s in sablonlar)
        {
            var bas = TekrarlayanTakvim.AyBasi(s.BaslangicAyi);
            for (var ay = bas > buAy ? bas : buAy; ay <= sonAy; ay = ay.AddMonths(1))
            {
                var vade = TekrarlayanTakvim.Vade(ay, s.AyinGunu);
                if (vade <= bugun || vade > bitis || kararlar.Contains((s.Id, ay))) continue;
                sonuc.Add(new TahminKalemi(vade, TahminKalemTuru.TekrarlayanGider,
                    $"{Metin.Kisalt(s.Kalem, 40)} · tekrarlayan gider", -s.Tutar));
            }
        }
        return sonuc;
    }

    private IEnumerable<TahminKalemi> IleriTarihliIslemler(DateOnly sinir, DateOnly bitis)
        => _db.Islemler.AsNoTracking()
            .Where(i => i.Tarih > sinir && i.Tarih <= bitis && i.KrediKartiId == null && i.Tip != GiderTipi.KrediKarti)
            .ToList()
            .Select(i => new TahminKalemi(i.Tarih, TahminKalemTuru.IleriTarihliIslem,
                $"{Metin.Kisalt(i.Cari, 40)} · {i.Kanal}", -i.TutarTl));

    private IEnumerable<TahminKalemi> KartsizKrediKarti(DateOnly bugun, DateOnly bitis)
    {
        // Bu aydan önceki ayın K.K'sı bu ayın son döneminde düşer; daha eskisi bugünkü kasaya girmiştir.
        var ilk = TekrarlayanTakvim.AyBasi(bugun).AddMonths(-1);
        var islemler = _db.Islemler.AsNoTracking()
            .Where(i => i.Tarih >= ilk && i.Tarih <= bitis && i.KrediKartiId == null && i.Tip == GiderTipi.KrediKarti)
            .Select(e => new Islem(e.Tarih, e.Cari, e.TutarTl, e.Kanal, e.Tip, e.Not, e.KrediKartiId))
            .ToList();
        return NakitTahmini.KartsizKrediKartiDusumleri(islemler, bugun, bitis);
    }
}
