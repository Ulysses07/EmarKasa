using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Denetim;

/// <summary>Değişiklik geçmişi satırı. <paramref name="OncekiJson"/>/<paramref name="YeniJson"/>: değişiklikte yalnız değişen
/// alanlar, eklemede/silmede bütün alanlar; gizli alanlar '***'. <paramref name="KilitAcmaOlayiId"/>: değişiklik bir ay
/// kilidi açılışının penceresine düştüyse o açılışın kilit olayı (AyKilidiOlayDto.Id). <paramref name="Tur"/> 'GecmisKayit':
/// denetim izinden önceki sürümün sakladığı gerekçe ve önceki durum (Migrations/DenetimGecmisAktarimi); zamanı aktarım anıdır.</summary>
public record DenetimOlayDto(int Id, DateTimeOffset Zaman, string AktorRol, int? AktorId, string? IstemciIp, string Tur, string Varlik,
    string? VarlikId, string? OncekiJson, string? YeniJson, string? Gerekce, Guid? IstekId, string? TraceId, int? KilitAcmaOlayiId);

public static class DenetimEndpoints
{
    public const int VarsayilanAdet = 100;
    public const int EnFazlaAdet = 200;

    public static WebApplication MapDenetimEndpoints(this WebApplication app)
    {
        // Değişiklik geçmişi yalnız editöre açıktır (IP ve gerekçe içerir). Yeniden eskiye; oncekiId ile sayfalanır.
        // Salt okunur anlık görüntüde çalışır: okuma olay yazmaz, rapor okumalarını beklemez.
        app.MapGet("/api/denetim", (string? varlik, string? varlikId, string? tur, int? kilitAcmaOlayiId, int? oncekiId, int? adet, KasaDbContext db) =>
        {
            var v = new GirdiDogrulama();
            v.Metin(varlik, "varlik", 100, zorunlu: false);
            v.Metin(varlikId, "varlikId", 100, zorunlu: false);
            v.Metin(tur, "tur", 100, zorunlu: false);
            v.Kontrol(varlikId is null || !string.IsNullOrWhiteSpace(varlik), "varlikId", "Kayıt kimliği varlık adıyla birlikte verilir.");
            v.Kontrol(adet is null || adet is >= 1 and <= EnFazlaAdet, "adet", $"Adet 1–{EnFazlaAdet} olmalı.");
            if (v.Sonuc() is { } hata)
                return hata;
            return AlisEndpoints.Oku(db, () =>
            {
                var q = db.DenetimOlaylari.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(varlik))
                { var ad = varlik.Trim(); q = q.Where(o => o.Varlik == ad); }
                if (!string.IsNullOrWhiteSpace(varlikId))
                { var anahtar = varlikId.Trim(); q = q.Where(o => o.VarlikId == anahtar); }
                if (!string.IsNullOrWhiteSpace(tur))
                { var t = tur.Trim(); q = q.Where(o => o.Tur == t); }
                if (kilitAcmaOlayiId is { } kilit)
                    q = q.Where(o => o.KilitAcmaOlayiId == kilit);
                if (oncekiId is { } once)
                    q = q.Where(o => o.Id < once);
                var satirlar = q.OrderByDescending(o => o.Id).Take(adet ?? VarsayilanAdet).ToList();
                return Results.Ok(satirlar.Select(Dto).ToList());
            });
        }).RequireAuthorization("Editor");
        return app;
    }

    /// <summary>Olayın API görünümü (değişiklik geçmişi ve "kasa kontrolünden beri değişenler").</summary>
    internal static DenetimOlayDto Dto(DenetimOlayEntity o) => new(o.Id, DateTimeOffset.FromUnixTimeMilliseconds(o.ZamanUtc), o.AktorRol, o.AktorId,
        o.IstemciIp, o.Tur, o.Varlik, o.VarlikId, o.OncekiJson, o.YeniJson, o.Gerekce, o.IstekId, o.TraceId, o.KilitAcmaOlayiId);
}
