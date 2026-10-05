namespace Kasa.ApiClient;

/// <summary>İşlem listesini sunucudan sınırlı parçalar halinde okuyan isteğe bağlı istemci yüzeyi.</summary>
public interface IIslemSayfalamaApi
{
    Task<IslemSayfasiDto> IslemlerSayfasiAsync(DateOnly? baslangic = null, DateOnly? bitis = null,
        string? kanal = null, string? cari = null, string? imlec = null, int limit = 100,
        CancellationToken ct = default);
}
