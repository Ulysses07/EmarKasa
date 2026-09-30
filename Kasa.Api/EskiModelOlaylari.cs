using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

/// <summary>
/// Takipsiz (geçişi yapılmamış) eski kart ve kredilerin hatırlatma olayları (gap-tarihsel-spec-ve-emekli-web-7). 2.0 öncesi
/// masaüstü hatırlatıcısı (docs/specs/2026-07-15-kart-odeme-hatirlatici-design.md) bütün kartlar için kesim, son ödemeye 3 gün
/// kala ve son ödeme günü hatırlatması üretiyordu; o istemci kaldırılınca sunucunun olay üreticisi yalnız takipli kayıtları
/// dolaştığından geçişi yapılmamış kartlar hiçbir kanaldan hatırlatma almıyordu. Olaylar eski modelin kendi hesabından üretilir:
/// veri dönüştürülmez, takip kaydı oluşturulmaz, kasa ve rapor hesabına dokunulmaz.
/// - Kart: ekstre borcu eski kart listesinin (GET /api/kredikartlari) türetmesidir (<see cref="EkstreBorc"/>): güncel borç
///   (açılış + harcamalar − ödemeler) eksi kesimden sonraki harcamalar. Kapı 07-15 spec'indeki gibidir: ekstre borcu &gt; 0 ve
///   kesimden sonra karta ödeme girilmemiş (kısmi ödeme dönemi "ele alınmış" sayar). Açık ekstre bugüne en yakın geçmiş kesimdir;
///   önceki ekstrenin son ödeme günü henüz geçmediyse (kesim ve son ödeme aynı gün) o da hatırlatılır. Olaylar takipli kartınkiyle
///   aynı türdedir: kesim günü "Kesim", son ödeme günü "SonOdeme" (bildirim 3 gün kala ve günün kendisinde); kalem kimliği 0.
/// - Kredi: eski planın taksitleri (<see cref="FinansTakipServisi.Kredi(TakipHesapBaglami,int)"/>); taksit eski modelde de
///   tarihinde kasaya işlenir, gerçekleşme takipli eski kredide işlenmez (OtomatikKasa false).
/// Olay adı kaydın takipte olmadığını söyler (<see cref="Ek"/>) ve <see cref="TakipOlayDto.EskiModel"/> işaretlidir. Olaylar yalnız
/// bildirim hattına girer (<see cref="FinansTakipServisi.GetNotificationEvents"/>, <see cref="Servisler.FinansBildirimKaynaklari"/>);
/// takip özeti (ana sayfa "yaklaşan ödemeler") değişmez. Takipte olmayan kayıtlar ana sayfada kalıcı uyarıyla listelenir
/// (<see cref="TakipsizKayitlar"/>).
/// </summary>
public static class EskiModelOlaylari
{
    /// <summary>Takipsiz kayıt olayının ad eki.</summary>
    public const string Ek = " (eski model; geçiş yapılmadı)";

    /// <summary>Eski kartın ekstre borcu (GET /api/kredikartlari EkstreBorc; 07-15 spec'i): güncel borç eksi kesimden sonraki harcamalar.</summary>
    internal static decimal EkstreBorc(decimal guncelBorc, IEnumerable<(DateOnly Tarih, decimal Tutar)> harcamalar, DateOnly kesim)
        => guncelBorc - harcamalar.Where(h => h.Tarih > kesim).Sum(h => h.Tutar);

    /// <summary>Takip kaydı olmayan kartların kimlikleri (Id sırası).</summary>
    internal static List<int> TakipsizKartlar(KasaDbContext db) =>
        db.KrediKartlari.AsNoTracking().Where(k => !db.TakipKartlar.Any(t => t.KrediKartiId == k.Id)).OrderBy(k => k.Id).Select(k => k.Id).ToList();
    /// <summary>Takip kaydı olmayan kredilerin kimlikleri (Id sırası).</summary>
    internal static List<int> TakipsizKrediler(KasaDbContext db) =>
        db.Krediler.AsNoTracking().Where(k => !db.TakipKrediler.Any(t => t.KrediId == k.Id)).OrderBy(k => k.Id).Select(k => k.Id).ToList();

    /// <summary>Bütün takipsiz kartların, sonra takipsiz kredilerin olayları (Id sırası).</summary>
    internal static List<TakipOlayDto> Hepsi(TakipHesapBaglami b) =>
        [.. TakipsizKartlar(b.Db).SelectMany(id => Kart(b, id)), .. TakipsizKrediler(b.Db).SelectMany(id => Kredi(b, id))];

