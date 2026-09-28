using System.Globalization;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Kasa.Api;

/// <summary>
/// Kanal değişikliği kuralları (ops-1, ops-2, statement-6, gap-veri-degismezleri-patlama-yaricapi-6). Kanalın mali bağı kimliğidir
/// (KanalId): hesap motoru ve raporlar kanalı kimlikten adlandırır (CoreMapping, dağılım JSON'ları). Gider, gelir ve kredi
/// satırlarındaki Kanal metni o kimliğin görünen adının kopyasıdır (etiket): gider listesi ve filtresi onu gösterir, gelirin
/// (dönem, kanal adı) tekilliği onu kullanır.
/// - Etiket senkronu mali değişiklik değildir: kimliği, tutarı, tarihi ve tipi aynı kalıp yalnız Kanal metni kanalın güncel adına
///   eşitlenen gider/kredi satırı kaynak kurallarına (aylık gider ödemesi, ekstre) ve dönem kilidine takılmaz.
/// - Dönem kilidi: aylık rapor Ortak gideri güncel SIRALI AKTİF KANAL KÜMESİNE böler (HesapServisi kanalları Sira'ya göre okur,
///   HesapMotoru artık kuruşu ilk aktif kanallara verir). Kilitli ayın raporu kapanışta dondurulur (<see cref="AyRaporAnlikGoruntusu"/>)
///   ama görüntü ay açılınca silinir ve ay yeniden canlı hesaplanır; ay bazında saklanan bir kanal kümesi yoktur. Bu yüzden kilit
///   varken bu küme değişmez: aktif kanal eklenemez ya da silinemez, aktiflik değişmez, aktif kanalların sırası (eşit sıralılar dahil)
///   değişmez. Böylece kapatılmış ay sonradan açılınca raporu kapanıştakiyle aynı kalır. Kümeyi değiştirmeyenler serbesttir: ad,
///   pasif yeni kanal, aktif kanalların sırasını bozmayan sıra değişikliği, geçmişsiz pasif kanalı silme. Haftalık rapor kanal
///   satırlarını kimlik ve açılış devrinden hesaplar, aktiflik ve sırayı kullanmaz. Açılış devri takip başlangıcından itibaren her
///   haftanın kanal devrini değiştirir: kilitte değişmez (yeni kanal açılış devri 0 ile eklenir). Kilit varken aktif kanal
///   eklemek ve pasife almayı yalnız ileriye dönük uygulamak, ay kapatılırken saklanan ve geçmiş ayın hesabında kullanılan bir
///   Ortak kümesi (şema, göç ve hesap motoru değişikliği) ister; bu kurallar onu sağlamaz (ops-2'nin açık kalan kısmı).
/// - Kilitli dönemin gelir satırını veritabanı tetikleyicisi hiç değiştirmez: kilit varken yeniden adlandırılan kanalın o satırı
///   kapanıştaki etiketi taşır. Bu eski ad, satır kilitli kaldıkça başka kanala verilmez (aynı dönemde iki kanal aynı adla gelir
///   tutamaz); ay açılınca etiket, kanal eklenirken ya da yeniden adlandırılırken kendi kanalının adına çekilir.
/// </summary>
internal static class KanalKurallari
{
    internal const string GecmisIletisi = "Geçmişi (kaydı, aylık gider şablonu ya da ekstre dağılımı) veya açılış bakiyesi olan kanal silinemez. Kanalı pasifleştirebilirsiniz.";

    private static DateOnly? KilitSonu(KasaDbContext db) => db.AyKilidi.AsNoTracking().Select(k => k.KilitliSonTarih).SingleOrDefault();

    /// <summary>SaveChanges doğrulamasına girecek değişiklikler: etiket senkronu girdileri çıkarılır (bkz. sınıf özeti).</summary>
    internal static List<EntityEntry> EtiketSenkronuHaric(KasaDbContext db, List<EntityEntry> entries)
    {
        var adaylar = entries.Where(EtiketAdayi).ToList();
        if (adaylar.Count == 0) return entries;
        var kimlikler = adaylar.Select(e => (int)e.CurrentValues["KanalId"]!).ToHashSet();
        // Aynı SaveChanges'ta yeniden adlandırılan kanalın kaydedilecek adı izlenen varlıktadır; diğerleri veritabanından okunur.
        var adlar = db.ChangeTracker.Entries<KanalEntity>().Where(k => k.State != EntityState.Deleted && kimlikler.Contains(k.Entity.Id))
            .ToDictionary(k => k.Entity.Id, k => k.Entity.Ad);
        var eksik = kimlikler.Where(id => !adlar.ContainsKey(id)).ToList();
        if (eksik.Count > 0)
            foreach (var k in db.Kanallar.AsNoTracking().Where(k => eksik.Contains(k.Id)).Select(k => new { k.Id, k.Ad }).ToList()) adlar[k.Id] = k.Ad;
        return entries.Where(e => !(EtiketAdayi(e) && adlar.TryGetValue((int)e.CurrentValues["KanalId"]!, out var ad)
            && string.Equals(ad, (string?)e.CurrentValues["Kanal"], StringComparison.Ordinal))).ToList();
    }

