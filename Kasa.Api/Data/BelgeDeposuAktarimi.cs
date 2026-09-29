using System.Globalization;
using Kasa.Api.Servisler;
using Microsoft.Data.Sqlite;

namespace Kasa.Api.Data;

/// <summary>
/// Belge deposu geçişinin veri adımı (data-3, gap-okuma-yolu-maliyet-kilit-cekismesi-8). Başlatıcı
/// (<see cref="KasaDatabaseInitializer"/>) belge deposu migration'ı bekliyorsa, göç öncesi yedekten SONRA ve BLOB sütunlarını düşüren
/// migration'dan (<see cref="Migrations.BelgeDeposuGocu"/>) ÖNCE çalıştırır:
/// <list type="number">
/// <item><see cref="EkstreleriAktar"/> (hazırlık migration'ından önce, veritabanına yazmaz): her ekstre PDF'i depoya yazılır ve
/// hesaplanan özet kayıtlı DosyaOzeti ile karşılaştırılır. Eşleşmeyen tek bir belge açılışı açıklayıcı hatayla durdurur;
/// veritabanı hiç değiştirilmemiş olur.</item>
/// <item><see cref="BelgeleriAktar"/> (hazırlık migration'ından sonra): özeti boş her alış belgesi depoya yazılır, dosya geri
/// okunup özeti doğrulanır ve satır başına küçük bir transaction'da IcerikOzeti doldurulur.</item>
/// </list>
/// İçerikler SqliteBlob ile akış halinde okunur (bellek sınırlı konteynerde belge başına en çok okuma tamponu kadar bellek).
/// Adım idempotenttir: yarıda kalırsa (süreç düşer, disk dolar) sonraki açılış kaldığı yerden sürer; depoda zaten bulunan içerik
/// yeniden yazılmaz, yalnız doğrulanır. Ham SQL'dir, denetim olayı üretmez (içerik ve raporlar değişmez).
/// </summary>
public static class BelgeDeposuAktarimi
{
    /// <summary>Depoya taşınacak içerik: BLOB sütunları hâlâ duran tablolardaki (alış belgelerinde henüz özeti olmayan) satırlar.</summary>
    public readonly record struct Tasinacak(int Belge, int Ekstre, long Bayt)
    {
        public int Sayi => Belge + Ekstre;
    }

    public static Tasinacak TasinacakIcerik(SqliteConnection c)
    {
        int belge = 0, ekstre = 0;
        long bayt = 0;
        if (SutunVar(c, "Belgeler", "Icerik"))
        {
            var bekleyen = SutunVar(c, "Belgeler", "IcerikOzeti") ? " WHERE \"IcerikOzeti\" IS NULL" : "";
            (belge, bayt) = SayiVeBayt(c, $"SELECT COUNT(*), COALESCE(SUM(length(\"Icerik\")), 0) FROM \"Belgeler\"{bekleyen};");
        }
        if (SutunVar(c, "EkstreBelgeler", "Dosya"))
        {
            var (sayi, boyut) = SayiVeBayt(c, "SELECT COUNT(*), COALESCE(SUM(length(\"Dosya\")), 0) FROM \"EkstreBelgeler\";");
            ekstre = sayi;
            bayt += boyut;
        }
        return new(belge, ekstre, bayt);
    }

    /// <summary>Göç öncesi yedeğin bekleyen işler listesi için tanım; taşınacak içerik yoksa null.</summary>
    public static string? BekleyenIs(SqliteConnection c)
    {
        var t = TasinacakIcerik(c);
        return t.Sayi == 0 ? null
            : $"Belge içeriklerinin belge deposuna aktarımı: {t.Belge} alış belgesi, {t.Ekstre} ekstre PDF'i ({Mb(t.Bayt)} MB)";
    }

