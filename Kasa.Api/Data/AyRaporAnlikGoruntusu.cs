using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kasa.Api.Data;

/// <summary>Kilitli (kapatılmış) ayın aylık raporu: kilitlendiği anda API'nin döndüğü JSON ve onu üreten kural sürümü.
/// Satır yalnız ay kilitliyken bulunur; kilit açılınca silinir (bkz. <see cref="AyRaporAnlikGoruntusu"/>).</summary>
public class AyRaporAnlikGoruntuEntity
{
    public int Id { get; set; }
    public int Yil { get; set; }
    public int Ay { get; set; }
    public int KuralSurumu { get; set; }
    public string Json { get; set; } = "";
    public DateTimeOffset Zaman { get; set; }
}

public partial class KasaDbContext
{
    public DbSet<AyRaporAnlikGoruntuEntity> AyRaporAnlikGoruntuleri => Set<AyRaporAnlikGoruntuEntity>();

    partial void ConfigureAyRaporAnlikGoruntusu(ModelBuilder b)
    {
        b.Entity<AyRaporAnlikGoruntuEntity>().ToTable("AyRaporAnlikGoruntuleri");
        b.Entity<AyRaporAnlikGoruntuEntity>().HasIndex(g => new { g.Yil, g.Ay }).IsUnique();
    }
}

/// <summary>
/// Kilitli ay rapor anlık görüntüsü (kullanıcı kararı K4: rapor kuralı değişiklikleri kapatılmış ayların raporunu
/// DEĞİŞTİRMEZ). Kilit kuralları kapatılmış ayın verisini zaten korur; görüntü, raporu üreten KODUN (kuralın) sonradan
/// değişmesine karşı korur.
/// - Ay kapatılırken, yeni kilitlenen her ayın (araya giren aylar dahil) aylık raporu o anda açık aylara uygulanan kuralla
///   (<see cref="HesapServisi.AcikAyKurali"/>) hesaplanır ve aynı transaction'da saklanır; kapatılan ay, kapatılmadan hemen
///   önce gösterilen raporu birebir taşır.
/// - Kilit açılınca açılan ayların görüntüsü silinir; rapor yeniden canlı hesaplanır.
/// - Kilitli ayın raporu görüntüden döner (<c>"dondurulmus": true</c>, <c>"kuralSurumu"</c>); görüntünün sayı belirteçleri
///   saklandığı gibi yazılır.
/// - Geçiş tohumu (<see cref="GecisTohumu"/>): bu sürümden önce kapatılmış, görüntüsü olmayan her ay açılışta bir kez
///   <see cref="AylikKural.V1"/> ile dondurulur: geçiş anında kilitli aylar bugünkü rakamlarıyla kalır. Kilit ile görüntü
///   aynı transaction'da yazıldığından, görüntüsü olmayan kilitli ay yalnız bu sürümden önce kilitlenmiş olabilir.
/// - Bozuk kayıtla hesaplanan rapor dondurulmaz (gap-veri-degismezleri-patlama-yaricapi-3): raporuna karantina kaydı giren ay
///   kapatılamaz (409) ve geçiş tohumunda atlanır; bu yüzden görüntüler karantina uyarısı taşımaz.
/// Takip başlangıcının ayından önceki aylar görüntülenmez: kilitlenemezler ve dönem içermedikleri için iki kuralda aynıdır.
/// Değiştirilemezlik: kilit kuralları (<see cref="AyKilidiKurallari"/>) ve veritabanı tetikleyicileri görüntünün
/// güncellenmesini, kilitli ayın görüntüsünün silinmesini ve kilitli olmayan ay için görüntü yazılmasını reddeder.
/// </summary>
public static class AyRaporAnlikGoruntusu
{
    /// <summary>API'nin yanıt biçimi (web varsayılanları: camelCase, sayılar olduğu gibi).</summary>
    private static readonly JsonSerializerOptions JsonSecenekleri = new(JsonSerializerDefaults.Web);

