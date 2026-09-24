using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api;

public static partial class HizliGirisEndpoints
{
    /// <summary>Tek toplu yüklemede en fazla satır.</summary>
    public const int TopluEnFazlaSatir = 1000;

    /// <summary>
    /// Excel'den yapıştırılan / CSV'den okunan işlemleri TEK transaction'da ekler: her satır tek tek
    /// eklemedeki doğrulamadan (<c>IslemHatasi</c>) geçer; bir satır bile hatalıysa hiçbiri yazılmaz
    /// (400 + satır satır hatalar). İstenirse kayıtlı olmayan cariler aynı transaction'da eklenir.
    /// Geçmişe her işlem ayrı satır olarak, ayrıca yüklemenin özeti tek satır olarak yazılır.
    /// </summary>
    private static void TopluUclari(RouteGroupBuilder api,
        Func<KasaDbContext, IslemEntity, string?> islemHatasi,
        Func<KasaDbContext, string, int?, string?> cariHatasi)
    {
        api.MapPost("/islemler/toplu", (TopluIslemIstegi istek, KasaDbContext db) =>
        {
            var satirlar = istek.Satirlar ?? [];
            if (satirlar.Count == 0) return Hata("Yüklenecek satır yok.");
            if (satirlar.Count > TopluEnFazlaSatir) return Hata($"Tek seferde en fazla {TopluEnFazlaSatir} satır yüklenebilir.");
            if (satirlar.Any(s => s is null)) return Hata("Boş satır gönderilemez.");

            try
            {
                using var tx = db.Database.BeginTransaction();
                var hatalar = new List<TopluIslemHatasiDto>();

                // 1) Yeni cariler (istenirse): sabit gider (kartsız) satırları kalemdir, cari eklenmez.
                var yeniCariler = new List<string>();
                if (istek.YeniCarileriEkle)
                {
                    var kayitli = db.Cariler.Select(c => c.Ad).ToList();
                    for (var i = 0; i < satirlar.Count; i++)
                    {
                        var s = satirlar[i];
                        var ad = s.Cari?.Trim() ?? "";
                        if (ad.Length == 0 || (s.Tip == GiderTipi.SabitGider && s.KrediKartiId is null)) continue;
                        if (kayitli.Any(a => Metin.EsitBuyukKucukDuyarsiz.Equals(a, ad))
                            || yeniCariler.Any(a => Metin.EsitBuyukKucukDuyarsiz.Equals(a, ad))) continue;
                        if (cariHatasi(db, ad, null) is string ch) { hatalar.Add(new(i + 1, ch)); continue; }
                        yeniCariler.Add(ad);
                    }
                    if (hatalar.Count == 0 && yeniCariler.Count > 0)
                    {
                        db.Cariler.AddRange(yeniCariler.Select(a => new CariEntity { Ad = a }));
                        db.SaveChanges();
                    }
                }

                // 2) Her satır tek eklemedeki kurallarla doğrulanır (kayıtlı yazıma çevrilir).
                var eklenecek = new List<IslemEntity>(satirlar.Count);
                if (hatalar.Count == 0)
                {
                    for (var i = 0; i < satirlar.Count; i++)
                    {
                        var s = satirlar[i];
                        var e = new IslemEntity
                        {
                            Tarih = s.Tarih, Cari = s.Cari ?? "", TutarTl = s.TutarTl, Kanal = s.Kanal?.Trim() ?? "",
                            Tip = s.Tip, Not = string.IsNullOrWhiteSpace(s.Not) ? null : s.Not.Trim(), KrediKartiId = s.KrediKartiId,
                        };
                        if (e.KrediKartiId is not null) e.Tip = GiderTipi.KrediKarti; // kart harcaması tutarlılığı
                        if (islemHatasi(db, e) is string h) hatalar.Add(new(i + 1, h));
                        else eklenecek.Add(e);
                    }
                }

                if (hatalar.Count > 0)
                {
                    tx.Rollback();
                    db.ChangeTracker.Clear();
                    var ilk = hatalar[0];
                    return Results.BadRequest(new
                    {
                        hata = $"{hatalar.Count} satırda hata var; hiçbir satır kaydedilmedi. {ilk.Sira}. satır: {ilk.Hata}",
                        satirlar = hatalar,
                    });
                }

                // 3) Hepsi geçerli: tek seferde ekle, özeti geçmişe yaz.
                db.Islemler.AddRange(eklenecek);
                var toplam = eklenecek.Sum(e => e.TutarTl);
                var ilkTarih = eklenecek.Min(e => e.Tarih);
                var sonTarih = eklenecek.Max(e => e.Tarih);
                var aralik = ilkTarih == sonTarih ? $"{ilkTarih:dd.MM.yyyy}" : $"{ilkTarih:dd.MM.yyyy} – {sonTarih:dd.MM.yyyy}";
                var cariNotu = yeniCariler.Count > 0 ? $"; {yeniCariler.Count} yeni cari eklendi" : "";
                db.TopluDegisiklikEkle(GecmisTurleri.Islem, null,
                    $"Toplu yükleme: {eklenecek.Count} işlem eklendi (toplam {Para(toplam)}, {aralik}){cariNotu}");
                db.SaveChanges();
                tx.Commit();
                return Results.Ok(new TopluIslemSonucuDto(eklenecek.Count, toplam, yeniCariler, eklenecek));
            }
            catch (Exception ex) when (KisitIhlali(ex))
            {
                db.ChangeTracker.Clear();
                return Results.Conflict(new { hata = "Toplu yükleme kaydedilemedi: ilgili bir kayıt aynı anda değişti. Hiçbir satır kaydedilmedi; tekrar deneyin." });
            }
        }).RequireAuthorization("Editor");
    }
}
