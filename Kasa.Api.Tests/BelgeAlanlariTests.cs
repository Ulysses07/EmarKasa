using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Servisler;
using static Kasa.Api.Tests.PaketFYardimci;

namespace Kasa.Api.Tests;

/// <summary>İşlemin belge alanları (belge türü, belge no, fatura bekleniyor): isteğe bağlı, eski istemci uyumlu.</summary>
public class BelgeAlanlariTests : IClassFixture<PaketFFactory>
{
    private readonly PaketFFactory _f;
    public BelgeAlanlariTests(PaketFFactory f) => _f = f;

    private record GecmisSatir(int Id, string Tur, int? KayitId, string Eylem, string Ozet, string? EskiJson, bool GeriAlinabilir);

    [Fact]
    public async Task Belge_alanlari_kaydedilir_bos_belge_no_null_olur_kirpilir()
    {
        var c = await _f.EditorClientAsync();
        var a = await IslemEkleAsync(c, "Market", 100m, belgeTuru: "EFatura", belgeNo: "  GIB2026000000123 ", faturaBekleniyor: false);
        Assert.Equal("EFatura", a.BelgeTuru);
        Assert.Equal("GIB2026000000123", a.BelgeNo);

        var b = await IslemEkleAsync(c, "Market", 50m, belgeTuru: "Fis", belgeNo: "   ", faturaBekleniyor: true);
        Assert.Equal("Fis", b.BelgeTuru);
        Assert.Null(b.BelgeNo);
        Assert.True(b.FaturaBekleniyor);

        var liste = await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler?baslangic=2026-09-10&bitis=2026-09-10");
        Assert.Contains(liste!, i => i.Id == a.Id && i.BelgeTuru == "EFatura" && i.BelgeNo == "GIB2026000000123");
    }

    [Fact]
    public async Task Eski_istemci_belge_alanlarini_gondermez_bugunku_gibi_calisir_ve_duzeltme_belgeyi_silmez()
    {
        var c = await _f.EditorClientAsync();
        // Eski istemci gövdesi: belge alanları yok.
        var r = await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-09-11", cari = "A", tutarTl = 10m, kanal = "MEZAT", tip = "Cari", not = (string?)null });
        r.EnsureSuccessStatusCode();
        var eski = (await r.Content.ReadFromJsonAsync<IslemYanit>())!;
        Assert.Null(eski.BelgeTuru);
        Assert.Null(eski.BelgeNo);
        Assert.False(eski.FaturaBekleniyor);

        // Yeni istemci belgeyi girer.
        (await c.PutAsJsonAsync($"/api/islemler/{eski.Id}", new
        {
            tarih = "2026-09-11", cari = "A", tutarTl = 10m, kanal = "MEZAT", tip = "Cari",
            belgeTuru = "EArsiv", belgeNo = "EA-1", faturaBekleniyor = true,
        })).EnsureSuccessStatusCode();

        // Eski istemci tutarı düzeltir: belge alanları gövdede yok → korunur.
        var p = await c.PutAsJsonAsync($"/api/islemler/{eski.Id}", new { tarih = "2026-09-11", cari = "A", tutarTl = 12m, kanal = "MEZAT", tip = "Cari", not = "düzeltme" });
        p.EnsureSuccessStatusCode();
        var son = (await p.Content.ReadFromJsonAsync<IslemYanit>())!;
        Assert.Equal(12m, son.TutarTl);
        Assert.Equal("EArsiv", son.BelgeTuru);
        Assert.Equal("EA-1", son.BelgeNo);
        Assert.True(son.FaturaBekleniyor);

        // Yalnız belge no gönderilirse tür ve bekleniyor korunur; null göndermek alanı temizler.
        var q = await c.PutAsJsonAsync($"/api/islemler/{eski.Id}", new { tarih = "2026-09-11", cari = "A", tutarTl = 12m, kanal = "MEZAT", tip = "Cari", belgeNo = (string?)null });
        var q2 = (await q.Content.ReadFromJsonAsync<IslemYanit>())!;
        Assert.Null(q2.BelgeNo);
        Assert.Equal("EArsiv", q2.BelgeTuru);
        Assert.True(q2.FaturaBekleniyor);
    }

