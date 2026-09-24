using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

/// <summary>
/// Sistem ve risk kartı (GET /api/sistem/risk, yalnız editör, salt okunur): sunucu dışı ve günlük
/// yedeğin yaşı (/health'in hesapladığı değerler), son yedek doğrulaması, boş disk, dünkü başarısız
/// girişler ve takipsiz (notu yok, icrada değil) karşılıksız çekler için kırmızı/sarı uyarılar.
/// </summary>
public static class SistemRiskEndpoints
{
    public static RouteGroupBuilder MapSistemRiskUclari(this RouteGroupBuilder api)
    {
        api.MapGet("/sistem/risk", (KasaDbContext db, YedekDurumu yedek, IConfiguration cfg, TimeProvider saat) =>
        {
            var simdi = saat.GetUtcNow();
            var simdiUtc = simdi.UtcDateTime;

            // "Dün": Türkiye'nin dünü (UTC'ye çevrilmiş aralık).
            var bugun = Saat.Bugun(saat);
            var yerelFark = Saat.Simdi(simdiUtc) - simdiUtc;
            var dunBas = bugun.AddDays(-1).ToDateTime(TimeOnly.MinValue) - yerelFark;
            var dunSon = bugun.ToDateTime(TimeOnly.MinValue) - yerelFark;
            // Başarısızlar saatlik satırlarda toplanır: deneme sayısı Tekrar toplamıdır.
            var dunBasarisiz = db.GirisKayitlari.AsNoTracking().Where(g => !g.Basarili && g.Neden != GirisNedenleri.KodBekleniyor
                && g.ZamanUtc >= dunBas && g.ZamanUtc < dunSon).Sum(g => g.Tekrar);

            // Takipte sayılır: takip notu yazılmış ya da Paket D'nin konumuyla icraya (takibe) verilmiş.
            var karsiliksiz = db.Cekler.AsNoTracking()
                .Where(c => c.Yon == CekYonu.Alinan && c.Durum == CekDurumu.Karsiliksiz)
                .Select(c => new { c.Id, c.Kisi, c.Tutar, c.VadeTarihi, c.Not, c.Konum })
                .ToList()
                .Select(c => new KarsiliksizCek(c.Id, c.Kisi, c.Tutar, c.VadeTarihi,
                    !string.IsNullOrWhiteSpace(c.Not) || c.Konum == CekKonumu.Icrada))
                .ToList();

            var son = db.YedekDogrulamalari.AsNoTracking().OrderByDescending(d => d.Id).FirstOrDefault();
            var disk = DiskBos(db);
            var girdi = new RiskGirdisi(
                UzakYedekDurumu.Oku(cfg["Kasa:UzakYedekDurumDosyasi"], simdi,
                    cfg.GetValue("Kasa:UzakYedekEskiSaat", UzakYedekDurumu.VarsayilanEskiSaat)),
                yedek.Etkin, yedek.SonBasariliUtc, yedek.SonHata, son, disk, dunBasarisiz, karsiliksiz, simdiUtc);
            var maddeler = RiskHesaplayici.Hesapla(girdi, RiskEsikleri.Oku(cfg));
            return new SistemRiskDto(RiskHesaplayici.Durum(maddeler), maddeler, simdiUtc,
                son is null ? null : new YedekDogrulamaDto(DateTime.SpecifyKind(son.ZamanUtc, DateTimeKind.Utc), son.Dosya,
                    DateTime.SpecifyKind(son.DosyaZamaniUtc, DateTimeKind.Utc), son.Basarili, son.Mesaj),
                disk, dunBasarisiz);
        }).RequireAuthorization("Editor");

        return api;
    }

    /// <summary>/health ile aynı ölçüm: DB dosyasının bulunduğu diskteki boş alan (MB); in-memory'de null.</summary>
    public static long? DiskBos(KasaDbContext db)
    {
        try
        {
            var kaynak = db.Database.GetDbConnection().DataSource;
            if (string.IsNullOrEmpty(kaynak) || kaynak.Contains(":memory:", StringComparison.OrdinalIgnoreCase)) return null;
            var klasor = Path.GetDirectoryName(Path.GetFullPath(kaynak));
            if (string.IsNullOrEmpty(klasor) || !Directory.Exists(klasor)) return null;
            return new DriveInfo(klasor).AvailableFreeSpace / (1024 * 1024);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
