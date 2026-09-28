using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api;

public static class KayitGirdileri
{
    public static (KrediEntity Kayit, IResult? Hata) Kredi(KrediYazDto dto, KasaDbContext db)
    {
        var v = new GirdiDogrulama();
        v.Metin(dto.Ad, "ad");
        v.Tarih(dto.CekimTarihi, "cekimTarihi");
        v.Para(dto.CekilenTutar, "cekilenTutar");
        v.Para(dto.AylikOdeme, "aylikOdeme");
        v.Kontrol(dto.OdemeGunu is >= 1 and <= 31, "odemeGunu", "Ödeme günü 1 ile 31 arasında olmalıdır.");
        v.Kontrol(dto.TaksitSayisi is >= 1 and <= 600, "taksitSayisi", "Taksit sayısı 1 ile 600 arasında olmalıdır.");
        var kanal = v.Kanal(db, dto.Kanal);
        var e = new KrediEntity
        {
            Ad = dto.Ad?.Trim() ?? "", CekilenTutar = dto.CekilenTutar, CekimTarihi = dto.CekimTarihi,
            TaksitSayisi = dto.TaksitSayisi, AylikOdeme = dto.AylikOdeme, OdemeGunu = dto.OdemeGunu,
            Kanal = kanal?.Ad ?? dto.Kanal?.Trim() ?? "", KanalId = kanal?.Id
        };
        if (v.Sonuc() is { } hata) return (e, hata);
        try { KrediTuretici.Dogrula(e.ToCore()); }
        catch (ArgumentException)
        {
            v.Kontrol(false, "cekimTarihi", "Kredi taksitleri desteklenen tarih aralığını aşıyor.");
        }
        return (e, v.Sonuc());
    }

    /// <param name="mevcut">Düzenlenen kayıt (PUT); yeni kayıtta null.</param>
    public static (IslemEntity Kayit, IResult? Hata) Islem(IslemYazDto dto, KasaDbContext db, IslemEntity? mevcut = null)
    {
        var v = new GirdiDogrulama();
        v.Tarih(dto.Tarih, "tarih");
        // Temiz başlangıç (spec §2): takip başlangıcından önceki gider hiçbir döneme girmez; haftalık kasada yok, aylık
        // raporda var olurdu. Gelir, alış ödemesi, aylık gider ve ekstre yolları gibi yeni kayıt ve tarih değişikliği
        // reddedilir. Kural öncesinden kalan kayıt tarihi değişmeden düzenlenebilir; raporlardaki yeri değişmez.
        var baslangic = db.Ayarlar.Select(a => a.TakipBaslangic).First();
        if (dto.Tarih != default)
            v.Kontrol(dto.Tarih >= baslangic || mevcut?.Tarih == dto.Tarih, "tarih", $"Gider tarihi takip başlangıcından ({baslangic:dd.MM.yyyy}) önce olamaz.");
        v.Metin(dto.Cari, "cari");
        v.Metin(dto.Not, "not", 2000, zorunlu: false);
        v.Para(dto.TutarTl, "tutarTl", negatifOlabilir: true);
        v.Kontrol(Enum.IsDefined(dto.Tip), "tip", "Geçerli bir gider tipi seçin.");
        var kanal = v.Kanal(db, dto.Kanal);
        v.Kart(db, dto.KrediKartiId);
        if (dto.KrediKartiId is { } cardId)
            v.Kontrol(!db.TakipKartlar.Any(k => k.KrediKartiId == cardId && dto.Tarih < k.Baslangic), "tarih", "Kart harcaması kart takip başlangıcından önce olamaz.");
        TakipliKartKurali(v, db, dto.KrediKartiId is not null ? GiderTipi.KrediKarti : dto.Tip, dto.KrediKartiId, mevcut);
        if (mevcut is null) TaksitKurali(v, db, dto.TaksitSayisi, dto.IlkKesimTarihi, dto.Tarih, dto.KrediKartiId, mevcutKayit: false);
        else v.Kontrol(dto.TaksitSayisi is null && dto.IlkKesimTarihi is null, "taksitSayisi",
            "Taksit planı düzenlemede değiştirilemez; kart harcaması ödenmediyse gideri silip taksitle yeniden girin.");
        return (new IslemEntity
        {
            Tarih = dto.Tarih, Cari = dto.Cari?.Trim() ?? "", TutarTl = dto.TutarTl,
            Kanal = kanal?.Ad ?? dto.Kanal?.Trim() ?? "", KanalId = kanal?.Id,
            Tip = dto.KrediKartiId is not null ? GiderTipi.KrediKarti : dto.Tip,
            Not = dto.Not, KrediKartiId = dto.KrediKartiId
        }, v.Sonuc());
    }