    [Theory]
    [InlineData("\"belgeTuru\":\"Fatura\"", null)]            // bilinmeyen ad: JSON bağlama hatası
    [InlineData("\"belgeTuru\":99", "Geçersiz belge türü.")]
    public async Task Gecersiz_belge_turu_reddedilir(string alan, string? mesaj)
    {
        var c = await _f.EditorClientAsync();
        var govde = "{\"tarih\":\"2026-09-12\",\"cari\":\"A\",\"tutarTl\":5,\"kanal\":\"MEZAT\",\"tip\":\"Cari\"," + alan + "}";
        var r = await c.PostAsync("/api/islemler", new StringContent(govde, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        if (mesaj is not null) Assert.Equal(mesaj, await HataAsync(r));
    }

    [Fact]
    public async Task Belge_no_sinirlari()
    {
        var c = await _f.EditorClientAsync();
        var uzun = await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-09-12", cari = "A", tutarTl = 5m, kanal = "MEZAT", tip = "Cari", belgeNo = new string('X', 51) });
        Assert.Equal(HttpStatusCode.BadRequest, uzun.StatusCode);
        Assert.Equal("Belge no en fazla 50 karakter olabilir.", await HataAsync(uzun));
        var kontrol = await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-09-12", cari = "A", tutarTl = 5m, kanal = "MEZAT", tip = "Cari", belgeNo = "A\u0007B" });
        Assert.Equal(HttpStatusCode.BadRequest, kontrol.StatusCode);
        var tam = await IslemEkleAsync(c, "A", 5m, "2026-09-12", belgeNo: new string('X', 50));
        Assert.Equal(50, tam.BelgeNo!.Length);
    }

    /// <summary>
    /// Bulgu: "Belgesiz" + "fatura bekleniyor" birlikte kaydedilebiliyordu; fatura gelse de ödeme ayın belgesiz
    /// toplamında ve muhasebeci listesinde Belgesiz kalıyordu. İkisi birlikte olamaz.
    /// </summary>
    [Fact]
    public async Task Belgesiz_ile_fatura_bekleniyor_birlikte_kaydedilemez()
    {
        var c = await _f.EditorClientAsync();
        var ekle = await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-09-12", cari = "A", tutarTl = 5m, kanal = "MEZAT", tip = "Cari", belgeTuru = "Belgesiz", faturaBekleniyor = true });
        Assert.Equal(HttpStatusCode.BadRequest, ekle.StatusCode);
        Assert.Equal(BelgeKurallari.BelgesizBekleniyorMesaji, await HataAsync(ekle));

        var i = await IslemEkleAsync(c, "A", 5m, "2026-09-12", faturaBekleniyor: true);
        var belge = await c.PutAsJsonAsync($"/api/islemler/{i.Id}/belge", new { belgeTuru = "Belgesiz", belgeNo = (string?)null, faturaBekleniyor = true });
        Assert.Equal(HttpStatusCode.BadRequest, belge.StatusCode);
        var duzelt = await c.PutAsJsonAsync($"/api/islemler/{i.Id}", new { tarih = "2026-09-12", cari = "A", tutarTl = 5m, kanal = "MEZAT", tip = "Cari", belgeTuru = "Belgesiz", belgeNo = (string?)null, faturaBekleniyor = true });
        Assert.Equal(HttpStatusCode.BadRequest, duzelt.StatusCode);

        // Belgesiz tek başına ve türsüz "fatura bekleniyor" geçerlidir.
        Assert.Equal("Belgesiz", (await IslemEkleAsync(c, "A", 5m, "2026-09-12", belgeTuru: "Belgesiz")).BelgeTuru);
        Assert.True(_f.Db(db => db.Islemler.Single(x => x.Id == i.Id).FaturaBekleniyor));
    }

    [Fact]
    public async Task Belge_ucu_yalniz_belge_alanlarini_gunceller_izleyici_yazamaz()
    {
        var c = await _f.EditorClientAsync();
        var i = await IslemEkleAsync(c, "B", 250m, "2026-09-13", faturaBekleniyor: true);
        var r = await c.PutAsJsonAsync($"/api/islemler/{i.Id}/belge", new { belgeTuru = "EFatura", belgeNo = " F-77 ", faturaBekleniyor = false });
        r.EnsureSuccessStatusCode();
        var y = (await r.Content.ReadFromJsonAsync<IslemYanit>())!;
        Assert.Equal(("EFatura", "F-77", false, 250m, "B"), (y.BelgeTuru, y.BelgeNo, y.FaturaBekleniyor, y.TutarTl, y.Cari));

        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/islemler/999999/belge", new { faturaBekleniyor = false })).StatusCode);
        var izleyici = await _f.IzleyiciAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PutAsJsonAsync($"/api/islemler/{i.Id}/belge", new { faturaBekleniyor = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().PutAsJsonAsync($"/api/islemler/{i.Id}/belge", new { faturaBekleniyor = true })).StatusCode);
    }

    [Fact]
    public async Task Gecmise_turkce_yazilir_silinip_geri_alinan_islem_belgesiyle_doner()
    {
        var c = await _f.EditorClientAsync();
        var i = await IslemEkleAsync(c, "X", 40m, "2026-09-14");
        (await c.PutAsJsonAsync($"/api/islemler/{i.Id}/belge", new { belgeTuru = "Makbuz", belgeNo = "M-1", faturaBekleniyor = false })).EnsureSuccessStatusCode();

        var gecmis = await c.GetFromJsonAsync<List<GecmisSatir>>("/api/gecmis?limit=50");
        var guncelleme = gecmis!.First(g => g.KayitId == i.Id && g.Eylem == "Güncellendi");
        Assert.Contains("Belge türü: — → Makbuz", guncelleme.Ozet);
        Assert.Contains("Belge no: — → M-1", guncelleme.Ozet);

        (await c.DeleteAsync($"/api/islemler/{i.Id}")).EnsureSuccessStatusCode();
        gecmis = await c.GetFromJsonAsync<List<GecmisSatir>>("/api/gecmis?limit=50");
        var silme = gecmis!.First(g => g.KayitId == i.Id && g.Eylem == "Silindi");
        var geri = await c.PostAsync($"/api/gecmis/{silme.Id}/geri-al", null);
        geri.EnsureSuccessStatusCode();
        var yeni = (await geri.Content.ReadFromJsonAsync<IslemYanit>())!;
        Assert.Equal(i.Id, yeni.Id);   // geri alınan kayıt eski Id'siyle döner (Id ile tutulan bağlar kopmasın)
        Assert.Equal(("Makbuz", "M-1"), (yeni.BelgeTuru, yeni.BelgeNo));
    }
}
