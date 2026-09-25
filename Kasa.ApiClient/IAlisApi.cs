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
    Task<IReadOnlyList<AliciDto>> AlicilarAsync();
    Task<AliciDto> AliciOlusturAsync(AliciYaz g);
    Task<AliciDto> AliciGuncelleAsync(int id, AliciYaz g);
}
