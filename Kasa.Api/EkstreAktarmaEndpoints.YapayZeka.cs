using Kasa.Api.Data;
using Kasa.Api.YapayZeka;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api;

/// <summary>Yapay zekânın durumu: açık mı (değilse nedeni), İstanbul ayının harcaması ve sınırı, önerisi sonuçlanan satırlardan türü aynı kaydedilenler ve
/// belge türü başına son ölçüm.</summary>
public record YapayZekaDurumDto(bool Acik, string? KapaliNedeni, string Model, decimal AyHarcamasiUsd, decimal AylikSinirUsd, int AyCagri, int Sonuclanan, int AyniTur,
    IReadOnlyList<YapayZekaOlcumDto> Olcumler);
/// <summary>Belgeden gönderilecek maskeli satırlar (gönderilmeden). <paramref name="KalanSatir"/>: sonraki çağrıya kalan.</summary>
public record YapayZekaOnizlemeDto(int SatirSayisi, int KalanSatir, int OrnekSayisi, IReadOnlyList<string> Satirlar, decimal EnYuksekMaliyetUsd);
public record YapayZekaSorYaz(Guid IstekId);
/// <summary>Çağrının sonucu. <paramref name="Not"/>: yanıt kullanılamadıysa nedeni.</summary>
public record YapayZekaSonucDto(int Sorulan, int Onerilen, int KalanSatir, decimal MaliyetUsd, string? Not);
public record YapayZekaOlcumYaz(Guid IstekId, string Kaynak);
/// <summary>Gizlenen geçmiş kararlarla ölçüm: tür ve dağılım doğruluğu, yüksek güvenli önerilerin doğruluğu, öneri gelmeyen satır ve maliyet.</summary>
public record YapayZekaOlcumDto(string Kaynak, int Satir, int TurDogru, int DagilimSatiri, int DagilimDogru, int YuksekGuven, int YuksekDogru, int Onerisiz, decimal MaliyetUsd, DateTimeOffset Zaman);

// Yapay zekâ önerileri (docs/specs/2026-11-25-yapay-zeka-onerileri.md). Çağrı veritabanı işleminin dışında yapılır: önce maliyet ayrılır, sonra sonuç yazılır.
public static partial class EkstreAktarmaEndpoints
{
    private static void MapYapayZeka(RouteGroupBuilder api)
    {
        api.MapGet("/yapay-zeka", (KasaDbContext db, YapayZekaServisi yz) => Safe(() => AlisEndpoints.Oku(db, () => Results.Ok(YapayZekaDurumu(db, yz)))));
        api.MapGet("/{id:int}/yapay-zeka", (int id, KasaDbContext db, YapayZekaServisi yz) => Safe(() => AlisEndpoints.Oku(db, () =>
        {
            var h = YapayZekaHazirla(db, yz, GetDocument(db, id));
            return Results.Ok(new YapayZekaOnizlemeDto(h.Satirlar.Sum(s => s.Satirlar.Count), h.Kalan, h.OrnekSayisi, h.Satirlar.Select(s => s.Yazi).ToList(),
                h.Istek is null ? 0 : YapayZekaAyarlari.EnYuksekMaliyet(h.Istek.Karakter)));
        })));
        api.MapPost("/{id:int}/yapay-zeka", YapayZekaSor).RequireRateLimiting("guvenlik");
        api.MapPost("/yapay-zeka/olcum", YapayZekaOlc).RequireRateLimiting("guvenlik");
    }

    private static YapayZekaServisi.Hazirlik YapayZekaHazirla(KasaDbContext db, YapayZekaServisi yz, EkstreBelgeEntity belge)
    {
        Require(yz.Ayarlar.KapaliNedeni is null, yz.Ayarlar.KapaliNedeni ?? "", 409);
        Require(belge.Kaynak != EkstreKaynaklari.Erp, "ERP12 satırları yapay zekâya sorulmaz.", 409);
        return YapayZekaServisi.Hazirla(db, belge);
    }

