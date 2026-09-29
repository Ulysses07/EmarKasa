using System.Data;
using Kasa.Api.Migrations;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Data;

/// <summary>Boş veritabanında migration çalıştırır. Migration geçmişi olmayan eski
/// EnsureCreated şemasını tek transaction içinde, kayıtları ve kimlikleri koruyarak
/// ilk migration'a eşler. Belirsiz/veri kaybettirecek bir dönüşümde geri alır.
/// Dosya tabanlı, boş olmayan veritabanında bekleyen iş (eski şema köprüsü, migration, veri adımı) varsa önce
/// göç öncesi yedek alınır (<see cref="YedekServisi.GocOncesiYedekAl"/>); yedek alınamazsa hiçbir iş çalışmaz ve
/// açılış açıklayıcı hatayla durur.</summary>
public static class KasaDatabaseInitializer
{
    /// <param name="yedek">Göç öncesi yedeği alan servis (Program.cs verir). Yalnız bellek içi ya da boş veritabanında,
    /// ya da bekleyen iş yokken verilmeyebilir; aksi halde yedeksiz migration çalıştırılmaz.</param>
    /// <param name="depo">Belge deposu (Program.cs verir). Belge içerikleri henüz veritabanındaysa zorunludur; bellek içi
    /// veritabanında verilmezse geçici dizinde depo kullanılır.</param>
    /// <param name="disk">Belge deposu geçişinden önce boş alan denetimi; verilmezse denetlenmez.</param>
    public static void Initialize(KasaDbContext db, YedekServisi? yedek = null, BelgeDeposu? depo = null, IDiskAlani? disk = null)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) connection.Open();
        try
        {
            var kopru = !HasMigrationHistory(connection) && StableSchemaDefinition.Tables.Any(t => TableExists(connection, t.Name));
            GocOncesiYedek(db, connection, kopru, yedek);
            var kartIadeAdimi = db.Database.GetPendingMigrations().Contains(KartTakipDuzeltmeleri.Kimlik);
            var belgeDeposuGecisi = db.Database.GetPendingMigrations().Contains(BelgeDeposuGocu.Kimlik);
            if (kopru) BridgeLegacyDatabase(connection);

            var kanalKumesiGecisi = db.Database.GetPendingMigrations().Contains(AyKanalKumesi.MigrationId);
            if (belgeDeposuGecisi) BelgeDeposunaGecis(db, connection, depo, disk);
            db.Database.Migrate();
            if (belgeDeposuGecisi) Sikistir(db, connection);
            WalKipineAl(db, connection);
            if (kanalKumesiGecisi) KanalKumesiGecisi(db);
            GecisTohumu(db);
            if (kartIadeAdimi) KartIadeTohumu(db);
        }
        finally
        {
            if (openedHere) connection.Close();
        }
    }

    /// <summary>
    /// Göç öncesi yedek (kullanıcı kararı: mimari/veri dönüşümleri otomatik migration'dır ve her birinden önce otomatik,
    /// tutarlı yedek alınır). Bellek içi veritabanında ve henüz tablo içermeyen yeni dosyada korunacak veri yoktur; bekleyen
    /// iş yoksa (olağan açılış) yedek alınmaz. Yedek SQLite yedekleme API'siyle (tutarlı anlık görüntü) alınır ve
    /// doğrulanır; herhangi bir hata migration'dan önce açılışı durdurur: veritabanı hiç değiştirilmemiş olur.
    /// </summary>
    private static void GocOncesiYedek(KasaDbContext db, SqliteConnection connection, bool kopru, YedekServisi? yedek)
    {
        if (string.IsNullOrEmpty(Convert.ToString(Scalar(connection, "SELECT file FROM pragma_database_list WHERE name = 'main';"), System.Globalization.CultureInfo.InvariantCulture))
            || Scalar(connection, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' LIMIT 1;") is null)
            return;
        var bekleyen = new List<string>();
        if (kopru) bekleyen.Add("Eski şema köprüsü (migration geçmişi yok)");
        bekleyen.AddRange(db.Database.GetPendingMigrations());
        bekleyen.AddRange(AyRaporAnlikGoruntusu.BekleyenTohum(connection));
        if (BelgeDeposuAktarimi.BekleyenIs(connection) is { } belgeAktarimi) bekleyen.Add(belgeAktarimi);
        if (bekleyen.Count == 0) return;
        if (yedek is null)
            throw new InvalidOperationException("Kasa veritabanında bekleyen güncelleme var ancak göç öncesi yedek servisi verilmedi; yedeksiz güncelleme yapılmaz. Veritabanı değiştirilmedi.");
        string yol;
        try { yol = yedek.GocOncesiYedekAl(connection, bekleyen); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Kasa veritabanı güncellenmeden önce göç öncesi yedek alınamadı: {ex.Message} Güncelleme çalıştırılmadı; veritabanı değiştirilmedi. "
                + $"Yedek dizinini ({yedek.Dizin}), boş disk alanını ve yazma izinlerini kontrol edip uygulamayı yeniden başlatın. Veritabanını silmeyin.", ex);
        }
        db.GetService<ILoggerFactory>().CreateLogger(typeof(KasaDatabaseInitializer))
            .LogInformation("Göç öncesi yedek hazır: {Yedek}. Bekleyen işler: {Isler}.", yol, string.Join(", ", bekleyen));
    }

    /// <summary>
    /// Belge deposu geçişi (data-3, gap-okuma-yolu-maliyet-kilit-cekismesi-8; göç öncesi yedekten sonra, BLOB sütunlarını düşüren
    /// migration'dan önce; bkz. <see cref="BelgeDeposuAktarimi"/>): taşınacak içerik varsa depo zorunludur ve deposunun diskinde
    /// içeriklerin tamamı + 256 MB boş alan aranır; yetmezse ya da bir ekstre PDF'i kayıtlı özetiyle eşleşmezse açılış veritabanı
    /// değiştirilmeden durur. Sonra hazırlık migration'ı uygulanır ve alış belgeleri satır satır aktarılır (yarıda kalırsa sonraki
    /// açılış kaldığı yerden sürer).
    /// </summary>
    private static void BelgeDeposunaGecis(KasaDbContext db, SqliteConnection connection, BelgeDeposu? depo, IDiskAlani? disk)
    {
        var logger = db.GetService<ILoggerFactory>().CreateLogger(typeof(KasaDatabaseInitializer));
        var tasinacak = BelgeDeposuAktarimi.TasinacakIcerik(connection);
        if (depo is null && BellekIci(connection)) depo = BelgeDeposu.Gecici();
        if (tasinacak.Sayi > 0)
        {
            if (depo is null)
                throw new InvalidOperationException("Belge içerikleri belge deposuna taşınmalı ancak belge deposu verilmedi; veritabanı değiştirilmedi.");
            const long pay = 256L * 1024 * 1024;
            if (disk?.BosAlan(depo.Kok) is { } bos && bos < tasinacak.Bayt + pay)
                throw new InvalidOperationException(
                    $"Belge deposuna ({depo.Kok}) {tasinacak.Sayi} belge ({BelgeDeposuAktarimi.Mb(tasinacak.Bayt)} MB) taşınacak ancak diskte "
                    + $"{BelgeDeposuAktarimi.Mb(bos)} MB boş alan var (gereken en az {BelgeDeposuAktarimi.Mb(tasinacak.Bayt + pay)} MB). "
                    + "Güncelleme çalıştırılmadı; veritabanı değiştirilmedi. Disk alanı açıp uygulamayı yeniden başlatın.");
            logger.LogInformation("Belge deposu geçişi başlıyor: {Belge} alış belgesi, {Ekstre} ekstre PDF'i ({Mb} MB) → {Depo}.",
                tasinacak.Belge, tasinacak.Ekstre, BelgeDeposuAktarimi.Mb(tasinacak.Bayt), depo.Kok);
            BelgeDeposuAktarimi.EkstreleriAktar(connection, depo, logger);
        }
        // Hedefli Migrate hedeften sonraki uygulanmış migration'ları geri alır: yalnız hazırlık bekliyorsa ve içerikleri düşüren
        // migration henüz uygulanmamışsa (ör. eşzamanlı ikinci başlangıç onu tamamlamadıysa) çalıştırılır.
        var bekleyen = db.Database.GetPendingMigrations().ToList();
        if (!bekleyen.Contains(BelgeDeposuGocu.Kimlik)) return;
        if (bekleyen.Contains(BelgeDeposuHazirlik.Kimlik))
        {
            try { db.GetService<IMigrator>().Migrate(BelgeDeposuHazirlik.Kimlik); }
            catch (NotSupportedException)
            {
                // Denetim ile Migrate arasında eşzamanlı başka bir başlangıç (ör. aynı anda açılan ikinci konteyner) geçişi tamamladıysa
                // EF hedefe inmek için içerikleri düşüren migration'ı geri almayı dener; Down hiçbir komut çalıştırmadan reddeder. EF bu
                // yolda SQLite migration kilidini (__EFMigrationsLock satırı) bırakmaz; Down kilit alındıktan sonra üretildiği için satır
                // bu başlangıcındır ve burada bırakılır (yoksa sonraki her Migrate sonsuza dek bekler). Geçiş tamamlanmıştır: açılış sürer.
                Execute(connection, "DELETE FROM \"__EFMigrationsLock\";");
                if (db.Database.GetPendingMigrations().Contains(BelgeDeposuGocu.Kimlik)) throw;
                return;
            }
        }
        if (depo is not null) BelgeDeposuAktarimi.BelgeleriAktar(connection, depo, logger);
    }

    /// <summary>BLOB sütunları düşürüldükten sonra bir kez (transaction dışında): VACUUM dosyayı yeniden yazar, silinmiş belge
    /// içerikleri serbest sayfalardan da gider ve dosya küçülür; WAL'daki kopya sıfırlanır. Başarısızlık açılışı durdurmaz
    /// (veri doğrudur, yalnız dosya büyük kalır): uyarı yazılır, sonraki sürümde elle çalıştırılabilir.</summary>
    private static void Sikistir(KasaDbContext db, SqliteConnection connection)
    {
        if (BellekIci(connection)) return;
        var logger = db.GetService<ILoggerFactory>().CreateLogger(typeof(KasaDatabaseInitializer));
        try
        {
            var once = Boyut(connection);
            Execute(connection, "VACUUM;");
            if (string.Equals(Convert.ToString(Scalar(connection, "PRAGMA journal_mode;"), System.Globalization.CultureInfo.InvariantCulture), "wal", StringComparison.OrdinalIgnoreCase))
                Execute(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
            logger.LogInformation("Belge içerikleri veritabanından çıkarıldı ve veritabanı sıkıştırıldı: {Once} MB → {Sonra} MB.",
                BelgeDeposuAktarimi.Mb(once), BelgeDeposuAktarimi.Mb(Boyut(connection)));
        }
        catch (SqliteException ex)
        {
            logger.LogWarning(ex, "Belge deposu geçişinden sonra VACUUM çalıştırılamadı; veriler doğrudur, veritabanı dosyası küçülmedi.");
        }
    }

    private static long Boyut(SqliteConnection connection) =>
        Convert.ToInt64(Scalar(connection, "PRAGMA page_count;")) * Convert.ToInt64(Scalar(connection, "PRAGMA page_size;"));

    private static bool BellekIci(SqliteConnection connection) =>
        string.IsNullOrEmpty(Convert.ToString(Scalar(connection, "SELECT file FROM pragma_database_list WHERE name = 'main';"), System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Kalıcı WAL günlük kipi: okuyucular yazanı, yazan okuyucuları bekletmez (okuma uçları DEFERRED anlık görüntüde
    /// çalışır, bkz. <see cref="OkumaAnlikGoruntusu"/>). Kip dosya başlığında kalıcıdır; her açılışta yeniden istemek
    /// etkisizdir. WAL'da <c>kasa.db-wal</c> ve <c>kasa.db-shm</c> veritabanının parçasıdır: uygulama çalışırken yalnız
    /// <c>kasa.db</c> kopyalanmaz (yedek SQLite yedekleme API'siyle alınır), geri yüklemede eski -wal/-shm kaldırılır.
    /// synchronous varsayılan (FULL) kalır: her commit diske işlenir. Bellek içi veritabanı "memory" kipinde kalır.
    /// WAL açılamazsa (ör. paylaşılan bellek desteklemeyen dosya sistemi) uygulama eski kiple çalışır ve uyarı yazılır:
    /// okumalar yine tutarlıdır, yalnız yazanı bekletebilir.
    /// </summary>
    private static void WalKipineAl(KasaDbContext db, SqliteConnection connection)
    {
        var mode = Convert.ToString(Scalar(connection, "PRAGMA journal_mode = WAL;"), System.Globalization.CultureInfo.InvariantCulture);
        if (mode is "wal" or "memory") return;
        db.GetService<ILoggerFactory>().CreateLogger(typeof(KasaDatabaseInitializer))
            .LogWarning("Veritabanı WAL kipine alınamadı (günlük kipi: {Kip}); okumalar yazma işlemlerini bekletebilir.", mode);
    }

    /// <summary>Veri adımı (ay kanal kümesi migration'ının uygulandığı açılışta, göç öncesi yedekten sonra): takip başlangıcından geçen
    /// aya kadar her tamamlanmış ayın kanal kümesi bugünkü kanallarla dondurulur (bkz. <see cref="AyKanalKumesi.GecisDondurmasi"/>);
    /// geçmiş raporlar birebir aynı kalır. Rapor görüntüsü tohumundan önce çalışır: görüntüsüz kilitli ay da kendi kümesini alır.</summary>
    private static void KanalKumesiGecisi(KasaDbContext db)
    {
        var aylar = AyKanalKumesi.GecisDondurmasi(db);
        if (aylar.Count == 0) return;
        db.GetService<ILoggerFactory>().CreateLogger(typeof(KasaDatabaseInitializer)).LogInformation(
            "Tamamlanmış {Sayi} ayın kanal kümesi (Ortak gider dağılımı ve rapor satırları) bugünkü kanallarla donduruldu: {Aylar}.", aylar.Count,
            string.Join(", ", aylar.Select(AyKanalKumesi.AyMetni)));
    }

    /// <summary>Veri adımı: bu sürümden önce kilitlenmiş ayların raporu kural 1 ile dondurulur (bkz.
    /// <see cref="AyRaporAnlikGoruntusu.GecisTohumu"/>; idempotent, göç öncesi yedekten sonra çalışır).</summary>
    private static void GecisTohumu(KasaDbContext db)
    {
        var aylar = AyRaporAnlikGoruntusu.GecisTohumu(db, db.Saati().GetUtcNow());
        if (aylar.Count == 0) return;
        db.GetService<ILoggerFactory>().CreateLogger(typeof(KasaDatabaseInitializer)).LogInformation(
            "Bu sürümden önce kilitlenmiş {Sayi} ayın raporu kural 1 ile donduruldu: {Aylar}.", aylar.Count,
            string.Join(", ", aylar.Select(a => $"{a.Yil:D4}-{a.Ay:D2}")));
    }

    /// <summary>Veri adımı: eski kart iadelerinin hesap kaydı (bkz. <see cref="FinansTakipServisi.IadeHesabiTohumu"/>). Kart
    /// takibi düzeltmeleri göçü bu açılışta uygulandıysa bir kez, göç öncesi yedekten sonra çalışır; raporlar değişmez.</summary>
    private static void KartIadeTohumu(KasaDbContext db)
    {
        var (eslesen, eslesmeyen) = FinansTakipServisi.IadeHesabiTohumu(db);
        if (eslesen + eslesmeyen == 0) return;
        db.GetService<ILoggerFactory>().CreateLogger(typeof(KasaDatabaseInitializer)).LogInformation(
            "Kart iadelerinin hesap kaydı yazıldı: {Eslesen} iade kaynak harcamanın güncel payını izleyecek, {Eslesmeyen} iade dondurulmuş payıyla kalır. Raporlar değişmedi.",
            eslesen, eslesmeyen);
    }

    private static void BridgeLegacyDatabase(SqliteConnection connection)
    {
        // SQLite tablo yeniden oluşturma sırasında eski CASCADE/SET NULL davranışlarının
        // canlı kayıtları değiştirmesini önle. Son durumda bütün FK'ler kontrol edilir.
        var foreignKeys = Convert.ToInt32(Scalar(connection, "PRAGMA foreign_keys;"));
        Execute(connection, "PRAGMA foreign_keys = OFF;");
        try
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            // İki süreç aynı anda açılmışsa ilk sürecin tamamladığı geçişi tekrarlama.
            if (HasMigrationHistory(connection, transaction))
            {
                transaction.Commit();
                return;
            }

            foreach (var table in StableSchemaDefinition.Tables)
            {
                if (!TableExists(connection, table.Name, transaction))
                {
                    Execute(connection, table.Create(), transaction);
                    continue;
                }

                CheckCustomSchema(connection, transaction, table);
                var columns = ColumnNames(connection, transaction, table.Name);
                foreach (var column in table.Columns.Where(c => !columns.Contains(c)))
                {
                    if (table.AdditiveColumns?.TryGetValue(column, out var declaration) != true)
                        throw CannotUpgrade($"{table.Name}.{column} zorunlu alanı bulunamadı.");
                    Execute(connection, $"ALTER TABLE \"{table.Name}\" ADD COLUMN \"{column}\" {declaration};", transaction);
                }
            }

            CheckDuplicates(connection, transaction);
            BackfillChannels(connection, transaction);
            var duplicateIncome = HasDuplicateIncome(connection, transaction);

            // Her sütun açıkça kopyalanır; decimal ve tarih metinleri dönüştürülmez.
            // AUTOINCREMENT'in silinmiş en yüksek Id bilgisini de koru.
            foreach (var table in StableSchemaDefinition.Tables)
            {
                var sequence = Scalar(connection, "SELECT seq FROM sqlite_sequence WHERE name = $name;", transaction, ("$name", table.Name));
                var temporaryName = "__kasa_upgrade_" + table.Name;
                if (TableExists(connection, temporaryName, transaction))
                    throw CannotUpgrade($"Geçici tablo adı kullanımda: {temporaryName}.");
                Execute(connection, table.Create(temporaryName), transaction);
                var columns = string.Join(", ", table.Columns.Select(c => $"\"{c}\""));
                Execute(connection, $"INSERT INTO \"{temporaryName}\" ({columns}) SELECT {columns} FROM \"{table.Name}\";", transaction);
                Execute(connection, $"DROP TABLE \"{table.Name}\"; ALTER TABLE \"{temporaryName}\" RENAME TO \"{table.Name}\";", transaction);
                if (sequence is not null and not DBNull)
                    Execute(connection, "UPDATE sqlite_sequence SET seq = MAX(seq, $sequence) WHERE name = $name;", transaction,
                        ("$sequence", sequence), ("$name", table.Name));
            }

            foreach (var (name, sql) in StableSchemaDefinition.Indexes)
            {
                // Eski gelirlerin tamamını koru. Bu iki index, ileri migration'da
                // eski gruplar işaretlendikten sonra filtreli olarak kurulacak.
                // Arada süreç durursa initial history sayesinde kalan migration'lar
                // sonraki başlangıçta tamamlanır; henüz HTTP sunucusu açılmamıştır.
                if (duplicateIncome && name is "IX_Gelenler_DonemStart_KanalId" or "IX_Gelenler_DonemStart_Kanal") continue;
                Execute(connection, sql, transaction);
            }

            using (var check = Command(connection, "PRAGMA foreign_key_check;", transaction))
            using (var reader = check.ExecuteReader())
            {
                if (reader.Read())
                    throw CannotUpgrade($"{reader.GetString(0)} tablosunda {reader.GetValue(1)} kimlikli kaydın ilişkisi geçersiz.");
            }

            Execute(connection, """
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL);
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ($id, $version);
                """, transaction, ("$id", StableSchemaDefinition.MigrationId), ("$version", StableSchemaDefinition.ProductVersion));
            transaction.Commit();
        }
        finally
        {
            Execute(connection, $"PRAGMA foreign_keys = {(foreignKeys == 1 ? "ON" : "OFF")};");
        }
    }

    private static void CheckCustomSchema(SqliteConnection connection, SqliteTransaction transaction, StableSchemaDefinition.Table table)
    {
        var unknown = ColumnNames(connection, transaction, table.Name).Except(table.Columns).ToArray();
        if (unknown.Length != 0)
            throw CannotUpgrade($"{table.Name} tablosunda tanınmayan alanlar var: {string.Join(", ", unknown)}.");

        using var command = Command(connection, "SELECT name, type FROM sqlite_master WHERE tbl_name = $name AND type IN ('trigger', 'index') AND sql IS NOT NULL;", transaction, ("$name", table.Name));
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (reader.GetString(1) == "trigger" || !StableSchemaDefinition.Indexes.Any(i => i.Name == reader.GetString(0)))
                throw CannotUpgrade($"{table.Name} tablosunda özel şema nesnesi var: {reader.GetString(0)}.");
    }

    private static void CheckDuplicates(SqliteConnection connection, SqliteTransaction transaction)
    {
        var duplicateChannel = Scalar(connection, "SELECT Ad FROM Kanallar GROUP BY Ad COLLATE NOCASE HAVING COUNT(*) > 1 LIMIT 1;", transaction);
        if (duplicateChannel is not null)
            throw CannotUpgrade($"Aynı adlı birden fazla kanal var: '{duplicateChannel}'. Kanal kimliklerini ve hareketlerini inceleyip birleştirmeden geçiş yapılamaz.");

        var reservedChannel = Scalar(connection, "SELECT Ad FROM Kanallar WHERE Ad COLLATE NOCASE IN ($common, $credit) LIMIT 1;", transaction,
            ("$common", Kanallar.Ortak), ("$credit", KrediTuretici.KrediKanal));
        if (reservedChannel is not null)
            throw CannotUpgrade($"'{reservedChannel}' özel muhasebe etiketi gerçek kanal olarak kullanılmış.");

    }

    private static void BackfillChannels(SqliteConnection connection, SqliteTransaction transaction)
    {
        foreach (var table in new[] { "Islemler", "Gelenler", "Krediler" })
        {
            Execute(connection, $"""
                INSERT INTO Kanallar (Ad, Aktif, Sira, AcilisDevri)
                SELECT MIN(v.Kanal), 0, COALESCE((SELECT MAX(Sira) + 1 FROM Kanallar), 0), '0.0'
                FROM "{table}" v
                WHERE v.KanalId IS NULL AND v.Kanal COLLATE NOCASE NOT IN ($common, $credit)
                    AND NOT EXISTS (SELECT 1 FROM Kanallar k WHERE k.Ad = v.Kanal COLLATE NOCASE)
                GROUP BY v.Kanal COLLATE NOCASE;
                UPDATE "{table}" SET KanalId = (SELECT k.Id FROM Kanallar k WHERE k.Ad = "{table}".Kanal COLLATE NOCASE)
                WHERE KanalId IS NULL AND Kanal COLLATE NOCASE NOT IN ($common, $credit);
                UPDATE "{table}" SET Kanal = $common WHERE KanalId IS NULL AND Kanal COLLATE NOCASE = $common;
                UPDATE "{table}" SET Kanal = $credit WHERE KanalId IS NULL AND Kanal COLLATE NOCASE = $credit;
                """, transaction, ("$common", Kanallar.Ortak), ("$credit", KrediTuretici.KrediKanal));
        }
    }

    private static bool HasDuplicateIncome(SqliteConnection connection, SqliteTransaction transaction) =>
        Scalar(connection, "SELECT 1 FROM Gelenler GROUP BY DonemStart, Kanal COLLATE NOCASE HAVING COUNT(*) > 1 LIMIT 1;", transaction) is not null
        || Scalar(connection, "SELECT 1 FROM Gelenler WHERE KanalId IS NOT NULL GROUP BY DonemStart, KanalId HAVING COUNT(*) > 1 LIMIT 1;", transaction) is not null;

    private static InvalidOperationException CannotUpgrade(string detail) => new(
        $"Kasa veritabanı güvenli biçimde güncellenemedi. {detail} Geçiş geri alındı; mevcut veriler korundu. Veritabanını silmeyin; yedek üzerinde verileri inceleyin.");

    private static HashSet<string> ColumnNames(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = Command(connection, $"PRAGMA table_info(\"{table}\");", transaction);
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) columns.Add(reader.GetString(1));
        return columns;
    }

    private static bool HasMigrationHistory(SqliteConnection connection, SqliteTransaction? transaction = null) =>
        TableExists(connection, "__EFMigrationsHistory", transaction) &&
        Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\";", transaction)) > 0;

    private static bool TableExists(SqliteConnection connection, string name, SqliteTransaction? transaction = null) =>
        Scalar(connection, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;", transaction, ("$name", name)) is not null;

    private static SqliteCommand Command(SqliteConnection connection, string sql, SqliteTransaction? transaction = null,
        params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private static object? Scalar(SqliteConnection connection, string sql, SqliteTransaction? transaction = null,
        params (string Name, object Value)[] parameters)
    {
        using var command = Command(connection, sql, transaction, parameters);
        return command.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null,
        params (string Name, object Value)[] parameters)
    {
        using var command = Command(connection, sql, transaction, parameters);
        command.ExecuteNonQuery();
    }
}
