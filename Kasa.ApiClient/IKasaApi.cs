namespace Kasa.ApiClient;

/// <summary>KasaApiClient'in test edilebilir yüzeyi (VM'ler buna bağlanır).</summary>
public interface IKasaApi
{
    /// <summary>
    /// Sunucu oturumu geçersiz saydığında (token'lı istek 401 aldı) ya da tüm oturumlar
    /// kapatıldığında tetiklenir. Token bu olaydan önce depodan silinmiş olur.
    /// </summary>
    event EventHandler<OturumBitisNedeni>? OturumSonaErdi;

    Task<LoginYanit> LoginAsync(string? kullanici, string sifre);
    Task<string?> BenKimAsync();
    /// <summary>Sunucuya çıkış bildirir; sunucuya ulaşılamasa ya da 401 dönse bile yerel token silinir.</summary>
    Task CikisAsync();

    Task<PanelDto> PanelAsync();
    Task<IReadOnlyList<HaftalikOzetDto>> HaftalikAsync();
    Task<AylikRaporDto> AylikAsync(int yil, int ay);
    Task<IReadOnlyList<DonemDto>> DonemlerAsync();
    Task<IReadOnlyList<KanalDto>> KanallarAsync();
    Task<IReadOnlyList<CariDto>> CarilerAsync(string? ara = null);
    /// <summary>Sabit gider kalemleri (Kira, SGK, Maaş…), ada göre sıralı.</summary>
    Task<IReadOnlyList<GiderKalemiDto>> GiderKalemleriAsync();
    /// <param name="limit">İsteğe bağlı sayfa boyu (sunucu destekliyorsa).</param>
    /// <param name="offset">İsteğe bağlı atlanacak kayıt sayısı (sunucu destekliyorsa).</param>
    Task<IReadOnlyList<IslemDto>> IslemlerAsync(DateOnly? baslangic = null, DateOnly? bitis = null, string? kanal = null, string? cari = null,
        int? limit = null, int? offset = null);
    /// <summary>
    /// İşlem listesinin bir sayfası (sunucu sırası: tarih, id artan) ve filtreye uyan toplam kayıt
    /// sayısı (<c>X-Toplam-Kayit</c> başlığı).
    /// </summary>
    Task<IslemSayfasi> IslemSayfasiAsync(DateOnly? baslangic, DateOnly? bitis, string? kanal, string? cari, int limit, int offset);
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
    Task<GiderKalemiDto> GiderKalemiOlusturAsync(GiderKalemiYaz g);
    Task<GiderKalemiDto> GiderKalemiGuncelleAsync(int id, GiderKalemiYaz g);
    Task GiderKalemiSilAsync(int id);

    /// <summary>Tekrarlayan gider kayıtları (her iki rol okur), kaleme göre sıralı.</summary>
    Task<IReadOnlyList<TekrarlayanGiderDto>> TekrarlayanGiderlerAsync();
    /// <summary>Girilmesi bekleyen tekrarlayan giderler (bu ay ve önceki 2 ay, vadesi gelmiş), vadeye göre sıralı.</summary>
    Task<IReadOnlyList<BekleyenGiderDto>> BekleyenGiderlerAsync();
    Task<TekrarlayanGiderDto> TekrarlayanGiderOlusturAsync(TekrarlayanGiderYaz g);
    Task<TekrarlayanGiderDto> TekrarlayanGiderGuncelleAsync(int id, TekrarlayanGiderYaz g);
    Task TekrarlayanGiderSilAsync(int id);
    /// <summary>Bekleyen ayı sabit gider işlemi olarak girer; oluşan işlemi döner. Aynı ay için ikinci karar 409.</summary>
    Task<IslemDto> TekrarlayanOnaylaAsync(int id, TekrarlayanOnayYaz g);
    /// <summary>Bekleyen ayı işlem girmeden kapatır. Aynı ay için ikinci karar 409.</summary>
    Task TekrarlayanAtlaAsync(int id, DateOnly ay);
    Task<IslemDto> IslemOlusturAsync(IslemYaz g);
    Task<IslemDto> IslemGuncelleAsync(int id, IslemYaz g);
    Task IslemSilAsync(int id);
    Task<KrediKartiDto> KrediKartiOlusturAsync(KrediKartiYaz g);
    Task<KrediKartiDto> KrediKartiGuncelleAsync(int id, KrediKartiYaz g);
    Task KrediKartiSilAsync(int id);
    Task<IReadOnlyList<KartOdemeDto>> KartOdemelerAsync(int krediKartiId);
    /// <summary>Tüm kartların ödemeleri tek istekte (sunucu en yeni önce döner).</summary>
    Task<IReadOnlyList<KartOdemeDto>> TumKartOdemeleriAsync();
    Task<KartOdemeDto> KartOdemeKaydetAsync(KartOdemeYaz g);
    Task KartOdemeSilAsync(int id);
    Task<GelenDto> GelenKaydetAsync(GelenYaz g);
    Task AyarGuncelleAsync(AyarYaz g);
    Task IzleyiciSifreAsync(string yeniSifre);
    /// <summary>Tüm cihazlardaki oturumları (bu cihaz dahil) kapatır; başarıda yerel token silinir ve <see cref="OturumSonaErdi"/> tetiklenir.</summary>
    Task OturumlariKapatAsync();
}

/// <summary>Oturumun neden sona erdiği.</summary>
public enum OturumBitisNedeni
{
    /// <summary>Sunucu token'ı reddetti (401): süresi doldu, şifre değişti ya da oturumlar başka cihazdan kapatıldı.</summary>
    Yetkisiz,
    /// <summary>Bu cihazdan "Tüm oturumları kapat" çalıştırıldı.</summary>
    OturumlarKapatildi,
}
