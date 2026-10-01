using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Kasa.Api;

/// <summary>
/// Kanal değişikliği kuralları (ops-1, ops-2, statement-6, gap-veri-degismezleri-patlama-yaricapi-6). Kanalın mali bağı kimliğidir
/// (KanalId): hesap motoru ve raporlar kanalı kimlikten adlandırır (CekirdekEslemesi, dağılım JSON'ları). Gider, gelir ve kredi
/// satırlarındaki Kanal metni o kimliğin görünen adının kopyasıdır (etiket): gider listesi ve filtresi onu gösterir, gelirin
/// (dönem, kanal adı) tekilliği onu kullanır.
/// - Etiket senkronu mali değişiklik değildir: kimliği, tutarı, tarihi ve tipi aynı kalıp yalnız Kanal metni kanalın güncel adına
///   eşitlenen gider/kredi satırı kaynak kurallarına (aylık gider ödemesi, ekstre) ve dönem kilidine takılmaz.
/// - Dönem kilidi ve Ortak kümesi: aylık rapor Ortak gideri kanallara böler ve kanal başına satır verir; tamamlanmış ayın kanal
///   kümesi (satırlar, sıraları, Ortak'ı bölüşen kanallar) Ortak kümesini değiştiren her kanal değişikliğinden önce ve ay
///   kapatılırken dondurulur (<see cref="AyKanalKumesi"/>). Bu yüzden kanal eklemek, pasife almak, aktifleştirmek ve sırasını
///   değiştirmek yalnız açık ayları etkiler ve kilit varken de serbesttir: kilitli ayın raporu (dondurulmuş görüntüsü, görüntüsü
///   yoksa kendi kümesiyle canlı hesabı) ve ay açılınca gösterilecek raporu değişmez. Haftalık rapor kanal satırlarını kimlik ve
///   açılış devrinden hesaplar, aktiflik ve sırayı kullanmaz. Açılış devri takip başlangıcından itibaren her haftanın kanal devrini
///   değiştirir: kilitte değişmez (yeni kanal açılış devri 0 ile eklenir). Tamamlanmış bir ayın kümesinde yer alan kanal silinemez
///   (pasifleştirilebilir).
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
        if (adaylar.Count == 0)
            return entries;
        var kimlikler = adaylar.Select(e => (int)e.CurrentValues["KanalId"]!).ToHashSet();
        // Aynı SaveChanges'ta yeniden adlandırılan kanalın kaydedilecek adı izlenen varlıktadır; diğerleri veritabanından okunur.
        var adlar = db.ChangeTracker.Entries<KanalEntity>().Where(k => k.State != EntityState.Deleted && kimlikler.Contains(k.Entity.Id))
            .ToDictionary(k => k.Entity.Id, k => k.Entity.Ad);
        var eksik = kimlikler.Where(id => !adlar.ContainsKey(id)).ToList();
        if (eksik.Count > 0)
            foreach (var k in db.Kanallar.AsNoTracking().Where(k => eksik.Contains(k.Id)).Select(k => new { k.Id, k.Ad }).ToList())
                adlar[k.Id] = k.Ad;
        return entries.Where(e => !(EtiketAdayi(e) && adlar.TryGetValue((int)e.CurrentValues["KanalId"]!, out var ad)
            && string.Equals(ad, (string?)e.CurrentValues["Kanal"], StringComparison.Ordinal))).ToList();
    }

    private static bool EtiketAdayi(EntityEntry e) => e.State == EntityState.Modified && (e.Entity is IslemEntity or KrediEntity)
        && e.CurrentValues["KanalId"] is int kanal && Equals(e.OriginalValues["KanalId"], kanal)
        && e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).SequenceEqual(["Kanal"]);

    /// <summary>Kilit varken (<paramref name="son"/>) bu SaveChanges'taki kanal değişiklikleri (<paramref name="kanallar"/>) kilitli
    /// dönemi etkiliyorsa ileti; etkilemiyorsa null. Yalnız açılış devri kilitli dönemi etkiler: kanal kümesi değişiklikten önce
    /// dondurulmuştur (bkz. sınıf özeti).</summary>
    internal static string? KilitIhlali(IReadOnlyList<EntityEntry> kanallar, DateOnly son)
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
        return null;
    }

    /// <summary>
    /// Kanal adı olarak <paramref name="ad"/> verilmeden (ekleme ya da yeniden adlandırma) önce çağrılır; çağıranın transaction'ı
    /// içinde. Başka bir kanalın gelir satırında eski etiket olarak kalan ad: açık dönemdeki satırın etiketi kendi kanalının güncel
    /// adına çekilir ve kaydedilir; kilitli dönemde kalan satır varsa (tetikleyici değiştirmez) ileti döner, yoksa null.
    /// </summary>
    internal static string? AdEngeli(KasaDbContext db, int? kanalId, string ad)
    {
        // Gelenler.Kanal NOCASE: karşılaştırma (dönem, kanal adı) tekilliğiyle aynı kuraldadır.
        var eskiEtiketler = db.Gelenler.Where(g => g.KanalId != null && g.KanalId != kanalId && g.Kanal == ad).ToList();
        if (eskiEtiketler.Count == 0)
            return null;
        var son = KilitSonu(db);
        var adlar = db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
        bool Kilitli(GelenEntity g) => son is { } s && g.DonemStart <= s;
        foreach (var g in eskiEtiketler.Where(g => !Kilitli(g)))
            g.Kanal = adlar[g.KanalId!.Value];
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
        foreach (var i in db.Islemler.Where(i => i.KanalId == kanalId && i.Kanal != ad))
            i.Kanal = ad;
        foreach (var k in db.Krediler.Where(k => k.KanalId == kanalId && k.Kanal != ad))
            k.Kanal = ad;
        var son = KilitSonu(db);
        // Gelenler.Kanal NOCASE: yalnız büyük/küçük harfi değişen ad SQL'de eşit sayılır; karşılaştırmayı EF (ordinal) yapar.
        foreach (var g in db.Gelenler.Where(g => g.KanalId == kanalId && (son == null || g.DonemStart > son)))
            g.Kanal = ad;
    }

    /// <summary>Kanal silinebilir mi: geçmişi (kimliğiyle ya da kimliksiz eski etiketle bağlı hareket, takip, alış payı, aylık gider
    /// şablonu, ekstre dağılımı, çek ya da çek hareketi) veya açılış devri varsa ileti; geçmişsizse ve tamamlanmış bir ayın kanal kümesinde yer alıyorsa
    /// (<see cref="AyKanalKumesi.SilmeEngeli"/>) o ileti; yoksa null. İleti pasife almayı önerir (kilit varken de serbesttir). Çok
    /// kanallı aylık gider ve ekstre giderinin kaydı kanal kimliği taşımaz, payları şablon revizyonunda ve ekstre kaydının
    /// dağılımındadır: bunlar da burada denetlenir; kaydetme kurallarındaki aynı denetim (AyKilidiKurallari, EkstreKaynakKurallari,
    /// AyKanalKumesi) yalnız savunmadır.</summary>
    internal static string? SilmeEngeli(KasaDbContext db, KanalEntity kanal)
    {
        var (id, ad) = (kanal.Id, kanal.Ad);
        var gecmisli = kanal.AcilisDevri != 0
            || db.Islemler.Any(i => i.KanalId == id || i.KanalId == null && i.Kanal == ad)
            || db.Gelenler.Any(g => g.KanalId == id || g.KanalId == null && g.Kanal == ad)
            || db.Krediler.Any(k => k.KanalId == id || k.KanalId == null && k.Kanal == ad)
            || db.HesapHareketler.Any(h => h.KanalId == id)
            || db.Cekler.Any(c => c.KanalId == id) || db.CekHareketler.Any(h => h.KanalId == id)
            || FinansTakipServisi.KanalKullaniliyor(db, id)
            || db.AlisDagilimlar.Any(d => d.KanalId == id)
            || db.AylikGiderRevizyonlar.AsNoTracking().Select(r => r.DagilimJson).AsEnumerable()
                .Any(j => FinansTakipServisi.Read<KanalPayYaz>(j).Any(p => p.KanalId == id))
            || db.EkstreKayitlar.AsNoTracking().Select(k => k.DagilimJson).AsEnumerable()
                .Any(j => FinansTakipServisi.Read<TakipKanalPayi>(j).Any(p => p.KanalId == id));
        return gecmisli ? GecmisIletisi : AyKanalKumesi.SilmeEngeli(db, kanal);
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
