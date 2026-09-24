using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>
/// Kaydetmeden önceki uyarılar (bkz. <see cref="IslemUyariKodlari"/>). Yalnız sorar: kaydı hiçbir
/// zaman engellemez, kayıt kuralları (<c>IslemHatasi</c>) aynen geçerlidir. Kurallar sunucudadır ki
/// her istemci aynı uyarıyı görsün.
/// <list type="bullet">
/// <item>Aynı cariye aynı tutar ±<see cref="AyniTutarGun"/> gün içinde girilmiş.</item>
/// <item>Tutar, carinin son <see cref="OlaganOrnek"/> işleminin ortancasının <see cref="OlaganKat"/> katı
///       ya da fazlası (en az <see cref="OlaganEnAz"/> işlem varsa).</item>
/// <item>Kayıt kapanmış (geçmiş) bir ayın rakamlarını değiştiriyor (düzenlemede eski ya da yeni hali): ortakların
///       gördüğü raporlar değişir. Kural Geçmiş'teki "Geçmişe dönük" işaretinin kuralıdır
///       (<see cref="GecmiseDonukKurali"/>; K.K harcaması ertesi ayı etkiler): kayıttan sonra işaretlenecek her
///       değişiklik önceden uyarılır, işaretlenmeyecek olan uyarılmaz.</item>
/// <item>Bu kişiye (çekteki kişi/firma = cari, harf duyarsız) aynı tutarda verilmiş çek var: ödenmişse ödeme
///       tarihi, ödenecekse vadesi işlem tarihinin ±<see cref="CekGun"/> günü içinde (çift düşme).</item>
/// </list>
/// </summary>
public static class IslemUyarilari
{
    public const int AyniTutarGun = 3;
    public const int OlaganKat = 10;
    public const int OlaganOrnek = 20;
    public const int OlaganEnAz = 3;
    public const int CekGun = 7;

