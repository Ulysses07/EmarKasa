using Kasa.Api.Data;
using Kasa.Api.Servisler;

namespace Kasa.Api;

public static class IslemEndpoints
{
    public static WebApplication MapIslemEndpoints(this WebApplication app)
    {
        // Finansal bilgiler yalnız editör ve izleyiciye açıktır.
        var api = app.MapGroup("/api").RequireAuthorization("Finans");

        // Islemler
        api.MapGet("/islemler", (DateOnly? baslangic, DateOnly? bitis, string? kanal, string? cari, IslemListeServisi svc) =>
            svc.Liste(baslangic, bitis, kanal, cari));
        api.MapPost("/islemler", (IslemYazDto dto, KasaDbContext db) =>
        {
            using var transaction = db.Database.BeginTransaction();
            if (KayitGirdileri.IslemTekrari(dto, db) is { } tekrar)
                return tekrar;
            var (e, hata) = KayitGirdileri.Islem(dto, db);
            if (hata is not null)
                return hata;
            // finance-9: takipli karta eksi/sıfır gider kaynaksız alacak olurdu; iade Kredi Kartları ekranındaki akıştan girilir.
            if (FinansHesaplari.TakipliKartIadeHatasi(dto, db) is { } iade)
                return iade;
            db.Islemler.Add(e);
            db.SaveChanges();
            KayitGirdileri.IslemIstegiKaydet(dto, db, e.Id);
            // Takipli kart giderinin harcaması taksit planıyla hemen yazılır (gap-coklu-giris-cift-sayim-mutabakat-6); taksitsizde Sync'in
            // tek taksitli kaydıyla aynıdır.
            // (Takipli kartta takip başlangıcından önceki gider yukarıda reddedilir.)
            if (e.KrediKartiId is { } kart && db.TakipKartlar.Any(t => t.KrediKartiId == kart))
                FinansTakipServisi.KaynakHarcamaEkle(db, e, dto.TaksitSayisi ?? 1, dto.IlkKesimTarihi);
            FinansTakipServisi.Sync(db);
            transaction.Commit();
            return Results.Created($"/api/islemler/{e.Id}", e);
        }).RequireAuthorization("Editor");
        api.MapPut("/islemler/{id:int}", (int id, IslemYazDto dto, KasaDbContext db) =>
        {
            using var transaction = db.Database.BeginTransaction();
            if (db.HesapHareketler.Any(h => h.IslemId == id) || db.KrediTaksitOdemeler.Any(o => o.IslemId == id))
                return Results.Conflict(new { hata = "Hesap veya krediye bağlı hareket genel gider ekranından değiştirilemez." });
            if (db.AlisOdemeler.Any(o => o.IslemId == id))
                return Results.Conflict(new { hata = "Bu gider bir alışa bağlı. Kanal dağılımını Alışlar ekranından düzenleyin; ödeme tutarı ve tarihi burada değiştirilemez." });
            var e = db.Islemler.Find(id);
            if (e is null)
                return Results.NotFound();
            // contract-6: istemcinin okuduğu gider arada (başka oturum ya da dolaylı yazım) değiştiyse eski değerler geri yazılmaz.
            if (CekirdekSurum.Denetle(dto.Surum, e.Surum, CekirdekSurum.GiderIletisi) is { } eskiSurum)
                return eskiSurum;
            // gap-coklu-giris-cift-sayim-mutabakat-5: takipli kart giderinin kart harcaması ödenmemiş, iadesiz ve ekstreye bağsızsa açıklama,
            // not ve kanal düzeltilir; tarih, tutar ve kart harcamanın taksit planıdır, değişmez.
            var harcama = FinansTakipServisi.KaynakHarcama(db, id);
            if (harcama is not null)
            {
                if (dto.Tarih != e.Tarih || dto.TutarTl != e.TutarTl || dto.KrediKartiId != e.KrediKartiId)
                    return Results.Conflict(new { hata = "Kart takibindeki giderin tarihi, tutarı ve kartı değiştirilemez. Kart harcaması ödenmediyse gideri silip doğru bilgilerle yeniden girin; ödendiyse Kredi Kartları ekranında açıklamalı iade girin." });
                if (FinansTakipServisi.KaynakHarcamaEngeli(db, harcama) is { } engel)
                    return Results.Conflict(new { hata = KartGideriEngeli(engel, "değiştirilemez") });
            }
            else if (FinansTakipServisi.IslemYonetiliyor(db, e) || (dto.KrediKartiId is { } newCard && db.TakipKartlar.Any(t => t.KrediKartiId == newCard)))
                return Results.Conflict(new { hata = "Kart takibine bağlı hareket için Kredi Kartları ekranından açıklamalı iade/düzeltme girin." });
            var (gelen, hata) = KayitGirdileri.Islem(dto, db, e);
            if (hata is not null)
                return hata;
            gelen.Id = id;
            var kanalDegisti = e.Kanal != gelen.Kanal || e.KanalId != gelen.KanalId;
            db.Entry(e).CurrentValues.SetValues(gelen);
            if (harcama is not null)
            {
                // Kanal değişince harcamanın dondurulmuş payı giderle aynı kuralla yeniden yazılır (ödeme payı yoktur).
                harcama.Aciklama = e.Cari;
                if (kanalDegisti)
                    harcama.DagilimJson = FinansTakipServisi.Json(FinansTakipServisi.DonmusPaylar(db, e));
                db.TakipKartlar.Single(t => t.KrediKartiId == harcama.KrediKartiId).Surum++;
            }
            db.SaveChanges();
            transaction.Commit();
            return Results.Ok(e);
        }).RequireAuthorization("Editor");
        api.MapDelete("/islemler/{id:int}", (int id, KasaDbContext db) =>
        {
            using var transaction = db.Database.BeginTransaction();
            if (db.HesapHareketler.Any(h => h.IslemId == id) || db.KrediTaksitOdemeler.Any(o => o.IslemId == id))
                return Results.Conflict(new { hata = "Hesap veya krediye bağlı hareket genel gider ekranından silinemez." });
            if (db.AlisOdemeler.Any(o => o.IslemId == id))
                return Results.Conflict(new { hata = "Alışa bağlı ödeme silinemez; alış ve kasa bağlantısı korunmalıdır." });
            var e = db.Islemler.Find(id);
            if (e is null)
                return Results.NotFound();
            // gap-coklu-giris-cift-sayim-mutabakat-5: ödenmemiş, iadesiz ve ekstreye bağsız kart harcamasının gideri harcamayla birlikte kalkar
            // (harcama iptal edilir, taksitleri borçtan çıkar); diğer takipli kart giderleri için açıklamalı iade yolu kalır.
            var harcama = FinansTakipServisi.KaynakHarcama(db, id);
            if (harcama is not null)
            {
                if (FinansTakipServisi.KaynakHarcamaEngeli(db, harcama) is { } engel)
                    return Results.Conflict(new { hata = KartGideriEngeli(engel, "silinemez") });
                harcama.Iptal = true;
                harcama.IslemId = null;
                db.TakipKartlar.Single(t => t.KrediKartiId == harcama.KrediKartiId).Surum++;
                db.SaveChanges();
            }
            else if (FinansTakipServisi.IslemYonetiliyor(db, e))
                return Results.Conflict(new { hata = "Kart takibine bağlı hareket silinemez; açıklamalı iade girin." });
            db.Islemler.Remove(e);
            db.SaveChanges();
            transaction.Commit();
            return Results.NoContent();
        }).RequireAuthorization("Editor");
        return app;
    }

    private static string KartGideriEngeli(string engel, string islem) => engel switch
    {
        "ödendi" => $"Bu kart harcaması ödendi; gideri {islem}: önceki kart ödemesinin kanal payı değişirdi. Harcama gerçekleşmediyse Kredi Kartları ekranında açıklamalı iade girin.",
        "iadesi var" => $"Bu kart harcamasının iadesi var; gideri {islem}. Önce iadeyi Kredi Kartları ekranında gerekçeyle iptal edin.",
        _ => $"Bu kart harcaması bir ekstre satırıyla eşleştirildi; gideri {islem}. Önce PDF İçe Aktarma bölümünden eşleştirmeyi gerekçeyle iptal edin.",
    };
}
