using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.YapayZeka;
using Kasa.Core.Kodlar;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>Yapay zekâ önerileri (docs/specs/2026-11-25-yapay-zeka-onerileri.md): kapalıyken çağrı yok, maskeli istek, yanıtın doğrulanması, öncelik,
/// harcama sınırı, tekrar istek, hatalar, doğruluk ve ölçüm. Gerçek API çağrılmaz.</summary>
public class YapayZekaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateOnly Bugun => KasaWebFactory.VarsayilanBugun;
    private static DateOnly G(int once) => Bugun.AddDays(-once);
    private static object?[] S(params object?[] h) => h;

    private sealed class SahteIstemci : IYapayZekaIstemcisi
    {
        public List<YapayZekaIstegi> Istekler { get; } = [];
        public Func<YapayZekaIstegi, Task<YapayZekaYaniti>> Yanitla { get; set; } = _ => Task.FromResult(Yanit());
        public Task<YapayZekaYaniti> SorAsync(YapayZekaIstegi istek, CancellationToken ct) { Istekler.Add(istek); return Yanitla(istek); }
        public void Yanitlari(params object[] satirlar) => Yanitla = _ => Task.FromResult(Yanit(satirlar));
    }
    private static YapayZekaYaniti Yanit(params object[] satirlar) => new(JsonSerializer.Serialize(new { satirlar }), null, 1_000, 400, 2_000, 500);
    private static object Y(int no, string tur, string dagilim = "Belirsiz", int[]? kanallar = null, string guven = "Yuksek", string kategori = "", string aciklama = "") =>
        new { no, tur, dagilim, kanallar = kanallar ?? [], guven, kategori, aciklama };

    private sealed class Fabrika(string? anahtar = "test-anahtari", string? sinir = null, string etkin = "true") : KasaWebFactory
    {
        public SahteIstemci Istemci { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            { ["YapayZeka:Etkin"] = etkin, ["YapayZeka:Anahtar"] = anahtar, ["YapayZeka:AylikSinirUsd"] = sinir }));
            builder.ConfigureServices(s => { s.RemoveAll<IYapayZekaIstemcisi>(); s.AddSingleton<IYapayZekaIstemcisi>(Istemci); });
        }
    }
    private static Fabrika Ac(string? anahtar = "test-anahtari", string? sinir = null, string etkin = "true") => new(anahtar, sinir, etkin) { Saat = new SabitSaat(Bugun) };

    private static async Task<HttpClient> Editor(KasaWebFactory f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(Bugun.Year, 1, 1), kasaAcilisDevri = 1000m }, Ct)).EnsureSuccessStatusCode();
        return c;
    }
    private static Task<EkstreBelgeDto> Banka(HttpClient c, params object?[][] satirlar) => Yukle(c, S("Tarih", "Açıklama", "Tutar (TL)"), satirlar);
    private static async Task<EkstreBelgeDto> Yukle(HttpClient c, object?[] baslik, object?[][] satirlar)
    {
        var dosya = TestExcel.Xlsx(("H", [baslik, .. satirlar]));
        using var form = new MultipartFormDataContent
        { { new StringContent("Banka"), "kaynak" }, { new StringContent("Akbank"), "banka" }, { new StringContent("Ana hesap"), "hesapAdi" }, { new ByteArrayContent(dosya), "dosya", "banka.xlsx" } };
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
    private static async Task<(HttpStatusCode Kod, string Hata)> Hata(HttpClient c, string yol, object govde)
    {
        var r = await c.PostAsJsonAsync(yol, govde, Ct);
        return (r.StatusCode, (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("hata").GetString()!);
    }
    private static Task<YapayZekaSonucDto> Sor(HttpClient c, int id, Guid? istek = null) => Post<YapayZekaSonucDto>(c, $"/api/ekstre-aktar/{id}/yapay-zeka", new YapayZekaSorYaz(istek ?? Guid.NewGuid()));
    private static Task<(HttpStatusCode Kod, string Hata)> SorHata(HttpClient c, int id, Guid? istek = null) => Hata(c, $"/api/ekstre-aktar/{id}/yapay-zeka", new YapayZekaSorYaz(istek ?? Guid.NewGuid()));
    private static async Task<YapayZekaDurumDto> Durum(HttpClient c) => (await c.GetFromJsonAsync<YapayZekaDurumDto>("/api/ekstre-aktar/yapay-zeka", Ct))!;
    private static async Task<List<EkstreKuralOnerisi>> Oneriler(HttpClient c, int id) => (await c.GetFromJsonAsync<List<EkstreKuralOnerisi>>($"/api/ekstre-aktar/{id}/oneriler", Ct))!;
    private static async Task<EkstreBelgeDto> Kaydet(HttpClient c, EkstreBelgeDto d, params (int No, string Tur, string Dagilim, int[] Kanallar)[] satirlar)
    {
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, satirlar.Select(x =>
        {
            var s = d.Satirlar.Single(s => s.No == x.No);
            return new EkstreSatirYaz(x.No, s.Tarih!.Value, s.Aciklama, s.Tutar!.Value, x.Tur, x.Dagilim, x.Kanallar.Select(k => new KanalPayYaz(k, 0)).ToList());
        }).ToList());
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{d.Id}/onizleme", istek);
        return await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = true });
    }
    private static async Task<decimal> Kasa(HttpClient c) => (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel", Ct))!.GuncelKasa;

    // ---- Açma / kapama ----

    [Theory]
    [InlineData("a", null, "false", "Yapay zekâ sunucuda kapalı.")]
    [InlineData(null, null, "true", "Yapay zekâ açık ama sunucuda anahtarı yok (YapayZeka:Anahtar).")]
    [InlineData("a", "0", "true", "Aylık sınır (YapayZeka:AylikSinirUsd) 0 ile 100 $ arasında bir sayı olmalı; ondalık ayıracı nokta.")]
    [InlineData("a", "iki", "true", "Aylık sınır (YapayZeka:AylikSinirUsd) 0 ile 100 $ arasında bir sayı olmalı; ondalık ayıracı nokta.")]
    [InlineData("a", "2,5", "true", "Aylık sınır (YapayZeka:AylikSinirUsd) 0 ile 100 $ arasında bir sayı olmalı; ondalık ayıracı nokta.")]
    public async Task Kapali_ya_da_ayari_eksikse_cagri_yapilmaz(string? anahtar, string? sinir, string etkin, string neden)
    {
        await using var f = Ac(anahtar, sinir, etkin);
        using var c = await Editor(f);
        var d = await Banka(c, S(G(3), "ELEKTRIK FATURA", -10));
        var durum = await Durum(c);
        Assert.Equal((false, neden), (durum.Acik, durum.KapaliNedeni));
        Assert.Equal((HttpStatusCode.Conflict, neden), await SorHata(c, d.Id));
        Assert.Equal((HttpStatusCode.Conflict, neden), await Hata(c, "/api/ekstre-aktar/yapay-zeka/olcum", new YapayZekaOlcumYaz(Guid.NewGuid(), "Banka")));
        Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync($"/api/ekstre-aktar/{d.Id}/yapay-zeka", Ct)).StatusCode);
        Assert.Empty(f.Istemci.Istekler);
        Assert.Equal("Yok", (await Oneriler(c, d.Id)).Single().Durum);
    }

    [Fact]
    public void Varsayilan_yapilandirmada_kapalidir()
    {
        var ayar = new YapayZekaAyarlari(new ConfigurationBuilder().Build());
        Assert.Equal((false, null, 2m, "Yapay zekâ sunucuda kapalı."), (ayar.Etkin, ayar.Anahtar, ayar.AylikSinirUsd, ayar.KapaliNedeni));
    }

    [Fact]
    public async Task Izleyici_soramaz()
    {
        await using var f = Ac();
        using var c = await Editor(f);
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi-12" }, Ct)).EnsureSuccessStatusCode();
        using var i = f.CreateClient();
        (await i.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izleyici-sifresi-12" }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await i.GetAsync("/api/ekstre-aktar/yapay-zeka", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await i.PostAsJsonAsync("/api/ekstre-aktar/1/yapay-zeka", new YapayZekaSorYaz(Guid.NewGuid()), Ct)).StatusCode);
    }

    // ---- İstek ----

    /// <summary>Para birimi okunamayan satırı uygulama TL sayıp seçtirir; yapay zekâ da onu sorar (kart ekstrelerinde çoğu satır böyledir). Dövizli satır
    /// sorulmaz.</summary>
    [Fact]
    public async Task Para_birimi_okunamayan_satir_sorulur_dovizli_satir_sorulmaz()
    {
        await using var f = Ac();
        using var c = await Editor(f);
        var d = await Yukle(c, S("Tarih", "Açıklama", "Tutar", "Para birimi"), [S(G(3), "ELEKTRIK FATURA 881", -900, ""), S(G(3), "AMAZON WEB HIZMET", -50, "USD")]);
        Assert.Equal(["Belirsiz", "USD"], d.Satirlar.Select(r => r.ParaBirimi));
        var onizleme = (await c.GetFromJsonAsync<YapayZekaOnizlemeDto>($"/api/ekstre-aktar/{d.Id}/yapay-zeka", Ct))!;
        Assert.Equal(["1 | CIKIS | 100-1000 | ELEKTRIK FATURA #"], onizleme.Satirlar);
        f.Istemci.Yanitlari(Y(1, "Gider", "Genel", kategori: "elektrik"));
        Assert.Equal(1, (await Sor(c, d.Id)).Sorulan);
        var o = await Oneriler(c, d.Id);
        Assert.Equal((YapayZekaDurumlari.Oneri, "Gider", "Yok"), (o[0].Durum, o[0].IslemTuru, o[1].Durum));
    }

    /// <summary>Gönderilen metinde yalnız maskeli açıklama, yön ve tutar aralığı vardır; tarih, kesin tutar, banka, hesap, dosya adı ve kişi adı yoktur.
    /// Kural ve hafızanın tanıdığı, kapanmış, dövizli ve yalnız ad/sayıdan oluşan satır sorulmaz; aynı satırlar bir kez sorulur.</summary>
    [Fact]
    public async Task Istek_maskelidir_yalniz_taninmayan_satirlar_bir_kez_sorulur_gecmis_kararlar_ornektir()
    {
        await using var f = Ac();
        using var c = await Editor(f);
        var gecmis = await Banka(c, S(G(20), "FAST MEHMET DEMIR KIRA", -7_500), S(G(20), "TRENDYOL KOMISYON KESINTISI", -120));
        await Kaydet(c, gecmis, (1, "Gider", "Genel", []), (2, "Gider", "Esit", [2]));
        await Post<EkstreKuralDto>(c, "/api/ekstre-aktar/kurallar", new EkstreKuralYaz(Guid.NewGuid(), 0, "Su", "Banka", null, "ISKI", null, "Gider", "Genel", []));
        var d = await Banka(c, S(G(3), "FAST MEHMET DEMIR KIRA", -7_500), S(G(3), "ISKI SU FATURASI", -300), S(G(3), "HAVALE MEHMET DEMIR AIDAT 2026/09", -1_234.56m),
            S(G(3), "ELEKTRIK FATURA 881", -900), S(G(2), "ELEKTRIK FATURA 992", -850), S(G(3), "AHMET YILMAZ", 500), S(G(3), "123456789", -5));
        var onizleme = (await c.GetFromJsonAsync<YapayZekaOnizlemeDto>($"/api/ekstre-aktar/{d.Id}/yapay-zeka", Ct))!;
        Assert.Equal((3, 0, 2), (onizleme.SatirSayisi, onizleme.KalanSatir, onizleme.OrnekSayisi));
        Assert.Equal(["3 | CIKIS | 1000-10000 | HAVALE [1] [2] AIDAT #", "4 | CIKIS | 100-1000 | ELEKTRIK FATURA #"], onizleme.Satirlar);
        Assert.InRange(onizleme.EnYuksekMaliyetUsd, 0.008m, 0.02m);
        Assert.Empty(f.Istemci.Istekler);

        f.Istemci.Yanitlari(Y(3, "Gider", "Genel", kategori: "aidat", aciklama: "[1] [2] aidat"), Y(4, "Gider", "Kanal", [2, 2], "Orta", "elektrik"));
        var once = await Kasa(c);
        var sonuc = await Sor(c, d.Id);
        Assert.Equal((3, 3, 0, null), (sonuc.Sorulan, sonuc.Onerilen, sonuc.KalanSatir, sonuc.Not));
        var istek = Assert.Single(f.Istemci.Istekler);
        Assert.Contains("CIKIS | FAST [1] [2] KIRA => Gider, Genel", istek.Baglam);
        Assert.Contains("CIKIS | TRENDYOL KOMISYON KESINTISI => Gider, Kanal 2", istek.Baglam);
        Assert.Contains("2 = PERAKENDE", istek.Baglam);
        Assert.StartsWith("Belge: banka hesap hareketleri\n", istek.Soru);
        var hepsi = istek.Yonerge + istek.Baglam + istek.Soru + istek.Sema;
        foreach (var gizli in new[] { "MEHMET", "DEMIR", "AHMET", "YILMAZ", "1234", "1.234", "123456789", "881", "2026", "Akbank", "Ana hesap", "banka.xlsx", $"{G(3):dd.MM.yyyy}" })
            Assert.DoesNotContain(gizli, hepsi, StringComparison.OrdinalIgnoreCase);

        // Öneri satırı doldurur, kaydetmez; öncelik kural ve hafızadadır.
        var o = await Oneriler(c, d.Id);
        Assert.Equal([EkstreHafizaDurumlari.Hazir, "Oneri", YapayZekaDurumlari.Oneri, YapayZekaDurumlari.Oneri, YapayZekaDurumlari.Oneri, "Yok", "Yok"], o.Select(x => x.Durum));
        Assert.Equal(("Gider", "Genel", "Yapay zekâ (yüksek güven): aidat. Kontrol edip kendiniz işaretleyin.", new EkstreYapayZekaOnerisi("Yuksek", "aidat", "MEHMET DEMIR aidat")), (o[2].IslemTuru, o[2].DagilimTuru, o[2].Aciklama, o[2].YapayZeka));
        Assert.Equal(("Gider", "Esit", "Yapay zekâ (orta güven): elektrik. Kontrol edip kendiniz işaretleyin."), (o[4].IslemTuru, o[4].DagilimTuru, o[4].Aciklama));
        Assert.Equal([2], o[4].KanalIds);
        Assert.Equal(once, await Kasa(c));
        // Önerisi gelen satır yeniden sorulmaz.
        Assert.Equal(HttpStatusCode.Conflict, (await SorHata(c, d.Id)).Kod);
        Assert.Single(f.Istemci.Istekler);
        // Kaydedilen satır hafızaya geçer: aynı hareket bir daha yapay zekâya gitmez.
        await Kaydet(c, d, (4, "Gider", "Genel", []));
        var yeni = await Banka(c, S(G(1), "ELEKTRIK FATURA 555", -950));
        Assert.Equal(EkstreHafizaDurumlari.Hazir, (await Oneriler(c, yeni.Id)).Single().Durum);
        Assert.Contains("sorulacak satır yok", (await SorHata(c, yeni.Id)).Hata);
    }

    [Fact]
    public async Task Bir_cagrida_en_cok_60_farkli_satir_sorulur_kalani_sonrakine_kalir()
    {
        await using var f = Ac();
        using var c = await Editor(f);
        string[] a = ["KARGO", "ELEKTRIK", "DOGALGAZ", "MARKET", "YAKIT", "REKLAM", "KIRTASIYE", "SIGORTA"], b = ["FATURA", "ODEME", "MASRAF", "BEDELI", "KOMISYON", "IADE", "AIDAT", "UCRET"];
        var d = await Banka(c, [.. a.SelectMany((x, i) => b.Select((y, j) => S(G(3), $"{x} {y}", -10m - i * 8 - j)))]);
        f.Istemci.Yanitlari(Y(1, "Gider"));
        var sonuc = await Sor(c, d.Id);
        Assert.Equal((60, 4, 1), (sonuc.Sorulan, sonuc.KalanSatir, sonuc.Onerilen));
        static List<int> Nolar(YapayZekaIstegi i) => i.Soru.Split('\n').Skip(2).Select(s => int.Parse(s.Split(" | ")[0])).ToList();
        Assert.Equal(Enumerable.Range(1, 60), Nolar(f.Istemci.Istekler[0]));
        // Öneri gelmeyen 59 satır yeniden sorulabilir ama önce hiç sorulmayan 4 satır gelir (Codex incelemesi, EmarKasa#65).
        f.Istemci.Yanitlari();
        Assert.Equal((60, 0, 0), ((await Sor(c, d.Id)).Sorulan, (await Sor(c, d.Id)).KalanSatir, (await Durum(c)).Sonuclanan));
        Assert.Equal([61, 62, 63, 64], Nolar(f.Istemci.Istekler[1]).Order().TakeLast(4));
        Assert.Equal([61, 62, 63, 64], Nolar(f.Istemci.Istekler[1]).Take(4));
        // Sonraki soruda en eski sorulanlar öndedir; ilk çağrıda sorulup ikincisine girmeyenler.
        Assert.Equal(Enumerable.Range(58, 3), Nolar(f.Istemci.Istekler[2]).Take(3));
    }

    /// <summary>Yanıtı kaybolan isteğin yinelenmesi ilk sonucu (kalan satır ve not dahil) aynen döndürür (Codex incelemesi, EmarKasa#65).</summary>
    [Fact]
    public async Task Tekrar_istek_kalan_satiri_ve_notu_korur()
    {
        await using var f = Ac();
        using var c = await Editor(f);
        string[] a = ["KARGO", "ELEKTRIK", "DOGALGAZ", "MARKET", "YAKIT", "REKLAM", "KIRTASIYE", "SIGORTA"], b = ["FATURA", "ODEME", "MASRAF", "BEDELI", "KOMISYON", "IADE", "AIDAT", "UCRET"];
        var d = await Banka(c, [.. a.SelectMany((x, i) => b.Select((y, j) => S(G(3), $"{x} {y}", -10m - i * 8 - j)))]);
        f.Istemci.Yanitla = _ => Task.FromResult(new YapayZekaYaniti(null, "Yanıt yarım kaldı.", 1_000, 16_000, 0, 0));
        var istek = Guid.NewGuid();
        var ilk = await Sor(c, d.Id, istek);
        Assert.Equal((60, 0, 4, "Yanıt yarım kaldı."), (ilk.Sorulan, ilk.Onerilen, ilk.KalanSatir, ilk.Not));
        Assert.Equal(ilk, await Sor(c, d.Id, istek));
        Assert.Single(f.Istemci.Istekler);
    }

    // ---- Yanıt ----

    [Fact]
    public async Task Yanit_dogrulanir_yonu_celisen_turu_uygun_olmayan_taninmayan_satir_ve_kanal_alinmaz()
    {
        await using var f = Ac();
        using var c = await Editor(f);
        var d = await Banka(c, S(G(3), "KARGO ODEME", -10), S(G(3), "POS SATIS", 20), S(G(3), "VIRMAN HESAP", -30), S(G(3), "MARKET MASRAF", -40), S(G(3), "KART BORCU", -50), S(G(3), "YAKIT MASRAF", -60), S(G(3), "REKLAM MASRAF", -70));
        f.Istemci.Yanitlari(Y(1, "Gelir", "Genel"), Y(2, "Gelir", "Kanal", [99]), Y(3, "Atla", kategori: "virman"), Y(4, "KartHarcama", "Genel"), Y(5, "KartOdemesi", "Genel", guven: "Emin"),
            Y(6, "Belirsiz"), Y(7, "Gider", "Genel", kategori: "reklam [1]", aciklama: "[7] 2026"), Y(7, "Gelir"), Y(88, "Gider"));
        Assert.Equal(4, (await Sor(c, d.Id)).Onerilen);
        var o = await Oneriler(c, d.Id);
        Assert.Equal(["Yok", YapayZekaDurumlari.Oneri, YapayZekaDurumlari.Oneri, "Yok", YapayZekaDurumlari.Oneri, "Yok", YapayZekaDurumlari.Oneri], o.Select(x => x.Durum));
        Assert.Equal(("Gelir", (string?)null), (o[1].IslemTuru, o[1].DagilimTuru));
        Assert.Equal(("Atla", (string?)null, "Yapay zekâ (orta güven): virman. Kasa hareketi olmayabilir; öyleyse yok sayın."), (o[2].IslemTuru, o[2].DagilimTuru, o[2].Aciklama));
        Assert.Equal(("KartOdemesi", (string?)null, "Dusuk"), (o[4].IslemTuru, o[4].DagilimTuru, o[4].YapayZeka!.Guven));
        Assert.Equal(("Gider", "Genel", "", (string?)null), (o[6].IslemTuru, o[6].DagilimTuru, o[6].YapayZeka!.Kategori, o[6].YapayZeka!.Aciklama));
    }

    [Theory]
    [InlineData("{bozuk", null, "Yanıt okunamadı.")]
    [InlineData("{}", null, "Yanıt okunamadı.")]
    [InlineData(null, "Yanıt yarım kaldı.", "Yanıt yarım kaldı.")]
    [InlineData("{\"satirlar\":[]}", null, "Bu satırlar için güvenilir öneri gelmedi.")]
    public async Task Kullanilamayan_yanitin_maliyeti_yazilir_oneri_olmaz(string? metin, string? neden, string not)
    {
        await using var f = Ac();
        using var c = await Editor(f);
        var d = await Banka(c, S(G(3), "KARGO ODEME", -10));
        f.Istemci.Yanitla = _ => Task.FromResult(new YapayZekaYaniti(metin, neden, 1_000, 16_000, 0, 0));
        var sonuc = await Sor(c, d.Id);
        Assert.Equal((0, not, 0.0081m), (sonuc.Onerilen, sonuc.Not, sonuc.MaliyetUsd));
        Assert.Equal(0.0081m, (await Durum(c)).AyHarcamasiUsd);
        Assert.Equal("Yok", (await Oneriler(c, d.Id)).Single().Durum);
    }

    [Fact]
    public async Task Yeniden_okunan_belgenin_onerileri_silinir_cagri_surerken_okunduysa_yazilmaz()
    {
        await using var f = Ac();
        using var c = await Editor(f);
        var d = await Banka(c, S(G(3), "KARGO ODEME", -10));
        f.Istemci.Yanitlari(Y(1, "Gider", "Genel"));
        Assert.Equal(1, (await Sor(c, d.Id)).Onerilen);
        d = await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/yeniden-oku", new EkstreYenidenOkuYaz(Guid.NewGuid(), d.Surum));
        Assert.Equal("Yok", (await Oneriler(c, d.Id)).Single().Durum);
        // Çağrı sürerken kaydedilen satıra öneri yazılmaz.
        f.Istemci.Yanitla = async _ => { await Kaydet(c, d, (1, "Gider", "Genel", [])); return Yanit(Y(1, "Gider", "Genel")); };
        Assert.Equal(0, (await Sor(c, d.Id)).Onerilen);
        // Çağrı sürerken belge yeniden okunup satırları değiştiyse eski numaralara öneri yazılmaz (Codex incelemesi, EmarKasa#65).
        var d2 = await Banka(c, S(G(2), "MARKET MASRAF", -15));
        f.Istemci.Yanitla = _ =>
        {
            using var kapsam = f.Services.CreateScope();
            var db = kapsam.ServiceProvider.GetRequiredService<KasaDbContext>();
            var belge = db.EkstreBelgeler.Single(b => b.Id == d2.Id);
            belge.SatirlarJson = belge.SatirlarJson.Replace("MARKET", "KIRA");
            db.SaveChanges();
            return Task.FromResult(Yanit(Y(1, "Gider", "Genel")));
        };
        var sonuc = await Sor(c, d2.Id);
        Assert.Equal((0, "Belge bu sırada yeniden okundu; yeniden sorun."), (sonuc.Onerilen, sonuc.Not));
        Assert.Equal("Yok", (await Oneriler(c, d2.Id)).Single().Durum);
    }

    [Fact]
    public async Task Erp12_belgesi_sorulmaz()
    {
        await using var f = Ac();
        using var c = await Editor(f);
        var dosya = TestExcel.Xlsx(("C", [S("Tarih", "Cari", "Evrak Tipi", "Borç", "Alacak"), S(G(1), "A LTD", "Nakit Ödeme", 5, null)]));
        using var form = new MultipartFormDataContent { { new StringContent("Erp"), "kaynak" }, { new StringContent(""), "banka" }, { new StringContent(""), "hesapAdi" }, { new ByteArrayContent(dosya), "dosya", "erp.xlsx" } };
        var d = (await (await c.PostAsync("/api/ekstre-aktar/yukle", form, Ct)).Content.ReadFromJsonAsync<EkstreBelgeDto>(Ct))!;
        Assert.Equal((HttpStatusCode.Conflict, "ERP12 satırları yapay zekâya sorulmaz."), await SorHata(c, d.Id));
        Assert.Empty(f.Istemci.Istekler);
    }

    // ---- Harcama ----

    [Fact]
    public async Task Aylik_sinir_asilacaksa_cagri_yapilmaz_gercek_maliyet_yazilir_yeni_ayda_sifirlanir()
    {
        await using var f = Ac(sinir: "0.02");
        using var c = await Editor(f);
        var d = await Banka(c, S(G(3), "KARGO ODEME", -10), S(G(3), "MARKET MASRAF", -20));
        f.Istemci.Yanitlari();
        var sonuc = await Sor(c, d.Id);
        // 1.000 girdi × 0,10 + 400 çıktı × 0,50 + 2.000 yazma × 0,125 + 500 okuma × 0,01 (milyon jeton başına $).
        Assert.Equal(0.000555m, sonuc.MaliyetUsd);
        var durum = await Durum(c);
        Assert.Equal((0.000555m, 0.02m, 1, "claude-haiku-5-5"), (durum.AyHarcamasiUsd, durum.AylikSinirUsd, durum.AyCagri, durum.Model));
        // Sonucu bilinmeyen çağrının ayrılan tutarı harcanmış sayılır; ikincisi sınırı aşacağı için yapılmaz.
        f.Istemci.Yanitla = _ => throw new YapayZekaHatasi("Ulaşılamadı.", true);
        Assert.Equal((HttpStatusCode.BadGateway, "Ulaşılamadı."), await SorHata(c, d.Id));
        Assert.InRange((await Durum(c)).AyHarcamasiUsd, 0.008m, 0.02m);
        Assert.Equal((HttpStatusCode.BadGateway, "Ulaşılamadı."), await SorHata(c, d.Id));
        var (kod, hata) = await SorHata(c, d.Id);
        Assert.Equal((HttpStatusCode.Conflict, "Aylık yapay zekâ sınırına (0,02 $) ulaşıldı; gelecek ay ya da sınır yükseltilince sorulabilir."), (kod, hata));
        Assert.Equal(3, f.Istemci.Istekler.Count);
        ((SabitSaat)f.Saat!).Ayarla(Bugun.AddMonths(1));
        Assert.Equal((0m, 0), ((await Durum(c)).AyHarcamasiUsd, (await Durum(c)).AyCagri));
        f.Istemci.Yanitlari(Y(1, "Gider", "Genel"));
        Assert.Equal(1, (await Sor(c, d.Id)).Onerilen);
    }

    [Fact]
    public async Task Reddedilen_cagri_ucretsizdir_ayni_istek_kimligi_ikinci_kez_cagrilmaz()
    {
        await using var f = Ac();
        using var c = await Editor(f);
        var d = await Banka(c, S(G(3), "KARGO ODEME", -10));
        f.Istemci.Yanitla = _ => throw new YapayZekaHatasi("Yapay zekâ anahtarı geçersiz.", false);
        var basarisiz = Guid.NewGuid();
        Assert.Equal((HttpStatusCode.BadGateway, "Yapay zekâ anahtarı geçersiz."), await SorHata(c, d.Id, basarisiz));
        Assert.Equal(0m, (await Durum(c)).AyHarcamasiUsd);
        Assert.Equal((HttpStatusCode.Conflict, "Önceki istek tamamlanmadı; yeniden sorun."), await SorHata(c, d.Id, basarisiz));

        f.Istemci.Yanitlari(Y(1, "Gider", "Genel"));
        var istek = Guid.NewGuid();
        var ilk = await Sor(c, d.Id, istek);
        var tekrar = await Sor(c, d.Id, istek);
        Assert.Equal(ilk, tekrar);
        Assert.Equal(2, f.Istemci.Istekler.Count);
        Assert.Equal("İstek kimliği başka bir işlem için kullanılmış.", (await Hata(c, "/api/ekstre-aktar/yapay-zeka/olcum", new YapayZekaOlcumYaz(istek, "Banka"))).Hata);
        Assert.Equal("Geçerli bir istek kimliği gerekir.", (await SorHata(c, d.Id, Guid.Empty)).Hata);
    }

    [Fact]
    public async Task Ayni_anda_tek_cagri_yapilir()
    {
        await using var f = Ac();
        using var c = await Editor(f);
        var d = await Banka(c, S(G(3), "KARGO ODEME", -10));
        var (basladi, bitir) = (new TaskCompletionSource(), new TaskCompletionSource<YapayZekaYaniti>());
        f.Istemci.Yanitla = _ => { basladi.SetResult(); return bitir.Task; };
        var ilk = Sor(c, d.Id);
        await basladi.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.Equal((HttpStatusCode.Conflict, "Başka bir yapay zekâ isteği sürüyor; bitince yeniden deneyin."), await SorHata(c, d.Id));
        bitir.SetResult(Yanit(Y(1, "Gider", "Genel")));
        Assert.Equal(1, (await ilk).Onerilen);
        Assert.Single(f.Istemci.Istekler);
    }

    // ---- Doğruluk ve ölçüm ----

    [Fact]
    public async Task Dogruluk_kaydedilen_ve_yok_sayilan_satirlardan_okunur()
    {
        await using var f = Ac();
        using var c = await Editor(f);
        var d = await Banka(c, S(G(3), "KARGO ODEME", -10), S(G(3), "VIRMAN HESAP", -30), S(G(3), "MARKET MASRAF", -40), S(G(3), "YAKIT MASRAF", -60));
        f.Istemci.Yanitlari(Y(1, "Gider", "Genel"), Y(2, "Atla"), Y(3, "Atla"), Y(4, "Gider", "Genel"));
        await Sor(c, d.Id);
        Assert.Equal((0, 0), ((await Durum(c)).Sonuclanan, (await Durum(c)).AyniTur));
        d = await Kaydet(c, d, (1, "Gider", "Genel", []), (3, "Gider", "Genel", []));
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/yok-say", new EkstreYokSayYaz(Guid.NewGuid(), d.Surum, [2], true));
        Assert.Equal((3, 2), ((await Durum(c)).Sonuclanan, (await Durum(c)).AyniTur));
    }

    [Fact]
    public async Task Olcum_kararlarin_yarisini_gizleyip_sorar_sonucu_saklar()
    {
        await using var f = Ac();
        using var c = await Editor(f);
        string[] adlar = ["KARGO", "ELEKTRIK", "DOGALGAZ", "MARKET", "YAKIT", "REKLAM", "KIRTASIYE", "SIGORTA", "INTERNET", "NAKLIYE"];
        var az = await Banka(c, S(G(9), "KARGO MASRAF", -10));
        await Kaydet(c, az, (1, "Gider", "Genel", []));
        Assert.Contains("en az 10 farklı banka kararı", (await Hata(c, "/api/ekstre-aktar/yapay-zeka/olcum", new YapayZekaOlcumYaz(Guid.NewGuid(), "Banka"))).Hata);
        Assert.Contains("Banka ya da kart", (await Hata(c, "/api/ekstre-aktar/yapay-zeka/olcum", new YapayZekaOlcumYaz(Guid.NewGuid(), "Erp"))).Hata);
        var d = await Banka(c, [.. adlar.Skip(1).Select((a, i) => S(G(8), $"{a} MASRAF", -20m - i))]);
        await Kaydet(c, d, [.. d.Satirlar.Select(s => (s.No, "Gider", s.No % 2 == 0 ? "Genel" : "Esit", s.No % 2 == 0 ? Array.Empty<int>() : [1]))]);
        // Beş satır sorulur: ilk ikisi doğru (biri dağılımıyla), üçüncüsü yanlış tür, kalanlar öneri gelmez.
        f.Istemci.Yanitla = i =>
        {
            var satirlar = i.Soru.Split('\n').Skip(2).ToList();
            Assert.Equal(5, satirlar.Count);
            // Gizlenen kararlar örneklerde yoktur.
            Assert.All(satirlar, s => Assert.DoesNotContain(s.Split(" | ")[3] + " =>", i.Baglam));
            return Task.FromResult(Yanit(Y(1, "Gider", "Genel"), Y(2, "Gider", "Kanal", [3], "Orta"), Y(3, "Atla")));
        };
        var istek = Guid.NewGuid();
        var olcum = await Post<YapayZekaOlcumDto>(c, "/api/ekstre-aktar/yapay-zeka/olcum", new YapayZekaOlcumYaz(istek, "Banka"));
        Assert.Equal(("Banka", 5, 2, 5, 2, 1, 2, 0.000555m), (olcum.Kaynak, olcum.Satir, olcum.TurDogru, olcum.DagilimSatiri, olcum.YuksekGuven, olcum.YuksekDogru, olcum.Onerisiz, olcum.MaliyetUsd));
        Assert.InRange(olcum.DagilimDogru, 0, 1);
        Assert.Equal(olcum, await Post<YapayZekaOlcumDto>(c, "/api/ekstre-aktar/yapay-zeka/olcum", new YapayZekaOlcumYaz(istek, "Banka")));
        Assert.Single(f.Istemci.Istekler);
        var durum = await Durum(c);
        Assert.Equal(olcum, Assert.Single(durum.Olcumler));
        Assert.Equal(0.000555m, durum.AyHarcamasiUsd);
    }

    /// <summary>Çağrı ve öneri mali kayıt değildir: değişiklik geçmişine düşmez; gönderilen metin ve yanıt saklanmaz.</summary>
    [Fact]
    public async Task Cagri_gecmise_dusmez_metin_saklanmaz()
    {
        await using var f = Ac();
        using var c = await Editor(f);
        var d = await Banka(c, S(G(3), "KARGO ODEME", -10));
        f.Istemci.Yanitlari(Y(1, "Gider", "Genel", kategori: "kargo"));
        await Sor(c, d.Id);
        using var kapsam = f.Services.CreateScope();
        var db = kapsam.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.DoesNotContain(db.DenetimOlaylari.AsNoTracking().Select(x => x.Varlik).ToList(), v => v.StartsWith("YapayZeka"));
        var cagri = db.YapayZekaCagrilar.AsNoTracking().Single();
        Assert.Equal((YapayZekaCagriDurumlari.Tamam, "2026-09", 1, 3_500L, 400L), (cagri.Durum, cagri.Ay, cagri.SatirSayisi, cagri.GirdiJetonu, cagri.CiktiJetonu));
        Assert.DoesNotContain("KARGO", cagri.SonucJson, StringComparison.OrdinalIgnoreCase);
    }

    // ---- SDK ----

    private sealed class SahteHttp : HttpMessageHandler, IHttpClientFactory
    {
        public int Kod = 200;
        public string Durdurma = "end_turn";
        public HttpRequestMessage? Istek;
        public string Govde = "";
        public HttpClient CreateClient(string name) => new(this, false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (Kod == 0)
                throw new HttpRequestException("ağ yok");
            Istek = r;
            Govde = await r.Content!.ReadAsStringAsync(ct);
            var yanit = Kod != 200 ? """{"type":"error","error":{"type":"hata","message":"x"}}"""
                : $$$"""{"id":"m","type":"message","role":"assistant","model":"claude-haiku-5-5","content":[{"type":"thinking","thinking":"","signature":"s"},{"type":"text","text":"{\"satirlar\":[]}"}],"stop_reason":"{{{Durdurma}}}","stop_sequence":null,"stop_details":null,"usage":{"input_tokens":12,"output_tokens":5,"cache_creation_input_tokens":3,"cache_read_input_tokens":4}}""";
            return new((HttpStatusCode)Kod) { Content = new StringContent(yanit, Encoding.UTF8, "application/json") };
        }
    }
    private static AnthropicIstemcisi Sdk(SahteHttp http) => new(new YapayZekaAyarlari(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["YapayZeka:Etkin"] = "true", ["YapayZeka:Anahtar"] = "test-anahtari" }).Build()), http);
    private static readonly YapayZekaIstegi OrnekIstek = new("yönerge", "bağlam", "soru", """{"type":"object","properties":{},"required":[],"additionalProperties":false}""");

    [Fact]
    public async Task Sdk_istegi_resmi_adrese_dogru_model_sema_ve_anahtarla_gider()
    {
        var http = new SahteHttp();
        var yanit = await Sdk(http).SorAsync(OrnekIstek, Ct);
        Assert.Equal(new YapayZekaYaniti("{\"satirlar\":[]}", null, 12, 5, 3, 4), yanit);
        Assert.Equal((HttpMethod.Post, "https://api.anthropic.com/v1/messages"), (http.Istek!.Method, http.Istek.RequestUri!.ToString()));
        Assert.Contains(http.Istek.Headers, h => h.Value.Any(v => v.Contains("test-anahtari")));
        var g = JsonDocument.Parse(http.Govde).RootElement;
        Assert.Equal(("claude-haiku-5-5", 16_000), (g.GetProperty("model").GetString(), g.GetProperty("max_tokens").GetInt32()));
        Assert.Equal(("low", "json_schema"), (g.GetProperty("output_config").GetProperty("effort").GetString(), g.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString()));
        Assert.False(g.GetProperty("output_config").GetProperty("format").GetProperty("schema").GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(("yönerge\n\nbağlam", "ephemeral"), (g.GetProperty("system")[0].GetProperty("text").GetString(), g.GetProperty("system")[0].GetProperty("cache_control").GetProperty("type").GetString()));
        Assert.Equal("soru", g.GetProperty("messages")[0].GetProperty("content").GetString());
        // Haiku 5.5 bu alanları kabul etmez.
        foreach (var alan in new[] { "temperature", "top_p", "top_k", "thinking" })
            Assert.False(g.TryGetProperty(alan, out _));
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(429, false)]
    [InlineData(500, true)]
    [InlineData(529, true)]
    [InlineData(0, true)]
    public async Task Sdk_hatalari_iletiye_ve_sonucun_bilinmesine_cevrilir(int kod, bool bilinmiyor)
    {
        var hata = await Assert.ThrowsAsync<YapayZekaHatasi>(() => Sdk(new SahteHttp { Kod = kod }).SorAsync(OrnekIstek, Ct));
        Assert.Equal(bilinmiyor, hata.SonucBilinmiyor);
        Assert.StartsWith("Yapay zekâ", hata.Message.Replace("Model bulunamadı", "Yapay zekâ"));
    }

    [Theory]
    [InlineData("refusal", "Model bu satırlara yanıt vermedi.")]
    [InlineData("max_tokens", "Yanıt yarım kaldı.")]
    [InlineData("stop_sequence", "Yanıt beklenmeyen biçimde bitti.")]
    public async Task Sdk_yarim_ya_da_ret_yaniti_metinsizdir_jetonlari_sayilir(string durdurma, string neden)
    {
        var yanit = await Sdk(new SahteHttp { Durdurma = durdurma }).SorAsync(OrnekIstek, Ct);
        Assert.Equal((null, neden, 12, 5), (yanit.Metin, yanit.Neden, yanit.GirdiJetonu, yanit.CiktiJetonu));
    }
}
