using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public sealed class VeritabaniHataIsleyici : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is KilitliDonemException)
        {
            http.Response.StatusCode = StatusCodes.Status409Conflict;
            await http.Response.WriteAsJsonAsync(new { hata = exception.Message }, ct);
            return true;
        }
        var sqlite = exception as SqliteException ?? (exception as DbUpdateException)?.InnerException as SqliteException;
        if (sqlite?.SqliteErrorCode != 19) return false;
        http.Response.StatusCode = StatusCodes.Status409Conflict;
        await http.Response.WriteAsJsonAsync(new
        {
            hata = sqlite.Message.Contains("Kilitli ay", StringComparison.Ordinal)
                ? "Bu tarih kilitli dönemde. Değişiklik için ilgili ayı gerekçeyle açın."
                : "Kayıt başka bir kayıtla çakışıyor veya bağlı olduğu kayıt değişmiş. Listeyi yenileyip tekrar deneyin."
        }, ct);
        return true;
    }
}
