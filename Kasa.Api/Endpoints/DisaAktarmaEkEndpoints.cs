using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Endpoints;

/// <summary>
/// 40 · Eksik "Excel'e aktar" çıktıları (çekler, kasa sayımları, geçmiş, kasa dökümü) ve ay sonu
/// paketi (tek ZIP). Okuma — her iki rol; rakamlar JSON uç noktalarıyla aynıdır.
/// </summary>
public static class DisaAktarmaEkEndpoints
{
    private static T? EnumCoz<T>(string metin) where T : struct, Enum
    {
        var ad = Enum.GetNames<T>().FirstOrDefault(n => string.Equals(n, metin.Trim(), StringComparison.OrdinalIgnoreCase));
        return ad is null ? null : Enum.Parse<T>(ad);
    }

    public static RouteGroupBuilder MapDisaAktarmaEk(this RouteGroupBuilder api)
    {
        // GET /api/cekler ile aynı filtre (yön, durum, tür, vade aralığı) ve sıra. Konum, Çekler sayfasındaki
        // gibi yalnız alınan evrakta aranır (verilen evrak hep "Elde" sayılır, listede konum süzgecine girmez).
        api.MapGet("/disaaktar/cekler.csv", (string? yon, string? durum, string? tur, string? konum, DateOnly? baslangic, DateOnly? bitis,
            KasaDbContext db, TimeProvider saat) =>
        {
            var q = db.Cekler.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(tur))
            {
                if (EnumCoz<CekTuru>(tur) is not { } t) return UcNokta.Hata("Geçersiz evrak türü (Cek ya da Senet).");
                q = q.Where(c => c.Tur == t);
            }
            if (!string.IsNullOrWhiteSpace(konum))
            {
                if (EnumCoz<CekKonumu>(konum) is not { } k) return UcNokta.Hata("Geçersiz evrak konumu.");
                q = q.Where(c => c.Yon == CekYonu.Alinan && c.Konum == k);
            }
            if (!string.IsNullOrWhiteSpace(yon))
            {
                if (EnumCoz<CekYonu>(yon) is not { } y) return UcNokta.Hata("Geçersiz yön (Alinan ya da Verilen).");
                q = q.Where(c => c.Yon == y);
            }
            if (!string.IsNullOrWhiteSpace(durum))
            {
                if (EnumCoz<CekDurumu>(durum) is not { } d) return UcNokta.Hata("Geçersiz çek durumu.");
                q = q.Where(c => c.Durum == d);
            }
            if (baslangic is { } b) q = q.Where(c => c.VadeTarihi >= b);
            if (bitis is { } s) q = q.Where(c => c.VadeTarihi <= s);
            var liste = q.OrderByDescending(c => c.VadeTarihi).ThenByDescending(c => c.Id).ToList();
            return UcNokta.Dosya(CsvRaporlariEk.Cekler(liste), CsvYazici.IcerikTipi, $"kasa-cekler-{CsvYazici.DosyaTarihi(Saat.Bugun(saat))}.csv");
        });

        api.MapGet("/disaaktar/kasasayimlari.csv", (KasaDbContext db, HesapServisi hesap, TimeProvider saat) =>
        {
            var liste = db.KasaSayimlari.AsNoTracking().OrderByDescending(s => s.Tarih).ThenByDescending(s => s.Id).ToList();
            var guncel = hesap.KasaTarihlerde(liste.Select(s => s.Tarih));
            var dtolar = liste.Select(s => KasaSayimDto.Olustur(s, guncel.TryGetValue(s.Tarih, out var g) ? g : null)).ToList();
            return UcNokta.Dosya(CsvRaporlariEk.KasaSayimlari(dtolar), CsvYazici.IcerikTipi,
                $"kasa-sayimlari-{CsvYazici.DosyaTarihi(Saat.Bugun(saat))}.csv");
        });

        api.MapGet("/disaaktar/gecmis.csv", (string? tur, KasaDbContext db, TimeProvider saat) =>
        {
            var q = db.Degisiklikler.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(tur)) q = q.Where(d => d.Tur == tur);
            var ad = "kasa-gecmis-" + CsvYazici.DosyaTarihi(Saat.Bugun(saat));
            if (CsvYazici.DosyaAdiParcasi(tur) is { Length: > 0 } p) ad += "-" + p;
            return UcNokta.Dosya(CsvRaporlariEk.Gecmis(q.OrderByDescending(d => d.Id).ToList()), CsvYazici.IcerikTipi, ad + ".csv");
        });

        api.MapGet("/disaaktar/kasa-dokumu.csv", (DateOnly? baslangic, DateOnly? bitis, RaporServisi rapor) =>
        {
            if (KasaDokumuEndpoints.AralikHatasi(baslangic, bitis) is string h) return UcNokta.Hata(h);
            var d = rapor.KasaDokumu(baslangic!.Value, bitis!.Value);
            if (d is null) return UcNokta.Hata("Bu aralıkta takip dönemi yok.");
            return UcNokta.Dosya(CsvRaporlariEk.KasaDokumu(d), CsvYazici.IcerikTipi,
                $"kasa-dokumu-{CsvYazici.DosyaTarihi(d.Baslangic)}_{CsvYazici.DosyaTarihi(d.Bitis)}.csv");
        });

        api.MapGet("/disaaktar/ay-paketi.zip", (int yil, int ay, KasaDbContext db, RaporServisi rapor, HesapServisi hesap, TimeProvider saat) =>
        {
            if (UcNokta.AyHatasi(yil, ay) is string h) return UcNokta.Hata(h);
            return UcNokta.Dosya(AyPaketi.Olustur(db, rapor, hesap, saat, yil, ay), AyPaketi.IcerikTipi, AyPaketi.DosyaAdi(yil, ay));
        });
        return api;
    }
}
