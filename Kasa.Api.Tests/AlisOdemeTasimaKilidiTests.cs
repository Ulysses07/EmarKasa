using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Servisler;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.MonthlyExpenseTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Ödeme taşıma ve ay kilidi (purchase-2): onaylı alışta ödemelerin kanal payları Id sırasıyla kümülatif dağıtılır. Tarih,
/// tutar ve kart aynı kalıp yalnız hedef alış değişen "saf taşıma", KAYNAK alışta taşınan ödemeden sonra girilmiş kilitli dönem
/// ödemelerinin paylarını yeniden hesaplatır; hedef alışa girdiği sıra da hedefteki sonraki ödemeleri etkiler. İkisi de
/// reddedilir (409) ve kilitli ayın raporu birebir aynı kalır. Etkilenen kilitli ödeme yoksa taşıma serbesttir.
/// </summary>
public class AlisOdemeTasimaKilidiTests
{
    private static DateOnly Old => Month.AddMonths(-1);

    /// <summary>LockedPeriodTests'teki kuruş örneği: onaylı alış 0,03 TL (kanal1 0,01, kanal2 0,02); önce bugün tarihli P1 (düşük
    /// Id), sonra geçen ay tarihli P2 girilir. P2'nin payı kanal1'dedir; P1 alıştan çıkarsa kanal2'ye geçerdi.</summary>
    private static async Task<(AlisDto Alis, AlisOdemeDto P1)> KurusAlisi(HttpClient c, bool onayla = true)
    {
        var alis = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Old, "Kuruş", null, [new("Mal", .03m, [new(1, .01m), new(2, .02m)])]));
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Today, .01m));
        var p1 = alis.Odemeler.Single();
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Old, .01m));
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/gonder", new AlisDurumYaz(alis.Surum));
        if (onayla) alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/onayla", new AlisDurumYaz(alis.Surum));
        return (alis, p1);
    }

    private static Task<AlisDto> Hedef(HttpClient c) => Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Today, "Hedef", null, [new("Mal", 1m, [new(1, 1m)])]));

    private static Task<HttpResponseMessage> Tasi(HttpClient c, AlisDto kaynak, AlisOdemeDto odeme, AlisDto hedef)
        => c.PutAsJsonAsync($"/api/alis/{kaynak.Id}/odemeler/{odeme.Id}",
            new AlisOdemeDuzelt(kaynak.Surum, Guid.NewGuid(), odeme.Tarih, odeme.Tutar, "Yanlış alışa yazılmış", HedefAlisId: hedef.Id, HedefSurum: hedef.Surum));

    [Fact]
    public async Task Saf_tasima_kaynak_alistaki_kilitli_odemenin_kurusunu_degistiremez()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        var (alis, p1) = await KurusAlisi(c);
        var hedef = await Hedef(c);
        var once = await c.GetStringAsync(Rapor);
        await Kapat(c);

        using var r = await Tasi(c, alis, p1, hedef);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("kilitli", await AlisTestYardimcisi.Hata(r));
        await RaporDegismedi(f, c, once);
        var sonra = (await c.GetFromJsonAsync<AlisDto[]>("/api/alis"))!;
        Assert.Equal(2, sonra.Single(a => a.Id == alis.Id).Odemeler.Count);
        Assert.Empty(sonra.Single(a => a.Id == hedef.Id).Odemeler);
    }

    [Fact]
    public async Task Hedef_alista_kilitli_sonraki_odemenin_payini_degistiren_tasima_reddedilir()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        // Kaynak onaysız: taşınan ödeme kaynağın kilitli ödemesini etkilemez. Hedef onaylı ve taşınan ödemeden SONRA girilmiş
        // kilitli ödemesi var: taşınan (düşük Id) ödeme hedefte onun önüne girer ve kuruşunu kaydırırdı.
        var kaynak = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Today, "Kaynak", null, [new("Mal", 1m, [new(1, 1m)])]));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/odemeler", new AlisOdemeYaz(kaynak.Surum, Guid.NewGuid(), Today, .01m));
        var tasinan = kaynak.Odemeler.Single();
        var hedef = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Old, "Hedef", null, [new("Mal", .03m, [new(1, .01m), new(2, .02m)])]));
        hedef = await Post<AlisDto>(c, $"/api/alis/{hedef.Id}/odemeler", new AlisOdemeYaz(hedef.Surum, Guid.NewGuid(), Old, .01m));
        hedef = await Post<AlisDto>(c, $"/api/alis/{hedef.Id}/gonder", new AlisDurumYaz(hedef.Surum));
        hedef = await Post<AlisDto>(c, $"/api/alis/{hedef.Id}/onayla", new AlisDurumYaz(hedef.Surum));
        var once = await c.GetStringAsync(Rapor);
        await Kapat(c);

        using var r = await Tasi(c, kaynak, tasinan, hedef);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        await RaporDegismedi(f, c, once);
    }

    [Fact]
    public async Task Etkilenen_kilitli_odeme_yoksa_tasima_serbesttir_ve_kilitli_rapor_degismez()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        // Onaysız kaynakta pay türetilmez (dağılım bekliyor): P1'in çıkması kilitli P2'nin kasaya etkisini değiştirmez.
        var (alis, p1) = await KurusAlisi(c, onayla: false);
        var hedef = await Hedef(c);
        var once = await c.GetStringAsync(Rapor);
        await Kapat(c);

        using var r = await Tasi(c, alis, p1, hedef);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        await RaporDegismedi(f, c, once);
        Assert.Single((await c.GetFromJsonAsync<AlisDto[]>("/api/alis"))!.Single(a => a.Id == hedef.Id).Odemeler);

        // Onaylı kaynakta da kilitli dönem ödemesinden SONRA girilmiş açık ödeme serbestçe taşınır.
        var kaynak = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Today, "Onaylı", null, [new("Mal", 1m, [new(1, .4m), new(2, .6m)])]));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/odemeler", new AlisOdemeYaz(kaynak.Surum, Guid.NewGuid(), Today, .3m));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/odemeler", new AlisOdemeYaz(kaynak.Surum, Guid.NewGuid(), Today, .3m));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/gonder", new AlisDurumYaz(kaynak.Surum));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/onayla", new AlisDurumYaz(kaynak.Surum));
        hedef = (await c.GetFromJsonAsync<AlisDto[]>("/api/alis"))!.Single(a => a.Id == hedef.Id);
        using var serbest = await Tasi(c, kaynak, kaynak.Odemeler.OrderBy(o => o.Id).Last(), hedef);
        Assert.True(serbest.IsSuccessStatusCode, await serbest.Content.ReadAsStringAsync());
        await RaporDegismedi(f, c, once);
    }

    private static string Rapor => $"/api/rapor/aylik?yil={Old.Year}&ay={Old.Month}";

    private static async Task Kapat(HttpClient c)
    {
        var durum = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi"))!;
        await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), durum.Surum, Old.Year, Old.Month, "Ay tamamlandı"));
    }

    /// <summary>Kilitli ayın dondurulmuş raporu ve canlı hesabı kapatma öncesi rapora birebir eşittir.</summary>
    private static async Task RaporDegismedi(KasaWebFactory f, HttpClient c, string kilitOncesi)
    {
        var beklenen = JsonNode.Parse(kilitOncesi)!.AsObject();
        beklenen["kuralSurumu"] = HesapServisi.AcikAyKurali; beklenen["dondurulmus"] = true;
        Assert.Equal(beklenen.ToJsonString(), await c.GetStringAsync(Rapor));
        using var scope = f.Services.CreateScope();
        var canli = scope.ServiceProvider.GetRequiredService<HesapServisi>().Aylik(Old.Year, Old.Month);
        Assert.Equal(kilitOncesi, JsonSerializer.Serialize(canli, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
