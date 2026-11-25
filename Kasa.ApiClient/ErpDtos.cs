namespace Kasa.ApiClient;

/// <summary>Cari listesinin öğesi: yalnız ERP12'den aktarılan hareketlerin toplamı. <paramref name="Bekleyen"/>: kasada karşılığı verilmemiş kasa hareketi.</summary>
public record ErpCariDto(string Anahtar, string? Kod, string Ad, decimal Borc, decimal Alacak, decimal Bakiye, int HareketSayisi, int Bekleyen, DateOnly SonTarih);
/// <summary>Carinin hareketi; <paramref name="Durum"/> ErpHareketDurumlari. Eşleşmişse bağlandığı kayıt <paramref name="EslesmeTuru"/>/<paramref name="EslesmeId"/>.</summary>
public record ErpCariHareketDto(int Id, int BelgeId, int? SatirNo, DateOnly Tarih, string? IslemTuru, string? BelgeNo, string? Aciklama, decimal Borc, decimal Alacak, string Durum,
    string? EslesmeTuru = null, int? EslesmeId = null);

/// <summary>ERP12 cari okumaları (/api/erp).</summary>
public interface IErpApi
{
    Task<IReadOnlyList<ErpCariDto>> ErpCarilerAsync(string? ara = null);
    Task<IReadOnlyList<ErpCariHareketDto>> ErpCariHareketleriAsync(string anahtar);
}

public sealed partial class KasaApiClient : IErpApi
{
    public Task<IReadOnlyList<ErpCariDto>> ErpCarilerAsync(string? ara = null) =>
        GetAsync<IReadOnlyList<ErpCariDto>>("api/erp/cariler" + (string.IsNullOrWhiteSpace(ara) ? "" : "?ara=" + Uri.EscapeDataString(ara.Trim())));
    public Task<IReadOnlyList<ErpCariHareketDto>> ErpCariHareketleriAsync(string anahtar) =>
        GetAsync<IReadOnlyList<ErpCariHareketDto>>("api/erp/cariler/hareketler?anahtar=" + Uri.EscapeDataString(anahtar));
}
