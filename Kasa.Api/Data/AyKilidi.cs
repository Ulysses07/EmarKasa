using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
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
/// düşülür); bu yüzden o ay da denetlenir. Tüm ayları etkileyen ayarlar ayrı denetlenir:
/// takip başlangıcı (değişim noktasından sonra biten kilitli ay varsa), kasa açılış devri ve kanal
/// açılış devri (herhangi bir kilitli ay varsa) değiştirilemez. Kanal/cari/kalem adı değişimi
/// (yalnız etiket) ve kanalın aktif bayrağı serbesttir.
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

    /// <summary>Kilitli aylar (ay başı). Tablo yoksa (şema henüz güncellenmemiş) boş.</summary>
    public static List<DateOnly> KilitliAylar(KasaDbContext db)
    {
        try { return db.AyKilitleri.AsNoTracking().Select(k => k.Ay).ToList(); }
        catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)) { return []; }
    }

    /// <summary>
    /// Değişiklik izleyicideki yazmaları kilitlere karşı denetler; kilitli aya dokunan varsa
    /// <see cref="AyKilitliHatasi"/> fırlatır (hiçbir şey yazılmaz).
    /// </summary>
    public static void Denetle(KasaDbContext db)
    {
        var aylar = new HashSet<DateOnly>();
        DateOnly? takipDegisimi = null;
        string? tumAylariEtkileyen = null;
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
            }
        }
        if (aylar.Count == 0 && takipDegisimi is null && tumAylariEtkileyen is null) return;

        var kilitli = KilitliAylar(db);
        if (kilitli.Count == 0) return;
        if (takipDegisimi is { } t && kilitli.Where(a => AyBicimi.AySonu(a) >= t).ToList() is { Count: > 0 } etkilenen)
            throw new AyKilitliHatasi(
                $"Takip başlangıcı değişirse kilitli ayların ({AyBicimi.Liste(etkilenen)}) rakamları değişir. Önce bu ayların kilidini açın.");
        if (tumAylariEtkileyen is not null)
            throw new AyKilitliHatasi($"Kilitli ay varken ({AyBicimi.Liste(kilitli)}) {tumAylariEtkileyen}. Önce kilitleri açın.");
        var carpisan = aylar.Where(kilitli.Contains).ToList();
        if (carpisan.Count > 0)
            throw new AyKilitliHatasi(
                $"{AyBicimi.Liste(carpisan)} kilitli: bu aya ait kayıt eklenemez, değiştirilemez ya da silinemez. " +
                "Editör Aylık rapordan ayın kilidini açabilir.");
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
