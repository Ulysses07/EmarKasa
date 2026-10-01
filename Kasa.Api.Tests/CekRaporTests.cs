using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core.Kodlar;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Çek hareketlerinin raporlara etkisi (docs/specs/2026-10-01-cekler.md "Rapora etkisi"): hareketler HesapServisi'nde türetilmiş
/// satıra çevrilir; haftalık, aylık, panel ve kasa dökümü onları kendiliğinden görür. Bugün 25 Eylül 2026 (sabit saat), takip
/// başlangıcı 1 Haziran 2026, açılış kasası 1.000; kanallar MEZAT (1), PERAKENDE (2), TOPTAN (3). Kayıtlar veritabanına doğrudan
/// yazılır (uçlar ayrı görevde sınanır).
/// </summary>
public class CekRaporTests
{
    private static readonly DateOnly Agustos = Month.AddMonths(-1);

    internal static async Task<CekEntity> CekEkle(KasaWebFactory f, CekEntity cek, params CekHareketEntity[] hareketler)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        db.Cekler.Add(cek);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await HareketEkle(f, cek.Id, hareketler);
        return cek;
    }

    internal static async Task HareketEkle(KasaWebFactory f, int cekId, params CekHareketEntity[] hareketler)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var sira = db.CekHareketler.Where(h => h.CekId == cekId).Select(h => (int?)h.Sira).Max() ?? 0;
        foreach (var h in hareketler)
        {
            h.CekId = cekId;
            h.Sira = ++sira;
            db.CekHareketler.Add(h);
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<JsonNode> Json(HttpClient c, string yol) => JsonNode.Parse(await c.GetStringAsync(yol, TestContext.Current.CancellationToken))!;
    private static string Aylik(DateOnly ay) => $"/api/rapor/aylik?yil={ay.Year}&ay={ay.Month}";
    private static JsonNode Kanal(JsonNode rapor, string ad) => rapor["kanallar"]!.AsArray().Single(k => (string)k!["kanal"]! == ad)!;

    [Fact]
    public async Task Tahsilat_gunu_ve_kasasiyla_haftalik_aylik_panel_ve_dokume_girer()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var tahsilat = new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = new(2026, 9, 16), Tutar = 20_000m, KanalId = 1 };
        await CekEkle(f, CekVeriModeliTests.Cek(), tahsilat);

        var hafta = (await Json(c, "/api/rapor/haftalik")).AsArray().Single(h => (string)h!["donem"]!["start"]! == "2026-09-14")!;
        Assert.Equal((20_000m, 20_000m), ((decimal)hafta["toplamGelen"]!, (decimal)Kanal(hafta, "MEZAT")["gelen"]!));
        var eylul = await Json(c, Aylik(Month));
        Assert.Equal((20_000m, 20_000m), ((decimal)Kanal(eylul, "MEZAT")["gelen"]!, (decimal)Kanal(eylul, "MEZAT")["aySonucu"]!));
        var panel = await Panel(c);
        Assert.Equal((21_000m, 20_000m), (panel.GuncelKasa, panel.Kanallar.Single(k => k.KanalId == 1).Bakiye));
        var dokum = (await c.GetFromJsonAsync<KasaHareketleriDto>("/api/kasa-hareketleri?baslangic=2026-09-01", TestContext.Current.CancellationToken))!;
        var satir = Assert.Single(dokum.Hareketler);
        Assert.Equal((KasaHareketTurleri.Cek, "Çek tahsili: Ahmet Yılmaz / 12345", "MEZAT", 20_000m, "Cek:" + tahsilat.Id, false, new DateOnly(2026, 9, 16)),
            (satir.Tur, satir.Aciklama, satir.Kanal, satir.GenelKasaEtkisi, satir.KaynakAnahtari, satir.Otomatik, satir.EtkiTarihi));
    }

    [Fact]
    public async Task Ortak_kasali_verilen_cek_odemesi_cari_gider_olur_aylik_ortak_paya_bolunur()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await CekEkle(f, CekVeriModeliTests.Cek(CekYonleri.Verilen, 30_000.01m, kisi: "Mehmet Ticaret", no: "777"),
            new CekHareketEntity { Tur = CekHareketTurleri.Odeme, Tarih = new(2026, 9, 10), Tutar = 30_000.01m });

        var eylul = await Json(c, Aylik(Month));
        Assert.Equal(new[] { 10_000.01m, 10_000m, 10_000m }, eylul["kanallar"]!.AsArray().Select(k => (decimal)k!["ortakPay"]!));
        var panel = await Panel(c);
        Assert.Equal(1_000m - 30_000.01m, panel.GuncelKasa);
        Assert.All(panel.Kanallar, k => Assert.Equal(0m, k.Bakiye));
        var satir = Assert.Single((await c.GetFromJsonAsync<KasaHareketleriDto>("/api/kasa-hareketleri?baslangic=2026-09-01", TestContext.Current.CancellationToken))!.Hareketler);
        Assert.Equal((KasaHareketTurleri.Cek, "Mehmet Ticaret · Çek ödemesi / 777", KanalEtiketleri.Ortak, -30_000.01m),
            (satir.Tur, satir.Aciklama, satir.Kanal, satir.GenelKasaEtkisi));
    }

    [Fact]
    public async Task Ciro_kasa_ve_ay_sonucunu_degistirmez_donus_iki_ters_satirla_geri_alir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekEkle(f, CekVeriModeliTests.Cek(),
            new CekHareketEntity { Tur = CekHareketTurleri.Ciro, Tarih = new(2026, 9, 15), Tutar = 50_000m, KanalId = 2, Karsi = "Veli Toptan" });
        Assert.Equal(1_000m, (await Panel(c)).GuncelKasa);
        var perakende = Kanal(await Json(c, Aylik(Month)), "PERAKENDE");
        Assert.Equal((50_000m, 50_000m, 0m), ((decimal)perakende["gelen"]!, (decimal)perakende["cariGiden"]!, (decimal)perakende["aySonucu"]!));

        await HareketEkle(f, cek.Id, new CekHareketEntity { Tur = CekHareketTurleri.Donus, Tarih = new(2026, 9, 24), Tutar = 50_000m, KanalId = 2, Karsi = "Veli Toptan" });
        Assert.Equal(1_000m, (await Panel(c)).GuncelKasa);
        perakende = Kanal(await Json(c, Aylik(Month)), "PERAKENDE");
        Assert.Equal((0m, 0m, 0m), ((decimal)perakende["gelen"]!, (decimal)perakende["cariGiden"]!, (decimal)perakende["aySonucu"]!));
        var dokum = (await c.GetFromJsonAsync<KasaHareketleriDto>("/api/kasa-hareketleri?baslangic=2026-09-01", TestContext.Current.CancellationToken))!;
        Assert.Equal(new[] { 50_000m, -50_000m, -50_000m, 50_000m }.Order(), dokum.Hareketler.Select(h => h.GenelKasaEtkisi).Order());
        Assert.All(dokum.Hareketler, h => Assert.Equal(KasaHareketTurleri.Cek, h.Tur));
    }

    [Fact]
    public async Task Kirdirma_kasaya_net_tutari_yazar_kirdirilan_cekin_donusu_tutari_geri_alir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekEkle(f, CekVeriModeliTests.Cek(),
            new CekHareketEntity { Tur = CekHareketTurleri.Kirdirma, Tarih = new(2026, 9, 15), Tutar = 50_000m, NetTutar = 48_750m, KanalId = 1, Karsi = "Faktoring A.Ş." });
        Assert.Equal(1_000m + 48_750m, (await Panel(c)).GuncelKasa);
        var mezat = Kanal(await Json(c, Aylik(Month)), "MEZAT");
        Assert.Equal((50_000m, 1_250m, 48_750m), ((decimal)mezat["gelen"]!, (decimal)mezat["cariGiden"]!, (decimal)mezat["aySonucu"]!));

        await HareketEkle(f, cek.Id, new CekHareketEntity { Tur = CekHareketTurleri.Donus, Tarih = new(2026, 9, 24), Tutar = 50_000m, KanalId = 1, Karsi = "Faktoring A.Ş." });
        Assert.Equal(1_000m - 1_250m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Kapatilmis_ay_dondurulmus_goruntuden_doner_sonraki_hareket_onu_degistirmez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var cek = await CekEkle(f, CekVeriModeliTests.Cek(),
            new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = Agustos.AddDays(19), Tutar = 5_000m, KanalId = 1 });
        var canli = await c.GetStringAsync(Aylik(Agustos), TestContext.Current.CancellationToken);
        Assert.Equal(5_000m, (decimal)Kanal(JsonNode.Parse(canli)!, "MEZAT")["gelen"]!);
        var kilit = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi", TestContext.Current.CancellationToken))!;
        await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), kilit.Surum, Agustos.Year, Agustos.Month, "Ay tamamlandı"));

        await HareketEkle(f, cek.Id, new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = Today, Tutar = 1_000m, KanalId = 1 });
        Assert.Equal(AyRaporuAnlikGoruntusuTests.Dondurulmus(canli, HesapServisi.AcikAyKurali), await c.GetStringAsync(Aylik(Agustos), TestContext.Current.CancellationToken));
        Assert.Equal(1_000m, (decimal)Kanal(await Json(c, Aylik(Month)), "MEZAT")["gelen"]!);
        Assert.Equal(7_000m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Kasasi_cozulemeyen_hareket_raporu_dusurmez_genel_kasaya_yazilir_ve_uyarilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await CekEkle(f, CekVeriModeliTests.Cek(), new CekHareketEntity { Tur = CekHareketTurleri.Tahsilat, Tarih = new(2026, 9, 16), Tutar = 300m });
        Assert.Equal(1_300m, (await Panel(c)).GuncelKasa);
        var eylul = await Json(c, Aylik(Month));
        Assert.Equal(300m, (decimal)eylul["genelGelir"]!);
        Assert.Contains("Çek hareketi #", (string)eylul["veriSagligiUyarisi"]!, StringComparison.Ordinal);
    }
}
