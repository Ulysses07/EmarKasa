using System.Data;
using System.Globalization;
using Kasa.Api.Data;
using Kasa.Api.Denetim;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kasa.Api.Auth;

/// <summary>
/// Yedekten geri yüklenmiş veritabanının ilk açılışta işlenmesi (gap-geri-yukleme-durum-geri-sarma-2, -6).
/// <para>Geri yükleme veritabanındaki bütün durumu yedek anına sarar: izleyici şifresi değişikliği, editör ve alıcı oturum
/// iptalleri, AUTOINCREMENT sayaçları ve kayıt sürümleri. Yedekten sonra neyin değiştiği (ör. ayrılan biri yüzünden izleyici
/// şifresinin değiştirilmesi) atılan soydadır; geri yüklenen dosyadan bilinemez. Bu yüzden işlem koşulsuzdur ve güvenli
/// yöndedir.</para>
/// <para>Tanıma: uygulamanın aldığı her yedek kopyası (<see cref="Servisler.YedekServisi"/>) ve restore_backup.py'nin geri
/// açtığı her dosya SQLite başlığında <see cref="Isaret"/> taşır (<c>PRAGMA user_version</c>; şema değildir, EF kullanmaz).
/// Canlı dosyada işaret yoktur (0). Program.cs başlatıcıdan (migration) sonra, HTTP sunucusu açılmadan çağırır; işaretli
/// dosyada tek transaction'da:</para>
/// <list type="bullet">
/// <item>Bütün oturumlar kapanır, Kasa:JwtKey'e dokunulmaz: editör güvenlik kaydının sürümü artar (kayıt yoksa sürüm 1 ile
/// açılır; ortam şifresindeki editörün damgası da sürümü içerir, <see cref="EditorGuvenligi.Kaynak"/>), her alıcının oturum
/// sürümü artar, izleyici şifresi silinir. Oturum damgası (<see cref="OturumDamgasi"/>) değiştiğinden yedekten önce ya da
/// sonra alınmış bütün oturum belirteçleri, tanıdık cihaz belirteçleri (<see cref="TanidikCihaz"/>) ve editör damgasına bağlı
/// bildirim abonelikleri geçersiz olur. Böylece oturum damgası veri soyunu taşır: geri yüklemeden önce alınmış hiçbir belirteç
/// yeni soyda geçmez. Açık kalan masaüstü/web ekranı eski soydaki (Id, Surum) çiftiyle ya da bekleyen tekrar anahtarıyla
/// yazamaz: 401 alır, istemci oturumu kapatıp ekran durumunu atar.</item>
/// <item>İzleyici girişi kapanır: editör yeni izleyici şifresi belirleyene kadar izleyici giremez; yedekteki eski şifre de
/// yedekten sonra belirlenen de geçersizdir. Uyarıyla yetinilmez: izleyici bütün finans verisini (dışa aktarma dahil) okur ve
/// uyarıyı editör görene kadar ayrılan kişi erişebilirdi. Bedeli izleyicinin kısa kesintisidir; editör ayarlarda şifreyi yok
/// görür.</item>
/// <item>Kimlikler ileri alınır: AUTOINCREMENT'li her tablonun sayacı MAX(sayaç, en yüksek kimlik) + <see cref="KimlikAraligi"/>
/// olur (sayaç satırı olmayan tabloya satır eklenir). Atılan soyda açılmış kayıtların kimlikleri yeni kayda verilmez; eski
/// ekranın ya da tekrarın taşıdığı kimlik başka kayda ulaşamaz (404). Yedek anında var olan kayıtların sürümü de geri sarıldığı
/// için eski sürümle gelen yazma 409 alır. Kimlikler 32 bittir; her geri yükleme 1.000.000 tüketir (~2.000 geri yükleme).
/// Aynı yedek ikinci kez geri yüklenirse ilk geri yüklemeden sonra açılan kayıtların kimlikleri yeniden verilebilir;
/// oturumlar yine kapandığından eski ekranlar yazamaz.</item>
/// <item>İşaret silinir ve 'Oturum ve güvenlik' izine <see cref="OlayTuru"/> olayı (veri soyu kimliği, yeni sayaçlar,
/// kapatılan oturumlar) yazılır; izleyici şifresi ve alıcı oturumu değişiklikleri kendi olaylarıyla (aktör sistem) görünür.
/// Kasa.Guvenlik loguna uyarı düşer. Sonraki açılışlarda yeniden çalışmaz; hata olursa hiçbir şey değişmez ve açılış durur.</item>
/// </list>
/// <para>İşaretsiz yedek (bu sürümden önce alınmış) restore_backup.py dışında, ZIP'ten elle çıkarılarak canlıya alınırsa
/// tanınmaz; runbook ("Geri yüklemeden sonra") aracı zorunlu tutar. Editör şifresi, kurtarma kodu ve alıcı etkinliği yedek
/// anındaki değerlerine döner; bunları yeniden belirlemek runbook'taki operatör adımlarıdır.</para>
/// </summary>
public static class GeriYuklemeIsleyici
{
    /// <summary>Yedek kopyasının başlığındaki işaret (<c>PRAGMA user_version</c>, ASCII "KSGY"). restore_backup.py'deki
    /// GERI_YUKLEME_ISARETI ile aynıdır.</summary>
    public const int Isaret = 0x4B534759;
    /// <summary>Geri yüklemede sayaçların ileri alındığı pay (restore_backup.py'deki KIMLIK_ARALIGI ile aynı).</summary>
    public const int KimlikAraligi = 1_000_000;
    public const string OlayTuru = GuvenlikOlaylari.GeriYuklemeIslendi;

