using Kasa.Core;

namespace Kasa.Api;

// "Hızlı ve hatasız giriş" paketinin (C) uç gövdeleri ve yanıtları.

/// <summary>
/// Kayıttan önce uyarı denetimi için taslak işlem. <see cref="HaricId"/> düzenlenen işlemin Id'sidir
/// (kendisiyle çift sayılmasın). Uyarılar yalnız bilgi verir; kaydı engellemez.
/// </summary>
public record IslemUyariIstegi(DateOnly Tarih, string? Cari, decimal TutarTl, string? Kanal, GiderTipi Tip,
    int? KrediKartiId = null, int? HaricId = null);

/// <summary>Kayıttan önceki uyarı. <see cref="Kod"/>: <see cref="IslemUyariKodlari"/>.</summary>
public record IslemUyariDto(string Kod, string Mesaj);

public static class IslemUyariKodlari
{
    /// <summary>Aynı cariye aynı tutar 3 gün içinde girilmiş (çift kayıt olabilir).</summary>
    public const string AyniTutar = "AyniTutar";
    /// <summary>Tutar bu carinin olağan tutarının 10 katı ya da fazlası (fazladan sıfır olabilir).</summary>
    public const string OlaganDisiTutar = "OlaganDisiTutar";
    /// <summary>Tarih 45 günden eski: ortakların gördüğü raporlar değişir.</summary>
    public const string EskiTarih = "EskiTarih";
    /// <summary>Bu kişiye aynı tutarda verilmiş (ödenmiş ya da ödenecek) bir çek var: para iki kez düşebilir.</summary>
    public const string CekCiftDusme = "CekCiftDusme";
}

/// <summary>Toplu işlem yükleme (Excel'den yapıştırma / CSV). Ya hepsi kaydedilir ya hiçbiri.</summary>
/// <param name="YeniCarileriEkle">true ise kayıtlı olmayan cariler aynı transaction'da eklenir.</param>
public record TopluIslemIstegi(IReadOnlyList<TopluIslemSatiri>? Satirlar, bool YeniCarileriEkle = false);

public record TopluIslemSatiri(DateOnly Tarih, string? Cari, decimal TutarTl, string? Kanal, GiderTipi Tip,
    string? Not = null, int? KrediKartiId = null);

/// <summary>Toplu yüklemede hatalı satır (<see cref="Sira"/> 1'den başlar, gönderilen sırayla).</summary>
public record TopluIslemHatasiDto(int Sira, string Hata);

public record TopluIslemSonucuDto(int Eklenen, decimal Toplam, IReadOnlyList<string> YeniCariler,
    IReadOnlyList<Kasa.Api.Data.IslemEntity> Islemler);

/// <summary>Carinin son işlemi: yeni işlemde kanal ve tip önerisi için.</summary>
public record IslemOneriDto(string Cari, DateOnly Tarih, decimal TutarTl, string Kanal, GiderTipi Tip, int? KrediKartiId);

/// <summary>Gelen tablosunun bir hücresi (kanal satırı): kayıt yoksa <see cref="TutarTl"/> null.</summary>
public record GelenHucreDto(string Kanal, bool Aktif, decimal? TutarTl, int? GelenId);

/// <summary>
/// Bir dönemin (haftanın) bütün kanallarının geleni: aktif kanallar + o dönemde geleni olan pasif
/// kanallar. <see cref="Onceki"/>/<see cref="Sonraki"/> gezinme için komşu dönemlerin başıdır (yoksa null).
/// </summary>
public record GelenTablosuDto(DateOnly DonemStart, DateOnly DonemEnd, DateOnly? Onceki, DateOnly? Sonraki,
    IReadOnlyList<GelenHucreDto> Satirlar);

/// <summary>Bitmiş bir dönemde aktif kanalın geleni hiç girilmemiş.</summary>
public record EksikGelenSatiriDto(DateOnly DonemStart, DateOnly DonemEnd, string Kanal);
