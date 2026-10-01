namespace Kasa.App.Core;

/// <summary>Uygulama penceresi açık mı: pencereli süreç adlı bir mutex tutar (<see cref="Al"/>); pencere açmadan çalışan bildirim
/// görevi (BildirimKontrolu) mutex varsa hiçbir şey yapmadan çıkar. Açık uygulama zaten 5 dakikada bir bakar; görev ayrıca bakarsa
/// aynı bildirimi iki süreç yarışır ve iki sürecin Windows bildirim kaydı karışır. Ad Windows oturumuna özeldir (Local\). Mutex
/// sahiplenilmez (initiallyOwned false): yalnız adlı nesnenin varlığı işarettir, iş parçacığı bağı yoktur; son tanıtıcı kapanınca
/// (Dispose ya da süreç sonu) ad kaybolur. <see cref="Al"/> ile alınan nesne süreç boyunca statik bir alanda tutulmalıdır: yerel
/// değişkende kalıp çöp toplayıcıya giderse sonlandırıcı tanıtıcıyı kapatır ve kilit pencere açıkken kaybolur.</summary>
public sealed class PencereKilidi : IDisposable
{
    public const string VarsayilanAd = @"Local\EmarKasa.Pencere";

    private readonly Mutex _mutex;

    private PencereKilidi(Mutex mutex) => _mutex = mutex;

    public static PencereKilidi Al(string ad = VarsayilanAd) => new(new Mutex(false, ad));

    /// <summary>Kilit tutuluyor mu. Denetim hata verirse (erişim reddi <see cref="UnauthorizedAccessException"/>,
    /// <see cref="WaitHandleCannotBeOpenedException"/>, geçersiz ad, G/Ç hatası) güvenli tarafta açık sayılır: görev hiçbir şey
    /// yapmadan çıkar, uygulama bir sonraki açılışta zaten bakar.</summary>
    public static bool AcikMi(string ad = VarsayilanAd)
    {
        try
        {
            if (!Mutex.TryOpenExisting(ad, out var mutex))
                return false;
            mutex.Dispose();
            return true;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public void Dispose() => _mutex.Dispose();
}
