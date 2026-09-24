namespace Kasa.ApiClient;

// "Hızlı ve hatasız giriş" (paket C) gövdeleri ve yanıtları.

/// <summary>
/// İşlem listesinin gelişmiş süzgeci. <see cref="Baslangic"/>…<see cref="Cari"/> eski parametrelerdir;
/// diğerleri isteğe bağlıdır ve eski sunucu onları yok sayar.
/// </summary>
public record IslemAramasi(DateOnly? Baslangic = null, DateOnly? Bitis = null, string? Kanal = null, string? Cari = null,
    string? NotAra = null, GiderTipi? Tip = null, int? KartId = null, decimal? MinTutar = null, decimal? MaxTutar = null)
{
    /// <summary>Eski parametreler dışında bir süzgeç var mı.</summary>
    public bool GelismisVar => !string.IsNullOrWhiteSpace(NotAra) || Tip is not null || KartId is not null
                               || MinTutar is not null || MaxTutar is not null;
}

/// <summary>Kaydetmeden önceki uyarı (bilgi; kaydı engellemez). <see cref="Kod"/>: <see cref="IslemUyariKodlari"/>.</summary>
public record IslemUyariDto(string Kod, string Mesaj);

public static class IslemUyariKodlari
{
    public const string AyniTutar = "AyniTutar";
    public const string OlaganDisiTutar = "OlaganDisiTutar";
    public const string EskiTarih = "EskiTarih";
    public const string CekCiftDusme = "CekCiftDusme";
}

/// <summary>Toplu yüklemede reddedilen satır (<see cref="Sira"/> gönderilen sırayla 1'den başlar).</summary>
public record TopluSatirHatasi(int Sira, string Hata);

/// <summary>
/// Toplu yükleme sonucu. <see cref="Kaydedildi"/> false ise hiçbir satır yazılmamıştır; <see cref="SatirHatalari"/>
/// hangi satırın neden reddedildiğini söyler.
/// </summary>
public record TopluIslemSonucu(bool Kaydedildi, int Eklenen, decimal Toplam, IReadOnlyList<string> YeniCariler,
    IReadOnlyList<IslemDto> Islemler, string? Hata, IReadOnlyList<TopluSatirHatasi> SatirHatalari);

/// <summary>Carinin son işlemi (kanal/tip önerisi).</summary>
public record IslemOneriDto(string Cari, DateOnly Tarih, decimal TutarTl, string Kanal, GiderTipi Tip, int? KrediKartiId);

/// <summary>
/// Korumalı gelen kaydı sonucu: <see cref="Kaydedildi"/> false ise kayıt siz açtıktan sonra değişmiştir;
/// <see cref="MevcutTutar"/> şu anki kayıtlı tutardır (kayıt yoksa 0).
/// </summary>
public record GelenKayitSonucu(bool Kaydedildi, GelenDto? Gelen, decimal MevcutTutar, string? Mesaj);

/// <summary>Gelen tablosunun kanal satırı; kayıt yoksa <see cref="TutarTl"/> null.</summary>
public record GelenHucreDto(string Kanal, bool Aktif, decimal? TutarTl, int? GelenId);

/// <summary>Bir dönemin bütün kanallarının geleni; <see cref="Onceki"/>/<see cref="Sonraki"/> komşu dönem başları.</summary>
public record GelenTablosuDto(DateOnly DonemStart, DateOnly DonemEnd, DateOnly? Onceki, DateOnly? Sonraki,
    IReadOnlyList<GelenHucreDto> Satirlar);

/// <summary>Bitmiş bir dönemde aktif kanalın geleni girilmemiş.</summary>
public record EksikGelenSatiriDto(DateOnly DonemStart, DateOnly DonemEnd, string Kanal);

/// <summary>Eksik gelenler (en yeni önce, sunucu sınırına kadar) ve toplam sayısı.</summary>
public record EksikGelenSayfasi(IReadOnlyList<EksikGelenSatiriDto> Kayitlar, int Toplam);

/// <summary>Değişiklik geçmişindeki tür adları (sunucudaki <c>GecmisTurleri</c> ile aynı).</summary>
public static class GecmisTurAdlari
{
    public const string Islem = "İşlem";
    public const string Gelen = "Gelen";
    public const string Cari = "Cari";
    public const string Kanal = "Kanal";
    public const string KrediKarti = "Kredi kartı";
    public const string KartOdemesi = "Kart ödemesi";
    public const string GiderKalemi = "Gider kalemi";
    public const string Cek = "Çek";
    public const string KasaSayimi = "Kasa sayımı";
    public const string TekrarlayanGider = "Tekrarlayan gider";
}
