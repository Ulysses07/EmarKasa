using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kasa.Api.Data;

/// <summary>
/// Bu tarih alanı kaydın ait olduğu ayı belirler: ay kilitliyse (<see cref="AyKilidiEntity"/>)
/// kayıt o aya eklenemez, o ayda değiştirilemez, o aydan silinemez ya da o aya/aydan taşınamaz.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AyKilidiAttribute : Attribute;

/// <summary>Kilitli aya dokunan yazma. API bunu anlaşılır bir 409'a çevirir.</summary>
public sealed class AyKilitliHatasi(string mesaj) : InvalidOperationException(mesaj);

/// <summary>Ay yardımcıları: ay başı ve Türkçe ad ("Eylül 2026").</summary>
public static class AyBicimi
{
    public static DateOnly AyBasi(DateOnly d) => new(d.Year, d.Month, 1);
    public static DateOnly AySonu(DateOnly ay) => AyBasi(ay).AddMonths(1).AddDays(-1);
    public static string Etiket(DateOnly ay) => $"{Metin.Tr.DateTimeFormat.GetMonthName(ay.Month)} {ay.Year}";
    public static string Etiket(int yil, int ay) => Etiket(new DateOnly(yil, ay, 1));
    public static string Liste(IEnumerable<DateOnly> aylar) => string.Join(", ", aylar.Distinct().Order().Select(Etiket));
}

/// <summary>
/// Ay kilidi kuralı. Kilit alanları <see cref="AyKilidiAttribute"/>'ten okunur (işlem, gelen, kart
/// ödemesi, çek, kasa sayımı). Ek kural: karta bağlı ya da kartsız K.K işlemi bir SONRAKİ ayın
/// sonucunu da değiştirir (aylık raporda önceki ayın K.K'sı, kasada kartsız K.K bir sonraki ayda
/// düşülür); bu yüzden o ay da denetlenir.
/// <para>Kilit geriye doğru kapsar: kasa aydan aya devrettiği için önceki bir aydaki düzeltme kilitli
/// ayın açılış ve kapanış kasasını değiştirir. Bu yüzden takip başlangıcından en son kilitli aya kadar
/// her ay kilitli sayılır (<see cref="EtkinKilitliAylar"/>); kilitleme uç noktası da aradaki ayları
/// kilitler, kilit açma sonraki ayların kilidini de açar.</para>
/// <para>Tüm ayları etkileyen ayarlar ayrı denetlenir: takip başlangıcı (değişim noktasından sonra
/// biten kilitli ay varsa), kasa açılış devri ve kanal açılış devri (herhangi bir kilitli ay varsa)
/// değiştirilemez. Kanal sırası, aktifliği, kanal ekleme/silme Ortak giderin kanallara bölünüşünü
/// değiştirebilir: kilitli ayların aylık raporu değişiklikten önce ve sonra hesaplanır, bir rakam
/// değişirse engellenir. Kanal/cari/kalem adı değişimi (yalnız etiket) serbesttir.</para>
/// </summary>
public static class AyKilidiKurali
{
    private static readonly ConcurrentDictionary<Type, string[]> Alanlar = new();