    /// <summary>Takipsiz kartın açık ekstre(ler)inin kesim ve son ödeme olayları; kapıdan geçmeyen ekstre olay üretmez.</summary>
    internal static List<TakipOlayDto> Kart(TakipHesapBaglami b, int kartId)
    {
        b.Iptal.ThrowIfCancellationRequested();
        var db = b.Db;
        var bugun = b.Bugun;
        var kart = db.KrediKartlari.AsNoTracking().Single(k => k.Id == kartId);
        var harcamalar = db.Islemler.AsNoTracking().Where(i => i.KrediKartiId == kartId).Select(i => new { i.Tarih, i.TutarTl }).ToList()
            .Select(i => (i.Tarih, i.TutarTl)).ToList();
        var odemeler = db.KartOdemeler.AsNoTracking().Where(o => o.KrediKartiId == kartId).Select(o => new { o.Tarih, o.Tutar }).ToList();
        var guncel = kart.Borc + harcamalar.Sum(h => h.TutarTl) - odemeler.Sum(o => o.Tutar);
        int kesimGunu = kart.KesimTarihi.Day, odemeGunu = kart.SonOdemeTarihi.Day;
        var kesim = KartDonem.SonKesim(kesimGunu, bugun);
        var onceki = KartDonem.SonKesim(kesimGunu, kesim.AddDays(-1));
        var sonuc = new List<TakipOlayDto>();
        foreach (var k in FinansTakipServisi.Vade(onceki, odemeGunu) >= bugun ? [onceki, kesim] : new[] { kesim })
        {
            var borc = EkstreBorc(guncel, harcamalar, k);
            if (borc <= 0 || odemeler.Any(o => o.Tarih > k))
                continue;
            sonuc.Add(new(TakipKaynaklari.Kart, kart.Id, 0, kart.Ad + Ek, k, borc, TakipOlayTurleri.Kesim, false) { EskiModel = true });
            sonuc.Add(new(TakipKaynaklari.Kart, kart.Id, 0, kart.Ad + Ek, FinansTakipServisi.Vade(k, odemeGunu), borc, TakipOlayTurleri.SonOdeme, false) { EskiModel = true });
        }
        return sonuc;
    }

    /// <summary>Takipsiz kredinin eski plandaki taksit olayları (takipli krediyle aynı kural: her taksit).</summary>
    internal static List<TakipOlayDto> Kredi(TakipHesapBaglami b, int krediId)
    {
        var otomatik = !b.Db.Krediler.AsNoTracking().Where(k => k.Id == krediId).Select(k => k.GerceklesmeTakibi).Single();
        return FinansTakipServisi.KrediOlaylari(FinansTakipServisi.Kredi(b, krediId))
            .Select(e => e with { Ad = e.Ad + Ek, OtomatikKasa = otomatik, EskiModel = true }).ToList();
    }

    /// <summary>Ana sayfanın kalıcı uyarısı için takipte olmayan kayıtlar: geçişi yapılmamış bütün kartlar (yeni gider ve güncel
    /// ekstre ancak geçişle mümkün) ve kalan taksidi olan eski krediler (planı geçersiz kredinin kalanı bilinmez: listede kalır).
    /// Kayıt yoksa null (alan yazılmaz).</summary>
    public static IReadOnlyList<TakipsizKayitDto>? TakipsizKayitlar(TakipHesapBaglami b)
    {
        var db = b.Db;
        var bugun = b.Bugun;
        var liste = db.KrediKartlari.AsNoTracking().Where(k => !db.TakipKartlar.Any(t => t.KrediKartiId == k.Id)).OrderBy(k => k.Id)
            .Select(k => new { k.Id, k.Ad }).ToList().Select(k => new TakipsizKayitDto(TakipKaynaklari.Kart, k.Id, k.Ad)).ToList();
        foreach (var k in db.Krediler.AsNoTracking().Where(k => !db.TakipKrediler.Any(t => t.KrediId == k.Id)).OrderBy(k => k.Id).ToList())
            if (KrediSuruyor(k, bugun))
                liste.Add(new(TakipKaynaklari.Kredi, k.Id, k.Ad));
        return liste.Count == 0 ? null : liste;
    }

    private static bool KrediSuruyor(KrediEntity kredi, DateOnly bugun)
    {
        try
        { return KrediTuretici.TaksitGiderleri(kredi.ToCore()).Any(t => t.Tarih > bugun); }
        catch (ArgumentException) { return true; }
    }
}
