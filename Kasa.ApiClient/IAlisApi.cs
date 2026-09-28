namespace Kasa.ApiClient;

public interface IAlisApi
{
    Task<IReadOnlyList<AlisKanalDto>> AlisKanallariAsync();
    Task<IReadOnlyList<AlisDto>> AlislarAsync();
    Task<AlisDto> AlisOlusturAsync(AlisYaz g);
    Task<AlisDto> AlisGuncelleAsync(int id, AlisYaz g);
    Task<AlisDto> AlisGonderAsync(int id, AlisDurumYaz g);
    Task<AlisDto> AlisOnaylaAsync(int id, AlisDurumYaz g);
    Task<AlisDto> AlisIadeAsync(int id, AlisDurumYaz g);
    Task<AlisDto> AlisOdemeKaydetAsync(int id, AlisOdemeYaz g);
    /// <summary>Ödemeye bağlanabilecek mevcut giderler (yalnız editör): tarih, tutar ve açıklama/not süzgeciyle, imleçli sayfa.
    /// Bütün gider geçmişi çekilmez. Eski sunucuda (uç yoksa 404) eski gider listesinden istemcide süzülmüş tek sayfa döner.</summary>
    Task<BaglanabilirGiderSayfasi> BaglanabilirGiderlerAsync(string? arama = null, decimal? tutar = null, DateOnly? baslangic = null, DateOnly? bitis = null, string? imlec = null, int? limit = null);
    Task<IReadOnlyList<AliciDto>> AlicilarAsync();
    Task<AliciDto> AliciOlusturAsync(AliciYaz g);
    Task<AliciDto> AliciGuncelleAsync(int id, AliciYaz g);
}