    /// <summary>Ekstre PDF'lerini depoya yazar ve özetlerini doğrular; veritabanına yazmaz. Eşleşmeyen belgede
    /// <see cref="InvalidDataException"/>. Yazılan (doğrulanmış) dosya sayısını döner.</summary>
    public static int EkstreleriAktar(SqliteConnection c, BelgeDeposu depo, ILogger logger)
    {
        if (!SutunVar(c, "EkstreBelgeler", "Dosya"))
            return 0;
        var satirlar = new List<(long Id, string Ozet, string Ad)>();
        using (var komut = c.CreateCommand())
        {
            komut.CommandText = "SELECT \"Id\", \"DosyaOzeti\", \"DosyaAdi\" FROM \"EkstreBelgeler\" ORDER BY \"Id\";";
            using var okuyucu = komut.ExecuteReader();
            while (okuyucu.Read())
                satirlar.Add((okuyucu.GetInt64(0), okuyucu.GetString(1), okuyucu.GetString(2)));
        }
        foreach (var (id, kayitli, ad) in satirlar)
        {
            BelgeYazimi yazim;
            using (var blob = new SqliteBlob(c, "EkstreBelgeler", "Dosya", id, readOnly: true))
                yazim = depo.Yaz(blob);
            if (!string.Equals(yazim.Ozet, kayitli, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Ekstre belgesi {id} ('{ad}') içeriği kayıtlı özetle eşleşmiyor (kayıtlı {Kisa(kayitli)}, hesaplanan {Kisa(yazim.Ozet)}). "
                    + "Belge deposu geçişi durduruldu; veritabanı değiştirilmedi. Göç öncesi yedeği ve bu belgeyi inceleyin; veritabanını silmeyin.");
            if (!depo.Dogrula(yazim.Ozet))
                throw new IOException($"Ekstre belgesi {id} belge deposuna yazıldı ancak geri okunup doğrulanamadı; geçiş durduruldu, veritabanı değiştirilmedi.");
        }
        if (satirlar.Count > 0)
            logger.LogInformation("{Sayi} ekstre PDF'i belge deposuna yazıldı ve özetleri doğrulandı.", satirlar.Count);
        return satirlar.Count;
    }

    /// <summary>Özeti boş alış belgelerini depoya yazar, doğrular ve özetlerini satır satır kaydeder. <paramref name="belgeSonrasi"/>
    /// her satır kaydedildikten sonra çağrılır (sınama). Aktarılan belge sayısını döner.</summary>
    public static int BelgeleriAktar(SqliteConnection c, BelgeDeposu depo, ILogger logger, Action<long>? belgeSonrasi = null)
    {
        if (!SutunVar(c, "Belgeler", "Icerik") || !SutunVar(c, "Belgeler", "IcerikOzeti"))
            return 0;
        var idler = new List<long>();
        using (var komut = c.CreateCommand())
        {
            komut.CommandText = "SELECT \"Id\" FROM \"Belgeler\" WHERE \"IcerikOzeti\" IS NULL ORDER BY \"Id\";";
            using var okuyucu = komut.ExecuteReader();
            while (okuyucu.Read())
                idler.Add(okuyucu.GetInt64(0));
        }
        long toplam = 0;
        for (var i = 0; i < idler.Count; i++)
        {
            var id = idler[i];
            BelgeYazimi yazim;
            using (var blob = new SqliteBlob(c, "Belgeler", "Icerik", id, readOnly: true))
                yazim = depo.Yaz(blob);
            if (!depo.Dogrula(yazim.Ozet))
                throw new IOException($"Alış belgesi {id} belge deposuna yazıldı ancak geri okunup doğrulanamadı; geçiş durduruldu. Aktarılan belgeler korunur, sonraki açılış kaldığı yerden sürer.");
            using (var tx = c.BeginTransaction(deferred: false))
            using (var komut = c.CreateCommand())
            {
                komut.Transaction = tx;
                komut.CommandText = "UPDATE \"Belgeler\" SET \"IcerikOzeti\" = $ozet WHERE \"Id\" = $id AND \"IcerikOzeti\" IS NULL;";
                komut.Parameters.AddWithValue("$ozet", yazim.Ozet);
                komut.Parameters.AddWithValue("$id", id);
                komut.ExecuteNonQuery();
                tx.Commit();
            }
            toplam += yazim.Boyut;
            if ((i + 1) % 100 == 0)
                logger.LogInformation("Belge deposuna aktarım sürüyor: {Aktarilan}/{Toplam} alış belgesi ({Mb} MB).", i + 1, idler.Count, Mb(toplam));
            belgeSonrasi?.Invoke(id);
        }
        if (idler.Count > 0)
            logger.LogInformation("{Sayi} alış belgesi ({Mb} MB) belge deposuna aktarıldı ve doğrulandı.", idler.Count, Mb(toplam));
        return idler.Count;
    }

    public static bool SutunVar(SqliteConnection c, string tablo, string sutun)
    {
        using var komut = c.CreateCommand();
        komut.CommandText = "SELECT 1 FROM pragma_table_info($tablo) WHERE name = $sutun;";
        komut.Parameters.AddWithValue("$tablo", tablo);
        komut.Parameters.AddWithValue("$sutun", sutun);
        return komut.ExecuteScalar() is not null;
    }

    private static (int Sayi, long Bayt) SayiVeBayt(SqliteConnection c, string sql)
    {
        using var komut = c.CreateCommand();
        komut.CommandText = sql;
        using var okuyucu = komut.ExecuteReader();
        okuyucu.Read();
        return (okuyucu.GetInt32(0), okuyucu.GetInt64(1));
    }

    private static string Kisa(string ozet) => ozet.Length > 12 ? ozet[..12] + "…" : ozet;
    public static string Mb(long bayt) => (bayt / 1048576.0).ToString("0.#", CultureInfo.InvariantCulture);
}