    /// <summary>Takip başlangıcının ayından başlayarak ay sonu <paramref name="kilitSonu"/>'nu aşmayan aylar;
    /// <paramref name="oncekiKilitSonu"/> verilirse yalnız onun kapsamadığı (yeni kilitlenen) aylar.</summary>
    public static IEnumerable<(int Yil, int Ay)> KilitliAylar(DateOnly takipBaslangic, DateOnly? kilitSonu, DateOnly? oncekiKilitSonu = null)
    {
        if (kilitSonu is not { } son)
            yield break;
        for (var ay = new DateOnly(takipBaslangic.Year, takipBaslangic.Month, 1); AySonu(ay.Year, ay.Month) <= son; ay = ay.AddMonths(1))
            if (oncekiKilitSonu is not { } onceki || AySonu(ay.Year, ay.Month) > onceki)
                yield return (ay.Year, ay.Month);
    }

    public static DateOnly AySonu(int yil, int ay) => new(yil, ay, DateTime.DaysInMonth(yil, ay));

    /// <summary>Kilitli ayın dondurulmuş raporu; görüntü yoksa ya da ay kilitli değilse null (rapor canlı hesaplanır). Görüntü
    /// yalnız ay kilit sonuna kadarsa döner: görüntüyü silmeyen bir önceki sürüme dönülüp ay orada açılırsa satır artık kalır;
    /// açık ay için bayat "dondurulmus" rapor sunulmaz, artık satır ay başına bir kez Warning olarak loglanır. Artık satır ay
    /// yeniden kapatılmadan önce (ay açıkken tetikleyici silmeye izin verir) silinmelidir: kapatma aynı ay için yeni görüntü yazar.</summary>
    public static JsonObject? Oku(KasaDbContext db, int yil, int ay)
    {
        var satir = db.AyRaporAnlikGoruntuleri.AsNoTracking().Where(g => g.Yil == yil && g.Ay == ay)
            .Select(g => new { g.Json, g.KuralSurumu }).SingleOrDefault();
        if (satir is null)
            return null;
        var kilit = db.AyKilidi.AsNoTracking().Select(k => k.KilitliSonTarih).SingleOrDefault();
        if (kilit is not { } son || AySonu(yil, ay) > son)
        {
            VeriKarantinasi.Logla(db, string.Create(CultureInfo.InvariantCulture, $"AyRaporGoruntusu:{yil:D4}-{ay:D2}"), string.Create(CultureInfo.InvariantCulture,
                $"{yil:D4}-{ay:D2} ayının rapor görüntüsü var ama ay kilitli değil (kilit sonu: {(kilit is { } k ? k.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "yok")}); rapor canlı hesaplandı. Satır görüntüyü silmeyen bir önceki sürümde açılan aydan kalmış olabilir; ay yeniden kapatılmadan önce silinmelidir."));
            return null;
        }
        var rapor = JsonNode.Parse(satir.Json)!.AsObject();
        rapor["kuralSurumu"] = satir.KuralSurumu;
        rapor["dondurulmus"] = true;
        return rapor;
    }

    /// <summary>Ay kapatılırken (kilit durumu aynı transaction'da kaydedildikten sonra): yeni kilitlenen ayların raporu
    /// açık ay kuralıyla hesaplanıp eklenir. Çağıran SaveChanges yapar. Bozuk kayıtla hesaplanan rapor dondurulmaz: yeni kilitlenen
    /// bir aya dokunan karantina kaydı varsa (bkz. <see cref="HesapServisi"/>) kapatma 409 ile reddedilir ve ileti ayı ve kaydı
    /// kimliğiyle söyler; transaction geri alınır, kilit ve görüntü yazılmaz. Yaklaşık rakam (genel kasaya kaymış gelir, sayılmamış
    /// kredi, "Dağılım bekliyor" gider) değişmez görüntüye girmez; kilitli aydaki kayıt ise kilit açılmadan düzeltilemezdi.</summary>
    internal static void Kilitlendi(KasaDbContext db, DateOnly? oncekiKilitSonu, DateOnly yeniKilitSonu, DateTimeOffset zaman, CancellationToken ct = default)
    {
        var baslangic = db.Ayarlar.AsNoTracking().Select(a => a.TakipBaslangic).First();
        var hesap = new HesapServisi(db);
        foreach (var (yil, ay) in KilitliAylar(baslangic, yeniKilitSonu, oncekiKilitSonu).ToList())
        {
            var (rapor, karantina) = hesap.AylikVeKarantina(yil, ay, ct, HesapServisi.AcikAyKurali);
            if (karantina.Count > 0)
                AylikGiderEndpoints.Need(false, string.Create(CultureInfo.InvariantCulture,
                    $"{yil:D4}-{ay:D2} ayı kapatılamaz: raporuna giren {karantina.Count} kayıt karantinada ({HesapServisi.KarantinaListesi(karantina)}). Bozuk kayıtla hesaplanan rapor dondurulmaz; önce kaydı düzeltin, sonra ayı kapatın."),
                    StatusCodes.Status409Conflict);
            Ekle(db, yil, ay, HesapServisi.AcikAyKurali, rapor, zaman);
        }
    }

