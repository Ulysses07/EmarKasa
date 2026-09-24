using System.Security.Claims;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.GuvenlikUcYardimci;

namespace Kasa.Api;

/// <summary>
/// Kayda soru: izleyici (ve editör) bir işleme, haftaya, çeke ya da genel bir soru yazar; editör
/// cevaplar, kapatır, yeniden açar ya da siler. Liste ve cevaplar herkese açıktır. Sorular ayrı
/// tablodadır: hiçbir kasa rakamına dokunmaz. İzleyicinin yazabildiği tek şey soru eklemektir.
/// </summary>
public static class SoruEndpoints
{
    public const string SoruPolitikasi = "Soru";
    public const int MetinEnCok = 2000;
    public const int CevapEnCok = 4000;
    /// <summary>Bir kişinin aynı anda açık tutabileceği en fazla soru (yanlışlıkla çoğaltmaya karşı).</summary>
    public const int KisiBasinaAcikEnCok = 50;

    public static RouteGroupBuilder MapSoruUclari(this RouteGroupBuilder api)
    {
        api.MapGet("/sorular", (string? durum, SoruHedefTuru? hedefTur, int? hedefId, DateOnly? hafta, KasaDbContext db) =>
        {
            var q = db.Sorular.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(durum))
            {
                if (!Enum.TryParse<SoruDurumu>(durum, ignoreCase: true, out var d) || !Enum.IsDefined(d))
                    return HataYaniti("Geçersiz durum (Acik ya da Kapali).");
                q = q.Where(s => s.Durum == d);
            }
            if (hedefTur is { } t) q = q.Where(s => s.HedefTur == t);
            if (hedefId is { } id) q = q.Where(s => s.HedefId == id);
            if (hafta is { } h) q = q.Where(s => s.Hafta == h);
            return Results.Ok(q.OrderBy(s => s.Durum).ThenByDescending(s => s.Id).Take(1000).ToList().Select(SoruDto.Olustur).ToList());
        });

        api.MapGet("/sorular/ozet", (KasaDbContext db) =>
        {
            var acik = db.Sorular.AsNoTracking().Where(s => s.Durum == SoruDurumu.Acik);
            return new SoruOzetDto(acik.Count(), acik.Count(s => s.Cevap == null),
                acik.OrderByDescending(s => s.Id).Take(5).ToList().Select(SoruDto.Olustur).ToList());
        });

        api.MapPost("/sorular", (SoruEkleDto dto, HttpContext http, KasaDbContext db, KullaniciOnbellegi kullanicilar, TimeProvider saat) =>
        {
            var metin = dto.Metin?.Trim() ?? "";
            if (metin.Length == 0) return HataYaniti("Soru boş olamaz.");
            if (metin.Length > MetinEnCok) return HataYaniti($"Soru en fazla {MetinEnCok} karakter olabilir.");
            if (!Enum.IsDefined(dto.HedefTur)) return HataYaniti("Geçersiz soru hedefi.");

            var (hedefId, hafta, ozet, hata) = Hedef(db, dto, saat);
            if (hata is not null) return hata;

            var u = http.User;
            var soranId = kullanicilar.HesapId(db, u);
            var rol = u.FindFirstValue(ClaimTypes.Role) ?? "";
            var ad = KimlikBilgisi.Ad(http) ?? (rol == Roller.Izleyici ? "Ortak (izleyici)" : "Editör");
            var acikSayisi = soranId is { } sid
                ? db.Sorular.Count(s => s.Durum == SoruDurumu.Acik && s.SoranId == sid)
                : db.Sorular.Count(s => s.Durum == SoruDurumu.Acik && s.SoranId == null && s.SoranRol == rol);
            if (acikSayisi >= KisiBasinaAcikEnCok)
                return HataYaniti($"En fazla {KisiBasinaAcikEnCok} açık sorunuz olabilir; cevaplananlar kapanınca yenisini sorabilirsiniz.");

            var e = new SoruEntity
            {
                HedefTur = dto.HedefTur, HedefId = hedefId, Hafta = hafta, HedefOzet = ozet, Metin = metin,
                SoranId = soranId, SoranAd = ad, SoranRol = rol, SorulmaUtc = saat.GetUtcNow().UtcDateTime,
                Durum = SoruDurumu.Acik,
            };
            db.Sorular.Add(e);
            db.SaveChanges();
            return Results.Created($"/api/sorular/{e.Id}", SoruDto.Olustur(e));
        }).RequireAuthorization(SoruPolitikasi);

