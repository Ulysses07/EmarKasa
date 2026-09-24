using System.Net;
using System.Net.Http.Json;
using System.Text;
using static Kasa.Api.Tests.PaketFYardimci;

namespace Kasa.Api.Tests;

/// <summary>Fatura takibi (bekleyenler cari cari, ayın belge dökümü) ve muhasebeci listesi CSV'si.</summary>
public class FaturaTakibiTests : IClassFixture<PaketFFactory>
{
    private readonly PaketFFactory _f;
    public FaturaTakibiTests(PaketFFactory f) => _f = f;

    private record FIslem(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string? BelgeTuru, string? BelgeNo, bool FaturaBekleniyor, int EkSayisi);
    private record FCari(string Cari, decimal Toplam, int Adet, DateOnly EnEskiTarih, List<FIslem> Islemler);
    private record FTur(string? Tur, string Ad, decimal Toplam, int Adet);
    private record FYanit(int Yil, int Ay, List<FCari> Bekleyenler, decimal BekleyenToplam, int BekleyenAdet,
        List<FTur> AyOzeti, decimal AyToplam, int AyAdet, decimal AyBelgesizToplam, int AyBelgesizAdet);

    [Fact]
    public async Task Bekleyenler_cari_cari_en_eski_once_ay_ozeti_belge_turune_gore()
    {
        _f.Temizle();
        var c = await _f.EditorClientAsync();
        var b1 = await IslemEkleAsync(c, "B", 100m, "2026-08-20", faturaBekleniyor: true);   // önceki ay: yine bekleyenlerde
        await IslemEkleAsync(c, "b", 50m, "2026-09-03", faturaBekleniyor: true);             // aynı cari (kayıtlı yazım "B")
        await IslemEkleAsync(c, "Market", 70m, "2026-09-01", faturaBekleniyor: true);
        await IslemEkleAsync(c, "Market", 10m, "2026-09-02", belgeTuru: "EFatura", belgeNo: "F1");
        await IslemEkleAsync(c, "A", 20m, "2026-09-05", belgeTuru: "Belgesiz");
        await IslemEkleAsync(c, "A", 30m, "2026-09-30", belgeTuru: "Belgesiz");
        await IslemEkleAsync(c, "A", 999m, "2026-10-01", belgeTuru: "Belgesiz");            // sonraki ay
        await IslemEkleAsync(c, "X", 5m, "2026-09-06", belgeTuru: "Fis");
        (await c.PostAsync($"/api/islemler/{b1.Id}/ekler", IslemEkiTests.Form(IslemEkiTests.Pdf(), "irsaliye.pdf"))).EnsureSuccessStatusCode();

        var y = (await c.GetFromJsonAsync<FYanit>("/api/faturatakibi?yil=2026&ay=9"))!;
        Assert.Equal(["B", "Market"], y.Bekleyenler.Select(b => b.Cari));
        Assert.Equal((150m, 2, new DateOnly(2026, 8, 20)), (y.Bekleyenler[0].Toplam, y.Bekleyenler[0].Adet, y.Bekleyenler[0].EnEskiTarih));
        Assert.Equal(1, y.Bekleyenler[0].Islemler[0].EkSayisi);
        Assert.Equal((220m, 3), (y.BekleyenToplam, y.BekleyenAdet));

        Assert.Equal(["e-Fatura", "e-Arşiv", "Fiş", "Makbuz", "Belgesiz", "Belirtilmemiş"], y.AyOzeti.Select(o => o.Ad));
        Assert.Equal(10m, y.AyOzeti[0].Toplam);
        Assert.Equal((5m, 1), (y.AyOzeti[2].Toplam, y.AyOzeti[2].Adet));
        Assert.Equal((120m, 2), (y.AyOzeti[5].Toplam, y.AyOzeti[5].Adet));   // 50 + 70, belirtilmemiş
        Assert.Null(y.AyOzeti[5].Tur);
        Assert.Equal((50m, 2), (y.AyBelgesizToplam, y.AyBelgesizAdet));
        Assert.Equal((185m, 6), (y.AyToplam, y.AyAdet));
        Assert.Equal(y.AyToplam, y.AyOzeti.Sum(o => o.Toplam));
    }

