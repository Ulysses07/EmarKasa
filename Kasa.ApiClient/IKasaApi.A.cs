namespace Kasa.ApiClient;

// Paket A — panel, nakit tahmini ve bildirim uçları (hepsi okuma; her iki rol).
public partial interface IKasaApi
{
    /// <summary>
    /// Önümüzdeki <paramref name="gun"/> günün (1–366) gün gün nakit tahmini.
    /// <paramref name="haricCekler"/>: hesaptan çıkarılacak (riskli) çeklerin Id'leri.
    /// </summary>
    Task<NakitTahminDto> NakitTahminAsync(int gun, IReadOnlyCollection<int>? haricCekler = null);

    /// <summary>Son iki haftada biten, geleni girilmemiş dönemler (eskiden yeniye).</summary>
    Task<IReadOnlyList<EksikGelenDto>> EksikGelenlerAsync();

    /// <summary>Geçmiş özeti; <paramref name="sonId"/> verilirse ondan sonraki değişiklikler sayılır.</summary>
    Task<GecmisOzetDto> GecmisOzetAsync(int? sonId = null);
}
