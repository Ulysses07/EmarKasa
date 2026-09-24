using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

/// <summary>
/// Açılışta DB'yi hazırlar: oluştur, WAL, şema güncelle, seed, eski iptal kayıtlarını ve
/// saklama süresini aşan geçmiş satırlarını temizle. Buradaki yazmalar HTTP isteği dışında
/// olduğundan değişiklik geçmişine yazılmaz (bkz. <see cref="KasaDbContext.DegistirenRol"/>).
/// </summary>
public static class VeritabaniBaslatici
{
    public static void Baslat(KasaDbContext db, ILogger log, string? yedekKlasoru)
    {
        db.Database.EnsureCreated();

        // WAL her açılışta zorlanır: VACUUM INTO yedeğinden geri yüklenen dosya DELETE
        // modunda gelir ve o modda panel okurken yazmalar "database is locked" ile düşer.
        // (In-memory DB'de sonuç "memory" olur; önemsizdir.)
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();
        string? mod;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL";
            mod = cmd.ExecuteScalar() as string;
        }
        if (!string.Equals(mod, "wal", StringComparison.OrdinalIgnoreCase) && !string.Equals(mod, "memory", StringComparison.OrdinalIgnoreCase))
            log.LogWarning("SQLite WAL moduna alınamadı (journal_mode={Mod}).", mod);

        SemaGuncelleyici.Guncelle(db, log, yedekKlasoru);

        if (!db.Kanallar.Any())
        {
            db.Kanallar.AddRange(
                new KanalEntity { Ad = "MEZAT", Sira = 0 },
                new KanalEntity { Ad = "PERAKENDE", Sira = 1 },
                new KanalEntity { Ad = "TOPTAN", Sira = 2 });
        }
        if (!db.Ayarlar.Any())
        {
            db.Ayarlar.Add(new AyarEntity
            {
                TakipBaslangic = Saat.Bugun(),
                KasaAcilisDevri = 0m,
                IzleyiciSifreHash = null,
            });
        }
        db.SaveChanges();

        var simdi = DateTime.UtcNow;
        db.IptalEdilenTokenlar.Where(t => t.BitisUtc <= simdi).ExecuteDelete();

        // Değişiklik geçmişi en fazla SaklamaYili saklanır.
        var gecmisSiniri = simdi.AddYears(-GecmisKurallari.SaklamaYili);
        var silinen = db.Degisiklikler.Where(d => d.ZamanUtc < gecmisSiniri).ExecuteDelete();
        if (silinen > 0)
            log.LogInformation("{Sayi} eski geçmiş satırı silindi ({Sinir:yyyy-MM-dd} öncesi).", silinen, gecmisSiniri);
    }
}
