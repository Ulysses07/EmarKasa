using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class CardDebtSummaryTests
{
    private static DateOnly Today => FinansTakipServisi.Bugun;
    private static DateOnly Start => new(Today.Year, 1, 1);

    [Fact]
    public async Task Bir_kartin_alacagi_diger_kartin_kanal_borcunu_azaltmaz_ve_okuma_kasayi_degistirmez()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
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
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
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
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
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
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
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
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
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
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var card = await Charge(c, await Card(c, -25m), 100m, [new(1, 60m), new(2, 40m)]);
        AssertShares(card.KanalKartBorclari, (1, 45m), (2, 30m));
        Assert.Equal(75m, card.Borc); Assert.Equal(1000m, await Cash(c));
    }

    [Fact]
    public async Task Eski_kart_borcu_belirsizdir_geciste_kasada_sayilmis_odeme_de_brut_borcu_azaltir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
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
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
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
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
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
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
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

    private static void AssertShares(IReadOnlyList<TakipKanalPayi>? actual, params (int? Id, decimal Amount)[] expected)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.OrderBy(p => p.Id), actual.Select(p => (p.KanalId, p.Tutar)).OrderBy(p => p.KanalId));
    }
    private static async Task<HttpClient> Editor(KasaWebFactory f)
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
    private static Task<KartTakipDto> Card(HttpClient c, decimal opening = 0m) => Post<KartTakipDto>(c, "/api/takip/kartlar",
        new KartTakipYaz(Guid.NewGuid(), 0, "Kart", 10000m, 5, 25, Start, opening, []));
    private static Task<KartTakipDto> Charge(HttpClient c, KartTakipDto card, decimal amount, IReadOnlyList<KanalPayYaz> shares, int installments = 1) =>
        Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Start, "Malzeme", amount, installments, null, shares));
    private static Task<KartTakipDto> Pay(HttpClient c, KartTakipDto card, decimal amount, int? statement = null) =>
        Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, amount, statement));
    private static Task<KartTakipDto> Refund(HttpClient c, KartTakipDto card, int source, decimal amount) =>
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
