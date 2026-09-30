using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Ay bazında Ortak kanal kümesi (core-1, ops-2, gap-tarihsel-spec-ve-emekli-web-2). Tamamlanmış ayın Ortak gider dağılımı ve rapor
/// satırları o ayın kanal kümesiyle sabittir: küme, Ortak kümesini değiştiren ilk kanal değişikliğinden (aktif kanal ekleme,
/// aktiflik, sıra, aktif kanal silme) ÖNCE yazma yolunda ve ay kapatılırken dondurulur. Kanal eklemek ya da pasife almak yalnız
/// açık ayları etkiler; kilit varken de serbesttir (açılış devri hariç). Bugün 25 Eylül 2026 (sabit saat), takip başlangıcı
/// Haziran 2026 başı, geçen ay Ağustos 2026.
/// </summary>
public class KanalKumesiTests
{
    private static DateOnly Old => Month.AddMonths(-1);
    private static string AylikUrl(DateOnly ay) => $"/api/rapor/aylik?yil={ay.Year}&ay={ay.Month}";

    [Fact]
    public async Task Kilit_yokken_aktif_kanal_eklemek_ve_pasife_almak_gecen_ayin_raporunu_degistirmez_bu_ay_yeni_kumeyle_bolunur()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old.AddDays(9), "Ortak kira", 300m, KanalEtiketleri.Ortak, GiderTipi.SabitGider));
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Today, "Bu ayın ortak gideri", 90m, KanalEtiketleri.Ortak, GiderTipi.Cari));
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Old, "MEZAT", 1_000m), cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var once = await c.GetStringAsync(AylikUrl(Old), TestContext.Current.CancellationToken);
        Assert.Equal(100m, OrtakPay(once, "MEZAT"));
        // Okuma yolu yazmaz: rapor okumak kümeyi dondurmaz.
        Assert.Equal(0, KumeSayisi(f));

        // Önceden: ONLINE Ağustos'un Ortak giderinden 75 alıyor, üç kanalın Ağustos sonucu 25'er artıyordu (core-1 senaryosu).
        using (var r = await c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("ONLINE", true, 3), cancellationToken: TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(once, await c.GetStringAsync(AylikUrl(Old), TestContext.Current.CancellationToken));
        var buAy = await c.GetStringAsync(AylikUrl(Month), TestContext.Current.CancellationToken);
        Assert.Equal(new[] { ("MEZAT", 22.5m), ("PERAKENDE", 22.5m), ("TOPTAN", 22.5m), ("ONLINE", 22.5m) }, OrtakPaylari(buAy));

        // Pasife alınan PERAKENDE geçen ayın payını almaya devam eder; bu ayın Ortak gideri kalan üç aktif kanala bölünür.
        using (var r = await c.PutAsJsonAsync("/api/kanallar/2", new KanalYazDto("PERAKENDE", false, 1), cancellationToken: TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(once, await c.GetStringAsync(AylikUrl(Old), TestContext.Current.CancellationToken));
        Assert.Equal(new[] { ("MEZAT", 30m), ("PERAKENDE", 0m), ("TOPTAN", 30m), ("ONLINE", 30m) }, OrtakPaylari(await c.GetStringAsync(AylikUrl(Month), TestContext.Current.CancellationToken)));

        // Tamamlanmış aylar (Haziran–Ağustos) ilk değişiklikten önceki kümeyle, bir kez dondurulmuştur.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var kumeler = db.AyKanalKumeleri.AsNoTracking().OrderBy(k => k.Yil).ThenBy(k => k.Ay).ToList();
            Assert.Equal(new[] { (2026, 6), (2026, 7), (2026, 8) }, kumeler.Select(k => (k.Yil, k.Ay)).ToArray());
            Assert.All(kumeler, k => Assert.Equal(AyKanalKumesi.KanalDegisikligi, k.Kaynak));
            var agustos = kumeler.Single(k => k.Ay == 8).Id;
            Assert.Equal(new[] { (1, 0, true), (2, 1, true), (3, 2, true) }, db.AyKanalKumesiKanallari.AsNoTracking().Where(u => u.KumeId == agustos)
                .OrderBy(u => u.Sira).Select(u => new { u.KanalId, u.Sira, u.Aktif }).AsEnumerable().Select(u => (u.KanalId, u.Sira, u.Aktif)).ToArray());
        }

        // Kümeden sonra açılan kanalın açık geçmiş aya girilen gideri kendi satırında görünür; Ortak payı almaz, öteki satırlar değişmez.
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old.AddDays(20), "Online reklam", 40m, "ONLINE", GiderTipi.Cari));
        var sonra = JsonNode.Parse(await c.GetStringAsync(AylikUrl(Old), TestContext.Current.CancellationToken))!["kanallar"]!.AsArray();
        var eski = JsonNode.Parse(once)!["kanallar"]!.AsArray();
        Assert.Equal(eski.Count + 1, sonra.Count);
        for (var i = 0; i < eski.Count; i++)
            Assert.Equal(eski[i]!.ToJsonString(), sonra[i]!.ToJsonString());
        var online = sonra[^1]!;
        Assert.Equal(("ONLINE", 40m, 0m, -40m), ((string)online["kanal"]!, (decimal)online["cariGiden"]!, (decimal)online["ortakPay"]!, (decimal)online["aySonucu"]!));
    }

    [Fact]
    public async Task Kilit_altinda_aktif_kanal_eklenir_pasife_alinir_sirasi_degisir_kilitli_ay_acilinca_rapor_birebir_ayni()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old, "Ortak kuruş", 100.01m, KanalEtiketleri.Ortak, GiderTipi.Cari));
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Old, "TOPTAN", 500m), cancellationToken: ct)).EnsureSuccessStatusCode();
        var kilitOncesi = await c.GetStringAsync(AylikUrl(Old), ct);
        Assert.Equal(new[] { ("MEZAT", 33.34m), ("PERAKENDE", 33.34m), ("TOPTAN", 33.33m) }, OrtakPaylari(kilitOncesi));
        await AyKilidi(c, Old, ac: false);
        var dondurulmus = await c.GetStringAsync(AylikUrl(Old), ct);
        // Ay kapatılırken takip başlangıcından kapatılan aya kadar kanal kümesi dondurulur.
        using (var scope = f.Services.CreateScope())
            Assert.Equal(new[] { (6, AyKanalKumesi.AyKapanisi), (7, AyKanalKumesi.AyKapanisi), (8, AyKanalKumesi.AyKapanisi) },
                scope.ServiceProvider.GetRequiredService<KasaDbContext>().AyKanalKumeleri.AsNoTracking().OrderBy(k => k.Ay).AsEnumerable().Select(k => (k.Ay, k.Kaynak)).ToArray());

        // Önceden hepsi 409 idi (Ortak kümesi kilitte değişemezdi). Açılış devri 0 olan aktif kanal eklenir; pasife alma ve sıra serbest.
        using (var r = await c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("E-TİCARET", true, 5), cancellationToken: ct))
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", false), cancellationToken: ct));
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/3", new KanalYazDto("TOPTAN", true, 0), cancellationToken: ct));
        // Açılış devri takip başlangıcından itibaren her haftanın kanal devrini değiştirir: kilitte yine değişmez.
        using (var r = await c.PutAsJsonAsync("/api/kanallar/2", new KanalYazDto("PERAKENDE", true, 1, 99m), cancellationToken: ct))
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        using (var r = await c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("DEVİRLİ", true, 6, 50m), cancellationToken: ct))
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);

        Assert.Equal(dondurulmus, await c.GetStringAsync(AylikUrl(Old), ct));
        using (var scope = f.Services.CreateScope())
            Assert.Equal(kilitOncesi, JsonSerializer.Serialize(scope.ServiceProvider.GetRequiredService<HesapServisi>().Aylik(Old.Year, Old.Month, ct), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        // Ay düzeltme için açılınca rapor canlı hesaplanır ve kapanmadan önce gösterilenle birebir aynıdır.
        await AyKilidi(c, Old, ac: true);
        Assert.Equal(kilitOncesi, await c.GetStringAsync(AylikUrl(Old), ct));
        // Açık ay yeni kümeyle: MEZAT pasif, TOPTAN ilk sırada, E-TİCARET aktif.
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Today, "Kuruş", 0.01m, KanalEtiketleri.Ortak, GiderTipi.Cari));
        var buAy = OrtakPaylari(await c.GetStringAsync(AylikUrl(Month), ct));
        Assert.Equal(0.01m, buAy.Single(p => p.Kanal == "TOPTAN").Pay);
        Assert.Equal(0.01m, buAy.Sum(p => p.Pay));
    }

    [Fact]
    public async Task Kumede_yer_alan_kanal_silinemez_kumeden_sonra_acilan_gecmissiz_kanal_silinir()
    {
        var saat = new SabitSaat(Old.AddDays(19));
        await using var f = new KasaWebFactory { Saat = saat };
        using var c = await Editor(f);
        // Ağustos'ta açılan ONLINE Ağustos'un kümesine girer (Haziran ve Temmuz ondan önce dondurulur).
        var online = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("ONLINE", true, 3));
        saat.Ayarla(Today);

        // Ağustos henüz dondurulmamış ama tamamlanmış: ONLINE onun Ortak kümesinde olduğundan silinmesi Ağustos'un dağılımını değiştirirdi.
        using (var r = await c.DeleteAsync($"/api/kanallar/{online.Id}", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Contains("kanal kümesinde", await Hata(r));
        }
        await AyKilidi(c, Old, ac: false);
        using (var r = await c.DeleteAsync($"/api/kanallar/{online.Id}", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var ileti = await Hata(r);
            Assert.Contains("kanal kümesinde", ileti);
            Assert.Contains("pasifleştirebilirsiniz", ileti);
        }
        // Kilit varken de pasife alınabilir.
        await Basarili(await c.PutAsJsonAsync($"/api/kanallar/{online.Id}", new KanalYazDto("ONLINE", false, 3), cancellationToken: TestContext.Current.CancellationToken));

        // Bütün tamamlanmış aylar dondurulduktan sonra açılan (aktif ya da pasif) geçmişsiz kanal silinir.
        var yeni = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("YENİ", true, 4));
        var pasif = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("PASİF", false, 5));
        await Durum(await c.DeleteAsync($"/api/kanallar/{yeni.Id}", TestContext.Current.CancellationToken), HttpStatusCode.NoContent);
        await Durum(await c.DeleteAsync($"/api/kanallar/{pasif.Id}", TestContext.Current.CancellationToken), HttpStatusCode.NoContent);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.True(db.Kanallar.Any(k => k.Id == online.Id));
        var onlineKumeleri = db.AyKanalKumesiKanallari.AsNoTracking().Where(u => u.KanalId == online.Id).Select(u => u.KumeId).ToList();
        var uyelik = db.AyKanalKumeleri.AsNoTracking().OrderBy(k => k.Ay).ToList().Select(k => (k.Ay, k.Kaynak, onlineKumeleri.Contains(k.Id))).ToArray();
        Assert.Equal(new[] { (6, AyKanalKumesi.KanalDegisikligi, false), (7, AyKanalKumesi.KanalDegisikligi, false), (8, AyKanalKumesi.AyKapanisi, true) }, uyelik);
    }

    [Fact]
    public async Task Ay_kanal_kumesi_degistirilemez_ve_silinemez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("ONLINE", true, 3));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(3, db.AyKanalKumeleri.Count());

        // Veritabanı tetikleyicileri ve kısıtları: küme ve üyeleri güncellenmez, silinmez; kümedeki kanal silinmez.
        foreach (var sql in new[] { "UPDATE AyKanalKumeleri SET Kaynak = 'sahte'", "DELETE FROM AyKanalKumeleri",
                     "UPDATE AyKanalKumesiKanallari SET Aktif = 0", "DELETE FROM AyKanalKumesiKanallari", "DELETE FROM Kanallar WHERE Id = 1" })
            Assert.Equal(19, Assert.Throws<SqliteException>(() => db.Database.ExecuteSqlRaw(sql)).SqliteErrorCode);

        // EF yolu: küme yalnız dondurulurken yazılır; kümedeki kanalın silinmesi kaydetme kuralında da reddedilir.
        db.AyKanalKumeleri.First().Kaynak = "sahte";
        Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        db.AyKanalKumeleri.Add(new AyKanalKumesiEntity { Yil = 2026, Ay = 9, Kaynak = "sahte", Zaman = DateTimeOffset.UnixEpoch });
        Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        db.Kanallar.Remove(db.Kanallar.Single(k => k.Id == 1));
        Assert.Contains("kanal kümesinde", Assert.Throws<KilitliDonemException>(() => db.SaveChanges()).Message);
        db.ChangeTracker.Clear();
        Assert.Equal(3, db.AyKanalKumeleri.AsNoTracking().Count(k => k.Kaynak == AyKanalKumesi.KanalDegisikligi));
    }

    private static decimal OrtakPay(string rapor, string kanal) =>
        (decimal)JsonNode.Parse(rapor)!["kanallar"]!.AsArray().Single(k => (string)k!["kanal"]! == kanal)!["ortakPay"]!;

    private static (string Kanal, decimal Pay)[] OrtakPaylari(string rapor) =>
        JsonNode.Parse(rapor)!["kanallar"]!.AsArray().Select(k => ((string)k!["kanal"]!, (decimal)k["ortakPay"]!)).ToArray();

    private static int KumeSayisi(KasaWebFactory f)
    {
        using var scope = f.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<KasaDbContext>().AyKanalKumeleri.Count();
    }

    private static async Task Basarili(HttpResponseMessage r)
    {
        using (r)
            Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }

    private static async Task Durum(HttpResponseMessage r, HttpStatusCode beklenen)
    {
        using (r)
            Assert.True(r.StatusCode == beklenen, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }

    private static async Task<string> Hata(HttpResponseMessage r) => (string)(await r.Content.ReadFromJsonAsync<JsonObject>())!["hata"]!;

    private static async Task<AyKilidiDto> AyKilidi(HttpClient c, DateOnly ay, bool ac)
    {
        var durum = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi"))!;
        return await Post<AyKilidiDto>(c, ac ? "/api/ay-kilidi/ac" : "/api/ay-kilidi/kapat",
            new AyKilidiYaz(Guid.NewGuid(), durum.Surum, ay.Year, ay.Month, ac ? "Düzeltme için açıldı" : "Ay tamamlandı"));
    }
}
