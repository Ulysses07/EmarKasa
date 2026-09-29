using Microsoft.AspNetCore.Diagnostics;

namespace Kasa.Api;

/// <summary>Uç sarmalayıcısı dışındaki (Program.cs uçları, servisler) veritabanı hatalarını
/// <see cref="VeritabaniHataSiniflandirici"/>'nın kuralıyla yanıtlar ve loglar. Sınıflandırılamayan istisna genel 500'e kalır;
/// onu ara katman Error olarak loglar (işleyicinin true döndürdüğü istisnayı ise loglamaz, bu yüzden log burada yazılır).</summary>
public sealed class VeritabaniHataIsleyici : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (VeritabaniHataSiniflandirici.Siniflandir(exception) is not { } hata) return false;
        await VeritabaniHataSiniflandirici.Yanitla(http, hata, exception);
        return true;
    }
}
