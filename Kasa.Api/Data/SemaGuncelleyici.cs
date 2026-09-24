using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kasa.Api.Data;

/// <summary>
/// Var olan bir SQLite dosyasını güncel EF modeline getirir. Canlı DB migration geçmişi
/// olmadan EnsureCreated ile kurulduğu için EF migration yerine bu yol seçildi. Her
/// açılışta EnsureCreated'dan sonra çalışır ve idempotenttir.
///
/// Önce PLAN çıkarır (DB'ye dokunmadan); desteklenmeyen bir fark görürse (sütun yeniden
/// adlandırma şüphesi, tip / NULL değişikliği, varsayılanı olmayan NOT NULL sütun
/// kaldırılması, zorunlu FK'de yetim kayıt) açılışı açık bir hatayla durdurur. Bekleyen
/// değişiklik varsa önce göç öncesi yedek alır, sonra hepsini tek transaction'da uygular:
/// <list type="bullet">
/// <item>eksik tablolar (EF'nin tablo başına ürettiği CREATE TABLE/INDEX komutlarıyla),</item>
/// <item>eksik sütunlar (ALTER TABLE ADD COLUMN),</item>
/// <item>tekil index'ten önce tek seferlik veri düzeltmeleri (çift kanal/cari, gelen dönem
///       başı normalizasyonu + çift gelen temizliği, işlemlerdeki carilerin cari listesine
///       eklenmesi) — silinen her satır loglanır,</item>
/// <item>eksik/yanlış FK'si olan tablonun yeniden kurulması (SQLite ALTER ile FK eklenemez),
///       nullable FK'deki yetim değerlerin NULL'lanması,</item>
/// <item>eksik index'ler (var olan tablolarda da).</item>
/// </list>
/// </summary>
public static class SemaGuncelleyici
{
    private sealed record DbSutun(string Ad, string Tip, bool NotNull, string? Varsayilan, bool Pk);
    private sealed record DbIndex(string Ad, bool Tekil);
    private sealed record DbFk(string Tablo, string Kaynak, string Hedef, string OnDelete);

    private sealed class Plan
    {
        public List<string> EksikTablolar { get; } = new();
        public List<(string Tablo, IColumn Sutun)> EksikSutunlar { get; } = new();
        public List<(string Tablo, ITableIndex Index, bool YenidenKur)> EksikIndexler { get; } = new();
        public HashSet<string> YenidenKurulacak { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Tablo, string Sutun, string HedefTablo, string HedefSutun)> NullanacakYetimler { get; } = new();
        public List<string> Hatalar { get; } = new();
        public List<string> Uyarilar { get; } = new();

        public bool Bos => EksikTablolar.Count == 0 && EksikSutunlar.Count == 0 && EksikIndexler.Count == 0
                           && YenidenKurulacak.Count == 0 && NullanacakYetimler.Count == 0;
    }

