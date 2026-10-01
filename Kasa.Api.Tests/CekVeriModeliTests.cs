using System.Net;
using Kasa.Api.Data;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>Çek veri modeli (migration 20261008000100_Cekler): iki tablo, hareket sırası tekil, çek ya da hareketi olan kanal
/// silinmez (KanalKurallari.SilmeEngeli; veritabanında ON DELETE RESTRICT).</summary>
public class CekVeriModeliTests
{
    internal static CekEntity Cek(string yon = CekYonleri.Alinan, decimal tutar = 50_000m, int? kanalId = null, bool teminat = false,
        string kisi = "Ahmet Yılmaz", string no = "12345", DateOnly? vade = null) => new()
        {
            Tur = CekTurleri.Cek,
            Yon = yon,
            No = no,
            Banka = "Ziraat",
            Kisi = kisi,
            Tutar = tutar,
            VadeTarihi = vade ?? new(2026, 11, 30),
            KanalId = kanalId,
            Teminat = teminat,
            Surum = 1,
        };

    [Fact]
    public async Task Cek_ve_hareketleri_kaydedilir_hareket_sirasi_cek_basina_tekildir()
    {
        await using var f = Fabrika();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var cek = Cek();
        db.Cekler.Add(cek);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.CekHareketler.Add(new CekHareketEntity { CekId = cek.Id, Sira = 1, Tur = CekHareketTurleri.Tahsilat, Tarih = Today, Tutar = 100.01m, KanalId = 1 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var okunan = await db.CekHareketler.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((cek.Id, 1, 100.01m, (decimal?)null, 1), (okunan.CekId, okunan.Sira, okunan.Tutar, okunan.NetTutar, okunan.KanalId));
        db.CekHareketler.Add(new CekHareketEntity { CekId = cek.Id, Sira = 1, Tur = CekHareketTurleri.Iade, Tarih = Today });
        var hata = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Contains("UNIQUE", hata.InnerException!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cek_ya_da_hareketi_olan_kanal_silinmez(bool yalnizHareket)
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var kanal = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("GECICI", true, 9));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var cek = Cek(yalnizHareket ? CekYonleri.Alinan : CekYonleri.Verilen, kanalId: yalnizHareket ? null : kanal.Id);
            db.Cekler.Add(cek);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            if (yalnizHareket)
            {
                db.CekHareketler.Add(new CekHareketEntity { CekId = cek.Id, Sira = 1, Tur = CekHareketTurleri.Tahsilat, Tarih = Today, Tutar = 10m, KanalId = kanal.Id });
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }
        var yanit = await c.DeleteAsync($"/api/kanallar/{kanal.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
        Assert.Contains("olan kanal silinemez", await yanit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }
}
