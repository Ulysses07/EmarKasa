using Kasa.Api.Data;
using Kasa.Api.Servisler;

namespace Kasa.Api;

public static class RaporEndpoints
{
    public static WebApplication MapRaporEndpoints(this WebApplication app)
    {
        // Finansal bilgiler yalnız editör ve izleyiciye açıktır.
        var api = app.MapGroup("/api").RequireAuthorization("Finans");

        // Raporlar (okuma — her iki rol). Salt okunur anlık görüntüde çalışır (yazma kilidi ve Sync yok); istemci isteği
        // bırakırsa (RequestAborted) hesap sorgular ve döngüler arasında kesilir, anlık görüntü hemen bırakılır.
        api.MapGet("/donemler", (HesapServisi svc, CancellationToken ct) => svc.Donemler(ct));
        api.MapGet("/rapor/haftalik", (HesapServisi svc, CancellationToken ct) => svc.Haftalik(ct));
        // Kilitli ayın raporu kilitlendiği andaki görüntüden döner ("dondurulmus": true); açık ay canlı hesaplanır.
        api.MapGet("/rapor/aylik", (int yil, int ay, HesapServisi svc, CancellationToken ct) =>
            GirdiDogrulama.RaporAyi(yil, ay) ?? Results.Ok(svc.AylikYanit(yil, ay, ct)));
        api.MapGet("/rapor/panel", (HesapServisi svc, CancellationToken ct) => svc.Panel(ct));
        // Ana sayfanın panel + kasa eşikleri + takip özeti üçlüsü tek istekte, tek anlık görüntüde ve tek hesap bağlamıyla
        // (kart verisi ve ödeme etkileri bir kez). Ayrı uçlar geriye uyum için aynen durur.
        api.MapGet("/rapor/ana-sayfa", (int? gun, KasaDbContext db, HesapServisi svc, CancellationToken ct) =>
            AnaSayfaOzeti.Oku(db, svc, gun ?? 30, ct));
        return app;
    }
}
