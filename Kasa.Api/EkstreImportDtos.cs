namespace Kasa.Api;

public record EkstreBelgeOzetDto(int Id, int Surum, string Kaynak, string Banka, string HesapAdi, int? KartId, string DosyaAdi, DateTimeOffset Yuklendi, int SatirSayisi, int KayitSayisi);
public record EkstreBelgeDto(int Id, int Surum, string Kaynak, string Banka, string HesapAdi, int? KartId, string DosyaAdi, DateTimeOffset Yuklendi, IReadOnlyList<string> Uyarilar, IReadOnlyList<EkstreOkunanSatir> Satirlar, IReadOnlyList<EkstreKayitDto> Kayitlar);
public record EkstreOkunanSatir(int No, int Sayfa, string KaynakSatir, DateOnly? Tarih, string Aciklama, decimal? Tutar, string Yon, string OnerilenIslem, string Sinif, string ParaBirimi, IReadOnlyList<string> Uyarilar);
/// <summary><paramref name="IptalAciklamasi"/> ve <paramref name="IptalZamani"/> yalnız iptal edilmiş satırda doludur; iptal anı denetim izinden
/// okunur, sürüm öncesi iptalde bilinmez (null). <paramref name="EslesmeTuru"/>/<paramref name="EslesmeId"/>: satırın bağlı olduğu mevcut
/// kayıt ('Gider', 'KartHarcama', 'KartTaksidi', 'KartOdeme'). İşlem türü 'Eslestir' olan satır yalnız bağdır; kendi kaydını üretip
/// sonradan alış ödemesine bağlanan Gider/KartHarcama satırında sahiplik (IslemId/KartHarcamaId) boşalır ve eşleşme dolar.
/// <paramref name="EslesmeDurumu"/> iptal edilmemiş eşleşmede 'Eslesti', hedef kayıt silinmiş ya da iptal edilmişse 'KayitYok'.
/// Eski sunucu bu alanları göndermez (null).</summary>
public record EkstreKayitDto(int Id, int SatirNo, DateOnly Tarih, string Aciklama, decimal Tutar, string IslemTuru, string DagilimTuru, IReadOnlyList<TakipKanalPayi> Dagilimlar, int? KrediKartiId, int? IslemId, int? KartHarcamaId, int? KartOdemeId, bool Iptal, string? IptalAciklamasi = null, DateTimeOffset? IptalZamani = null,
    string? EslesmeTuru = null, int? EslesmeId = null, string? EslesmeDurumu = null);
/// <summary><paramref name="IslemTuru"/> 'Eslestir' ise satır <paramref name="EslesenKayitTuru"/>/<paramref name="EslesenKayitId"/> ile
/// gösterilen mevcut kayda bağlanır (DagilimTuru 'Eslesme', dağılım boş): yeni kayıt üretmez, kasa ve kart borcu değişmez.
/// Eski istemci göndermez (null).</summary>
public record EkstreSatirYaz(int SatirNo, DateOnly Tarih, string Aciklama, decimal Tutar, string IslemTuru, string DagilimTuru, IReadOnlyList<KanalPayYaz> Dagilimlar, int? KrediKartiId = null, int? KaynakHarcamaId = null,
    string? EslesenKayitTuru = null, int? EslesenKayitId = null);
/// <summary>POST /api/ekstre-aktar/{id}/eslesme-adaylari gövdesi: satırın tarih ve tutarı.</summary>
public record EkstreEslesmeAdayiSorgu(DateOnly Tarih, decimal Tutar);
/// <summary>Ekstre satırının eşleştirilebileceği mevcut kayıt. <paramref name="Tur"/> EkstreSatirYaz.EslesenKayitTuru değeridir ('Gider',
/// 'KartHarcama', 'KartTaksidi', 'KartOdeme'); taksitte <paramref name="Id"/> taksidin, <paramref name="HarcamaId"/> harcamanın kimliğidir.
/// <paramref name="KanalEtiketi"/> kaydın kasadan düştüğü kanal(lar) ya da "Genel kasa"/"Dağılım bekliyor"; <paramref name="EkstreKayitId"/>
/// kaydı üreten ekstre satırı; <paramref name="AlisId"/> kaydın bağlı olduğu alış.</summary>
public record EkstreEslesmeAdayiDto(string Tur, int Id, DateOnly Tarih, decimal Tutar, string Aciklama, int? KrediKartiId, string? KanalEtiketi = null, int? AlisId = null, int? EkstreKayitId = null,
    int? HarcamaId = null, int? TaksitNo = null, int? TaksitSayisi = null);
public record EkstreKaydetYaz(Guid IstekId, int Surum, IReadOnlyList<EkstreSatirYaz> Satirlar, string? OnizlemeOzeti = null, bool TekrarOnay = false);
public record EkstreSatirOnizleme(int SatirNo, DateOnly Tarih, string Aciklama, decimal Tutar, string IslemTuru, decimal KasaEtkisi, IReadOnlyList<TakipKanalPayi> Dagilimlar, IReadOnlyList<string> Uyarilar);
public record EkstreOnizlemeDto(string OnizlemeOzeti, decimal KasaEtkisi, IReadOnlyList<EkstreSatirOnizleme> Satirlar, IReadOnlyList<string> Uyarilar, bool TekrarOnayGerekli);
public record EkstreIptalYaz(Guid IstekId, string Aciklama);
public record EkstreOkumaSonucu(IReadOnlyList<EkstreOkunanSatir> Satirlar, IReadOnlyList<string> Uyarilar);
