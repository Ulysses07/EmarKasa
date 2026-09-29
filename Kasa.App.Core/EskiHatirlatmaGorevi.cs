using System.Diagnostics;

namespace Kasa.App.Core;

/// <summary>
/// 07-15 kart hatırlatıcısının kullanıcı düzeyi Windows zamanlanmış görevi (<see cref="GorevAdi"/>: her gün 09:00'da
/// "Kasa.App.exe --hatirlatma-kontrol"). Hatırlatıcı kaldırıldı (hatırlatmalar sunucudan gelir); görevi silen kod olmadığından
/// eski kurulumlarda exe her gün boşuna açılıp kapanıyordu (gap-tarihsel-spec-ve-emekli-web-7). Uygulama hem normal açılışta hem
/// görevin kendi çalıştırmasında görevi bir kez siler: pencere açmadan (schtasks, CreateNoWindow), arka planda, hataları yutarak.
/// Silme başarılıysa ya da görev zaten yoksa yerel işaret dosyası yazılır, sonraki açılışlarda komut çalıştırılmaz. schtasks
/// başlatılamaz, süre dolar ya da görev hâlâ duruyorsa işaret yazılmaz: sonraki açılışta yeniden denenir. Komut kurgusu ve işaret
/// mantığı buradadır (test edilebilir); çağıran platform kodu Kasa.App/Platforms/Windows/App.xaml.cs'tedir.
/// </summary>
public sealed class EskiHatirlatmaGorevi
{
    public const string GorevAdi = "EmarKasaHatirlatici";
    /// <summary>Eski görevin exe'ye verdiği argüman: bu sürümde pencere açılmaz, görev silinir ve uygulama kapanır.</summary>
    public const string KontrolArgumani = "--hatirlatma-kontrol";
    /// <summary>Tek schtasks çağrısının en uzun bekleme süresi.</summary>
    public static readonly TimeSpan ZamanAsimi = TimeSpan.FromSeconds(5);

    private readonly string _isaret;
    public EskiHatirlatmaGorevi(string klasor) => _isaret = Path.Combine(klasor, "eski-hatirlatma-gorevi-silindi.txt");
    /// <summary>Varsayılan konum: %LOCALAPPDATA%\EmarKasa (07-15 hatırlatıcısının durum klasörü).</summary>
    public static EskiHatirlatmaGorevi Varsayilan() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EmarKasa"));

    public static bool KontrolModu(IEnumerable<string> argumanlar) => argumanlar.Contains(KontrolArgumani);
    public static ProcessStartInfo SilmeKomutu() => Komut("/Delete", "/TN", GorevAdi, "/F");
    public static ProcessStartInfo SorguKomutu() => Komut("/Query", "/TN", GorevAdi);
    private static ProcessStartInfo Komut(params string[] argumanlar)
    {
        var komut = new ProcessStartInfo("schtasks") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in argumanlar) komut.ArgumentList.Add(a);
        return komut;
    }

    /// <summary>Görev bu makinede daha önce silindi (ya da yoktu).</summary>
    public bool Yapildi => File.Exists(_isaret);

    /// <summary>Görevi gerçek schtasks ile bir kez siler (bkz. <see cref="TemizleAsync(Func{ProcessStartInfo, Task{int?}})"/>).</summary>
    public Task<bool> TemizleAsync() => TemizleAsync(k => CalistirAsync(k, ZamanAsimi));

    /// <summary>Görevi bir kez siler. <paramref name="calistir"/> komutu çalıştırıp çıkış kodunu döner; başlatılamaz ya da süre dolarsa
    /// null. Hiçbir hata dışarı çıkmaz. İşaret bu çağrıda yazıldıysa true döner.</summary>
    public async Task<bool> TemizleAsync(Func<ProcessStartInfo, Task<int?>> calistir)
    {
        try
        {
            if (Yapildi) return false;
            var silme = await calistir(SilmeKomutu());
            if (silme is null) return false;
            // Silme başarısız: görev yoksa (sorgu başarısız) iş bitmiştir; görev duruyor ya da sorgulanamadıysa yeniden denenir.
            if (silme != 0 && await calistir(SorguKomutu()) is null or 0) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(_isaret)!);
            File.WriteAllText(_isaret, DateTimeOffset.Now.ToString("O"));
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Komutu pencere açmadan çalıştırır ve en çok <paramref name="zamanAsimi"/> bekler: çıkış kodunu, başlatılamazsa ya da
    /// süre dolarsa (süreç sonlandırılır) null döner.</summary>
    public static async Task<int?> CalistirAsync(ProcessStartInfo komut, TimeSpan zamanAsimi)
    {
        try
        {
            using var surec = Process.Start(komut);
            if (surec is null) return null;
            // Yönlendirilen çıktı okunmazsa tampon dolunca süreç beklemede kalabilir.
            var cikti = komut.RedirectStandardOutput ? surec.StandardOutput.ReadToEndAsync() : Task.FromResult("");
            var hata = komut.RedirectStandardError ? surec.StandardError.ReadToEndAsync() : Task.FromResult("");
            using var sure = new CancellationTokenSource(zamanAsimi);
            try { await surec.WaitForExitAsync(sure.Token); }
            catch (OperationCanceledException)
            {
                try { surec.Kill(); } catch (Exception) { }
                return null;
            }
            await Task.WhenAll(cikti, hata);
            return surec.ExitCode;
        }
        catch (Exception) { return null; }
    }
}
