using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

/// <summary>
/// Güncellemeyi geri alma ("Önceki haline döndür") kuralları. Geri alma kaydı YENİ kayıt
/// üretmez: var olan kaydı geçmiş satırındaki eski değerlere döndürür; eski değerler normal
/// düzenlemedeki doğrulamalardan aynen geçer (uç: POST /api/gecmis/{id}/geri-al).
/// <list type="bullet">
/// <item>Yalnız işlem, çek, gelen tutarı, kanal açılış devri (ad değişmeden) ve kasa açılış devri
/// (takip başlangıcı değişmeden) güncellemeleri geri alınabilir.</item>
/// <item>Kayıt, satırın "yeni" haliyle birebir aynı olmalı: sonradan yeniden değiştiyse ya da
/// silindiyse 409 döner (önce sonraki değişiklik geri alınmalı).</item>
/// <item>Takip başlangıcı değişince birleşen gelenler ve toplu (ad değişimi) satırları geri alınamaz:
/// aynı parayı iki kez saydırırdı.</item>
/// </list>
/// </summary>
public static class GuncellemeGeriAlma
{
    /// <summary>Güncellemesi geri alınabilen türler.</summary>
    public static readonly IReadOnlySet<string> Turler = new HashSet<string>
    {
        GecmisTurleri.Islem, GecmisTurleri.Cek, GecmisTurleri.Gelen, GecmisTurleri.Kanal, GecmisTurleri.Ayar,
    };

    /// <summary>Takip birleştirme satırıyla aynı anda yazılmış gelen güncellemesi sayılacak en büyük zaman farkı.</summary>
    private static readonly TimeSpan BirlesmePenceresi = TimeSpan.FromSeconds(5);

    /// <summary>Yalnız satıra bakarak (DB'siz) geri alınamama nedeni; alınabiliyorsa null.</summary>
    public static string? Engel(DegisiklikEntity d, DateTime simdiUtc)
    {
        if (!Turler.Contains(d.Tur)) return $"{d.Tur} güncellemeleri geri alınamaz.";
        if (string.IsNullOrEmpty(d.EskiJson) || string.IsNullOrEmpty(d.YeniJson) || d.KayitId is null)
            return "Kaydın eski hali yok; geri alınamaz.";
        if (simdiUtc - d.ZamanUtc > GecmisKurallari.GeriAlmaSuresi)
            return $"{GecmisKurallari.GeriAlmaSuresi.TotalDays:0} günden eski değişiklikler geri alınamaz.";
        JsonElement eski, yeni;
        try
        {
            using var e = JsonDocument.Parse(d.EskiJson);
            using var y = JsonDocument.Parse(d.YeniJson);
            eski = e.RootElement.Clone();
            yeni = y.RootElement.Clone();
        }
        catch (JsonException) { return "Kaydın eski hali okunamadı; geri alınamaz."; }
        // Toplu satırlar (ad değişimi, takip birleştirmesi) kaydın tam hali değildir.
        if (eski.ValueKind != JsonValueKind.Object || yeni.ValueKind != JsonValueKind.Object
            || !eski.TryGetProperty("id", out var eid) || eid.ValueKind != JsonValueKind.Number || eid.GetInt32() != d.KayitId)
            return "Toplu değişiklikler geri alınamaz.";
        if (!FarkVar(eski, yeni)) return "Bu değişiklikte geri alınacak bir alan yok.";
        switch (d.Tur)
        {
            case GecmisTurleri.Kanal when !AyniAlan(eski, yeni, "ad"):
                return "Kanal adı değişiklikleri geri alınamaz; adı Ayarlar'dan yeniden değiştirin.";
            case GecmisTurleri.Ayar when !AyniAlan(eski, yeni, "takipBaslangic"):
                return "Takip başlangıcı değişikliği geri alınamaz (gelenler dönemlere yeniden dağıtıldı).";
            case GecmisTurleri.Gelen when !AyniAlan(eski, yeni, "donemStart") || !AyniAlan(eski, yeni, "kanal"):
                return "Takip başlangıcı değişince taşınan gelen geri alınamaz.";
        }
        return null;
    }

