using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kasa.Api.Data;

/// <summary>Paket B'nin geçmişte görünen tür adları.</summary>
public static partial class GecmisTurleri
{
    public const string AyKilidi = "Ay kilidi";
    public const string AyYayini = "Ay yayını";
    public const string KanalHedefi = "Kanal hedefi";
    public const string GiderButcesi = "Gider bütçesi";
    public const string Kur = "Kur";
}

/// <summary>
/// Kilitli (kapatılmış) ay. Satır varken o aya ait kayıt (<see cref="AyKilidiAttribute"/> taşıyan
/// tarih alanı o aya düşen işlem, gelen, kart ödemesi, çek, kasa sayımı) eklenemez, değiştirilemez,
/// silinemez; denetim <see cref="AyKilidiDenetcisi"/>'nde merkezîdir. Kilidi açmak satırı silmektir.
/// </summary>
[Gecmis(GecmisTurleri.AyKilidi, nameof(Etiket))]
[Index(nameof(Ay), IsUnique = true)]
public class AyKilidiEntity
{
    public int Id { get; set; }
    /// <summary>Ayın ilk günü.</summary>
    public DateOnly Ay { get; set; }
    /// <summary>Geçmişte okunur ad: "Eylül 2026".</summary>
    public string Etiket { get; set; } = "";
    public DateTime KilitZamaniUtc { get; set; }
}

/// <summary>
/// "Ayı yayınla": ayın rakamlarının o anki anlık görüntüsü (<see cref="AnlikJson"/>) ve o ana kadarki
/// son geçmiş satırı. Aylık rapor bugünkü rakamları bununla karşılaştırıp yayından sonra değişeni
/// ve o aya dokunan geçmiş satırlarını gösterir. Yeniden yayınlamak görüntüyü yeniler.
/// </summary>
[Gecmis(GecmisTurleri.AyYayini, nameof(Etiket))]
[Index(nameof(Ay), IsUnique = true)]
public class AyYayinEntity
{
    public int Id { get; set; }
    public DateOnly Ay { get; set; }
    public string Etiket { get; set; } = "";
    public DateTime YayinZamaniUtc { get; set; }
    /// <summary>Yayın anındaki en büyük geçmiş (Degisiklik) Id'si; sonrakiler "yayından sonra"dır.</summary>
    public int SonDegisiklikId { get; set; }
    [Gizli("Anlık görüntü yenilendi")]
    public string AnlikJson { get; set; } = "";
}

/// <summary>Kanalın aylık gelir hedefi. Kanal Id'ye bağlıdır: kanal adı değişse de hedef kalır.</summary>
[Gecmis(GecmisTurleri.KanalHedefi, nameof(Ay), nameof(KanalId), nameof(GelirHedefi))]
[Index(nameof(Ay), nameof(KanalId), IsUnique = true)]
public class KanalHedefEntity
{
    public int Id { get; set; }
    /// <summary>Ayın ilk günü.</summary>
    public DateOnly Ay { get; set; }
    [ForeignKey(nameof(KanalKaydi))]
    public int KanalId { get; set; }
    public decimal GelirHedefi { get; set; }
    /// <summary>Yalnız ilişki için (kanal silinince hedefleri de silinir).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public KanalEntity? KanalKaydi { get; set; }
}

/// <summary>Sabit gider kaleminin aylık bütçesi. Kalem Id'ye bağlıdır: ad değişse de bütçe kalır.</summary>
[Gecmis(GecmisTurleri.GiderButcesi, nameof(Ay), nameof(GiderKalemiId), nameof(Tutar))]
[Index(nameof(Ay), nameof(GiderKalemiId), IsUnique = true)]
public class GiderButceEntity
{
    public int Id { get; set; }
    public DateOnly Ay { get; set; }
    [ForeignKey(nameof(KalemKaydi))]
    public int GiderKalemiId { get; set; }
    public decimal Tutar { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public GiderKalemiEntity? KalemKaydi { get; set; }
}

/// <summary>
/// Aylık kur/endeks satırı (grafiklerde reel TL, USD, EUR, gram altın için). Değerleri editör girer;
/// USD/EUR TCMB'den doldurulabilir. Boş alan "kur yok" demektir: hiçbir değer uydurulmaz.
/// </summary>
[Gecmis(GecmisTurleri.Kur, nameof(Ay))]
[Index(nameof(Ay), IsUnique = true)]
public class KurEntity
{
    public int Id { get; set; }
    public DateOnly Ay { get; set; }
    /// <summary>TÜFE endeksi (ör. 2003=100 bazlı genel endeks).</summary>
    public decimal? TufeEndeksi { get; set; }
    /// <summary>Ayın ortalama USD/TRY döviz satış kuru.</summary>
    public decimal? UsdTry { get; set; }
    public decimal? EurTry { get; set; }
    /// <summary>Gram altının TL fiyatı.</summary>
    public decimal? AltinGramTry { get; set; }
}

public partial class KasaDbContext
{
    public DbSet<AyKilidiEntity> AyKilitleri => Set<AyKilidiEntity>();
    public DbSet<AyYayinEntity> AyYayinlari => Set<AyYayinEntity>();
    public DbSet<KanalHedefEntity> KanalHedefleri => Set<KanalHedefEntity>();
    public DbSet<GiderButceEntity> GiderButceleri => Set<GiderButceEntity>();
    public DbSet<KurEntity> Kurlar => Set<KurEntity>();

    /// <summary>Context'in saati (DI'daki TimeProvider); ay kilidi denetimi raporu bununla hesaplar.</summary>
    internal TimeProvider SaatSaglayici => _saat;

    /// <summary>Ay kilidi denetimi her SaveChanges'te merkezî olarak çalışır (bkz. <see cref="AyKilidiDenetcisi"/>).</summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.AddInterceptors(AyKilidiDenetcisi.Ornek);
}
