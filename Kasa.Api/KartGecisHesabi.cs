using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

/// <summary>Eski karttan yeni takibe geçişin sistemce bilinen toplamları.</summary>
/// <param name="SistemKartBorcu">Açılış borcu + eski kart giderleri − eski kart ödemeleri (eski ekranın kart borcu).</param>
/// <param name="EskiKuraldaIslenenTutar">İşlem tarihi başlangıçtan önce ve eski etkisi başlangıçtan önce kasaya işlenmiş giderler.</param>
/// <param name="BekleyenEskiDusumTutari">İşlem tarihi başlangıçtan önce, eski ay sonu etkisi başlangıçta veya sonrasında olan giderler.</param>
/// <param name="SonBekleyenDusumTarihi">Bekleyen eski düşümlerin en geç ay sonu; bekleyen yoksa null.</param>
/// <param name="OnerilenKasadaSayilanTutar">Devir borcunun ödendiğinde kasadan düşmemesi gereken kısmı.</param>
public record KartGecisOzeti(decimal SistemKartBorcu, decimal EskiKuraldaIslenenTutar, decimal BekleyenEskiDusumTutari,
    DateOnly? SonBekleyenDusumTarihi, decimal OnerilenKasadaSayilanTutar);

/// <summary>
/// Kart geçişinin yan etkisiz (yazmayan) hesabı. Önizleme, onay ve eski modeli emekliye
/// ayıracak otomatik dönüşüm aynı hesabı kullanır.
/// </summary>
public static class KartGecisHesabi
{
    // Önerilen K = max(0, min(kalan borç, sistem kart borcu)). Yeni kuralda başlangıçtan önceki her
    // eski gider eski ay sonu kuralıyla bir kez düşer; bankadaki kalan borcun bu giderlerden gelen
    // kısmı ödendiğinde ikinci kez düşmemelidir. Açılış borcu (card.Borc) eski modelde hiçbir zaman
    // kasadan düşmedi: önerilen tutar onu da "sayılmış" kabul eder ve eski kasa sonucunu korur.
    // Kalan borç sistem borcunu aşarsa aşan kısım (kaydı olmayan faiz/masraf) ödendiğinde düşer.
    public static KartGecisOzeti Hesapla(KasaDbContext db, int kartId, DateOnly baslangic, decimal kalanBorc)
    {
        var card = db.KrediKartlari.AsNoTracking().Single(k => k.Id == kartId);
        var expenses = db.Islemler.AsNoTracking().Where(i => i.KrediKartiId == kartId).ToList();
        var payments = db.KartOdemeler.AsNoTracking().Where(o => o.KrediKartiId == kartId).ToList();
        var system = card.Borc + expenses.Sum(i => i.TutarTl) - payments.Sum(o => o.Tutar);
        var old = expenses.Where(i => i.Tarih < baslangic).Select(i => (i.TutarTl, Etki: EskiEtkiTarihi(i))).ToList();
        var pending = old.Where(x => x.Etki >= baslangic).ToList();
        return new(system, old.Where(x => x.Etki < baslangic).Sum(x => x.TutarTl), pending.Sum(x => x.TutarTl),
            pending.Count == 0 ? null : pending.Max(x => x.Etki), Math.Max(0, Math.Min(kalanBorc, system)));
    }

    /// <summary>HesapMotoru ile aynı kural: kart gideri sonraki ayın son günü, diğer tipler kendi tarihinde kasaya düşer.</summary>
    public static DateOnly EskiEtkiTarihi(IslemEntity islem)
    {
        if (islem.Tip != GiderTipi.KrediKarti) return islem.Tarih;
        var next = islem.Tarih.AddMonths(1);
        return new(next.Year, next.Month, DateTime.DaysInMonth(next.Year, next.Month));
    }
}
