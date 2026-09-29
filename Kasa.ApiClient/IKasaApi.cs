namespace Kasa.ApiClient;

/// <summary>KasaApiClient'in test edilebilir yüzeyi (VM'ler buna bağlanır).</summary>
public interface IKasaApi
{
    Task<LoginYanit> LoginAsync(string? kullanici, string sifre);
    Task<string?> BenKimAsync();
    Task CikisAsync();

    /// <summary>Haftalık rapor; ekran değişince çağıran isteği iptal edebilir.</summary>
    Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync(CancellationToken ct = default);
    /// <summary>Ana sayfa özeti tek istekte; <paramref name="gun"/> takip özetinin ufku. Eski sunucuda (uç yoksa 404) ve
    /// ucun sunucu hatasında (5xx) yalnız panel dolu döner (bkz. <see cref="AnaSayfaDto"/>).</summary>
    Task<AnaSayfaDto> AnaSayfaAsync(int gun = 30, CancellationToken ct = default);
    Task<AylikRaporDto> AylikAsync(int yil, int ay);
    Task<IReadOnlyList<DonemDto>> DonemlerAsync();
    Task<IReadOnlyList<KanalDto>> KanallarAsync();
    Task<IReadOnlyList<IslemDto>> IslemlerAsync(DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null);
    Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync();
    Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null);
    Task<AyarlarDto> AyarlarAsync();

    // Editör mutasyonları (KasaApiClient bunları zaten uyguluyor)
    Task<KanalDto> KanalOlusturAsync(KanalYaz g);
    Task<KanalDto> KanalGuncelleAsync(int id, KanalYaz g);
    Task KanalSilAsync(int id);
    Task<IslemDto> IslemOlusturAsync(IslemYaz g);
    Task<IslemDto> IslemGuncelleAsync(int id, IslemYaz g);
    Task IslemSilAsync(int id);
    Task<GelenDto> GelenKaydetAsync(GelenYaz g);
    Task AyarGuncelleAsync(AyarYaz g);
    Task IzleyiciSifreAsync(string yeniSifre);
}