    /// <summary>Türün ay kilidi taşıyan tarih alanları.</summary>
    public static IReadOnlyList<string> KilitAlanlari(Type tip) => Alanlar.GetOrAdd(tip, t => t
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.IsDefined(typeof(AyKilidiAttribute)) && (p.PropertyType == typeof(DateOnly) || p.PropertyType == typeof(DateOnly?)))
        .Select(p => p.Name).ToArray());

    /// <summary>
    /// Kaydın (bir halinin) etkilediği aylar. <paramref name="deger"/> alan adına (C# adı) değer verir.
    /// </summary>
    public static IEnumerable<DateOnly> EtkiledigiAylar(Type tip, Func<string, object?> deger)
    {
        foreach (var alan in KilitAlanlari(tip))
        {
            if (deger(alan) is not DateOnly d) continue;
            var ay = AyBicimi.AyBasi(d);
            yield return ay;
            if (tip == typeof(IslemEntity)
                && (deger(nameof(IslemEntity.KrediKartiId)) is not null || deger(nameof(IslemEntity.Tip)) is GiderTipi.KrediKarti))
                yield return ay.AddMonths(1);
        }
    }

    /// <summary>Geçmiş JSON'undan (camelCase, enum metin) <see cref="EtkiledigiAylar"/> için değer okuyucu.</summary>
    public static Func<string, object?> JsonOkuyucu(JsonElement kok) => alan =>
    {
        if (kok.ValueKind != JsonValueKind.Object) return null;
        if (!kok.TryGetProperty(JsonNamingPolicy.CamelCase.ConvertName(alan), out var v) || v.ValueKind == JsonValueKind.Null)
            return null;
        if (alan == nameof(IslemEntity.Tip))
            return v.ValueKind == JsonValueKind.String && Enum.TryParse<GiderTipi>(v.GetString(), out var t) ? t : null;
        if (v.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(v.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetDecimal(out var m) ? m : null;
        return v.ToString();
    };

    /// <summary>Kilit satırı olan aylar (ay başı). Tablo yoksa (şema henüz güncellenmemiş) boş.</summary>
    public static List<DateOnly> KilitliAylar(KasaDbContext db)
    {
        try { return db.AyKilitleri.AsNoTracking().Select(k => k.Ay).ToList(); }
        catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)) { return []; }
    }

    /// <summary>Takip başlangıcının ayı (ay başı).</summary>
    public static DateOnly TakipAyi(KasaDbContext db)
        => AyBicimi.AyBasi(db.Ayarlar.AsNoTracking().OrderBy(a => a.Id).Select(a => a.TakipBaslangic).First());

    /// <summary>
    /// Yazılamayan aylar: kilit satırı olan aylar ve takip başlangıcından en son kilitli aya kadar
    /// aradaki her ay (kasa devri önceki aydan gelir). Kilit uç noktası aradaki ayları zaten
    /// kilitler; bu, kural her durumda geçerli olsun diye yeniden hesaplanır.
    /// </summary>
    public static SortedSet<DateOnly> EtkinKilitliAylar(KasaDbContext db, IReadOnlyCollection<DateOnly>? kilitli = null)
    {
        kilitli ??= KilitliAylar(db);
        var sonuc = new SortedSet<DateOnly>(kilitli);
        if (sonuc.Count == 0) return sonuc;
        for (var a = TakipAyi(db); a < sonuc.Max; a = a.AddMonths(1)) sonuc.Add(a);
        return sonuc;
    }

    /// <summary>Ay (ayın herhangi bir günü) yazmaya kapalı mı (bkz. <see cref="EtkinKilitliAylar"/>).</summary>
    public static bool KilitliMi(KasaDbContext db, DateOnly ay) => EtkinKilitliAylar(db).Contains(AyBicimi.AyBasi(ay));

    /// <summary>
    /// Değişiklik izleyicideki yazmaları kilitlere karşı denetler; kilitli aya dokunan varsa
    /// <see cref="AyKilitliHatasi"/> fırlatır (hiçbir şey yazılmaz).
    /// </summary>
    public static void Denetle(KasaDbContext db)
    {
        var aylar = new HashSet<DateOnly>();
        DateOnly? takipDegisimi = null;
        string? tumAylariEtkileyen = null;
        bool kanalDagilimi = false;
        foreach (var e in db.ChangeTracker.Entries())
        {
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            var tip = e.Metadata.ClrType;
            if (KilitAlanlari(tip).Count > 0)
            {
                if (e.State != EntityState.Deleted)
                    aylar.UnionWith(EtkiledigiAylar(tip, a => e.Property(a).CurrentValue));
                if (e.State != EntityState.Added)
                    aylar.UnionWith(EtkiledigiAylar(tip, a => e.Property(a).OriginalValue));
            }
            if (e.Entity is AyarEntity && e.State == EntityState.Modified)
            {
                var tb = e.Property(nameof(AyarEntity.TakipBaslangic));
                if (tb.OriginalValue is DateOnly eski && tb.CurrentValue is DateOnly yeni && eski != yeni)
                    takipDegisimi = eski < yeni ? eski : yeni;
                var ka = e.Property(nameof(AyarEntity.KasaAcilisDevri));
                if (!Equals(ka.OriginalValue, ka.CurrentValue))
                    tumAylariEtkileyen = "kasa açılış devri değiştirilemez (tüm ayların kasasını değiştirir)";
            }
            if (e.Entity is KanalEntity k)
            {
                var ad = e.Property(nameof(KanalEntity.AcilisDevri));
                var degisti = e.State switch
                {
                    EntityState.Modified => !Equals(ad.OriginalValue, ad.CurrentValue),
                    EntityState.Added => k.AcilisDevri != 0m,
                    _ => ad.OriginalValue is decimal o && o != 0m,
                };
                if (degisti)
                    tumAylariEtkileyen = "kanal açılış devri değiştirilemez; açılış devri olan kanal eklenemez ya da silinemez (tüm ayların kanal devirlerini değiştirir)";
                // Ortak gider kanal sırasına (artık kuruşlar) ve aktif kanallara (o ay hareketli kanal yoksa) göre bölünür.
                if (e.State != EntityState.Modified
                    || !Equals(e.Property(nameof(KanalEntity.Sira)).OriginalValue, k.Sira)
                    || !Equals(e.Property(nameof(KanalEntity.Aktif)).OriginalValue, k.Aktif))
                    kanalDagilimi = true;
            }
        }
        if (aylar.Count == 0 && takipDegisimi is null && tumAylariEtkileyen is null && !kanalDagilimi) return;

        var kilitli = KilitliAylar(db);
        if (kilitli.Count == 0) return;
        var etkin = EtkinKilitliAylar(db, kilitli);
        if (takipDegisimi is { } t && etkin.Where(a => AyBicimi.AySonu(a) >= t).ToList() is { Count: > 0 } etkilenen)
            throw new AyKilitliHatasi(
                $"Takip başlangıcı değişirse kilitli ayların ({AyBicimi.Liste(etkilenen)}) rakamları değişir. Önce bu ayların kilidini açın.");
        if (tumAylariEtkileyen is not null)
            throw new AyKilitliHatasi($"Kilitli ay varken ({AyBicimi.Liste(etkin)}) {tumAylariEtkileyen}. Önce kilitleri açın.");
        var carpisan = aylar.Where(etkin.Contains).ToList();
        if (carpisan.Count > 0)
            throw new AyKilitliHatasi(
                $"{AyBicimi.Liste(carpisan)} kilitli: bu aya ait kayıt eklenemez, değiştirilemez ya da silinemez. " +
                "Editör Aylık rapordan ayın kilidini açabilir.");
        if (kanalDagilimi && OrtakDagilimiDegisenAylar(db, etkin) is { Count: > 0 } dagilim)
            throw new AyKilitliHatasi(
                $"Bu kanal değişikliği kilitli ayların ({AyBicimi.Liste(dagilim)}) Ortak gider paylarını değiştirir " +
                "(Ortak gider kanal sırasına ve aktif kanallara göre bölünür). Önce bu ayların kilidini açın.");
    }

    /// <summary>
    /// Kanal değişikliğinden (sıra, aktiflik, ekleme, silme) önceki ve sonraki kanal listesiyle kilitli
    /// ayların aylık raporunu hesaplar; kanal satırlarından biri değişen aylar döner. Ad değişimi
    /// kayıtlara da taşındığından (işlem, gelen, çek adları zaten yeni) iki listede de yeni ad kullanılır.
    /// </summary>
    private static List<DateOnly> OrtakDagilimiDegisenAylar(KasaDbContext db, IEnumerable<DateOnly> kilitliAylar)
    {
        var izlenen = db.ChangeTracker.Entries<KanalEntity>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        var degisen = izlenen.Where(x => x.State != EntityState.Added).ToDictionary(x => x.Entity.Id);
        var eklenen = izlenen.Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToList();
        var dbdeki = db.Kanallar.AsNoTracking().ToList();

        // HesapServisi'nin sırası: Sira, sonra Id (yeni kanal en büyük Id'yi alır).
        static IReadOnlyList<Kanal> Liste(IEnumerable<(int Id, string Ad, decimal Devir, bool Aktif, int Sira)> l)
            => l.OrderBy(x => x.Sira).ThenBy(x => x.Id).Select(x => new Kanal(x.Ad, x.Devir, x.Aktif, x.Sira)).ToList();
        var once = Liste(dbdeki.Select(k => (k.Id, degisen.TryGetValue(k.Id, out var x) ? x.Entity.Ad : k.Ad, k.AcilisDevri, k.Aktif, k.Sira)));
        var sonra = Liste(dbdeki
            .Where(k => !(degisen.TryGetValue(k.Id, out var x) && x.State == EntityState.Deleted))
            .Select(k => degisen.TryGetValue(k.Id, out var x) ? (k.Id, x.Entity.Ad, x.Entity.AcilisDevri, x.Entity.Aktif, x.Entity.Sira) : (k.Id, k.Ad, k.AcilisDevri, k.Aktif, k.Sira))
            .Concat(eklenen.Select(k => (int.MaxValue, k.Ad, k.AcilisDevri, k.Aktif, k.Sira))));

        var hesap = new HesapServisi(db, db.SaatSaglayici);
        var takipAyi = TakipAyi(db);
        var sonuc = new List<DateOnly>();
        foreach (var ay in kilitliAylar.Where(a => a >= takipAyi))
        {
            if (!AyniKanalSatirlari(hesap.Aylik(ay.Year, ay.Month, once).Kanallar, hesap.Aylik(ay.Year, ay.Month, sonra).Kanallar))
                sonuc.Add(ay);
        }
        return sonuc;
    }

    /// <summary>Kanal satırları ada göre aynı mı (satırı olmayan kanal sıfır sayılır).</summary>
    private static bool AyniKanalSatirlari(IReadOnlyList<KanalAylik> a, IReadOnlyList<KanalAylik> b)
    {
        static (decimal, decimal, decimal, decimal, decimal, decimal, decimal, decimal) Deger(KanalAylik? k)
            => k is null ? default : (k.Gelen, k.CariGiden, k.SabitGider, k.KrediKarti, k.OrtakPay, k.AySonucu, k.CekGelen, k.CekGiden);
        return a.Select(k => k.Kanal).Concat(b.Select(k => k.Kanal)).Distinct()
            .All(ad => Deger(a.FirstOrDefault(k => k.Kanal == ad)) == Deger(b.FirstOrDefault(k => k.Kanal == ad)));
    }
}

/// <summary>
/// Ay kilidini her SaveChanges'te merkezî olarak uygular (ExecuteUpdate/ExecuteDelete değişiklik
/// izleyiciyi atlar; bu yolla yalnız ad değişimi yapılır, rakamlar değişmez).
/// </summary>
public sealed class AyKilidiDenetcisi : SaveChangesInterceptor
{
    public static readonly AyKilidiDenetcisi Ornek = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is KasaDbContext db) AyKilidiKurali.Denetle(db);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is KasaDbContext db) AyKilidiKurali.Denetle(db);
        return ValueTask.FromResult(result);
    }
}
