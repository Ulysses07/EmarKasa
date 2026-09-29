using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Rapor ufku (gap-okuma-yolu-maliyet-kilit-cekismesi-10, gap-veri-degismezleri-patlama-yaricapi-4): tek bir ileri
/// tarihli kayıt haftalık raporu ve dönem listesini bugün + 1 yılın ay sonundan öteye uzatmaz; ufuk dışında
/// kalan kayıt varsa haftalık raporun son dönemi veri sağlığı uyarısı taşır. Ufuk içindeki ileri tarihli kayıt
/// eskisi gibi raporu kendi tarihine kadar uzatır ve uyarı üretmez.
/// </summary>
public class RaporUfkuTests
{
    private static readonly DateOnly Bugun = KasaWebFactory.VarsayilanBugun; // 25.09.2026
    private static readonly DateOnly UfukSonu = new(2027, 9, 30);            // bugün + 1 yıl, ay sonu

    private static async Task<(KasaWebFactory F, HttpClient C)> Kur(params DateOnly[] ileriTarihler)
    {
        var f = KasaWebFactory.Sabit(Bugun);
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(2026, 1, 1), kasaAcilisDevri = 1_000m })).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(new(2026, 3, 2), "Olağan", 100m, "MEZAT", GiderTipi.Cari))).EnsureSuccessStatusCode();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        // Girdi doğrulamasından önce yazılmış (veya içe aktarılmış) aşırı ileri tarihli kayıt: doğrudan veritabanına.
        foreach (var tarih in ileriTarihler)
            db.Islemler.Add(new() { Tarih = tarih, Cari = "Yazım hatası " + tarih.Year, TutarTl = 50m, KanalId = 1, Kanal = "MEZAT", Tip = GiderTipi.Cari });
        db.SaveChanges();
        return (f, c);
    }

    [Fact]
    public async Task Tek_ileri_tarihli_kayit_haftalik_raporu_ve_donemleri_ufkun_otesine_uzatmaz_uyari_doner()
    {
        var (f, c) = await Kur(new DateOnly(2206, 6, 22));
        await using var _ = f;
        using var __ = c;
        var haftalik = JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik"))!.AsArray();
        var donemler = JsonNode.Parse(await c.GetStringAsync("/api/donemler"))!.AsArray();
        Assert.Equal(UfukSonu, DateOnly.Parse(haftalik[^1]!["donem"]!["end"]!.GetValue<string>()));
        Assert.Equal(UfukSonu, DateOnly.Parse(donemler[^1]!["end"]!.GetValue<string>()));
        Assert.Equal(haftalik.Count, donemler.Count);
        Assert.InRange(donemler.Count, 100, 130); // 21 ay; ufuksuz ≈ 11 bin dönem olurdu.
        var uyari = haftalik[^1]!["veriSagligiUyarisi"]?.GetValue<string>();
        Assert.NotNull(uyari);
        Assert.Contains("1 kayıt", uyari);
        Assert.Contains("30.09.2027", uyari);
        Assert.Contains("22.06.2206", uyari);
        // Uyarı yalnız son dönemdedir; ufuk dışı kayıt ufuk içindeki dönemlerin tutarlarını değiştirmez.
        Assert.All(haftalik.Take(haftalik.Count - 1), d => Assert.Null(d!["veriSagligiUyarisi"]));
        Assert.Equal(1_000m - 100m, haftalik[^1]!["kasaDevir"]!.GetValue<decimal>());
        Assert.Equal(900m, (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa);
    }

    [Fact]
    public async Task Yil_9998_kaydi_bile_ufku_sinirli_tutar_ve_uyari_en_gec_tarihi_verir()
    {
        var (f, c) = await Kur(new DateOnly(2206, 6, 22), new DateOnly(9998, 12, 31));
        await using var _ = f;
        using var __ = c;
        var haftalik = JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik"))!.AsArray();
        Assert.Equal(UfukSonu, DateOnly.Parse(haftalik[^1]!["donem"]!["end"]!.GetValue<string>()));
        var uyari = haftalik[^1]!["veriSagligiUyarisi"]!.GetValue<string>();
        Assert.Contains("2 kayıt", uyari);
        Assert.Contains("31.12.9998", uyari);
        Assert.Equal(UfukSonu, DateOnly.Parse(JsonNode.Parse(await c.GetStringAsync("/api/donemler"))!.AsArray()[^1]!["end"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Ufuk_icindeki_ileri_tarihli_kayit_raporu_kendi_tarihine_uzatir_ve_uyari_uretmez()
    {
        var ileri = Bugun.AddDays(300);
        var (f, c) = await Kur(ileri);
        await using var _ = f;
        using var __ = c;
        var haftalik = JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik"))!.AsArray();
        Assert.Equal(ileri, DateOnly.Parse(haftalik[^1]!["donem"]!["end"]!.GetValue<string>()));
        Assert.All(haftalik, d => Assert.False(d!.AsObject().ContainsKey("veriSagligiUyarisi")));
        Assert.Equal(1_000m - 150m, haftalik[^1]!["kasaDevir"]!.GetValue<decimal>());
    }
}
