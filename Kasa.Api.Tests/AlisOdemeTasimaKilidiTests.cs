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
/// ödemelerinin (tarihi kilitli ya da kart taksidi kilitli dönemde ödenmiş) paylarını yeniden hesaplatır: reddedilir (409) ve
/// kilitli ayın raporu birebir aynı kalır. Hedef tarafını genel kilit kuralı kapsar. Kilitli ödemeden SONRA girilmiş ödeme ve
/// etkilenen kilitli ödemesi olmayan alışlar serbestçe taşınır (kilit aşırı engellemez).
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
        if (onayla)
            alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/onayla", new AlisDurumYaz(alis.Surum));
        return (alis, p1);
    }

    private static Task<AlisDto> Hedef(HttpClient c) => Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Today, "Hedef", null, [new("Mal", 1m, [new(1, 1m)])]));

    private static Task<HttpResponseMessage> Tasi(HttpClient c, AlisDto kaynak, AlisOdemeDto odeme, AlisDto hedef)
        => c.PutAsJsonAsync($"/api/alis/{kaynak.Id}/odemeler/{odeme.Id}",
            new AlisOdemeDuzelt(kaynak.Surum, Guid.NewGuid(), odeme.Tarih, odeme.Tutar, "Yanlış alışa yazılmış", HedefAlisId: hedef.Id, HedefSurum: hedef.Surum));

    [Fact]
    public async Task Saf_tasima_kaynak_alistaki_kilitli_odemenin_kurusunu_degistiremez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
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
    public async Task Onayli_kaynakta_kilitli_odemeden_sonra_girilmis_acik_odeme_tasinir_oncesindeki_tasinamaz()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        // Onaylı kaynak: A (bugün, en düşük Id), B (geçen ay, kilitlenecek), C (bugün, en yüksek Id). Kümülatif paylarla A kanal2'yi,
        // B kanal1'i, C kanal2'yi alır. C'nin çıkması B'nin payını değiştirmez (B'den sonra gelir): kilit aşırı engellemez.
        // A'nın çıkması B'yi ilk ödeme yapar ve payını kanal2'ye kaydırırdı: 409.
        var kaynak = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Old, "Üç ödeme", null, [new("Mal", .03m, [new(1, .01m), new(2, .02m)])]));
        foreach (var tarih in new[] { Today, Old, Today })
            kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/odemeler", new AlisOdemeYaz(kaynak.Surum, Guid.NewGuid(), tarih, .01m));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/gonder", new AlisDurumYaz(kaynak.Surum));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/onayla", new AlisDurumYaz(kaynak.Surum));
        var odemeler = kaynak.Odemeler.OrderBy(o => o.Id).ToArray();
        Assert.Equal([Today, Old, Today], odemeler.Select(o => o.Tarih));
        var hedef = await Hedef(c);
        var once = await c.GetStringAsync(Rapor);
        await Kapat(c);

        using (var serbest = await Tasi(c, kaynak, odemeler[2], hedef))
            Assert.True(serbest.IsSuccessStatusCode, await serbest.Content.ReadAsStringAsync());
        await RaporDegismedi(f, c, once);

        var guncel = (await c.GetFromJsonAsync<AlisDto[]>("/api/alis"))!;
        kaynak = guncel.Single(a => a.Id == kaynak.Id);
        hedef = guncel.Single(a => a.Id == hedef.Id);
        Assert.Equal(odemeler[2].Id, Assert.Single(hedef.Odemeler).Id);
        using (var kilitli = await Tasi(c, kaynak, odemeler[0], hedef))
        {
            Assert.Equal(HttpStatusCode.Conflict, kilitli.StatusCode);
            Assert.Contains("kilitli", await AlisTestYardimcisi.Hata(kilitli));
        }
        await RaporDegismedi(f, c, once);
        Assert.Equal(2, (await c.GetFromJsonAsync<AlisDto[]>("/api/alis"))!.Single(a => a.Id == kaynak.Id).Odemeler.Count);
    }

    [Fact]
    public async Task Kilitli_donemde_kart_odemesiyle_odenmis_sonraki_kart_harcamasi_kaynaktan_tasimayi_engeller()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        // Onaylı kaynak: P1 (bugün, nakit, düşük Id) ve P2 (bugün, takipteki kartla). P2'nin tarihi açık dönemdedir ama kart
        // taksidi geçen ay tarihli bir kart ödemesiyle ödenmiştir: P2'nin kanal payı kilitli ayın kart ödemesine girmiştir.
        // P1'in çıkması P2'nin kümülatif payını kaydırırdı; genel kilit kuralı yalnız hedefi gördüğünden bunu kaynak denetimi yakalar.
        var kart = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "İş kartı", 1000m, 5, 25, Old, 0m, []));
        var kaynak = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Old, "Kartlı", null, [new("Mal", .03m, [new(1, .01m), new(2, .02m)])]));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/odemeler", new AlisOdemeYaz(kaynak.Surum, Guid.NewGuid(), Today, .01m));
        var p1 = kaynak.Odemeler.Single();
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/odemeler", new AlisOdemeYaz(kaynak.Surum, Guid.NewGuid(), Today, .01m, KrediKartiId: kart.Id));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/gonder", new AlisDurumYaz(kaynak.Surum));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/onayla", new AlisDurumYaz(kaynak.Surum));
        kart = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{kart.Id}"))!;
        Assert.Equal(Today, Assert.Single(kart.Harcamalar).Tarih);
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, Old, .01m));
        var hedef = await Hedef(c);
        var once = await c.GetStringAsync(Rapor);
        await Kapat(c);

        using var r = await Tasi(c, kaynak, p1, hedef);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("kilitli", await AlisTestYardimcisi.Hata(r));
        await RaporDegismedi(f, c, once);
        Assert.Empty((await c.GetFromJsonAsync<AlisDto[]>("/api/alis"))!.Single(a => a.Id == hedef.Id).Odemeler);
    }

    [Fact]
    public async Task Hedef_alista_kilitli_sonraki_odemenin_payini_degistiren_tasima_reddedilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        // Kaynak onaysız: taşınan ödeme kaynağın kilitli ödemesini etkilemez. Hedef onaylı ve taşınan ödemeden SONRA girilmiş
        // kilitli ödemesi var: taşınan (düşük Id) ödeme hedefte onun önüne girer ve kuruşunu kaydırırdı. Hedef tarafını genel kilit
        // kuralı (AyKilidiKurallari: kilitli dönem ödemesi olan alışa ödeme taşınamaz) kapsar; bu test o korumanın regresyonudur.
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
        await using var f = Fabrika();
        using var c = await Editor(f);
        // Onaysız kaynakta pay türetilmez (dağılım bekliyor): P1'in çıkması kilitli P2'nin kasaya etkisini değiştirmez.
        var (alis, p1) = await KurusAlisi(c, onayla: false);
        var hedef = await Hedef(c);
        var once = await c.GetStringAsync(Rapor);
        await Kapat(c);

        using var r = await Tasi(c, alis, p1, hedef);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        await RaporDegismedi(f, c, once);
        Assert.Single((await c.GetFromJsonAsync<AlisDto[]>("/api/alis"))!.Single(a => a.Id == hedef.Id).Odemeler);

        // Kilitli dönem ödemesi hiç olmayan onaylı alışlar arasında taşıma da serbesttir.
        var kaynak = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Today, "Onaylı", null, [new("Mal", 1m, [new(1, .4m), new(2, .6m)])]));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/odemeler", new AlisOdemeYaz(kaynak.Surum, Guid.NewGuid(), Today, .3m));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/odemeler", new AlisOdemeYaz(kaynak.Surum, Guid.NewGuid(), Today, .3m));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/gonder", new AlisDurumYaz(kaynak.Surum));
        kaynak = await Post<AlisDto>(c, $"/api/alis/{kaynak.Id}/onayla", new AlisDurumYaz(kaynak.Surum));
        hedef = (await c.GetFromJsonAsync<AlisDto[]>("/api/alis"))!.Single(a => a.Id == hedef.Id);
        using var serbest = await Tasi(c, kaynak, kaynak.Odemeler.OrderBy(o => o.Id).First(), hedef);
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
        beklenen["kuralSurumu"] = HesapServisi.AcikAyKurali;
        beklenen["dondurulmus"] = true;
        Assert.Equal(beklenen.ToJsonString(), await c.GetStringAsync(Rapor));
        using var scope = f.Services.CreateScope();
        var canli = scope.ServiceProvider.GetRequiredService<HesapServisi>().Aylik(Old.Year, Old.Month);
        Assert.Equal(kilitOncesi, JsonSerializer.Serialize(canli, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
