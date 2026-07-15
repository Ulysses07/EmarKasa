namespace Kasa.ApiClient;

/// <summary>KasaApiClient'in test edilebilir yüzeyi (VM'ler buna bağlanır).</summary>
public interface IKasaApi
{
    Task<LoginYanit> LoginAsync(string? kullanici, string sifre);
    Task<string?> BenKimAsync();
    Task CikisAsync();

    Task<PanelDto> PanelAsync();
    Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync();
    Task<AylikRaporDto> AylikAsync(int yil, int ay);
    Task<IReadOnlyList<DonemDto>> DonemlerAsync();
    Task<IReadOnlyList<KanalDto>> KanallarAsync();
    Task<IReadOnlyList<CariDto>> CarilerAsync(string? ara = null);
    Task<IReadOnlyList<IslemDto>> IslemlerAsync(DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null);
    Task<IReadOnlyList<KrediKartiDto>> KrediKartlariAsync();
    Task<IReadOnlyList<GelenDto>> GelenlerAsync(DateOnly? donemStart = null);
    Task<AyarlarDto> AyarlarAsync();

    // Editör mutasyonları (KasaApiClient bunları zaten uyguluyor)
    Task<KanalDto> KanalOlusturAsync(KanalYaz g);
    Task<KanalDto> KanalGuncelleAsync(int id, KanalYaz g);
    Task KanalSilAsync(int id);
    Task<CariDto> CariOlusturAsync(CariYaz g);
    Task<CariDto> CariGuncelleAsync(int id, CariYaz g);
    Task CariSilAsync(int id);
    Task<IslemDto> IslemOlusturAsync(IslemYaz g);
    Task<IslemDto> IslemGuncelleAsync(int id, IslemYaz g);
    Task IslemSilAsync(int id);
    Task<KrediKartiDto> KrediKartiOlusturAsync(KrediKartiYaz g);
    Task<KrediKartiDto> KrediKartiGuncelleAsync(int id, KrediKartiYaz g);
    Task KrediKartiSilAsync(int id);
    Task<IReadOnlyList<KartOdemeDto>> KartOdemelerAsync(int krediKartiId);
    Task<KartOdemeDto> KartOdemeKaydetAsync(KartOdemeYaz g);
    Task KartOdemeSilAsync(int id);
    Task<GelenDto> GelenKaydetAsync(GelenYaz g);
    Task AyarGuncelleAsync(AyarYaz g);
    Task IzleyiciSifreAsync(string yeniSifre);
}
