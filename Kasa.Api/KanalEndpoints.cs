using Kasa.Api.Data;

namespace Kasa.Api;

public static class KanalEndpoints
{
    public static WebApplication MapKanalEndpoints(this WebApplication app)
    {
        // Finansal bilgiler yalnız editör ve izleyiciye açıktır.
        var api = app.MapGroup("/api").RequireAuthorization("Finans");

        // Kanallar
        api.MapGet("/kanallar", (KasaDbContext db) => db.Kanallar.OrderBy(k => k.Sira).ToList());
        api.MapPost("/kanallar", (KanalYazDto dto, KasaDbContext db) =>
        {
            var v = new GirdiDogrulama();
            v.Metin(dto.Ad, "ad");
            v.Para(dto.AcilisDevri, "acilisDevri", negatifOlabilir: true);
            v.Kontrol(!GirdiDogrulama.AyrilmisKanalAdi(dto.Ad?.Trim() ?? ""), "ad", "Bu ad sistem tarafından kullanılıyor.");
            if (v.Sonuc() is { } hata)
                return hata;
            var ad = dto.Ad!.Trim();
            if (db.Kanallar.AsEnumerable().Any(k => string.Equals(k.Ad, ad, StringComparison.OrdinalIgnoreCase)))
                return Results.Conflict(new { hata = "Bu kanal adı zaten kullanılıyor." });
            // Kilit varken yeni kanal açılış devri 0 ile eklenir (aktif de olabilir): tamamlanmış ayların kanal kümesi değişiklikten önce
            // dondurulur, kapanmış ayların Ortak dağılımı değişmez (AyKanalKumesi, KanalKurallari).
            using var transaction = db.Database.BeginTransaction();
            if (KanalKurallari.AdEngeli(db, null, ad) is { } engel)
                return Results.Conflict(new { hata = engel });
            var e = new KanalEntity { Ad = ad, Aktif = dto.Aktif, Sira = dto.Sira, AcilisDevri = dto.AcilisDevri };
            db.Kanallar.Add(e);
            db.SaveChanges();
            transaction.Commit();
            return Results.Created($"/api/kanallar/{e.Id}", e);
        }).RequireAuthorization("Editor");
        api.MapPut("/kanallar/{id:int}", (int id, KanalYazDto gelen, KasaDbContext db) =>
        {
            var e = db.Kanallar.Find(id);
            if (e is null)
                return Results.NotFound();
            // contract-6: istemcinin okuduğu kanal arada değiştiyse (sürüm gönderen istemci) üzerine yazılmaz.
            if (CekirdekSurum.Denetle(gelen.Surum, e.Surum, CekirdekSurum.KanalIletisi) is { } eskiSurum)
                return eskiSurum;
            var v = new GirdiDogrulama();
            v.Metin(gelen.Ad, "ad");
            v.Para(gelen.AcilisDevri, "acilisDevri", negatifOlabilir: true);
            v.Kontrol(!GirdiDogrulama.AyrilmisKanalAdi(gelen.Ad?.Trim() ?? ""), "ad", "Bu ad sistem tarafından kullanılıyor.");
            if (v.Sonuc() is { } hata)
                return hata;
            var ad = gelen.Ad!.Trim();
            if (db.Kanallar.AsEnumerable().Any(k => k.Id != id && string.Equals(k.Ad, ad, StringComparison.OrdinalIgnoreCase)))
                return Results.Conflict(new { hata = "Bu kanal adı zaten kullanılıyor." });

            // Ad, aktiflik ve sıra kilit varken de değişir (tamamlanmış ayların kanal kümesi değişiklikten önce dondurulur; bkz.
            // AyKanalKumesi); açılış devri kilitte değişmez (KanalKurallari). Kayıtlardaki kanal metni yalnız yeni ada eşitlenir (etiket
            // senkronu: aylık gider/ekstre kaynak kuralına ve dönem kilidine takılmaz).
            using var transaction = db.Database.BeginTransaction();
            if (KanalKurallari.AdEngeli(db, id, ad) is { } engel)
                return Results.Conflict(new { hata = engel });
            KanalKurallari.EtiketleriGuncelle(db, id, ad);
            e.Ad = ad;
            e.Aktif = gelen.Aktif;
            e.Sira = gelen.Sira;
            e.AcilisDevri = gelen.AcilisDevri;
            db.SaveChanges();
            transaction.Commit();
            return Results.Ok(e);
        }).RequireAuthorization("Editor");
        api.MapDelete("/kanallar/{id:int}", (int id, KasaDbContext db) =>
        {
            // Denetim ve silme tek transaction'da: arada yazılan hareket kısıt hatasına düşmez. Kasa alt sınırı kanalla silinir. Geçmişi
            // olan ya da tamamlanmış bir ayın kanal kümesinde yer alan kanal silinmez (KanalKurallari.SilmeEngeli).
            using var transaction = db.Database.BeginTransaction();
            var e = db.Kanallar.Find(id);
            if (e is null)
                return Results.NotFound();
            if (KanalKurallari.SilmeEngeli(db, e) is { } engel)
                return Results.Conflict(new { hata = engel });
            KanalKurallari.Sil(db, e);
            transaction.Commit();
            return Results.NoContent();
        }).RequireAuthorization("Editor");
        return app;
    }
}
