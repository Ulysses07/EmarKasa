namespace Kasa.ApiClient;

/// <summary>Paket B: raporlar ve ay kapanışı.</summary>
public partial interface IKasaApi
{
    /// <summary>"Kasa neden değişti?": aralıkla çakışan dönemlerin açılıştan kapanışa dökümü (takvim dışı → 400).</summary>
    Task<KasaDokumuDto> KasaDokumuAsync(DateOnly baslangic, DateOnly bitis);
    Task<IndirilenDosya> KasaDokumuCsvAsync(DateOnly baslangic, DateOnly bitis);

    /// <summary>
    /// <see cref="IslemSayfasiAsync"/> gibi, ama gider tipine göre süzülmüş (rapordan iniş): toplam kayıt
    /// sayısı ve sayfalar süzülmüş listeye göredir.
    /// </summary>
    Task<IslemSayfasi> IslemSayfasiTipeGoreAsync(DateOnly? baslangic, DateOnly? bitis, string? kanal, IslemTipSuzgeci tip, int limit, int offset);
    /// <summary><see cref="IslemlerCsvAsync"/> gibi, gider tipine göre süzülmüş.</summary>
    Task<IndirilenDosya> IslemlerCsvTipeGoreAsync(DateOnly? baslangic, DateOnly? bitis, string? kanal, IslemTipSuzgeci tip);

    /// <summary>Ayın kilit/yayın durumu, yayından sonraki farklar ve o aya dokunan geçmiş satırları.</summary>
    Task<AyKapanisDto> AyKapanisiAsync(int yil, int ay);
    Task<IReadOnlyList<AyKilidiDto>> AyKilitleriAsync();
    /// <summary>
    /// Editör: bitmiş ayı kilitler (kilitli aya kayıt eklenemez/değiştirilemez/silinemez). Kasa aydan aya
    /// devrettiği için takip başlangıcından bu yana önceki kilitsiz aylar da kilitlenir.
    /// </summary>
    Task<AyKapanisDto> AyiKilitleAsync(int yil, int ay);
    /// <summary>Editör: ayın ve sonraki kilitli ayların kilidini açar.</summary>
    Task<AyKapanisDto> AyKilidiniAcAsync(int yil, int ay);
    /// <summary>Editör: bitmiş ayın bugünkü rakamlarını anlık görüntü olarak saklar (yeniden yayın görüntüyü yeniler).</summary>
    Task<AyKapanisDto> AyiYayinlaAsync(int yil, int ay);

    /// <summary>Tek sayfalık yazdırılabilir aylık rapor (HTML).</summary>
    Task<IndirilenDosya> AylikYazdirAsync(int yil, int ay);
    /// <summary>Ay sonu paketi (ZIP): işlemler, raporlar, gelenler, çekler, kart ödemeleri, sayımlar, geçmiş, muhasebeci listesi.</summary>
    Task<IndirilenDosya> AyPaketiAsync(int yil, int ay);
    /// <summary>Çek/senet listesi; <paramref name="konum"/> yalnız alınan evrakta aranır (Çekler sayfasındaki gibi).</summary>
    Task<IndirilenDosya> CeklerCsvAsync(CekYonu? yon = null, CekDurumu? durum = null, DateOnly? baslangic = null, DateOnly? bitis = null,
        CekTuru? tur = null, CekKonumu? konum = null);
    Task<IndirilenDosya> KasaSayimlariCsvAsync();
    Task<IndirilenDosya> GecmisCsvAsync(string? tur = null);

    /// <summary>Aylık kur/endeks tablosu (en yeni ay önce).</summary>
    Task<IReadOnlyList<KurDto>> KurlarAsync();
    /// <summary>Editör: ayın satırını yazar; tüm değerler boşsa satır silinir.</summary>
    Task<KurDto> KurKaydetAsync(KurDto g);
    /// <summary>Editör: USD/EUR'yu TCMB'den ayın iş günü ortalamasıyla doldurur (TÜFE/altına dokunmaz).</summary>
    Task<KurTcmbSonucDto> KurTcmbDoldurAsync(DateOnly ay);
    /// <summary>Seçilen aya kadar 24 ayın kanal geliri ve ay sonucu + kur satırları.</summary>
    Task<GrafikDto> GrafikAsync(int yil, int ay);

    Task<HedefButceDto> HedefButceAsync(int yil, int ay);
    Task<HedefButceDto> HedefButceKaydetAsync(HedefButceYaz g);
    /// <summary>Geçen ayın hedef/bütçelerini bu aya kopyalar (var olanlar korunur; geçen ay boşsa 400).</summary>
    Task<KopyalaSonucDto> HedefButceKopyalaAsync(DateOnly ay);

    /// <summary>Cari (ya da sabit gider kalemi) için yılın ay ay özeti.</summary>
    Task<CariOzetiDto> CariOzetiAsync(string ad, int yil, CariOzetiTuru tur);
}
