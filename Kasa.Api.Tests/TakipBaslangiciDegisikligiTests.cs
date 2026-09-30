using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Takip başlangıcı değişikliği (gap-veri-degismezleri-patlama-yaricapi-5). Gider üretmeyen mali kayıtlar da
/// (takipli kart açılışı ve ödemesi, PDF'ten alınan banka geliri, kasa sayımı) başlangıcı sabitler: başlangıç
/// ileri alınsaydı bu kayıtlar ilk dönemden önceye düşer, raporlardan sessizce çıkardı. Değişiklik açıklayıcı
/// 409 ile reddedilir ve raporlar değişmez. Tarihler sabittir.
/// </summary>
public class TakipBaslangiciDegisikligiTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 25);
    private static readonly DateOnly Baslangic = new(2026, 9, 14);
    private static readonly DateOnly YeniBaslangic = new(2026, 9, 21);

    private static async Task<HttpClient> Editor(KasaWebFactory f)
    {
        var c = await f.EditorClientAsync();
        (await Ayar(c, Baslangic)).EnsureSuccessStatusCode();
        return c;
    }
    private static Task<HttpResponseMessage> Ayar(HttpClient c, DateOnly baslangic) =>
        c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = baslangic, kasaAcilisDevri = 10_000m });
    private static async Task<T> Post<T>(HttpClient c, string path, object body)
    {
        var r = await c.PostAsJsonAsync(path, body);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }
    private static Task<KartTakipDto> Kart(HttpClient c) =>
        Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Takipli kart", 50_000m, 5, 25, Baslangic, 0, []));
    /// <summary>Yüklenmiş banka ekstresine (belge doğrudan yazılır) tek Gelir satırını önizleme ve onayla işler.</summary>
    private static async Task<EkstreBelgeDto> EkstreGeliri(KasaWebFactory f, HttpClient c, DateOnly tarih, decimal tutar)
    {
        int id;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var belge = new EkstreBelgeEntity
            {
                Kaynak = "Banka",
                Banka = "Akbank",
                HesapAdi = "İş hesabı",
                DosyaAdi = "ekstre.pdf",
                DosyaOzeti = Guid.NewGuid().ToString(),
                Yuklendi = f.Saat!.GetUtcNow().ToUnixTimeMilliseconds(),
                SatirlarJson = JsonSerializer.Serialize(new[] { new EkstreOkunanSatir(1, 1, "Kaynak 1", tarih, "Banka geliri", tutar, "Giris", "Gelir", "Hareket", "TRY", []) })
            };
            db.EkstreBelgeler.Add(belge);
            db.SaveChanges();
            id = belge.Id;
        }
        var doc = (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{id}"))!;
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), doc.Surum, [new EkstreSatirYaz(1, tarih, "Banka geliri", tutar, "Gelir", "Genel", [])]);
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{id}/onizleme", istek);
        return await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{id}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = true });
    }
    private static async Task<string> Raporlar(HttpClient c) =>
        await c.GetStringAsync("/api/rapor/panel") + await c.GetStringAsync("/api/rapor/haftalik") + await c.GetStringAsync("/api/rapor/aylik?yil=2026&ay=9");
    private static async Task Reddedilir(HttpClient c, string kayitTuru)
    {
        var once = await Raporlar(c);
        var r = await Ayar(c, YeniBaslangic);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var hata = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString()!;
        Assert.Contains("takip başlangıcı değiştirilemez", hata);
        Assert.Contains(kayitTuru, hata);
        Assert.Equal(Baslangic, (await c.GetFromJsonAsync<JsonElement>("/api/ayarlar")).GetProperty("takipBaslangic").Deserialize<DateOnly>());
        Assert.Equal(once, await Raporlar(c));
    }

    [Fact]
    public async Task Kart_odemesi_ve_ekstre_geliri_varken_baslangic_ileri_alinamaz_raporlar_degismez()
    {
        // Bulgu senaryosu: elle gider yok; takipli kart açılmış, 17'sinde 5.000 TL kart ödemesi ve 16'sında
        // PDF'ten 20.000 TL banka geliri kaydedilmiş.
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var kart = await Kart(c);
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, new(2026, 9, 17), 5_000m));
        await EkstreGeliri(f, c, new(2026, 9, 16), 20_000m);
        Assert.Equal(25_000m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel", cancellationToken: TestContext.Current.CancellationToken))!.GuncelKasa);
        await Reddedilir(c, "takipli kart");
    }

    [Fact]
    public async Task Yalniz_takipli_kart_acilisi_baslangici_sabitler()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        await Kart(c);
        await Reddedilir(c, "takipli kart");
    }

    [Fact]
    public async Task Yalniz_ekstreden_alinan_banka_geliri_baslangici_sabitler()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        await EkstreGeliri(f, c, new(2026, 9, 16), 20_000m);
        Assert.Equal(30_000m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel", cancellationToken: TestContext.Current.CancellationToken))!.GuncelKasa);
        await Reddedilir(c, "ekstre kaydı");
    }

    [Fact]
    public async Task Yalniz_kasa_sayimi_baslangici_sabitler()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.KasaKontrolleri.Add(new KasaKontrolEntity { Kaydedildi = f.Saat!.GetUtcNow().ToUnixTimeMilliseconds(), SistemBakiye = 10_000m, GercekBakiye = 10_000m, Fark = 0m });
            db.SaveChanges();
        }
        await Reddedilir(c, "kasa sayımı");
    }

    [Fact]
    public async Task Iptal_edilmis_ekstre_kaydi_baslangici_sabitlemez()
    {
        // İptal edilen satırın mali etkisi yoktur; başlangıç değişikliği raporlardan kayıt düşürmez.
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var doc = await EkstreGeliri(f, c, new(2026, 9, 16), 20_000m);
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{doc.Id}/kayitlar/{Assert.Single(doc.Kayitlar).Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış hesap"));
        (await Ayar(c, YeniBaslangic)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Bos_veritabaninda_baslangic_ileri_ve_geri_alinabilir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        (await Ayar(c, YeniBaslangic)).EnsureSuccessStatusCode();
        (await Ayar(c, Baslangic)).EnsureSuccessStatusCode();
        Assert.Equal(Baslangic, (await c.GetFromJsonAsync<JsonElement>("/api/ayarlar", cancellationToken: ct)).GetProperty("takipBaslangic").Deserialize<DateOnly>());
    }
}
