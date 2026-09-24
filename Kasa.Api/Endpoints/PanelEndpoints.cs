using System.Globalization;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

/// <summary>
/// Paket A — panel, tahmin ve bildirim uçları. Hepsi okumadır (her iki rol); hiçbir kaydı ve
/// hiçbir rapor rakamını değiştirmez. Program.cs'te tek satırla bağlanır: <c>api.MapPanelUclari();</c>
/// </summary>
public static class PanelEndpoints
{
    /// <summary>Tahminin varsayılan ufku (gün).</summary>
    public const int VarsayilanTahminGunu = 30;

    /// <summary>Eksik gelen için geriye bakılan gün: bu süreden önce biten dönemler listelenmez.</summary>
    public const int EksikGelenGeriyeGun = 14;

    /// <summary>Özet yanıtındaki en fazla geçmişe dönük satır.</summary>
    public const int OzetSatirSiniri = 5;

    public static RouteGroupBuilder MapPanelUclari(this RouteGroupBuilder api)
    {
        // Nakit tahmini: gun = 1–366 (varsayılan 30); haric = hesaptan çıkarılacak çek Id'leri ("3,7").
        api.MapGet("/rapor/tahmin", (int? gun, string? haric, KasaDbContext db, TimeProvider saat, HesapServisi svc) =>
        {
            var g = gun ?? VarsayilanTahminGunu;
            if (g is < 1 or > NakitTahmini.EnFazlaGun)
                return Hata($"gun 1 ile {NakitTahmini.EnFazlaGun} arasında olmalı.");
            if (!IdListesiCoz(haric, out var haricIdler))
                return Hata("haric virgülle ayrılmış çek numaraları olmalı (ör. 3,7).");
            return Results.Ok(new TahminServisi(db, saat, svc).Hesapla(g, haricIdler));
        });

        // Geleni girilmemiş, son iki haftada biten dönemler (Bugün yapılacaklar, Pazartesi bildirimi). Kanal kuralı
        // Gelenler sayfasının eksik listesiyle (GET /gelenler/eksik-liste) aynıdır: kanal, var olmadığı ya da baştan
        // sona pasif olduğu dönem için eksik sayılmaz (KanalDonemleri).
        api.MapGet("/gelenler/eksik", (KasaDbContext db, TimeProvider saat, HesapServisi svc) =>
        {
            var bugun = Saat.Bugun(saat);
            var enErken = bugun.AddDays(-EksikGelenGeriyeGun);
            var donemler = svc.Donemler().Where(d => d.End < bugun && d.End >= enErken).ToList();
            if (donemler.Count == 0) return Results.Ok(Array.Empty<EksikGelenDto>());
            var kanallar = db.Kanallar.AsNoTracking().Where(k => k.Aktif)
                .OrderBy(k => k.Sira).ThenBy(k => k.Id).Select(k => new { k.Id, k.Ad }).ToList();
            var takvim = KanalDonemleri.Oku(db, kanallar.Select(k => (k.Id, k.Ad)).ToList());
            var ilk = donemler.Min(d => d.Start);
            var son = donemler.Max(d => d.Start);
            var girilen = db.Gelenler.AsNoTracking().Where(x => x.DonemStart >= ilk && x.DonemStart <= son)
                .Select(x => new { x.DonemStart, x.Kanal }).AsEnumerable()
                .Select(x => (x.DonemStart, x.Kanal)).ToHashSet();
            var sonuc = donemler.OrderBy(d => d.Start)
                .Select(d => new EksikGelenDto(d.Start, d.End, kanallar
                    .Where(k => takvim[k.Id].Etkin(d.Start, d.End) && !girilen.Contains((d.Start, k.Ad)))
                    .Select(k => k.Ad).ToList()))
                .Where(e => e.Kanallar.Count > 0)
                .ToList();
            return Results.Ok(sonuc);
        });

        // Geçmiş özeti: en yeni defter satırı (defterin son güncellenmesi) ve sonId'den sonraki defter değişiklikleri.
        // Defter dışı satırlar (kullanıcı, güvenlik ayarı, soru; yalnız gizli alanı değişen güncelleme) sayılmaz.
        api.MapGet("/gecmis/ozet", (int? sonId, KasaDbContext db) =>
        {
            if (sonId is < 0) return Hata("sonId negatif olamaz.");
            var defter = DefterSatirlari(db);
            var enYeni = defter.OrderByDescending(d => d.Id)
                .Select(d => new { d.Id, d.ZamanUtc }).FirstOrDefault();
            if (enYeni is null) return Results.Ok(new GecmisOzetDto(0, null, 0, 0, []));
            var sonZaman = DateTime.SpecifyKind(enYeni.ZamanUtc, DateTimeKind.Utc);
            if (sonId is not { } s) return Results.Ok(new GecmisOzetDto(enYeni.Id, sonZaman, 0, 0, []));

            var toplam = defter.Count(d => d.Id > s);
            var turler = GecmiseDonukKurali.IlgiliTurler.ToList();
            var donukler = db.Degisiklikler.AsNoTracking()
                .Where(d => d.Id > s && turler.Contains(d.Tur))
                .OrderByDescending(d => d.Id)
                .AsEnumerable()
                .Where(GecmiseDonukKurali.Mi)
                .ToList();
            var satirlar = donukler.Take(OzetSatirSiniri)
                .Select(d => new GecmisOzetSatiriDto(d.Id, DateTime.SpecifyKind(d.ZamanUtc, DateTimeKind.Utc), d.Tur, d.Ozet))
                .ToList();
            return Results.Ok(new GecmisOzetDto(enYeni.Id, sonZaman, toplam, donukler.Count, satirlar));
        });

        return api;
    }

    /// <summary>
    /// Defterin kayıtlarına dokunmayan geçmiş türleri (Paket E): kullanıcılar, güvenlik ayarı ve sorular. Geçmiş
    /// özetinde ("Defter en son … güncellendi", son bakıştan beri değişiklik) sayılmazlar.
    /// </summary>
    public static readonly string[] DefterDisiTurler =
        [GecmisTurleri.Kullanici, GecmisTurleri.GuvenlikAyari, GecmisTurleri.Soru];

    /// <summary>
    /// Özetin saydığı geçmiş satırları: defter dışı türler ve yalnız gizli alanı değişen güncellemeler
    /// (izleyici şifresi, oturumları kapatma: eski ve yeni görünür hal aynıdır) hariç.
    /// </summary>
    private static IQueryable<DegisiklikEntity> DefterSatirlari(KasaDbContext db)
        => db.Degisiklikler.AsNoTracking()
            .Where(d => !DefterDisiTurler.Contains(d.Tur)
                        && !(d.Eylem == Eylemler.Guncellendi && d.EskiJson != null && d.EskiJson == d.YeniJson));

    private static IResult Hata(string mesaj) => Results.BadRequest(new { hata = mesaj });

    // "3, 7,12" → {3, 7, 12}; boş → boş küme. Sayı olmayan ya da negatif parça → false.
    private static bool IdListesiCoz(string? metin, out IReadOnlyCollection<int> idler)
    {
        var sonuc = new HashSet<int>();
        idler = sonuc;
        if (string.IsNullOrWhiteSpace(metin)) return true;
        foreach (var parca in metin.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(parca, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return false;
            sonuc.Add(id);
        }
        return sonuc.Count <= 1000;
    }
}
