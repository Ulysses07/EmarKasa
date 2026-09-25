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

    public static (IslemEntity Kayit, IResult? Hata) Islem(IslemYazDto dto, KasaDbContext db)
    {
        var v = new GirdiDogrulama();
        v.Tarih(dto.Tarih, "tarih");
        v.Metin(dto.Cari, "cari");
        v.Metin(dto.Not, "not", 2000, zorunlu: false);
        v.Para(dto.TutarTl, "tutarTl", negatifOlabilir: true);
        v.Kontrol(Enum.IsDefined(dto.Tip), "tip", "Geçerli bir gider tipi seçin.");
        var kanal = v.Kanal(db, dto.Kanal);
        v.Kart(db, dto.KrediKartiId);
        if (dto.KrediKartiId is { } cardId)
            v.Kontrol(!db.TakipKartlar.Any(k => k.KrediKartiId == cardId && dto.Tarih < k.Baslangic), "tarih", "Kart harcaması kart takip başlangıcından önce olamaz.");
        return (new IslemEntity
        {
            Tarih = dto.Tarih, Cari = dto.Cari?.Trim() ?? "", TutarTl = dto.TutarTl,
            Kanal = kanal?.Ad ?? dto.Kanal?.Trim() ?? "", KanalId = kanal?.Id,
            Tip = dto.KrediKartiId is not null ? GiderTipi.KrediKarti : dto.Tip,
            Not = dto.Not, KrediKartiId = dto.KrediKartiId
        }, v.Sonuc());
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
