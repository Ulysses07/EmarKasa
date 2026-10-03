namespace Kasa.Api.Auth;

/// <summary>
/// Güvenlik günlüğü + veritabanı commit'i ile yedeğin zaman kesimi + SQLite kopyasını aynı süreçte sıraya alır.
/// Böylece günlüğe yazılmış ama henüz commit edilmemiş bir kimlik kararı, kendisinden sonraki zaman kesimli eski DB yedeğinde kaybolmaz.
/// </summary>
public sealed class GuvenlikYedekSeriKilidi
{
    private readonly SemaphoreSlim _kilit = new(1, 1);
    private int _bekleyenYedekSayisi;

    /// <summary>Yedekleme anlık görüntüsü için sırada bekleyen çağrı sayısı.</summary>
    public int BekleyenYedekSayisi => Volatile.Read(ref _bekleyenYedekSayisi);

    public IDisposable Al()
    {
        _kilit.Wait();
        return new Kiralama(_kilit);
    }

    public async ValueTask<IDisposable> AlAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _bekleyenYedekSayisi);
        try
        {
            await _kilit.WaitAsync(ct);
        }
        finally
        {
            Interlocked.Decrement(ref _bekleyenYedekSayisi);
        }
        return new Kiralama(_kilit);
    }

    private sealed class Kiralama(SemaphoreSlim kilit) : IDisposable
    {
        private SemaphoreSlim? _kilit = kilit;

        public void Dispose() => Interlocked.Exchange(ref _kilit, null)?.Release();
    }
}
