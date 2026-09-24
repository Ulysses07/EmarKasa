namespace Kasa.ApiClient;

/// <summary>Paket E: kişisel giriş, hesabım, kullanıcılar, oturumlar, sorular, risk kartı.</summary>
public partial interface IKasaApi
{
    /// <summary>
    /// Giriş (kişisel hesap, .env editörü ya da ortak izleyici şifresi). İki adımlı giriş açıksa ve
    /// kod verilmediyse (ya da kod hatalıysa) token alınmaz, <see cref="GirisSonucu.KodGerekli"/> döner.
    /// Başarıda token saklanır.
    /// </summary>
    Task<GirisSonucu> GirisAsync(string? kullanici, string sifre, string? kod);
    /// <summary>Oturumdaki kişi (rol, ad).</summary>
    Task<BenDto> BenAsync();

    // Hesabım (editör)
    Task<HesapDto> HesabimAsync();
    /// <summary>Şifre değişir; diğer cihazlar çıkar, bu cihazın yeni token'ı saklanır. Yanlış mevcut şifre 400.</summary>
    Task SifremiDegistirAsync(string mevcutSifre, string yeniSifre);
    Task<IkiAdimKurulumDto> IkiAdimBaslatAsync();
    /// <summary>
    /// Şifreyi ve ilk kodu doğrular, iki adımlı girişi açar; kurtarma kodlarını döner (bir kez gösterilir).
    /// Yeni token saklanır. Şifre istenir: yalnız token'ı ele geçiren iki adımı açıp sahibini dışarıda bırakamasın.
    /// </summary>
    Task<IReadOnlyList<string>> IkiAdimOnaylaAsync(string sifre, string kod);
    Task IkiAdimKapatAsync(string sifre, string kod);
    /// <summary>Şifre ve uygulamadaki güncel kodla yeni kurtarma kodları (eskiler geçersiz olur).</summary>
    Task<IReadOnlyList<string>> KurtarmaKodlariYenileAsync(string sifre, string kod);

    // Kullanıcılar (editör)
    Task<IReadOnlyList<KullaniciDto>> KullanicilarAsync();
    Task<KullaniciDto> KullaniciEkleAsync(KullaniciEkle g);
    Task KullaniciGuncelleAsync(int id, KullaniciGuncelle g);
    Task KullaniciSifreAsync(int id, string yeniSifre);
    Task KullaniciOturumlariniKapatAsync(int id);
    Task KullaniciIkiAdimKapatAsync(int id);
    Task KullaniciSilAsync(int id);
    /// <summary>Ortak izleyici şifresini kaldırır; o şifreyle açılmış oturumlar kapanır.</summary>
    Task IzleyiciSifresiniKaldirAsync();

    // Oturumlar ve giriş günlüğü (editör)
    Task<IReadOnlyList<OturumDto>> OturumlarAsync();
    Task OturumKapatAsync(string oturumId);
    Task<GirisKaydiSayfasi> GirisKayitlariAsync(bool yalnizBasarisiz, int limit, int offset);
    Task<GuvenlikAyariDto> GuvenlikAyariAsync();
    Task GuvenlikAyariKaydetAsync(int editorOturumGun);

    // Sorular (okuma ve soru sorma her iki rol; cevap/kapat/aç/sil editör)
    Task<IReadOnlyList<SoruDto>> SorularAsync(SoruDurumu? durum = null, SoruHedefTuru? hedefTur = null, int? hedefId = null, DateOnly? hafta = null);
    Task<SoruOzetDto> SoruOzetAsync();
    Task<SoruDto> SoruSorAsync(SoruYaz g);
    Task<SoruDto> SoruCevaplaAsync(int id, string cevap, bool kapat);
    Task<SoruDto> SoruKapatAsync(int id);
    Task<SoruDto> SoruAcAsync(int id);
    Task SoruSilAsync(int id);

    // Sistem ve risk kartı (editör)
    Task<SistemRiskDto> SistemRiskiAsync();
}
