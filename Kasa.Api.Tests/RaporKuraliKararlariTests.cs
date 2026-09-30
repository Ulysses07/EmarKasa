using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.MonthlyExpenseTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Kullanıcının rapor kuralı kararları (2026-09-27), API üzerinden. Takip başlangıcı 1 Haziran 2026, bugün 25 Eylül 2026
/// (sabit saat); Eylül açık aydır.
/// K1: takip başlangıcından önce tarihli mevcut giderler olduğu gibi kalır (rakamlar değişmez), raporda uyarıyla işaretlenir.
/// K2: takipli kredi çekimi aylık raporda kanal 'Gelen' ve 'Ay sonucu'ndan çıkar, ayrı 'Kredi girişi' alanında görünür;
/// kasa bakiyesi ve haftalık rapor değişmez; eski (takipsiz) kredi çekimi de aynı alanda görünür.
/// K3: yeni kredi kartı gideri yeni takipteki (aktif) bir karta bağlanmak zorundadır; mevcut kayıtlar aynen kalır ve
/// tutar/not/tarih düzeltmesi engellenmez.
/// </summary>
public class RaporKuraliKararlariTests
{
    private static JsonObject Kanal(JsonNode rapor, string ad) => rapor["kanallar"]!.AsArray().Single(k => (string)k!["kanal"]! == ad)!.AsObject();
    private static decimal Sayi(JsonNode? d) => d!.GetValue<decimal>();

