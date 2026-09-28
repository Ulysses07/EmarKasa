using System.Text.Json.Serialization;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>Ana sayfanın tek çağrıdaki özeti: panel, kanal kasa eşikleri ve takip özeti (kart/kredi olayları).</summary>
/// <param name="TakipOzeti">Takip özeti; bozuk bir kart/kredi kaydı yüzünden hesaplanamadıysa null (panel ve eşikler yine döner).</param>
/// <param name="VeriSagligiUyarisi">Karantinaya alınan kayıtların ve hesaplanamayan takip özetinin uyarısı; sorun yoksa null ve
/// JSON'a yazılmaz (sağlam veride yanıt biçimi aynen korunur).</param>
public record AnaSayfaDto(PanelDto Panel, IReadOnlyList<KasaEsikDto> KasaEsikleri, TakipOzetDto? TakipOzeti,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VeriSagligiUyarisi = null);

/// <summary>
/// Ana sayfa tekrarının (gap-okuma-yolu-maliyet-kilit-cekismesi-4) sunucu çözümü. Ana sayfa panel, kasa eşikleri ve takip
/// özetini ayrı uçlardan istediğinde panel iki kez, her kartın hesabı iki kez yapılır. Bu uç üçünü tek salt okunur anlık
/// görüntüde ve tek hesap bağlamıyla üretir: kasa hesabı bir kez, kart verisi ve ödeme etkileri kart başına bir kez;
/// eşik rozetleri ile bakiyeler aynı andan okunur. Önbellek bilinçli olarak kullanılmaz: doğru geçersizleştirme bütün
/// yazma yollarını (SaveChanges, ham SQL upsert'ler, Sync, migration, yedekten dönüş) ve gün dönümünü kapsamak zorunda
/// kalırdı; tek bir kaçak eski kasa rakamı gösterir. Eski uçlar (/api/rapor/panel, /api/kasa-esikleri,
/// /api/takip/ozet) aynen korunur; istemcilerin bu uca geçmesi ayrı iştir.
/// Dayanıklılık (gap-veri-degismezleri-patlama-yaricapi-3): panel bozuk kaydı karantinaya alarak hesaplanır (bkz.
/// <see cref="HesapServisi"/>); takip özeti bozuk bir kart/kredi kaydında hesaplanamazsa panel ve eşikler yine döner, özet null
/// olur ve uyarı okunamayan kaydı kimliğiyle söyler (kayıt başına bir kez Warning). Geçersiz gün (400) ve veritabanı hataları
/// eskisi gibi döner.
/// </summary>
public static class AnaSayfaOzeti
{
    public static IResult Oku(KasaDbContext db, HesapServisi hesap, int gun, CancellationToken ct) => FinansTakipEndpoints.Safe(() =>
        AlisEndpoints.Oku(db, () =>
        {
            var takip = new TakipHesapBaglami(db, ct);
            var (panel, uyari) = hesap.PanelVeUyari(takip);
            var esikler = KasaKontrolEndpoints.Esikler(db, panel);
            TakipOzetDto? ozet;
            try { ozet = FinansTakipEndpoints.Ozet(takip, gun); }
            catch (Exception e) when (VeriKarantinasi.VeriHatasiMi(e))
            {
                ozet = null;
                var ozetUyarisi = TakipOzetiUyarisi(takip, e);
                uyari = uyari is null ? ozetUyarisi : uyari + " " + ozetUyarisi;
            }
            return Results.Ok(new AnaSayfaDto(panel, esikler, ozet, uyari));
        }));

    /// <summary>Takip özeti hesaplanamadı: özetin okuduğu kartlar ve krediler tek tek denenir, okunamayanlar kimlikleriyle uyarıya
    /// ve (kayıt başına bir kez) loga yazılır. Yalnız hata yolunda çalışır.</summary>
    private static string TakipOzetiUyarisi(TakipHesapBaglami takip, Exception hata)
    {
        var db = takip.Db;
        var bozuk = new List<(string Anahtar, string Ad, Exception Hata)>();
        foreach (var kart in db.KrediKartlari.AsNoTracking().OrderBy(k => k.Id).Select(k => new { k.Id, k.Ad }).ToList())
            try { FinansTakipServisi.Kart(takip, kart.Id); }
            catch (Exception e) when (VeriKarantinasi.VeriHatasiMi(e)) { bozuk.Add(("TakipOzeti:KrediKarti:" + kart.Id, $"kredi kartı #{kart.Id} ('{kart.Ad}')", e)); }
        foreach (var kredi in db.Krediler.AsNoTracking().OrderBy(k => k.Id).Select(k => new { k.Id, k.Ad }).ToList())
            try { FinansTakipServisi.Kredi(takip, kredi.Id); }
            catch (Exception e) when (VeriKarantinasi.VeriHatasiMi(e)) { bozuk.Add(("TakipOzeti:Kredi:" + kredi.Id, $"kredi #{kredi.Id} ('{kredi.Ad}')", e)); }
        if (bozuk.Count == 0) bozuk.Add(("TakipOzeti", "kart/kredi kayıtları", hata));
        foreach (var b in bozuk)
            VeriKarantinasi.Logla(db, b.Anahtar, $"Takip özeti hesaplanamadı: {b.Ad} okunamadı; ana sayfa takip özetsiz döndü.", b.Hata);
        return $"Kart ve kredi takip özeti hesaplanamadı ({string.Join(", ", bozuk.Select(b => b.Ad))} okunamadı); yaklaşan kesim, son ödeme ve taksitler gösterilemiyor. Kart ve kredi ekranlarından kaydı kontrol edin.";
    }
}
