using System.Globalization;
using Kasa.Api;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>DB'den veriyi yükler, dönem takvimini üretir ve HesapMotoru'nu çağırır. "Bugün" bağlamın
/// saatinden (<c>db.Bugunu()</c>) çağrı başına bir kez okunur: istek dışında da (eşik bildirimi) aynı gün.
/// Okuma salt okunur bir anlık görüntüde yapılır (<see cref="OkumaAnlikGoruntusu"/>): yazma kilidi alınmaz ve
/// Sync çalışmaz. Raporlar Sync'in yazdığı hiçbir şeye bağlı değildir: kart harcamaları ve avans payları yazma
/// yollarının sonunda türetilir; gün dönümünde bakım adımının yazdığı boş kesim ekstreleri kasa hesabına girmez.</summary>
public class HesapServisi
{
    private readonly KasaDbContext _db;
    public HesapServisi(KasaDbContext db) => _db = db;

    /// <summary>Haftalık rapor ve dönem listesinin ileri ufku: bugünden bir yıl sonrasının ay sonu. Daha ileri tarihli
    /// tek bir kayıt (ör. yıl yazım hatası) dönem listesini binlerce döneme uzatamaz; ufkun ötesindeki kayıtlar
    /// silinmez, tarihleri ufka girdikçe rapora girer ve o zamana dek haftalık rapor veri sağlığı uyarısı verir.</summary>
    public static DateOnly IleriUfuk(DateOnly bugun)
    {
        var sinir = bugun.AddYears(1);
        return new(sinir.Year, sinir.Month, DateTime.DaysInMonth(sinir.Year, sinir.Month));
    }

    private record Yuk(
        IReadOnlyList<Kanal> Kanallar,
        IReadOnlyList<Islem> Islemler,
        IReadOnlyList<Gelen> Gelenler,
        IReadOnlyList<Donem> Donemler,
        decimal KasaAcilis,
        IReadOnlyDictionary<string, int?> KanalIdleri,
        string? UfukUyarisi);