    [Fact]
    public async Task K1_baslangic_oncesi_mevcut_giderler_aynen_kalir_aylik_ve_haftalik_raporda_uyariyla_isaretlenir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f); // takip başlangıcı 1 Haziran, kasa açılışı 1.000
        // Başlangıç öncesi tarihli yeni gider kabul edilmez; canlıdaki eski kayıtlar doğrudan veritabanında.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(new(2026, 5, 20), "Geç girilen", 10m, "MEZAT", GiderTipi.Cari))).StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Islemler.AddRange(
                new IslemEntity { Tarih = new(2026, 5, 20), Cari = "Eski cari", TutarTl = 5_000m, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.Cari },
                new IslemEntity { Tarih = new(2026, 5, 28), Cari = "Eski maaş", TutarTl = 300m, Kanal = KanalEtiketleri.Ortak, Tip = GiderTipi.SabitGider },
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

    /// <summary>K1 adedi kullanıcının kayıtlarını sayar (R3 notu): üç kanala eşit bölünmüş aylık gider ödemesi bir kayıt, takip
    /// başlangıcından önce çekilmiş eski kredinin başlangıç öncesi türetilmiş taksitleri tek kayıt (kredi) sayılır. Tutarlar
    /// bütün satırların toplamıdır; raporun rakamları değişmez.</summary>
    [Fact]
    public async Task K1_uyarisi_kaynak_kayitlari_sayar_bolunmus_gider_ve_eski_kredi_taksitleri_bir_kez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f); // takip başlangıcı 1 Haziran, kasa açılışı 1.000
        var sablon = await Create(c, "Esit", [new(1, 0), new(2, 0), new(3, 0)]); // 100 TL, üç kanala 33,34 / 33,33 / 33,33
        var odeme = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{sablon.Id}/ode", Payment(sablon));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            // Canlıdaki eski kayıt gibi başlangıç öncesine tarihli ödeme; eski kredi Şubat'ta çekilmiş, 5 Mart, 5 Nisan ve
            // 5 Mayıs taksitleri başlangıçtan önce (5 Haziran ve sonrası takip içinde).
            db.Database.ExecuteSqlRaw("UPDATE Islemler SET Tarih = '2026-05-20' WHERE Id = {0}", odeme.IslemId!.Value);
            db.Krediler.Add(new KrediEntity { Ad = "Eski kredi", CekilenTutar = 6_000m, CekimTarihi = new(2026, 2, 10), TaksitSayisi = 12, AylikOdeme = 500m, OdemeGunu = 5, Kanal = "MEZAT", KanalId = 1 });
            db.SaveChanges();
        }

        var haftalik = JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik"))!.AsArray();
        Assert.StartsWith("Takip başlangıcından önce tarihli 2 kayıt, toplam 1.600,00 ₺ —", (string)haftalik[^1]!["veriSagligiUyarisi"]!);
        var mayis = JsonNode.Parse(await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=5"))!;
        Assert.StartsWith("Takip başlangıcından önce tarihli 2 kayıt, toplam 600,00 ₺ —", (string)mayis["veriSagligiUyarisi"]!);
        Assert.Equal(-33.34m - 500m, Sayi(Kanal(mayis, "MEZAT")["aySonucu"]));
        var mart = JsonNode.Parse(await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=3"))!;
        Assert.StartsWith("Takip başlangıcından önce tarihli 1 kayıt, toplam 500,00 ₺ —", (string)mart["veriSagligiUyarisi"]!);
    }

    [Fact]
    public async Task K2_takipli_ve_eski_kredi_girisi_aylik_raporda_ayri_alanda_kasa_ve_haftalik_degismez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f); // kasa açılışı 1.000
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

    private const string K3Iletisi = "Kredi kartı gideri için yeni takipteki bir kart seçin. Kart eski takipteyse önce kart ekranından yeni takibe geçirin.";

    private static async Task<(int Takipli, int Eski, int DigerEski)> Kartlar(KasaWebFactory f, HttpClient c)
    {
        var takipli = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Takipli kart", 10_000m, 5, 25, new(2026, 6, 1), 0m, []));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var eski = new KrediKartiEntity { Ad = "Eski kart", KesimTarihi = new(2026, 1, 10), SonOdemeTarihi = new(2026, 1, 20), Limit = 5_000m };
        var diger = new KrediKartiEntity { Ad = "Diğer eski kart", KesimTarihi = new(2026, 1, 12), SonOdemeTarihi = new(2026, 1, 22), Limit = 5_000m };
        db.KrediKartlari.AddRange(eski, diger);
        db.SaveChanges();
        return (takipli.Id, eski.Id, diger.Id);
    }

    private static async Task K3Reddi(HttpResponseMessage yanit)
    {
        var govde = await yanit.Content.ReadAsStringAsync();
        Assert.True(yanit.StatusCode == HttpStatusCode.BadRequest, $"{yanit.StatusCode}: {govde}");
        Assert.Equal(K3Iletisi, (string)JsonNode.Parse(govde)!["errors"]!["krediKartiId"]![0]!);
    }

    private static async Task<T> Put<T>(HttpClient c, string yol, object govde)
    {
        var r = await c.PutAsJsonAsync(yol, govde);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task K3_yeni_kredi_karti_gideri_ve_kartli_alis_odemesi_takipteki_karta_baglanmak_zorundadir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var (takipli, eski, _) = await Kartlar(f, c);
        var gun = new DateOnly(2026, 9, 20);

        // Genel gider: kartsız K.K ve eski (takipsiz) karta bağlı gider reddedilir.
        await K3Reddi(await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(gun, "Kartsız", 100m, "MEZAT", GiderTipi.KrediKarti)));
        await K3Reddi(await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(gun, "Eski kartla", 100m, "MEZAT", GiderTipi.KrediKarti, KrediKartiId: eski)));
        await K3Reddi(await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(gun, "Tipsiz eski kart", 100m, KanalEtiketleri.Ortak, GiderTipi.Cari, KrediKartiId: eski)));
        // Takipteki kart ve kartsız diğer giderler kabul edilir.
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(gun, "Takipli kartla", 100m, "MEZAT", GiderTipi.KrediKarti, KrediKartiId: takipli));
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(gun, "Nakit", 50m, "MEZAT", GiderTipi.Cari));

        // Alış ödemesi: eski kartla yeni ödeme reddedilir, takipteki kartla ve nakit kabul edilir.
        var alis = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, gun, "Tedarikçi", null, [new("Mal", 300m, [new(1, 300m)])]));
        await K3Reddi(await c.PostAsJsonAsync($"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), gun, 100m, eski)));
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), gun, 100m, takipli));
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), gun, 100m));
        Assert.Equal(200m, alis.Odenen);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.DoesNotContain(db.Islemler, i => i.Tip == GiderTipi.KrediKarti && i.KrediKartiId != takipli);
        Assert.Equal(2, db.Islemler.Count(i => i.KrediKartiId == takipli));
    }

    [Fact]
    public async Task K3_mevcut_kartsiz_ve_eski_kartli_kayitlar_aynen_kalir_tutar_ve_not_guncellenebilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var (_, eski, diger) = await Kartlar(f, c);
        int kartsiz, eskiKartli, nakit;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var a = new IslemEntity { Tarih = new(2026, 8, 5), Cari = "Kartsız eski", TutarTl = 100m, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.KrediKarti };
            var b = new IslemEntity { Tarih = new(2026, 8, 6), Cari = "Eski kartlı", TutarTl = 200m, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.KrediKarti, KrediKartiId = eski };
            var n = new IslemEntity { Tarih = new(2026, 8, 7), Cari = "Nakit", TutarTl = 300m, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.Cari };
            db.Islemler.AddRange(a, b, n);
            db.SaveChanges();
            (kartsiz, eskiKartli, nakit) = (a.Id, b.Id, n.Id);
        }
        // Tutar, not ve tarih düzeltmesi: kart ve tip aynı kaldıkça kabul edilir.
        (await c.PutAsJsonAsync($"/api/islemler/{kartsiz}", new IslemYazDto(new(2026, 8, 8), "Kartsız eski", 110m, "MEZAT", GiderTipi.KrediKarti, "Dekont"))).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync($"/api/islemler/{eskiKartli}", new IslemYazDto(new(2026, 8, 6), "Eski kartlı", 250m, "MEZAT", GiderTipi.KrediKarti, "Ekstre", eski))).EnsureSuccessStatusCode();
        // Yeni K.K bağı kurmak (başka eski kart, nakit gideri K.K yapmak) yeni kredi kartı gideri sayılır: reddedilir.
        await K3Reddi(await c.PutAsJsonAsync($"/api/islemler/{eskiKartli}", new IslemYazDto(new(2026, 8, 6), "Eski kartlı", 250m, "MEZAT", GiderTipi.KrediKarti, null, diger)));
        await K3Reddi(await c.PutAsJsonAsync($"/api/islemler/{nakit}", new IslemYazDto(new(2026, 8, 7), "Nakit", 300m, "MEZAT", GiderTipi.KrediKarti)));
        await K3Reddi(await c.PutAsJsonAsync($"/api/islemler/{nakit}", new IslemYazDto(new(2026, 8, 7), "Nakit", 300m, "MEZAT", GiderTipi.Cari, null, eski)));

        // Alışa bağlanmış eski kartlı ödeme (mevcut gider bağlanır): tutar düzeltmesi kabul, başka eski karta taşıma reddedilir.
        var alis = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, new(2026, 8, 6), "Tedarikçi", null, [new("Mal", 500m, [new(1, 500m)])]));
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), new(2026, 8, 6), 250m, eski, eskiKartli));
        var odeme = alis.Odemeler.Single();
        await K3Reddi(await c.PutAsJsonAsync($"/api/alis/{alis.Id}/odemeler/{odeme.Id}", new AlisOdemeDuzelt(alis.Surum, Guid.NewGuid(), new(2026, 8, 6), 250m, "Kart değişti", diger)));
        alis = await Put<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler/{odeme.Id}", new AlisOdemeDuzelt(alis.Surum, Guid.NewGuid(), new(2026, 8, 6), 240m, "Tutar düzeltildi", eski));
        Assert.Equal(240m, alis.Odenen);

        using var kontrol = f.Services.CreateScope();
        var son = kontrol.ServiceProvider.GetRequiredService<KasaDbContext>();
        var k = son.Islemler.Single(i => i.Id == kartsiz);
        var e = son.Islemler.Single(i => i.Id == eskiKartli);
        var n2 = son.Islemler.Single(i => i.Id == nakit);
        Assert.Equal((GiderTipi.KrediKarti, (int?)null, 110m, "Dekont"), (k.Tip, k.KrediKartiId, k.TutarTl, k.Not));
        Assert.Equal((GiderTipi.KrediKarti, (int?)eski, 240m), (e.Tip, e.KrediKartiId, e.TutarTl));
        Assert.Equal((GiderTipi.Cari, (int?)null, 300m), (n2.Tip, n2.KrediKartiId, n2.TutarTl));
    }
}
