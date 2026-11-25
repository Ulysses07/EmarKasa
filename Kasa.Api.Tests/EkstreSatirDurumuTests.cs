using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>Satırı kaydetmeden kapatma (yok say), belgeyi yeniden okuma ve toplu satır hataları (docs/specs/2026-11-25-pdf-ekrani.md).</summary>
public class EkstreSatirDurumuTests
{
    private sealed class Fabrika : KasaWebFactory
    {
        public DegisenPdf Pdf { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => { services.RemoveAll<IPdfMetinOkuyucu>(); services.AddSingleton<IPdfMetinOkuyucu>(Pdf); });
        }
    }
    private sealed class DegisenPdf : IPdfMetinOkuyucu
    {
        public string Metin { get; set; } = "";
        public Task<string> OkuAsync(byte[] pdf, CancellationToken ct) => Task.FromResult(Metin);
    }
    private static DateOnly Bugun => KasaWebFactory.VarsayilanBugun;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Metin(params string[] satirlar) => "Para Birimi: TL\n" + string.Join("\n", satirlar.Select(s => $"{Bugun:dd.MM.yyyy} {s}"));
    private static async Task<HttpClient> Editor(Fabrika f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(Bugun.Year, 1, 1), kasaAcilisDevri = 1000m }, Ct)).EnsureSuccessStatusCode();
        return c;
    }
    private static async Task<EkstreBelgeDto> Yukle(HttpClient c)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Banka"), "kaynak" }, { new StringContent("Akbank"), "banka" }, { new StringContent("Ana hesap"), "hesapAdi" },
            { new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7 " + Guid.NewGuid())), "dosya", "ekstre.pdf" },
        };
        var r = await c.PostAsync("/api/ekstre-aktar/yukle", form, Ct);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync(Ct));
        return (await r.Content.ReadFromJsonAsync<EkstreBelgeDto>(Ct))!;
    }
    private static async Task<T> Post<T>(HttpClient c, string yol, object govde)
    {
        var r = await c.PostAsJsonAsync(yol, govde, Ct);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync(Ct)}");
        return (await r.Content.ReadFromJsonAsync<T>(Ct))!;
    }
    private static async Task<(HttpStatusCode Durum, string Hata)> Reddet(HttpClient c, string yol, object govde)
    {
        var r = await c.PostAsJsonAsync(yol, govde, Ct);
        return (r.StatusCode, (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("hata").GetString()!);
    }
    private static Task<EkstreBelgeDto> YokSay(HttpClient c, EkstreBelgeDto d, bool yokSay, params int[] nolar) =>
        Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/yok-say", new EkstreYokSayYaz(Guid.NewGuid(), d.Surum, nolar, yokSay));
    private static EkstreSatirYaz Satir(int no, string tur = "Gider", decimal tutar = 100m, string aciklama = "Hareket", string dagilim = "Genel") => new(no, Bugun, aciklama, tutar, tur, dagilim, []);
    private static async Task<EkstreBelgeDto> Kaydet(HttpClient c, EkstreBelgeDto d, params EkstreSatirYaz[] satirlar)
    {
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, satirlar);
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{d.Id}/onizleme", istek);
        return await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = true });
    }
    private static async Task<decimal> Kasa(HttpClient c) => (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel", Ct))!.GuncelKasa;

    [Fact]
    public async Task Yok_sayilan_satir_kasayi_degistirmeden_kapanir_kaydedilemez_geri_alinir()
    {
        await using var f = new Fabrika();
        f.Pdf.Metin = Metin("Hareket bir -100,00", "Hareket iki -200,00", "Hareket uc -300,00");
        using var c = await Editor(f);
        var d = await Yukle(c);
        Assert.Empty(d.YokSayilanlar);
        Assert.True(d.YenidenOkunabilir);
        var once = await Kasa(c);

        var kapali = await YokSay(c, d, true, 3, 1);
        Assert.Equal([1, 3], kapali.YokSayilanlar);
        Assert.Equal(d.Surum + 1, kapali.Surum);
        Assert.False(kapali.YenidenOkunabilir);
        Assert.Empty(kapali.Kayitlar);
        Assert.Equal(once, await Kasa(c));
        var ozet = Assert.Single((await c.GetFromJsonAsync<List<EkstreBelgeOzetDto>>("/api/ekstre-aktar", Ct))!);
        Assert.Equal((3, 0, 2), (ozet.SatirSayisi, ozet.KayitSayisi, ozet.YokSayilanSayisi));
        var oneriler = (await c.GetFromJsonAsync<List<EkstreKuralOnerisi>>($"/api/ekstre-aktar/{d.Id}/oneriler", Ct))!;
        Assert.Equal(["YokSayildi", "Yok", "YokSayildi"], oneriler.Select(o => o.Durum));

        var (durum, hata) = await Reddet(c, $"/api/ekstre-aktar/{d.Id}/onizleme", new EkstreKaydetYaz(Guid.NewGuid(), kapali.Surum, [Satir(1)]));
        Assert.Equal(HttpStatusCode.Conflict, durum);
        Assert.StartsWith("Satır 1: Bu satır yok sayıldı.", hata);

        var acik = await YokSay(c, kapali, false, 1);
        Assert.Equal([3], acik.YokSayilanlar);
        var kayitli = await Kaydet(c, acik, Satir(1));
        Assert.Equal(1, Assert.Single(kayitli.Kayitlar).SatirNo);
        Assert.Equal(once - 100m, await Kasa(c));
    }

    [Fact]
    public async Task Yok_say_surum_tekrar_kayitli_satir_ve_satir_numarasini_denetler()
    {
        await using var f = new Fabrika();
        f.Pdf.Metin = Metin("Hareket bir -100,00", "Hareket iki -200,00");
        using var c = await Editor(f);
        var d = await Yukle(c);
        var yol = $"/api/ekstre-aktar/{d.Id}/yok-say";
        var istek = new EkstreYokSayYaz(Guid.NewGuid(), d.Surum, [2], true);
        var ilk = await Post<EkstreBelgeDto>(c, yol, istek);
        // Aynı istek yeniden gelirse (yanıt kaybolduysa) ikinci kez işlenmez, güncel belge döner.
        var tekrar = await Post<EkstreBelgeDto>(c, yol, istek);
        Assert.Equal((ilk.Surum, 1), (tekrar.Surum, tekrar.YokSayilanlar.Count));
        Assert.Equal(HttpStatusCode.Conflict, (await Reddet(c, yol, istek with { IstekId = Guid.NewGuid() })).Durum);
        Assert.Equal(HttpStatusCode.Conflict, (await Reddet(c, yol, istek with { YokSay = false })).Durum);
        foreach (var gecersiz in new int[][] { [], [1, 1], [9] })
            Assert.Equal(HttpStatusCode.BadRequest, (await Reddet(c, yol, new EkstreYokSayYaz(Guid.NewGuid(), ilk.Surum, gecersiz, true))).Durum);
        Assert.Equal(HttpStatusCode.NotFound, (await Reddet(c, "/api/ekstre-aktar/999/yok-say", istek with { IstekId = Guid.NewGuid() })).Durum);

        var kayitli = await Kaydet(c, ilk, Satir(1));
        var (durum, hata) = await Reddet(c, yol, new EkstreYokSayYaz(Guid.NewGuid(), kayitli.Surum, [1], true));
        Assert.Equal(HttpStatusCode.Conflict, durum);
        Assert.StartsWith("Satır 1:", hata);
        // İptal edilen satır yeniden bekler ve yok sayılabilir.
        var iptal = await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/kayitlar/{kayitli.Kayitlar[0].Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış"));
        Assert.Equal([1, 2], (await YokSay(c, iptal, true, 1)).YokSayilanlar);
        // Zaten kapalı satırı yeniden yok saymak ve açık satırı geri almak bir şey bozmaz.
        var son = (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{d.Id}", Ct))!;
        Assert.Equal([1, 2], (await YokSay(c, son, true, 1, 2)).YokSayilanlar);
    }

    [Fact]
    public async Task Yok_say_denetim_izine_yazilir()
    {
        await using var f = new Fabrika();
        f.Pdf.Metin = Metin("Hareket bir -100,00");
        using var c = await Editor(f);
        var kapali = await YokSay(c, await Yukle(c), true, 1);
        await YokSay(c, kapali, false, 1);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(2, db.DenetimOlaylari.AsNoTracking().Count(k => k.Varlik == "EkstreYokSayilan"));
    }

    [Fact]
    public async Task Yeniden_oku_yalniz_islenmemis_belgede_saklanan_pdfi_yeniden_okur()
    {
        await using var f = new Fabrika();
        f.Pdf.Metin = Metin("Hareket bir 100,00 10.000,00");
        using var c = await Editor(f);
        var d = await Yukle(c);
        Assert.Null(Assert.Single(d.Satirlar).Tutar);
        f.Pdf.Metin = "Bloke 5,00\n" + Metin("Hareket bir -100,00", "Hareket iki -200,00");
        var yol = $"/api/ekstre-aktar/{d.Id}/yeniden-oku";
        var istek = new EkstreYenidenOkuYaz(Guid.NewGuid(), d.Surum);
        var yeni = await Post<EkstreBelgeDto>(c, yol, istek);
        Assert.Equal(2, yeni.Satirlar.Count);
        Assert.Equal(100m, yeni.Satirlar[0].Tutar);
        Assert.Equal("Bloke 5,00", Assert.Single(yeni.Atlananlar).Metin);
        Assert.Equal(d.Surum + 1, yeni.Surum);
        Assert.Equal(yeni.Surum, (await Post<EkstreBelgeDto>(c, yol, istek)).Surum);
        Assert.Equal(HttpStatusCode.Conflict, (await Reddet(c, yol, istek with { IstekId = Guid.NewGuid() })).Durum);
        Assert.Equal(HttpStatusCode.NotFound, (await Reddet(c, "/api/ekstre-aktar/999/yeniden-oku", istek)).Durum);

        // Yok sayılan satır numarası yeni okumada başka harekete denk gelebilir.
        var kapali = await YokSay(c, yeni, true, 1);
        var (durum, hata) = await Reddet(c, yol, new EkstreYenidenOkuYaz(Guid.NewGuid(), kapali.Surum));
        Assert.Equal(HttpStatusCode.Conflict, durum);
        Assert.Contains("yeniden okunamaz", hata);
        // İptal edilmiş kayıt da satır numarasına bağlıdır.
        var acik = await YokSay(c, kapali, false, 1);
        Assert.True(acik.YenidenOkunabilir);
        var kayitli = await Kaydet(c, acik, Satir(1));
        var iptal = await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/kayitlar/{kayitli.Kayitlar[0].Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış"));
        Assert.False(iptal.YenidenOkunabilir);
        Assert.Equal(HttpStatusCode.Conflict, (await Reddet(c, yol, new EkstreYenidenOkuYaz(Guid.NewGuid(), iptal.Surum))).Durum);
    }

    [Fact]
    public async Task Yeniden_okuma_dosya_yoksa_belgeyi_degistirmez()
    {
        await using var f = new Fabrika();
        f.Pdf.Metin = Metin("Hareket bir -100,00");
        using var c = await Editor(f);
        var d = await Yukle(c);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.EkstreBelgeler.Single().DosyaOzeti = new string('0', 64);
            db.SaveChanges();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Reddet(c, $"/api/ekstre-aktar/{d.Id}/yeniden-oku", new EkstreYenidenOkuYaz(Guid.NewGuid(), d.Surum))).Durum);
        Assert.Single((await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{d.Id}", Ct))!.Satirlar);
    }

    [Fact]
    public async Task Onizleme_butun_hatali_satirlari_numarasiyla_bir_kerede_soyler_hicbirini_uygulamaz()
    {
        await using var f = new Fabrika();
        f.Pdf.Metin = Metin("Hareket bir -100,00", "Hareket iki -200,00", "Hareket uc -300,00", "Hareket dort -400,00");
        using var c = await Editor(f);
        var d = await Yukle(c);
        var once = await Kasa(c);
        var (durum, hata) = await Reddet(c, $"/api/ekstre-aktar/{d.Id}/onizleme", new EkstreKaydetYaz(Guid.NewGuid(), d.Surum,
            [Satir(1), Satir(2, dagilim: "Esit"), Satir(3) with { Tarih = new(2090, 1, 1) }, Satir(4), Satir(99)]));
        Assert.Equal(HttpStatusCode.BadRequest, durum);
        var iletiler = hata.Split('\n');
        Assert.Equal(["Satır 2: Dağıtılacak kanalları seçin.", "Satır 3: Hareket tarihi takip başlangıcı ile bugün arasında olmalı.", "Satır 99: Kaynak satır bu PDF'de bulunamadı."], iletiler);
        Assert.Empty((await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{d.Id}", Ct))!.Kayitlar);
        Assert.Equal(once, await Kasa(c));
        // Karta ödeme satırında kart seçilmemesi de satır numarasıyla gelir.
        var (_, kartHatasi) = await Reddet(c, $"/api/ekstre-aktar/{d.Id}/onizleme", new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, [Satir(1, "KartOdemesi", dagilim: "Otomatik")]));
        Assert.StartsWith("Satır 1:", kartHatasi);
    }

    [Fact]
    public async Task Onizlemeden_sonra_yok_sayilan_ya_da_yeniden_okunan_belge_eski_ozetle_kaydedilmez()
    {
        await using var f = new Fabrika();
        f.Pdf.Metin = Metin("Hareket bir -100,00", "Hareket iki -200,00");
        using var c = await Editor(f);
        var once = await Kasa(c);
        var d = await Yukle(c);
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, [Satir(1)]);
        var ozet = (await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{d.Id}/onizleme", istek)).OnizlemeOzeti;
        var yeni = await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/yeniden-oku", new EkstreYenidenOkuYaz(Guid.NewGuid(), d.Surum));
        Assert.Equal(HttpStatusCode.Conflict, (await Reddet(c, $"/api/ekstre-aktar/{d.Id}/kaydet", istek with { OnizlemeOzeti = ozet, TekrarOnay = true })).Durum);

        istek = istek with { IstekId = Guid.NewGuid(), Surum = yeni.Surum };
        ozet = (await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{d.Id}/onizleme", istek)).OnizlemeOzeti;
        await YokSay(c, yeni, true, 1);
        Assert.Equal(HttpStatusCode.Conflict, (await Reddet(c, $"/api/ekstre-aktar/{d.Id}/kaydet", istek with { OnizlemeOzeti = ozet, TekrarOnay = true })).Durum);
        Assert.Equal(once, await Kasa(c));
    }

    [Theory]
    [InlineData("yok-say")]
    [InlineData("yeniden-oku")]
    public async Task Yeni_uclar_yalniz_editore_aciktir(string uc)
    {
        await using var f = new Fabrika();
        using var anonim = f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.PostAsJsonAsync($"/api/ekstre-aktar/1/{uc}", new { }, Ct)).StatusCode);
    }
}
