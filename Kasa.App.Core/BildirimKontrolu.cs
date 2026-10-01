using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Pencere açmadan çalışma (zamanlanmış görev; argüman <see cref="BildirimGorevi.KontrolArgumani"/>): ayar açıksa kayıtlı oturum
/// belirteciyle rolü sorar (/me), editörse bir kez bakar ve biter; en çok <see cref="EnUzunSure"/> çalışır. Belirteç yok, süresi
/// dolmuş, rol editör değil, ayar kapalı ya da sunucuya ulaşılamıyorsa sessizce null döner; uygulama bir sonraki açılışta zaten
/// bakar. Çağıran platform kodu Kasa.App/Platforms/Windows/App.xaml.cs'tedir (pencere kilidi, Windows bildirim kaydı, süreç çıkışı).
/// <para>Süresi dolmuş belirteç: KasaApiClient 401 yanıtında belirteç deposunu temizler (OturumuGecersizKilAsync). Görev süresi dolmuş
/// belirteçle çalışırsa kayıtlı oturum silinir ve uygulama bir sonraki açılışta giriş ister; bu, uygulamanın kendi açılış
/// doğrulamasıyla (AuthViewModel.AcilistaDogrulaAsync) aynı davranıştır ve bilinçli olarak değiştirilmez.</para>
/// <para>Süre dolunca beklenen çağrı iptal edilmez, yalnız beklenmez: çağıran süreç hemen sonra kapanır.</para>
/// </summary>
public sealed class BildirimKontrolu(IKasaApi api, BildirimYoklayici yoklayici, IBildirimAyari ayar)
{
    public static readonly TimeSpan EnUzunSure = TimeSpan.FromSeconds(60);

    public static bool KontrolModu(IEnumerable<string> argumanlar) => argumanlar.Contains(BildirimGorevi.KontrolArgumani);

    /// <param name="sure">En uzun çalışma süresi (varsayılan <see cref="EnUzunSure"/>; testler kısaltır).</param>
    public async Task<YoklamaSonucu?> CalistirAsync(TimeSpan? sure = null)
    {
        try
        {
            return await IcAsync().WaitAsync(sure ?? EnUzunSure);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<YoklamaSonucu?> IcAsync()
    {
        if (!ayar.Acik)
            return null;
        var rol = SekmeModeli.RolCoz(await api.BenKimAsync());
        return await yoklayici.YoklaAsync(rol == Rol.Editor);
    }
}