    /// <summary>
    /// Şemayı günceller ve yapılan değişikliklerin listesini döner.
    /// </summary>
    /// <param name="yedekKlasoru">Bekleyen değişiklik varsa göç öncesi yedeğin yazılacağı klasör
    /// (<c>kasa-once-yyyyMMdd-HHmmss.db</c>). null ise yedek alınmaz (uyarı loglanır).</param>
    /// <exception cref="InvalidOperationException">Otomatik uygulanamayan bir şema farkı var.</exception>
    public static IReadOnlyList<string> Guncelle(KasaDbContext db, ILogger? log = null, string? yedekKlasoru = null)
    {
        log ??= NullLogger.Instance;
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();

        var model = db.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var plan = PlanCikar(conn, model);

        foreach (var u in plan.Uyarilar) log.LogWarning("Şema uyarısı: {Uyari}", u);
        if (plan.Hatalar.Count > 0)
            throw new InvalidOperationException(
                "Veritabanı şeması otomatik güncellenemiyor; veri kaybını önlemek için uygulama başlatılmadı. " +
                "Elle göç gerekiyor (önce yedek alın):" + Environment.NewLine + " - " +
                string.Join(Environment.NewLine + " - ", plan.Hatalar));
        if (plan.Bos) return Array.Empty<string>();

        var yapilan = new List<string>();
        var mevcutTabloVar = TablolariOku(conn, null).Count > 0;
        if (mevcutTabloVar)
        {
            if (!string.IsNullOrWhiteSpace(yedekKlasoru))
            {
                var dosya = GocOncesiYedek(conn, yedekKlasoru);
                log.LogInformation("Göç öncesi yedek alındı: {Dosya}", dosya);
                yapilan.Add($"yedek {Path.GetFileName(dosya)}");
            }
            else
            {
                log.LogWarning("Şema değişecek ama Kasa:YedekKlasoru tanımlı değil; göç öncesi yedek alınmadı.");
            }
        }

        // PRAGMA foreign_keys transaction içinde etkisizdir; tablo yeniden kurulurken kapalı olmalı.
        Calistir(conn, null, "PRAGMA foreign_keys = OFF");
        try
        {
            using var tx = conn.BeginTransaction();
            Uygula(conn, tx, db, model, plan, yapilan, log);

            var ihlal = Oku(conn, tx, "SELECT \"table\" || ' rowid=' || rowid || ' -> ' || parent FROM pragma_foreign_key_check");
            if (ihlal.Count > 0)
                throw new InvalidOperationException("Şema güncellemesi sonrası FK ihlali kaldı: " + string.Join("; ", ihlal));
            tx.Commit();
        }
        finally
        {
            Calistir(conn, null, "PRAGMA foreign_keys = ON");
        }

        foreach (var y in yapilan) log.LogInformation("Şema güncellendi: {Degisiklik}", y);
        return yapilan;
    }

    // ------------------------------------------------------------------ plan

