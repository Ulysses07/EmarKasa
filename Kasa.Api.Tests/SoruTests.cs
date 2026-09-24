using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static Kasa.Api.Tests.PaketEYardimci;

namespace Kasa.Api.Tests;

/// <summary>25 · Kayda soru sorma: izleyici sorar, editör cevaplar/kapatır, herkes görür; rakamlar değişmez.</summary>
public class SoruTests : IClassFixture<KasaWebFactory>
{
    private readonly KasaWebFactory _f;
    public SoruTests(KasaWebFactory f) => _f = f;

    private async Task<(HttpClient Editor, HttpClient Izleyici)> HazirlaAsync()
    {
        var editor = await EditorAsync(_f);
        await KasaWebFactory.TakipBaslangiciAyarla(editor, new DateOnly(2026, 6, 1));
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "soru-izle" })).EnsureSuccessStatusCode();
        var izleyici = await GirisliAsync(_f, null, "soru-izle");
        return (editor, izleyici);
    }

    private static async Task<int> IslemEkleAsync(HttpClient editor, decimal tutar)
    {
        var r = await editor.PostAsJsonAsync("/api/islemler", new { tarih = "2026-06-10", cari = "Market", tutarTl = tutar, kanal = "MEZAT", tip = "Cari" });
        r.EnsureSuccessStatusCode();
        return (await JsonAsync(r)).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Izleyici_isleme_soru_sorar_editor_cevaplar_herkes_gorur()
    {
        var (editor, izleyici) = await HazirlaAsync();
        var islemId = await IslemEkleAsync(editor, 1_250.5m);

        var r = await izleyici.PostAsJsonAsync("/api/sorular", new { hedefTur = "Islem", hedefId = islemId, metin = "  Bu ödeme neyin?  " });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var soru = await JsonAsync(r);
        var id = soru.GetProperty("id").GetInt32();
        Assert.Equal("Bu ödeme neyin?", soru.GetProperty("metin").GetString());
        Assert.Equal("Acik", soru.GetProperty("durum").GetString());
        Assert.Equal("viewer", soru.GetProperty("soranRol").GetString());
        Assert.Equal("10.06.2026 · Market · 1.250,50 ₺ · MEZAT", soru.GetProperty("hedefOzet").GetString());

        // İzleyici yalnız soru yazar.
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PostAsJsonAsync($"/api/sorular/{id}/cevap", new { cevap = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PostAsync($"/api/sorular/{id}/kapat", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.DeleteAsync($"/api/sorular/{id}")).StatusCode);

        var ozet = await editor.GetFromJsonAsync<JsonElement>("/api/sorular/ozet");
        Assert.True(ozet.GetProperty("acikSayisi").GetInt32() >= 1);
        Assert.Contains(ozet.GetProperty("sonAciklar").EnumerateArray(), s => s.GetProperty("id").GetInt32() == id);

        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsJsonAsync($"/api/sorular/{id}/cevap", new { cevap = " " })).StatusCode);
        var c = await editor.PostAsJsonAsync($"/api/sorular/{id}/cevap", new { cevap = "Kargo ödemesi.", kapat = true });
        c.EnsureSuccessStatusCode();

        var liste = (await izleyici.GetFromJsonAsync<List<JsonElement>>($"/api/sorular?hedefTur=Islem&hedefId={islemId}"))!;
        var gorulen = Assert.Single(liste);
        Assert.Equal("Kargo ödemesi.", gorulen.GetProperty("cevap").GetString());
        Assert.Equal("Kapali", gorulen.GetProperty("durum").GetString());
        Assert.Equal("EDİTOR", gorulen.GetProperty("cevaplayanAd").GetString());

        // Yeniden aç / kapat / sil (editör).
        Assert.Equal("Acik", (await JsonAsync(await editor.PostAsync($"/api/sorular/{id}/ac", null))).GetProperty("durum").GetString());
        Assert.Equal("Kapali", (await JsonAsync(await editor.PostAsync($"/api/sorular/{id}/kapat", null))).GetProperty("durum").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await editor.DeleteAsync($"/api/sorular/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await editor.PostAsync($"/api/sorular/{id}/kapat", null)).StatusCode);
    }

    [Fact]
    public async Task Hafta_sorusu_donem_basina_normallesir_ay_sonunda_bolunur()
    {
        var (_, izleyici) = await HazirlaAsync();
        var r = await izleyici.PostAsJsonAsync("/api/sorular", new { hedefTur = "Hafta", hafta = "2026-06-10", metin = "Bu hafta neden eksi?" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var s = await JsonAsync(r);
        Assert.Equal("2026-06-08", s.GetProperty("hafta").GetString());
        Assert.Equal("Hafta 08.06 – 14.06.2026", s.GetProperty("hedefOzet").GetString());

        var ay = await JsonAsync(await izleyici.PostAsJsonAsync("/api/sorular", new { hedefTur = "Hafta", hafta = "2026-06-30", metin = "Ay sonu?" }));
        Assert.Equal("2026-06-29", ay.GetProperty("hafta").GetString());
        Assert.Equal("Hafta 29.06 – 30.06.2026", ay.GetProperty("hedefOzet").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await izleyici.PostAsJsonAsync("/api/sorular", new { hedefTur = "Hafta", hafta = "2026-05-20", metin = "Takip öncesi" })).StatusCode);
        var liste = (await izleyici.GetFromJsonAsync<List<JsonElement>>("/api/sorular?hedefTur=Hafta&hafta=2026-06-08"))!;
        Assert.Single(liste);
    }

    [Fact]
    public async Task Cek_ve_genel_soru_hedef_dogrulamasi()
    {
        var (editor, izleyici) = await HazirlaAsync();
        var cr = await editor.PostAsJsonAsync("/api/cekler", new
        {
            yon = "Alinan", kisi = "Ahmet Yılmaz", tutar = 5_000m, duzenlemeTarihi = "2026-06-01", vadeTarihi = "2026-07-15",
            kanal = "MEZAT", durum = "Portfoyde",
        });
        cr.EnsureSuccessStatusCode();
        var cekId = (await JsonAsync(cr)).GetProperty("id").GetInt32();

        var cek = await JsonAsync(await izleyici.PostAsJsonAsync("/api/sorular", new { hedefTur = "Cek", hedefId = cekId, metin = "Tahsil edildi mi?" }));
        Assert.Equal("Alınan çek · Ahmet Yılmaz · 5.000,00 ₺ · vade 15.07.2026", cek.GetProperty("hedefOzet").GetString());

        var genel = await izleyici.PostAsJsonAsync("/api/sorular", new { hedefTur = "Genel", metin = "Ay sonu toplantısı ne zaman?" });
        Assert.Equal(HttpStatusCode.Created, genel.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(genel)).GetProperty("hedefOzet").ValueKind);

        Assert.Equal(HttpStatusCode.BadRequest, (await izleyici.PostAsJsonAsync("/api/sorular", new { hedefTur = "Islem", hedefId = 999_999, metin = "?" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await izleyici.PostAsJsonAsync("/api/sorular", new { hedefTur = "Cek", metin = "?" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await izleyici.PostAsJsonAsync("/api/sorular", new { hedefTur = "Genel", metin = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await izleyici.PostAsJsonAsync("/api/sorular", new { hedefTur = "Genel", metin = new string('a', 2001) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await izleyici.GetAsync("/api/sorular?durum=yok")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/api/sorular")).StatusCode);
    }

    [Fact]
    public async Task Sorular_hicbir_rakami_degistirmez_ve_gecmise_yazilir()
    {
        var (editor, izleyici) = await HazirlaAsync();
        var islemId = await IslemEkleAsync(editor, 300m);
        var panelOnce = await editor.GetStringAsync("/api/rapor/panel");
        var haftalikOnce = await editor.GetStringAsync("/api/rapor/haftalik");

        var id = (await JsonAsync(await izleyici.PostAsJsonAsync("/api/sorular", new { hedefTur = "Islem", hedefId = islemId, metin = "Rakam?" })))
            .GetProperty("id").GetInt32();
        (await editor.PostAsJsonAsync($"/api/sorular/{id}/cevap", new { cevap = "Doğru.", kapat = false })).EnsureSuccessStatusCode();

        Assert.Equal(panelOnce, await editor.GetStringAsync("/api/rapor/panel"));
        Assert.Equal(haftalikOnce, await editor.GetStringAsync("/api/rapor/haftalik"));

        var gecmis = (await editor.GetFromJsonAsync<List<JsonElement>>("/api/gecmis?tur=Soru"))!;
        Assert.Contains(gecmis, g => g.GetProperty("eylem").GetString() == "Eklendi" && g.GetProperty("rol").GetString() == "viewer");
        Assert.Contains(gecmis, g => g.GetProperty("eylem").GetString() == "Güncellendi" && g.GetProperty("ozet").GetString()!.Contains("Doğru."));
    }
}