        api.MapPost("/sorular/{id:int}/cevap", (int id, SoruCevapDto dto, HttpContext http, KasaDbContext db, TimeProvider saat) =>
        {
            var e = db.Sorular.Find(id);
            if (e is null) return Results.NotFound();
            var cevap = dto.Cevap?.Trim() ?? "";
            if (cevap.Length == 0) return HataYaniti("Cevap boş olamaz.");
            if (cevap.Length > CevapEnCok) return HataYaniti($"Cevap en fazla {CevapEnCok} karakter olabilir.");
            var simdi = saat.GetUtcNow().UtcDateTime;
            e.Cevap = cevap;
            e.CevaplayanAd = KimlikBilgisi.Ad(http) ?? "Editör";
            e.CevaplanmaUtc = simdi;
            if (dto.Kapat)
            {
                e.Durum = SoruDurumu.Kapali;
                e.KapanmaUtc = simdi;
            }
            db.SaveChanges();
            return Results.Ok(SoruDto.Olustur(e));
        }).RequireAuthorization("Editor");

        api.MapPost("/sorular/{id:int}/kapat", (int id, KasaDbContext db, TimeProvider saat) =>
        {
            var e = db.Sorular.Find(id);
            if (e is null) return Results.NotFound();
            if (e.Durum == SoruDurumu.Kapali) return Results.Ok(SoruDto.Olustur(e));
            e.Durum = SoruDurumu.Kapali;
            e.KapanmaUtc = saat.GetUtcNow().UtcDateTime;
            db.SaveChanges();
            return Results.Ok(SoruDto.Olustur(e));
        }).RequireAuthorization("Editor");

        api.MapPost("/sorular/{id:int}/ac", (int id, KasaDbContext db) =>
        {
            var e = db.Sorular.Find(id);
            if (e is null) return Results.NotFound();
            if (e.Durum == SoruDurumu.Acik) return Results.Ok(SoruDto.Olustur(e));
            e.Durum = SoruDurumu.Acik;
            e.KapanmaUtc = null;
            db.SaveChanges();
            return Results.Ok(SoruDto.Olustur(e));
        }).RequireAuthorization("Editor");

        api.MapDelete("/sorular/{id:int}", (int id, KasaDbContext db) =>
        {
            var e = db.Sorular.Find(id);
            if (e is null) return Results.NotFound();
            db.Sorular.Remove(e);
            db.SaveChanges();
            return Results.NoContent();
        }).RequireAuthorization("Editor");

        return api;
    }

    /// <summary>Hedefi doğrular ve soru anındaki kısa özetini çıkarır.</summary>
    private static (int? HedefId, DateOnly? Hafta, string? Ozet, IResult? Hata) Hedef(KasaDbContext db, SoruEkleDto dto, TimeProvider saat)
    {
        switch (dto.HedefTur)
        {
            case SoruHedefTuru.Genel:
                return (null, null, null, null);
            case SoruHedefTuru.Islem:
            {
                if (dto.HedefId is not { } id) return (null, null, null, HataYaniti("Hangi işlem? (hedefId gerekli)"));
                var i = db.Islemler.AsNoTracking().FirstOrDefault(x => x.Id == id);
                if (i is null) return (null, null, null, HataYaniti("İşlem bulunamadı; silinmiş olabilir."));
                return (id, null, $"{i.Tarih:dd.MM.yyyy} · {i.Cari} · {Tutar(i.TutarTl)} ₺ · {i.Kanal}", null);
            }
            case SoruHedefTuru.Cek:
            {
                if (dto.HedefId is not { } id) return (null, null, null, HataYaniti("Hangi çek? (hedefId gerekli)"));
                var c = db.Cekler.AsNoTracking().FirstOrDefault(x => x.Id == id);
                if (c is null) return (null, null, null, HataYaniti("Çek bulunamadı; silinmiş olabilir."));
                var yon = c.Yon == CekYonu.Alinan ? "Alınan" : "Verilen";
                return (id, null, $"{yon} çek · {c.Kisi} · {Tutar(c.Tutar)} ₺ · vade {c.VadeTarihi:dd.MM.yyyy}", null);
            }
            case SoruHedefTuru.Hafta:
            {
                if (dto.Hafta is not { } gun) return (null, null, null, HataYaniti("Hangi hafta? (hafta gerekli)"));
                var takip = db.Ayarlar.AsNoTracking().OrderBy(a => a.Id).Select(a => a.TakipBaslangic).First();
                var bitis = Takvim.Bitis(takip, Saat.Bugun(saat));
                if (gun > bitis || Takvim.DonemBaslangici(gun, takip) is not { } bas)
                    return (null, null, null, HataYaniti("Bu tarih takvimin dışında."));
                var ayinSonu = new DateOnly(bas.Year, bas.Month, 1).AddMonths(1).AddDays(-1);
                var pazar = Takvim.Pazartesi(bas).AddDays(6);
                var son = pazar < ayinSonu ? pazar : ayinSonu;
                return (null, bas, $"Hafta {bas:dd.MM} – {son:dd.MM.yyyy}", null);
            }
            default:
                return (null, null, null, HataYaniti("Geçersiz soru hedefi."));
        }
    }

    private static string Tutar(decimal t) => t.ToString("#,##0.00", Metin.Tr);
}
