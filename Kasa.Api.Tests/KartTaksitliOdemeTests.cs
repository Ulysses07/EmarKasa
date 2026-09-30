using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Kartlı alış ödemesinde ve kartlı manuel giderde taksit (gap-coklu-giris-cift-sayim-mutabakat-6): yeni takipteki kartla girilen
/// ödeme <c>TaksitSayisi</c> (1–60) ve isteğe bağlı <c>IlkKesimTarihi</c> taşır; kart harcaması taksitli oluşur, ekstre borcu ve son
/// ödeme hatırlatması taksit tutarındadır. Alanları göndermeyen eski istemci tek taksitle çalışmaya devam eder.
/// </summary>
public class KartTaksitliOdemeTests
{
    private static readonly DateOnly Today = KasaWebFactory.VarsayilanBugun;
    private static readonly DateOnly Start = new(Today.Year, Today.Month - 8, 1);
    private static readonly DateOnly Gun = Today.AddDays(-6);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static KasaWebFactory Factory() => KasaWebFactory.Sabit(Today);
    private static async Task<HttpClient> Editor(KasaWebFactory f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Start, kasaAcilisDevri = 100000m })).EnsureSuccessStatusCode();
        return c;
    }
    private static async Task<T> Post<T>(HttpClient c, string path, object value)
    {
        var r = await c.PostAsJsonAsync(path, value);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }
    private static async Task<string> Hata(HttpResponseMessage r, HttpStatusCode beklenen)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == beklenen, $"{r.StatusCode}: {metin}");
        return metin;
    }
    private static async Task<KartTakipDto> Kart(HttpClient c, int id) => (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{id}", Json))!;
    private static Task<KartTakipDto> YeniKart(HttpClient c) => Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Taksit kart", 100000m, 5, 25, Start, 0, []));
    private static Task<AlisDto> Alis(HttpClient c, decimal tutar) => Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Gun, "Laptop", null, [new("Mal", tutar, [new(1, tutar)])]));
    private static List<TakipHarcamaEntity> Harcamalar(KasaWebFactory f, int kart)
    {
        using var scope = f.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<KasaDbContext>().TakipHarcamalar.AsNoTracking().Where(h => h.KrediKartiId == kart && !h.Iptal).ToList();
    }

    [Fact]
    public async Task Kartli_alis_odemesi_taksitle_girilince_ekstre_borcu_ve_son_odeme_taksit_tutarindadir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        var alis = await Alis(c, 36000m);
        var istek = new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Gun, 36000m, kart.Id, TaksitSayisi: 3);
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", istek);
        Assert.Equal(0m, alis.Kalan);
        // Aynı istek tekrarında ikinci harcama oluşmaz; aynı kimlikle farklı taksit reddedilir.
        Assert.Equal(alis.Surum, (await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", istek with { Surum = 0 })).Surum);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/alis/{alis.Id}/odemeler", istek with { TaksitSayisi = 2 }, cancellationToken: ct)).StatusCode);

        var harcama = Assert.Single(Harcamalar(f, kart.Id));
        Assert.Equal((3, alis.Odemeler.Single().IslemId), (harcama.TaksitSayisi, harcama.IslemId));
        var dto = await Kart(c, kart.Id);
        Assert.Equal(36000m, dto.Borc);
        var ilkKesim = new DateOnly(Gun.Year, Gun.Month, 5).AddMonths(Gun.Day > 5 ? 1 : 0);
        Assert.Equal([(ilkKesim, 12000m), (ilkKesim.AddMonths(1), 12000m), (ilkKesim.AddMonths(2), 12000m)],
            dto.Ekstreler.Where(e => e.Borc != 0).Select(e => (e.KesimTarihi, e.Borc)));
        var ozet = (await c.GetFromJsonAsync<TakipOzetDto>("/api/takip/ozet", Json, cancellationToken: ct))!;
        Assert.Equal(12000m, Assert.Single(ozet.Olaylar, o => o.Tur == "SonOdeme" && o.KaynakId == kart.Id).Tutar);
    }

    [Fact]
    public async Task Ilk_kesim_tarihi_ilk_taksidin_dongusunu_secer()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        var alis = await Alis(c, 1000m);
        var ilk = new DateOnly(Gun.Year, Gun.Month, 5).AddMonths(Gun.Day > 5 ? 2 : 1);
        await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Gun, 1000m, kart.Id, TaksitSayisi: 2, IlkKesimTarihi: ilk));
        var dto = await Kart(c, kart.Id);
        Assert.Equal([(ilk, 500m), (ilk.AddMonths(1), 500m)], dto.Ekstreler.Where(e => e.Borc != 0).Select(e => (e.KesimTarihi, e.Borc)));
    }

    [Fact]
    public async Task Gecersiz_taksit_girdisi_kayit_yazmadan_reddedilir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        var alis = await Alis(c, 1000m);
        async Task Red(AlisOdemeYaz g, string alan)
            => Assert.Contains(alan, await Hata(await c.PostAsJsonAsync($"/api/alis/{alis.Id}/odemeler", g), HttpStatusCode.BadRequest));
        var temel = new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Gun, 1000m, kart.Id);
        await Red(temel with { TaksitSayisi = 0 }, "taksitSayisi");
        await Red(temel with { TaksitSayisi = 61 }, "taksitSayisi");
        await Red(temel with { KrediKartiId = null, TaksitSayisi = 3 }, "Taksit yalnız yeni takipteki kartla");
        await Red(temel with { KrediKartiId = null, IlkKesimTarihi = Gun.AddDays(20) }, "Taksit yalnız yeni takipteki kartla");
        await Red(temel with { TaksitSayisi = 2, IlkKesimTarihi = Gun.AddDays(-1) }, "ilkKesimTarihi");
        await Red(temel with { TaksitSayisi = 2, IlkKesimTarihi = new DateOnly(Gun.Year, Gun.Month, 5).AddMonths(1).AddDays(12) }, "ilkKesimTarihi");
        await Red(temel with { TaksitSayisi = 2, MevcutKartHarcamaId = 1 }, "taksitSayisi");
        await Red(temel with { TaksitSayisi = 2, MevcutIslemId = 1 }, "taksitSayisi");
        Assert.Empty(Harcamalar(f, kart.Id));
        Assert.Empty((await c.GetFromJsonAsync<List<AlisDto>>("/api/alis", Json, cancellationToken: TestContext.Current.CancellationToken))!.Single().Odemeler);
    }

    [Fact]
    public async Task Kartli_manuel_gider_taksitle_girilir_eski_istemci_tek_taksitle_devam_eder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Gun, "Telefon", 3000m, "MEZAT", GiderTipi.KrediKarti, KrediKartiId: kart.Id, IstekId: Guid.NewGuid(), TaksitSayisi: 6), cancellationToken: ct)).EnsureSuccessStatusCode();
        // Eski istemci yeni alanları hiç göndermez (JSON'da yok).
        (await c.PostAsJsonAsync("/api/islemler",
            new { tarih = Gun, cari = "Kargo", tutarTl = 120m, kanal = "MEZAT", tip = "KrediKarti", krediKartiId = kart.Id }, cancellationToken: ct)).EnsureSuccessStatusCode();
        var harcamalar = Harcamalar(f, kart.Id).OrderBy(h => h.Id).ToList();
        Assert.Equal([6, 1], harcamalar.Select(h => h.TaksitSayisi));
        var dto = await Kart(c, kart.Id);
        Assert.Equal(500m + 120m, dto.Ekstreler.Where(e => e.Borc != 0).OrderBy(e => e.KesimTarihi).First().Borc);
        Assert.Equal(6, dto.Ekstreler.Count(e => e.Borc != 0));

        Assert.Contains("Taksit yalnız yeni takipteki kartla", await Hata(await c.PostAsJsonAsync("/api/islemler",
            new IslemYazDto(Gun, "Nakit", 100m, "MEZAT", GiderTipi.Cari, TaksitSayisi: 2), cancellationToken: ct), HttpStatusCode.BadRequest));
        var gider = harcamalar[1].IslemId!.Value;
        Assert.Contains("Taksit", await Hata(await c.PutAsJsonAsync($"/api/islemler/{gider}",
            new IslemYazDto(Gun, "Kargo", 120m, "MEZAT", GiderTipi.KrediKarti, KrediKartiId: kart.Id, TaksitSayisi: 3), cancellationToken: ct), HttpStatusCode.BadRequest));
    }
}
