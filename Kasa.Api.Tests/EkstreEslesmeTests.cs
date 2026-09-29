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
/// Ekstre satırının mevcut kayıtla eşleşmesi (gap-coklu-giris-cift-sayim-mutabakat-1). 'Eslestir' satırı yalnız bağdır: kayıt
/// üretmez, kasa ve kart borcu değişmez; iptali de hiçbir etkiyi geri almaz. Ters sırada (ekstre önce işlenmiş) alış ödemesi
/// ekstreden gelmiş kart harcamasına (MevcutKartHarcamaId) ya da banka giderine (MevcutIslemId) bağlanır; ikinci kayıt oluşmaz ve
/// ekstre satırının sahipliği eşleşmeye döner. Ödeme alıştan ayrılınca satır kendi kaydına geri döner.
/// </summary>
public class EkstreEslesmeTests
{
    private static readonly DateOnly Today = KasaWebFactory.VarsayilanBugun;
    private static readonly DateOnly Start = new(Today.Year, Today.Month - 8, 1);
    private static readonly DateOnly Gun = Today.AddDays(-6);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static KasaWebFactory Factory() => KasaWebFactory.Sabit(Today);
    private static async Task<HttpClient> Editor(KasaWebFactory f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Start, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
        return c;
    }
    private static async Task<T> Post<T>(HttpClient c, string path, object value)
    {
        var r = await c.PostAsJsonAsync(path, value);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<string> Hata(HttpResponseMessage r, HttpStatusCode beklenen)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == beklenen, $"{r.StatusCode}: {metin}");
        return metin;
    }
    private static async Task<decimal> Kasa(HttpClient c) => (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa;
    private static async Task<KartTakipDto> Kart(HttpClient c, int id) => (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{id}"))!;
    private static Task<KartTakipDto> YeniKart(HttpClient c) => Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Ekstre kart", 100000m, 5, 25, Start, 0, []));
    private static Task<AlisDto> Alis(HttpClient c, decimal tutar, string tedarikci = "MEZAT tedarik") =>
        Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Gun, tedarikci, null, [new("Mal", tutar, [new(1, tutar)])]));

    private static async Task<EkstreBelgeDto> Belge(KasaWebFactory f, HttpClient c, string kaynak, int? kart, params (DateOnly Tarih, decimal Tutar)[] satirlar)
    {
        int id;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var d = new EkstreBelgeEntity
            {
                Kaynak = kaynak,
                Banka = "Akbank",
                HesapAdi = kaynak == "Banka" ? "İş hesabı" : "",
                KartId = kart,
                DosyaAdi = "test.pdf",
                DosyaOzeti = Guid.NewGuid().ToString(),
                Yuklendi = f.Saat!.GetUtcNow().ToUnixTimeMilliseconds(),
                SatirlarJson = JsonSerializer.Serialize(satirlar.Select((s, i) => new EkstreOkunanSatir(i + 1, 1, "Kaynak " + (i + 1), s.Tarih, "Hareket " + (i + 1), s.Tutar, "Cikis",
                    kaynak == "Banka" ? "Gider" : "KartHarcama", "Hareket", "TRY", [])))
            };
            db.EkstreBelgeler.Add(d);
            db.SaveChanges();
            id = d.Id;
        }
        return (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{id}"))!;
    }
    private static EkstreSatirYaz Satir(EkstreBelgeDto d, int no, string tur, string dagilim = "Genel", IReadOnlyList<KanalPayYaz>? paylar = null) =>
        new(no, d.Satirlar[no - 1].Tarih!.Value, d.Satirlar[no - 1].Aciklama, d.Satirlar[no - 1].Tutar!.Value, tur, dagilim, paylar ?? []);
    private static EkstreSatirYaz Eslestir(EkstreBelgeDto d, int no, string tur, int id) => Satir(d, no, "Eslestir", "Eslesme") with { EslesenKayitTuru = tur, EslesenKayitId = id };
    private static async Task<(EkstreOnizlemeDto Onizleme, EkstreBelgeDto Belge)> Kaydet(HttpClient c, EkstreBelgeDto d, params EkstreSatirYaz[] satirlar)
    {
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, satirlar);
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{d.Id}/onizleme", istek);
        return (onizleme, await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = onizleme.TekrarOnayGerekli }));
    }
    private static Task<List<EkstreEslesmeAdayiDto>> Adaylar(HttpClient c, EkstreBelgeDto d, DateOnly tarih, decimal tutar) =>
        Post<List<EkstreEslesmeAdayiDto>>(c, $"/api/ekstre-aktar/{d.Id}/eslesme-adaylari", new EkstreEslesmeAdayiSorgu(tarih, tutar));
    private static int HarcamaSayisi(KasaWebFactory f, int kart)
    {
        using var scope = f.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<KasaDbContext>().TakipHarcamalar.Count(h => h.KrediKartiId == kart && !h.Iptal);
    }

    [Fact]
    public async Task Kart_ekstresi_satiri_kartli_alis_harcamasiyla_eslesir_kayit_ve_borc_uretmez_iptali_etkisizdir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        var alis = await Alis(c, 18000m);
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Gun, 18000m, kart.Id));
        var once = await Kart(c, kart.Id);
        var h1 = Assert.Single(once.Harcamalar);
        Assert.Equal(alis.Odemeler[0].IslemId, h1.IslemId);
        var kasa = await Kasa(c);

        // Banka aynı işlemi bir gün sonra basmış: aday listesinde alışın harcaması var.
        var belge = await Belge(f, c, "Kart", kart.Id, (Gun.AddDays(1), 18000m), (Gun.AddDays(2), 18000m));
        var aday = Assert.Single(await Adaylar(c, belge, Gun.AddDays(1), 18000m));
        Assert.Equal(("KartHarcama", h1.Id, alis.Id, kart.Id), (aday.Tur, aday.Id, aday.AlisId, aday.KrediKartiId));
        Assert.Empty(await Adaylar(c, belge, Gun.AddDays(4), 18000m));
        Assert.Empty(await Adaylar(c, belge, Gun.AddDays(1), 18000.01m));

        var (onizleme, sonuc) = await Kaydet(c, belge, Eslestir(belge, 1, "KartHarcama", h1.Id));
        Assert.Equal(0m, onizleme.KasaEtkisi);
        Assert.False(onizleme.TekrarOnayGerekli);
        Assert.Empty(Assert.Single(onizleme.Satirlar).Dagilimlar);
        var kayit = Assert.Single(sonuc.Kayitlar);
        Assert.Equal(("Eslestir", "KartHarcama", h1.Id, "Eslesti"), (kayit.IslemTuru, kayit.EslesmeTuru, kayit.EslesmeId, kayit.EslesmeDurumu));
        Assert.Equal((kart.Id, (int?)null, (int?)null, (int?)null), (kayit.KrediKartiId, kayit.IslemId, kayit.KartHarcamaId, kayit.KartOdemeId));
        var sonra = await Kart(c, kart.Id);
        Assert.Equal(1, HarcamaSayisi(f, kart.Id));
        Assert.Equal(18000m, sonra.Borc);
        Assert.Equal(kasa, await Kasa(c));
        Assert.Equal(once.Surum, sonra.Surum);

        // Aynı harcama ikinci bir satıra bağlanamaz; aday listesinden de düşer.
        Assert.Empty(await Adaylar(c, belge, Gun.AddDays(2), 18000m));
        Assert.Contains("başka bir ekstre satırıyla", await Hata(await c.PostAsJsonAsync($"/api/ekstre-aktar/{sonuc.Id}/onizleme",
            new EkstreKaydetYaz(Guid.NewGuid(), sonuc.Surum, [Eslestir(sonuc, 2, "KartHarcama", h1.Id)])), HttpStatusCode.Conflict));
        // Tutar ya da tarih penceresi tutmayan kayıt eşleşmez.
        var uzak = Eslestir(sonuc, 2, "KartHarcama", h1.Id) with { Tarih = Gun.AddDays(4) };
        Assert.Contains("eşleşmiyor", await Hata(await c.PostAsJsonAsync($"/api/ekstre-aktar/{sonuc.Id}/onizleme", new EkstreKaydetYaz(Guid.NewGuid(), sonuc.Surum, [uzak])), HttpStatusCode.BadRequest));

        // İptal yalnız bağı kaldırır: harcama, borç ve kasa aynen kalır; kayıt yeniden aday olur.
        sonuc = await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{sonuc.Id}/kayitlar/{kayit.Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış satır"));
        Assert.True(Assert.Single(sonuc.Kayitlar).Iptal);
        Assert.Equal(1, HarcamaSayisi(f, kart.Id));
        Assert.Equal(18000m, (await Kart(c, kart.Id)).Borc);
        Assert.Equal(kasa, await Kasa(c));
        Assert.Single(await Adaylar(c, belge, Gun.AddDays(2), 18000m));
    }

    [Fact]
    public async Task Ters_sirada_alis_odemesi_ekstreden_gelen_kart_harcamasina_baglanir_ayrilinca_ekstreye_doner()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        var belge = await Belge(f, c, "Kart", kart.Id, (Gun.AddDays(1), 18000m));
        var (_, sonuc) = await Kaydet(c, belge, Satir(belge, 1, "KartHarcama", "Ozel", [new(1, 18000m)]));
        var satir = Assert.Single(sonuc.Kayitlar);
        var h2 = satir.KartHarcamaId!.Value;
        var kasa = await Kasa(c);

        // Bağlanabilir kart harcamaları ucu harcamayı ekstre kaynağıyla listeler.
        var liste = (await c.GetFromJsonAsync<List<BaglanabilirKartHarcamasiDto>>($"/api/alis/baglanabilir-kart-harcamalari?krediKartiId={kart.Id}&tutar=18000"))!;
        Assert.Equal((h2, satir.Id), (Assert.Single(liste).Id, liste[0].EkstreKayitId));
        Assert.Empty((await c.GetFromJsonAsync<List<BaglanabilirKartHarcamasiDto>>($"/api/alis/baglanabilir-kart-harcamalari?krediKartiId={kart.Id}&tutar=17000"))!);

        var alis = await Alis(c, 18000m);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/alis/{alis.Id}/odemeler",
            new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Gun.AddDays(1), 18000m, kart.Id, MevcutIslemId: 1, MevcutKartHarcamaId: h2))).StatusCode);
        Assert.Contains("eşleşmiyor", await Hata(await c.PostAsJsonAsync($"/api/alis/{alis.Id}/odemeler",
            new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Gun, 18000m, kart.Id, MevcutKartHarcamaId: h2)), HttpStatusCode.Conflict));
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Gun.AddDays(1), 18000m, kart.Id, MevcutKartHarcamaId: h2));

        // İkinci harcama oluşmaz, borç çift sayılmaz, alış kapanır; satır eşleşmeye döner.
        Assert.Equal(0m, alis.Kalan);
        var odeme = Assert.Single(alis.Odemeler);
        var kartSonra = await Kart(c, kart.Id);
        Assert.Equal(1, HarcamaSayisi(f, kart.Id));
        Assert.Equal(18000m, kartSonra.Borc);
        Assert.Equal(kasa, await Kasa(c));
        var harcama = Assert.Single(kartSonra.Harcamalar);
        Assert.Equal((h2, (int?)odeme.IslemId, (int?)null), (harcama.Id, harcama.IslemId, harcama.EkstreKayitId));
        satir = Assert.Single((await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{belge.Id}"))!.Kayitlar);
        Assert.Equal(("KartHarcama", (int?)null, "KartHarcama", (int?)h2, "Eslesti"), (satir.IslemTuru, satir.KartHarcamaId, satir.EslesmeTuru, satir.EslesmeId, satir.EslesmeDurumu));
        Assert.Empty((await c.GetFromJsonAsync<List<BaglanabilirKartHarcamasiDto>>($"/api/alis/baglanabilir-kart-harcamalari?krediKartiId={kart.Id}"))!);

        // Ekstre satırı, kart harcaması ve düzeltme yolu alış bağını bozamaz.
        Assert.Contains($"Alış #{alis.Id}", await Hata(await c.PostAsJsonAsync($"/api/ekstre-aktar/{belge.Id}/kayitlar/{satir.Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış")), HttpStatusCode.Conflict));
        Assert.Contains($"Alış #{alis.Id}", await Hata(await c.PostAsJsonAsync($"/api/takip/kartlar/{kart.Id}/harcamalar/{h2}/iptal", new TakipIptalYaz(Guid.NewGuid(), kartSonra.Surum, "Yanlış")), HttpStatusCode.Conflict));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/alis/{alis.Id}/odemeler/{odeme.Id}",
            new AlisOdemeDuzelt(alis.Surum, Guid.NewGuid(), odeme.Tarih, odeme.Tutar, "Taşı", kart.Id))).StatusCode);

        // Ödeme alıştan ayrılınca oluşturulan gider silinir, harcama ekstre kaydına döner; borç ve kasa değişmez.
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler/{odeme.Id}/iptal", new AlisOdemeIptal(alis.Surum, Guid.NewGuid(), "Yanlış alış"));
        Assert.Equal(18000m, alis.Kalan);
        Assert.Empty(alis.Odemeler);
        kartSonra = await Kart(c, kart.Id);
        Assert.Equal((h2, (int?)null, (int?)satir.Id), (kartSonra.Harcamalar.Single().Id, kartSonra.Harcamalar.Single().IslemId, kartSonra.Harcamalar.Single().EkstreKayitId));
        Assert.Equal(18000m, kartSonra.Borc);
        Assert.Equal(kasa, await Kasa(c));
        Assert.Equal(1, HarcamaSayisi(f, kart.Id));
        using (var scope = f.Services.CreateScope())
            Assert.False(scope.ServiceProvider.GetRequiredService<KasaDbContext>().Islemler.Any(i => i.Id == odeme.IslemId));
        satir = Assert.Single((await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{belge.Id}"))!.Kayitlar);
        Assert.Equal(((int?)h2, (string?)null, (int?)null), (satir.KartHarcamaId, satir.EslesmeTuru, satir.EslesmeId));

        // Satır yeniden kendi kaydının sahibi: iptali harcamayı iptal eder.
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{belge.Id}/kayitlar/{satir.Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış satır"));
        Assert.Equal(0m, (await Kart(c, kart.Id)).Borc);
    }

    [Fact]
    public async Task Banka_ekstresi_gideri_alisa_baglanir_kasa_bir_kez_duser_ayrilinca_ekstreye_doner()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var belge = await Belge(f, c, "Banka", null, (Gun, 500m));
        var (_, sonuc) = await Kaydet(c, belge, Satir(belge, 1, "Gider"));
        var satir = Assert.Single(sonuc.Kayitlar);
        var gider = satir.IslemId!.Value;
        Assert.Equal(500m, await Kasa(c));

        // Bağlanabilir giderler ekstre giderini kaynağıyla listeler.
        var sayfa = (await c.GetFromJsonAsync<BaglanabilirGiderSayfasi>("/api/alis/baglanabilir-giderler", Json))!;
        Assert.Equal(satir.Id, Assert.Single(sayfa.Ogeler, o => o.Id == gider).EkstreKayitId);

        var alis = await Alis(c, 500m);
        var diger = await Alis(c, 800m, "Başka tedarik");
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Gun, 500m, MevcutIslemId: gider));
        Assert.Equal(0m, alis.Kalan);
        Assert.Equal(500m, await Kasa(c));
        satir = Assert.Single((await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{belge.Id}"))!.Kayitlar);
        Assert.Equal(((int?)null, "Gider", (int?)gider, "Eslesti"), (satir.IslemId, satir.EslesmeTuru, satir.EslesmeId, satir.EslesmeDurumu));
        Assert.DoesNotContain((await c.GetFromJsonAsync<BaglanabilirGiderSayfasi>("/api/alis/baglanabilir-giderler", Json))!.Ogeler, o => o.Id == gider);

        // Banka satırının tarihi ve tutarı değişmez; yalnız başka alışa taşınabilir.
        var odeme = Assert.Single(alis.Odemeler);
        Assert.Contains("değiştirilemez", await Hata(await c.PutAsJsonAsync($"/api/alis/{alis.Id}/odemeler/{odeme.Id}",
            new AlisOdemeDuzelt(alis.Surum, Guid.NewGuid(), Gun, 400m, "Tutar")), HttpStatusCode.Conflict));
        await Hata(await c.PutAsJsonAsync($"/api/alis/{alis.Id}/odemeler/{odeme.Id}",
            new AlisOdemeDuzelt(alis.Surum, Guid.NewGuid(), Gun, 500m, "Doğru alış", HedefAlisId: diger.Id, HedefSurum: diger.Surum)), HttpStatusCode.OK);
        diger = (await c.GetFromJsonAsync<List<AlisDto>>("/api/alis"))!.Single(a => a.Id == diger.Id);
        Assert.Equal(300m, diger.Kalan);
        Assert.Equal(500m, await Kasa(c));
        Assert.Contains($"Alış #{diger.Id}", await Hata(await c.PostAsJsonAsync($"/api/ekstre-aktar/{belge.Id}/kayitlar/{satir.Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış")), HttpStatusCode.Conflict));

        // Alıştan ayrılınca gider korunur ve yeniden ekstre satırınındır; kasa bir kez düşmüş kalır.
        diger = await Post<AlisDto>(c, $"/api/alis/{diger.Id}/odemeler/{odeme.Id}/iptal", new AlisOdemeIptal(diger.Surum, Guid.NewGuid(), "Alıştan ayır"));
        Assert.Equal(800m, diger.Kalan);
        Assert.Equal(500m, await Kasa(c));
        satir = Assert.Single((await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{belge.Id}"))!.Kayitlar);
        Assert.Equal(((int?)gider, (string?)null, (int?)null), (satir.IslemId, satir.EslesmeTuru, satir.EslesmeId));
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{belge.Id}/kayitlar/{satir.Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış satır"));
        Assert.Equal(1000m, await Kasa(c));
    }

    [Fact]
    public async Task Banka_satiri_gidere_ve_kart_odemesine_eslesir_kart_odemesi_iki_belge_turunde_birer_kez_eslesir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        using var r = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Gun, "Kargo", 300m, "MEZAT", GiderTipi.Cari));
        var gider = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, Gun, 250m));
        var odeme = Assert.Single(kart.Odemeler).Id;
        var kasa = await Kasa(c);

        var banka = await Belge(f, c, "Banka", null, (Gun.AddDays(2), 300m), (Gun.AddDays(-3), 250m));
        var aday = Assert.Single(await Adaylar(c, banka, Gun.AddDays(2), 300m));
        Assert.Equal(("Gider", gider, "MEZAT"), (aday.Tur, aday.Id, aday.KanalEtiketi));
        var (onizleme, sonuc) = await Kaydet(c, banka, Eslestir(banka, 1, "Gider", gider), Eslestir(banka, 2, "KartOdeme", odeme));
        Assert.Equal(0m, onizleme.KasaEtkisi);
        Assert.Equal(kasa, await Kasa(c));
        Assert.All(sonuc.Kayitlar, k => Assert.Equal("Eslesti", k.EslesmeDurumu));
        Assert.Null(sonuc.Kayitlar.Single(k => k.EslesmeTuru == "KartOdeme").KrediKartiId);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.Equal(1, db.Islemler.Count());
            Assert.Equal(1, db.TakipKartOdemeler.Count());
        }

        // Kart ödemesi kart ekstresinde de görünür: kart belgesinden bir kez daha eşleşir, aynı türden ikinci belgeden eşleşmez.
        var kartBelgesi = await Belge(f, c, "Kart", kart.Id, (Gun, 250m), (Gun, 300m));
        var kartAdayi = Assert.Single(await Adaylar(c, kartBelgesi, Gun, 250m));
        Assert.Equal(("KartOdeme", odeme), (kartAdayi.Tur, kartAdayi.Id));
        await Kaydet(c, kartBelgesi, Eslestir(kartBelgesi, 1, "KartOdeme", odeme));
        var ikinciBanka = await Belge(f, c, "Banka", null, (Gun, 250m));
        Assert.Empty(await Adaylar(c, ikinciBanka, Gun, 250m));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{ikinciBanka.Id}/onizleme",
            new EkstreKaydetYaz(Guid.NewGuid(), ikinciBanka.Surum, [Eslestir(ikinciBanka, 1, "KartOdeme", odeme)]))).StatusCode);
        // Banka satırı kart harcamasıyla, kart satırı başka kartın/kartsız kayıtla eşleşmez.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{kartBelgesi.Id}/onizleme",
            new EkstreKaydetYaz(Guid.NewGuid(), (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{kartBelgesi.Id}"))!.Surum, [Eslestir(kartBelgesi, 2, "Gider", gider)]))).StatusCode);

        // Hedef sonradan silinirse satır 'KayitYok' görünür; kasa yine yalnız silmeyle değişir.
        (await c.DeleteAsync($"/api/islemler/{gider}")).EnsureSuccessStatusCode();
        sonuc = (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{banka.Id}"))!;
        Assert.Equal("KayitYok", sonuc.Kayitlar.Single(k => k.EslesmeTuru == "Gider").EslesmeDurumu);
        Assert.Equal(kasa + 300m, await Kasa(c));
    }

    [Fact]
    public async Task Taksitli_harcamanin_her_taksidi_ayri_satirla_bir_kez_eslesir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        var ilkGun = Today.AddMonths(-3);
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, ilkGun, "Laptop", 3000m, 3, null, [new(1, 3000m)]));
        var harcama = Assert.Single(kart.Harcamalar);
        var belge = await Belge(f, c, "Kart", kart.Id, (ilkGun, 1000m), (ilkGun.AddMonths(1), 1000m), (ilkGun.AddMonths(1).AddDays(1), 1000m));
        var ilk = await Adaylar(c, belge, ilkGun, 1000m);
        Assert.All(ilk, a => Assert.Equal(("KartTaksidi", (int?)harcama.Id, (int?)3), (a.Tur, a.HarcamaId, a.TaksitSayisi)));
        var ikinci = await Adaylar(c, belge, ilkGun.AddMonths(1), 1000m);
        var t1 = ilk.Single(a => a.TaksitNo == 1);
        var t2 = ikinci.First(a => a.TaksitNo == 2);
        var (_, sonuc) = await Kaydet(c, belge, Eslestir(belge, 1, "KartTaksidi", t1.Id), Eslestir(belge, 2, "KartTaksidi", t2.Id));
        Assert.Equal(2, sonuc.Kayitlar.Count(k => k.EslesmeDurumu == "Eslesti"));
        Assert.Equal(1, HarcamaSayisi(f, kart.Id));
        Assert.Equal(3000m, (await Kart(c, kart.Id)).Borc);
        Assert.DoesNotContain(await Adaylar(c, belge, ilkGun.AddMonths(1), 1000m), a => a.Id == t2.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{belge.Id}/onizleme",
            new EkstreKaydetYaz(Guid.NewGuid(), sonuc.Surum, [Eslestir(sonuc, 3, "KartTaksidi", t2.Id)]))).StatusCode);
    }

    /// <summary>gap-coklu-giris-cift-sayim-mutabakat-6: taksitle girilmiş kartlı alış ödemesi varken kart ekstresindeki aylık taksit
    /// satırı yeni kart harcaması olarak işlenecekse önizleme onu taksit olarak adlandırır ve ayrıca onay ister. Harcama başına tek
    /// uyarı, satırın tarihine en yakın kesimli taksidi adlandırır.</summary>
    [Fact]
    public async Task Ekstredeki_taksit_satiri_yeni_harcama_olarak_islenirken_taksitli_harcamanin_taksidi_diye_uyarir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        var alis = await Alis(c, 36000m);
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Gun, 36000m, kart.Id, TaksitSayisi: 3));
        var harcama = Assert.Single((await Kart(c, kart.Id)).Harcamalar);
        const string Uyari = "taksitli harcamanın 1/3. taksidi olabilir; atlayın ya da mevcut kayıtla eşleştirin.";
        var belge = await Belge(f, c, "Kart", kart.Id, (Gun.AddDays(1), 12000m), (Gun.AddDays(1), 11999m));
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{belge.Id}/onizleme",
            new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [Satir(belge, 1, "KartHarcama", "Ozel", [new(1, 12000m)]), Satir(belge, 2, "KartHarcama", "Ozel", [new(1, 11999m)])]));
        Assert.True(onizleme.TekrarOnayGerekli);
        Assert.Equal($"Bu satır #{harcama.Id} {Uyari}", Assert.Single(onizleme.Satirlar[0].Uyarilar, u => u.Contains("taksidi olabilir")));
        Assert.DoesNotContain(onizleme.Satirlar[1].Uyarilar, u => u.Contains("taksidi olabilir"));
        // Harcama tarihinden uzak ama taksidin ekstre kesimine yakın (ekstre dönemi içinde) basan satır da tanınır.
        var kesimBelgesi = await Belge(f, c, "Kart", kart.Id, (Today, 12000m));
        var ikinci = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{kesimBelgesi.Id}/onizleme",
            new EkstreKaydetYaz(Guid.NewGuid(), kesimBelgesi.Surum, [Satir(kesimBelgesi, 1, "KartHarcama", "Ozel", [new(1, 12000m)])]));
        Assert.True(ikinci.TekrarOnayGerekli);
        Assert.Contains($"Bu satır #{harcama.Id} {Uyari}", ikinci.Satirlar[0].Uyarilar);
    }

    /// <summary>gap-coklu-giris-cift-sayim-mutabakat-5: ekstreden gelen kart harcamasına bağlı alış ödemesi de yalnız başka alışa
    /// taşınır (tarih, tutar, kart sabit); ekstre bağı ve kart harcaması aynen kalır, ayrılınca harcama ekstre kaydına döner.</summary>
    [Fact]
    public async Task Ekstreden_gelen_kart_harcamasina_bagli_odeme_dogru_alisa_tasinir_ekstre_bagi_korunur()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var kart = await YeniKart(c);
        var belge = await Belge(f, c, "Kart", kart.Id, (Gun, 700m));
        var (_, sonuc) = await Kaydet(c, belge, Satir(belge, 1, "KartHarcama", "Ozel", [new(1, 700m)]));
        var harcama = Assert.Single(sonuc.Kayitlar).KartHarcamaId!.Value;
        var yanlis = await Alis(c, 700m, "Yanlış");
        var dogru = await Alis(c, 700m, "Doğru");
        yanlis = await Post<AlisDto>(c, $"/api/alis/{yanlis.Id}/odemeler", new AlisOdemeYaz(yanlis.Surum, Guid.NewGuid(), Gun, 700m, kart.Id, MevcutKartHarcamaId: harcama));
        var odeme = Assert.Single(yanlis.Odemeler);
        var kasa = await Kasa(c);

        Assert.Contains("yalnız başka alışa taşınabilir", await Hata(await c.PutAsJsonAsync($"/api/alis/{yanlis.Id}/odemeler/{odeme.Id}",
            new AlisOdemeDuzelt(yanlis.Surum, Guid.NewGuid(), Gun, 600m, "Tutar", kart.Id, HedefAlisId: dogru.Id, HedefSurum: dogru.Surum)), HttpStatusCode.Conflict));
        await Hata(await c.PutAsJsonAsync($"/api/alis/{yanlis.Id}/odemeler/{odeme.Id}",
            new AlisOdemeDuzelt(yanlis.Surum, Guid.NewGuid(), Gun, 700m, "Doğru alış", kart.Id, HedefAlisId: dogru.Id, HedefSurum: dogru.Surum)), HttpStatusCode.OK);
        dogru = (await c.GetFromJsonAsync<List<AlisDto>>("/api/alis", Json))!.Single(a => a.Id == dogru.Id);
        Assert.Equal(0m, dogru.Kalan);
        Assert.Equal(1, HarcamaSayisi(f, kart.Id));
        Assert.Equal(700m, (await Kart(c, kart.Id)).Borc);
        Assert.Equal(kasa, await Kasa(c));
        var satir = Assert.Single((await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{belge.Id}"))!.Kayitlar);
        Assert.Equal(("KartHarcama", (int?)harcama, "Eslesti"), (satir.EslesmeTuru, satir.EslesmeId, satir.EslesmeDurumu));
        Assert.Contains($"Alış #{dogru.Id}", await Hata(await c.PostAsJsonAsync($"/api/ekstre-aktar/{belge.Id}/kayitlar/{satir.Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış")), HttpStatusCode.Conflict));

        dogru = await Post<AlisDto>(c, $"/api/alis/{dogru.Id}/odemeler/{odeme.Id}/iptal", new AlisOdemeIptal(dogru.Surum, Guid.NewGuid(), "Ayır"));
        Assert.Equal(700m, dogru.Kalan);
        satir = Assert.Single((await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{belge.Id}"))!.Kayitlar);
        Assert.Equal(((int?)harcama, (string?)null), (satir.KartHarcamaId, satir.EslesmeTuru));
        Assert.Equal(700m, (await Kart(c, kart.Id)).Borc);
    }
}
