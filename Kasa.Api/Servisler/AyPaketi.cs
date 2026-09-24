using System.IO.Compression;
using System.Text;
using Kasa.Api.Data;
using Kasa.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>
/// "Ay paketini indir": ayın tüm dökümlerini tek ZIP'te toplar. Her dosya mevcut "Excel'e aktar"
/// yazıcılarıyla (<see cref="CsvRaporlari"/>, <see cref="CsvRaporlariEk"/>) ve yazdırılabilir aylık
/// raporla (<see cref="AylikYazdirma"/>) üretilir; rakamlar ekrandakilerle aynıdır. Muhasebeciye giden
/// belge bilgisi (paket F: belge türü, belge no, fatura bekleniyor, ek sayısı) ayın muhasebeci listesindedir
/// (<see cref="FaturaTakibi.MuhasebeciCsv"/>, /api/disaaktar/muhasebeci.csv ile aynı).
/// </summary>
public static class AyPaketi
{
    public const string IcerikTipi = "application/zip";

    public static string DosyaAdi(int yil, int ay) => $"kasa-ay-paketi-{yil:D4}-{ay:D2}.zip";

    /// <summary>Paketteki dosya adları (ay son eki ile), sırasıyla.</summary>
    public static readonly string[] DosyaKokleri =
    [
        "islemler", "haftalik", "aylik", "kasa-dokumu", "gelenler", "cekler", "kart-odemeleri", "kasa-sayimlari", "gecmis",
        "muhasebeci",
    ];

    public static byte[] Olustur(KasaDbContext db, RaporServisi rapor, HesapServisi hesap, TimeProvider saat, int yil, int ay)
    {
        var ayBasi = new DateOnly(yil, ay, 1);
        var aySonu = AyBicimi.AySonu(ayBasi);
        var ek = $"-{yil:D4}-{ay:D2}";
        var kartAdlari = db.KrediKartlari.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);

        var islemler = db.Islemler.AsNoTracking().Where(i => i.Tarih >= ayBasi && i.Tarih <= aySonu)
            .OrderBy(i => i.Tarih).ThenBy(i => i.Id).ToList();
        var haftalik = hesap.Haftalik().Where(o => o.Donem.Yil == yil && o.Donem.Ay == ay).ToList();
        var gelenler = db.Gelenler.AsNoTracking().Where(g => g.DonemStart >= ayBasi && g.DonemStart <= aySonu).ToList()
            .OrderBy(g => g.DonemStart).ThenBy(g => g.Kanal, Metin.Sirala).ToList();
        // Ayda herhangi bir tarihi (düzenleme, vade, işlem) olan çekler.
        var cekler = db.Cekler.AsNoTracking()
            .Where(c => (c.DuzenlemeTarihi >= ayBasi && c.DuzenlemeTarihi <= aySonu)
                        || (c.VadeTarihi >= ayBasi && c.VadeTarihi <= aySonu)
                        || (c.IslemTarihi >= ayBasi && c.IslemTarihi <= aySonu))
            .OrderBy(c => c.VadeTarihi).ThenBy(c => c.Id).ToList();
        var odemeler = db.KartOdemeler.AsNoTracking().Where(o => o.Tarih >= ayBasi && o.Tarih <= aySonu)
            .OrderBy(o => o.Tarih).ThenBy(o => o.Id).ToList();
        var sayimlar = db.KasaSayimlari.AsNoTracking().Where(s => s.Tarih >= ayBasi && s.Tarih <= aySonu)
            .OrderByDescending(s => s.Tarih).ThenByDescending(s => s.Id).ToList();
        var guncel = hesap.KasaTarihlerde(sayimlar.Select(s => s.Tarih));
        var sayimDtolari = sayimlar.Select(s => KasaSayimDto.Olustur(s, guncel.TryGetValue(s.Tarih, out var g) ? g : null)).ToList();
        // Geçmiş: Türkiye saatiyle bu ay içinde yazılan satırlar (UTC aralığı bir gün geniş okunup süzülür).
        var utcBas = ayBasi.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var utcSon = aySonu.AddDays(2).ToDateTime(TimeOnly.MinValue);
        var gecmis = db.Degisiklikler.AsNoTracking().Where(d => d.ZamanUtc >= utcBas && d.ZamanUtc < utcSon)
            .OrderByDescending(d => d.Id).ToList()
            .Where(d => DateOnly.FromDateTime(Saat.Simdi(d.ZamanUtc)) is var t && t >= ayBasi && t <= aySonu)
            .ToList();

        var dosyalar = new List<(string Ad, byte[] Icerik)>
        {
            ("islemler" + ek + ".csv", CsvRaporlari.Islemler(islemler, kartAdlari)),
            ("haftalik" + ek + ".csv", CsvRaporlari.Haftalik(haftalik)),
            ("aylik" + ek + ".csv", CsvRaporlari.Aylik(hesap.Aylik(yil, ay))),
            ("kasa-dokumu" + ek + ".csv", CsvRaporlariEk.KasaDokumu(rapor.AyinKasaDokumu(yil, ay))),
            ("gelenler" + ek + ".csv", CsvRaporlariEk.Gelenler(gelenler)),
            ("cekler" + ek + ".csv", CsvRaporlariEk.Cekler(cekler)),
            ("kart-odemeleri" + ek + ".csv", CsvRaporlariEk.KartOdemeleri(odemeler, kartAdlari)),
            ("kasa-sayimlari" + ek + ".csv", CsvRaporlariEk.KasaSayimlari(sayimDtolari)),
            ("gecmis" + ek + ".csv", CsvRaporlariEk.Gecmis(gecmis)),
            ("muhasebeci" + ek + ".csv", FaturaTakibi.MuhasebeciCsv(islemler, FaturaTakibi.EkSayilari(db, islemler.Select(i => i.Id)), kartAdlari)),
            (AylikYazdirma.DosyaAdi(yil, ay), Encoding.UTF8.GetBytes(AylikYazdirma.Olustur(AylikYazdirma.Topla(db, rapor, hesap, saat, yil, ay)))),
        };
        return Zip(dosyalar, Saat.Simdi(saat.GetUtcNow().UtcDateTime));
    }

    public static byte[] Zip(IEnumerable<(string Ad, byte[] Icerik)> dosyalar, DateTime zaman)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (ad, icerik) in dosyalar)
            {
                var giris = zip.CreateEntry(ad, CompressionLevel.Optimal);
                giris.LastWriteTime = new DateTimeOffset(DateTime.SpecifyKind(zaman, DateTimeKind.Unspecified), TimeSpan.FromHours(3));
                using var s = giris.Open();
                s.Write(icerik);
            }
        }
        return ms.ToArray();
    }
}
