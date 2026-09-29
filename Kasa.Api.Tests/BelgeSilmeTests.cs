using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Denetim;
using Kasa.Api.Servisler;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Belge silme yumuşaktır ve iz bırakır (gap-denetim-izi-gozlemlenebilirlik-9): yükleyen rol/kimlik yüklemede yazılır; silme satırı ve
/// içeriği korur, silen, zaman ve gerekçe yazılır ve olay denetim izine düşer. Editör gerekçe vermeden silemez; alıcı yalnız kendi
/// yüklediği, ödemeye bağlı olmayan ve taslaktaki belgeyi kaldırabilir. Silinen belge varsayılan listede görünmez; editör
/// <c>?silinenler=true</c> ile görür ve indirebilir, alıcı göremez. Silinen belgeler 30 belge sınırına ve taslak kotalarına sayılmaz,
/// son 24 saatteki yükleme hacmine sayılır.
/// </summary>
public class BelgeSilmeTests
{
    private static readonly byte[] Fatura = "%PDF-1.7 ilk fis KDV haric"u8.ToArray();
    private static readonly byte[] Duzeltilmis = "%PDF-1.7 duzeltilmis fis"u8.ToArray();

    private static async Task<BelgeDto> Yukle(HttpClient c, int alisId, byte[] icerik, string ad)
    {
        using var r = await AlisTestYardimcisi.YukleYanit(c, alisId, icerik, ad);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<BelgeDto>())!;
    }

    private static Task<HttpResponseMessage> Sil(HttpClient c, int id, string? gerekce = null)
    {
        var istek = new HttpRequestMessage(HttpMethod.Delete, $"/api/belgeler/{id}");
        if (gerekce is not null)
            istek.Content = JsonContent.Create(new BelgeSilYaz(gerekce));
        return c.SendAsync(istek);
    }

    private static List<DenetimOlayEntity> Olaylar(KasaWebFactory f, int belgeId)
    {
        using var scope = f.Services.CreateScope();
        var anahtar = belgeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return scope.ServiceProvider.GetRequiredService<KasaDbContext>().DenetimOlaylari.AsNoTracking()
            .Where(o => o.Varlik == "Belge" && o.VarlikId == anahtar).OrderBy(o => o.Id).ToList();
    }

    private static BelgeEntity Satir(KasaWebFactory f, int id)
    {
        using var scope = f.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<KasaDbContext>().Belgeler.AsNoTracking().Single(b => b.Id == id);
    }

    [Fact]
    public async Task Alicinin_kendi_belgesini_kaldirmasi_yumusak_silmedir_iz_kalir_editor_gorur_ve_indirir()
    {
        await using var f = KasaWebFactory.Sabit(KasaWebFactory.VarsayilanBugun);
        using var editor = await f.EditorClientAsync();
        using var alici = await AlisTestYardimcisi.Alici(f, editor, "silen-alici");
        var taslak = await AlisTestYardimcisi.Taslak(alici, "Kereste AŞ");

        var kendi = await Yukle(alici, taslak.Id, Fatura, "fis.pdf");
        Assert.Equal(("alici", "Alıcı silen-alici"), (kendi.YukleyenRol, kendi.Yukleyen));
        var editorun = await Yukle(editor, taslak.Id, Duzeltilmis, "editor-notu.pdf");
        Assert.Equal(("editor", "Editör"), (editorun.YukleyenRol, editorun.Yukleyen));
        int aliciId;
        using (var scope = f.Services.CreateScope())
            aliciId = scope.ServiceProvider.GetRequiredService<KasaDbContext>().Alicilar.Single(a => a.Kullanici == "silen-alici").Id;
        Assert.Equal(("alici", (int?)aliciId), (Satir(f, kendi.Id).YukleyenRol, Satir(f, kendi.Id).YukleyenId));

        // Alıcı editörün eklediği belgeyi kaldıramaz.
        using (var r = await Sil(alici, editorun.Id))
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("Alıcı yalnız kendi yüklediği belgeyi kaldırabilir.", await AlisTestYardimcisi.Hata(r));
        }
        // Kendi belgesini gerekçesiz kaldırabilir: satır ve içerik korunur, iz yazılır.
        Assert.Equal(HttpStatusCode.NoContent, (await Sil(alici, kendi.Id)).StatusCode);
        var satir = Satir(f, kendi.Id);
        Assert.True(satir.Silindi);
        Assert.Equal(("alici", (int?)aliciId, (string?)null), (satir.SilenRol, satir.SilenId, satir.SilmeGerekcesi));
        Assert.Equal(f.Saat!.GetUtcNow(), satir.SilinmeZamani);
        Assert.Equal(Fatura, File.ReadAllBytes(f.Services.GetRequiredService<BelgeDeposu>().Yol(satir.IcerikOzeti)));
        Assert.Equal(HttpStatusCode.NotFound, (await Sil(alici, kendi.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Sil(editor, kendi.Id, "tekrar")).StatusCode);

        // Varsayılan listede yok; alıcı silineni ne listeler ne indirir; editör ?silinenler=true ile görür ve indirir.
        Assert.Equal(new[] { editorun.Id }, (await alici.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{taslak.Id}/belgeler"))!.Select(b => b.Id));
        Assert.Equal(new[] { editorun.Id }, (await alici.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{taslak.Id}/belgeler?silinenler=true"))!.Select(b => b.Id));
        Assert.Equal(new[] { editorun.Id }, (await editor.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{taslak.Id}/belgeler"))!.Select(b => b.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await alici.GetAsync($"/api/belgeler/{kendi.Id}")).StatusCode);
        Assert.Equal(Fatura, await editor.GetByteArrayAsync($"/api/belgeler/{kendi.Id}"));
        var hepsi = (await editor.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{taslak.Id}/belgeler?silinenler=true"))!;
        var silinen = Assert.Single(hepsi, b => b.Silindi);
        Assert.Equal((kendi.Id, "alici", "Alıcı silen-alici", "Alıcı silen-alici"), (silinen.Id, silinen.SilenRol, silinen.Silen, silinen.Yukleyen));
        Assert.NotNull(silinen.SilinmeZamani);

        // Olaylar denetim izinde: ekleme (özetle, içerik yok) ve silme değişikliği (alıcı aktörüyle).
        var olaylar = Olaylar(f, kendi.Id);
        Assert.Equal(["Ekle", "Degistir"], olaylar.Select(o => o.Tur));
        Assert.Equal(("alici", (int?)aliciId), (olaylar[1].AktorRol, olaylar[1].AktorId));
        var yeni = JsonDocument.Parse(olaylar[1].YeniJson!).RootElement;
        Assert.True(yeni.GetProperty("Silindi").GetBoolean());
        Assert.Equal("alici", yeni.GetProperty("SilenRol").GetString());
    }

    [Fact]
    public async Task Editor_gerekce_vermeden_silemez_gerekce_satira_ve_denetim_olayina_yazilir()
    {
        await using var f = KasaWebFactory.Sabit(KasaWebFactory.VarsayilanBugun);
        using var editor = await f.EditorClientAsync();
        var alis = await AlisTestYardimcisi.Taslak(editor, "Boya Ltd");
        var belge = await Yukle(editor, alis.Id, Fatura, "fatura.pdf");

        foreach (var bos in new[] { null, "   " })
            using (var r = await Sil(editor, belge.Id, bos))
            {
                Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                Assert.Equal(BelgeEndpoints.GerekceGerekli, await AlisTestYardimcisi.Hata(r));
            }
        Assert.False(Satir(f, belge.Id).Silindi);
        Assert.Equal(HttpStatusCode.NoContent, (await Sil(editor, belge.Id, "  İade sonrası yanlış fatura  ")).StatusCode);
        var satir = Satir(f, belge.Id);
        Assert.Equal(("editor", (int?)null, "İade sonrası yanlış fatura"), (satir.SilenRol, satir.SilenId, satir.SilmeGerekcesi));
        var olay = Olaylar(f, belge.Id).Last();
        Assert.Equal(("Degistir", "editor", "İade sonrası yanlış fatura"), (olay.Tur, olay.AktorRol, olay.Gerekce));

        // Gövdesiz istemci gerekçeyi X-Kasa-Gerekce başlığıyla verebilir.
        var ikinci = await Yukle(editor, alis.Id, Duzeltilmis, "ikinci.pdf");
        using var istek = new HttpRequestMessage(HttpMethod.Delete, $"/api/belgeler/{ikinci.Id}");
        istek.Headers.Add(DenetimBaglami.GerekceBasligi, Uri.EscapeDataString("Mükerrer yükleme"));
        Assert.Equal(HttpStatusCode.NoContent, (await editor.SendAsync(istek)).StatusCode);
        Assert.Equal("Mükerrer yükleme", Satir(f, ikinci.Id).SilmeGerekcesi);
    }

    [Fact]
    public async Task Alici_odemeye_bagli_ya_da_taslak_disi_ve_yukleyeni_bilinmeyen_belgeyi_kaldiramaz()
    {
        await using var f = KasaWebFactory.Sabit(KasaWebFactory.VarsayilanBugun);
        using var editor = await f.EditorClientAsync();
        using var alici = await AlisTestYardimcisi.Alici(f, editor, "sinirli-alici");
        var taslak = await AlisTestYardimcisi.Taslak(alici, "Firma");
        var belge = await Yukle(alici, taslak.Id, Fatura, "fis.pdf");
        // Bu sürümden önce yüklenmiş (yükleyeni bilinmeyen) belge.
        int eski;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var satir = new BelgeEntity { AlisId = taslak.Id, DosyaAdi = "eski.pdf", IcerikTuru = "application/pdf", Boyut = 1, Yuklendi = f.Saat!.GetUtcNow(), IcerikOzeti = TestBelgeDeposu.Ozet([1]) };
            db.Belgeler.Add(satir);
            db.SaveChanges();
            eski = satir.Id;
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Sil(alici, eski)).StatusCode);
        Assert.Null((await alici.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{taslak.Id}/belgeler"))!.Single(b => b.Id == eski).YukleyenRol);

        await AlisTestYardimcisi.Gonder(alici, taslak);
        using var r = await Sil(alici, belge.Id);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("taslağındaki", await AlisTestYardimcisi.Hata(r));
        Assert.False(Satir(f, belge.Id).Silindi);
    }

    [Fact]
    public async Task Silinen_belgeler_otuz_siniri_ve_taslak_kotalarina_sayilmaz_gunluk_yukleme_hacmine_sayilir()
    {
        await using var f = AlisTestYardimcisi.KotaFabrikasi(new() { ["Kasa:AliciKota:TaslakBelgeSayisi"] = "1", ["Kasa:AliciKota:GunlukYuklemeMb"] = "1" });
        using var editor = await f.EditorClientAsync();
        var alis = await AlisTestYardimcisi.Taslak(editor, "Otuz belge");
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            for (var i = 0; i < 30; i++)
                db.Belgeler.Add(new BelgeEntity { AlisId = alis.Id, DosyaAdi = $"b{i}.pdf", IcerikTuru = "application/pdf", Boyut = 1, Yuklendi = f.Saat!.GetUtcNow(), IcerikOzeti = TestBelgeDeposu.Ozet([(byte)i]), YukleyenRol = "editor" });
            db.SaveChanges();
        }
        using (var r = await AlisTestYardimcisi.YukleYanit(editor, alis.Id, Fatura, "otuzbir.pdf"))
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var ilk = (await editor.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{alis.Id}/belgeler"))![0];
        Assert.Equal(HttpStatusCode.NoContent, (await Sil(editor, ilk.Id, "Yer açmak için")).StatusCode);
        await Yukle(editor, alis.Id, Fatura, "otuzbir.pdf");

        // Alıcı: taslak başına 1 belge; kaldırılan belge yer açar, ama günlük hacim (1 MB) kaldırılanı da sayar.
        using var alici = await AlisTestYardimcisi.Alici(f, editor, "kota-silme");
        var taslak = await AlisTestYardimcisi.Taslak(alici, "Kota");
        var birinci = await Yukle(alici, taslak.Id, AlisTestYardimcisi.Pdf(400 * 1024), "bir.pdf");
        using (var r = await AlisTestYardimcisi.YukleYanit(alici, taslak.Id, AlisTestYardimcisi.Pdf(1024), "iki.pdf"))
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Sil(alici, birinci.Id)).StatusCode);
        await Yukle(alici, taslak.Id, AlisTestYardimcisi.Pdf(400 * 1024 + 1), "iki.pdf");
        Assert.Equal(HttpStatusCode.NoContent, (await Sil(alici, (await alici.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{taslak.Id}/belgeler"))!.Single().Id)).StatusCode);
        using (var r = await AlisTestYardimcisi.YukleYanit(alici, taslak.Id, AlisTestYardimcisi.Pdf(300 * 1024), "uc.pdf"))
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Contains("Son 24 saatte en fazla 1 MB", await AlisTestYardimcisi.Hata(r));
        }
    }
}