    private static YapayZekaDurumDto YapayZekaDurumu(KasaDbContext db, YapayZekaServisi yz)
    {
        var ay = yz.Ay();
        var (sonuclanan, ayni) = YapayZekaServisi.Dogruluk(db);
        var olcumler = db.YapayZekaCagrilar.AsNoTracking().Where(c => c.Tur == YapayZekaCagriTurleri.Olcum && c.SonucJson != null).OrderByDescending(c => c.Id).Select(c => c.SonucJson!).ToList()
            .Select(j => System.Text.Json.JsonSerializer.Deserialize<YapayZekaOlcumDto>(j, System.Text.Json.JsonSerializerOptions.Web)!).DistinctBy(o => o.Kaynak).ToList();
        return new(yz.Ayarlar.KapaliNedeni is null, yz.Ayarlar.KapaliNedeni, YapayZekaAyarlari.Model, YapayZekaServisi.AyHarcamasi(db, ay), yz.Ayarlar.AylikSinirUsd,
            db.YapayZekaCagrilar.Count(c => c.Ay == ay), sonuclanan, ayni, olcumler);
    }

    /// <summary>İstek kimliğiyle önceki çağrı: bittiyse sonucu, sürüyorsa ya da yarıda kaldıysa 409; yoksa null. Aynı kimlik ikinci kez ücret doğurmaz.</summary>
    private static IResult? YapayZekaTekrari(KasaDbContext db, Guid istekId, string tur, int? belgeId, Func<YapayZekaCagriEntity, IResult> sonuc)
    {
        Require(istekId != Guid.Empty, "Geçerli bir istek kimliği gerekir.");
        if (db.YapayZekaCagrilar.AsNoTracking().SingleOrDefault(c => c.IstekId == istekId) is not { } c)
            return null;
        Require(c.Tur == tur && c.BelgeId == belgeId, "İstek kimliği başka bir işlem için kullanılmış.", 409);
        Require(c.Durum == YapayZekaCagriDurumlari.Tamam, "Önceki istek tamamlanmadı; yeniden sorun.", 409);
        return sonuc(c);
    }

    /// <summary>Ayrılan çağrıyı yapar ve sonucunu yazar. Çağrı istemci vazgeçse de sürer: ücreti doğan yanıt kaydedilir.</summary>
    private static async Task<IResult> YapayZekaCagir(KasaDbContext db, YapayZekaServisi yz, ILoggerFactory loglar, YapayZekaCagriEntity cagri, YapayZekaIstegi istek, Func<YapayZekaYaniti, IResult> yaz)
    {
        YapayZekaYaniti yanit;
        try
        { yanit = await yz.SorAsync(istek); }
        catch (YapayZekaHatasi e)
        {
            loglar.CreateLogger("Kasa.Api.YapayZeka").LogWarning(e.InnerException, "Yapay zekâ çağrısı {Id} yapılamadı: {Ileti}", cagri.Id, e.Message);
            var kayit = Safe(() => AlisEndpoints.Mutate(db, () => { YapayZekaServisi.Bitir(cagri, e); db.SaveChanges(); return Results.Ok(); }));
            return kayit is IStatusCodeHttpResult { StatusCode: >= 400 } ? kayit : Error(e.Message, 502);
        }
        return Safe(() => AlisEndpoints.Mutate(db, () => { YapayZekaServisi.Bitir(cagri, yanit); var sonuc = yaz(yanit); db.SaveChanges(); return sonuc; }));
    }

    private const string YapayZekaMesgul = "Başka bir yapay zekâ isteği sürüyor; bitince yeniden deneyin.";
    private static string YapayZekaSinirda(YapayZekaServisi yz) =>
        $"Aylık yapay zekâ sınırına ({yz.Ayarlar.AylikSinirUsd.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))} $) ulaşıldı; gelecek ay ya da sınır yükseltilince sorulabilir.";