    private static bool EtiketAdayi(EntityEntry e) => e.State == EntityState.Modified && (e.Entity is IslemEntity or KrediEntity)
        && e.CurrentValues["KanalId"] is int kanal && Equals(e.OriginalValues["KanalId"], kanal)
        && e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).SequenceEqual(["Kanal"]);

    /// <summary>Kilit varken (<paramref name="son"/>) bu SaveChanges'taki kanal değişiklikleri (<paramref name="kanallar"/>) kilitli
    /// dönemi etkiliyorsa ileti; etkilemiyorsa null (bkz. sınıf özeti).</summary>
    internal static string? KilitIhlali(KasaDbContext db, IReadOnlyList<EntityEntry> kanallar, DateOnly son)
    {
        static decimal Devir(PropertyValues v) => (decimal)v[nameof(KanalEntity.AcilisDevri)]!;
        if (kanallar.Any(e => e.State switch
            {
                EntityState.Added => Devir(e.CurrentValues) != 0,
                EntityState.Deleted => Devir(e.OriginalValues) != 0,
                EntityState.Modified => Devir(e.OriginalValues) != Devir(e.CurrentValues),
                _ => false,
            }))
            return $"{son:yyyy-MM-dd} tarihine kadar dönem kilitli. Kanal açılış devri takip başlangıcından itibaren bütün haftaların kanal devrini "
                + "değiştirdiği için kilit varken değiştirilemez; yeni kanalı açılış devri 0 ile ekleyin. Açılış devrini değiştirmek için kilidi "
                + "takip başlangıcı ayından gerekçeyle açın.";
        var satirDegisir = kanallar.Any(e => e.State is EntityState.Added or EntityState.Deleted || e.State == EntityState.Modified
            && (e.Property(nameof(KanalEntity.Aktif)).IsModified || e.Property(nameof(KanalEntity.Sira)).IsModified));
        if (!satirDegisir) return null;
        // Görüntüsü olmayan kilitli ay (açılıştaki geçiş tohumu bekliyor) kural 1 ile canlı hesaplanır; pasif kanal bile ona satır ekler.
        // Geçiş tohumu raporuna karantinadaki kayıt giren ayı dondurmaz ve açılışı durdurmaz (AyRaporAnlikGoruntusu.GecisTohumu):
        // böyle bir ay, kayıt düzeltilip ay yeniden kapatılana kadar görüntüsüz kalır; yeniden başlatmak onu dondurmaz.
        if (!KilitliRaporlarDondurulmus(db))
            return $"{son:yyyy-MM-dd} tarihine kadar dönem kilitli ve kilitli ayların bir kısmının raporu henüz dondurulmamış. Kanal eklemek, silmek, "
                + "aktifliğini ya da sırasını değiştirmek bu ayların raporunu (kanal satırları ve Ortak gider payı) değiştireceği için engellendi; "
                + "kanal adı değiştirilebilir. Uygulama açılırken kilitli ayların raporu dondurulur; raporuna karantinadaki (okunamayan) kayıt "
                + "giren ay ise dondurulmaz: o ayı gerekçeyle açıp kaydı düzeltin, sonra ayı yeniden kapatın.";
        var (once, sonra) = OrtakKumeleri(db, kanallar);
        if (once != sonra)
            return $"{son:yyyy-MM-dd} tarihine kadar dönem kilitli. Ortak giderler aylık raporda aktif kanallara sıralarına göre bölünür ve kapatılmış "
                + "bir ay yeniden açıldığında bu bölüşüm güncel kanallarla yeniden hesaplanır. Bu yüzden kilit varken aktif kanal eklenemez ya da "
                + "silinemez, kanal aktifleştirilemez ya da pasife alınamaz ve aktif kanalların sırası değiştirilemez. Kanal adı değiştirilebilir, "
                + "yeni kanal pasif olarak eklenebilir. Kanal kümesini değiştirmek için kilidi takip başlangıcı ayından gerekçeyle açın; değişiklik "
                + "bütün ayların Ortak payını yeniden hesaplar.";
        return null;
    }

    /// <summary>Ortak gideri bölen sıralı aktif kanal kümesi değişiklikten önce (veritabanı) ve sonra (izlenen değerler): Sira'ya
    /// göre sıralı gruplar. Eşit sıralı aktif kanalların kendi aralarındaki sırası veritabanına kaldığından bir grup sayılır; grup
    /// oluşması ya da dağılması da küme değişikliğidir. Yeni kanal kimliksiz olduğundan kendi anahtarıyla girer.</summary>
    private static (string Once, string Sonra) OrtakKumeleri(KasaDbContext db, IReadOnlyList<EntityEntry> kanallar)
    {
        static string Kimlik(int id) => id.ToString(CultureInfo.InvariantCulture);
        static string Kume(Dictionary<string, (bool Aktif, int Sira)> d) => string.Join("|", d.Where(k => k.Value.Aktif)
            .GroupBy(k => k.Value.Sira).OrderBy(g => g.Key).Select(g => string.Join(",", g.Select(k => k.Key).Order(StringComparer.Ordinal))));
        var once = db.Kanallar.AsNoTracking().Select(k => new { k.Id, k.Aktif, k.Sira }).ToList().ToDictionary(k => Kimlik(k.Id), k => (k.Aktif, k.Sira));
        var sonra = new Dictionary<string, (bool Aktif, int Sira)>(once);
        for (var i = 0; i < kanallar.Count; i++)
        {
            var e = kanallar[i];
            var anahtar = e.State == EntityState.Added ? $"yeni-{i}" : Kimlik((int)e.OriginalValues[nameof(KanalEntity.Id)]!);
            if (e.State == EntityState.Deleted) sonra.Remove(anahtar);
            else if (e.State is EntityState.Added or EntityState.Modified)
                sonra[anahtar] = ((bool)e.CurrentValues[nameof(KanalEntity.Aktif)]!, (int)e.CurrentValues[nameof(KanalEntity.Sira)]!);
        }
        return (Kume(once), Kume(sonra));
    }

    private static bool KilitliRaporlarDondurulmus(KasaDbContext db) =>
        db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='AyRaporAnlikGoruntuleri'").Single() != 0
        && AyRaporAnlikGoruntusu.EksikAylar(db).Count == 0;

    /// <summary>
    /// Kanal adı olarak <paramref name="ad"/> verilmeden (ekleme ya da yeniden adlandırma) önce çağrılır; çağıranın transaction'ı
    /// içinde. Başka bir kanalın gelir satırında eski etiket olarak kalan ad: açık dönemdeki satırın etiketi kendi kanalının güncel
    /// adına çekilir ve kaydedilir; kilitli dönemde kalan satır varsa (tetikleyici değiştirmez) ileti döner, yoksa null.
    /// </summary>
    internal static string? AdEngeli(KasaDbContext db, int? kanalId, string ad)
    {
        // Gelenler.Kanal NOCASE: karşılaştırma (dönem, kanal adı) tekilliğiyle aynı kuraldadır.
        var eskiEtiketler = db.Gelenler.Where(g => g.KanalId != null && g.KanalId != kanalId && g.Kanal == ad).ToList();
        if (eskiEtiketler.Count == 0) return null;
        var son = KilitSonu(db);
        var adlar = db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
        bool Kilitli(GelenEntity g) => son is { } s && g.DonemStart <= s;
        foreach (var g in eskiEtiketler.Where(g => !Kilitli(g))) g.Kanal = adlar[g.KanalId!.Value];
        db.SaveChanges();
        var sahipler = eskiEtiketler.Where(Kilitli).Select(g => $"'{adlar[g.KanalId!.Value]}'").Distinct().ToList();
        return sahipler.Count == 0 ? null
            : $"'{ad}' adı, kilitli aylardaki gelir kayıtlarında {string.Join(", ", sahipler)} kanalının önceki adı olarak duruyor. Kilitli gelir "
              + "kayıtları değişmediği ve aynı dönemde iki kanal aynı adla gelir tutamadığı için bu ad, o aylar kilitliyken başka kanala verilemez. "
              + "Başka bir ad seçin.";
    }

    /// <summary>Yeniden adlandırmada kimliği bu kanal olan satırların etiketi yeni ada eşitlenir; KanalId'ye dokunulmaz (kimliksiz
    /// satır bağlanmaz: aylık gider ve ekstreden gelen çok kanallı ya da genel kasa gideri kimliksizdir). Kilitli dönemin gelir
    /// satırı atlanır: veritabanı tetikleyicisi kilitli geliri hiç değiştirmez.</summary>
    internal static void EtiketleriGuncelle(KasaDbContext db, int kanalId, string ad)
    {
        foreach (var i in db.Islemler.Where(i => i.KanalId == kanalId && i.Kanal != ad)) i.Kanal = ad;
        foreach (var k in db.Krediler.Where(k => k.KanalId == kanalId && k.Kanal != ad)) k.Kanal = ad;
        var son = KilitSonu(db);
        // Gelenler.Kanal NOCASE: yalnız büyük/küçük harfi değişen ad SQL'de eşit sayılır; karşılaştırmayı EF (ordinal) yapar.
        foreach (var g in db.Gelenler.Where(g => g.KanalId == kanalId && (son == null || g.DonemStart > son))) g.Kanal = ad;
    }

    /// <summary>Kanal silinebilir mi: geçmişi (kimliğiyle ya da kimliksiz eski etiketle bağlı hareket, takip, alış payı, aylık gider
    /// şablonu ya da ekstre dağılımı) veya açılış devri varsa ileti; yoksa null. İleti pasife almayı önerir; ay kilidi varken aktif
    /// kanal pasife alınamadığı (Ortak kümesi) için bunu da söyler. Çok kanallı aylık gider ve ekstre giderinin kaydı kanal kimliği
    /// taşımaz, payları şablon revizyonunda ve ekstre kaydının dağılımındadır: bunlar da burada denetlenir; kaydetme kurallarındaki
    /// aynı denetim (AyKilidiKurallari, EkstreKaynakKurallari) yalnız savunmadır, iletileri kilit durumuna göre değişmez.</summary>
    internal static string? SilmeEngeli(KasaDbContext db, KanalEntity kanal)
    {
        var (id, ad) = (kanal.Id, kanal.Ad);
        var gecmisli = kanal.AcilisDevri != 0
            || db.Islemler.Any(i => i.KanalId == id || i.KanalId == null && i.Kanal == ad)
            || db.Gelenler.Any(g => g.KanalId == id || g.KanalId == null && g.Kanal == ad)
            || db.Krediler.Any(k => k.KanalId == id || k.KanalId == null && k.Kanal == ad)
            || db.HesapHareketler.Any(h => h.KanalId == id)
            || FinansTakipServisi.KanalKullaniliyor(db, id)
            || db.AlisDagilimlar.Any(d => d.KanalId == id)
            || db.AylikGiderRevizyonlar.AsNoTracking().Select(r => r.DagilimJson).AsEnumerable()
                .Any(j => FinansTakipServisi.Read<KanalPayYaz>(j).Any(p => p.KanalId == id))
            || db.EkstreKayitlar.AsNoTracking().Select(k => k.DagilimJson).AsEnumerable()
                .Any(j => FinansTakipServisi.Read<TakipKanalPayi>(j).Any(p => p.KanalId == id));
        if (!gecmisli) return null;
        return kanal.Aktif && KilitSonu(db) is not null
            ? GecmisIletisi + " Ancak ay kilidi varken aktif kanal pasife alınamaz: kapatılmış bir ay açıldığında Ortak gider payı değişirdi."
            : GecmisIletisi;
    }

    /// <summary>
    /// Geçmişsiz kanalı siler (çağıran <see cref="SilmeEngeli"/>'ni denetlemiş olmalı). Kasa alt sınırı kanalın uyarı ayarıdır, mali
    /// geçmiş değildir: geçmişsiz ve açılış devri 0 olan kanalın bakiyesi 0'dır ve alt sınırı kaldıran ayrı bir uç yoktur (409 kanalı
    /// kalıcı olarak silinemez kılardı). Bu yüzden alt sınır kanalla birlikte silinir; kısıt (FOREIGN KEY) ihlali yanıta düşmez.
    /// Alarmın gönderilmemiş bildirimi sonraki bildirim turunda iptal olur; olay anahtarı alt sınır kimliğine bağlıdır, sonra
    /// açılan kanalın alt sınırıyla karışmaz.
    /// </summary>
    internal static void Sil(KasaDbContext db, KanalEntity kanal)
    {
        db.KasaEsikleri.RemoveRange(db.KasaEsikleri.Where(x => x.KanalId == kanal.Id).ToList());
        db.Kanallar.Remove(kanal);
        db.SaveChanges();
    }
}