    private const string IslemOlusturTuru = "IslemOlustur";

    /// <summary>
    /// POST /api/islemler tekrar anahtarı (appcore-5), yazma transaction'ı içinde kayıttan önce: istemci 15 sn'de isteği kesip
    /// aynı gideri yeniden gönderdiğinde aynı istek kimliği ve aynı içerik ilk gideri 200 ile döndürür; farklı içerik 409,
    /// boş kimlik 400 alır. İstek kimliği göndermeyen eski istemci için null (her istek yeni gider).
    /// </summary>
    public static IResult? IslemTekrari(IslemYazDto dto, KasaDbContext db)
        => dto.IstekId is { } istekId
            ? FinansHesaplari.Tekrar(db, istekId, IslemOlusturTuru, IslemOzeti(dto), id => db.Islemler.Find(id) is { } kayit
                ? Results.Ok(kayit)
                : AlisEndpoints.Conflict("Bu istekle oluşturulan gider sonradan silinmiş. Gideri yeniden kaydetmek için formu yenileyin."))
            : null;

    /// <summary>Kayıttan sonra, aynı transaction'da: istek kimliğini oluşan giderle saklar.</summary>
    public static void IslemIstegiKaydet(IslemYazDto dto, KasaDbContext db, int islemId)
    {
        if (dto.IstekId is not { } istekId) return;
        FinansHesaplari.IstekKaydet(db, istekId, IslemOlusturTuru, IslemOzeti(dto), islemId);
        db.SaveChanges();
    }

    /// <summary>İsteğin özeti. Sonradan eklenen taksit alanları yalnız doluyken katılır: taksitsiz isteğin özeti alanlar eklenmeden
    /// önceki kayıt biçimiyle (aynı alanlar, aynı sıra) birebir aynıdır.</summary>
    private static string IslemOzeti(IslemYazDto dto)
    {
        var ozet = FinansHesaplari.Ozet(new { dto.Tarih, dto.Cari, dto.TutarTl, dto.Kanal, dto.Tip, dto.Not, dto.KrediKartiId, IstekId = (Guid?)null });
        return dto.TaksitSayisi is null && dto.IlkKesimTarihi is null ? ozet : FinansHesaplari.Ozet(new { ozet, dto.TaksitSayisi, dto.IlkKesimTarihi });
    }

    /// <summary>gap-coklu-giris-cift-sayim-mutabakat-6 iletisi: taksit alanları yalnız yeni takipteki kartla girilen yeni harcamada.</summary>
    public const string TaksitYalnizTakipliKartta = "Taksit yalnız yeni takipteki kartla girilen yeni kart harcamasında seçilir.";

    /// <summary>
    /// Taksit girdisi (gap-coklu-giris-cift-sayim-mutabakat-6): kartlı yeni alış ödemesi ve kartlı yeni genel gider kart harcamasının
    /// taksit sayısını (1–60) ve isteğe bağlı ilk kesim tarihini taşır. Alanlar yalnız yeni takipteki kartla YENİ harcama üretilirken
    /// kabul edilir: mevcut gider ya da kart harcaması bağlanırken (<paramref name="mevcutKayit"/>) plan bağlanan kaydındır. İlk kesim
    /// harcamadan önce olamaz ve kartın kesim gününe en çok <see cref="FinansTakipServisi.IlkKesimToleransi"/> gün uzak olmalıdır
    /// (Kredi Kartları ekranındaki harcamayla aynı kural). Alan göndermeyen (eski) istemci tek taksitle devam eder.
    /// </summary>
    public static void TaksitKurali(GirdiDogrulama v, KasaDbContext db, int? taksitSayisi, DateOnly? ilkKesim, DateOnly tarih, int? kartId, bool mevcutKayit)
    {
        if (taksitSayisi is null && ilkKesim is null) return;
        v.Kontrol(taksitSayisi is null or (>= 1 and <= 60), "taksitSayisi", "Taksit sayısı 1 ile 60 arasında olmalı.");
        if (mevcutKayit) { v.Kontrol(false, "taksitSayisi", "Mevcut gider ya da kart harcaması bağlanırken taksit girilmez; taksit planı bağlanan kaydındır."); return; }
        var kart = kartId is { } id && db.TakipKartlar.Any(t => t.KrediKartiId == id) ? db.KrediKartlari.SingleOrDefault(k => k.Id == id) : null;
        if (kart is null) { v.Kontrol(false, "taksitSayisi", TaksitYalnizTakipliKartta); return; }
        if (ilkKesim is not { } ilk) return;
        v.Tarih(ilk, "ilkKesimTarihi");
        if (ilk == default) return;
        v.Kontrol(ilk >= tarih, "ilkKesimTarihi", "İlk kesim tarihi harcamadan önce olamaz.");
        if (FinansTakipServisi.IlkKesimHatasi(db, kart, ilk) is { } hata) v.Kontrol(false, "ilkKesimTarihi", hata);
    }

