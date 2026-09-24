using Kasa.Api.Servisler;

namespace Kasa.Api.Endpoints;

/// <summary>21 · "Kasa neden değişti?": açılıştan kapanışa adım adım kasa dökümü (okuma — her iki rol).</summary>
public static class KasaDokumuEndpoints
{
    public static RouteGroupBuilder MapKasaDokumu(this RouteGroupBuilder api)
    {
        // Aralıkla çakışan dönemlerin tamamı alınır (dönem ortasından başlayan aralık o dönemin başına genişler);
        // yanıttaki Baslangic/Bitis gerçek aralıktır.
        api.MapGet("/rapor/kasa-dokumu", (DateOnly? baslangic, DateOnly? bitis, RaporServisi rapor) =>
        {
            if (AralikHatasi(baslangic, bitis) is string h) return UcNokta.Hata(h);
            var d = rapor.KasaDokumu(baslangic!.Value, bitis!.Value);
            return d is null ? UcNokta.Hata("Bu aralıkta takip dönemi yok.") : Results.Ok(RaporServisi.Dto(d));
        });
        return api;
    }

    internal static string? AralikHatasi(DateOnly? baslangic, DateOnly? bitis)
    {
        if (baslangic is not { } b || bitis is not { } s) return "Başlangıç ve bitiş tarihi gerekli.";
        if ((UcNokta.TarihHatasi(b) ?? UcNokta.TarihHatasi(s)) is string h) return h;
        if (s < b) return "Bitiş tarihi başlangıçtan önce olamaz.";
        return null;
    }
}