    [Fact]
    public async Task Belgesiz_islem_yokken_bos_ozet_doner_gecersiz_ay_400()
    {
        _f.Temizle();
        var c = await _f.EditorClientAsync();
        var y = (await c.GetFromJsonAsync<FYanit>("/api/faturatakibi?yil=2026&ay=2"))!;
        Assert.Empty(y.Bekleyenler);
        Assert.Equal((0m, 0), (y.AyBelgesizToplam, y.AyBelgesizAdet));
        Assert.All(y.AyOzeti, o => Assert.Equal(0m, o.Toplam));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/faturatakibi?yil=2026&ay=13")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/disaaktar/muhasebeci.csv?yil=1999&ay=1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync("/api/faturatakibi?yil=2026&ay=9")).StatusCode);
    }

    [Fact]
    public async Task Muhasebeci_listesi_csv_belge_bilgisi_toplamlar_ve_formul_korumasi()
    {
        _f.Temizle();
        var c = await _f.EditorClientAsync();
        (await c.PostAsJsonAsync("/api/cariler", new { ad = "=HYPERLINK(\"x\")", aktif = true })).EnsureSuccessStatusCode();
        var a = await IslemEkleAsync(c, "=HYPERLINK(\"x\")", 1234.5m, "2026-09-02", belgeTuru: "EFatura", belgeNo: "+905551112233", not: "@not");
        await IslemEkleAsync(c, "A", 10m, "2026-09-01", belgeTuru: "Belgesiz");
        await IslemEkleAsync(c, "B", 5m, "2026-09-03", faturaBekleniyor: true);   // türü fatura gelince seçilir
        await IslemEkleAsync(c, "A", 99m, "2026-08-31", belgeTuru: "Belgesiz");   // önceki ay: listede yok
        (await c.PostAsync($"/api/islemler/{a.Id}/ekler", IslemEkiTests.Form(IslemEkiTests.Jpeg(), "fatura.jpg"))).EnsureSuccessStatusCode();

        var izleyici = await _f.IzleyiciAsync();   // izleyici de indirebilir
        var r = await izleyici.GetAsync("/api/disaaktar/muhasebeci.csv?yil=2026&ay=9");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("kasa-muhasebeci-2026-09.csv", r.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        var baytlar = await r.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), baytlar[..3]);
        var satirlar = Encoding.UTF8.GetString(baytlar[3..]).Split("\r\n");
        Assert.Equal("Tarih;Cari/Kalem;Kanal;Tip;Kart;Tutar;Belge türü;Belge no;Fatura bekleniyor;Ek sayısı;Not", satirlar[0]);
        Assert.Equal("01.09.2026;A;MEZAT;Cari;;10,00;Belgesiz;;;0;", satirlar[1]);
        Assert.Equal("02.09.2026;\"'=HYPERLINK(\"\"x\"\")\";MEZAT;Cari;;1234,50;e-Fatura;'+905551112233;;1;'@not", satirlar[2]);
        Assert.Equal("03.09.2026;B;MEZAT;Cari;;5,00;;;Evet;0;", satirlar[3]);
        Assert.Equal("", satirlar[4]);
        Assert.Equal("Toplam: e-Fatura (1 işlem);;;;;1234,50;;;;;", satirlar[5]);
        Assert.Equal("Toplam: Belgesiz (1 işlem);;;;;10,00;;;;;", satirlar[6]);
        Assert.Equal("Toplam: Belirtilmemiş (1 işlem);;;;;5,00;;;;;", satirlar[7]);
        Assert.Equal("Fatura bekleniyor (1 işlem);;;;;5,00;;;;;", satirlar[8]);
        Assert.Equal("Genel toplam (3 işlem);;;;;1249,50;;;;;", satirlar[9]);
    }
}
