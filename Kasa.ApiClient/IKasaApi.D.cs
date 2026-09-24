namespace Kasa.ApiClient;

/// <summary>Paket D uçları (çek/senet, tekrarlayan gider ikinci adım, kart mutabakatı, kasa sayımı).</summary>
public partial interface IKasaApi
{
    /// <summary>Portföydeki evrakı tek dokunuşla tahsil / ciro / karşılıksız / ödendi yapar (PUT ile aynı kayıt).</summary>
    Task<CekDto> CekDurumAsync(int id, CekDurumYaz g);
    /// <summary>Portföydeki alınan evrakın keşideci ve bankaya göre dağılımı.</summary>
    Task<CekRiskDto> CekRiskAsync(CekTuru? tur = null);

    /// <summary>Bu ay ve önceki 2 ayın atlanan tekrarlayan giderleri (en yeni önce).</summary>
    Task<IReadOnlyList<TekrarlayanAtlananDto>> AtlananGiderlerAsync();
    /// <summary>"Bu ay atla" kararını geri alır; ay yeniden bekleyene döner.</summary>
    Task TekrarlayanAtlamayiGeriAlAsync(int id, DateOnly ay);
    Task<IReadOnlyList<TekrarlayanHazirDto>> TekrarlayanHazirlarAsync();
    /// <summary>Hazır şablonu ekler (gider kalemi yoksa o da eklenir); eklenen şablonları döner. Zaten varsa 409.</summary>
    Task<IReadOnlyList<TekrarlayanGiderDto>> TekrarlayanHazirEkleAsync(string kod);

    /// <summary>Kartın kapanmış ekstre dönemleri (en yeni önce) ve mutabakat özetleri.</summary>
    Task<IReadOnlyList<KartDonemDto>> KartDonemleriAsync(int krediKartiId, int? adet = null);
    Task<KartMutabakatDetayDto> KartMutabakatAsync(int krediKartiId, DateOnly kesim);
    Task<KartMutabakatDetayDto> KartMutabakatKaydetAsync(KartMutabakatYaz g);
    Task KartMutabakatSilAsync(int id);

    /// <summary>Sayım farkının durumu ve açıklaması (fark yoksa 400).</summary>
    Task<KasaSayimDto> SayimFarkiAsync(int id, SayimFarkYaz g);
    /// <summary>Sayımdan sonra sayım gününü etkileyen değişiklikler.</summary>
    Task<NedenDegistiDto> SayimNedenDegistiAsync(int id);
    /// <summary>Son kasa sayımının tarihi ve üzerinden geçen gün.</summary>
    Task<SonSayimDto> SonSayimAsync();
}