    /// <summary>Kilit açılırken (yeni kilit sonu kaydedildikten sonra): artık kilitli olmayan ayların görüntüsü silinir.
    /// Çağıran SaveChanges yapar.</summary>
    internal static void KilidiAcildi(KasaDbContext db, DateOnly? yeniKilitSonu)
    {
        var silinecek = db.AyRaporAnlikGoruntuleri.AsEnumerable()
            .Where(g => yeniKilitSonu is not { } son || AySonu(g.Yil, g.Ay) > son).ToList();
        db.AyRaporAnlikGoruntuleri.RemoveRange(silinecek);
    }

    /// <summary>
    /// Geçiş tohumu (açılışta, migration'dan sonra; idempotent): kilitli olup görüntüsü olmayan her ay kural 1 ile
    /// dondurulur. Dondurulan ayları döner. Tek transaction: yarıda kalırsa hiçbiri yazılmaz, sonraki açılış yeniden dener.
    /// Bozuk kayıtla hesaplanan rapor dondurulmaz (<see cref="Kilitlendi"/> ile aynı kural): raporuna karantina kaydı giren ay
    /// atlanır ve ayı ve kayıtları söyleyen Warning olarak loglanır; açılış durmaz. Ay kilitli ve görüntüsüz kalır: raporu kural 1
    /// ile canlı hesaplanır ve karantina uyarısını taşır (<see cref="TohumBekliyor"/>). Ay bekleyen tohumda kalır: her açılış
    /// yeniden dener, bu yüzden göç öncesi yedek de her açılışta alınır (veritabanı değişmediyse aynı yedek yeniden kullanılır).
    /// Kilitli aydaki kaydı düzeltmek için ay gerekçeyle açılır; yeniden kapatılınca güncel kuralla dondurulur.
    /// </summary>
    public static IReadOnlyList<(int Yil, int Ay)> GecisTohumu(KasaDbContext db, DateTimeOffset zaman)
    {
        using var transaction = db.Database.BeginTransaction();
        var eksik = EksikAylar(db);
        if (eksik.Count == 0)
            return eksik;
        var hesap = new HesapServisi(db);
        var donan = new List<(int Yil, int Ay)>();
        foreach (var (yil, ay) in eksik)
        {
            var (rapor, karantina) = hesap.AylikVeKarantina(yil, ay, CancellationToken.None, AylikKural.V1);
            if (karantina.Count > 0)
            {
                db.GetService<ILoggerFactory>().CreateLogger(VeriKarantinasi.LogKategorisi).LogWarning(
                    "{Ay} ayının raporu geçiş tohumunda dondurulmadı: raporuna giren {Sayi} kayıt karantinada ({Kayitlar}). Ay kilitli kalır; raporu kural 1 ile canlı hesaplanır ve veri sağlığı uyarısı taşır. Tohum her açılışta yeniden dener; kilitli aydaki kaydı düzeltmek için ayı gerekçeyle açın.",
                    string.Create(CultureInfo.InvariantCulture, $"{yil:D4}-{ay:D2}"), karantina.Count, HesapServisi.KarantinaListesi(karantina));
                continue;
            }
            Ekle(db, yil, ay, AylikKural.V1, rapor, zaman);
            donan.Add((yil, ay));
        }
        if (donan.Count == 0)
            return donan;
        db.SaveChanges();
        transaction.Commit();
        return donan;
    }

