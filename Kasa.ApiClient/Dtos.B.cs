namespace Kasa.ApiClient;

// ---------------------------------------------------------------- 21 · Kasa neden değişti?

/// <summary>Kasa dökümünün bir adımı: işaretli tutar (giren +, çıkan −) ve adımdan sonraki kasa.</summary>
/// <param name="Tur">Gelen, CekTahsilat, CariGider, SabitGider, OrtakGider, CekOdemesi, KartOdemesi, ErtelenenKk.</param>
public record KasaDokumAdimiDto(string Tur, string TurAdi, string? Kanal, decimal Tutar, decimal Bakiye);
/// <summary>Açılış + Σ adımlar = kapanış; aralık istenen tarihlerle çakışan dönemlerin tamamıdır.</summary>
public record KasaDokumuDto(DateOnly Baslangic, DateOnly Bitis, decimal Acilis, decimal Kapanis,
    decimal ToplamGiren, decimal ToplamCikan, IReadOnlyList<KasaDokumAdimiDto> Adimlar);

// ---------------------------------------------------------------- 11 · Ay kilidi ve yayın

/// <summary>Yayından sonra değişen rakam (yayındaki / bugünkü; satır yoksa null).</summary>
public record AyFarkiDto(string Kalem, decimal? Eski, decimal? Yeni);
public record AyKapanisDto(int Yil, int Ay, string Etiket, bool Kilitli, DateTime? KilitZamaniUtc, bool Kilitlenebilir,
    bool Yayinlandi, DateTime? YayinZamaniUtc, IReadOnlyList<AyFarkiDto> Farklar, IReadOnlyList<DegisiklikDto> Degisiklikler)
{
    /// <summary>Yayından sonra rakam değişti ya da o aya dokunan kayıt var: kırmızı şerit.</summary>
    public bool YayindanSonraDegisti => Yayinlandi && (Farklar.Count > 0 || Degisiklikler.Count > 0);
}
public record AyKilidiDto(int Yil, int Ay, string Etiket, DateTime KilitZamaniUtc);

// ---------------------------------------------------------------- 04 · Grafikler ve kurlar

/// <summary>Aylık kur/endeks satırı (Ay = ayın ilk günü); boş alan "kur yok"tur.</summary>
public record KurDto(DateOnly Ay, decimal? TufeEndeksi, decimal? UsdTry, decimal? EurTry, decimal? AltinGramTry);
public record KurTcmbSonucDto(KurDto Kur, int GunSayisi, DateOnly IlkGun, DateOnly SonGun);
/// <summary>Kanalın ay geliri (gelen + çek tahsilatı) ve ay sonucu, nominal TL.</summary>
public record GrafikKanalDto(string Kanal, decimal Gelir, decimal AySonucu);
public record GrafikAyDto(int Yil, int Ay, bool TakipOncesi, IReadOnlyList<GrafikKanalDto> Kanallar,
    decimal? TufeEndeksi, decimal? UsdTry, decimal? EurTry, decimal? AltinGramTry);
/// <summary>Seçilen aya kadar 24 ay (eskiden yeniye).</summary>
public record GrafikDto(int Yil, int Ay, IReadOnlyList<string> Kanallar, IReadOnlyList<GrafikAyDto> Aylar);

// ---------------------------------------------------------------- 05 · Hedef ve bütçe

public record KanalHedefDto(int KanalId, string Kanal, bool Aktif, decimal? Hedef, decimal Gerceklesen, decimal? Yuzde);
public record GiderButceDto(int GiderKalemiId, string Kalem, bool Aktif, decimal? Butce, decimal Gerceklesen, decimal? Yuzde, decimal? Sablon);
public record HedefButceDto(int Yil, int Ay, IReadOnlyList<KanalHedefDto> Kanallar, IReadOnlyList<GiderButceDto> Kalemler);
/// <summary>Tutar null → satır silinir.</summary>
public record HedefYaz(int KanalId, decimal? Tutar);
public record ButceYaz(int GiderKalemiId, decimal? Tutar);
public record HedefButceYaz(DateOnly Ay, IReadOnlyList<HedefYaz>? Kanallar, IReadOnlyList<ButceYaz>? Kalemler);
public record KopyalaSonucDto(int Kopyalanan, int Atlanan);

// ---------------------------------------------------------------- 07 · Cari özeti

public enum CariOzetiTuru { Cari, Kalem }
public record CariOzetiAyDto(int Ay, decimal Nakit, decimal KrediKarti, decimal Cek, decimal Toplam, int Adet,
    decimal? Sablon, string? Karar);
public record CariOzetiDto(string Ad, int Yil, string Tur, IReadOnlyList<CariOzetiAyDto> Aylar, decimal Toplam, decimal? SablonToplam);
