using System.Globalization;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

/// <summary>Eski karttan yeni takibe geçişin sistemce bilinen toplamları.</summary>
/// <param name="SistemKartBorcu">Açılış borcu + eski kart giderleri − eski kart ödemeleri (eski ekranın kart borcu).</param>
/// <param name="EskiKuraldaIslenenTutar">İşlem tarihi başlangıçtan önce ve eski etkisi başlangıçtan önce olan, yani
/// başlangıçtan önce eski kuralla kasadan düşen/düşecek giderler.</param>
/// <param name="BekleyenEskiDusumTutari">İşlem tarihi başlangıçtan önce, eski ay sonu etkisi başlangıçta veya sonrasında olan giderler.</param>
/// <param name="SonBekleyenDusumTarihi">Bekleyen eski düşümlerin en geç ay sonu; bekleyen yoksa null.</param>
/// <param name="OnerilenKasadaSayilanTutar">Devir borcunun ödendiğinde kasadan düşmemesi gereken kısmı.</param>
/// <param name="BaslangicaKadarDusecekTutar">EskiKuraldaIslenenTutar'ın bugünden sonra, başlangıçtan önceki ay
/// sonlarında düşecek (henüz düşmemiş) kısmı; bugün verilmezse 0.</param>
public record KartGecisOzeti(decimal SistemKartBorcu, decimal EskiKuraldaIslenenTutar, decimal BekleyenEskiDusumTutari,
    DateOnly? SonBekleyenDusumTarihi, decimal OnerilenKasadaSayilanTutar, decimal BaslangicaKadarDusecekTutar = 0);

/// <summary>İlk sürüm (etki tarihi) kuralıyla yapılmış kart geçişinin tespiti; veri yazmaz, dönüştürmez.</summary>
/// <param name="RaporDisiTutar">Başlangıçtan önceki, eski ay sonu düşümü başlangıçta/sonrasında olduğu için raporlara hiç girmeyen giderler (P).</param>
/// <param name="SistemKartBorcu">Başlangıçtaki eski sistem borcu (S): açılış borcu + başlangıçtan önceki kart giderleri − eski kart ödemeleri.</param>
/// <param name="KalanBorc">Geçişte girilen kalan borç (R, iptal edilmemiş devir harcaması; yoksa 0).</param>
/// <param name="KasadaOncedenSayilanTutar">Geçişte girilen kasada önceden sayılan tutar (K).</param>
/// <param name="TahminiKasaFarki">K − (min(R, S) − P), önizlemedeki genel kasa farkıyla aynı yönde: artı kasadan hiç düşmeyecek
/// (kasa fazla görünür), eksi ödemede ikinci kez düşecek (açılış borcu kasadan ayrıca ödenmediyse), 0 toplam etki tutarlı.</param>
public record IlkSurumKalintisi(int KartId, string KartAdi, DateOnly Baslangic, decimal RaporDisiTutar, DateOnly? IlkDusumTarihi, DateOnly? SonDusumTarihi,
    decimal SistemKartBorcu, decimal KalanBorc, decimal KasadaOncedenSayilanTutar, decimal AcilisBorcu, decimal TahminiKasaFarki);

/// <summary>
/// Kart geçişinin yan etkisiz (yazmayan) hesabı. Önizleme, onay ve eski modeli emekliye
/// ayıracak otomatik dönüşüm aynı hesabı kullanır.
/// </summary>
public static class KartGecisHesabi
{
    /// <summary>Geçişin yazdığı eski borç devri harcamasının açıklaması; devir bununla (ve kaynaksız, gidersiz, başlangıç
    /// tarihli olmasıyla) tanınır. Kullanıcı harcamasında bu açıklama kullanılamaz.</summary>
    public const string DevirAciklamasi = "Onaylanan eski borç devri";

    /// <summary>Kartın iptal edilmemiş eski borç devri (Id sırasıyla ilki); geçişli olmayan kartta ya da devir yoksa null.
    /// Devir düzeltmesi eskisini iptal edip aynı tarih ve açıklamayla yenisini yazar: etkin devir her zaman tektir.</summary>
    public static TakipHarcamaEntity? AktifDevir(IQueryable<TakipHarcamaEntity> harcamalar, TakipKartEntity takip) => !takip.EskiKayit ? null
        : harcamalar.Where(h => h.KrediKartiId == takip.KrediKartiId && !h.Iptal && h.IslemId == null && h.KaynakHarcamaId == null
            && h.Tarih == takip.Baslangic && h.Aciklama == DevirAciklamasi).OrderBy(h => h.Id).FirstOrDefault();

