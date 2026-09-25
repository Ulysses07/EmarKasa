using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class CardLoanTrackingTests
{
    private static DateOnly Today => FinansTakipServisi.Bugun;
    private static DateOnly Start => new(Today.Year, 1, 1);
    private static async Task<HttpClient> Editor(KasaWebFactory factory)
    {
        var c = await factory.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Start, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
        return c;
    }
    private static async Task<T> Post<T>(HttpClient c, string path, object body)
    {
        var r = await c.PostAsJsonAsync(path, body);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }
    private static Task<KartTakipDto> Card(HttpClient c) => Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Takip kart", 10000m, 5, 25, Start, 0, []));
    private static Task<KartTakipDto> Charge(HttpClient c, KartTakipDto card, decimal amount, int installments = 1) => Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar",
        new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Start, "Malzeme", amount, installments, null, [new(1, amount * .6m), new(2, amount * .4m)]));
    private static async Task<decimal> Cash(HttpClient c) => (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa;

    [Fact]
    public async Task Kart_harcamasi_ve_vade_kasayi_dusurmez_yalniz_kismi_odeme_kaynak_kanallari_dusurur()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, 3);
        Assert.Equal(100m, card.Borc); Assert.Equal(1000m, await Cash(c));
        Assert.Equal(100m, card.Ekstreler.Sum(s => s.Borc));
        var request = new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, 25m);
        var preview = await Post<KartOdemeOnizlemeDto>(c, $"/api/takip/kartlar/{card.Id}/odeme-onizleme", request);
        Assert.Equal(25m, preview.KasaEtkisi); Assert.Equal(25m, preview.Dagilimlar.Sum(p => p.Tutar));
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", request);
        Assert.Equal(75m, card.Borc); Assert.Equal(975m, await Cash(c));
        var payment = Assert.Single(card.Odemeler);
        Assert.Equal(15m, payment.Dagilimlar.Single(p => p.KanalId == 1).Tutar);
        Assert.Equal(10m, payment.Dagilimlar.Single(p => p.KanalId == 2).Tutar);
        var panel = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;
        Assert.Equal(-15m, panel.Kanallar.Single(k => k.Kanal == "MEZAT").Bakiye);
        Assert.Equal(-10m, panel.Kanallar.Single(k => k.Kanal == "PERAKENDE").Bakiye);
        var replay = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", request);
        Assert.Single(replay.Odemeler); Assert.Equal(975m, await Cash(c));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/odemeler", request with { Tutar = 26m })).StatusCode);
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler/{payment.Id}/iptal", new TakipIptalYaz(Guid.NewGuid(), card.Surum, "Yanlış ödeme"));
        Assert.Equal(100m, card.Borc); Assert.Equal(1000m, await Cash(c));
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", request);
        Assert.Equal(1000m, await Cash(c)); // İptal sonrası ilk isteğin replay'i geri ödeme üretmez.
    }

    [Fact]
    public async Task Ortak_kredi_cekimi_ve_taksitleri_her_kanala_esit_genel_kasaya_bir_kez_yansir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var first = Today.AddMonths(-1); var draw = first.AddDays(-1);
        var request = new KrediTakipYaz(Guid.NewGuid(), "Ortak kredi", 100m, draw, first, 3, 10m, [1, 2, 3]);
        var loan = await Post<KrediTakipDto>(c, "/api/takip/krediler", request);
        Assert.Equal(100m, loan.KanalPaylari.Sum(p => p.Tutar));
        Assert.Equal(new[] { 33.34m, 33.33m, 33.33m }, loan.KanalPaylari.Select(p => p.Tutar));
        Assert.All(loan.Taksitler, t => Assert.Equal(t.Tutar, t.Dagilimlar.Sum(p => p.Tutar)));
        Assert.Equal(1080m, await Cash(c));
        Assert.Equal(10m, loan.KalanPlanliOdeme);
        var replay = await Post<KrediTakipDto>(c, "/api/takip/krediler", request);
        Assert.Equal(loan.Id, replay.Id); Assert.Equal(1080m, await Cash(c));
        var sameDay = loan.Taksitler.Single(t => t.Tarih == Today);
        var note = await c.PutAsJsonAsync($"/api/takip/krediler/{loan.Id}/taksitler/{sameDay.Id}", new KrediTaksitYaz(Guid.NewGuid(), loan.Surum, sameDay.Tarih, sameDay.Tutar, "Dekont bankada", false, "Dekont açıklaması"));
        note.EnsureSuccessStatusCode(); Assert.Equal(1080m, await Cash(c));
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var events = FinansTakipServisi.GetNotificationEvents(db, Today);
        Assert.Contains(events, e => e.Kaynak == "Kredi" && e.KalemId == sameDay.Id && e.Tarih == Today);
        var oldShares = db.TakipKrediTaksitler.Where(t => t.KrediId == loan.Id).Select(t => t.DagilimJson).ToArray();
        db.Kanallar.Single(k => k.Id == 1).Aktif = false; db.Kanallar.Add(new() { Ad = "SONRADAN", Sira = 20 }); db.SaveChanges();
        Assert.Equal(oldShares, db.TakipKrediTaksitler.Where(t => t.KrediId == loan.Id).Select(t => t.DagilimJson).ToArray());
        Assert.Equal(1080m, await Cash(c));
    }

    [Fact]
    public async Task Mevcut_kredi_yeni_gelir_uretmez_ve_gelecek_taksit_erken_dusmez()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var loan = await Post<KrediTakipDto>(c, "/api/takip/krediler", new KrediTakipYaz(Guid.NewGuid(), "Eski banka borcu", 5000m, Start, Today.AddMonths(1), 4, 200m, [1], true));
        Assert.Equal(1000m, await Cash(c)); Assert.Equal(800m, loan.KalanPlanliOdeme);
        loan = await Post<KrediTakipDto>(c, $"/api/takip/krediler/{loan.Id}/erken-kapat", new KrediKapatYaz(Guid.NewGuid(), loan.Surum, Today, 700m, "Banka kapama tutarı"));
        Assert.Equal(300m, await Cash(c)); Assert.Equal(0m, loan.KalanPlanliOdeme);
        Assert.Equal(4, loan.Taksitler.Count(t => t.Durum == "Iptal"));
        Assert.Single(loan.Taksitler, t => t.Durum == "KasayaIslendi");
    }

    [Fact]
    public async Task Eski_kart_gecis_oncesi_raporu_korur_sayilmis_borcu_ikinci_kez_dusurmez()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        int id;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var card = new KrediKartiEntity { Ad = "Eski", KesimTarihi = Start.AddDays(4), SonOdemeTarihi = Start.AddDays(24), Limit = 10000 };
            db.KrediKartlari.Add(card); db.SaveChanges(); id = card.Id;
            db.Islemler.Add(new() { Tarih = Start, Cari = "Eski harcama", TutarTl = 100, KanalId = 1, Kanal = "MEZAT", Tip = GiderTipi.KrediKarti, KrediKartiId = id }); db.SaveChanges();
        }
        var oldReport = await c.GetStringAsync($"/api/rapor/aylik?yil={Start.Year}&ay=2");
        Assert.Equal(900m, await Cash(c));
        var request = new KartGecisYaz(Guid.NewGuid(), 0, Today, 100m, 100m, [new(1, 100m)], "Borç kasada önceden sayılmış", false);
        var preview = await Post<TakipGecisDto>(c, $"/api/takip/kartlar/{id}/gecis-onizleme", request);
        Assert.Equal(0m, preview.GenelKasaAnlikFarki);
        var cardDto = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/gecis", request with { Onay = true });
        Assert.Equal(oldReport, await c.GetStringAsync($"/api/rapor/aylik?yil={Start.Year}&ay=2"));
        cardDto = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), cardDto.Surum, Today, 40m));
        Assert.Equal(900m, await Cash(c)); Assert.Equal(60m, cardDto.Borc); Assert.Equal(0m, Assert.Single(cardDto.Odemeler).KasaEtkisi);
    }

    [Fact]
    public async Task Giderden_gelen_kart_harcamasi_bir_kez_izlenir_ve_ortak_paylari_sabit_kalir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Card(c);
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Start, "Ortak gider", 30m, Kanallar.Ortak, GiderTipi.KrediKarti, KrediKartiId: card.Id))).EnsureSuccessStatusCode();
        card = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!;
        Assert.Single(card.Harcamalar); Assert.Equal(1000m, await Cash(c));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Kanallar.Single(k => k.Id == 1).Aktif = false; db.Kanallar.Add(new() { Ad = "Yeni kanal" }); db.SaveChanges();
        }
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, 30m));
        Assert.Equal(new[] { 10m, 10m, 10m }, card.Odemeler.Single().Dagilimlar.Select(p => p.Tutar));
        Assert.Equal(970m, await Cash(c));
        Assert.Single((await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!.Harcamalar);
        var expenseId = card.Harcamalar.Single().IslemId;
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/islemler/{expenseId}")).StatusCode);
    }

    [Fact]
    public async Task Eski_uc_yeni_takip_kuralini_atlayamaz_ve_alici_finansa_erismez()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Card(c);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/kredikartlari", new KrediKartiYazDto("X", Start, Start, 1m, 0m))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/krediler", new KrediYazDto("X", 1m, Start, 1, 1m, 1, "MEZAT"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/kredikartlari/{card.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/kartodemeler", new KartOdemeYazDto(card.Id, Today, 1m))).StatusCode);
        using var buyer = await AlisWorkflowTests.Buyer(f, c, "finans-alici");
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync("/api/takip/kartlar")).StatusCode);
    }

    [Fact]
    public async Task Kart_avansi_yeni_harcamaya_baglaninca_kasa_ikinci_kez_dusmez()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Card(c);
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, 100m));
        Assert.Equal(-100m, card.Borc); Assert.Equal(900m, await Cash(c));
        Assert.Null(card.Odemeler.Single().Dagilimlar.Single().KanalId);
        card = await Charge(c, card, 100m);
        Assert.Equal(0m, card.Borc); Assert.Equal(900m, await Cash(c));
        Assert.Equal(new[] { 60m, 40m }, card.Odemeler.Single().Dagilimlar.Select(p => p.Tutar));
        Assert.All(card.Odemeler.Single().Dagilimlar, p => Assert.NotNull(p.KanalId));
        Assert.All(card.Ekstreler, s => Assert.Equal(0m, s.Kalan));
    }

    [Fact]
    public async Task Subat_kesimi_otuzbir_gunlu_kartin_sonraki_taksitini_ayin_yirmisekizine_sabitlemez()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var card = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Ay sonu kartı", 1000m, 31, 10, Start, 0, []));
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, new(Today.Year, 2, 1), "Üç taksit", 100.01m, 3, null, [new(1, 100.01m)]));
        var statements = card.Ekstreler.Where(s => s.Borc > 0).ToArray();
        Assert.Equal(new[] { new DateOnly(Today.Year, 2, DateTime.DaysInMonth(Today.Year, 2)), new DateOnly(Today.Year, 3, 31), new DateOnly(Today.Year, 4, 30) }, statements.Select(s => s.KesimTarihi));
        Assert.Equal(new[] { 33.34m, 33.34m, 33.33m }, statements.Select(s => s.Borc));
        Assert.Equal(new DateOnly(Today.Year, 3, 10), statements[0].SonOdemeTarihi);
    }

    [Fact]
    public async Task Eski_kredi_gecisi_gecmis_raporu_ve_cekimi_korur_ileri_taksitleri_secilen_kanallara_dagitır()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); int id;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var loan = new KrediEntity { Ad = "Eski kredi", CekilenTutar = 1000m, CekimTarihi = Start, TaksitSayisi = 24, AylikOdeme = 100m, OdemeGunu = 15, Kanal = "MEZAT", KanalId = 1 };
            db.Krediler.Add(loan); db.SaveChanges(); id = loan.Id;
        }
        var before = await c.GetStringAsync($"/api/rapor/aylik?yil={Start.Year}&ay=2"); var cash = await Cash(c);
        var request = new KrediGecisYaz(Guid.NewGuid(), 0, Today.AddDays(1), [1, 2], "İleri ortak paylaşım", false);
        var preview = await Post<TakipGecisDto>(c, $"/api/takip/krediler/{id}/gecis-onizleme", request);
        Assert.Equal(0m, preview.GenelKasaAnlikFarki);
        var loanDto = await Post<KrediTakipDto>(c, $"/api/takip/krediler/{id}/gecis", request with { Onay = true });
        Assert.Equal(cash, await Cash(c)); Assert.Equal(before, await c.GetStringAsync($"/api/rapor/aylik?yil={Start.Year}&ay=2"));
        Assert.All(loanDto.Taksitler, t => { Assert.True(t.Tarih >= request.Baslangic); Assert.Equal(new[] { 50m, 50m }, t.Dagilimlar.Select(p => p.Tutar)); });
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/krediler/{id}")).StatusCode);
    }

    [Theory]
    [InlineData(0)] [InlineData(61)]
    public async Task Gecersiz_taksit_sayisi_veri_yazmadan_reddedilir(int count)
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Card(c);
        var r = await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Today, "Hata", 10m, count, null, []));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!.Harcamalar);
    }

    [Fact]
    public async Task B_kanalinin_iadesi_A_kanali_borcunu_kapatmaz()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Card(c);
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Start, "A harcaması", 100m, 1, null, [new(1, 100m)]));
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Start, "B harcaması", 100m, 1, null, [new(2, 100m)]));
        var source = card.Harcamalar.Single(h => h.Aciklama == "B harcaması");
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Today, "B iadesi", -100m, 1, null, [], source.Id));
        Assert.Equal(100m, card.Borc); Assert.Equal(1000m, await Cash(c));
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, 100m));
        Assert.Equal(0m, card.Borc); Assert.Equal(900m, await Cash(c));
        var pay = Assert.Single(Assert.Single(card.Odemeler).Dagilimlar);
        Assert.Equal(1, pay.KanalId); Assert.Equal(100m, pay.Tutar);
    }

    [Fact]
    public async Task Odenmis_harcama_iadesi_ve_odemeye_bagli_kaynak_iptali_para_degistirmeden_reddedilir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Charge(c, await Card(c), 100m);
        var source = card.Harcamalar.Single();
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, 80m));
        var r = await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Today, "Ödenmiş tutar iadesi", -30m, 1, null, [], source.Id));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Contains("mevcut ödeme kayıtları değişmedi", await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/harcamalar/{source.Id}/iptal", new TakipIptalYaz(Guid.NewGuid(), card.Surum, "Silme"))).StatusCode);
        Assert.Equal(920m, await Cash(c));
        var final = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!;
        Assert.Single(final.Harcamalar); Assert.Single(final.Odemeler); Assert.Equal(20m, final.Borc);
    }

    [Fact]
    public async Task Bugun_kasaya_islenmis_eski_kredi_taksidi_ayni_gun_gecisle_kanal_degistirmez()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); int id;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var old = new KrediEntity { Ad = "Bugün taksit", CekilenTutar = 0, CekimTarihi = Today.AddDays(-1), AylikOdeme = 100, OdemeGunu = Today.Day, TaksitSayisi = 2, Kanal = "MEZAT", KanalId = 1 };
            db.Krediler.Add(old); db.SaveChanges(); id = old.Id;
        }
        var before = await c.GetStringAsync("/api/rapor/panel");
        var d = new KrediGecisYaz(Guid.NewGuid(), 0, Today, [1, 2], "Aynı gün geçiş", true);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/krediler/{id}/gecis-onizleme", d)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/krediler/{id}/gecis", d)).StatusCode);
        Assert.Equal(before, await c.GetStringAsync("/api/rapor/panel"));
    }

    [Fact]
    public async Task Gecmis_acik_kart_ekstresi_ozette_gorunur_odemeden_sonra_hatirlatma_kalkar_kesim_kalir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Charge(c, await Card(c), 100m);
        var statement = card.Ekstreler.Single(s => s.Borc > 0);
        var summary = (await c.GetFromJsonAsync<TakipOzetDto>("/api/takip/ozet?gun=7"))!;
        Assert.Contains(summary.Olaylar, e => e.KaynakId == card.Id && e.KalemId == statement.Id && e.Tur == "SonOdeme");
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, 100m));
        summary = (await c.GetFromJsonAsync<TakipOzetDto>("/api/takip/ozet?gun=30"))!;
        Assert.DoesNotContain(summary.Olaylar, e => e.KaynakId == card.Id && e.KalemId == statement.Id && e.Tur == "SonOdeme");
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Contains(FinansTakipServisi.GetNotificationEvents(db, Today), e => e.KaynakId == card.Id && e.KalemId == statement.Id && e.Tur == "Kesim");
    }

    [Theory]
    [InlineData(true, 1)] [InlineData(false, 2)]
    public async Task Kismi_iadenin_kurusu_yalniz_odenmemis_kanal_payindan_cikar(bool paymentFirst, int paidChannel)
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Card(c);
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Start, "İki kuruş", .02m, 1, null, [new(1, .01m), new(2, .01m)]));
        var source = card.Harcamalar.Single();
        if (paymentFirst) card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, .01m));
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Today, "Bir kuruş iade", -.01m, 1, null, [], source.Id));
        if (!paymentFirst) card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, .01m));
        Assert.Equal(0m, card.Borc); Assert.Equal(999.99m, await Cash(c));
        Assert.Equal(paidChannel, Assert.Single(Assert.Single(card.Odemeler).Dagilimlar).KanalId);
        Assert.Equal(paidChannel == 1 ? 2 : 1, Assert.Single(card.Harcamalar.Single(h => h.Tutar < 0).Dagilimlar).KanalId);
    }

    [Fact]
    public async Task Bugunku_islenmis_taksit_erken_kapama_yoluyla_iptal_edilemez()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var loan = await Post<KrediTakipDto>(c, "/api/takip/krediler", new KrediTakipYaz(Guid.NewGuid(), "Bugün otomatik", 1000m, Today.AddDays(-1), Today, 3, 100m, [1]));
        Assert.Equal(1900m, await Cash(c));
        var r = await c.PostAsJsonAsync($"/api/takip/krediler/{loan.Id}/erken-kapat", new KrediKapatYaz(Guid.NewGuid(), loan.Surum, Today, 150m, "Aynı gün kapama"));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal(1900m, await Cash(c));
        var after = (await c.GetFromJsonAsync<KrediTakipDto>($"/api/takip/krediler/{loan.Id}"))!;
        Assert.Equal(3, after.Taksitler.Count); Assert.DoesNotContain(after.Taksitler, t => t.Durum == "Iptal");
    }

    [Fact]
    public async Task Iade_onceki_odemenin_bir_kurusunu_baska_kanala_tasiyamaz()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Card(c);
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Start, "Kuruş sınırı", .08m, 1, null, [new(1, .01m), new(2, .02m), new(3, .05m)]));
        var source = card.Harcamalar.Single();
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, .02m));
        Assert.Equal(3, Assert.Single(card.Odemeler.Single().Dagilimlar).KanalId);
        var r = await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Today, "Geçmiş kuruşu oynatacak iade", -.01m, 1, null, [], source.Id));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var after = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!;
        Assert.Single(after.Harcamalar); Assert.Equal(.06m, after.Borc);
        Assert.Equal(3, Assert.Single(after.Odemeler.Single().Dagilimlar).KanalId);
    }
}
