namespace Kasa.Api.Servisler;

/// <summary>Bir dizinin bulunduğu diskte kullanılabilir boş alan (data-3). Yedek servisi kopyadan önce, durum ucu ve belge deposu
/// geçişi bu soyutlamayı kullanır; testler sahtesini kaydeder.</summary>
public interface IDiskAlani
{
    /// <summary>Yolun (yoksa en yakın var olan üst dizininin) bulunduğu diskte bu sürecin kullanabileceği boş bayt; okunamazsa null.</summary>
    long? BosAlan(string yol);
}

/// <summary>İşletim sisteminin bildirdiği boş alan: Linux'ta statvfs (DriveInfo verilen yolu bağlama noktası gibi sorgular),
/// Windows'ta yolun sürücü kökü.</summary>
public sealed class DiskAlani : IDiskAlani
{
    public long? BosAlan(string yol)
    {
        try
        {
            var dizin = Path.GetFullPath(yol);
            while (!Directory.Exists(dizin))
            {
                var ust = Path.GetDirectoryName(dizin);
                if (string.IsNullOrEmpty(ust)) return null;
                dizin = ust;
            }
            var kok = OperatingSystem.IsWindows() ? Path.GetPathRoot(dizin) : dizin;
            return string.IsNullOrEmpty(kok) ? null : new DriveInfo(kok).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
}