    private static async Task<IResult> YapayZekaSor(int id, YapayZekaSorYaz dto, KasaDbContext db, YapayZekaServisi yz, ILoggerFactory loglar)
    {
        using var tek = yz.TekCagri();
        if (tek is null)
            return Error(YapayZekaMesgul, 409);
        YapayZekaServisi.Hazirlik? h = null;
        YapayZekaCagriEntity? cagri = null;
        string? satirlarJson = null;
        var on = Safe(() => AlisEndpoints.Mutate(db, () =>
        {
            var belge = GetDocument(db, id);
            if (YapayZekaTekrari(db, dto.IstekId, YapayZekaCagriTurleri.Satir, id, c => Results.Content(c.SonucJson ?? "null", "application/json")) is { } onceki)
                return onceki;
            var hazirlik = YapayZekaHazirla(db, yz, belge);
            Require(hazirlik.Istek is not null, "Yapay zekâya sorulacak satır yok: bekleyen satırlar kural ya da hafızayla tanınıyor, önerisi var ya da yalnız ad ve sayıdan oluşuyor.", 409);
            Require(hazirlik.Istek.Karakter <= YapayZekaAyarlari.EnCokGirdiKarakteri, "İstek çok uzun.", 409);
            cagri = yz.Ayir(db, dto.IstekId, YapayZekaCagriTurleri.Satir, id, hazirlik.Satirlar.Sum(s => s.Satirlar.Count), hazirlik.Istek);
            Require(cagri is not null, YapayZekaSinirda(yz), 409);
            (h, satirlarJson) = (hazirlik, belge.SatirlarJson);
            return Results.Ok();
        }));
        if (on is IStatusCodeHttpResult { StatusCode: >= 400 } || cagri is null || h?.Istek is null)
            return on;
        return await YapayZekaCagir(db, yz, loglar, cagri, h.Istek, yanit =>
        {
            // Çağrı sürerken belge yeniden okunduysa satır numaraları başka hareketlerdir. Belge bu bağlamda izleniyor; güncel hâli veritabanından
            // sorulur (Codex incelemesi, EmarKasa#65).
            var ayniBelge = db.EkstreBelgeler.AsNoTracking().Any(b => b.Id == id && b.SatirlarJson == satirlarJson);
            var oneriler = ayniBelge ? YapayZekaServisi.Yaz(db, id, h, cagri, yanit.Metin) : null;
            var not = yanit.Metin is null ? yanit.Neden
                : !ayniBelge ? "Belge bu sırada yeniden okundu; yeniden sorun."
                : oneriler is null ? "Yanıt okunamadı." : oneriler == 0 ? "Bu satırlar için güvenilir öneri gelmedi." : null;
            var sonuc = new YapayZekaSonucDto(cagri.SatirSayisi, oneriler ?? 0, h.Kalan, cagri.MaliyetUsd, not);
            cagri.SonucJson = System.Text.Json.JsonSerializer.Serialize(sonuc, System.Text.Json.JsonSerializerOptions.Web);
            return Results.Ok(sonuc);
        });
    }

    private static async Task<IResult> YapayZekaOlc(YapayZekaOlcumYaz dto, KasaDbContext db, YapayZekaServisi yz, ILoggerFactory loglar)
    {
        using var tek = yz.TekCagri();
        if (tek is null)
            return Error(YapayZekaMesgul, 409);
        YapayZekaCagriEntity? cagri = null;
        var olcum = ((YapayZekaIstegi Istek, IReadOnlyDictionary<int, (string Yon, bool Uyarili, YapayZekaMaskesi.Maskeli Metin)> Sorulan, IReadOnlyDictionary<int, EkstreHafizaServisi.Karar> Beklenen)?)null;
        var on = Safe(() => AlisEndpoints.Mutate(db, () =>
        {
            Require(dto.Kaynak is EkstreKaynaklari.Banka or EkstreKaynaklari.Kart, "Banka ya da kart seçin.");
            Require(yz.Ayarlar.KapaliNedeni is null, yz.Ayarlar.KapaliNedeni ?? "", 409);
            if (YapayZekaTekrari(db, dto.IstekId, YapayZekaCagriTurleri.Olcum, null, c => Results.Content(c.SonucJson ?? "null", "application/json")) is { } onceki)
                return onceki;
            olcum = YapayZekaServisi.OlcumHazirla(db, dto.Kaynak);
            Require(olcum is not null, $"Ölçüm için en az {YapayZekaServisi.EnAzOlcumKarari} farklı {(dto.Kaynak == EkstreKaynaklari.Kart ? "kart" : "banka")} kararı gerekir.", 409);
            cagri = yz.Ayir(db, dto.IstekId, YapayZekaCagriTurleri.Olcum, null, olcum.Value.Beklenen.Count, olcum.Value.Istek);
            Require(cagri is not null, YapayZekaSinirda(yz), 409);
            return Results.Ok();
        }));
        if (on is IStatusCodeHttpResult { StatusCode: >= 400 } || cagri is null || olcum is not { } o)
            return on;
        return await YapayZekaCagir(db, yz, loglar, cagri, o.Istek, yanit =>
        {
            var sonuc = YapayZekaServisi.OlcumSonucu(db, dto.Kaynak, o.Sorulan, o.Beklenen, yanit.Metin ?? "", cagri);
            cagri.SonucJson = System.Text.Json.JsonSerializer.Serialize(sonuc, System.Text.Json.JsonSerializerOptions.Web);
            return Results.Ok(sonuc);
        });
    }
}
