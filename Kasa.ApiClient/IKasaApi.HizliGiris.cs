namespace Kasa.ApiClient;

// "Hızlı ve hatasız giriş" (paket C) istemci yüzeyi.
public partial interface IKasaApi
{
    /// <summary>
    /// Gelişmiş süzgeçli işlem sayfası (not, tip, kart, tutar aralığı) + toplam kayıt. Süzgeçler boşsa
    /// <see cref="IslemSayfasiAsync"/> ile aynı isteği yapar.
    /// </summary>
    Task<IslemSayfasi> IslemAraAsync(IslemAramasi arama, int limit, int offset);
    /// <summary>Excel'e aktar, <see cref="IslemAraAsync"/> ile aynı süzgeçle (sayfalama yok).</summary>
    Task<IndirilenDosya> IslemAramaCsvAsync(IslemAramasi arama);

    /// <summary>
    /// Kaydetmeden önceki uyarılar (aynı tutar, olağan dışı tutar, eski tarih, çek çift düşme). Uç yoksa
    /// (eski sunucu: 404/405) boş liste döner.
    /// </summary>
    /// <param name="haricId">Düzenlenen işlemin Id'si (kendisiyle çift sayılmasın).</param>
    Task<IReadOnlyList<IslemUyariDto>> IslemUyarilariAsync(IslemYaz g, int? haricId = null);

    /// <summary>
    /// Satırları tek transaction'da ekler (ya hepsi ya hiçbiri). Satır hataları istisna değil sonuçtur
    /// (<see cref="TopluIslemSonucu.Kaydedildi"/> false); diğer hatalar <see cref="KasaApiException"/>.
    /// </summary>
    Task<TopluIslemSonucu> TopluIslemKaydetAsync(IReadOnlyList<IslemYaz> satirlar, bool yeniCarileriEkle);

    /// <summary>Carinin (ya da sabit gider kaleminin) son işlemi; hiç işlemi yoksa (ya da eski sunucu) null.</summary>
    Task<IslemOneriDto?> IslemOnerisiAsync(string cari);

    /// <summary>
    /// Geleni, kullanıcının gördüğü tutar (<paramref name="beklenenTutar"/>; kayıt yoksa 0) hâlâ kayıtlıysa yazar.
    /// Kayıt o arada değiştiyse yazmaz ve güncel tutarı döner (409). Çakışma olmayan 409 (ay kilitli)
    /// <see cref="KasaApiException"/> olarak fırlar; mesajı sunucununkidir.
    /// </summary>
    Task<GelenKayitSonucu> GelenKorumaliKaydetAsync(GelenYaz g, decimal beklenenTutar);

    /// <summary>Dönemin bütün kanallarının geleni; <paramref name="donemStart"/> null ise bugünün dönemi.</summary>
    Task<GelenTablosuDto> GelenTablosuAsync(DateOnly? donemStart = null);

    /// <summary>Bitmiş dönemlerde girilmemiş gelenler (en yeni önce).</summary>
    Task<EksikGelenSayfasi> EksikGelenListesiAsync();

    /// <summary>Kaydın son silinme satırı (Geri al için); yoksa null.</summary>
    /// <param name="tur">Geçmiş tür adı (<see cref="GecmisTurAdlari"/>).</param>
    Task<DegisiklikDto?> SonSilmeAsync(string tur, int kayitId);
}
