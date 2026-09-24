using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public static partial class HizliGirisEndpoints
{
    /// <summary>Eksik gelen listesinin üst sınırı (en yeniler döner; toplam X-Toplam-Kayit başlığında).</summary>
    public const int EksikGelenEnFazla = 200;

    /// <summary>
    /// Gelen tablosu (dönemin bütün kanalları tek ekranda) ve geçmiş dönemlerde girilmemiş gelenler.
    /// Yazma mevcut <c>PUT /api/gelenler</c> upsert'iyle yapılır (isteğe bağlı <c>beklenenTutar</c> korumasıyla).
    /// </summary>
    private static void GelenUclari(RouteGroupBuilder api)
    {
        api.MapGet("/gelenler/tablo", (DateOnly? donemStart, KasaDbContext db, TimeProvider saat) =>
        {
            var takip = TakipBaslangici(db);
            var bitis = Takvim.Bitis(takip, Saat.Bugun(saat));
            var tarih = donemStart ?? bitis; // verilmezse bugünün dönemi (takip ileride ise ilk dönem)
            if (tarih > bitis || Takvim.DonemBaslangici(tarih, takip) is not { } bas)
                return Hata($"Tarih takip dönemlerinin dışında ({takip:dd.MM.yyyy} – {bitis:dd.MM.yyyy}).");

            var son = DonemUretici.DogalBitis(bas);
            DateOnly? onceki = bas > takip ? Takvim.DonemBaslangici(bas.AddDays(-1), takip) : null;
            DateOnly? sonraki = son < bitis ? son.AddDays(1) : null;

            var gelenler = db.Gelenler.AsNoTracking().Where(g => g.DonemStart == bas).ToList();
            var satirlar = db.Kanallar.AsNoTracking().OrderBy(k => k.Sira).ThenBy(k => k.Id).ToList()
                .Where(k => k.Aktif || gelenler.Any(g => g.Kanal == k.Ad))
                .Select(k =>
                {
                    var g = gelenler.FirstOrDefault(x => x.Kanal == k.Ad);
                    return new GelenHucreDto(k.Ad, k.Aktif, g?.TutarTl, g?.Id);
                })
                .ToList();
            return Results.Ok(new GelenTablosuDto(bas, son, onceki, sonraki, satirlar));
        });

        // Bitmiş dönemlerde aktif kanalın gelen satırı hiç yoksa ("MEZAT geleni girilmedi"). 0 girilmiş
        // gelen girilmiş sayılır. Kanalın başlangıcından önceki ve pasif olduğu dönemler sayılmaz
        // (bkz. KanalDonemleri); başlangıcı hiç bilinmeyen kanal listelenmez.
        api.MapGet("/gelenler/eksik-liste", (KasaDbContext db, TimeProvider saat, HttpContext http) =>
        {
            var takip = TakipBaslangici(db);
            var bugun = Saat.Bugun(saat);
            var sonuc = new List<EksikGelenSatiriDto>();
            if (takip < bugun)
            {
                var donemler = DonemUretici.Uret(takip, bugun)
                    .Where(d => DonemUretici.DogalBitis(d.Start) < bugun)
                    .OrderByDescending(d => d.Start)
                    .ToList();
                var kanallar = db.Kanallar.AsNoTracking().Where(k => k.Aktif)
                    .OrderBy(k => k.Sira).ThenBy(k => k.Id).Select(k => new { k.Id, k.Ad }).ToList();
                var takvim = KanalDonemleri.Oku(db, kanallar.Select(k => (k.Id, k.Ad)).ToList());
                var girilen = db.Gelenler.AsNoTracking().Select(g => new { g.DonemStart, g.Kanal }).ToList()
                    .Select(g => (g.DonemStart, g.Kanal)).ToHashSet();

                foreach (var d in donemler)
                    foreach (var k in kanallar)
                    {
                        var son = DonemUretici.DogalBitis(d.Start);
                        if (!takvim[k.Id].Etkin(d.Start, son)) continue;
                        if (!girilen.Contains((d.Start, k.Ad)))
                            sonuc.Add(new EksikGelenSatiriDto(d.Start, son, k.Ad));
                    }
            }
            http.Response.Headers["X-Toplam-Kayit"] = sonuc.Count.ToString();
            return Results.Ok(sonuc.Take(EksikGelenEnFazla).ToList());
        });
    }
}

