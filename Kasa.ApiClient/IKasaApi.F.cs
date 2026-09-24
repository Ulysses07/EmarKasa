namespace Kasa.ApiClient;

// Paket F: belge, işlem ekleri, fatura takibi, muhasebeci listesi ve POS.
public partial interface IKasaApi
{
    // İşlem ekleri (liste/indirme her iki rol; yükleme/silme editör)
    Task<IReadOnlyList<EkDto>> EklerAsync(int islemId);
    /// <summary>Tek dosya yükler (JPG/PNG/WEBP/HEIC/PDF, en fazla 10 MB, işlem başına 10 ek).</summary>
    Task<EkDto> EkYukleAsync(int islemId, string dosyaAdi, byte[] icerik);
    Task<IndirilenDosya> EkIndirAsync(int ekId);
    Task EkSilAsync(int ekId);

    /// <summary>Yalnız belge alanlarını günceller (ör. "fatura geldi").</summary>
    Task<IslemDto> BelgeGuncelleAsync(int islemId, BelgeBilgisi belge);
    Task<FaturaTakibiDto> FaturaTakibiAsync(int yil, int ay);
    /// <summary>Ay sonu muhasebeci listesi (CSV).</summary>
    Task<IndirilenDosya> MuhasebeciCsvAsync(int yil, int ay);

    // POS (okuma her iki rol; yazma editör). Kasa ve kârlılığa girmez.
    Task<IReadOnlyList<PosTanimDto>> PosTanimlariAsync();
    Task<PosTanimDto> PosTanimOlusturAsync(PosTanimYaz g);
    Task<PosTanimDto> PosTanimGuncelleAsync(int id, PosTanimYaz g);
    Task PosTanimSilAsync(int id);
    Task<IReadOnlyList<PosSatisDto>> PosSatislariAsync(DateOnly? baslangic = null, DateOnly? bitis = null, int? posId = null);
    Task<PosSatisDto> PosSatisOlusturAsync(PosSatisYaz g);
    Task<PosSatisDto> PosSatisGuncelleAsync(int id, PosSatisYaz g);
    Task PosSatisSilAsync(int id);
    Task<PosOzetDto> PosOzetAsync(int yil, int ay);
}
