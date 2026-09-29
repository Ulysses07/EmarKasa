using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kasa.Api.Tests;

public class CardDebtSummaryTests
{
    // Takvim sınırları (tests-1): aynı testler yıl başında, artık yılın Şubat sonunda ve kırpılan ay sonunda da koşar.
    public sealed class YilBasi() : CardDebtSummaryTests(new(2027, 1, 1));
    public sealed class ArtikYilSubatSonu() : CardDebtSummaryTests(new(2028, 2, 29));
    public sealed class KirpilanAySonu() : CardDebtSummaryTests(new(2027, 3, 31));

    public CardDebtSummaryTests() : this(KasaWebFactory.VarsayilanBugun) { }
    private CardDebtSummaryTests(DateOnly bugun) => Today = bugun;
    private DateOnly Today { get; }
    // Takip başlangıcı bugünün ayından 8 ay önce: varsayılan günde 1 Ocak 2026, her günde geçmişte kalır.
    private DateOnly Start => new DateOnly(Today.Year, Today.Month, 1).AddMonths(-8);
    private KasaWebFactory Factory() => KasaWebFactory.Sabit(Today);

    [Fact]
    public async Task Bir_kartin_alacagi_diger_kartin_kanal_borcunu_azaltmaz_ve_okuma_kasayi_degistirmez()
    {
        await using var f = Factory(); using var c = await Editor(f);
        var a = await Charge(c, await Card(c), 100m, [new(1, 60m), new(2, 40m)]);
        a = await Pay(c, a, 10m);
        await Charge(c, await Card(c), 20m, [new(1, 20m)]);
        var credit = await Card(c, -30m);
        Assert.Empty(credit.KanalKartBorclari!);
        var before = await c.GetStringAsync("/api/rapor/panel");
        var summary = (await c.GetFromJsonAsync<TakipOzetDto>("/api/takip/ozet"))!;
        Assert.Equal(110m, summary.KartBorcu);
        Assert.Equal(30m, summary.KartAlacakBakiyesi);
        AssertShares(summary.KanalKartBorclari, (1, 74m), (2, 36m));
        AssertShares(a.KanalKartBorclari, (1, 54m), (2, 36m));
        Assert.Equal(before, await c.GetStringAsync("/api/rapor/panel"));
        Assert.Equal(990m, await Cash(c));
    }