    /// <summary>
    /// Kaydın bugünkü haline göre engel (409 metni): kayıt silinmiş, satırdan sonra yeniden
    /// değişmiş ya da takip birleştirmesinin parçası. Güncellendi dışındaki satırlarda null.
    /// </summary>
    public static string? DbEngeli(KasaDbContext db, DegisiklikEntity d)
    {
        if (d.Eylem != Eylemler.Guncellendi || d.KayitId is not int id || d.YeniJson is null) return null;
        var guncel = GuncelKayit(db, d.Tur, id, izle: false);
        if (guncel is null) return "Kayıt artık yok (silinmiş); önceki haline döndürülemez.";
        try
        {
            using var yeni = JsonDocument.Parse(d.YeniJson);
            using var simdi = JsonDocument.Parse(GecmisJson.Yaz(guncel));
            if (!Kapsar(simdi.RootElement, yeni.RootElement))
                return "Kayıt bu değişiklikten sonra yeniden değişti; önce daha yeni değişikliği geri alın.";
        }
        catch (JsonException) { return "Kaydın hali okunamadı; geri alınamaz."; }
        if (d.Tur == GecmisTurleri.Gelen)
        {
            var birlesmeler = db.Degisiklikler.AsNoTracking()
                .Where(x => x.Tur == GecmisTurleri.Gelen && x.KayitId == id && x.Id != d.Id && x.EskiJson != null && x.EskiJson.StartsWith("["))
                .Select(x => x.ZamanUtc).ToList();
            if (birlesmeler.Any(z => (z - d.ZamanUtc).Duration() <= BirlesmePenceresi))
                return "Bu gelen, takip başlangıcı değişince birleştirildi; önceki haline döndürmek aynı parayı iki kez saydırır.";
        }
        return null;
    }

    /// <summary>Türün kaydı (izlenerek ya da izlemesiz); tür desteklenmiyorsa ya da kayıt yoksa null.</summary>
    public static object? GuncelKayit(KasaDbContext db, string tur, int id, bool izle)
    {
        return tur switch
        {
            GecmisTurleri.Islem => Bul(db.Islemler, id, izle),
            GecmisTurleri.Cek => Bul(db.Cekler, id, izle),
            GecmisTurleri.Gelen => Bul(db.Gelenler, id, izle),
            GecmisTurleri.Kanal => Bul(db.Kanallar, id, izle),
            GecmisTurleri.Ayar => Bul(db.Ayarlar, id, izle),
            _ => null,
        };

        static T? Bul<T>(DbSet<T> set, int id, bool izle) where T : class
            => izle ? set.Find(id) : set.AsNoTracking().FirstOrDefault(x => EF.Property<int>(x, "Id") == id);
    }

    /// <summary>
    /// Güncel kaydın kopyası, geçmiş satırının eski değerleriyle: eski JSON'daki alanlar üzerine
    /// yazılır, JSON'da olmayan alanlar (gizli alanlar, sonradan eklenen sütunlar) bugünkü değerini korur.
    /// </summary>
    public static T EskiHal<T>(T guncel, string eskiJson) where T : class
    {
        var hedef = JsonNode.Parse(GecmisJson.Yaz(guncel))!.AsObject();
        var eski = JsonNode.Parse(eskiJson)!.AsObject();
        foreach (var (ad, deger) in eski)
            hedef[ad] = deger?.DeepClone();
        return hedef.Deserialize<T>(GecmisJson.Secenekler) ?? throw new JsonException("Kayıt boş.");
    }

    // ------------------------------------------------------------------ JSON karşılaştırma

    /// <summary><paramref name="beklenen"/>'deki her alan <paramref name="guncel"/>'de aynı değerde mi (sayılar sayısal).</summary>
    private static bool Kapsar(JsonElement guncel, JsonElement beklenen)
    {
        foreach (var p in beklenen.EnumerateObject())
        {
            if (!guncel.TryGetProperty(p.Name, out var g)) return false;
            if (!DegerEsit(g, p.Value)) return false;
        }
        return true;
    }

    private static bool FarkVar(JsonElement eski, JsonElement yeni)
        => eski.EnumerateObject().Any(p => p.Name != "id" && (!yeni.TryGetProperty(p.Name, out var y) || !DegerEsit(p.Value, y)));

    private static bool AyniAlan(JsonElement a, JsonElement b, string ad)
        => a.TryGetProperty(ad, out var x) && b.TryGetProperty(ad, out var y) && DegerEsit(x, y);

    private static bool DegerEsit(JsonElement a, JsonElement b)
    {
        if (a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number)
            return a.GetDecimal() == b.GetDecimal();
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False => true,
            _ => a.GetRawText() == b.GetRawText(),
        };
    }
}
