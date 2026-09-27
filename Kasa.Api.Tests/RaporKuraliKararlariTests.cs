using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.MonthlyExpenseTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Kullanıcının rapor kuralı kararları (2026-09-27), API üzerinden. Takip başlangıcı 1 Haziran 2026, bugün 25 Eylül 2026
/// (sabit saat); Eylül açık aydır.
/// K1: takip başlangıcından önce tarihli mevcut giderler olduğu gibi kalır (rakamlar değişmez), raporda uyarıyla işaretlenir.
/// K2: takipli kredi çekimi aylık raporda kanal 'Gelen' ve 'Ay sonucu'ndan çıkar, ayrı 'Kredi girişi' alanında görünür;
/// kasa bakiyesi ve haftalık rapor değişmez; eski (takipsiz) kredi çekimi de aynı alanda görünür.
/// </summary>
public class RaporKuraliKararlariTests
{
    private static JsonObject Kanal(JsonNode rapor, string ad) => rapor["kanallar"]!.AsArray().Single(k => (string)k!["kanal"]! == ad)!.AsObject();
    private static decimal Sayi(JsonNode? d) => d!.GetValue<decimal>();

    [Fact]
    public async Task K1_baslangic_oncesi_mevcut_giderler_aynen_kalir_aylik_ve_haftalik_raporda_uyariyla_isaretlenir()
    {
        await using var f = Fabrika(); using var c = await Editor(f); // takip başlangıcı 1 Haziran, kasa açılışı 1.000
        // Başlangıç öncesi tarihli yeni gider kabul edilmez; canlıdaki eski kayıtlar doğrudan veritabanında.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(new(2026, 5, 20), "Geç girilen", 10m, "MEZAT", GiderTipi.Cari))).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Islemler.AddRange(
                new IslemEntity { Tarih = new(2026, 5, 20), Cari = "Eski cari", TutarTl = 5_000m, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.Cari },
                new IslemEntity { Tarih = new(2026, 5, 28), Cari = "Eski maaş", TutarTl = 300m, Kanal = Kanallar.Ortak, Tip = GiderTipi.SabitGider },
                // Mayıs K.K'sı Haziran sonunda haftalık kasadan da düşer: tutarlıdır, işaretlenmez.
                new IslemEntity { Tarih = new(2026, 5, 25), Cari = "Eski kart", TutarTl = 700m, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.KrediKarti });
            db.SaveChanges();
        }

        var mayis = JsonNode.Parse(await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=5"))!;
        var mezat = Kanal(mayis, "MEZAT");
        Assert.Equal((5_000m, 100m, -5_100m), (Sayi(mezat["cariGiden"]), Sayi(mezat["ortakPay"]), Sayi(mezat["aySonucu"])));
        Assert.Equal("Takip başlangıcından önce tarihli 2 kayıt, toplam 5.300,00 ₺ — raporlarda farklı işlenir: bu ayın sonucunda sayılır, "
            + "haftalık kasaya ve kanal devrine girmez. Kayıtlar ve tutarlar olduğu gibi korunur.", (string)mayis["veriSagligiUyarisi"]!);
        var haziran = JsonNode.Parse(await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=6"))!;
        Assert.Equal(700m, Sayi(Kanal(haziran, "MEZAT")["krediKarti"]));
        Assert.Null(haziran["veriSagligiUyarisi"]);

        var haftalik = JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik"))!.AsArray();
        Assert.StartsWith("Takip başlangıcından önce tarihli 2 kayıt, toplam 5.300,00 ₺ — raporlarda farklı işlenir: haftalık kasaya", (string)haftalik[^1]!["veriSagligiUyarisi"]!);
        Assert.All(haftalik.Take(haftalik.Count - 1), h => Assert.Null(h!["veriSagligiUyarisi"]));
        // Kasa: başlangıç öncesi gider hiç düşmez, Mayıs K.K'sı Haziran sonunda düşer (davranış aynen korunur).
        Assert.Equal(1_000m - 700m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task K2_takipli_ve_eski_kredi_girisi_aylik_raporda_ayri_alanda_kasa_ve_haftalik_degismez()
    {
        await using var f = Fabrika(); using var c = await Editor(f); // kasa açılışı 1.000
        await Post<KrediTakipDto>(c, "/api/takip/krediler", new KrediTakipYaz(Guid.NewGuid(), "İşletme kredisi", 120_000m, new(2026, 9, 10), new(2026, 10, 10), 12, 11_000m, [1]));
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Month, "MEZAT", 80_000m))).EnsureSuccessStatusCode();
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(new(2026, 9, 15), "Tedarik", 100_000m, "MEZAT", GiderTipi.Cari));
        // Eski (takipsiz) model: yeni oluşturulamaz, canlıdaki geçmiş kayıtlar gibi doğrudan veritabanında.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Krediler.Add(new KrediEntity { Ad = "Eski kredi", CekilenTutar = 50_000m, CekimTarihi = new(2026, 9, 12), TaksitSayisi = 10, AylikOdeme = 5_500m, OdemeGunu = 5, Kanal = "TOPTAN", KanalId = 3 });
            db.SaveChanges();
        }

        var rapor = JsonNode.Parse(await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=9"))!;
        var mezat = Kanal(rapor, "MEZAT");
        Assert.Equal(80_000m, Sayi(mezat["gelen"]));
        Assert.Equal(120_000m, Sayi(mezat["krediGirisi"]));
        Assert.Equal(-20_000m, Sayi(mezat["aySonucu"]));
        Assert.Equal(0m, Sayi(Kanal(rapor, "TOPTAN")["gelen"]));
        Assert.Equal(170_000m, Sayi(rapor["krediGirisi"])); // takipli 120.000 + eski 50.000
        Assert.Equal(2, rapor["kuralSurumu"]!.GetValue<int>());
        Assert.Null(rapor["dondurulmus"]);

        // Panelin "bu ay"ı aylık sonuçtur (kredi hariç); kasa bakiyesi krediyi nakit olarak içerir.
        var panel = await Panel(c);
        Assert.Equal(-20_000m, panel.BuAySonucu);
        Assert.Equal(1_000m + 80_000m + 120_000m + 50_000m - 100_000m, panel.GuncelKasa);

        // Haftalık rapor değişmez: kredi dönem geline ve (takipliyse) kanal devrine girer.
        var hafta = JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik"))!.AsArray()
            .Single(h => (string)h!["donem"]!["start"]! == "2026-09-07")!;
        Assert.Equal(120_000m, Sayi(Kanal(hafta, "MEZAT")["krediGirisi"]));
        Assert.Equal(120_000m, Sayi(Kanal(hafta, "MEZAT")["gelen"]));
        Assert.Equal(170_000m, Sayi(hafta["toplamGelen"]));
    }
}