    /// <summary>Ay kilitli, takip başlangıcının ayından sonra ve görüntüsü yok mu: bu sürümden önce kilitlenmiş ve geçiş tohumunun
    /// karantina kaydı yüzünden dondurmadığı ay (yeni kilitte görüntü aynı transaction'da yazılır ya da kapatma reddedilir). Raporu
    /// kilitlendiği sürümün kuralıyla (kural 1) canlı hesaplanır.</summary>
    internal static bool TohumBekliyor(KasaDbContext db, int yil, int ay)
    {
        var kilit = db.AyKilidi.AsNoTracking().Select(k => k.KilitliSonTarih).SingleOrDefault();
        if (kilit is not { } son || AySonu(yil, ay) > son)
            return false;
        var baslangic = db.Ayarlar.AsNoTracking().Select(a => a.TakipBaslangic).First();
        return new DateOnly(yil, ay, 1) >= new DateOnly(baslangic.Year, baslangic.Month, 1)
            && !db.AyRaporAnlikGoruntuleri.AsNoTracking().Any(g => g.Yil == yil && g.Ay == ay);
    }

    /// <summary>Kilitli olup görüntüsü olmayan aylar (bekleyen geçiş tohumu).</summary>
    public static IReadOnlyList<(int Yil, int Ay)> EksikAylar(KasaDbContext db)
    {
        var baslangic = db.Ayarlar.AsNoTracking().Select(a => (DateOnly?)a.TakipBaslangic).FirstOrDefault();
        var kilit = db.AyKilidi.AsNoTracking().Select(k => k.KilitliSonTarih).SingleOrDefault();
        if (baslangic is not { } b)
            return [];
        var mevcut = db.AyRaporAnlikGoruntuleri.AsNoTracking().Select(g => new { g.Yil, g.Ay }).AsEnumerable().Select(g => (g.Yil, g.Ay)).ToHashSet();
        return KilitliAylar(b, kilit).Where(a => !mevcut.Contains(a)).ToList();
    }

    /// <summary>Göç öncesi yedek kararı için bekleyen geçiş tohumu, migration'dan önce ve şemadan bağımsız (ham SQL) okunur:
    /// görüntü tablosu henüz yoksa tohum migration'la birlikte bekler (migration zaten yedek gerektirir).</summary>
    internal static IReadOnlyList<string> BekleyenTohum(SqliteConnection connection)
    {
        string? Oku(string sql)
        {
            using var komut = connection.CreateCommand();
            komut.CommandText = sql;
            return komut.ExecuteScalar() is string s ? s : null;
        }
        bool Tablo(string ad)
        {
            using var komut = connection.CreateCommand();
            komut.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $ad;";
            komut.Parameters.AddWithValue("$ad", ad);
            return komut.ExecuteScalar() is not null;
        }
        if (!Tablo("AyRaporAnlikGoruntuleri") || !Tablo("AyKilidi") || !Tablo("Ayarlar"))
            return [];
        if (Oku("SELECT TakipBaslangic FROM Ayarlar ORDER BY Id LIMIT 1;") is not { } b || Oku("SELECT KilitliSonTarih FROM AyKilidi WHERE Id = 1;") is not { } k)
            return [];
        var mevcut = new HashSet<(int, int)>();
        using (var komut = connection.CreateCommand())
        {
            komut.CommandText = "SELECT Yil, Ay FROM AyRaporAnlikGoruntuleri;";
            using var okuyucu = komut.ExecuteReader();
            while (okuyucu.Read())
                mevcut.Add((okuyucu.GetInt32(0), okuyucu.GetInt32(1)));
        }
        return KilitliAylar(DateOnly.Parse(b, CultureInfo.InvariantCulture), DateOnly.Parse(k, CultureInfo.InvariantCulture))
            .Where(a => !mevcut.Contains(a)).Select(a => $"Kilitli ay rapor görüntüsü (kural 1): {a.Yil:D4}-{a.Ay:D2}").ToList();
    }

    private static void Ekle(KasaDbContext db, int yil, int ay, int kural, AylikRapor rapor, DateTimeOffset zaman)
    {
        db.AyRaporAnlikGoruntuleri.Add(new AyRaporAnlikGoruntuEntity
        {
            Yil = yil,
            Ay = ay,
            KuralSurumu = kural,
            Json = JsonSerializer.Serialize(rapor, JsonSecenekleri),
            Zaman = zaman,
        });
    }
}