    /// <param name="Soy">Geri yüklemeyle başlayan veri soyunun kimliği (olayın VarlikId'si).</param>
    /// <param name="Sayaclar">Tablo → ileri alınmış sayaç.</param>
    public sealed record Sonuc(Guid Soy, IReadOnlyDictionary<string, long> Sayaclar, bool IzleyiciErisimiKapatildi, int AliciOturumlariKapatildi);

    /// <summary>Veritabanı geri yükleme işaretliyse işler ve sonucu döner; değilse hiçbir şeye dokunmaz (null).</summary>
    public static Sonuc? Isle(KasaDbContext db)
    {
        var baglanti = (SqliteConnection)db.Database.GetDbConnection();
        var acildi = baglanti.State != ConnectionState.Open;
        if (acildi) db.Database.OpenConnection();
        try
        {
            // Olağan açılışta yazma kilidi alınmaz; aynı dosyayla aynı anda açılan iki süreçte işareti transaction içinde yeniden okur.
            if (!Isaretli(baglanti, null)) return null;
            using var tx = db.Database.BeginTransaction();
            var sqliteTx = (SqliteTransaction)tx.GetDbTransaction();
            if (!Isaretli(baglanti, sqliteTx)) { tx.Commit(); return null; }

            var sonOlay = Deger(baglanti, sqliteTx, "SELECT MAX(\"ZamanUtc\") FROM \"DenetimOlaylari\";");
            var sayaclar = KimlikleriIlerlet(baglanti, sqliteTx);

            var ayar = db.Ayarlar.FirstOrDefault();
            var izleyiciKapatildi = ayar?.IzleyiciSifreHash is not null;
            if (ayar is not null) ayar.IzleyiciSifreHash = null;
            var alicilar = db.Alicilar.ToList();
            foreach (var a in alicilar) a.OturumSurumu++;
            var editor = db.EditorGuvenlik.SingleOrDefault(e => e.Id == 1);
            if (editor is null) db.EditorGuvenlik.Add(editor = new EditorGuvenlikEntity());
            editor.Surum++;
            db.SaveChanges();

            var soy = Guid.NewGuid();
            DenetimYazici.Yaz(db, new DenetimOlayi(OlayTuru, GuvenlikOlaylari.Varlik, soy.ToString("D"), null, DenetimYazici.Json(new
            {
                soy,
                kimlikAraligi = KimlikAraligi,
                sayaclar,
                oturumlarKapatildi = true,
                izleyiciErisimiKapatildi = izleyiciKapatildi,
                aliciOturumlariKapatildi = alicilar.Count,
                yedektekiSonOlay = sonOlay is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : (DateTimeOffset?)null,
            }), Aktor: DenetimAktoru.Sistem, BaslikGerekcesi: false));
            Calistir(baglanti, sqliteTx, "PRAGMA user_version = 0;");
            tx.Commit();

            db.GetService<ILoggerFactory>().CreateLogger("Kasa.Guvenlik").LogWarning(
                "Geri yüklenmiş veritabanı tanındı ve işlendi (veri soyu {Soy}): bütün oturumlar kapatıldı, izleyici girişi {Izleyici}, "
                + "{Sayi} tablonun kayıt numarası {Aralik} ileri alındı. Editör yeni izleyici şifresi belirlemeli (runbook: Geri yüklemeden sonra).",
                soy, izleyiciKapatildi ? "kapatıldı" : "zaten kapalıydı", sayaclar.Count, KimlikAraligi);
            return new Sonuc(soy, sayaclar, izleyiciKapatildi, alicilar.Count);
        }
        finally
        {
            if (acildi) db.Database.CloseConnection();
        }
    }