    private static Plan PlanCikar(DbConnection conn, IRelationalModel model)
    {
        var plan = new Plan();
        var mevcut = TablolariOku(conn, null).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var tablo in model.Tables)
        {
            if (!mevcut.Contains(tablo.Name))
            {
                plan.EksikTablolar.Add(tablo.Name);
                continue;
            }

            var dbSutunlar = SutunlariOku(conn, tablo.Name).ToDictionary(s => s.Ad, StringComparer.OrdinalIgnoreCase);
            var modelSutunlar = tablo.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var eksik = tablo.Columns.Where(c => !dbSutunlar.ContainsKey(c.Name)).ToList();
            var fazla = dbSutunlar.Values.Where(s => !modelSutunlar.ContainsKey(s.Ad)).ToList();

            if (eksik.Count > 0 && fazla.Count > 0)
                plan.Hatalar.Add($"{tablo.Name}: DB'de modelde olmayan sütun(lar) [{string.Join(", ", fazla.Select(f => f.Ad))}] " +
                                 $"varken modelde yeni sütun(lar) [{string.Join(", ", eksik.Select(e => e.Name))}] var — " +
                                 "yeniden adlandırma olabilir; otomatik eklemek eski veriyi kaybettirir.");

            foreach (var f in fazla)
            {
                if (f.NotNull && f.Varsayilan is null && !f.Pk)
                    plan.Hatalar.Add($"{tablo.Name}.{f.Ad}: modelden kaldırılmış NOT NULL ve varsayılansız sütun; yeni kayıt eklenemez.");
                else
                    plan.Uyarilar.Add($"{tablo.Name}.{f.Ad}: modelde yok, olduğu gibi bırakıldı.");
            }

            foreach (var c in tablo.Columns)
            {
                if (!dbSutunlar.TryGetValue(c.Name, out var d)) continue;
                if (!string.Equals(Normal(d.Tip), Normal(c.StoreType), StringComparison.OrdinalIgnoreCase))
                    plan.Hatalar.Add($"{tablo.Name}.{c.Name}: tip değişmiş (DB: {d.Tip}, model: {c.StoreType}).");
                if (!d.Pk && d.NotNull == c.IsNullable)
                    plan.Hatalar.Add($"{tablo.Name}.{c.Name}: NULL kuralı değişmiş (DB: {(d.NotNull ? "NOT NULL" : "NULL")}, " +
                                     $"model: {(c.IsNullable ? "NULL" : "NOT NULL")}).");
            }
            if (eksik.Count > 0 && fazla.Count == 0)
                plan.EksikSutunlar.AddRange(eksik.Select(c => (tablo.Name, c)));

            // FK'ler: eksik ya da farklı (ON DELETE) ise tablo yeniden kurulur.
            var dbFkler = FkOku(conn, tablo.Name);
            foreach (var fk in tablo.ForeignKeyConstraints)
            {
                var kaynak = fk.Columns.Single().Name;
                var hedef = fk.PrincipalTable.Name;
                var hedefSutun = fk.PrincipalColumns.Single().Name;
                var var_ = dbFkler.FirstOrDefault(x => x.Kaynak.Equals(kaynak, StringComparison.OrdinalIgnoreCase)
                                                        && x.Tablo.Equals(hedef, StringComparison.OrdinalIgnoreCase));
                if (var_ is null || !var_.OnDelete.Equals(OnDeleteSql(fk.OnDeleteAction), StringComparison.OrdinalIgnoreCase))
                    plan.YenidenKurulacak.Add(tablo.Name);

                // Yetim değerler: FK tanımlı olsun olmasın modele göre kontrol et.
                if (!mevcut.Contains(hedef) || !dbSutunlar.ContainsKey(kaynak)) continue;
                var yetim = Sayi(conn, $"SELECT COUNT(*) FROM \"{tablo.Name}\" WHERE \"{kaynak}\" IS NOT NULL " +
                                       $"AND \"{kaynak}\" NOT IN (SELECT \"{hedefSutun}\" FROM \"{hedef}\")");
                if (yetim == 0) continue;
                if (modelSutunlar[kaynak].IsNullable)
                    plan.NullanacakYetimler.Add((tablo.Name, kaynak, hedef, hedefSutun));
                else
                    plan.Hatalar.Add($"{tablo.Name}.{kaynak}: {yetim} kayıt olmayan bir {hedef} kaydına bağlı (zorunlu alan, NULL yapılamaz).");
            }

            var dbIndexler = IndexOku(conn, tablo.Name).ToDictionary(i => i.Ad, StringComparer.OrdinalIgnoreCase);
            foreach (var ix in tablo.Indexes)
            {
                if (!dbIndexler.TryGetValue(ix.Name, out var d))
                    plan.EksikIndexler.Add((tablo.Name, ix, false));
                else if (d.Tekil != ix.IsUnique)
                    plan.EksikIndexler.Add((tablo.Name, ix, true));
            }
        }
        return plan;
    }

    // ------------------------------------------------------------------ uygula

    private static void Uygula(DbConnection conn, DbTransaction tx, KasaDbContext db, IRelationalModel model,
        Plan plan, List<string> yapilan, ILogger log)
    {
        foreach (var tablo in plan.EksikTablolar)
        {
            foreach (var sql in TabloKomutlari(db, model, tablo, yeniAd: null, indexlerle: true))
                Calistir(conn, tx, sql);
            yapilan.Add($"tablo+ {tablo}");
        }

        foreach (var (tablo, c) in plan.EksikSutunlar)
        {
            var tanim = c.IsNullable
                ? $"\"{c.Name}\" {c.StoreType} NULL"
                : $"\"{c.Name}\" {c.StoreType} NOT NULL DEFAULT {Varsayilan(c)}";
            Calistir(conn, tx, $"ALTER TABLE \"{tablo}\" ADD COLUMN {tanim}");
            yapilan.Add($"sütun+ {tablo}.{c.Name}");
        }

        // Tekil index kurulmadan önce tek seferlik veri düzeltmeleri.
        var kurulacak = plan.EksikIndexler.Where(x => x.Index.IsUnique).Select(x => x.Index.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (kurulacak.Contains("IX_Kanallar_Ad"))
            CiftleriSil(conn, tx, "Kanallar", "Ad", enKucukIdKalsin: true, yapilan, log);
        if (kurulacak.Contains("IX_Cariler_Ad"))
        {
            CiftleriSil(conn, tx, "Cariler", "Ad", enKucukIdKalsin: true, yapilan, log);
            IslemCarileriniEkle(conn, tx, yapilan, log);
        }
        if (kurulacak.Contains("IX_Gelenler_DonemStart_Kanal"))
        {
            GelenleriNormallestir(conn, tx, yapilan, log);
            CiftleriSil(conn, tx, "Gelenler", "\"DonemStart\", \"Kanal\"", enKucukIdKalsin: false, yapilan, log);
        }

        foreach (var (tablo, sutun, hedef, hedefSutun) in plan.NullanacakYetimler)
        {
            var satirlar = OkuSatir(conn, tx, $"SELECT \"Id\", \"{sutun}\" FROM \"{tablo}\" WHERE \"{sutun}\" IS NOT NULL " +
                                              $"AND \"{sutun}\" NOT IN (SELECT \"{hedefSutun}\" FROM \"{hedef}\")");
            foreach (var s in satirlar)
                log.LogWarning("Yetim bağ kaldırıldı: {Tablo} Id={Id} {Sutun}={Deger} ({Hedef} kaydı yok) → NULL",
                    tablo, s[0], sutun, s[1], hedef);
            Calistir(conn, tx, $"UPDATE \"{tablo}\" SET \"{sutun}\" = NULL WHERE \"{sutun}\" IS NOT NULL " +
                               $"AND \"{sutun}\" NOT IN (SELECT \"{hedefSutun}\" FROM \"{hedef}\")");
            yapilan.Add($"yetim→NULL {tablo}.{sutun} ({satirlar.Count} satır)");
        }

        foreach (var tablo in plan.YenidenKurulacak)
        {
            YenidenKur(conn, tx, db, model, tablo);
            yapilan.Add($"yeniden kuruldu (FK) {tablo}");
        }

        foreach (var (tablo, ix, yenidenKur) in plan.EksikIndexler)
        {
            if (plan.YenidenKurulacak.Contains(tablo)) continue; // yeniden kurulumda index'ler de kuruldu
            if (yenidenKur) Calistir(conn, tx, $"DROP INDEX IF EXISTS \"{ix.Name}\"");
            var op = new CreateIndexOperation
            {
                Name = ix.Name, Table = tablo, IsUnique = ix.IsUnique,
                Columns = ix.Columns.Select(c => c.Name).ToArray(),
            };
            foreach (var sql in Uret(db, op)) Calistir(conn, tx, sql);
            yapilan.Add($"index+ {ix.Name}");
        }
    }

    /// <summary>
    /// SQLite ALTER ile FK eklenemediğinden tabloyu model şemasıyla yeniden kurar:
    /// yeni tablo → veriyi kopyala → eskiyi sil → adlandır → index'leri kur. Tüm sütunlar
    /// bu noktada DB'de var (eksikler önceden eklendi), yani veri aynen taşınır.
    /// </summary>
    private static void YenidenKur(DbConnection conn, DbTransaction tx, KasaDbContext db, IRelationalModel model, string tablo)
    {
        var gecici = $"{tablo}__yeni";
        Calistir(conn, tx, $"DROP TABLE IF EXISTS \"{gecici}\"");
        foreach (var sql in TabloKomutlari(db, model, tablo, yeniAd: gecici, indexlerle: false))
            Calistir(conn, tx, sql);
        var sutunlar = string.Join(", ", model.Tables.Single(t => t.Name == tablo).Columns.Select(c => $"\"{c.Name}\""));
        Calistir(conn, tx, $"INSERT INTO \"{gecici}\" ({sutunlar}) SELECT {sutunlar} FROM \"{tablo}\"");
        Calistir(conn, tx, $"DROP TABLE \"{tablo}\"");
        Calistir(conn, tx, $"ALTER TABLE \"{gecici}\" RENAME TO \"{tablo}\"");
        foreach (var ix in model.Tables.Single(t => t.Name == tablo).Indexes)
        {
            var op = new CreateIndexOperation
            {
                Name = ix.Name, Table = tablo, IsUnique = ix.IsUnique,
                Columns = ix.Columns.Select(c => c.Name).ToArray(),
            };
            foreach (var sql in Uret(db, op)) Calistir(conn, tx, sql);
        }
    }

    /// <summary>
    /// Aynı anahtara sahip satırlardan birini bırakıp gerisini siler ve her silineni loglar.
    /// Kanal/cari: en küçük Id (ilk kayıt) kalır. Gelen: en büyük Id (son yazılan) kalır.
    /// </summary>
    private static void CiftleriSil(DbConnection conn, DbTransaction tx, string tablo, string anahtar,
        bool enKucukIdKalsin, List<string> yapilan, ILogger log)
    {
        var secim = enKucukIdKalsin ? "MIN" : "MAX";
        var kosul = $"\"Id\" NOT IN (SELECT {secim}(\"Id\") FROM \"{tablo}\" GROUP BY {Tirnak(anahtar)})";
        var silinecek = OkuSatir(conn, tx, $"SELECT * FROM \"{tablo}\" WHERE {kosul}", basliklarla: true);
        if (silinecek.Count <= 1) return; // ilk satır başlık
        var baslik = silinecek[0];
        foreach (var s in silinecek.Skip(1))
            log.LogWarning("Çift kayıt silindi ({Tablo}): {Satir}", tablo,
                string.Join(", ", baslik.Zip(s, (b, v) => $"{b}={v}")));
        Calistir(conn, tx, $"DELETE FROM \"{tablo}\" WHERE {kosul}");
        yapilan.Add($"çift- {tablo} ({silinecek.Count - 1} satır)");
    }

    private static string Tirnak(string anahtar) => anahtar.Contains('"') ? anahtar : $"\"{anahtar}\"";

    /// <summary>
    /// Gelen satırlarının DonemStart'ını içinde bulunduğu dönemin başına çeker (takip
    /// başlangıcından önceki satırlar hiçbir döneme girmediği için olduğu gibi kalır).
    /// </summary>
    private static void GelenleriNormallestir(DbConnection conn, DbTransaction tx, List<string> yapilan, ILogger log)
    {
        var takipMetin = OkuSatir(conn, tx, "SELECT \"TakipBaslangic\" FROM \"Ayarlar\" ORDER BY \"Id\" LIMIT 1").FirstOrDefault()?[0];
        if (!DateOnly.TryParseExact(takipMetin, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var takip))
            return;
        int n = 0;
        foreach (var s in OkuSatir(conn, tx, "SELECT \"Id\", \"DonemStart\", \"Kanal\" FROM \"Gelenler\""))
        {
            if (!DateOnly.TryParseExact(s[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) continue;
            if (Takvim.DonemBaslangici(d, takip) is not { } bas || bas == d) continue;
            log.LogWarning("Gelen dönem başına çekildi: Id={Id} Kanal={Kanal} {Eski} → {Yeni}", s[0], s[2], s[1], bas.ToString("yyyy-MM-dd"));
            Calistir(conn, tx, $"UPDATE \"Gelenler\" SET \"DonemStart\" = '{bas:yyyy-MM-dd}' WHERE \"Id\" = {long.Parse(s[0]!)}");
            n++;
        }
        if (n > 0) yapilan.Add($"gelen dönem başı ({n} satır)");
    }

    /// <summary>İşlemlerde geçip cari listesinde olmayan adları cari olarak ekler (işlem cari'ye bağlanabilsin).</summary>
    private static void IslemCarileriniEkle(DbConnection conn, DbTransaction tx, List<string> yapilan, ILogger log)
    {
        var mevcut = TablolariOku(conn, tx);
        if (!mevcut.Contains("Islemler", StringComparer.OrdinalIgnoreCase)) return;
        var eksik = OkuSatir(conn, tx, "SELECT DISTINCT \"Cari\" FROM \"Islemler\" WHERE \"Cari\" <> '' " +
                                       "AND \"Cari\" NOT IN (SELECT \"Ad\" FROM \"Cariler\")");
        foreach (var s in eksik)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO \"Cariler\" (\"Ad\", \"Aktif\") VALUES ($ad, 1)";
            var p = cmd.CreateParameter(); p.ParameterName = "$ad"; p.Value = s[0]; cmd.Parameters.Add(p);
            cmd.ExecuteNonQuery();
            log.LogInformation("İşlemlerde geçen cari listeye eklendi: {Cari}", s[0]);
        }
        if (eksik.Count > 0) yapilan.Add($"cari+ işlemlerden ({eksik.Count})");
    }

    // ------------------------------------------------------------------ SQL üretimi

    /// <summary>
    /// Tek bir tablonun CREATE TABLE (+ isteğe bağlı CREATE INDEX) komutlarını EF'nin
    /// migration SQL üreticisiyle ayrı ayrı üretir; betiği ';' ile bölmek gerekmez.
    /// </summary>
    private static IEnumerable<string> TabloKomutlari(KasaDbContext db, IRelationalModel model, string tablo, string? yeniAd, bool indexlerle)
    {
        var differ = db.GetService<IMigrationsModelDiffer>();
        var ops = differ.GetDifferences(null, model);
        var create = ops.OfType<CreateTableOperation>().Single(o => o.Name == tablo);
        if (yeniAd is not null) create.Name = yeniAd;
        foreach (var sql in Uret(db, create)) yield return sql;
        if (!indexlerle) yield break;
        foreach (var ix in ops.OfType<CreateIndexOperation>().Where(o => o.Table == tablo))
            foreach (var sql in Uret(db, ix)) yield return sql;
    }

    private static IEnumerable<string> Uret(KasaDbContext db, MigrationOperation op)
        => db.GetService<IMigrationsSqlGenerator>()
            .Generate(new[] { op }, db.Model)
            .Select(k => k.CommandText.Trim())
            .Where(s => s.Length > 0);

    private static string OnDeleteSql(ReferentialAction a) => a switch
    {
        ReferentialAction.Cascade => "CASCADE",
        ReferentialAction.SetNull => "SET NULL",
        ReferentialAction.SetDefault => "SET DEFAULT",
        ReferentialAction.Restrict => "RESTRICT",
        _ => "NO ACTION",
    };

    private static string Normal(string tip) => tip.Trim().ToUpperInvariant();

    private static string Varsayilan(IColumn c)
    {
        var p = c.PropertyMappings.First().Property;
        var t = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
        if (t == typeof(string)) return "''";
        if (t == typeof(decimal)) return "'0.0'";
        if (t == typeof(DateOnly)) return "'0001-01-01'";
        if (t == typeof(DateTime)) return "'0001-01-01 00:00:00'";
        return "0"; // int, bool, enum
    }

    // ------------------------------------------------------------------ yedek

    private static string GocOncesiYedek(DbConnection conn, string klasor)
    {
        Directory.CreateDirectory(klasor);
        var dosya = Path.Combine(klasor, $"kasa-once-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        var gecici = dosya + ".tmp";
        if (File.Exists(gecici)) File.Delete(gecici);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "VACUUM INTO $d";
            var p = cmd.CreateParameter(); p.ParameterName = "$d"; p.Value = gecici; cmd.Parameters.Add(p);
            cmd.ExecuteNonQuery();
        }
        File.Move(gecici, dosya, overwrite: true);
        return dosya;
    }

    // ------------------------------------------------------------------ okuma yardımcıları

    private static List<string> TablolariOku(DbConnection conn, DbTransaction? tx)
        => Oku(conn, tx, "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'");

    private static List<DbSutun> SutunlariOku(DbConnection conn, string tablo)
        => OkuSatir(conn, null, $"SELECT name, type, \"notnull\", dflt_value, pk FROM pragma_table_info('{tablo}')")
            .Select(r => new DbSutun(r[0]!, r[1] ?? "", r[2] == "1", r[3], r[4] != "0")).ToList();

    private static List<DbIndex> IndexOku(DbConnection conn, string tablo)
        => OkuSatir(conn, null, $"SELECT name, \"unique\" FROM pragma_index_list('{tablo}')")
            .Select(r => new DbIndex(r[0]!, r[1] == "1")).ToList();

    private static List<DbFk> FkOku(DbConnection conn, string tablo)
        => OkuSatir(conn, null, $"SELECT \"table\", \"from\", \"to\", on_delete FROM pragma_foreign_key_list('{tablo}')")
            .Select(r => new DbFk(r[0]!, r[1]!, r[2] ?? "", r[3] ?? "NO ACTION")).ToList();

    private static long Sayi(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static List<string> Oku(DbConnection conn, DbTransaction? tx, string sql)
        => OkuSatir(conn, tx, sql).Select(r => r[0]!).ToList();

    private static List<string?[]> OkuSatir(DbConnection conn, DbTransaction? tx, string sql, bool basliklarla = false)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var l = new List<string?[]>();
        if (basliklarla)
            l.Add(Enumerable.Range(0, r.FieldCount).Select(i => (string?)r.GetName(i)).ToArray());
        while (r.Read())
        {
            var satir = new string?[r.FieldCount];
            for (int i = 0; i < r.FieldCount; i++)
                satir[i] = r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture);
            l.Add(satir);
        }
        return l;
    }

    private static void Calistir(DbConnection conn, DbTransaction? tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
