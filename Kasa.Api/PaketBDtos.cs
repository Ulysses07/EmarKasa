using Kasa.Core;

namespace Kasa.Api;

// ---------------------------------------------------------------- 21 · Kasa neden değişti?

/// <summary>Kasa dökümünün bir adımı: işaretli tutar (giren +, çıkan −) ve adımdan sonraki kasa.</summary>
public record KasaDokumAdimiDto(KasaKalemTuru Tur, string TurAdi, string? Kanal, decimal Tutar, decimal Bakiye);

/// <summary>Açılış + Σ adımlar = kapanış. Aralık, istenen tarihlerle çakışan dönemlerin tamamıdır.</summary>
public record KasaDokumuDto(DateOnly Baslangic, DateOnly Bitis, decimal Acilis, decimal Kapanis,
    decimal ToplamGiren, decimal ToplamCikan, IReadOnlyList<KasaDokumAdimiDto> Adimlar);

// ---------------------------------------------------------------- 11 · Ay kilidi ve yayın

public record AyIstekDto(int Yil, int Ay);

/// <summary>Yayından sonra değişen rakam (yayındaki / bugünkü; satır yoksa null).</summary>
public record AyFarkiDto(string Kalem, decimal? Eski, decimal? Yeni);

/// <summary>
/// Ayın kapanış durumu. <see cref="Farklar"/> yayındaki anlık görüntüyle bugünkü rakamlar arasındaki
/// farklar; <see cref="Degisiklikler"/> yayından sonra o aya dokunan geçmiş satırları (en yeni önce).
/// </summary>
public record AyKapanisDto(int Yil, int Ay, string Etiket, bool Kilitli, DateTime? KilitZamaniUtc, bool Kilitlenebilir,
    bool Yayinlandi, DateTime? YayinZamaniUtc, IReadOnlyList<AyFarkiDto> Farklar, IReadOnlyList<DegisiklikDto> Degisiklikler);

public record AyKilidiDto(int Yil, int Ay, string Etiket, DateTime KilitZamaniUtc);

/// <summary>Yayınlanan ayın anlık görüntüsü (AyYayinEntity.AnlikJson).</summary>
public record AyAnlikGoruntusu(IReadOnlyList<KanalAylik> Kanallar, decimal? KasaAcilis, decimal? KasaKapanis);

// ---------------------------------------------------------------- 04 · Grafikler ve kurlar

/// <summary>Aylık kur/endeks satırı; boş alan "kur yok"tur.</summary>
public record KurDto(DateOnly Ay, decimal? TufeEndeksi, decimal? UsdTry, decimal? EurTry, decimal? AltinGramTry);
public record KurTcmbIstekDto(DateOnly Ay);
/// <summary>TCMB'den doldurulan ayın satırı ve ortalamaya giren iş günü sayısı.</summary>
public record KurTcmbSonucDto(KurDto Kur, int GunSayisi, DateOnly IlkGun, DateOnly SonGun);

/// <summary>Kanalın ay geliri (gelen + çek tahsilatı) ve ay sonucu (nominal TL).</summary>
public record GrafikKanalDto(string Kanal, decimal Gelir, decimal AySonucu);
public record GrafikAyDto(int Yil, int Ay, bool TakipOncesi, IReadOnlyList<GrafikKanalDto> Kanallar,
    decimal? TufeEndeksi, decimal? UsdTry, decimal? EurTry, decimal? AltinGramTry);
/// <summary>Seçilen aya kadar 24 ay (son 12 ay + bir önceki yılın aynı ayları), eskiden yeniye.</summary>
public record GrafikDto(int Yil, int Ay, IReadOnlyList<string> Kanallar, IReadOnlyList<GrafikAyDto> Aylar);

// ---------------------------------------------------------------- 05 · Hedef ve bütçe

/// <summary>Kanal hedefi: gerçekleşen = gelen + çek tahsilatı (aylık raporla aynı). Yüzde hedef yoksa null.</summary>
public record KanalHedefDto(int KanalId, string Kanal, bool Aktif, decimal? Hedef, decimal Gerceklesen, decimal? Yuzde);
/// <summary>Kalem bütçesi: gerçekleşen = ayın kartsız sabit gider işlemleri; şablon = aktif tekrarlayan giderler.</summary>
public record GiderButceDto(int GiderKalemiId, string Kalem, bool Aktif, decimal? Butce, decimal Gerceklesen, decimal? Yuzde, decimal? Sablon);
public record HedefButceDto(int Yil, int Ay, IReadOnlyList<KanalHedefDto> Kanallar, IReadOnlyList<GiderButceDto> Kalemler);
public record HedefYazDto(int KanalId, decimal? Tutar);
public record ButceYazDto(int GiderKalemiId, decimal? Tutar);
/// <summary>Ayın hedef/bütçeleri; Tutar null olan satır silinir.</summary>
public record HedefButceYazDto(DateOnly Ay, IReadOnlyList<HedefYazDto>? Kanallar, IReadOnlyList<ButceYazDto>? Kalemler);
public record HedefKopyalaDto(DateOnly Ay);
public record KopyalaSonucDto(int Kopyalanan, int Atlanan);

// ---------------------------------------------------------------- 07 · Cari özeti

/// <summary>
/// Cari/kalem özetinin bir ayı. Cari için: Nakit = Cari tipli işlemler, KrediKarti = karta bağlı/K.K
/// işlemler, Cek = ödenen verilen çekler (kişi adı eşleşen). Kalem için: Nakit = kartsız sabit gider
/// işlemleri, Sablon = o ay aktif tekrarlayan giderlerin tutarı, Karar = tekrarlayan gider kararı.
/// </summary>
public record CariOzetiAyDto(int Ay, decimal Nakit, decimal KrediKarti, decimal Cek, decimal Toplam, int Adet,
    decimal? Sablon, string? Karar);
public record CariOzetiDto(string Ad, int Yil, string Tur, IReadOnlyList<CariOzetiAyDto> Aylar, decimal Toplam, decimal? SablonToplam);
