using System.Text.RegularExpressions;
using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>
/// Gece yedek doğrulaması: en yeni günlük yedek (<c>kasa-yyyy-MM-dd.db</c>) SALT OKUNUR açılır,
/// <c>PRAGMA quick_check</c> çalıştırılır ve tabloların (geçmiş ve günlükler hariç; bkz. <see cref="Tablolar"/>)
/// kayıt sayıları canlı DB ile karşılaştırılır.
/// Yedekten sonra yapılan değişiklikler sayıyı meşru olarak oynatır: fark, yedek zamanından
/// (10 dk pay ile) bu yana geçmişe yazılan değişiklik sayısını aşarsa doğrulama başarısızdır.
/// Canlı DB'ye yalnız sonuç satırı yazılır; yedek dosyasına hiç yazılmaz.
/// </summary>
public static partial class YedekDogrulayici
{
    [GeneratedRegex(@"^kasa-\d{4}-\d{2}-\d{2}\.db$")]
    private static partial Regex GunlukYedekAdi();

    /// <summary>
    /// Her sürümün yedeğinde bulunan tablolar (para ve tanım tabloları): yedekte yoksa yedek eksiktir.
    /// Sonradan eklenen tablolar (kart mutabakatı, kullanıcılar, sorular, ay kilidi, ekler, POS …) eski sürümden
    /// alınmış yedekte bulunmaz; onlar yedekte yoksa sayılmaz (sürüm yükseltildiği gün, yükseltmeden önce alınmış
    /// yedek). Canlıyla karşılaştırılamaz: açılışta geçmişe yazılmadan eklenen satırlar (güvenlik ayarı, yerleşik
    /// editör) yükseltme günü yanlış alarm verirdi.
    /// </summary>
    public static readonly IReadOnlyList<string> TemelTablolar =
    [
        "Kanallar", "Cariler", "GiderKalemleri", "Islemler", "Gelenler", "KrediKartlari", "KartOdemeler",
        "Cekler", "KasaSayimlari", "TekrarlayanGiderler", "TekrarlayanGirisler",
    ];

    /// <summary>
    /// Sayısı karşılaştırılmayan tablolar: geçmişin kendisi ve günlükler. Yedekten sonra geçmişe yazılmadan
    /// büyür ya da saklama süresiyle silinir; sayıları yedekle tutmaz.
    /// </summary>
    public static readonly IReadOnlySet<string> HaricTablolar = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Degisiklikler", "IptalEdilenTokenlar", "GirisKayitlari", "OturumKayitlari", "YedekDogrulamalari",
    };

    /// <summary>
    /// Sayısı karşılaştırılan tablolar: veritabanı modelindeki bütün tablolar, <see cref="HaricTablolar"/> hariç.
    /// Modelden okunur: yeni bir tablo (ör. Paket D'nin kart mutabakatı, Paket E'nin kullanıcıları ve soruları)
    /// listeye kendiliğinden girer.
    /// </summary>
    public static IReadOnlyList<string> Tablolar(KasaDbContext db)
        => db.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .OfType<string>()
            .Where(t => !HaricTablolar.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();

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
            var pay = Pay(db, sinir);
            var canliBag = db.Database.GetDbConnection();
            if (canliBag.State != System.Data.ConnectionState.Open) canliBag.Open();

            var farklar = new List<string>();
            var eskiSurum = new List<string>();
            var sayilan = 0;
            var toplam = 0L;
            foreach (var tablo in Tablolar(db))
            {
                if (!mevcut.Contains(tablo))
                {
                    if (TemelTablolar.Contains(tablo, StringComparer.OrdinalIgnoreCase)) farklar.Add($"{tablo} tablosu yedekte yok");
                    else eskiSurum.Add(tablo);   // yedek, tablo eklenmeden önceki sürümden
                    continue;
                }
                sayilan++;
                var y = Convert.ToInt64(Tek(yedek, $"SELECT COUNT(*) FROM \"{tablo}\""));
                var c = Convert.ToInt64(Tek(canliBag, $"SELECT COUNT(*) FROM \"{tablo}\""));
                toplam += y;
                if (Math.Abs(c - y) > pay) farklar.Add($"{tablo}: yedekte {y}, canlıda {c}");
            }
            if (farklar.Count > 0)
                return Bitir(sonuc, false, "Kayıt sayıları tutmuyor: " + string.Join("; ", farklar) + $" (yedekten beri {pay} değişiklik).");
            var not = eskiSurum.Count == 0 ? ""
                : $" Yedek önceki sürümden; şu tablolar o sürümde yoktu: {string.Join(", ", eskiSurum)}.";
            return Bitir(sonuc, true, $"Yedek açıldı, {sayilan} tablodaki {toplam} kayıt canlı veriyle uyumlu.{not}");
        }
        catch (Exception ex)
        {
            return Bitir(sonuc, false, "Yedek açılamadı: " + Metin.Kisalt(ex.Message, 200));
        }
    }

    /// <summary>
    /// Yedekten bu yana meşru kayıt sayısı oynaması: geçmiş satırı sayısı; toplu satırlar (eski hali dizi:
    /// ör. gece ek temizliği, takip birleştirmesi) dizideki kayıt sayısı kadar sayılır.
    /// </summary>
    private static long Pay(KasaDbContext db, DateTime sinir)
    {
        var satir = db.Degisiklikler.AsNoTracking().Count(d => d.ZamanUtc >= sinir);
        var toplu = db.Degisiklikler.AsNoTracking()
            .Where(d => d.ZamanUtc >= sinir && d.EskiJson != null && d.EskiJson.StartsWith("["))
            .Select(d => d.EskiJson!).ToList();
        long ek = 0;
        foreach (var json in toplu)
        {
            try
            {
                using var belge = System.Text.Json.JsonDocument.Parse(json);
                if (belge.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    ek += Math.Max(0, belge.RootElement.GetArrayLength() - 1);
            }
            catch (System.Text.Json.JsonException) { }
        }
        return satir + ek;
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
