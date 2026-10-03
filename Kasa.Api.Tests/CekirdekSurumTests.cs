using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// contract-6: çekirdek kasa kayıtlarının (gider, gelir, kanal, ayarlar) iyimser eşzamanlılığı. Kayıt her değiştiğinde sürümü artar
/// (uç yazımı da dolaylı yazım da); yazma isteği okuduğu sürümü gönderirse uyuşmazlıkta 409 alır ve kayıt değişmez. Sürüm
/// göndermeyen eski istemcinin düzenleme isteği denetlenmez, son yazan kazanır, sürüm yine artar. Gider silme sürümü zorunlu
/// tutar. Bu dosya dışındaki API testleri yazma gövdesinde sürüm göndermez; onlar eski istemci düzenleme yolunu sınar.
/// </summary>
public class CekirdekSurumTests
{
    private static readonly DateOnly Baslangic = new(2026, 9, 1);
    private const string GiderIletisi = "Gider başka bir oturumda değişti. Listeyi yenileyip tekrar deneyin.";
    private const string KanalIletisi = "Kanal başka bir oturumda değişti. Listeyi yenileyip tekrar deneyin.";
    private const string AyarIletisi = "Ayarlar başka bir oturumda değişti. Güncel değerleri yükleyip tekrar deneyin.";
    private const string GelenIletisi = "Bu dönem ve kanalın geliri başka bir oturumda değişti. Güncel toplamı yükleyip tekrar deneyin.";

