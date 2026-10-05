namespace Kasa.ApiClient;

/// <summary>Sonraki sayfa imleci yalnız aynı süzgeçle kullanılmalıdır; sayfa tutarları bütün listenin toplamı değildir.</summary>
public record IslemSayfasiDto(IReadOnlyList<IslemDto> Kayitlar, string? SonrakiImlec, bool DevamVar);
