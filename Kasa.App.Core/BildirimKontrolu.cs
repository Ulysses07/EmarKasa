using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Pencere açmadan çalışma (zamanlanmış görev; argüman <see cref="BildirimGorevi.KontrolArgumani"/>): ayar açıksa kayıtlı oturum
/// belirteciyle rolü sorar (/me), editörse bir kez bakar ve biter; en çok <see cref="EnUzunSure"/> çalışır. Belirteç yok, süresi
/// dolmuş, rol editör değil, ayar kapalı, /me sırasında sunucuya ulaşılamıyorsa ya da süre dolarsa sessizce null döner; uygulama bir
/// sonraki açılışta zaten bakar. /me başarılı olup liste (/bildirimler) alınamazsa null değil, yoklayıcının hata sonucu döner
/// (<see cref="YoklamaDurumu.SunucuyaUlasilamadi"/>, <see cref="YoklamaDurumu.SunucuHatasi"/> ya da
/// <see cref="YoklamaDurumu.OturumGecersiz"/>); bu durumda da bildirim gösterilmez. Çağıran platform kodu Kasa.App/Platforms/Windows/App.xaml.cs'tedir (pencere kilidi, Windows bildirim kaydı, süreç çıkışı).
/// <para>Süresi dolmuş belirteç: KasaApiClient 401 yanıtında belirteç deposunu temizler (OturumuGecersizKilAsync). Görev süresi dolmuş
/// belirteçle çalışırsa kayıtlı oturum silinir ve uygulama bir sonraki açılışta giriş ister; bu, uygulamanın kendi açılış
/// doğrulamasıyla (AuthViewModel.AcilistaDogrulaAsync) aynı davranıştır ve bilinçli olarak değiştirilmez.</para>
/// <para>Süre dolunca beklenen çağrı iptal edilmez, yalnız beklenmez: çağıran süreç hemen sonra kapanır. Bilinen risk: arkada kalan
/// yoklama süreç kapanırken depoda bildirimleri "gösterildi" diye ayırmış (YenileriAyir) ama henüz göstermemiş olabilir; o bildirimler
/// bu bilgisayarda Windows bildirimi olarak gösterilmez, listede ve telefonda görünmeye devam eder (karar 10 ile aynı sonuç).</para>
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
