using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Kasa.Api.Data;

/// <summary>Tamamlanmış bir ayın kanal kümesi (başlık). Yalnız <see cref="AyKanalKumesi"/> dondururken yazılır; hiç değişmez,
/// silinmez (veritabanı tetikleyicileri ve kaydetme kuralı).</summary>
public class AyKanalKumesiEntity
{
    public int Id { get; set; }
    public int Yil { get; set; }
    public int Ay { get; set; }
    /// <summary>Kümeyi yazan adım: <see cref="AyKanalKumesi.Gecis"/>, <see cref="AyKanalKumesi.KanalDegisikligi"/> ya da
    /// <see cref="AyKanalKumesi.AyKapanisi"/>.</summary>
    public string Kaynak { get; set; } = "";
    public DateTimeOffset Zaman { get; set; }
}

/// <summary>Kümenin bir kanalı: rapordaki sırası (<see cref="Sira"/>, 0'dan) ve o ayın Ortak gideri bölüşümüne girip girmediği
/// (<see cref="Aktif"/>, dondurulduğu andaki aktiflik).</summary>
public class AyKanalKumesiKanalEntity
{
    public int Id { get; set; }
    public int KumeId { get; set; }
    public int KanalId { get; set; }
    public int Sira { get; set; }
    public bool Aktif { get; set; }
}

public partial class KasaDbContext
{
    public DbSet<AyKanalKumesiEntity> AyKanalKumeleri => Set<AyKanalKumesiEntity>();
    public DbSet<AyKanalKumesiKanalEntity> AyKanalKumesiKanallari => Set<AyKanalKumesiKanalEntity>();