    private static bool Isaretli(SqliteConnection baglanti, SqliteTransaction? tx)
        => Convert.ToInt64(Deger(baglanti, tx, "PRAGMA user_version;"), CultureInfo.InvariantCulture) == Isaret;

    /// <summary>AUTOINCREMENT'li her tabloyu (sqlite_master'daki tanımından) MAX(sayaç, en yüksek rowid) + aralığa taşır.</summary>
    private static SortedDictionary<string, long> KimlikleriIlerlet(SqliteConnection baglanti, SqliteTransaction tx)
    {
        var tablolar = new List<string>();
        using (var komut = Komut(baglanti, tx, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND sql LIKE '%AUTOINCREMENT%' ORDER BY name;"))
        using (var okuyucu = komut.ExecuteReader())
            while (okuyucu.Read()) tablolar.Add(okuyucu.GetString(0));
        var sonuc = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var tablo in tablolar)
        {
            var enYuksek = Convert.ToInt64(Deger(baglanti, tx,
                $"SELECT MAX(COALESCE((SELECT seq FROM sqlite_sequence WHERE name = $ad), 0), COALESCE((SELECT MAX(rowid) FROM \"{tablo.Replace("\"", "\"\"")}\"), 0));",
                ("$ad", tablo)), CultureInfo.InvariantCulture);
            var yeni = enYuksek + KimlikAraligi;
            if (Calistir(baglanti, tx, "UPDATE sqlite_sequence SET seq = $seq WHERE name = $ad;", ("$seq", yeni), ("$ad", tablo)) == 0)
                Calistir(baglanti, tx, "INSERT INTO sqlite_sequence (name, seq) VALUES ($ad, $seq);", ("$ad", tablo), ("$seq", yeni));
            sonuc[tablo] = yeni;
        }
        return sonuc;
    }

    private static SqliteCommand Komut(SqliteConnection baglanti, SqliteTransaction? tx, string sql, params (string Ad, object Deger)[] parametreler)
    {
        var komut = baglanti.CreateCommand();
        komut.Transaction = tx;
        komut.CommandText = sql;
        foreach (var (ad, deger) in parametreler) komut.Parameters.AddWithValue(ad, deger);
        return komut;
    }

    private static object? Deger(SqliteConnection baglanti, SqliteTransaction? tx, string sql, params (string Ad, object Deger)[] parametreler)
    {
        using var komut = Komut(baglanti, tx, sql, parametreler);
        var deger = komut.ExecuteScalar();
        return deger is DBNull ? null : deger;
    }

    private static int Calistir(SqliteConnection baglanti, SqliteTransaction tx, string sql, params (string Ad, object Deger)[] parametreler)
    {
        using var komut = Komut(baglanti, tx, sql, parametreler);
        return komut.ExecuteNonQuery();
    }
}