    private Yuk Yukle(TakipHesapBaglami takip, DateOnly? raporBitis = null)
    {
        var bugun = takip.Bugun; var ct = takip.Iptal;
        // Tutarlı okuma: alış onayı/ödeme eşleştirmesi rapor okunurken yarım görünmez; yazanlar beklemez.
        using var snapshot = _db.OkumaBaslat();
        ct.ThrowIfCancellationRequested();
        var kartTakip = _db.TakipKartlar.AsNoTracking().ToDictionary(t => t.KrediKartiId);
        var kanallar = _db.Kanallar.AsNoTracking().OrderBy(k => k.Sira).ToList().Select(e => e.ToCore()).ToList();
        var kayitlar = _db.Islemler.AsNoTracking().Include(i => i.KanalKaydi).ToList();
        ct.ThrowIfCancellationRequested();
        var alislar = _db.Alislar.AsNoTracking()
            .Include(a => a.Kalemler).ThenInclude(k => k.Dagilimlar).ThenInclude(d => d.KanalKaydi)
            .Include(a => a.Odemeler).ThenInclude(o => o.Islem).ToList();
        ct.ThrowIfCancellationRequested();
        var eslemeler = alislar.SelectMany(a => a.Odemeler.Select(o => (o.IslemId, Alis: a)))
            .ToDictionary(o => o.IslemId, o => o.Alis);
        var dagilimlar = alislar.ToDictionary(a => a.Id, AlisHesaplari.OdemeDagilimlari);
        var kanalAdlari = _db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
        var aylikOdemeler = _db.AylikGiderOdemeler.AsNoTracking().Where(p => !p.Iptal && p.IslemId != null).ToDictionary(p => p.IslemId!.Value);
        var aylikRevizyonlar = _db.AylikGiderRevizyonlar.AsNoTracking().ToDictionary(r => r.Id);
        var imported = _db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal).ToList();
        var importedExpenses = imported.Where(k => k.IslemId != null).ToDictionary(k => k.IslemId!.Value);
        var dbIslemler = new List<Islem>();
        // Rapora satır veren her kaydın tarihi (birden çok kanala bölünen kayıt bir kez): dönem ufkunu belirler.
        var kayitTarihleri = new List<DateOnly>();
        var sayac = 0;
        foreach (var kayit in kayitlar)
        {
            if (++sayac % 256 == 0) ct.ThrowIfCancellationRequested();
            var onceki = dbIslemler.Count;
            KayitSatirlari(kayit);
            if (dbIslemler.Count > onceki) kayitTarihleri.Add(kayit.Tarih);
        }
        void KayitSatirlari(IslemEntity kayit)
        {
            if (importedExpenses.TryGetValue(kayit.Id, out var importedExpense))
            {
                var source = kayit.ToCore();
                if (importedExpense.DagilimTuru == "Genel") dbIslemler.Add(source with { YalnizGenelKasa = true });
                else foreach (var share in FinansTakipServisi.Read<TakipKanalPayi>(importedExpense.DagilimJson))
                    dbIslemler.Add(source with { Kanal = kanalAdlari[share.KanalId!.Value], TutarTl = share.Tutar });
                return;
            }
            if (aylikOdemeler.TryGetValue(kayit.Id, out var aylikOdeme))
            {
                var revision = aylikRevizyonlar[aylikOdeme.RevizyonId];
                var source = kayit.ToCore() with { AylikGider = true };
                if (revision.DagilimTuru == "Genel") dbIslemler.Add(source with { YalnizGenelKasa = true });
                else foreach (var share in FinansTakipServisi.Read<KanalPayYaz>(revision.DagilimJson))
                    dbIslemler.Add(source with { Kanal = kanalAdlari[share.KanalId], TutarTl = share.Tutar });
                return;
            }
            if (kayit.KrediKartiId is { } cardId && kartTakip.TryGetValue(cardId, out var tracking))
            {
                // Başlangıçtan sonraki kart giderleri takip harcaması olarak izlenir (Sync).
                if (!tracking.EskiKayit || kayit.Tarih >= tracking.Baslangic) return;
                // İşlem tarihi kuralı: başlangıçtan önceki her eski gider eski ay sonu kuralıyla bir
                // kez düşer. Etki tarihi kuralı (ilk sürüm geçişleri) raporları korumak için aynen
                // sürer: eski etkisi başlangıçta/sonrasında olan gider atlanır. Atlanan tutar kart
                // ekranında ve açılış logunda görünür (KartGecisHesabi.IlkSurumKalintisi, aynı koşul).
                if (KartGecisHesabi.IlkSurumdeAtlanir(tracking, kayit.Tarih)) return;
            }
            var islem = kayit.ToCore();
            if (!eslemeler.TryGetValue(kayit.Id, out var alis))
                dbIslemler.Add(islem);
            else if (alis.Durum != "Onaylandi")
                dbIslemler.Add(islem with { Kanal = Kanallar.DagilimBekliyor, DagilimBekliyor = true });
            else
                foreach (var pay in dagilimlar[alis.Id][kayit.Id].Where(p => p.Tutar > 0))
                    dbIslemler.Add(islem with { Kanal = kanalAdlari[pay.KanalId], TutarTl = pay.Tutar });
        }
        ct.ThrowIfCancellationRequested();
        var dbGelenler = _db.Gelenler.AsNoTracking().Include(g => g.KanalKaydi).ToList().Select(e => e.ToCore()).ToList();
        var krediKayitlari = _db.Krediler.AsNoTracking().Include(k => k.KanalKaydi).ToList();
        var krediTakip = _db.TakipKrediler.AsNoTracking().ToDictionary(t => t.KrediId);
        var ayar = _db.Ayarlar.AsNoTracking().First();

        var baslangic = ayar.TakipBaslangic;
        // Dönem ufku yalnız GERÇEKLEŞEN veriye göre (DB işlemleri + bugün). Gelecek kredi
        // taksitleri ufku ileri ÇEKMEZ — aksi halde henüz ödenmemiş taksitler güncel kasadan
        // erken düşerdi (spec: gelecek taksit güncel kasayı etkilemez; ileri aylar o ayın
        // raporu sorulunca yansır). İleri tarihli kayıt ufku en çok IleriUfuk'a kadar uzatır.
        var ekGelirTarihleri = _db.HesapHareketler.AsNoTracking().Where(h => h.IslemId == null && h.GelenId == null && h.KartOdemeId == null && h.KrediId == null).Select(h => h.Tarih).ToList();
        var ufukTarihleri = kayitTarihleri.Concat(ekGelirTarihleri).Concat(imported.Where(k => k.IslemTuru == "Gelir").Select(k => k.Tarih)).ToList();
        var enGecIslem = ufukTarihleri.DefaultIfEmpty(bugun).Max();
        string? ufukUyarisi = null;
        var ufuk = IleriUfuk(bugun);
        if (raporBitis is null && enGecIslem > ufuk)
        {
            var disarida = ufukTarihleri.Where(t => t > ufuk).ToList();
            ufukUyarisi = string.Create(CultureInfo.InvariantCulture,
                $"{disarida.Count} kayıt rapor ufkunun ({ufuk:dd.MM.yyyy}) ötesinde tarihli (en geç {disarida.Max():dd.MM.yyyy}). Tarihleri ufka girene kadar haftalık rapora ve dönem listesine girmez; tarihleri doğrulayın.");
            enGecIslem = ufuk;
        }
        var bitis = raporBitis ?? new[] { bugun, enGecIslem, baslangic }.Max();
        var donemler = DonemUretici.Uret(baslangic, bitis);

