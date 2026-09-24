using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Kasa.Api.Tests.PaketB;

namespace Kasa.Api.Tests;

/// <summary>
/// Paket B ay kilidi + paket F belge alanları: fatura çoğu zaman ay kapandıktan sonra gelir.
/// Kilitli aydaki işlemde yalnız belge bilgisi değişebilir; tutar, tarih, kanal gibi alanlar yine 409.
/// </summary>
public class KilitliAydaBelgeTests : IClassFixture<PaketBFactory>
{
    private readonly PaketBFactory _f;
    public KilitliAydaBelgeTests(PaketBFactory f) => _f = f;

    [Fact]
    public async Task Kilitli_aydaki_isleme_fatura_geldi_yazilir_para_alani_yine_kilitli()
    {
        _f.Temizle();
        _f.Saat.Ayarla(new DateOnly(2026, 9, 24));
        var c = await _f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 1));
        var id = await IslemEkle(c, new DateOnly(2026, 8, 10), 200m);
        (await c.PutAsJsonAsync($"/api/islemler/{id}/belge", new { belgeTuru = (string?)null, belgeNo = (string?)null, faturaBekleniyor = true }))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/ay-kapanisi/kilitle", new { yil = 2026, ay = 8 })).StatusCode);

        // Fatura takibinden "Fatura geldi".
        var r = await c.PutAsJsonAsync($"/api/islemler/{id}/belge", new { belgeTuru = "EFatura", belgeNo = "F-1", faturaBekleniyor = false });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var e = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("F-1", e.GetProperty("belgeNo").GetString());
        Assert.False(e.GetProperty("faturaBekleniyor").GetBoolean());

        // İşlem formundan yalnız belge değişirse de serbest; tutar da değişirse kilit.
        var ayni = new { tarih = "2026-08-10", cari = "X", tutarTl = 200m, kanal = "MEZAT", tip = "Cari", belgeTuru = "EFatura", belgeNo = "F-2", faturaBekleniyor = false };
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/islemler/{id}", ayni)).StatusCode);
        var tutarli = ayni with { tutarTl = 250m, belgeNo = "F-3" };
        var kilitli = await c.PutAsJsonAsync($"/api/islemler/{id}", tutarli);
        Assert.Equal(HttpStatusCode.Conflict, kilitli.StatusCode);
        Assert.Contains("Ağustos 2026", await HataMetni(kilitli));
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/islemler/{id}")).StatusCode);

        var son = await Oku(c, "/api/islemler?baslangic=2026-08-01&bitis=2026-08-31");
        var satir = son.EnumerateArray().Single();
        Assert.Equal(200m, satir.GetProperty("tutarTl").GetDecimal());
        Assert.Equal("F-2", satir.GetProperty("belgeNo").GetString());
    }
}