    /// <summary>K3 iletisi (yeni kredi kartı gideri takipsiz karta ya da kartsız kaydedilemez).</summary>
    public const string TakipliKartZorunlu = "Kredi kartı gideri için yeni takipteki bir kart seçin. Kart eski takipteyse önce kart ekranından yeni takibe geçirin.";

    /// <summary>
    /// K3 (kullanıcı kararı, 2026-09-27): YENİ kredi kartı gideri (Tip=KrediKarti; nakit kart ödemesi değil) yeni takipteki
    /// bir karta bağlanmak zorundadır. Kartsız ya da eski (takipsiz) karta bağlı gider eski "sonraki ay sonunda kasadan
    /// düşme" kuralına düşerdi: hiçbir kart borcuna, ekstreye ve bildirime bağlanmaz, ödemesi takipli karttan girilirse
    /// ikinci kez düşerdi. Kredi kartı gideri üreten bütün yazma yolları (genel gider, alış ödemesi ve düzeltmesi) bu
    /// kuralı çağırır; aylık gider ve ekstre içe aktarma K.K gideri üretmez (kart ekstresi takipteki kartın harcamasıdır).
    /// Mevcut kayıt (<paramref name="mevcut"/>) aynı tip ve aynı kartla kaldıkça kural uygulanmaz: geçmiş kartsız/eski kartlı
    /// kayıtlar aynen kalır, tutar/not/tarih düzeltmesi engellenmez. Yeni kullanıma kapatılmış takipli kart için
    /// <see cref="GirdiDogrulama.Kart"/>'ın kendi iletisi korunur.
    /// </summary>
    public static void TakipliKartKurali(GirdiDogrulama v, KasaDbContext db, GiderTipi tip, int? kartId, IslemEntity? mevcut)
    {
        if (tip != GiderTipi.KrediKarti) return;
        if (mevcut is { Tip: GiderTipi.KrediKarti } && mevcut.KrediKartiId == kartId) return;
        v.Kontrol(kartId is { } id && db.TakipKartlar.Any(t => t.KrediKartiId == id), "krediKartiId", TakipliKartZorunlu);
    }

    public static (KrediKartiEntity Kayit, IResult? Hata) Kart(KrediKartiYazDto dto)
    {
        var v = new GirdiDogrulama();
        v.Metin(dto.Ad, "ad");
        v.Tarih(dto.KesimTarihi, "kesimTarihi");
        v.Tarih(dto.SonOdemeTarihi, "sonOdemeTarihi");
        v.Para(dto.Limit, "limit");
        v.Para(dto.Borc, "borc", negatifOlabilir: true);
        return (new KrediKartiEntity
        {
            Ad = dto.Ad?.Trim() ?? "", KesimTarihi = dto.KesimTarihi, SonOdemeTarihi = dto.SonOdemeTarihi,
            Limit = dto.Limit, Borc = dto.Borc
        }, v.Sonuc());
    }

    public static (KartOdemeEntity Kayit, IResult? Hata) KartOdeme(KartOdemeYazDto dto, KasaDbContext db)
    {
        var v = new GirdiDogrulama();
        v.Kart(db, dto.KrediKartiId, zorunlu: true);
        v.Tarih(dto.Tarih, "tarih");
        v.Para(dto.Tutar, "tutar");
        v.Kontrol(dto.Tutar > 0, "tutar", "Ödeme tutarı sıfırdan büyük olmalıdır.");
        v.Metin(dto.Not, "not", 2000, zorunlu: false);
        return (new KartOdemeEntity
        {
            KrediKartiId = dto.KrediKartiId, Tarih = dto.Tarih, Tutar = dto.Tutar, Not = dto.Not
        }, v.Sonuc());
    }
}
