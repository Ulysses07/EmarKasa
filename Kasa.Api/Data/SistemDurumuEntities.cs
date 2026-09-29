using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

/// <summary>
/// Sistem durumu: tek satır (Id 1; migration 20261006000100_GeriYuklemeGuvenligi tohumlar). Veritabanındaki kimlik durumu
/// (editör şifresi ve kurtarma kodu, izleyici şifresi, alıcı hesapları, oturum sürümleri) geri yüklemede yedek anına sarılır;
/// bu satır geri yüklemenin güvenli işlenmesini taşır (gap-geri-yukleme-durum-geri-sarma-1, bkz. <see cref="Auth.GeriYuklemeIsleyici"/>).
/// <list type="bullet">
/// <item><see cref="OturumDonemi"/>: bütün oturum damgalarına (<see cref="Auth.OturumDamgasi"/>) giren veri soyu dönemi. Boşken
/// damga bu sürümden öncekiyle birebir aynıdır: yayın hiçbir oturumu, tanıdık cihaz belirtecini ya da bildirim aboneliğini
/// düşürmez. Her geri yükleme yeni rastgele dönem açar; yedekten önce ya da sonra, hatta aynı yedeğin önceki bir geri
/// yüklemesinden sonra alınmış hiçbir belirteç yeni soyda geçmez.</item>
/// <item><see cref="YedekZamani"/>: yalnız uygulamanın yedek kopyasında dolu (kopyalamanın başladığı an; canlı dosyada NULL).
/// Geri yüklemede güvenlik günlüğünün hangi olaylarının yedekte olmadığını (yeniden uygulanacağını) belirler; işlenince silinir.</item>
/// <item><see cref="SonGeriYukleme"/>, <see cref="GeriYuklemeRaporu"/>: son geri yüklemenin anı ve operatör/editör için Türkçe
/// madde listesi (JSON dizi); /api/yedek/durum ile web Araçlar ve masaüstü Güvenlik ekranında görünür.</item>
/// </list>
/// Satır denetim olayı üretmez (geri yükleme kendi olayını yazar).
/// </summary>
public class SistemDurumuEntity
{
    public int Id { get; set; } = 1;
    public string OturumDonemi { get; set; } = "";
    public DateTimeOffset? YedekZamani { get; set; }
    public DateTimeOffset? SonGeriYukleme { get; set; }
    public string? GeriYuklemeRaporu { get; set; }
}

public partial class KasaDbContext
{
    public DbSet<SistemDurumuEntity> SistemDurumu => Set<SistemDurumuEntity>();

    partial void ConfigureSistemDurumu(ModelBuilder b)
    {
        b.Entity<SistemDurumuEntity>().ToTable("SistemDurumu");
        b.Entity<SistemDurumuEntity>().Property(s => s.Id).ValueGeneratedNever();
    }
}
