using System.Globalization;
using Kasa.Api.Data;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Denetim;

/// <summary>
/// Alışın ödeme payları okuma anında türetilir: iade (onaylı → taslak), yeniden onay ve ödeme/gider bağlama, kilitlenmemiş
/// geçmiş ayların kanal sonuçlarını geriye dönük değiştirebilir (tasarım gereği; tek koruma ay kilididir). Davranış
/// değişmez, iz eklenir: pay değişen ödemeler bugünün ayından önceki bir aya düşüyorsa 'GecmisAyEtkisi' olayı ödeme
/// başına önceki ve yeni kanal paylarını ve etkilenen ayları taşır; yanıt <see cref="YanitBasligi"/> başlığında ayları
/// ('yyyy-MM', virgülle) bildirir. Etkilenen tarih: ödemenin gider tarihi ve takipli kartta o giderin taksitlerini ödeyen
/// kart ödemelerinin tarihleri (kanal payı kasaya ödemede düşer).
/// </summary>
public static class AlisDurumEtkisi
{
    public const string YanitBasligi = "X-Kasa-Gecmis-Ay-Etkisi";

    internal sealed record KanalPayi(int? KanalId, string? Etiket, decimal Tutar);
    /// <summary>Ödemenin kanal payları; boş liste: dağılım bekliyor (alış onaylı değil).</summary>
    internal sealed record OdemePayi(int IslemId, DateOnly Tarih, decimal Tutar, int? KrediKartiId, IReadOnlyList<KanalPayi> Paylar);

    /// <summary>Alışın ödemelerinin şu anki payları (alış kalem, dağılım ve ödeme giderleriyle yüklenmiş olmalı).</summary>
    internal static Dictionary<int, OdemePayi> Paylar(AlisEntity alis)
    {
        var dagilim = AlisHesaplari.OdemeDagilimlari(alis);
        return alis.Odemeler.ToDictionary(o => o.IslemId, o => new OdemePayi(o.IslemId, o.Islem.Tarih, o.Islem.TutarTl, o.Islem.KrediKartiId,
            dagilim.TryGetValue(o.IslemId, out var p) ? p.Select(x => new KanalPayi(x.KanalId, null, x.Tutar)).ToList() : []));
    }

    /// <summary>Alışa bağlanmadan önceki mevcut giderin kendi kanalı.</summary>
    internal static OdemePayi Gider(IslemEntity islem) =>
        new(islem.Id, islem.Tarih, islem.TutarTl, islem.KrediKartiId, [new KanalPayi(islem.KanalId, islem.Kanal, islem.TutarTl)]);

    /// <summary>Alışa bağlanmadan önceki banka ekstresi giderinin payları: hesap motoru onu ekstre satırının dağılımıyla sayar
    /// (yalnız genel kasa satırı hiçbir kanala düşmez).</summary>
    internal static OdemePayi EkstreGideri(IslemEntity islem, EkstreKayitEntity satir) =>
        new(islem.Id, islem.Tarih, islem.TutarTl, islem.KrediKartiId, satir.DagilimTuru == "Genel"
            ? [new KanalPayi(null, KanalEtiketleri.GenelKasa, islem.TutarTl)]
            : FinansTakipServisi.Read<TakipKanalPayi>(satir.DagilimJson).Select(p => new KanalPayi(p.KanalId, p.Kanal, p.Tutar)).ToList());

    /// <summary>Alış ödemesine bağlanan (gidersiz) kart harcamasının bağlanmadan önceki payları: kendi dağılımı. Kimlik, bağlama
    /// için oluşturulan giderinkidir.</summary>
    internal static OdemePayi KartHarcamasi(int islemId, TakipHarcamaEntity harcama) =>
        new(islemId, harcama.Tarih, harcama.Tutar, harcama.KrediKartiId,
            FinansTakipServisi.Read<KanalPayYaz>(harcama.DagilimJson).Select(p => new KanalPayi(p.KanalId, null, p.Tutar)).ToList());