    partial void ConfigureMonthlyChannelSets(ModelBuilder b)
    {
        b.Entity<AyKanalKumesiEntity>().ToTable("AyKanalKumeleri");
        b.Entity<AyKanalKumesiEntity>().HasIndex(k => new { k.Yil, k.Ay }).IsUnique();
        b.Entity<AyKanalKumesiKanalEntity>().ToTable("AyKanalKumesiKanallari");
        b.Entity<AyKanalKumesiKanalEntity>().HasIndex(u => new { u.KumeId, u.KanalId }).IsUnique();
        b.Entity<AyKanalKumesiKanalEntity>().HasOne<AyKanalKumesiEntity>().WithMany().HasForeignKey(u => u.KumeId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AyKanalKumesiKanalEntity>().HasOne<KanalEntity>().WithMany().HasForeignKey(u => u.KanalId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Ay bazında kanal kümesi (core-1, ops-2, gap-tarihsel-spec-ve-emekli-web-2). Aylık rapor Ortak gideri kanallara böler ve her kanal
/// için bir satır verir; kanal kaydı zamana bağlı değildir (Aktif ve Sira yalnız bugünü söyler). Bu yüzden tamamlanmış ayın kanal
/// kümesi — rapordaki kanallar, sıraları ve Ortak gideri bölüşen (o an aktif) kanallar — bir kez dondurulur ve o ayın raporu
/// bundan sonra onunla hesaplanır (<see cref="Servisler.HesapServisi"/>, <see cref="Core.HesapMotoru.AylikHesapla"/>):
/// - Ne zaman: Ortak kümesini değiştiren her kanal değişikliğinden (aktif kanal ekleme, aktiflik ya da sıra değişikliği, aktif
///   kanal silme) ÖNCE, aynı SaveChanges transaction'ında (<see cref="KanalDegisikligindenOnce"/>, LockedPeriodDbContext kancası);
///   ay kapatılırken (<see cref="AyKapanirken"/>); bu migration'ın uygulandığı açılışta bir kez (<see cref="GecisDondurmasi"/>).
///   Dondurulan her ay takip başlangıcının ayından bugünün (İstanbul) ayından önceki aya kadar kümesi olmayan aydır. Küme, değişiklik
///   kaydedilmeden önceki veritabanı durumundan, raporun kullandığı sırayla (<see cref="Kanallar"/>) yazılır. Değişiklikten önce
///   o ayın raporu zaten bu kümeyle hesaplanıyordu: dondurmak hiçbir raporu değiştirmez, yalnız sonraki değişikliklerin geçmişi
///   değiştirmesini engeller. Okuma yolu hiçbir şey yazmaz.
/// - Pasif kanal eklemek ya da geçmişsiz pasif kanalı silmek Ortak dağılımını ve hiçbir tutarı değiştirmez: tamamlanmış ayları
///   dondurmaz (kümesi olmayan tamamlanmış ayda eklenen kanal sıfır satır olarak görünür, silinenin sıfır satırı kalkar). Kilit
///   varken ise kilitli ayların eksik kümesi yine dondurulur: görüntüsü olmayan (geçiş tohumunun karantina yüzünden dondurmadığı)
///   kilitli ayın canlı raporunun satırları da değişmez.
/// - Kümesi olan ayda kümeden sonra açılan kanal, o ayda tutarı varsa (kilitli olmayan geçmiş aya girilen kayıt) satır olarak
///   sona eklenir; Ortak payı almaz. Tutarı yoksa satır vermez.
/// - Değişmezlik: küme ve üyeleri güncellenmez, silinmez; kümede yer alan kanal silinemez (tetikleyiciler, ON DELETE RESTRICT,
///   <see cref="AyKilidiKurallari"/>, <see cref="SilmeEngeli"/>). Ay kilidi açılınca küme silinmez: açılan ay yine kendi kümesiyle
///   hesaplanır, kapanıştaki raporla birebir aynı kalır.
/// - Kilit: kümeler sayesinde kilit varken aktif kanal eklemek, pasife almak ve sırasını değiştirmek serbesttir; yalnız açılış
///   devri (bütün haftaların kanal devri) kilitte değişmez (<see cref="KanalKurallari.KilitIhlali"/>).
/// Dondurma ham SQL'le yazılır: izleyiciye girmez, denetim olayı üretmez (türetilmiş veri; kanal değişikliğinin kendisi denetlenir).
/// </summary>
public static class AyKanalKumesi
{
    public const string MigrationId = "20261001000100_AyKanalKumeleri";
    public const string Gecis = "Gecis";
    public const string KanalDegisikligi = "KanalDegisikligi";
    public const string AyKapanisi = "AyKapanisi";

    /// <summary>Kümede yer alan kanalın silme iletisi.</summary>
    internal static string SilmeIletisi(bool aktif) =>
        "Bu kanal tamamlanmış bir ayın kanal kümesinde yer alıyor (o ayın Ortak gider dağılımı ve rapor satırları); silinirse o ayın raporu değişirdi. "
        + (aktif ? "Kanalı pasifleştirebilirsiniz: pasif kanal sonraki ayların Ortak giderinden pay almaz, tamamlanmış aylar değişmez."
                 : "Pasif kanal sonraki ayların Ortak giderinden pay almaz; listede kalması raporları etkilemez.");

    /// <summary>Dondurulmuş eski şema testleri ve köprü aşaması bu tablodan öncedir.</summary>
    internal static bool TabloVar(KasaDbContext db) =>
        db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='AyKanalKumeleri'").Single() != 0;

    /// <summary>Raporun kanal sırası (<see cref="Servisler.HesapServisi"/> ile aynı sorgu): küme bu sırayla dondurulur.</summary>
    internal static List<KanalEntity> Kanallar(KasaDbContext db) => db.Kanallar.AsNoTracking().OrderBy(k => k.Sira).ToList();

    /// <summary>Bütün kümeler: ay → üyeler (rapor sırasıyla). Üyesi olmayan küme de döner (o ay hiç kanal yoktu). Tablo yoksa boş.</summary>
    internal static Dictionary<(int Yil, int Ay), List<(int KanalId, bool Aktif)>> Oku(KasaDbContext db)
    {
        var sonuc = new Dictionary<(int Yil, int Ay), List<(int KanalId, bool Aktif)>>();
        if (!TabloVar(db))
            return sonuc;
        var aylar = db.AyKanalKumeleri.AsNoTracking().Select(k => new { k.Id, k.Yil, k.Ay }).ToList();
        var uyeler = db.AyKanalKumesiKanallari.AsNoTracking().OrderBy(u => u.KumeId).ThenBy(u => u.Sira)
            .Select(u => new { u.KumeId, u.KanalId, u.Aktif }).ToList().ToLookup(u => u.KumeId);
        foreach (var k in aylar)
            sonuc[(k.Yil, k.Ay)] = uyeler[k.Id].Select(u => (u.KanalId, u.Aktif)).ToList();
        return sonuc;
    }

    /// <summary>Takip başlangıcının ayından <paramref name="son"/>'un ayına (dahil) kadar kümesi olmayan aylar.</summary>
    internal static List<(int Yil, int Ay)> EksikAylar(KasaDbContext db, DateOnly takipBaslangic, DateOnly son)
    {
        var mevcut = db.AyKanalKumeleri.AsNoTracking().Select(k => new { k.Yil, k.Ay }).AsEnumerable().Select(k => (k.Yil, k.Ay)).ToHashSet();
        var eksik = new List<(int Yil, int Ay)>();
        for (var ay = new DateOnly(takipBaslangic.Year, takipBaslangic.Month, 1); ay <= son; ay = ay.AddMonths(1))
            if (!mevcut.Contains((ay.Year, ay.Month)))
                eksik.Add((ay.Year, ay.Month));
        return eksik;
    }

    /// <summary>Bugünün (İstanbul) ayından önceki son gün: tamamlanmış ayların sınırı.</summary>
    private static DateOnly TamamlanmisSonu(KasaDbContext db)
    {
        var bugun = db.Bugunu();
        return new DateOnly(bugun.Year, bugun.Month, 1).AddDays(-1);
    }

    private static DateOnly? TakipBaslangici(KasaDbContext db) => db.Ayarlar.AsNoTracking().Select(a => (DateOnly?)a.TakipBaslangic).FirstOrDefault();
    private static DateOnly? KilitSonu(KasaDbContext db) => db.AyKilidi.AsNoTracking().Select(k => k.KilitliSonTarih).SingleOrDefault();

    /// <summary>Verilen ayların kümesini veritabanındaki (kaydedilmemiş değişiklikleri görmeyen) kanallarla yazar. Çağıranın
    /// transaction'ında çalışır.</summary>
    internal static void Dondur(KasaDbContext db, IReadOnlyCollection<(int Yil, int Ay)> aylar, string kaynak, DateTimeOffset zaman)
    {
        if (aylar.Count == 0)
            return;
        var kanallar = Kanallar(db);
        foreach (var (yil, ay) in aylar)
        {
            db.Database.ExecuteSql($"INSERT INTO AyKanalKumeleri (Yil, Ay, Kaynak, Zaman) VALUES ({yil}, {ay}, {kaynak}, {zaman})");
            for (var sira = 0; sira < kanallar.Count; sira++)
                db.Database.ExecuteSql($"""
                    INSERT INTO AyKanalKumesiKanallari (KumeId, KanalId, Sira, Aktif)
                    SELECT Id, {kanallar[sira].Id}, {sira}, {kanallar[sira].Aktif} FROM AyKanalKumeleri WHERE Yil = {yil} AND Ay = {ay}
                    """);
        }
    }

    /// <summary>
    /// SaveChanges kancası (kilit kurallarından önce, aynı transaction'da): bu kayıttaki kanal değişiklikleri Ortak kümesini
    /// değiştiriyorsa (aktif kanal ekleme ya da silme, aktiflik ya da sıra değişikliği) kümesi olmayan bütün tamamlanmış aylar,
    /// yalnız rapor satırlarını değiştiriyorsa (pasif kanal ekleme ya da silme) ve kilit varsa kümesi olmayan kilitli aylar
    /// değişiklikten önceki kanallarla dondurulur (bkz. sınıf özeti).
    /// </summary>
    internal static void KanalDegisikligindenOnce(KasaDbContext db)
    {
        static bool Degisti(EntityEntry e, string alan) => !Equals(e.OriginalValues[alan], e.CurrentValues[alan]);
        var girdiler = db.ChangeTracker.Entries<KanalEntity>().Where(e => e.State is EntityState.Added or EntityState.Deleted or EntityState.Modified).ToList();
        if (girdiler.Count == 0)
            return;
        var ortakDegisir = girdiler.Any(e => e.State switch
        {
            EntityState.Added => e.Entity.Aktif,
            EntityState.Deleted => (bool)e.OriginalValues[nameof(KanalEntity.Aktif)]!,
            _ => Degisti(e, nameof(KanalEntity.Aktif)) || Degisti(e, nameof(KanalEntity.Sira)),
        });
        if (!ortakDegisir && !girdiler.Any(e => e.State is EntityState.Added or EntityState.Deleted))
            return;
        if (!TabloVar(db) || TakipBaslangici(db) is not { } baslangic)
            return;
        DateOnly? son = ortakDegisir ? TamamlanmisSonu(db) : KilitSonu(db);
        if (son is not { } s)
            return;
        Dondur(db, EksikAylar(db, baslangic, s), KanalDegisikligi, db.Saati().GetUtcNow());
    }

    /// <summary>Ay kapatılırken (kilit sınırı kaydedildikten sonra, aynı transaction'da): takip başlangıcından yeni kilit sınırına
    /// kadar kümesi olmayan aylar dondurulur. Kilidin açılması kümeleri silmez.</summary>
    internal static void AyKapanirken(KasaDbContext db, DateOnly kilitSonu, DateTimeOffset zaman)
    {
        if (!TabloVar(db) || TakipBaslangici(db) is not { } baslangic)
            return;
        Dondur(db, EksikAylar(db, baslangic, kilitSonu), AyKapanisi, zaman);
    }

    /// <summary>
    /// Göç veri adımı (<see cref="MigrationId"/> bu açılışta uygulandıysa, göç öncesi yedekten sonra): takip başlangıcından geçen aya
    /// kadar her tamamlanmış ay bugünkü kanallarla dondurulur. Geçişten önce bütün aylar bugünkü kanallarla hesaplanıyordu: geçmiş
    /// raporlar birebir aynı kalır. Tek transaction; yarıda kalırsa hiçbiri yazılmaz. Adım yalnız migration'ın uygulandığı açılışta
    /// çalışır; kaçırılırsa (ör. süreç arada durursa) sonuç değişmez: kancası her Ortak kümesi değişikliğinden önce aynı ayları aynı
    /// kanallarla dondurur. Dondurulan ayları döner.
    /// </summary>
    internal static IReadOnlyList<(int Yil, int Ay)> GecisDondurmasi(KasaDbContext db)
    {
        if (!TabloVar(db) || TakipBaslangici(db) is not { } baslangic)
            return [];
        using var transaction = db.Database.BeginTransaction();
        var aylar = EksikAylar(db, baslangic, TamamlanmisSonu(db));
        Dondur(db, aylar, Gecis, db.Saati().GetUtcNow());
        transaction.Commit();
        return aylar;
    }

    /// <summary>
    /// Kanal silinebilir mi (geçmiş denetiminden sonra, <see cref="KanalKurallari.SilmeEngeli"/>): kümede yer alan kanal; kümesi
    /// olmayan tamamlanmış ayların Ortak kümesindeki aktif kanal; kilit varken kümesi olmayan kilitli ayın satırındaki kanal silinemez
    /// (silme kümeyi değiştirirdi; kanca o ayları bu kanalla dondururdu). İleti ya da null.
    /// </summary>
    internal static string? SilmeEngeli(KasaDbContext db, KanalEntity kanal)
    {
        if (!TabloVar(db))
            return null;
        var engel = db.AyKanalKumesiKanallari.Any(u => u.KanalId == kanal.Id);
        if (!engel && TakipBaslangici(db) is { } baslangic)
            engel = kanal.Aktif && EksikAylar(db, baslangic, TamamlanmisSonu(db)).Count > 0
                || KilitSonu(db) is { } kilit && EksikAylar(db, baslangic, kilit).Count > 0;
        return engel ? SilmeIletisi(kanal.Aktif) : null;
    }

    /// <summary>Kaydetme kuralı (<see cref="AyKilidiKurallari"/>): küme yalnız dondurulurken (ham SQL) yazılır, izleyiciden hiç
    /// eklenmez, değişmez, silinmez; kümede yer alan kanal silinmez.</summary>
    internal static void Dogrula(KasaDbContext db, IReadOnlyList<EntityEntry> entries)
    {
        if (entries.Any(e => e.Entity is AyKanalKumesiEntity or AyKanalKumesiKanalEntity))
            throw new KilitliDonemException("Tamamlanmış ayın kanal kümesi değiştirilemez; yalnız kanal değişikliğinden önce ve ay kapatılırken otomatik yazılır.");
        var silinen = entries.Where(e => e.Entity is KanalEntity && e.State == EntityState.Deleted).Select(e => (KanalEntity)e.Entity).ToList();
        if (silinen.Count == 0 || !TabloVar(db))
            return;
        var kimlikler = silinen.Select(k => k.Id).ToList();
        var uye = db.AyKanalKumesiKanallari.AsNoTracking().Where(u => kimlikler.Contains(u.KanalId)).Select(u => u.KanalId).FirstOrDefault();
        if (uye != 0)
            throw new KilitliDonemException(SilmeIletisi(silinen.Single(k => k.Id == uye).Aktif));
    }

    internal static string AyMetni((int Yil, int Ay) ay) => string.Create(CultureInfo.InvariantCulture, $"{ay.Yil:D4}-{ay.Ay:D2}");
}
