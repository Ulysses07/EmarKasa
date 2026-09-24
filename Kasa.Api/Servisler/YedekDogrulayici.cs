using System.Text.RegularExpressions;
using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>
/// Gece yedek doğrulaması: en yeni günlük yedek (<c>kasa-yyyy-MM-dd.db</c>) SALT OKUNUR açılır,
/// <c>PRAGMA quick_check</c> çalıştırılır ve ana tabloların kayıt sayıları canlı DB ile karşılaştırılır.
/// Yedekten sonra yapılan değişiklikler sayıyı meşru olarak oynatır: fark, yedek zamanından
/// (10 dk pay ile) bu yana geçmişe yazılan değişiklik sayısını aşarsa doğrulama başarısızdır.
/// Canlı DB'ye yalnız sonuç satırı yazılır; yedek dosyasına hiç yazılmaz.
/// </summary>
public static partial class YedekDogrulayici
{
    [GeneratedRegex(@"^kasa-\d{4}-\d{2}-\d{2}\.db$")]
    private static partial Regex GunlukYedekAdi();

    /// <summary>Sayısı karşılaştırılan tablolar (para ve tanım tabloları).</summary>
    public static readonly IReadOnlyList<string> Tablolar =
    [
        "Kanallar", "Cariler", "GiderKalemleri", "Islemler", "Gelenler", "KrediKartlari", "KartOdemeler",
        "Cekler", "KasaSayimlari", "TekrarlayanGiderler", "TekrarlayanGirisler",
    ];

    public static readonly TimeSpan SaatPayi = TimeSpan.FromMinutes(10);

    /// <summary>Klasördeki en yeni günlük yedek (adındaki tarihe göre); yoksa null.</summary>
    public static string? EnYeniGunlukYedek(string? klasor)
    {
        if (string.IsNullOrWhiteSpace(klasor) || !Directory.Exists(klasor)) return null;
        return Directory.GetFiles(klasor)
            .Where(f => GunlukYedekAdi().IsMatch(Path.GetFileName(f)))
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static YedekDogrulamaEntity Dogrula(KasaDbContext db, string dosya, DateTime simdiUtc)
    {
        var sonuc = new YedekDogrulamaEntity
        {
            ZamanUtc = simdiUtc,
            Dosya = Path.GetFileName(dosya),
            DosyaZamaniUtc = File.Exists(dosya) ? File.GetLastWriteTimeUtc(dosya) : simdiUtc,
        };
        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = dosya, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            using var yedek = new SqliteConnection(cs);
            yedek.Open();

            var kontrol = Tek(yedek, "PRAGMA quick_check")?.ToString();
            if (!string.Equals(kontrol, "ok", StringComparison.OrdinalIgnoreCase))
                return Bitir(sonuc, false, $"Yedek dosyası bozuk (quick_check: {Metin.Kisalt(kontrol, 120)}).");

            var mevcut = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = yedek.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
                using var r = cmd.ExecuteReader();
                while (r.Read()) mevcut.Add(r.GetString(0));
            }

            var sinir = sonuc.DosyaZamaniUtc - SaatPayi;
            var pay = db.Degisiklikler.AsNoTracking().Count(d => d.ZamanUtc >= sinir);
            var canliBag = db.Database.GetDbConnection();
            if (canliBag.State != System.Data.ConnectionState.Open) canliBag.Open();

            var farklar = new List<string>();
            var toplam = 0L;
            foreach (var tablo in Tablolar)
            {
                if (!mevcut.Contains(tablo))
                {
                    farklar.Add($"{tablo} tablosu yedekte yok");
                    continue;
                }
                var y = Convert.ToInt64(Tek(yedek, $"SELECT COUNT(*) FROM \"{tablo}\""));
                var c = Convert.ToInt64(Tek(canliBag, $"SELECT COUNT(*) FROM \"{tablo}\""));
                toplam += y;
                if (Math.Abs(c - y) > pay) farklar.Add($"{tablo}: yedekte {y}, canlıda {c}");
            }
            if (farklar.Count > 0)
                return Bitir(sonuc, false, "Kayıt sayıları tutmuyor: " + string.Join("; ", farklar) + $" (yedekten beri {pay} değişiklik).");
            return Bitir(sonuc, true, $"Yedek açıldı, {Tablolar.Count} tablodaki {toplam} kayıt canlı veriyle uyumlu.");
        }
        catch (Exception ex)
        {
            return Bitir(sonuc, false, "Yedek açılamadı: " + Metin.Kisalt(ex.Message, 200));
        }
    }

    private static YedekDogrulamaEntity Bitir(YedekDogrulamaEntity s, bool basarili, string mesaj)
    {
        s.Basarili = basarili;
        s.Mesaj = mesaj;
        return s;
    }

    private static object? Tek(System.Data.Common.DbConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
