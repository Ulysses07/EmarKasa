using System.Net.Http.Json;
using Kasa.Api.Denetim;
using Kasa.Core.Kodlar;
using static Kasa.Api.Tests.AylikGiderTests;

namespace Kasa.Api.Tests;

/// <summary>Çek ve hareketleri değişiklik geçmişine düşer (docs/specs/2026-10-01-cekler.md "Uçlar"): DenetimYakalayici izlenen her
/// varlığı kendiliğinden yakalar (varlık adı tür adından: Cek, CekHareket); konum değişikliği önceki/yeni değerle, geri alınan hareket
/// silme olayıyla ve istek kimliğiyle yazılır. Yalnız sürüm sayacı değişen çek olay üretmez.</summary>
public class CekDenetimTests
{
    private static Task<List<DenetimOlayDto>> Olaylar(HttpClient c, string varlik, int id) =>
        c.GetFromJsonAsync<List<DenetimOlayDto>>($"/api/denetim?varlik={varlik}&varlikId={id}", TestContext.Current.CancellationToken)!;

    [Fact]
    public async Task Cek_konum_degisikligi_ve_geri_alinan_hareket_degisiklik_gecmisine_yazilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var yaz = new CekYaz(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Alinan, "12345", "Ziraat", "Ahmet Yılmaz", 50_000m, Today.AddDays(20), null, false, null, null);
        var cek = await Post<CekDto>(c, "/api/takip/cekler", yaz);
        var hareketIstegi = new CekHareketYaz(Guid.NewGuid(), cek.Surum, CekHareketTurleri.Tahsilat, Today, 1_000m, null, "MEZAT", null);
        cek = await Post<CekDto>(c, $"/api/takip/cekler/{cek.Id}/hareketler", hareketIstegi);
        var hareket = Assert.Single(cek.Hareketler);
        (await c.PutAsJsonAsync($"/api/takip/cekler/{cek.Id}", yaz with { IstekId = Guid.NewGuid(), Surum = cek.Surum, Konum = CekKonumlari.BankadaTahsilde },
            TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        cek = (await c.GetFromJsonAsync<CekDto>($"/api/takip/cekler/{cek.Id}", TestContext.Current.CancellationToken))!;
        var geriAl = new CekSilYaz(Guid.NewGuid(), cek.Surum);
        using (var istek = new HttpRequestMessage(HttpMethod.Delete, $"/api/takip/cekler/{cek.Id}/hareketler/son") { Content = JsonContent.Create(geriAl) })
            (await c.SendAsync(istek, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var cekOlaylari = await Olaylar(c, "Cek", cek.Id);
        Assert.Equal(["Degistir", "Ekle"], cekOlaylari.Select(o => o.Tur));
        Assert.Contains(CekKonumlari.Elde, cekOlaylari[0].OncekiJson!, StringComparison.Ordinal);
        Assert.Contains(CekKonumlari.BankadaTahsilde, cekOlaylari[0].YeniJson!, StringComparison.Ordinal);
        var hareketOlaylari = await Olaylar(c, "CekHareket", hareket.Id);
        Assert.Equal(["Sil", "Ekle"], hareketOlaylari.Select(o => o.Tur));
        Assert.Equal((geriAl.IstekId, hareketIstegi.IstekId), (hareketOlaylari[0].IstekId, hareketOlaylari[1].IstekId));
        Assert.Contains(CekHareketTurleri.Tahsilat, hareketOlaylari[0].OncekiJson!, StringComparison.Ordinal);
    }
}