    private static async Task<(KasaWebFactory F, HttpClient C)> Kur()
    {
        var f = KasaWebFactory.Sabit(KasaWebFactory.VarsayilanBugun);
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Baslangic, kasaAcilisDevri = 0m })).EnsureSuccessStatusCode();
        return (f, c);
    }

    /// <summary>Gider gövdesi; sürüm yalnız verilince yazılır (verilmezse alan hiç yoktur: eski istemci).</summary>
    private static Dictionary<string, object?> Gider(decimal tutar, string? not = null, int? surum = null, string kanal = "MEZAT")
    {
        var govde = new Dictionary<string, object?>
        {
            ["tarih"] = "2026-09-10",
            ["cari"] = "Kira",
            ["tutarTl"] = tutar,
            ["kanal"] = kanal,
            ["tip"] = "Cari",
            ["not"] = not,
        };
        if (surum is { } s)
            govde["surum"] = s;
        return govde;
    }

    private static Dictionary<string, object?> Gelen(decimal tutar, int? surum = null, string donem = "2026-09-01")
    {
        var govde = new Dictionary<string, object?> { ["donemStart"] = donem, ["kanal"] = "MEZAT", ["tutarTl"] = tutar };
        if (surum is { } s)
            govde["surum"] = s;
        return govde;
    }

    private static async Task<JsonNode> Json(HttpResponseMessage yanit)
    {
        using (yanit)
        {
            var metin = await yanit.Content.ReadAsStringAsync();
            Assert.True(yanit.IsSuccessStatusCode, $"{yanit.StatusCode}: {metin}");
            return JsonNode.Parse(metin)!;
        }
    }

    private static async Task Cakisma(HttpResponseMessage yanit, string ileti)
    {
        using (yanit)
        {
            Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
            Assert.Equal(ileti, JsonNode.Parse(await yanit.Content.ReadAsStringAsync())!["hata"]!.GetValue<string>());
        }
    }

    private static int Surum(JsonNode? n) => n!["surum"]!.GetValue<int>();

    [Fact]
    public async Task Gider_silme_guncel_surumu_ister_eskimis_istek_kaydi_korur()
    {
        var ct = TestContext.Current.CancellationToken;
        var (f, c) = await Kur();
        await using var _ = f;
        using var __ = c;
        var olusan = await Json(await c.PostAsJsonAsync("/api/islemler", Gider(1000m), cancellationToken: ct));
        var id = olusan["id"]!.GetValue<int>();

        Assert.Equal(HttpStatusCode.BadRequest, (await c.DeleteAsync($"/api/islemler/{id}", ct)).StatusCode);
        var guncel = await Json(await c.PutAsJsonAsync($"/api/islemler/{id}", Gider(1200m, surum: 0), cancellationToken: ct));
        await Cakisma(await c.DeleteAsync($"/api/islemler/{id}?surum=0", ct), GiderIletisi);

        var satir = Assert.Single((await c.GetFromJsonAsync<JsonArray>("/api/islemler", cancellationToken: ct))!);
        Assert.Equal((id, 1200m, Surum(guncel)),
            (satir!["id"]!.GetValue<int>(), satir["tutarTl"]!.GetValue<decimal>(), Surum(satir)));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/islemler/{id}?surum={Surum(guncel)}", ct)).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<JsonArray>("/api/islemler", cancellationToken: ct))!);
    }

    /// <summary>Bulgunun senaryosu: web gideri 1.000'den 1.200'e düzeltir; masaüstünde eski listeden açılmış aynı gider yalnız notla
    /// kaydedilince tutar 1.000'e geri yazılmaz, 409 döner.</summary>
    [Fact]
    public async Task Gider_eski_surumle_kaydedilemez_duzeltilen_tutar_geri_yazilmaz()
    {
        var (f, c) = await Kur();
        await using var _ = f;
        using var __ = c;
        var olusan = await Json(await c.PostAsJsonAsync("/api/islemler", Gider(1000m), cancellationToken: TestContext.Current.CancellationToken));
        var id = olusan["id"]!.GetValue<int>();
        Assert.Equal(0, Surum(olusan));

        var web = await Json(await c.PutAsJsonAsync($"/api/islemler/{id}", Gider(1200m, surum: 0), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((1200m, 1), (web["tutarTl"]!.GetValue<decimal>(), Surum(web)));

        await Cakisma(await c.PutAsJsonAsync($"/api/islemler/{id}", Gider(1000m, "yalnız not", surum: 0), cancellationToken: TestContext.Current.CancellationToken), GiderIletisi);
        var satir = Assert.Single((await c.GetFromJsonAsync<JsonArray>("/api/islemler?baslangic=2026-09-01&bitis=2026-09-30", cancellationToken: TestContext.Current.CancellationToken))!)!;
        Assert.Equal((1200m, 1), (satir["tutarTl"]!.GetValue<decimal>(), Surum(satir)));
        Assert.Null(satir["not"]);
        Assert.Equal(-1200m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel", cancellationToken: TestContext.Current.CancellationToken))!.GuncelKasa);

        // Güncel sürümle kayıt geçer; içeriği değişmeyen kayıt sürümü artırmaz.
        var guncel = await Json(await c.PutAsJsonAsync($"/api/islemler/{id}", Gider(1200m, "yalnız not", surum: 1), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(("yalnız not", 2), (guncel["not"]!.GetValue<string>(), Surum(guncel)));
        Assert.Equal(2, Surum(await Json(await c.PutAsJsonAsync($"/api/islemler/{id}", Gider(1200m, "yalnız not", surum: 2), cancellationToken: TestContext.Current.CancellationToken))));

        // Eski istemci (sürüm göndermez) kırılmaz: son yazan kazanır, sürüm yine artar.
        var eski = await Json(await c.PutAsJsonAsync($"/api/islemler/{id}", Gider(900m, "eski masaüstü"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((900m, 3), (eski["tutarTl"]!.GetValue<decimal>(), Surum(eski)));

        // 409 alan istek denetim olayı yazmaz; olaylarda sürüm alanı yoktur.
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var olaylar = db.DenetimOlaylari.AsNoTracking().Where(o => o.Varlik == "Islem").OrderBy(o => o.Id).ToList();
        Assert.Equal(["Ekle", "Degistir", "Degistir", "Degistir"], olaylar.Select(o => o.Tur));
        Assert.All(olaylar, o => Assert.DoesNotContain("Surum", (o.OncekiJson ?? "") + o.YeniJson));
    }

    [Fact]
    public async Task Kanal_eski_surumle_kaydedilemez_surumsuz_eski_istemci_kaydeder()
    {
        var ct = TestContext.Current.CancellationToken;
        var (f, c) = await Kur();
        await using var _ = f;
        using var __ = c;
        var kanal = await Json(await c.PostAsJsonAsync("/api/kanallar", new { ad = "ONLINE", aktif = true, sira = 5, acilisDevri = 0m }, cancellationToken: ct));
        var id = kanal["id"]!.GetValue<int>();
        Assert.Equal(0, Surum(kanal));
        Assert.All((await c.GetFromJsonAsync<JsonArray>("/api/kanallar", cancellationToken: ct))!, k => Assert.Equal(0, Surum(k)));

        var ilk = await Json(await c.PutAsJsonAsync($"/api/kanallar/{id}", new { ad = "ONLINE", aktif = true, sira = 6, acilisDevri = 0m, surum = 0 }, cancellationToken: ct));
        Assert.Equal((6, 1), (ilk["sira"]!.GetValue<int>(), Surum(ilk)));
        await Cakisma(await c.PutAsJsonAsync($"/api/kanallar/{id}", new { ad = "ONLINE", aktif = false, sira = 5, acilisDevri = 0m, surum = 0 }, cancellationToken: ct), KanalIletisi);
        var okunan = (await c.GetFromJsonAsync<JsonArray>("/api/kanallar", cancellationToken: ct))!.Single(k => k!["id"]!.GetValue<int>() == id)!;
        Assert.Equal((true, 6, 1), (okunan["aktif"]!.GetValue<bool>(), okunan["sira"]!.GetValue<int>(), Surum(okunan)));

        var eski = await Json(await c.PutAsJsonAsync($"/api/kanallar/{id}", new { ad = "ONLINE", aktif = false, sira = 5, acilisDevri = 0m }, cancellationToken: ct));
        Assert.Equal((false, 2), (eski["aktif"]!.GetValue<bool>(), Surum(eski)));
    }

    [Fact]
    public async Task Ayarlar_eski_surumle_kaydedilemez_izleyici_sifresi_surumu_artirmaz()
    {
        var (f, c) = await Kur();
        await using var _ = f;
        using var __ = c;
        var surum = Surum(await c.GetFromJsonAsync<JsonNode>("/api/ayarlar", cancellationToken: TestContext.Current.CancellationToken));

        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Baslangic, kasaAcilisDevri = 500m, surum }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        var okunan = (await c.GetFromJsonAsync<JsonNode>("/api/ayarlar", cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal((500m, surum + 1), (okunan["kasaAcilisDevri"]!.GetValue<decimal>(), Surum(okunan)));

        await Cakisma(await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Baslangic, kasaAcilisDevri = 0m, surum }, cancellationToken: TestContext.Current.CancellationToken), AyarIletisi);
        Assert.Equal(500m, (await c.GetFromJsonAsync<JsonNode>("/api/ayarlar", cancellationToken: TestContext.Current.CancellationToken))!["kasaAcilisDevri"]!.GetValue<decimal>());

        // İzleyici şifresi ayrı formdur: açık başlangıç formunun sürümünü eskitmez.
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi-1" }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        Assert.Equal(surum + 1, Surum(await c.GetFromJsonAsync<JsonNode>("/api/ayarlar", cancellationToken: TestContext.Current.CancellationToken)));

        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Baslangic, kasaAcilisDevri = 250m }, cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        okunan = (await c.GetFromJsonAsync<JsonNode>("/api/ayarlar", cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal((250m, surum + 2), (okunan["kasaAcilisDevri"]!.GetValue<decimal>(), Surum(okunan)));
    }

    /// <summary>Gelir upsert'ü ham SQL'dir: sürüm SQL'de artar. Yeni satır 1 ile eklenir; satırı görmeden (0) kaydeden ikinci oturum
    /// arada eklenmiş satırın üzerine yazamaz.</summary>
    [Fact]
    public async Task Gelen_eski_surumle_ve_gorulmeyen_satirin_uzerine_kaydedilemez()
    {
        var (f, c) = await Kur();
        await using var _ = f;
        using var __ = c;
        var ilk = await Json(await c.PutAsJsonAsync("/api/gelenler", Gelen(1000m, surum: 0), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((1000m, 1), (ilk["tutarTl"]!.GetValue<decimal>(), Surum(ilk)));
        // Aynı dönem ve kanal için satırı görmeden (0) giren ikinci oturum.
        await Cakisma(await c.PutAsJsonAsync("/api/gelenler", Gelen(700m, surum: 0), cancellationToken: TestContext.Current.CancellationToken), GelenIletisi);

        var ikinci = await Json(await c.PutAsJsonAsync("/api/gelenler", Gelen(1200m, surum: 1), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((1200m, 2), (ikinci["tutarTl"]!.GetValue<decimal>(), Surum(ikinci)));
        await Cakisma(await c.PutAsJsonAsync("/api/gelenler", Gelen(1000m, surum: 1), cancellationToken: TestContext.Current.CancellationToken), GelenIletisi);
        var satir = Assert.Single((await c.GetFromJsonAsync<JsonArray>("/api/gelenler?donemStart=2026-09-01", cancellationToken: TestContext.Current.CancellationToken))!)!;
        Assert.Equal((1200m, 2), (satir["tutarTl"]!.GetValue<decimal>(), Surum(satir)));

        var eski = await Json(await c.PutAsJsonAsync("/api/gelenler", Gelen(1500m), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((1500m, 3), (eski["tutarTl"]!.GetValue<decimal>(), Surum(eski)));

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(["Ekle", "Degistir", "Degistir"], db.DenetimOlaylari.AsNoTracking().Where(o => o.Varlik == "Gelen").OrderBy(o => o.Id).Select(o => o.Tur).ToList());
    }

    /// <summary>Dolaylı yazımlar da sürümü artırır: kanal adı değişikliğinin etiket senkronu gider ve gelir satırlarının, alış ödemesi
    /// düzeltmesi bağlı giderin sürümünü artırır. Önceden okunmuş gider eski adla geri yazılamaz.</summary>
    [Fact]
    public async Task Dolayli_yazimlar_surumu_artirir_onceden_okunan_kayit_409_alir()
    {
        var ct = TestContext.Current.CancellationToken;
        var (f, c) = await Kur();
        await using var _ = f;
        using var __ = c;
        var gider = await Json(await c.PostAsJsonAsync("/api/islemler", Gider(100m), cancellationToken: ct));
        var giderId = gider["id"]!.GetValue<int>();
        await Json(await c.PutAsJsonAsync("/api/gelenler", Gelen(1000m, surum: 0), cancellationToken: ct));
        var mezat = (await c.GetFromJsonAsync<JsonArray>("/api/kanallar", cancellationToken: ct))!.Single(k => k!["ad"]!.GetValue<string>() == "MEZAT")!;

        await Json(await c.PutAsJsonAsync($"/api/kanallar/{mezat["id"]}", new { ad = "MEZAT SALONU", aktif = true, sira = 0, acilisDevri = 0m, surum = Surum(mezat) }, cancellationToken: ct));
        var satir = Assert.Single((await c.GetFromJsonAsync<JsonArray>("/api/islemler?baslangic=2026-09-01&bitis=2026-09-30", cancellationToken: ct))!)!;
        Assert.Equal(("MEZAT SALONU", 1), (satir["kanal"]!.GetValue<string>(), Surum(satir)));
        var gelen = Assert.Single((await c.GetFromJsonAsync<JsonArray>("/api/gelenler?donemStart=2026-09-01", cancellationToken: ct))!)!;
        Assert.Equal(("MEZAT SALONU", 2), (gelen["kanal"]!.GetValue<string>(), Surum(gelen)));
        await Cakisma(await c.PutAsJsonAsync($"/api/islemler/{giderId}", Gider(150m, surum: 0, kanal: "MEZAT SALONU"), cancellationToken: ct), GiderIletisi);

        // Alış ödemesi düzeltmesi bağlı gideri (tutar) değiştirir: sürümü artar.
        await AlisIsAkisiTests.Prepare(c);
        var alis = await AlisIsAkisiTests.Read<AlisDto>(await c.PostAsJsonAsync("/api/alis", new AlisYaz(0, Baslangic, "Firma", null, [new("Ürün", 100m, [])]), cancellationToken: ct));
        alis = await AlisIsAkisiTests.Read<AlisDto>(await c.PostAsJsonAsync($"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Baslangic, 40m), cancellationToken: ct));
        var odeme = alis.Odemeler.Single();
        int OdemeGideriSurumu()
        {
            using var scope = f.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<KasaDbContext>().Islemler.AsNoTracking().Single(i => i.Id == odeme.IslemId).Surum;
        }
        Assert.Equal(0, OdemeGideriSurumu());
        await AlisIsAkisiTests.Read<AlisDto>(await c.PutAsJsonAsync($"/api/alis/{alis.Id}/odemeler/{odeme.Id}",
            new AlisOdemeDuzelt(alis.Surum, Guid.NewGuid(), Baslangic, 70m, "Tutar yanlış girildi"), cancellationToken: ct));
        Assert.Equal(1, OdemeGideriSurumu());
    }

    /// <summary>Sürüm eşzamanlılık belirtecidir: aynı kaydı okuyan iki bağlamdan ikincisinin kaydı (okuma ile yazma arasına giren
    /// yazım) sürüm çakışmasıyla durur; sessizce üzerine yazmaz.</summary>
    [Fact]
    public async Task Ayni_kaydi_okuyan_ikinci_baglamin_yazimi_surum_cakismasiyla_durur()
    {
        var (f, c) = await Kur();
        await using var _ = f;
        using var __ = c;
        var id = (await Json(await c.PostAsJsonAsync("/api/islemler", Gider(100m), cancellationToken: TestContext.Current.CancellationToken)))["id"]!.GetValue<int>();
        using var s1 = f.Services.CreateScope();
        using var s2 = f.Services.CreateScope();
        var a = s1.ServiceProvider.GetRequiredService<KasaDbContext>();
        var b = s2.ServiceProvider.GetRequiredService<KasaDbContext>();
        var ia = a.Islemler.Single(i => i.Id == id);
        var ib = b.Islemler.Single(i => i.Id == id);
        ia.TutarTl = 120m;
        a.SaveChanges();
        Assert.Equal(1, ia.Surum);
        ib.Not = "geç kalan";
        Assert.Throws<DbUpdateConcurrencyException>(() => b.SaveChanges());
        using var s3 = f.Services.CreateScope();
        var son = s3.ServiceProvider.GetRequiredService<KasaDbContext>().Islemler.AsNoTracking().Single(i => i.Id == id);
        Assert.Equal((120m, (string?)null, 1), (son.TutarTl, son.Not, son.Surum));
    }
}