    [Fact]
    public async Task Odenmis_kurus_kalan_borcta_ikinci_kez_ayni_kanala_yazilmaz_iptaller_geri_alinir()
    {
        await using var f = Factory(); using var c = await Editor(f);
        var card = await Charge(c, await Card(c), .02m, [new(1, .01m), new(2, .01m)], 2);
        card = await Pay(c, card, .01m);
        AssertShares(card.KanalKartBorclari, (2, .01m));
        Assert.Equal(1, Assert.Single(Assert.Single(card.Odemeler).Dagilimlar).KanalId);
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler/{card.Odemeler.Single().Id}/iptal",
            new TakipIptalYaz(Guid.NewGuid(), card.Surum, "Hatalı ödeme"));
        AssertShares(card.KanalKartBorclari, (1, .01m), (2, .01m));
        card = await CancelCharge(c, card, card.Harcamalar.Single().Id);
        Assert.Empty(card.KanalKartBorclari!);
        Assert.Equal(0m, card.Borc); Assert.Equal(1000m, await Cash(c));
    }

    [Fact]
    public async Task Iade_yalniz_kendi_kaynak_kanal_borcunu_azaltir_iptal_edilince_geri_gelir()
    {
        await using var f = Factory(); using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, [new(1, 100m)]);
        card = await Charge(c, card, 100m, [new(2, 100m)]);
        var source = card.Harcamalar.Last();
        card = await Refund(c, card, source.Id, 30m);
        AssertShares(card.KanalKartBorclari, (1, 100m), (2, 70m));
        card = await CancelCharge(c, card, card.Harcamalar.Single(h => h.Tutar < 0).Id);
        AssertShares(card.KanalKartBorclari, (1, 100m), (2, 100m));
        card = await CancelCharge(c, card, source.Id);
        AssertShares(card.KanalKartBorclari, (1, 100m));
        Assert.Equal(1000m, await Cash(c));
    }

    [Fact]
    public async Task Kismi_odeme_ardindan_iade_kalan_paylari_ve_odeme_iptali_borcu_dogru_gosterir()
    {
        await using var f = Factory(); using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, [new(1, 60m), new(2, 40m)], 3);
        var source = card.Harcamalar.Single();
        card = await Pay(c, card, 25m);
        card = await Refund(c, card, source.Id, 25m);
        AssertShares(card.KanalKartBorclari, (1, 30m), (2, 20m));
        Assert.Equal(50m, card.Borc); Assert.Equal(975m, await Cash(c));
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler/{card.Odemeler.Single().Id}/iptal",
            new TakipIptalYaz(Guid.NewGuid(), card.Surum, "Hatalı ödeme"));
        AssertShares(card.KanalKartBorclari, (1, 45m), (2, 30m));
        Assert.Equal(1000m, await Cash(c));
    }

    [Fact]
    public async Task Avans_yeni_harcamayi_kapatir_artan_alacak_ayri_kalir_iptal_borcu_acar()
    {
        await using var f = Factory(); using var c = await Editor(f);
        var card = await Pay(c, await Card(c), 50m);
        Assert.Empty(card.KanalKartBorclari!);
        card = await Charge(c, card, 20m, [new(1, 20m)]);
        Assert.Empty(card.KanalKartBorclari!);
        var summary = (await c.GetFromJsonAsync<TakipOzetDto>("/api/takip/ozet"))!;
        Assert.Equal(0m, summary.KartBorcu); Assert.Equal(30m, summary.KartAlacakBakiyesi);
        card = await Charge(c, card, 40m, [new(2, 40m)]);
        AssertShares(card.KanalKartBorclari, (2, 10m));
        Assert.Equal(950m, await Cash(c));
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler/{card.Odemeler.Single().Id}/iptal",
            new TakipIptalYaz(Guid.NewGuid(), card.Surum, "Avans iptali"));
        AssertShares(card.KanalKartBorclari, (1, 20m), (2, 40m));
        Assert.Equal(1000m, await Cash(c));
    }

    [Fact]
    public async Task Acilis_alacagi_yalniz_ayni_kartin_kalan_kaynak_borcundan_duser()
    {
        await using var f = Factory(); using var c = await Editor(f);
        var card = await Charge(c, await Card(c, -25m), 100m, [new(1, 60m), new(2, 40m)]);
        AssertShares(card.KanalKartBorclari, (1, 45m), (2, 30m));
        Assert.Equal(75m, card.Borc); Assert.Equal(1000m, await Cash(c));
    }

    [Fact]
    public async Task Eski_kart_borcu_belirsizdir_geciste_kasada_sayilmis_odeme_de_brut_borcu_azaltir()
    {
        await using var f = Factory(); using var c = await Editor(f);
        int id;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var card = new KrediKartiEntity { Ad = "Eski kart", Borc = 100m, Limit = 1000m,
                KesimTarihi = new(2000, 1, 5), SonOdemeTarihi = new(2000, 1, 25) };
            db.KrediKartlari.Add(card);
            db.KrediKartlari.Add(new() { Ad = "Eski alacak", Borc = -20m, Limit = 1000m,
                KesimTarihi = new(2000, 1, 5), SonOdemeTarihi = new(2000, 1, 25) });
            db.SaveChanges(); id = card.Id;
        }
        var old = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{id}"))!;
        AssertShares(old.KanalKartBorclari, (null, 100m));
        Assert.Equal(Kanallar.DagilimBekliyor, old.KanalKartBorclari!.Single().Kanal);
        var summary = (await c.GetFromJsonAsync<TakipOzetDto>("/api/takip/ozet"))!;
        Assert.Equal(100m, summary.KartBorcu); Assert.Equal(20m, summary.KartAlacakBakiyesi);
        var moved = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/gecis",
            new KartGecisYaz(Guid.NewGuid(), 0, Today, 100m, 100m, [new(1, 60m), new(2, 40m)], "Önceden kasada sayıldı", true));
        moved = await Pay(c, moved, 40m);
        AssertShares(moved.KanalKartBorclari, (1, 36m), (2, 24m));
        Assert.Equal(0m, moved.Odemeler.Single().KasaEtkisi);
        Assert.Equal(1000m, await Cash(c));
    }

    [Fact]
    public async Task Onaysiz_alis_kalan_kart_borcu_belirsizdir_onay_sonrasi_kaynak_payina_gecer()
    {
        await using var f = Factory(); using var c = await Editor(f);
        var card = await Card(c);
        var purchase = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Today, "Mağaza", null,
            [new("Malzeme", 100m, [new(1, 60m), new(2, 40m)])]));
        purchase = await Post<AlisDto>(c, $"/api/alis/{purchase.Id}/odemeler", new AlisOdemeYaz(purchase.Surum, Guid.NewGuid(), Today, 100m, card.Id));
        card = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!;
        card = await Pay(c, card, 25m);
        AssertShares(card.KanalKartBorclari, (null, 75m));
        purchase = await Post<AlisDto>(c, $"/api/alis/{purchase.Id}/gonder", new AlisDurumYaz(purchase.Surum));
        await Post<AlisDto>(c, $"/api/alis/{purchase.Id}/onayla", new AlisDurumYaz(purchase.Surum));
        card = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!;
        AssertShares(card.KanalKartBorclari, (1, 45m), (2, 30m));
        Assert.Equal(975m, await Cash(c));
    }

    [Fact]
    public async Task Asgari_kalan_banka_hedefinden_aktif_odemeyi_duser_iadede_kalan_borcu_asmaz()
    {
        await using var f = Factory(); using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, [new(1, 100m)]);
        var chargeId = card.Harcamalar.Single().Id;
        var statement = card.Ekstreler.Single(s => s.Borc > 0);
        Assert.Null(statement.AsgariKalan);
        card = await Minimum(c, card, statement.Id, 40m);
        Assert.Equal(40m, card.Ekstreler.Single(s => s.Id == statement.Id).AsgariKalan);
        card = await Pay(c, card, 15m, statement.Id);
        Assert.Equal(25m, card.Ekstreler.Single(s => s.Id == statement.Id).AsgariKalan);
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler/{card.Odemeler.Single().Id}/iptal",
            new TakipIptalYaz(Guid.NewGuid(), card.Surum, "Ödeme iptali"));
        Assert.Equal(40m, card.Ekstreler.Single(s => s.Id == statement.Id).AsgariKalan);
        card = await Refund(c, card, chargeId, 80m);
        Assert.Equal(20m, card.Ekstreler.Single(s => s.Id == statement.Id).AsgariKalan);
        card = await CancelCharge(c, card, card.Harcamalar.Single(h => h.Tutar < 0).Id);
        Assert.Equal(40m, card.Ekstreler.Single(s => s.Id == statement.Id).AsgariKalan);
        card = await CancelCharge(c, card, chargeId);
        Assert.Equal(0m, card.Ekstreler.Single(s => s.Id == statement.Id).AsgariKalan);
        Assert.Equal(1000m, await Cash(c));
    }

    [Fact]
    public async Task Asgari_odemeler_yalniz_bagli_ekstrede_sayilir_tam_odeme_sifira_indirir()
    {
        await using var f = Factory(); using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 200m, [new(1, 200m)], 2);
        var statements = card.Ekstreler.Where(s => s.Borc > 0).ToArray();
        card = await Minimum(c, card, statements[0].Id, 40m);
        card = await Minimum(c, card, statements[1].Id, 40m);
        card = await Pay(c, card, 10m, statements[1].Id);
        Assert.Equal(40m, card.Ekstreler.Single(s => s.Id == statements[0].Id).AsgariKalan);
        Assert.Equal(30m, card.Ekstreler.Single(s => s.Id == statements[1].Id).AsgariKalan);
        card = await Pay(c, card, 190m);
        Assert.All(card.Ekstreler.Where(s => s.AsgariOdeme.HasValue), s => Assert.Equal(0m, s.AsgariKalan));
        Assert.Empty(card.KanalKartBorclari!);
        Assert.Equal(800m, await Cash(c));
    }

    [Fact]
    public async Task Iptal_edilen_odeme_sonrasi_iade_kart_ve_raporlari_dusurmez()
    {
        await using var f = Factory(); using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, [new(1, 100m)]);
        var source = card.Harcamalar.Single();
        card = await Pay(c, card, 100m);
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler/{card.Odemeler.Single().Id}/iptal",
            new TakipIptalYaz(Guid.NewGuid(), card.Surum, "Yanlış ödeme"));
        // İptal edilen ödeme kaynağı yeniden açık gösterir; iade o açık tutardan düşer.
        card = await Refund(c, card, source.Id, 60m);
        AssertShares(card.KanalKartBorclari, (1, 40m));
        Assert.Equal(40m, card.Borc);
        var cancelled = Assert.Single(card.Odemeler);
        Assert.True(cancelled.Iptal); Assert.Equal(0m, cancelled.KasaEtkisi); Assert.Empty(cancelled.Dagilimlar);
        foreach (var path in new[] { "/api/rapor/panel", "/api/rapor/haftalik", "/api/takip/kartlar", $"/api/takip/kartlar/{card.Id}", "/api/takip/ozet" })
        {
            var response = await c.GetAsync(path);
            Assert.True(response.IsSuccessStatusCode, $"{path}: {response.StatusCode}");
        }
        Assert.Equal(1000m, await Cash(c));
    }

    [Fact]
    public async Task Bozuk_odeme_payi_kaynak_agirligini_asarsa_fazlasi_dagilim_bekliyor_olur_ve_uyari_loglanir()
    {
        var logs = new UyariToplayici();
        await using var f = new LogluFactory(logs) { Saat = new SabitSaat(Today) }; using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, [new(1, 100m)]);
        card = await Pay(c, card, 50m);
        var payment = card.Odemeler.Single();
        AssertShares(payment.Dagilimlar, (1, 50m));
        Assert.Empty(logs.Uyarilar);
        using (var scope = f.Services.CreateScope())
        {
            // Eski/bozuk veri: ödeme payı kaynak harcamanın 100 TL ağırlığını aşıyor.
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var taxId = db.TakipKartTaksitler.Single().Id;
            db.Database.ExecuteSqlRaw("UPDATE TakipKartOdemeler SET PaylarJson = {0} WHERE Id = {1}",
                $"[{{\"TaksitId\":{taxId},\"Tutar\":150,\"OncedenOdenen\":0}}]", payment.Id);
        }
        var response = await c.GetAsync($"/api/takip/kartlar/{card.Id}");
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var broken = (await response.Content.ReadFromJsonAsync<KartTakipDto>())!;
        var shares = broken.Odemeler.Single().Dagilimlar;
        AssertShares(shares, (1, 100m), (null, 50m));
        Assert.Equal(Kanallar.DagilimBekliyor, shares.Single(p => p.KanalId is null).Kanal);
        Assert.Contains(logs.Uyarilar, m => m.Contains($"ödeme {payment.Id}") && m.Contains("50"));
        foreach (var path in new[] { "/api/rapor/panel", "/api/takip/kartlar", "/api/takip/ozet" })
            Assert.True((await c.GetAsync(path)).IsSuccessStatusCode, path);
    }

    [Fact]
    public async Task Ayni_bozuk_odeme_payi_tekrar_tekrar_hesaplansa_da_uyari_bir_kez_loglanir()
    {
        var logs = new UyariToplayici();
        await using var f = new LogluFactory(logs) { Saat = new SabitSaat(Today) }; using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, [new(1, 100m)]);
        card = await Pay(c, card, 50m);
        var payment = card.Odemeler.Single();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var taxId = db.TakipKartTaksitler.Single().Id;
            db.Database.ExecuteSqlRaw("UPDATE TakipKartOdemeler SET PaylarJson = {0} WHERE Id = {1}",
                $"[{{\"TaksitId\":{taxId},\"Tutar\":170,\"OncedenOdenen\":0}}]", payment.Id);
        }
        // Rapor istekleri ve dakikalık bildirim işçisi aynı ödemeyi her seferinde yeniden hesaplar.
        foreach (var path in new[] { $"/api/takip/kartlar/{card.Id}", $"/api/takip/kartlar/{card.Id}", "/api/takip/kartlar", "/api/rapor/panel" })
            Assert.True((await c.GetAsync(path)).IsSuccessStatusCode, path);
        var broken = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!;
        AssertShares(broken.Odemeler.Single().Dagilimlar, (1, 100m), (null, 70m));
        Assert.Single(logs.Uyarilar, m => m.Contains($"ödeme {payment.Id}:"));
    }

    [Fact]
    public void Kirpma_yalniz_tasan_durumda_devreye_girer_normal_dagilim_birebir_aynidir()
    {
        var random = new Random(20260927);
        for (var i = 0; i < 2000; i++)
        {
            var weights = Enumerable.Range(1, random.Next(1, 5)).Select(k => new KanalPayYaz(k, random.Next(0, 5000) / 100m)).ToList();
            var total = weights.Sum(w => w.Tutar);
            var onceki = random.Next(0, (int)(total * 100) + 1) / 100m;
            var amount = random.Next(0, (int)(total * 100 - onceki * 100) + 1) / 100m;
            var (paylar, tasan) = FinansTakipServisi.KirparakOranla(weights, amount, onceki);
            // Kırpmasız eski yol: sığan her girdide aynı D'Hondt sonucu, taşma sıfır.
            var positive = weights.Where(w => w.Tutar > 0).Select(w => new AlisKanalPayi(w.KanalId, w.Tutar)).ToList();
            List<KanalPayYaz> eski = positive.Count == 0 || amount <= 0 ? [] : AlisDagitici.Dagit(positive, onceki, amount)
                .Where(p => p.Tutar > 0).Select(p => new KanalPayYaz(p.KanalId, p.Tutar)).ToList();
            Assert.Equal(eski, paylar); Assert.Equal(0m, tasan);
        }
        // Taşan girdide eski yol istisna fırlatır; kırpılmış yol sığanı dağıtıp fazlayı döndürür.
        List<KanalPayYaz> source = [new(1, 30m), new(2, 10m)];
        Assert.Throws<ArgumentOutOfRangeException>(() => AlisDagitici.Dagit([new(1, 30m), new(2, 10m)], 5m, 100m));
        var (kirpik, fazla) = FinansTakipServisi.KirparakOranla(source, 100m, 5m);
        Assert.Equal(new KanalPayYaz[] { new(1, 26.25m), new(2, 8.75m) }, kirpik); Assert.Equal(65m, fazla);
        var (bos, tamami) = FinansTakipServisi.KirparakOranla(source, 20m, 50m);
        Assert.Empty(bos); Assert.Equal(20m, tamami);
    }

    private sealed class LogluFactory(UyariToplayici logs) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder); builder.ConfigureLogging(logging => logging.AddProvider(logs));
        }
    }

    private static void AssertShares(IReadOnlyList<TakipKanalPayi>? actual, params (int? Id, decimal Amount)[] expected)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.OrderBy(p => p.Id), actual.Select(p => (p.KanalId, p.Tutar)).OrderBy(p => p.KanalId));
    }
    private async Task<HttpClient> Editor(KasaWebFactory f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Start, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
        return c;
    }
    private static async Task<T> Post<T>(HttpClient c, string path, object body)
    {
        var response = await c.PostAsJsonAsync(path, body);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private Task<KartTakipDto> Card(HttpClient c, decimal opening = 0m) => Post<KartTakipDto>(c, "/api/takip/kartlar",
        new KartTakipYaz(Guid.NewGuid(), 0, "Kart", 10000m, 5, 25, Start, opening, []));
    private Task<KartTakipDto> Charge(HttpClient c, KartTakipDto card, decimal amount, IReadOnlyList<KanalPayYaz> shares, int installments = 1) =>
        Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Start, "Malzeme", amount, installments, null, shares));
    private Task<KartTakipDto> Pay(HttpClient c, KartTakipDto card, decimal amount, int? statement = null) =>
        Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, amount, statement));
    private Task<KartTakipDto> Refund(HttpClient c, KartTakipDto card, int source, decimal amount) =>
        Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Today, "İade", -amount, 1, null, [], source));
    private static Task<KartTakipDto> CancelCharge(HttpClient c, KartTakipDto card, int charge) =>
        Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar/{charge}/iptal", new TakipIptalYaz(Guid.NewGuid(), card.Surum, "Kayıt iptali"));
    private static async Task<KartTakipDto> Minimum(HttpClient c, KartTakipDto card, int statementId, decimal minimum)
    {
        var statement = card.Ekstreler.Single(s => s.Id == statementId);
        var response = await c.PutAsJsonAsync($"/api/takip/kartlar/{card.Id}/ekstreler/{statementId}",
            new KartEkstreYaz(Guid.NewGuid(), card.Surum, statement.SonOdemeTarihi, minimum, "Bankanın asgari tutarı"));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<KartTakipDto>())!;
    }
    private static async Task<decimal> Cash(HttpClient c) => (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa;
}