    /// <summary>Değişiklik kaydedildikten sonra, aynı transaction'da: pay değişen ödemelerin geçmiş aylara etkisini
    /// olay olarak yazar ve yanıt başlığına ekler. Etkilenen geçmiş ay yoksa hiçbir şey yazmaz.</summary>
    internal static IReadOnlyList<string> Yaz(KasaDbContext db, HttpContext http, AlisEntity alis, string oncekiDurum, IReadOnlyDictionary<int, OdemePayi> once, string? gerekce)
    {
        var sonra = Paylar(alis);
        var degisen = once.Keys.Union(sonra.Keys).Where(id => !Ayni(once.GetValueOrDefault(id), sonra.GetValueOrDefault(id))).Order().ToList();
        if (degisen.Count == 0)
            return [];
        var tarihler = degisen.SelectMany(id => new[] { once.GetValueOrDefault(id)?.Tarih, sonra.GetValueOrDefault(id)?.Tarih }).OfType<DateOnly>().ToList();
        tarihler.AddRange(KartOdemeTarihleri(db, degisen));
        var bugun = db.Bugunu();
        var ayBasi = new DateOnly(bugun.Year, bugun.Month, 1);
        var aylar = tarihler.Where(t => t < ayBasi).Select(t => t.ToString("yyyy-MM", CultureInfo.InvariantCulture)).Distinct().Order().ToList();
        if (aylar.Count == 0)
            return [];
        var pencere = DenetimKilitPenceresi.Oku(db);
        DenetimYazici.Yaz(db, new DenetimOlayi("GecmisAyEtkisi", "Alis", alis.Id.ToString(CultureInfo.InvariantCulture),
            DenetimYazici.Json(new { Durum = oncekiDurum, Odemeler = degisen.Select(id => once.GetValueOrDefault(id)).OfType<OdemePayi>().ToList() }),
            DenetimYazici.Json(new { alis.Durum, Odemeler = degisen.Select(id => sonra.GetValueOrDefault(id)).OfType<OdemePayi>().ToList(), GecmisAylar = aylar }),
            gerekce, tarihler.Where(t => t < ayBasi).OrderDescending().Select(pencere.Bul).FirstOrDefault(p => p is not null)));
        http.Response.Headers[YanitBasligi] = string.Join(",", aylar);
        return aylar;
    }

    // Kanal etkisi aynı mı: tarih, tutar ve kanal başına pay (kanal etiketi yalnız bilgidir).
    private static bool Ayni(OdemePayi? a, OdemePayi? b) => a is not null && b is not null && a.Tarih == b.Tarih && a.Tutar == b.Tutar
        && a.Paylar.Select(p => (p.KanalId, p.Tutar)).Order().SequenceEqual(b.Paylar.Select(p => (p.KanalId, p.Tutar)).Order());

    // Takipli kartla ödenen giderin kanal payı, taksitlerini ödeyen (iptal edilmemiş) kart ödemesinde kasaya düşer.
    private static IEnumerable<DateOnly> KartOdemeTarihleri(KasaDbContext db, IReadOnlyList<int> islemler)
    {
        var harcamalar = db.TakipHarcamalar.AsNoTracking().Where(h => h.IslemId != null && islemler.Contains(h.IslemId.Value)).Select(h => new { h.Id, h.KrediKartiId }).ToList();
        if (harcamalar.Count == 0)
            return [];
        var ids = harcamalar.Select(h => h.Id).ToList();
        var taksitler = db.TakipKartTaksitler.AsNoTracking().Where(t => ids.Contains(t.HarcamaId)).Select(t => t.Id).ToHashSet();
        var kartlar = harcamalar.Select(h => h.KrediKartiId).Distinct().ToList();
        return db.TakipKartOdemeler.AsNoTracking().Where(p => kartlar.Contains(p.KrediKartiId) && !p.Iptal).AsEnumerable()
            .Where(p => FinansTakipServisi.Read<KartTaksitPayi>(p.PaylarJson).Any(x => taksitler.Contains(x.TaksitId))).Select(p => p.Tarih).ToList();
    }
}
