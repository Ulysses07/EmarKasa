using System.Security.Claims;
using Kasa.Api.Auth;
using Kasa.Api.Data;

namespace Kasa.Api
{
    public static class AyarEndpoints
    {
        public static WebApplication MapAyarEndpoints(this WebApplication app)
        {
            // Finansal bilgiler yalnız editör ve izleyiciye açıktır.
            var api = app.MapGroup("/api").RequireAuthorization("Finans");

            // Ayarlar
            api.MapGet("/ayarlar", (KasaDbContext db, ClaimsPrincipal u, IzleyiciSifreDurumu izleyiciSifresi, VekilDurumu vekil) =>
            {
                var a = db.Ayarlar.First();
                var editor = u.IsInRole("editor");
                return Results.Ok(new
                {
                    a.TakipBaslangic,
                    a.KasaAcilisDevri,
                    IzleyiciSifreVarMi = a.IzleyiciSifreHash != null,
                    // Yalnız editöre: kayıtlı izleyici şifresinin kurala (12+) uymadığı bir izleyici girişinde görüldüyse true
                    // (hash uzunluk saklamaz) ve güvenilmeyen kaynaktan X-Forwarded-For geldiyse yanlış vekil ayarı uyarısı.
                    IzleyiciSifreKisa = editor && izleyiciSifresi.KisaMi(a.IzleyiciSifreHash),
                    VekilUyarisi = editor ? vekil.Uyari : null,
                    // contract-6: başlangıç/açılış devri formunun sürümü (PUT /api/ayarlar geri gönderir).
                    a.Surum,
                });
            });
            api.MapPut("/ayarlar", (AyarGuncelleDto dto, KasaDbContext db) =>
            {
                using var transaction = db.Database.BeginTransaction();
                var v = new GirdiDogrulama();
                v.Tarih(dto.TakipBaslangic, "takipBaslangic");
                v.Para(dto.KasaAcilisDevri, "kasaAcilisDevri", negatifOlabilir: true);
                if (v.Sonuc() is { } hata)
                    return hata;
                var a = db.Ayarlar.First();
                if (CekirdekSurum.Denetle(dto.Surum, a.Surum, CekirdekSurum.AyarIletisi) is { } eskiSurum)
                    return eskiSurum;
                // gV5: gider üretmeyen mali kayıtlar da (takipli kart, ekstre geliri, kasa sayımı...) başlangıcı sabitler.
                if (a.TakipBaslangic != dto.TakipBaslangic && FinansHesaplari.IlkMaliKayitTuru(db) is { } kayit)
                    return Results.Conflict(new { hata = $"Hareketler kaydedildikten sonra takip başlangıcı değiştirilemez; mevcut dönem bağlantıları korunmalıdır (kayıtlı: {kayit})." });
                a.TakipBaslangic = dto.TakipBaslangic;
                a.KasaAcilisDevri = dto.KasaAcilisDevri;
                db.SaveChanges();
                transaction.Commit();
                return Results.Ok();
            }).RequireAuthorization("Editor");
            api.MapPut("/ayarlar/izleyici-sifre", (IzleyiciSifreDto dto, KasaDbContext db, GuvenlikGunlugu gunluk) =>
            {
                // Kural yalnız belirlerken/değiştirirken uygulanır; mevcut kısa hash ile giriş sürer.
                if (SifreKurallari.YeniSifreHatasi(dto.YeniSifre, "yeniSifre", "İzleyici şifresi") is { } hata)
                    return hata;
                using var seriKilit = gunluk.IslemKilidiAl();
                using var tx = db.Database.BeginTransaction();
                var a = db.Ayarlar.First();
                a.IzleyiciSifreHash = SifreHasher.Hashle(dto.YeniSifre);
                db.SaveChanges();
                gunluk.Yaz(GuvenlikGunlugu.IzleyiciSifresiDegisti, zorunlu: true);
                tx.Commit();
                return Results.Ok();
            }).RequireAuthorization("Editor");
            return app;
        }
    }
}

// Genel ad alanında (Program.cs'den taşındığı gibi): tür adı, uç meta verisindeki gövde türü dahil, değişmez.
/// <param name="Surum">contract-6: istemcinin okuduğu ayarların sürümü (GET /api/ayarlar); uyuşmazsa 409. Eski istemci göndermez (null):
/// denetlenmez, son yazan kazanır.</param>
public record AyarGuncelleDto(DateOnly TakipBaslangic, decimal KasaAcilisDevri, int? Surum = null);
