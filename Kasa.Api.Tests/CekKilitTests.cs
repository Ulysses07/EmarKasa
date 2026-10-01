using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Çeklerde ay kilidi (docs/specs/2026-10-01-cekler.md "Ay kilidi"; AyKilidiKurallari): hareket eklenirken ya da silinirken tarihi
/// kapatılmış aydaysa reddedilir; kasayı etkileyen hareketi kapatılmış aydaysa çekin tutarı, kasası, yönü ve türü değiştirilemez,
/// çek silinemez. Vade, konum, not, kişi, banka ve no her zaman değiştirilebilir. Ağustos 2026 kapatılır; bugün 25 Eylül 2026.
/// </summary>
public class CekKilitTests
{
    private static readonly DateOnly Agustos = Month.AddMonths(-1);

    private static async Task Kapat(HttpClient c)
    {
        var kilit = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi", TestContext.Current.CancellationToken))!;
        await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), kilit.Surum, Agustos.Year, Agustos.Month, "Ay tamamlandı"));
    }

    private static CekHareketEntity Tahsilat(DateOnly tarih) => new() { Tur = CekHareketTurleri.Tahsilat, Tarih = tarih, Tutar = 1_000m, KanalId = 1 };

    [Fact]
    public async Task Kapatilmis_aya_hareket_eklenemez_ve_oradaki_hareket_silinemez_acik_aya_eklenir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(), Tahsilat(Agustos.AddDays(10)));
        await Kapat(c);
        await Assert.ThrowsAsync<KilitliDonemException>(() => CekRaporTests.HareketEkle(f, cek.Id, Tahsilat(Agustos.AddDays(20))));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.CekHareketler.Remove(await db.CekHareketler.SingleAsync(TestContext.Current.CancellationToken));
            Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
        }
        await CekRaporTests.HareketEkle(f, cek.Id, Tahsilat(Today));
    }

    [Fact]
    public async Task Kilitli_kasa_hareketi_olan_cekin_tutari_kasasi_yonu_turu_degismez_cek_silinmez_diger_alanlar_degisir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(), Tahsilat(Agustos.AddDays(10)));
        await Kapat(c);
        foreach (var degistir in new Action<CekEntity>[] { x => x.Tutar = 60_000m, x => x.KanalId = 2, x => x.Yon = CekYonleri.Verilen, x => x.Tur = CekTurleri.Senet })
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            degistir(await db.Cekler.SingleAsync(x => x.Id == cek.Id, TestContext.Current.CancellationToken));
            Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
        }
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Cekler.Remove(await db.Cekler.SingleAsync(x => x.Id == cek.Id, TestContext.Current.CancellationToken));
            Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
        }
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var kayit = await db.Cekler.SingleAsync(x => x.Id == cek.Id, TestContext.Current.CancellationToken);
            kayit.VadeTarihi = Today.AddDays(40);
            kayit.Konum = CekKonumlari.BankadaTahsilde;
            kayit.Not = "Bankaya verildi";
            kayit.Kisi = "Ahmet Y.";
            kayit.Banka = "Halk";
            kayit.No = "12346";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Kilitli_ayda_yalniz_kasayi_etkilemeyen_hareketi_olan_cekin_tutari_degisir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(),
            new CekHareketEntity { Tur = CekHareketTurleri.Karsiliksiz, Tarih = Agustos.AddDays(10) });
        await Kapat(c);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        (await db.Cekler.SingleAsync(x => x.Id == cek.Id, TestContext.Current.CancellationToken)).Tutar = 45_000m;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Kilitli_hareketin_tutari_ve_kanali_degismez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(), Tahsilat(Agustos.AddDays(10)));
        await Kapat(c);
        foreach (var degistir in new Action<CekHareketEntity>[] { x => x.Tutar = 2_000m, x => x.KanalId = 2 })
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            degistir(await db.CekHareketler.SingleAsync(TestContext.Current.CancellationToken));
            Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
        }
    }

    [Fact]
    public async Task Kilitli_hareketin_tarihi_acik_aya_tasinamaz()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(), Tahsilat(Agustos.AddDays(10)));
        await Kapat(c);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        (await db.CekHareketler.SingleAsync(TestContext.Current.CancellationToken)).Tarih = Today;
        Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task Acik_aydaki_hareketin_tarihi_kilitli_aya_tasinamaz()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(), Tahsilat(Today));
        await Kapat(c);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        (await db.CekHareketler.SingleAsync(TestContext.Current.CancellationToken)).Tarih = Agustos.AddDays(10);
        Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task Sinir_gunu_31_Agustos_kilitli_1_Eylul_acik()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek());
        await Kapat(c);
        var agustosSonGunu = new DateOnly(Agustos.Year, Agustos.Month, DateTime.DaysInMonth(Agustos.Year, Agustos.Month));
        await Assert.ThrowsAsync<KilitliDonemException>(() => CekRaporTests.HareketEkle(f, cek.Id, Tahsilat(agustosSonGunu)));
        await CekRaporTests.HareketEkle(f, cek.Id, Tahsilat(agustosSonGunu.AddDays(1)));
    }

    [Fact]
    public async Task Kilitli_Karsiliksiz_hareketi_olan_cek_hareket_silme_engeliyle_silinemez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekRaporTests.CekEkle(f, CekVeriModeliTests.Cek(),
            new CekHareketEntity { Tur = CekHareketTurleri.Karsiliksiz, Tarih = Agustos.AddDays(10) });
        await Kapat(c);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        // Karşılıksız kasayı etkilemediği için CekKilitli(cek.Id) false döner, CekEntity kuralı tek başına tetiklenmez; ama
        // çeki silmek (DELETE uç, Görev 5) önce hareketini siler ve o hareket kilitli tarihli olduğu için CekHareketEntity
        // kuralıyla engellenir — çek dolaylı olarak silinemez kalır.
        db.CekHareketler.RemoveRange(db.CekHareketler.Where(h => h.CekId == cek.Id));
        db.Cekler.Remove(await db.Cekler.SingleAsync(x => x.Id == cek.Id, TestContext.Current.CancellationToken));
        Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
    }
}
