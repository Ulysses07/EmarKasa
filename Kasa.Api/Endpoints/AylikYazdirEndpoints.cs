using System.Text;
using Kasa.Api.Data;
using Kasa.Api.Servisler;

namespace Kasa.Api.Endpoints;

/// <summary>24 · Aylık raporun yazdırılabilir tek sayfalık HTML'i (okuma — her iki rol).</summary>
public static class AylikYazdirEndpoints
{
    public static RouteGroupBuilder MapAylikYazdir(this RouteGroupBuilder api)
    {
        api.MapGet("/rapor/aylik-yazdir", (int yil, int ay, KasaDbContext db, RaporServisi rapor, HesapServisi hesap,
            TimeProvider saat, HttpContext http) =>
        {
            if (UcNokta.AyHatasi(yil, ay) is string h) return UcNokta.Hata(h);
            var html = AylikYazdirma.Olustur(AylikYazdirma.Topla(db, rapor, hesap, saat, yil, ay));
            // Genel "default-src 'none'" politikası yerine bu belgeye özel sıkı CSP (satır içi stil + tek yazdır betiği).
            http.Response.Headers.ContentSecurityPolicy = AylikYazdirma.BaslikCsp;
            return UcNokta.Dosya(Encoding.UTF8.GetBytes(html), AylikYazdirma.IcerikTipi, AylikYazdirma.DosyaAdi(yil, ay));
        });
        return api;
    }
}
