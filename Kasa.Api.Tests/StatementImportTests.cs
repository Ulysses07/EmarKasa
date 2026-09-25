using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

public class StatementImportTests
{
    private static DateOnly Today => FinansTakipServisi.Bugun;
    private static DateOnly Start => new(Today.Year, 1, 1);
    private static async Task<HttpClient> Editor(KasaWebFactory f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Start, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode(); return c;
    }
    private static async Task<T> Post<T>(HttpClient c, string path, object value)
    {
        var r = await c.PostAsJsonAsync(path, value); Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }
    private static Task<PanelDto?> Panel(HttpClient c) => c.GetFromJsonAsync<PanelDto>("/api/rapor/panel");
    private static EkstreSatirYaz Row(int no, string type, decimal amount = 100m, string distribution = "Ozel", params KanalPayYaz[] shares) =>
        new(no, Today, "Banka hareketi " + no, amount, type, distribution, shares.Length == 0 && distribution == "Ozel" ? [new(1, amount)] : shares);
    private static async Task<EkstreBelgeDto> Document(KasaWebFactory f, HttpClient c, string source = "Banka", int? card = null, int count = 3, string currency = "TRY", string direction = "Cikis")
    {
        int id;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var d = new EkstreBelgeEntity { Kaynak = source, Banka = "Akbank", HesapAdi = source == "Banka" ? "İş hesabı" : "", KartId = card,
                DosyaAdi = "test.pdf", DosyaOzeti = Guid.NewGuid().ToString(), Dosya = "%PDF-test"u8.ToArray(), Yuklendi = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SatirlarJson = JsonSerializer.Serialize(Enumerable.Range(1, count).Select(no => new EkstreOkunanSatir(no, 1, "Kaynak " + no, Today, "Banka hareketi " + no, 100m, direction,
                    source == "Banka" ? "Gider" : "KartHarcama", "Hareket", currency, []))) };
            db.EkstreBelgeler.Add(d); db.SaveChanges(); id = d.Id;
        }
        return (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{id}"))!;
    }
    private static async Task<(EkstreKaydetYaz Request, EkstreOnizlemeDto Preview)> Preview(HttpClient c, EkstreBelgeDto d, params EkstreSatirYaz[] rows)
    {
        var request = new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, rows);
        var preview = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{d.Id}/onizleme", request);
        return (request with { OnizlemeOzeti = preview.OnizlemeOzeti, TekrarOnay = true }, preview);
    }
    private static Task<EkstreBelgeDto> Save(HttpClient c, EkstreBelgeDto d, EkstreKaydetYaz request) => Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/kaydet", request);
    private static Task<KartTakipDto> Card(HttpClient c) => Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Ekstre kart", 10000m, 5, 25, Start, 0, []));

    [Fact]
    public async Task Onizleme_kaydi_kasa_ve_kart_borcu_uretmez_tum_secili_banka_satirlari_bir_kez_islenir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var doc = await Document(f, c);
        var (request, preview) = await Preview(c, doc, Row(1, "Gelir", 200m, "Ozel", new(1, 120m), new(2, 80m)), Row(2, "Gider", 50m, "Esit", new(1, 0), new(2, 0)));
        Assert.Equal(150m, preview.KasaEtkisi); Assert.Equal(1000m, (await Panel(c))!.GuncelKasa);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>(); Assert.Empty(db.EkstreKayitlar); Assert.Empty(db.Islemler);
        }
        var saved = await Save(c, doc, request); Assert.Equal(2, saved.Kayitlar.Count);
        var panel = (await Panel(c))!; Assert.Equal(1150m, panel.GuncelKasa); Assert.Equal(95m, panel.Kanallar.Single(k => k.KanalId == 1).Bakiye);
        Assert.Equal(55m, panel.Kanallar.Single(k => k.KanalId == 2).Bakiye); Assert.Equal(150m, panel.BuAySonucu);
        var replay = await Save(c, doc, request); Assert.Equal(2, replay.Kayitlar.Count); Assert.Equal(1150m, (await Panel(c))!.GuncelKasa);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{doc.Id}/kaydet", request with { TekrarOnay = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{doc.Id}/onizleme", request with { IstekId = Guid.NewGuid(), Surum = saved.Surum })).StatusCode);
    }

    [Fact]
    public async Task Yalniz_genel_gelir_gider_kanallara_dagilmaz_rapor_ve_iptal_gecmisi_dogru_kalir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var doc = await Document(f, c);
        var (request, _) = await Preview(c, doc, Row(1, "Gelir", 100m, "Genel"), Row(2, "Gider", 30m, "Genel"));
        doc = await Save(c, doc, request); var panel = (await Panel(c))!;
        Assert.Equal(1070m, panel.GuncelKasa); Assert.Equal(70m, panel.BuAySonucu); Assert.All(panel.Kanallar, k => Assert.Equal(0m, k.Bakiye));
        var report = (await c.GetFromJsonAsync<AylikRapor>($"/api/rapor/aylik?yil={Today.Year}&ay={Today.Month}"))!;
        Assert.Equal(100m, report.GenelGelir); Assert.Equal(30m, report.GenelGider); Assert.Equal(0m, report.DagilimBekleyenTutar);
        var expense = doc.Kayitlar.Single(k => k.IslemTuru == "Gider");
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/islemler/{expense.IslemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/islemler/{expense.IslemId}", new IslemYazDto(Today, "Değiştir", 1m, "MEZAT", GiderTipi.Cari))).StatusCode);
        var cancel = new EkstreIptalYaz(Guid.NewGuid(), "Yanlış gider");
        doc = await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{doc.Id}/kayitlar/{expense.Id}/iptal", cancel);
        Assert.Equal(1100m, (await Panel(c))!.GuncelKasa); Assert.True(doc.Kayitlar.Single(k => k.Id == expense.Id).Iptal);
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{doc.Id}/kayitlar/{expense.Id}/iptal", cancel);
        var (again, _) = await Preview(c, doc, Row(2, "Gider", 25m, "Genel")); doc = await Save(c, doc, again);
        Assert.Equal(3, doc.Kayitlar.Count); Assert.Equal(1075m, (await Panel(c))!.GuncelKasa);
    }

    [Fact]
    public async Task Kart_harcama_ve_kismi_odeme_ayni_pakette_kaynak_kanallara_dogru_yansir_iptal_kaynak_bagindan_yapilir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Card(c); var doc = await Document(f, c, "Kart", card.Id);
        var (request, preview) = await Preview(c, doc, Row(1, "KartHarcama", 100m, "Ozel", new(1, 60m), new(2, 40m)), Row(2, "KartOdemesi", 20m, "Otomatik"));
        Assert.Equal(-20m, preview.KasaEtkisi); Assert.Equal(new[] { 12m, 8m }, preview.Satirlar[1].Dagilimlar.Select(p => p.Tutar));
        Assert.Equal(0m, (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!.Borc);
        doc = await Save(c, doc, request); card = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!;
        Assert.Equal(80m, card.Borc); Assert.Equal(980m, (await Panel(c))!.GuncelKasa); Assert.NotNull(Assert.Single(card.Harcamalar).EkstreKayitId); Assert.NotNull(Assert.Single(card.Odemeler).EkstreKayitId);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/odemeler/{card.Odemeler[0].Id}/iptal", new TakipIptalYaz(Guid.NewGuid(), card.Surum, "Yanlış"))).StatusCode);
        var charge = doc.Kayitlar.Single(k => k.IslemTuru == "KartHarcama");
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{doc.Id}/kayitlar/{charge.Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Ödemeli"))).StatusCode);
        doc = await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{doc.Id}/kayitlar/{doc.Kayitlar.Single(k => k.IslemTuru == "KartOdemesi").Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış ödeme"));
        Assert.Equal(1000m, (await Panel(c))!.GuncelKasa);
        doc = await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{doc.Id}/kayitlar/{charge.Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış harcama"));
        Assert.All(doc.Kayitlar, k => Assert.True(k.Iptal)); Assert.Equal(0m, (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!.Borc);
    }

    [Fact]
    public async Task Banka_kart_odemesi_gider_olarak_ikinci_kez_yazilmaz_ve_iade_yalniz_kaynak_borctan_duser()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Card(c);
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Today, "Mal", 100m, 1, null, [new(1, 60m), new(2, 40m)]));
        var bank = await Document(f, c); var (pay, _) = await Preview(c, bank, Row(1, "KartOdemesi", 20m, "Otomatik") with { KrediKartiId = card.Id });
        await Save(c, bank, pay); Assert.Equal(980m, (await Panel(c))!.GuncelKasa);
        var doc = await Document(f, c, "Kart", card.Id); var (refund, preview) = await Preview(c, doc, Row(1, "KartIade", 10m, "Otomatik") with { KaynakHarcamaId = card.Harcamalar[0].Id });
        Assert.Equal(0m, preview.KasaEtkisi); await Save(c, doc, refund);
        Assert.Equal(70m, (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!.Borc); Assert.Equal(980m, (await Panel(c))!.GuncelKasa);
    }

    [Fact]
    public async Task Odeme_harcamadan_once_secildiginde_onizleme_son_kanal_dagilimini_gosterir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Card(c); var doc = await Document(f, c, "Kart", card.Id);
        var (request, preview) = await Preview(c, doc, Row(1, "KartOdemesi", 100m, "Otomatik"), Row(2, "KartHarcama", 100m, "Ozel", new(1, 60m), new(2, 40m)));
        Assert.Equal(-100m, preview.KasaEtkisi); Assert.Equal(new[] { 60m, 40m }, preview.Satirlar[0].Dagilimlar.Select(p => p.Tutar));
        Assert.DoesNotContain(preview.Satirlar[0].Dagilimlar, p => p.KanalId is null);
        doc = await Save(c, doc, request); Assert.Equal(new[] { 60m, 40m }, doc.Kayitlar.Single(k => k.IslemTuru == "KartOdemesi").Dagilimlar.Select(p => p.Tutar));
        Assert.Equal(900m, (await Panel(c))!.GuncelKasa); Assert.Equal(-60m, (await Panel(c))!.Kanallar.Single(k => k.KanalId == 1).Bakiye);
    }

    [Fact]
    public async Task Eski_avansi_kanala_baglayan_harcama_onizlemede_acik_uyari_ve_onay_ister()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Card(c);
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, 100m));
        var doc = await Document(f, c, "Kart", card.Id); var (request, preview) = await Preview(c, doc, Row(1, "KartHarcama"));
        Assert.Equal(0m, preview.KasaEtkisi); Assert.True(preview.TekrarOnayGerekli); Assert.Contains(preview.Uyarilar, w => w.Contains("Önceden kaydedilen kart avansının"));
        Assert.Equal(900m, (await Panel(c))!.GuncelKasa); Assert.Equal(0m, (await Panel(c))!.Kanallar.Single(k => k.KanalId == 1).Bakiye);
        await Save(c, doc, request); Assert.Equal(900m, (await Panel(c))!.GuncelKasa); Assert.Equal(-100m, (await Panel(c))!.Kanallar.Single(k => k.KanalId == 1).Bakiye);
    }

    [Fact]
    public async Task Son_satirdaki_hata_ilk_satiri_yarim_kaydetmez_ve_oynanmis_kaynak_satiri_reddedilir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var doc = await Document(f, c);
        var request = new EkstreKaydetYaz(Guid.NewGuid(), doc.Surum, [Row(1, "Gider"), Row(99, "Gelir")]);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{doc.Id}/onizleme", request)).StatusCode);
        Assert.Equal(1000m, (await Panel(c))!.GuncelKasa);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>(); Assert.Empty(db.Islemler); Assert.Empty(db.EkstreKayitlar);
    }

    [Fact]
    public async Task Manuel_benzer_kayit_uyarisi_onay_gerektirir_ve_sonradan_eklenirse_eski_onizleme_reddedilir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var doc = await Document(f, c);
        var (request, preview) = await Preview(c, doc, Row(1, "Gider")); Assert.False(preview.TekrarOnayGerekli);
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Today, "Manuel banka gideri", 100m, "MEZAT", GiderTipi.Cari))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{doc.Id}/kaydet", request)).StatusCode);
        (request, preview) = await Preview(c, doc, Row(1, "Gider")); Assert.True(preview.TekrarOnayGerekli);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{doc.Id}/kaydet", request with { TekrarOnay = false })).StatusCode);
        await Save(c, doc, request); Assert.Equal(800m, (await Panel(c))!.GuncelKasa);
    }

    [Theory]
    [InlineData("USD", "Cikis", false)] [InlineData("Belirsiz", "Belirsiz", true)]
    public async Task Doviz_reddedilir_belirsiz_yon_ve_para_birimi_acik_onay_ister(string currency, string direction, bool allowed)
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var doc = await Document(f, c, currency: currency, direction: direction);
        var request = new EkstreKaydetYaz(Guid.NewGuid(), doc.Surum, [Row(1, "Gider")]);
        var response = await c.PostAsJsonAsync($"/api/ekstre-aktar/{doc.Id}/onizleme", request);
        if (!allowed) { Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); return; }
        response.EnsureSuccessStatusCode(); var preview = (await response.Content.ReadFromJsonAsync<EkstreOnizlemeDto>())!;
        Assert.True(preview.TekrarOnayGerekli); Assert.Contains(preview.Satirlar[0].Uyarilar, s => s.Contains("TL olarak"));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{doc.Id}/kaydet", request with { OnizlemeOzeti = preview.OnizlemeOzeti })).StatusCode);
        Assert.Equal(1000m, (await Panel(c))!.GuncelKasa);
    }

    [Fact]
    public async Task Kilitli_aya_gelir_gider_ve_kart_kaydi_yazilamaz_iptali_de_yapilamaz()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var doc = await Document(f, c);
        var previous = new DateOnly(Today.Year, Today.Month, 1).AddDays(-1);
        var (request, _) = await Preview(c, doc, Row(1, "Gelir", 100m, "Genel") with { Tarih = previous }); doc = await Save(c, doc, request);
        var state = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi"))!;
        await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), state.Surum, previous.Year, previous.Month, "Kontrol tamam"));
        var invalid = new EkstreKaydetYaz(Guid.NewGuid(), doc.Surum, [Row(2, "Gider") with { Tarih = previous }]);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{doc.Id}/onizleme", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{doc.Id}/kayitlar/{doc.Kayitlar[0].Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Düzelt"))).StatusCode);
        Assert.Equal(1100m, (await Panel(c))!.GuncelKasa);
    }

    [Fact]
    public async Task Kart_surumu_degisince_onizleme_yenilenir_ve_diger_belgenin_satirina_aktarim_yapilmaz()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var card = await Card(c); var doc = await Document(f, c, "Kart", card.Id);
        var (request, _) = await Preview(c, doc, Row(1, "KartHarcama"));
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Today, "Başka harcama", 20m, 1, null, [new(1, 20m)]));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{doc.Id}/kaydet", request)).StatusCode);
        var other = await Document(f, c, "Kart", card.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{other.Id}/kaydet", request)).StatusCode);
        Assert.Equal(20m, (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!.Borc);
    }

    [Fact]
    public async Task Yukleme_hash_ile_tekrarlanmaz_dosya_ozeldir_ve_sadece_editor_erisebilir()
    {
        await using var f = new PdfFactory(); using var c = await Editor(f);
        async Task<HttpResponseMessage> Upload(string account = "Ana banka", byte[]? bytes = null)
        {
            using var form = new MultipartFormDataContent(); form.Add(new StringContent("Banka"), "kaynak"); form.Add(new StringContent("Akbank"), "banka"); form.Add(new StringContent(account), "hesapAdi");
            form.Add(new ByteArrayContent(bytes ?? "%PDF-1.7 fake"u8.ToArray()), "dosya", "<ekstre>.pdf"); return await c.PostAsync("/api/ekstre-aktar/yukle", form);
        }
        var uploaded = await Upload(); Assert.True(uploaded.IsSuccessStatusCode, await uploaded.Content.ReadAsStringAsync());
        var document = (await uploaded.Content.ReadFromJsonAsync<EkstreBelgeDto>())!; Assert.Empty(document.Kayitlar); Assert.NotEmpty(document.Satirlar);
        var again = await Upload(); Assert.Equal(document.Id, (await again.Content.ReadFromJsonAsync<EkstreBelgeDto>())!.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Upload("Başka hesap")).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await Upload(bytes: "not pdf"u8.ToArray())).StatusCode);
        var file = await c.GetAsync($"/api/ekstre-aktar/{document.Id}/dosya"); Assert.Equal("attachment", file.Content.Headers.ContentDisposition!.DispositionType); Assert.Equal("application/pdf", file.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", file.Headers.GetValues("X-Content-Type-Options").Single()); Assert.Equal(1000m, (await Panel(c))!.GuncelKasa);
        using var anonymous = f.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/ekstre-aktar/{document.Id}/dosya")).StatusCode);
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici123" })).EnsureSuccessStatusCode();
        using var viewer = f.CreateClient(); (await viewer.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izleyici123" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/ekstre-aktar")).StatusCode);
    }

    [Fact]
    public async Task Elli_belgeden_eski_gecmise_sayfalama_ve_kaynak_kimligi_ile_ulas_ilabilir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); var original = await Document(f, c);
        var (request, _) = await Preview(c, original, Row(1, "Gider")); original = await Save(c, original, request);
        var source = original.Kayitlar[0];
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            for (var i = 0; i < 51; i++) db.EkstreBelgeler.Add(new() { Kaynak = "Banka", Banka = "QNB", HesapAdi = "Sonraki hesap", DosyaAdi = "sonraki.pdf", DosyaOzeti = Guid.NewGuid().ToString(), Dosya = "%PDF-test"u8.ToArray(), Yuklendi = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            db.SaveChanges();
        }
        var first = (await c.GetFromJsonAsync<List<EkstreBelgeOzetDto>>("/api/ekstre-aktar"))!;
        Assert.Equal(50, first.Count); Assert.DoesNotContain(first, d => d.Id == original.Id);
        var next = (await c.GetFromJsonAsync<List<EkstreBelgeOzetDto>>($"/api/ekstre-aktar?beforeId={first[^1].Id}"))!;
        Assert.Equal(2, next.Count); Assert.Contains(next, d => d.Id == original.Id); Assert.Empty(next.Select(d => d.Id).Intersect(first.Select(d => d.Id)));
        var direct = (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/kayitlar/{source.Id}"))!; Assert.Equal(original.Id, direct.Id);
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{original.Id}/kayitlar/{source.Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Eski belge düzeltmesi"));
        direct = (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/kayitlar/{source.Id}"))!; Assert.True(Assert.Single(direct.Kayitlar).Iptal);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/ekstre-aktar/kayitlar/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/ekstre-aktar?beforeId=0")).StatusCode);
        using var anonymous = f.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/ekstre-aktar/kayitlar/{source.Id}")).StatusCode);
    }

    private sealed class PdfFactory : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder); builder.ConfigureServices(services => { services.RemoveAll<IPdfMetinOkuyucu>(); services.AddSingleton<IPdfMetinOkuyucu, FakePdf>(); });
        }
    }
    private sealed class FakePdf : IPdfMetinOkuyucu
    {
        public Task<string> OkuAsync(byte[] pdf, CancellationToken ct) => Task.FromResult($"İşlem Tarihi    Açıklama                Tutar        Bakiye\n{Today:dd.MM.yyyy}    KOMİSYON                  -10,00 TL    990,00 TL\n");
    }
}