        // Krediyi sentetik kayıtlara türet (DB'ye yazılmaz, yalnız motora beslenir):
        // çekim → genel kasa geliri, taksitler → seçilen kanal/Ortak gideri.
        var taksitler = krediKayitlari.Where(k => !k.GerceklesmeTakibi).SelectMany(k =>
            KrediTuretici.TaksitGiderleri(k.ToCore()).Where(t => !krediTakip.TryGetValue(k.Id, out var tracking) || (tracking.EskiKayit && t.Tarih < tracking.Baslangic)));
        var islemler = dbIslemler.Concat(taksitler).ToList();
        var cekimGelenleri = krediKayitlari.Where(k => !krediTakip.TryGetValue(k.Id, out var tracking) || tracking.EskiKayit)
            .Select(k => KrediTuretici.CekimGeleni(k.ToCore(), donemler))
            .Where(g => g is not null)
            .Select(g => g!);
        var gelenler = dbGelenler.Concat(cekimGelenleri).Concat(FinansHesaplari.EkGelirler(_db, donemler)).ToList();
        foreach (var income in imported.Where(k => k.IslemTuru == "Gelir"))
            if (donemler.FirstOrDefault(d => d.Icerir(income.Tarih)) is { } period)
            {
                if (income.DagilimTuru == "Genel") gelenler.Add(new(period.Start, "Genel kasa", income.Tutar, GenelGelir: true));
                else foreach (var share in FinansTakipServisi.Read<TakipKanalPayi>(income.DagilimJson))
                    gelenler.Add(new(period.Start, kanalAdlari[share.KanalId!.Value], share.Tutar));
            }
        // Takipli kredilerin taksitleri tek sorguda okunur (kredi başına sorgu yok).
        var takipliKrediler = krediKayitlari.Where(k => krediTakip.ContainsKey(k.Id)).Select(k => k.Id).ToArray();
        var takipliTaksitler = _db.TakipKrediTaksitler.AsNoTracking().Where(t => !t.Iptal && takipliKrediler.Contains(t.KrediId)).ToList().ToLookup(t => t.KrediId);
        foreach (var loan in krediKayitlari.Where(k => krediTakip.ContainsKey(k.Id)))
        {
            var tracking = krediTakip[loan.Id];
            if (!tracking.MevcutKredi && donemler.FirstOrDefault(d => d.Icerir(loan.CekimTarihi)) is { } period)
                foreach (var share in FinansTakipServisi.Read<KanalPayYaz>(tracking.CekimPaylariJson))
                    gelenler.Add(new(period.Start, kanalAdlari[share.KanalId], share.Tutar, KrediGirisi: true));
            foreach (var installment in takipliTaksitler[loan.Id])
                foreach (var share in FinansTakipServisi.Read<KanalPayYaz>(installment.DagilimJson))
                    islemler.Add(new(installment.Tarih, loan.Ad + " / " + installment.No + ". taksit", share.Tutar, kanalAdlari[share.KanalId], GiderTipi.Cari));
        }
        // Takipli kartın yalnız ödeme kanal payları gerekir: tam kart DTO'su (ekstre, harcama, kalan borç) hesaplanmaz.
        foreach (var cardId in kartTakip.Keys)
            foreach (var payment in FinansTakipServisi.KartOdemeDagilimlari(takip, cardId))
                foreach (var share in payment.Dagilimlar)
                    islemler.Add(new(payment.Tarih, "Kart ödemesi", share.Tutar, share.Kanal, GiderTipi.KrediKarti, payment.Not,
                        DagilimBekliyor: share.KanalId is null, NakitKartOdemesi: true));