    /// <summary>Geçişli kartın devir düzeltmesinde (finance-2) kasada önceden sayılan tutarın sınırları; geçiş önizlemesiyle
    /// aynı kural, ancak yalnız başlangıçtan önceki eski kayıtlarla (geçişten sonraki kart giderleri takip harcamasıdır).
    /// İşlem tarihi kuralında önerilen = max(0, min(R, S)); ilk sürüm (etki tarihi) kuralında raporlara hiç girmeyen eski
    /// düşüm (P) ödemede de düşmelidir: önerilen = max(0, min(R, S) − P) (<see cref="IlkSurumKalintisi"/> ile aynı).
    /// En az = max(0, önerilen − max(0, açılış borcu)).</summary>
    public static (decimal SistemKartBorcu, decimal RaporDisiTutar, decimal AcilisBorcu, decimal Onerilen, decimal EnAz) DevirOnerisi(KasaDbContext db, TakipKartEntity takip, decimal kalanBorc)
    {
        var card = db.KrediKartlari.AsNoTracking().Single(k => k.Id == takip.KrediKartiId);
        var old = db.Islemler.AsNoTracking().Where(i => i.KrediKartiId == card.Id && i.Tarih < takip.Baslangic).ToList();
        var system = card.Borc + old.Sum(i => i.TutarTl) - db.KartOdemeler.AsNoTracking().Where(o => o.KrediKartiId == card.Id).ToList().Sum(o => o.Tutar);
        decimal pending = 0;
        if (takip.EskiDusumKurali == EskiDusumKurali.EtkiTarihi)
        {
            var rows = old.Where(i => IlkSurumdeAtlanir(takip, i.Tarih)).ToList();
            var ids = rows.Select(i => i.Id).ToList();
            var handled = db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal && k.IslemId != null && ids.Contains(k.IslemId.Value)).Select(k => k.IslemId!.Value).ToList()
                .Concat(db.AylikGiderOdemeler.AsNoTracking().Where(p => !p.Iptal && p.IslemId != null && ids.Contains(p.IslemId.Value)).Select(p => p.IslemId!.Value).ToList()).ToHashSet();
            pending = rows.Where(i => !handled.Contains(i.Id)).Sum(i => i.TutarTl);
        }
        var onerilen = Math.Max(0, Math.Min(kalanBorc, system) - pending);
        return (system, pending, card.Borc, onerilen, Math.Max(0, onerilen - Math.Max(0, card.Borc)));
    }

    // Önerilen K = max(0, min(kalan borç, sistem kart borcu)). Yeni kuralda başlangıçtan önceki her
    // eski gider eski ay sonu kuralıyla bir kez düşer; bankadaki kalan borcun bu giderlerden gelen
    // kısmı ödendiğinde ikinci kez düşmemelidir. Açılış borcu (card.Borc) eski modelde hiçbir zaman
    // kasadan düşmedi: önerilen tutar onu da "sayılmış" kabul eder ve eski kasa sonucunu korur.
    // Kalan borç sistem borcunu aşarsa aşan kısım (kaydı olmayan faiz/masraf) ödendiğinde düşer.
    public static KartGecisOzeti Hesapla(KasaDbContext db, int kartId, DateOnly baslangic, decimal kalanBorc, DateOnly? bugun = null)
    {
        var card = db.KrediKartlari.AsNoTracking().Single(k => k.Id == kartId);
        var expenses = db.Islemler.AsNoTracking().Where(i => i.KrediKartiId == kartId).ToList();
        var payments = db.KartOdemeler.AsNoTracking().Where(o => o.KrediKartiId == kartId).ToList();
        var system = card.Borc + expenses.Sum(i => i.TutarTl) - payments.Sum(o => o.Tutar);
        var old = expenses.Where(i => i.Tarih < baslangic).Select(i => (i.TutarTl, Etki: EskiEtkiTarihi(i))).ToList();
        var processed = old.Where(x => x.Etki < baslangic).ToList();
        var pending = old.Where(x => x.Etki >= baslangic).ToList();
        return new(system, processed.Sum(x => x.TutarTl), pending.Sum(x => x.TutarTl),
            pending.Count == 0 ? null : pending.Max(x => x.Etki), Math.Max(0, Math.Min(kalanBorc, system)),
            bugun is { } today ? processed.Where(x => x.Etki > today).Sum(x => x.TutarTl) : 0);
    }

    /// <summary>K'nın alt sınırı. Önerilenin altındaki kısım ödendiğinde kasadan düşer; eski kuralla düşmüş/düşecek
    /// borç için bu ikinci düşümdür. Tek meşru istisna, eski modelde kasadan hiç düşmemiş açılış borcunun
    /// kasadan ayrıca ödenmesidir.</summary>
    public static decimal EnAzKasadaSayilanTutar(KartGecisOzeti ozet, decimal acilisBorcu) =>
        Math.Max(0, ozet.OnerilenKasadaSayilanTutar - Math.Max(0, acilisBorcu));

    /// <summary>HesapMotoru ile aynı kural: kart gideri sonraki ayın son günü, diğer tipler kendi tarihinde kasaya düşer.</summary>
    public static DateOnly EskiEtkiTarihi(IslemEntity islem) => islem.Tip != GiderTipi.KrediKarti ? islem.Tarih : SonrakiAySonu(islem.Tarih);

    /// <summary>İlk sürüm (etki tarihi) kuralında raporun atladığı eski kart kaydı (2.3.0 davranışı; HesapServisi
    /// aynı koşulu kullanır): işlem tarihi başlangıçtan önce, sonraki ay sonu başlangıçta veya sonrasında. Kural
    /// tipten bağımsız sonraki ay sonuna bakar; kartla ödenmiş cari gider de atlanır.</summary>
    public static bool IlkSurumdeAtlanir(TakipKartEntity takip, DateOnly tarih) =>
        takip.EskiKayit && takip.EskiDusumKurali == EskiDusumKurali.EtkiTarihi && tarih < takip.Baslangic && SonrakiAySonu(tarih) >= takip.Baslangic;

    /// <summary>İlk sürüm kuralıyla geçirilmiş kartın tespiti; rapor dışı düşüm yoksa ve girilen tutarlar tutarlıysa null.
    /// İlk sürümde tutarlı K = min(R, S) − P idi (bekleyen düşüm yalnız ödemede kasaya girebiliyordu); geçiş formu
    /// bunu hesaplamadığından K elle (web'de varsayılan 0) girildi. Otomatik düzeltilmez: doğru tutar ancak
    /// banka/kasa kayıtlarıyla doğrulanabilir.</summary>
    public static IlkSurumKalintisi? IlkSurumKalintisi(KasaDbContext db, TakipKartEntity takip)
    {
        if (!takip.EskiKayit || takip.EskiDusumKurali != EskiDusumKurali.EtkiTarihi) return null;
        var card = db.KrediKartlari.AsNoTracking().Single(k => k.Id == takip.KrediKartiId);
        var old = db.Islemler.AsNoTracking().Where(i => i.KrediKartiId == card.Id && i.Tarih < takip.Baslangic).ToList();
        var rows = old.Where(i => IlkSurumdeAtlanir(takip, i.Tarih)).ToList();
        if (rows.Count > 0)
        {
            // HesapServisi PDF içe aktarma ve aylık gider ödemesine bağlı giderleri kart kuralından önce işler.
            var ids = rows.Select(i => i.Id).ToList();
            var handled = db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal && k.IslemId != null && ids.Contains(k.IslemId.Value)).Select(k => k.IslemId!.Value).ToList()
                .Concat(db.AylikGiderOdemeler.AsNoTracking().Where(p => !p.Iptal && p.IslemId != null && ids.Contains(p.IslemId.Value)).Select(p => p.IslemId!.Value).ToList()).ToHashSet();
            rows = rows.Where(i => !handled.Contains(i.Id)).ToList();
        }
        // Eski kart ödemeleri geçişten sonra eklenemez/silinemez; devir, ilk sürümün yazdığı harcamadır.
        var system = card.Borc + old.Sum(i => i.TutarTl) - db.KartOdemeler.AsNoTracking().Where(o => o.KrediKartiId == card.Id).ToList().Sum(o => o.Tutar);
        var transfer = AktifDevir(db.TakipHarcamalar.AsNoTracking(), takip);
        var pending = rows.Sum(i => i.TutarTl);
        var (debt, counted) = (transfer?.Tutar ?? 0, transfer?.KasadaOncedenSayilanTutar ?? 0);
        var difference = counted - (Math.Min(debt, system) - pending);
        if (pending == 0 && difference == 0) return null;
        return new(card.Id, card.Ad, takip.Baslangic, pending, rows.Count == 0 ? null : rows.Min(i => SonrakiAySonu(i.Tarih)), rows.Count == 0 ? null : rows.Max(i => SonrakiAySonu(i.Tarih)),
            system, debt, counted, card.Borc, difference);
    }

    public static List<IlkSurumKalintisi> IlkSurumKalintilari(KasaDbContext db) =>
        db.TakipKartlar.AsNoTracking().Where(t => t.EskiKayit && t.EskiDusumKurali == EskiDusumKurali.EtkiTarihi).OrderBy(t => t.KrediKartiId).ToList()
            .Select(t => IlkSurumKalintisi(db, t)).Where(k => k is not null).Select(k => k!).ToList();

    public static string Uyari(IlkSurumKalintisi k)
    {
        var text = $"İlk sürüm kuralıyla geçiş ({Tarih(k.Baslangic)}): " + (k.RaporDisiTutar != 0
            ? $"başlangıçtan önceki kart giderlerinin {Tl(k.RaporDisiTutar)} tutarındaki eski ay sonu düşümü ({Tarih(k.IlkDusumTarihi!.Value)}–{Tarih(k.SonDusumTarihi!.Value)}) raporlara girmiyor. "
            : "raporlara girmeyen eski ay sonu düşümü yok. ")
            + $"Geçişte girilen kalan borç {Tl(k.KalanBorc)}, kasada önceden sayılan {Tl(k.KasadaOncedenSayilanTutar)} ve sistem kart borcu {Tl(k.SistemKartBorcu)} ile ";
        text += k.TahminiKasaFarki switch
        {
            > 0 => $"{Tl(k.TahminiKasaFarki)} kasadan hiçbir zaman düşmüyor: kasa tahminen bu kadar fazla görünüyor.",
            < 0 => $"{Tl(-k.TahminiKasaFarki)} eski kuralla düşülmüş borç ödendiğinde kasadan ikinci kez düşüyor"
                + (k.AcilisBorcu > 0 ? $" (en çok {Tl(k.AcilisBorcu)} kadarı kasadan ayrıca ödenen açılış borcu olabilir)." : "."),
            _ => "bu tutar ödemede kasadan düşer: toplam kasa etkisi tutarlı, yalnız düşüş tarihi ay sonu yerine ödeme tarihidir.",
        };
        return text + " Tutarları banka/kasa kayıtlarıyla doğrulayın.";
    }

    /// <summary>Eski kayıttan geçirilmiş kartın denetim izi ve ilk sürüm tespiti; yeni açılan kartta null.</summary>
    public static KartGecisDto? Gecis(KasaDbContext db, TakipKartEntity takip)
    {
        if (!takip.EskiKayit) return null;
        var kalinti = IlkSurumKalintisi(db, takip);
        return new(takip.EskiDusumKurali.ToString(), takip.GecisAciklamasi,
            takip.GecisOzetiJson is { } json ? JsonSerializer.Deserialize<KartGecisKaydi>(json) : null,
            kalinti?.RaporDisiTutar ?? 0, kalinti?.IlkDusumTarihi, kalinti?.SonDusumTarihi, kalinti?.TahminiKasaFarki ?? 0, kalinti is null ? null : Uyari(kalinti));
    }

    internal static string Tl(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")) + " TL";
    internal static string Tarih(DateOnly value) => value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    private static DateOnly SonrakiAySonu(DateOnly tarih)
    {
        var next = tarih.AddMonths(1);
        return new(next.Year, next.Month, DateTime.DaysInMonth(next.Year, next.Month));
    }
}