/// <summary>
/// Eksik gelen listesi için kanalın hangi dönemlerde var ve aktif olduğu. Geçmiş (değişiklik kaydı)
/// yalnız ipucudur: eski kurulumlarda hiç yoktur ve <see cref="GecmisKurallari.SaklamaYili"/>'ndan
/// eskisi açılışta silinir. Bu yüzden:
/// <list type="bullet">
/// <item>Başlangıç = kanalın "Eklendi" satırı ile ilk hareketinin (ilk geleni ya da işlemi) erkeni. İkisi
///       de yoksa başlangıç bilinmez ve kanal listelenmez (eklenmeden önceki haftalar asla eksik sayılmaz).</item>
/// <item>Aktif/pasif geçişleri "Güncellendi" satırlarının eski/yeni JSON'undaki <c>aktif</c> alanından
///       okunur; bir dönem boyunca baştan sona pasifse o dönem sayılmaz. Geçmişi silinmiş geçişler
///       bilinemez: o dönemler aktif sayılır.</item>
/// </list>
/// </summary>
internal sealed class KanalDonemleri
{
    private readonly DateOnly? _baslangic;
    private readonly List<(DateOnly Tarih, bool Aktif)> _gecisler;

    private KanalDonemleri(DateOnly? baslangic, List<(DateOnly, bool)> gecisler)
    {
        _baslangic = baslangic;
        _gecisler = gecisler;
    }

    /// <summary>Dönem [bas, son] boyunca kanal bir an olsun var ve aktif miydi?</summary>
    public bool Etkin(DateOnly bas, DateOnly son)
    {
        if (_baslangic is not { } b || son < b) return false;
        if (_gecisler.Count == 0) return true;
        var aktif = !_gecisler[0].Aktif;                 // ilk geçişten önceki durum
        foreach (var g in _gecisler)
        {
            if (g.Tarih >= bas) break;
            aktif = g.Aktif;
        }
        return aktif || _gecisler.Any(g => g.Aktif && g.Tarih >= bas && g.Tarih <= son);
    }

    public static Dictionary<int, KanalDonemleri> Oku(KasaDbContext db, IReadOnlyList<(int Id, string Ad)> kanallar)
    {
        var idler = kanallar.Select(k => (int?)k.Id).ToList();
        var adlar = kanallar.Select(k => k.Ad).ToList();
        var satirlar = db.Degisiklikler.AsNoTracking()
            .Where(d => d.Tur == GecmisTurleri.Kanal && idler.Contains(d.KayitId)
                        && (d.Eylem == Eylemler.Eklendi || d.Eylem == Eylemler.GeriAlindi || d.Eylem == Eylemler.Guncellendi))
            .OrderBy(d => d.ZamanUtc).ThenBy(d => d.Id)
            .Select(d => new { KayitId = d.KayitId!.Value, d.ZamanUtc, d.Eylem, d.EskiJson, d.YeniJson })
            .ToList();
        var ilkGelen = db.Gelenler.AsNoTracking().Where(g => adlar.Contains(g.Kanal))
            .Select(g => new { g.Kanal, g.DonemStart }).ToList()
            .GroupBy(g => g.Kanal).ToDictionary(g => g.Key, g => g.Min(x => x.DonemStart));
        var ilkIslem = db.Islemler.AsNoTracking().Where(i => adlar.Contains(i.Kanal))
            .GroupBy(i => i.Kanal).Select(g => new { Kanal = g.Key, Ilk = g.Min(x => x.Tarih) }).ToList()
            .ToDictionary(x => x.Kanal, x => x.Ilk);

        var sonuc = new Dictionary<int, KanalDonemleri>();
        foreach (var (id, ad) in kanallar)
        {
            var kendi = satirlar.Where(d => d.KayitId == id).ToList();
            DateOnly? En(DateOnly? a, DateOnly? b) => a is null ? b : b is null ? a : (a < b ? a : b);
            DateOnly? baslangic = kendi.Where(d => d.Eylem != Eylemler.Guncellendi)
                .Select(d => (DateOnly?)DateOnly.FromDateTime(Saat.Simdi(d.ZamanUtc))).Min();
            baslangic = En(baslangic, ilkGelen.TryGetValue(ad, out var g) ? g : null);
            baslangic = En(baslangic, ilkIslem.TryGetValue(ad, out var i) ? i : null);
            var gecisler = new List<(DateOnly, bool)>();
            foreach (var d in kendi.Where(d => d.Eylem == Eylemler.Guncellendi))
                if (Aktif(d.EskiJson) is { } eski && Aktif(d.YeniJson) is { } yeni && eski != yeni)
                    gecisler.Add((DateOnly.FromDateTime(Saat.Simdi(d.ZamanUtc)), yeni));
            sonuc[id] = new KanalDonemleri(baslangic, gecisler);
        }
        return sonuc;
    }

    private static bool? Aktif(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var belge = System.Text.Json.JsonDocument.Parse(json);
            return belge.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                   && belge.RootElement.TryGetProperty("aktif", out var a)
                   && a.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
                ? a.GetBoolean()
                : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