        return new Yuk(kanallar, islemler, gelenler, donemler, ayar.KasaAcilisDevri,
            kanalAdlari.ToDictionary(k => k.Value, k => (int?)k.Key), ufukUyarisi);
    }

    /// <summary>Haftalık rapor. Ufkun ötesinde kayıt varsa son dönem <see cref="HaftalikOzet.VeriSagligiUyarisi"/> taşır.</summary>
    public IReadOnlyList<HaftalikOzet> Haftalik(CancellationToken ct = default)
    {
        var y = Yukle(new TakipHesapBaglami(_db, ct));
        ct.ThrowIfCancellationRequested();
        var sonuc = HesapMotoru.HaftalikHesapla(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
        if (y.UfukUyarisi is not { } uyari || sonuc.Count == 0) return sonuc;
        var liste = sonuc.ToList();
        liste[^1] = liste[^1] with { VeriSagligiUyarisi = uyari };
        return liste;
    }

    /// <summary>Açık (kilitli olmayan) aylara ve panelin "bu ay"ına uygulanan aylık rapor kuralı. Ay kapatılırken rapor bu
    /// kuralla dondurulur (<see cref="AyRaporAnlikGoruntusu"/>): kural sonradan değişse de kapatılmış ay değişmez.</summary>
    public const int AcikAyKurali = AylikKural.V1;

    /// <summary>Ayın canlı hesaplanan raporu (kilitli olsa da görüntüye bakmaz). <paramref name="kuralSurumu"/> verilmezse
    /// <see cref="AcikAyKurali"/>.</summary>
    public AylikRapor Aylik(int yil, int ay, CancellationToken ct = default, int? kuralSurumu = null)
    {
        var y = Yukle(new TakipHesapBaglami(_db, ct), new DateOnly(yil, ay, DateTime.DaysInMonth(yil, ay)));
        return HesapMotoru.AylikHesapla(yil, ay, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler, kuralSurumu ?? AcikAyKurali);
    }

    /// <summary>API'nin aylık raporu: kilitli ay dondurulmuş görüntüsünden (<c>"dondurulmus": true</c>), açık ay canlı
    /// hesaplanır.</summary>
    public object AylikYanit(int yil, int ay, CancellationToken ct = default)
    {
        using (_db.OkumaBaslat())
            if (AyRaporAnlikGoruntusu.Oku(_db, yil, ay) is { } dondurulmus) return dondurulmus;
        return Aylik(yil, ay, ct);
    }

    public IReadOnlyList<Donem> Donemler(CancellationToken ct = default) => Yukle(new TakipHesapBaglami(_db, ct)).Donemler;

    public PanelDto Panel(CancellationToken ct = default) => Panel(new TakipHesapBaglami(_db, ct));

    /// <summary>Paneli verilen istek bağlamıyla hesaplar: aynı istekte takip özeti de hesaplanıyorsa kart verisi ve
    /// ödeme etkileri yeniden okunmaz/hesaplanmaz.</summary>
    internal PanelDto Panel(TakipHesapBaglami takip)
    {
        // Gelecek tarihli manuel kayıtlar paneli gelecek bir döneme taşıyamaz.
        var bugun = takip.Bugun;
        var y = Yukle(takip, bugun);
        takip.Iptal.ThrowIfCancellationRequested();
        var haftalik = HesapMotoru.HaftalikHesapla(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
        var son = haftalik.Count > 0 ? haftalik[^1] : null;

        var guncelKasa = son?.KasaDevir ?? y.KasaAcilis;
        var buHafta = son?.KasaSonucu ?? 0m;
        var kanalIdleri = y.KanalIdleri;
        var kanalBakiyeleri = son is not null
            ? son.Kanallar.Select(k => new KanalBakiye(k.Kanal, k.Devir, kanalIdleri.GetValueOrDefault(k.Kanal))).ToList()
            : y.Kanallar.Select(k => new KanalBakiye(k.Ad, k.AcilisDevri, kanalIdleri.GetValueOrDefault(k.Ad))).ToList();

        var buAyRapor = HesapMotoru.AylikHesapla(bugun.Year, bugun.Month, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler, AcikAyKurali);
        var buAy = buAyRapor.Kanallar.Sum(k => k.AySonucu) - buAyRapor.DagilimBekleyenTutar - buAyRapor.GenelGider + buAyRapor.GenelGelir;

        return new PanelDto(guncelKasa, kanalBakiyeleri, buHafta, buAy,
            haftalik.Sum(h => h.DagilimBekleyenTutar));
    }
}
