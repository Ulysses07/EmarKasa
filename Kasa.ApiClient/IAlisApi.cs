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
    /// Bütün gider geçmişi çekilmez. <paramref name="aramaTutari"/> arama metninin tutar okumasıdır: gider metne ya da bu tutara
    /// uyarsa gelir (<paramref name="tutar"/> ise kesin süzgeçtir). Eski sunucuda (uç yoksa 404 ya da 405) eski gider listesinden
    /// istemcide süzülmüş tek sayfa döner.</summary>
    Task<BaglanabilirGiderSayfasi> BaglanabilirGiderlerAsync(string? arama = null, decimal? tutar = null, DateOnly? baslangic = null, DateOnly? bitis = null, string? imlec = null, int? limit = null, decimal? aramaTutari = null);
    /// <summary>Takipli kartla ödemede <c>MevcutKartHarcamaId</c> ile bağlanabilecek kart harcamaları (gidere bağlı olmayan, en yeni
    /// 50; <paramref name="tutar"/> verilirse yalnız o tutar). Eski sunucuda (uç yok) boş liste döner.</summary>
    Task<IReadOnlyList<BaglanabilirKartHarcamasiDto>> BaglanabilirKartHarcamalariAsync(int krediKartiId, decimal? tutar = null);
    Task<IReadOnlyList<AliciDto>> AlicilarAsync();
    Task<AliciDto> AliciOlusturAsync(AliciYaz g);
    Task<AliciDto> AliciGuncelleAsync(int id, AliciYaz g);
}
