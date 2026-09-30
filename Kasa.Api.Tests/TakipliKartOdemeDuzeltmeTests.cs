using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Takipli kartla girilmiş alış ödemesinin ve kartlı manuel giderin düzeltme yolları (gap-coklu-giris-cift-sayim-mutabakat-5).
/// Kart takibindeki ödeme yalnız başka alışa taşınır (tarih, tutar ve kart sabit) ya da alıştan ayrılır: harcama ödenmediyse
/// harcama, taksitleri ve gider birlikte kalkar; ödendiyse gerçek kanal dağılımı girilerek gider alıştan bağımsız kart gideri
/// olarak kalır. Hiçbir yolda kart harcaması çift oluşmaz, alışın kalanı ve gider listesi çift saymaz.
/// </summary>
public class TakipliKartOdemeDuzeltmeTests
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
    private static async Task<T> Put<T>(HttpClient c, string path, object value)
    {
        var r = await c.PutAsJsonAsync(path, value);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }
    private static async Task<string> Hata(HttpResponseMessage r, HttpStatusCode beklenen)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == beklenen, $"{r.StatusCode}: {metin}");
        return metin;
    }
    private static async Task<decimal> Kasa(HttpClient c) => (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa;
    private static async Task<KartTakipDto> Kart(HttpClient c, int id) => (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{id}", Json))!;
    private static async Task<AlisDto> Oku(HttpClient c, int id) => (await c.GetFromJsonAsync<List<AlisDto>>("/api/alis", Json))!.Single(a => a.Id == id);
    private static async Task<List<IslemOkuDto>> Giderler(HttpClient c) => (await c.GetFromJsonAsync<List<IslemOkuDto>>($"/api/islemler?baslangic={Start:yyyy-MM-dd}&bitis={Today:yyyy-MM-dd}", Json))!;
    private static Task<KartTakipDto> YeniKart(HttpClient c) => Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Takip kart", 100000m, 5, 25, Start, 0, []));
    private static Task<AlisDto> Alis(HttpClient c, string tedarikci, decimal tutar, IReadOnlyList<AlisDagilimYaz>? paylar = null) =>
        Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Gun, tedarikci, null, [new("Mal", tutar, paylar ?? [])]));
    private static async Task<AlisDto> Onayla(HttpClient c, AlisDto a)
    {
        a = await Post<AlisDto>(c, $"/api/alis/{a.Id}/gonder", new AlisDurumYaz(a.Surum));
        return await Post<AlisDto>(c, $"/api/alis/{a.Id}/onayla", new AlisDurumYaz(a.Surum));
    }
    private static int AktifHarcama(KasaWebFactory f, int kart)
    {
        using var scope = f.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<KasaDbContext>().TakipHarcamalar.Count(h => h.KrediKartiId == kart && !h.Iptal);
    }

    [Fact]
    public async Task Yanlis_alisa_girilmis_takipli_kart_odemesi_dogru_alisa_tasinir_harcama_cift_olusmaz()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        var dogru = await Alis(c, "Doğru tedarikçi", 12000m);
        var yanlis = await Alis(c, "Yanlış tedarikçi", 12000m);
        yanlis = await Post<AlisDto>(c, $"/api/alis/{yanlis.Id}/odemeler", new AlisOdemeYaz(yanlis.Surum, Guid.NewGuid(), Gun, 12000m, kart.Id));
        var odeme = Assert.Single(yanlis.Odemeler);
        var kasa = await Kasa(c);
        var once = await Kart(c, kart.Id);
        var harcama = Assert.Single(once.Harcamalar);

        // Tarih, tutar ve kart değişikliği ya da hedefsiz düzeltme bu yolla yapılamaz.
        foreach (var bozuk in new[]
        {
            new AlisOdemeDuzelt(yanlis.Surum, Guid.NewGuid(), Gun.AddDays(1), 12000m, "Tarih", kart.Id, HedefAlisId: dogru.Id, HedefSurum: dogru.Surum),
            new AlisOdemeDuzelt(yanlis.Surum, Guid.NewGuid(), Gun, 11000m, "Tutar", kart.Id, HedefAlisId: dogru.Id, HedefSurum: dogru.Surum),
            new AlisOdemeDuzelt(yanlis.Surum, Guid.NewGuid(), Gun, 12000m, "Nakit", null, HedefAlisId: dogru.Id, HedefSurum: dogru.Surum),
            new AlisOdemeDuzelt(yanlis.Surum, Guid.NewGuid(), Gun, 12000m, "Aynı alış", kart.Id),
        })
            Assert.Contains("yalnız başka alışa taşınabilir", await Hata(await c.PutAsJsonAsync($"/api/alis/{yanlis.Id}/odemeler/{odeme.Id}", bozuk), HttpStatusCode.Conflict));

        yanlis = await Put<AlisDto>(c, $"/api/alis/{yanlis.Id}/odemeler/{odeme.Id}",
            new AlisOdemeDuzelt(yanlis.Surum, Guid.NewGuid(), Gun, 12000m, "Ödeme Alış #7'nindi", kart.Id, HedefAlisId: dogru.Id, HedefSurum: dogru.Surum));
        Assert.Empty(yanlis.Odemeler);
        Assert.Equal(12000m, yanlis.Kalan);
        dogru = await Oku(c, dogru.Id);
        Assert.Equal(0m, dogru.Kalan);
        Assert.Equal(odeme.IslemId, Assert.Single(dogru.Odemeler).IslemId);

        // Kart tarafı değişmez: aynı harcama, aynı borç ve taksit; kasa değişmez. Harcama yeni alışın adını taşır.
        var sonra = await Kart(c, kart.Id);
        Assert.Equal(1, AktifHarcama(f, kart.Id));
        Assert.Equal(12000m, sonra.Borc);
        Assert.Equal(kasa, await Kasa(c));
        var tasinan = Assert.Single(sonra.Harcamalar);
        Assert.Equal((harcama.Id, harcama.IslemId, 1, "Doğru tedarikçi"), (tasinan.Id, tasinan.IslemId, tasinan.TaksitSayisi, tasinan.Aciklama));
        Assert.True(sonra.Surum > once.Surum);

        // Yanlış alışa gerçek ödemesi artık girilebilir; gider listesi her ödemeyi bir kez gösterir.
        yanlis = await Post<AlisDto>(c, $"/api/alis/{yanlis.Id}/odemeler", new AlisOdemeYaz(yanlis.Surum, Guid.NewGuid(), Gun, 12000m));
        Assert.Equal(0m, yanlis.Kalan);
        var giderler = await Giderler(c);
        Assert.Equal(24000m, giderler.Sum(g => g.TutarTl));
        Assert.Equal(dogru.Id, giderler.Single(g => g.KrediKartiId == kart.Id).AlisId);
    }

    [Fact]
    public async Task Odenmemis_takipli_kart_odemesinin_iptali_harcamayi_taksitleri_ve_gideri_kaldirir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        var dogru = await Alis(c, "Doğru", 12000m);
        var yanlis = await Alis(c, "Yanlış", 12000m);
        yanlis = await Post<AlisDto>(c, $"/api/alis/{yanlis.Id}/odemeler", new AlisOdemeYaz(yanlis.Surum, Guid.NewGuid(), Gun, 12000m, kart.Id));
        var odeme = Assert.Single(yanlis.Odemeler);
        var kasa = await Kasa(c);
        var harcamaId = Assert.Single((await Kart(c, kart.Id)).Harcamalar).Id;

        var iptal = new AlisOdemeIptal(yanlis.Surum, Guid.NewGuid(), "Kart ödemesi yanlış alışa girildi");
        yanlis = await Post<AlisDto>(c, $"/api/alis/{yanlis.Id}/odemeler/{odeme.Id}/iptal", iptal);
        Assert.Empty(yanlis.Odemeler);
        Assert.Equal(12000m, yanlis.Kalan);
        Assert.Equal(yanlis.Surum, (await Post<AlisDto>(c, $"/api/alis/{yanlis.Id}/odemeler/{odeme.Id}/iptal", iptal)).Surum);
        var kartSonra = await Kart(c, kart.Id);
        Assert.Equal(0m, kartSonra.Borc);
        Assert.Equal(0, AktifHarcama(f, kart.Id));
        Assert.Equal(kasa, await Kasa(c));
        Assert.True(Assert.Single(kartSonra.Harcamalar, h => h.Id == harcamaId).Iptal);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.False(db.Islemler.Any(i => i.Id == odeme.IslemId));
            Assert.Null(db.TakipHarcamalar.Single(h => h.Id == harcamaId).IslemId);
        }

        // Doğru alışa yeniden girilen ödeme tek kart harcaması üretir; gider listesi 12.000'i bir kez gösterir.
        dogru = await Post<AlisDto>(c, $"/api/alis/{dogru.Id}/odemeler", new AlisOdemeYaz(dogru.Surum, Guid.NewGuid(), Gun, 12000m, kart.Id));
        Assert.Equal(0m, dogru.Kalan);
        Assert.Equal(1, AktifHarcama(f, kart.Id));
        Assert.Equal(12000m, (await Kart(c, kart.Id)).Borc);
        Assert.Equal(12000m, (await Giderler(c)).Sum(g => g.TutarTl));
    }

    [Fact]
    public async Task Odenmis_takipli_kart_harcamasi_kanal_dagilimiyla_alistan_ayrilir_kart_odemesi_ve_kasa_degismez()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        var dogru = await Alis(c, "Doğru", 12000m, [new(1, 12000m)]);
        var yanlis = await Onayla(c, await Alis(c, "Yanlış", 12000m, [new(1, 7200m), new(2, 4800m)]));
        yanlis = await Post<AlisDto>(c, $"/api/alis/{yanlis.Id}/odemeler", new AlisOdemeYaz(yanlis.Surum, Guid.NewGuid(), Gun, 12000m, kart.Id));
        var odeme = Assert.Single(yanlis.Odemeler);
        var kartDto = await Kart(c, kart.Id);
        kartDto = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kartDto.Surum, Today, 5000m));
        var kartOdemesi = Assert.Single(kartDto.Odemeler);
        Assert.Equal([(1, 3000m), (2, 2000m)], kartOdemesi.Dagilimlar.Select(p => (p.KanalId!.Value, p.Tutar)));
        var kasa = await Kasa(c);
        var panel = await c.GetStringAsync("/api/rapor/panel");

        // Ödenmiş harcama silinemez; dağılımsız iptal yol gösterir. Pay toplamı ödemeye eşit olmalı.
        Assert.Contains("ödendi", await Hata(await c.PostAsJsonAsync($"/api/alis/{yanlis.Id}/odemeler/{odeme.Id}/iptal",
            new AlisOdemeIptal(yanlis.Surum, Guid.NewGuid(), "Yanlış alış")), HttpStatusCode.Conflict));
        await Hata(await c.PostAsJsonAsync($"/api/alis/{yanlis.Id}/odemeler/{odeme.Id}/iptal",
            new AlisOdemeIptal(yanlis.Surum, Guid.NewGuid(), "Yanlış alış", [new(1, 7000m), new(2, 4800m)])), HttpStatusCode.BadRequest);
        // Taşıma ise dağılım gerektirmez; burada doğru alış ayrılıp sonra bağlanarak düzeltilir.
        yanlis = await Post<AlisDto>(c, $"/api/alis/{yanlis.Id}/odemeler/{odeme.Id}/iptal",
            new AlisOdemeIptal(yanlis.Surum, Guid.NewGuid(), "Başka alışın ödemesi", [new(1, 7200m), new(2, 4800m)]));
        Assert.Empty(yanlis.Odemeler);
        Assert.Equal(12000m, yanlis.Kalan);

        // Gider alıştan bağımsız kart gideri olarak kalır; harcama, borç, kart ödemesinin payları ve kasa aynı.
        kartDto = await Kart(c, kart.Id);
        var harcama = Assert.Single(kartDto.Harcamalar);
        Assert.Equal((odeme.IslemId, false), (harcama.IslemId, harcama.Iptal));
        Assert.Equal([(1, 7200m), (2, 4800m)], harcama.Dagilimlar.Select(p => (p.KanalId!.Value, p.Tutar)));
        Assert.Equal(7000m, kartDto.Borc);
        Assert.Equal([(1, 3000m), (2, 2000m)], Assert.Single(kartDto.Odemeler).Dagilimlar.Select(p => (p.KanalId!.Value, p.Tutar)));
        Assert.Equal(kasa, await Kasa(c));
        Assert.Equal(panel, await c.GetStringAsync("/api/rapor/panel"));
        var gider = Assert.Single(await Giderler(c));
        Assert.Equal(("MEZAT / PERAKENDE", (int?)null, (int?)null), (gider.Kanal, gider.KanalId, gider.AlisId));
        Assert.Contains("Alıştan ayrıldı: Başka alışın ödemesi", gider.Not);

        // Ayrılan gider doğru alışa mevcut gider olarak bağlanır: ikinci harcama ve ikinci gider oluşmaz.
        dogru = await Post<AlisDto>(c, $"/api/alis/{dogru.Id}/odemeler", new AlisOdemeYaz(dogru.Surum, Guid.NewGuid(), Gun, 12000m, kart.Id, MevcutIslemId: odeme.IslemId));
        Assert.Equal(0m, dogru.Kalan);
        Assert.Equal(1, AktifHarcama(f, kart.Id));
        Assert.Equal(12000m, (await Giderler(c)).Sum(g => g.TutarTl));
    }

    [Fact]
    public async Task Iptalde_kanal_dagilimi_yalniz_kart_takibindeki_odemede_girilir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var a = await Alis(c, "Nakit", 500m);
        a = await Post<AlisDto>(c, $"/api/alis/{a.Id}/odemeler", new AlisOdemeYaz(a.Surum, Guid.NewGuid(), Gun, 500m));
        var o = Assert.Single(a.Odemeler);
        Assert.Contains("Kanal dağılımı yalnız kart takibindeki", await Hata(await c.PostAsJsonAsync($"/api/alis/{a.Id}/odemeler/{o.Id}/iptal",
            new AlisOdemeIptal(a.Surum, Guid.NewGuid(), "Ayır", [new(1, 500m)])), HttpStatusCode.BadRequest));
        Assert.Single((await Oku(c, a.Id)).Odemeler);
    }

    [Fact]
    public async Task Odenmemis_takipli_kartli_manuel_gider_silinir_ve_tarih_tutar_kart_disinda_duzenlenir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        async Task<int> Gider(decimal tutar)
        {
            using var r = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Gun, "Kargo", tutar, "MEZAT", GiderTipi.KrediKarti, KrediKartiId: kart.Id));
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        }
        var silinecek = await Gider(300m);
        var duzenlenecek = await Gider(200m);
        Assert.Equal(500m, (await Kart(c, kart.Id)).Borc);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/islemler/{silinecek}")).StatusCode);
        Assert.Equal(200m, (await Kart(c, kart.Id)).Borc);
        Assert.Equal(1, AktifHarcama(f, kart.Id));

        var yeni = new IslemYazDto(Gun, "Kargo firması", 200m, "PERAKENDE", GiderTipi.KrediKarti, "fatura", kart.Id);
        Assert.Contains("değiştirilemez", await Hata(await c.PutAsJsonAsync($"/api/islemler/{duzenlenecek}", yeni with { TutarTl = 250m }), HttpStatusCode.Conflict));
        Assert.Contains("değiştirilemez", await Hata(await c.PutAsJsonAsync($"/api/islemler/{duzenlenecek}", yeni with { Tarih = Gun.AddDays(1) }), HttpStatusCode.Conflict));
        (await c.PutAsJsonAsync($"/api/islemler/{duzenlenecek}", yeni)).EnsureSuccessStatusCode();
        var harcama = Assert.Single((await Kart(c, kart.Id)).Harcamalar, h => !h.Iptal);
        Assert.Equal(("Kargo firması", 2, 200m), (harcama.Aciklama, Assert.Single(harcama.Dagilimlar).KanalId!.Value, harcama.Dagilimlar[0].Tutar));

        // Ödeme payı alan harcamanın gideri silinmez ve kanalı değişmez (önceki ödemenin kanal payı değişirdi).
        var kartDto = await Kart(c, kart.Id);
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kartDto.Surum, Today, 50m));
        Assert.Contains("ödendi", await Hata(await c.DeleteAsync($"/api/islemler/{duzenlenecek}"), HttpStatusCode.Conflict));
        Assert.Contains("ödendi", await Hata(await c.PutAsJsonAsync($"/api/islemler/{duzenlenecek}", yeni with { Kanal = "MEZAT" }), HttpStatusCode.Conflict));
        // Kart ekranından iptal yol gösterir.
        kartDto = await Kart(c, kart.Id);
        Assert.Contains($"Gider #{duzenlenecek}", await Hata(await c.PostAsJsonAsync($"/api/takip/kartlar/{kart.Id}/harcamalar/{harcama.Id}/iptal",
            new TakipIptalYaz(Guid.NewGuid(), kartDto.Surum, "Yanlış")), HttpStatusCode.Conflict));
    }

    /// <summary>Kaynak harcama engeli (FinansTakipServisi.HarcamaEngeli) iadede: iadesi olan kart harcamasının gideri silinemez ve kanalı
    /// değiştirilemez; ileti işleme göre yazılır (tipli sonuca geçişte iletiler aynen korunur).</summary>
    [Fact]
    public async Task Iadesi_olan_kart_harcamasinin_gideri_silinemez_ve_kanali_degismez()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        using var r = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Gun, "Kargo", 200m, "MEZAT", GiderTipi.KrediKarti, KrediKartiId: kart.Id));
        var gider = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var kartDto = await Kart(c, kart.Id);
        var harcama = Assert.Single(kartDto.Harcamalar, h => !h.Iptal);
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kartDto.Surum, Gun, "Kargo iadesi", -50m, 1, null, [], harcama.Id));
        static string Ileti(string json) => JsonDocument.Parse(json).RootElement.GetProperty("hata").GetString()!;
        Assert.Equal("Bu kart harcamasının iadesi var; gideri silinemez. Önce iadeyi Kredi Kartları ekranında gerekçeyle iptal edin.",
            Ileti(await Hata(await c.DeleteAsync($"/api/islemler/{gider}"), HttpStatusCode.Conflict)));
        var yeni = new IslemYazDto(Gun, "Kargo", 200m, "PERAKENDE", GiderTipi.KrediKarti, null, kart.Id);
        Assert.Equal("Bu kart harcamasının iadesi var; gideri değiştirilemez. Önce iadeyi Kredi Kartları ekranında gerekçeyle iptal edin.",
            Ileti(await Hata(await c.PutAsJsonAsync($"/api/islemler/{gider}", yeni), HttpStatusCode.Conflict)));
    }

    [Fact]
    public async Task Alisa_bagli_kart_harcamasi_kart_ekraninda_alisi_gosterir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        var a = await Alis(c, "Tedarik", 900m);
        a = await Post<AlisDto>(c, $"/api/alis/{a.Id}/odemeler", new AlisOdemeYaz(a.Surum, Guid.NewGuid(), Gun, 900m, kart.Id));
        var kartDto = await Kart(c, kart.Id);
        Assert.Contains($"Alış #{a.Id} ödemesine bağlı; önce alış ödemesini alıştan ayırın", await Hata(await c.PostAsJsonAsync(
            $"/api/takip/kartlar/{kart.Id}/harcamalar/{kartDto.Harcamalar.Single().Id}/iptal", new TakipIptalYaz(Guid.NewGuid(), kartDto.Surum, "Yanlış")), HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Kilitli_donemde_odenmis_takipli_kart_odemesi_tasinamaz_ve_ayrilamaz_rapor_degismez()
    {
        var gecenAy = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        var kaynak = await Onayla(c, await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, gecenAy.AddDays(2), "Kaynak", null, [new("Mal", 1000m, [new(1, 1000m)])])));
        var hedef = await Onayla(c, await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, gecenAy.AddDays(2), "Hedef", null, [new("Mal", 1000m, [new(2, 1000m)])])));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/odemeler", new AlisOdemeYaz(kaynak.Surum, Guid.NewGuid(), gecenAy.AddDays(3), 1000m, kart.Id));
        var kartDto = await Kart(c, kart.Id);
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kartDto.Surum, gecenAy.AddDays(10), 1000m));
        var rapor = $"/api/rapor/aylik?yil={gecenAy.Year}&ay={gecenAy.Month}";
        var once = await c.GetStringAsync(rapor);
        var durum = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi", Json))!;
        await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), durum.Surum, gecenAy.Year, gecenAy.Month, "Ay tamamlandı"));

        var odeme = Assert.Single(kaynak.Odemeler);
        Assert.Contains("kilitli", await Hata(await c.PutAsJsonAsync($"/api/alis/{kaynak.Id}/odemeler/{odeme.Id}",
            new AlisOdemeDuzelt(kaynak.Surum, Guid.NewGuid(), odeme.Tarih, odeme.Tutar, "Taşı", kart.Id, HedefAlisId: hedef.Id, HedefSurum: hedef.Surum)), HttpStatusCode.Conflict));
        Assert.Contains("kilitli", await Hata(await c.PostAsJsonAsync($"/api/alis/{kaynak.Id}/odemeler/{odeme.Id}/iptal",
            new AlisOdemeIptal(kaynak.Surum, Guid.NewGuid(), "Ayır", [new(2, 1000m)])), HttpStatusCode.Conflict));
        var beklenen = JsonNode.Parse(once)!.AsObject();
        beklenen["kuralSurumu"] = HesapServisi.AcikAyKurali;
        beklenen["dondurulmus"] = true;
        Assert.Equal(beklenen.ToJsonString(), await c.GetStringAsync(rapor));
        Assert.Single((await Oku(c, kaynak.Id)).Odemeler);
    }
}
