using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Endpoints;

/// <summary>
/// Paket D — çek ve senet: tek dokunuşla durum (özellik 30) ve portföy risk dağılımı (özellik 43).
/// Kasa kuralı değişmez: durum değişikliği PUT /api/cekler/{id} ile birebir aynı kaydı üretir.
/// </summary>
public static class CekEvrakEndpoints
{
    public static RouteGroupBuilder MapCekEvrak(this RouteGroupBuilder api, YazIslemi yaz, CekDogrulama cekHatasi)
    {
        // Tek dokunuş: yalnız portföydeki evrak; tarih verilmezse bugün. Doğrulama PUT'takiyle aynı.
        api.MapPost("/cekler/{id:int}/durum", (int id, CekDurumYazDto dto, KasaDbContext db, TimeProvider saat) =>
            yaz(db, "Çek kaydedilemedi; tekrar deneyin.", () =>
        {
            var e = db.Cekler.Find(id);
            if (e is null) return Results.NotFound();
            if (!Enum.IsDefined(dto.Durum)) return Yanit.Hata("Geçersiz çek durumu.");
            if (e.Durum != CekDurumu.Portfoyde)
                return Yanit.Cakisma("Bu evrak artık portföyde değil; durumunu değiştirmek için Düzenle'yi kullanın.");
            if (!CekEvrakKurali.TekDokunusGecerli(e.Yon, dto.Durum))
                return Yanit.Hata(e.Yon == CekYonu.Alinan
                    ? "Alınan evrak için yalnız tahsil, ciro ya da karşılıksız seçilebilir."
                    : "Verilen evrak için yalnız ödendi seçilebilir.");
            if (dto.Durum == CekDurumu.CiroEdildi && string.IsNullOrWhiteSpace(dto.CiroEdilenCari))
                return Yanit.Hata("Ciro edilen cariyi seçin.");

            var yeni = CekEvrakKurali.Kopya(e);
            yeni.Durum = dto.Durum;
            // Karşılıksız kasaya dokunmaz ve formda da tarih taşımaz.
            yeni.IslemTarihi = dto.Durum == CekDurumu.Karsiliksiz ? null : dto.Tarih ?? Saat.Bugun(saat);
            yeni.CiroEdilenCari = dto.CiroEdilenCari;
            if (cekHatasi(db, yeni) is string hata) return Yanit.Hata(hata);
            CekEvrakKurali.Aktar(yeni, e);
            db.SaveChanges();
            return Results.Ok(e);
        })).RequireAuthorization("Editor");

        // Portföydeki alınan evrakın keşideciye ve bankaya göre dağılımı. tur: Cek / Senet (boş = ikisi).
        api.MapGet("/cekler/risk", (string? tur, KasaDbContext db) =>
        {
            CekTuru? t = null;
            if (!string.IsNullOrWhiteSpace(tur))
            {
                var ad = Enum.GetNames<CekTuru>().FirstOrDefault(n => string.Equals(n, tur.Trim(), StringComparison.OrdinalIgnoreCase));
                if (ad is null) return Yanit.Hata("Geçersiz evrak türü (Cek ya da Senet).");
                t = Enum.Parse<CekTuru>(ad);
            }
            var q = db.Cekler.AsNoTracking().Where(c => c.Yon == CekYonu.Alinan && c.Durum == CekDurumu.Portfoyde);
            if (t is { } tf) q = q.Where(c => c.Tur == tf);
            // Tutar SQLite'ta metin: toplamlar bellekte (portföy küçük bir kümedir).
            var liste = q.Select(c => new { c.Kisi, c.Banka, c.Tutar }).ToList();
            var toplam = liste.Sum(c => c.Tutar);
            return Results.Ok(new CekRiskDto(toplam, liste.Count,
                Dagilim(liste.Select(c => (c.Kisi, c.Tutar)), toplam),
                Dagilim(liste.Select(c => (string.IsNullOrWhiteSpace(c.Banka) ? BankaYok : c.Banka!, c.Tutar)), toplam)));
        });
        return api;
    }

    public const string BankaYok = "Banka belirtilmemiş";

    /// <summary>Ada göre (Türkçe büyük/küçük harf duyarsız) gruplar; ilk görülen yazım kullanılır. Tutara göre azalan.</summary>
    internal static IReadOnlyList<CekRiskKalemi> Dagilim(IEnumerable<(string Ad, decimal Tutar)> kalemler, decimal toplam)
        => kalemler
            .GroupBy(k => k.Ad.Trim(), Metin.EsitBuyukKucukDuyarsiz)
            .Select(g => new CekRiskKalemi(g.First().Ad.Trim(), g.Sum(x => x.Tutar), g.Count(),
                toplam == 0 ? 0m : decimal.Round(g.Sum(x => x.Tutar) / toplam, 4)))
            .OrderByDescending(k => k.Tutar).ThenBy(k => k.Ad, Metin.Sirala)
            .ToList();
}
