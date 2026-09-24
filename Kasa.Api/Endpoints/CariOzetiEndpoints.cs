using Kasa.Api.Servisler;

namespace Kasa.Api.Endpoints;

/// <summary>07 · Cari (ya da sabit gider kalemi) için yılın ay ay ödeme özeti (okuma — her iki rol).</summary>
public static class CariOzetiEndpoints
{
    public static RouteGroupBuilder MapCariOzeti(this RouteGroupBuilder api)
    {
        api.MapGet("/rapor/cari-ozeti", (string? ad, int yil, string? tur, RaporServisi rapor) =>
        {
            var a = ad?.Trim() ?? "";
            if (a.Length == 0) return UcNokta.Hata("Cari ya da kalem adı gerekli.");
            if (a.Length > 200) return UcNokta.Hata("Ad en fazla 200 karakter olabilir.");
            if (yil is < 2000 or > 2100) return UcNokta.Hata("Yıl 2000 ile 2100 arasında olmalı.");
            var t = string.IsNullOrWhiteSpace(tur) ? RaporServisi.CariTuru : tur.Trim().ToLowerInvariant();
            if (t is not (RaporServisi.CariTuru or RaporServisi.KalemTuru)) return UcNokta.Hata("Tür 'cari' ya da 'kalem' olmalı.");
            return Results.Ok(rapor.CariOzeti(a, yil, t));
        });
        return api;
    }
}
