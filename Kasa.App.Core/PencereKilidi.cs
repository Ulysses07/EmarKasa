namespace Kasa.App.Core;

/// <summary>Uygulama penceresi açık mı: pencereli süreç adlı bir mutex tutar (<see cref="Al"/>); pencere açmadan çalışan bildirim
/// görevi (BildirimKontrolu) mutex varsa hiçbir şey yapmadan çıkar. Açık uygulama zaten 5 dakikada bir bakar; görev ayrıca bakarsa
/// aynı bildirimi iki süreç yarışır ve iki sürecin Windows bildirim kaydı karışır. Ad Windows oturumuna özeldir (Local\). Mutex
/// sahiplenilmez (initiallyOwned false): yalnız adlı nesnenin varlığı işarettir, iş parçacığı bağı yoktur; son tanıtıcı kapanınca
/// (Dispose ya da süreç sonu) ad kaybolur.</summary>
public sealed class PencereKilidi : IDisposable
{
    public const string VarsayilanAd = @"Local\EmarKasa.Pencere";

    private readonly Mutex _mutex;

    private PencereKilidi(Mutex mutex) => _mutex = mutex;

    public static PencereKilidi Al(string ad = VarsayilanAd) => new(new Mutex(false, ad));

    public static bool AcikMi(string ad = VarsayilanAd)
    {
        if (!Mutex.TryOpenExisting(ad, out var mutex))
            return false;
        mutex.Dispose();
        return true;
    }

    public void Dispose() => _mutex.Dispose();
}