    public static IReadOnlyList<IslemUyariDto> Denetle(KasaDbContext db, IslemUyariIstegi t, DateOnly bugun)
    {
        var sonuc = new List<IslemUyariDto>();
        var yazilan = t.Cari?.Trim() ?? "";
        var haric = t.HaricId ?? 0;
        var sabitGider = t.Tip == GiderTipi.SabitGider && t.KrediKartiId is null;
        // İşlemler carinin kayıtlı yazımıyla tutulur (IslemHatasi çevirir): geçmiş o yazımla aranır.
        var ad = yazilan.Length == 0 ? null : KayitliAd(db, yazilan, sabitGider);

        if (ad is not null && t.TutarTl > 0)
        {
            var bas = t.Tarih.AddDays(-AyniTutarGun);
            var bit = t.Tarih.AddDays(AyniTutarGun);
            var ayni = db.Islemler.AsNoTracking()
                .Where(i => i.Cari == ad && i.Tarih >= bas && i.Tarih <= bit && i.Id != haric)
                .ToList()
                .Where(i => i.TutarTl == t.TutarTl)
                .OrderBy(i => i.Tarih).ThenBy(i => i.Id)
                .ToList();
            if (ayni.Count > 0)
            {
                var ilk = ayni[0];
                var fazla = ayni.Count > 1 ? $" (+{ayni.Count - 1} kayıt daha)" : "";
                sonuc.Add(new(IslemUyariKodlari.AyniTutar,
                    $"Aynı cariye aynı tutar {AyniTutarGun} gün içinde zaten girilmiş: {Metin.Kisalt(ad)} · {Para(t.TutarTl)} · " +
                    $"{ilk.Tarih:dd.MM.yyyy} · {ilk.Kanal}{fazla}. Aynı fişi iki kez girmediğinizden emin olun."));
            }

            var son = db.Islemler.AsNoTracking()
                .Where(i => i.Cari == ad && i.Id != haric)
                .OrderByDescending(i => i.Tarih).ThenByDescending(i => i.Id)
                .Take(OlaganOrnek)
                .Select(i => i.TutarTl)
                .ToList();
            if (son.Count >= OlaganEnAz && Ortanca(son) is var ortanca && ortanca > 0 && t.TutarTl >= OlaganKat * ortanca)
                sonuc.Add(new(IslemUyariKodlari.OlaganDisiTutar,
                    $"Tutar {Para(t.TutarTl)}, {Metin.Kisalt(ad)} için olağan tutarın yaklaşık {decimal.Floor(t.TutarTl / ortanca):0} katı " +
                    $"(son {son.Count} işlemin ortancası {Para(ortanca)}). Fazladan sıfır girilmiş olabilir."));
        }

        // Geçmişe dönük mü? Kaydedilecek hal (düzenlemede kaydın bugünkü haliyle birlikte) Geçmiş'in kuralıyla sorulur.
        var eski = haric == 0 ? null : db.Islemler.AsNoTracking().FirstOrDefault(i => i.Id == haric);
        var yeniJson = ParaAlanlari(t.Tarih, t.TutarTl, t.Kanal?.Trim() ?? eski?.Kanal, t.Tip, t.KrediKartiId);
        var eskiJson = eski is null ? null : ParaAlanlari(eski.Tarih, eski.TutarTl, eski.Kanal, eski.Tip, eski.KrediKartiId);
        if (GecmiseDonukKurali.Mi(GecmisTurleri.Islem, eskiJson, yeniJson, bugun))
            sonuc.Add(GecmiseDonukKurali.Mi(GecmisTurleri.Islem, null, yeniJson, bugun)
                ? new(IslemUyariKodlari.EskiTarih,
                    $"Tarih kapanmış bir ayda ({t.Tarih:dd.MM.yyyy}). Bu kayıt geçmiş bir ayın rakamlarını, ortakların daha önce gördüğü haftalık ve aylık raporları değiştirecek; Geçmiş'te \"Geçmişe dönük\" işaretlenir.")
                : new(IslemUyariKodlari.EskiTarih,
                    $"Düzenlenen işlem kapanmış bir ayda ({eski!.Tarih:dd.MM.yyyy}). Değişiklik geçmiş bir ayın rakamlarını, ortakların daha önce gördüğü haftalık ve aylık raporları değiştirecek; Geçmiş'te \"Geçmişe dönük\" işaretlenir."));

        if (yazilan.Length > 0 && t.TutarTl > 0)
        {
            var bas = t.Tarih.AddDays(-CekGun);
            var bit = t.Tarih.AddDays(CekGun);
            var cekler = db.Cekler.AsNoTracking()
                .Where(c => c.Yon == CekYonu.Verilen
                            && ((c.Durum == CekDurumu.Odendi && c.IslemTarihi >= bas && c.IslemTarihi <= bit)
                                || (c.Durum == CekDurumu.Portfoyde && c.VadeTarihi >= bas && c.VadeTarihi <= bit)))
                .ToList()
                .Where(c => c.Tutar == t.TutarTl
                            && (Metin.EsitBuyukKucukDuyarsiz.Equals(c.Kisi.Trim(), yazilan)
                                || (ad is not null && Metin.EsitBuyukKucukDuyarsiz.Equals(c.Kisi.Trim(), ad))))
                .OrderBy(c => c.Durum == CekDurumu.Odendi ? 0 : 1).ThenBy(c => c.VadeTarihi).ThenBy(c => c.Id)
                .ToList();
            if (cekler.Count > 0)
            {
                var c = cekler[0];
                var no = string.IsNullOrWhiteSpace(c.CekNo) ? "" : $", çek no {Metin.Kisalt(c.CekNo, 30)}";
                sonuc.Add(new(IslemUyariKodlari.CekCiftDusme, c.Durum == CekDurumu.Odendi
                    ? $"{Metin.Kisalt(c.Kisi)} kişisine aynı tutarda ({Para(c.Tutar)}) bir çek zaten ödenmiş ({c.IslemTarihi:dd.MM.yyyy}{no}). " +
                      "İşlem de girilirse aynı ödeme kasadan iki kez düşer."
                    : $"{Metin.Kisalt(c.Kisi)} kişisine aynı tutarda ({Para(c.Tutar)}) verilmiş, ödenecek bir çek var (vade {c.VadeTarihi:dd.MM.yyyy}{no}). " +
                      "Çek ödendiğinde kasadan ayrıca düşer; işlem de girilirse aynı ödeme iki kez düşer."));
            }
        }
        return sonuc;
    }

    // İşlemin geçmiş satırındaki para alanları (GecmiseDonukKurali'nın okuduğu biçimde: camelCase, tip metin).
    private static string ParaAlanlari(DateOnly tarih, decimal tutarTl, string? kanal, GiderTipi tip, int? krediKartiId)
        => GecmisJson.Yaz(new { Tarih = tarih, TutarTl = tutarTl, Kanal = kanal, Tip = tip, KrediKartiId = krediKartiId });

    /// <summary>Ortanca (çift sayıda değerde ortadaki ikisinin ortalaması).</summary>
    public static decimal Ortanca(IReadOnlyList<decimal> degerler)
    {
        if (degerler.Count == 0) return 0m;
        var s = degerler.Order().ToList();
        var o = s.Count / 2;
        return s.Count % 2 == 1 ? s[o] : (s[o - 1] + s[o]) / 2m;
    }

    /// <summary>Carinin (sabit giderde kalemin) kayıtlı yazımı; kayıtlı değilse null.</summary>
    private static string? KayitliAd(KasaDbContext db, string ad, bool sabitGider)
    {
        var adlar = sabitGider
            ? db.GiderKalemleri.AsNoTracking().Select(k => k.Ad).ToList()
            : db.Cariler.AsNoTracking().Select(c => c.Ad).ToList();
        return adlar.FirstOrDefault(a => a == ad) ?? adlar.FirstOrDefault(a => Metin.EsitBuyukKucukDuyarsiz.Equals(a, ad));
    }

    private static string Para(decimal d) => d.ToString("#,##0.00", Metin.Tr) + " ₺";
}
