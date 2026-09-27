using Kasa.Api.Data;

namespace Kasa.Api.Servisler;

/// <summary>Ana sayfanın tek çağrıdaki özeti: panel, kanal kasa eşikleri ve takip özeti (kart/kredi olayları).</summary>
public record AnaSayfaDto(PanelDto Panel, IReadOnlyList<KasaEsikDto> KasaEsikleri, TakipOzetDto TakipOzeti);

/// <summary>
/// Ana sayfa tekrarının (gap-okuma-yolu-maliyet-kilit-cekismesi-4) sunucu çözümü. Ana sayfa panel, kasa eşikleri ve takip
/// özetini ayrı uçlardan istediğinde panel iki kez, her kartın hesabı iki kez yapılır. Bu uç üçünü tek salt okunur anlık
/// görüntüde ve tek hesap bağlamıyla üretir: kasa hesabı bir kez, kart verisi ve ödeme etkileri kart başına bir kez;
/// eşik rozetleri ile bakiyeler aynı andan okunur. Önbellek bilinçli olarak kullanılmaz: doğru geçersizleştirme bütün
/// yazma yollarını (SaveChanges, ham SQL upsert'ler, Sync, migration, yedekten dönüş) ve gün dönümünü kapsamak zorunda
/// kalırdı; tek bir kaçak eski kasa rakamı gösterir. Eski uçlar (/api/rapor/panel, /api/kasa-esikleri,
/// /api/takip/ozet) aynen korunur; istemcilerin bu uca geçmesi ayrı iştir.
/// </summary>
public static class AnaSayfaOzeti
{
    public static IResult Oku(KasaDbContext db, HesapServisi hesap, int gun, CancellationToken ct) => FinansTakipEndpoints.Safe(() =>
        AlisEndpoints.Oku(db, () =>
        {
            var takip = new TakipHesapBaglami(db, ct);
            var panel = hesap.Panel(takip);
            return Results.Ok(new AnaSayfaDto(panel, KasaKontrolEndpoints.Esikler(db, panel), FinansTakipEndpoints.Ozet(takip, gun)));
        }));
}
