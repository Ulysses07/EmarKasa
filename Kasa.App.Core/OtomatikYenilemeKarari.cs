namespace Kasa.App.Core;

/// <summary>
/// Kabuğun otomatik yenileme kararı (bağlantı geldiğinde; docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §3,
/// incelemede K-1 ve Ö-1): art arda gelen istekler arasında en az <paramref name="aralik"/> beklenir (alt sınır), ama sınır
/// içinde gelen istek düşürülmez: sınırın sonunda (sondaki kenarda) bir kez tetiklenir (K-1). Saat <see cref="TimeProvider"/>
/// ile verilir (test edilebilir). "Yeniden dene" düğmesi bu sınıfı kullanmaz; kullanıcı isteğiyle her zaman hemen çalışır.
/// </summary>
public sealed class OtomatikYenilemeKarari(TimeProvider zaman, TimeSpan aralik)
{
    private DateTimeOffset? _sonTetik;
    private bool _bekliyor;

    /// <summary>Şimdi tetiklensin mi (true döner, <paramref name="beklemeSuresi"/> Zero'dur); değilse (false) ya kaydedilmemiş
    /// değişiklik var (otomatik yenileme hiç tetiklenmez, form kullanıcı isteği olmadan ezilmez; <paramref name="beklemeSuresi"/>
    /// Zero, bekleyen istek varsa düşürülür) ya da sınır içindedir (<paramref name="beklemeSuresi"/> sonra çağıran yeniden
    /// <see cref="Sor"/> çağırmalı; sınır içinde art arda gelen istekler için yalnız bir bekleme zamanlanır, istek düşürülmez).
    /// </summary>
    public bool Sor(bool kaydedilmemisDegisiklikVar, out TimeSpan beklemeSuresi)
    {
        beklemeSuresi = TimeSpan.Zero;
        if (kaydedilmemisDegisiklikVar)
        {
            _bekliyor = false;
            return false;
        }
        var simdi = zaman.GetUtcNow();
        if (_sonTetik is { } once && simdi - once < aralik)
        {
            if (!_bekliyor)
            {
                _bekliyor = true;
                beklemeSuresi = once + aralik - simdi;
            }
            return false;
        }
        _sonTetik = simdi;
        _bekliyor = false;
        return true;
    }
}
