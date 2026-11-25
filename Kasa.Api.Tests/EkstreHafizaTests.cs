using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core.Kodlar;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>İşlem hafızası (docs/specs/2026-11-25-islem-hafizasi.md): kaydedilen ve yok sayılan satırlardan sonraki belgelere öneri.</summary>
public class EkstreHafizaTests
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

    private static async Task<HttpClient> Editor(Fabrika f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(Bugun.Year, 1, 1), kasaAcilisDevri = 1000m }, Ct)).EnsureSuccessStatusCode();
        return c;
    }

    /// <summary>Banka belgesi yükler; her satır "açıklama tutar" biçimindedir, tarih bugünden <paramref name="gun"/> gün öncedir.</summary>
    private static async Task<EkstreBelgeDto> Yukle(Fabrika f, HttpClient c, int gun, string[] satirlar, string banka = "Akbank", string hesap = "Ana hesap")
    {
        f.Pdf.Metin = "Para Birimi: TL\n" + string.Join("\n", satirlar.Select(s => $"{Bugun.AddDays(-gun):dd.MM.yyyy} {s}"));
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Banka"), "kaynak" }, { new StringContent(banka), "banka" }, { new StringContent(hesap), "hesapAdi" },
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

    private static EkstreSatirYaz Satir(EkstreBelgeDto d, int no, string tur = EkstreIslemTurleri.Gider, int[]? kanallar = null, string? aciklama = null)
    {
        var s = d.Satirlar.Single(s => s.No == no);
        return new(no, s.Tarih!.Value, aciklama ?? s.Aciklama, s.Tutar!.Value, tur, kanallar is null ? DagilimBicimleri.Genel : DagilimBicimleri.Esit,
            kanallar?.Select(k => new KanalPayYaz(k, 0)).ToArray() ?? []);
    }

    private static async Task<EkstreBelgeDto> Kaydet(HttpClient c, EkstreBelgeDto d, params EkstreSatirYaz[] satirlar)
    {
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, satirlar);
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{d.Id}/onizleme", istek);
        return await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = true });
    }

    private static Task<EkstreBelgeDto> YokSay(HttpClient c, EkstreBelgeDto d, bool yokSay, params int[] nolar) =>
        Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/yok-say", new EkstreYokSayYaz(Guid.NewGuid(), d.Surum, nolar, yokSay));

    private static async Task<List<EkstreKuralOnerisi>> Oneriler(HttpClient c, EkstreBelgeDto d) =>
        (await c.GetFromJsonAsync<List<EkstreKuralOnerisi>>($"/api/ekstre-aktar/{d.Id}/oneriler", Ct))!;

    private static async Task<EkstreKuralOnerisi> Oneri(HttpClient c, EkstreBelgeDto d, int no = 1) => (await Oneriler(c, d)).Single(o => o.SatirNo == no);

    private static async Task<int[]> Kanallar(HttpClient c) =>
        (await c.GetFromJsonAsync<List<JsonElement>>("/api/kanallar", Ct))!.Select(k => k.GetProperty("id").GetInt32()).ToArray();

    // ---- Anahtar ----

    [Theory]
    [InlineData("FAST 20261125 AHMET YILMAZ KASIM KİRA 1234567", "FAST AHMET YILMAZ KIRA")]
    [InlineData("Yurtiçi Kargo Ref:88112233 25.11.2026 14:35", "YURTICI KARGO REF")]
    [InlineData("A101 MAĞAZA 4521 İSTANBUL", "A101 MAGAZA ISTANBUL")]
    [InlineData("TR120006100519786457841326 MEHMET KAYA 1.500,00 TL", "MEHMET KAYA")]
    [InlineData("3M", "3M")]
    [InlineData("5406 12** **** 1234", null)]
    [InlineData("25.11.2026 / 44", null)]
    [InlineData("TL", null)]
    public void Anahtar_degisen_parcalari_atar(string aciklama, string? beklenen) => Assert.Equal(beklenen, EkstreHafizaServisi.Anahtar(aciklama));

    [Theory]
    [InlineData("GELEN HAVALE", true)]
    [InlineData("ATM PARA YATIRMA", true)]
    [InlineData("HAVALE KOMISYONU", false)]
    [InlineData("FAST AHMET YILMAZ", false)]
    public void Genel_anahtar(string anahtar, bool genel) => Assert.Equal(genel, EkstreHafizaServisi.Genel(anahtar));

    // ---- Öğrenme ----

    [Fact]
    public async Task Kaydedilen_satir_sonraki_belgede_ayni_islemi_onerir_kasa_ve_belge_degismez()
    {
        await using var f = new Fabrika();
        using var c = await Editor(f);
        var kanallar = await Kanallar(c);
        var ilk = await Yukle(f, c, 30, ["YURTİÇİ KARGO REF 11223344 -100,00", "BİLİNMEYEN FİRMA -50,00"]);
        Assert.All(await Oneriler(c, ilk), o => Assert.Equal("Yok", o.Durum));
        await Kaydet(c, ilk, Satir(ilk, 1, kanallar: [kanallar[0], kanallar[1]], aciklama: "Yurtiçi Kargo"));

        var d = await Yukle(f, c, 1, ["Yurtiçi-Kargo Ref 99887766 -250,00", "YURTİÇİ KARGO REF 5 250,00", "YURTİÇİ KARGOLAR -10,00"]);
        var once = await c.GetStringAsync("/api/rapor/panel", Ct);
        var liste = await Oneriler(c, d);
        Assert.Equal(once, await c.GetStringAsync("/api/rapor/panel", Ct));
        var o = liste[0];
        Assert.Equal((EkstreHafizaDurumlari.Hazir, EkstreIslemTurleri.Gider, DagilimBicimleri.Esit), (o.Durum, o.IslemTuru, o.DagilimTuru));
        Assert.Equal(new[] { kanallar[0], kanallar[1] }.Order(), o.KanalIds);
        Assert.Equal(new EkstreHafizaOnerisi("YURTICI KARGO REF", 1, Bugun.AddDays(-30), null, "Yurtiçi Kargo"), o.Hafiza);
        Assert.Equal($"Hafızadan: 1 kez aynı işlendi, son {Bugun.AddDays(-30):dd.MM.yyyy}.", o.Aciklama);
        // Yön ya da sözcük farklıysa öneri yoktur.
        Assert.Equal(["Yok", "Yok"], liste.Skip(1).Select(x => x.Durum));
        // Belgede kaydedilen satır aynı belgenin kalan satırlarına da öğretir.
        var tekrar = await Yukle(f, c, 0, ["KIRTASİYE DÜNYASI -10,00", "KIRTASİYE DÜNYASI -20,00"]);
        tekrar = await Kaydet(c, tekrar, Satir(tekrar, 1));
        Assert.Equal((EkstreHafizaDurumlari.Hazir, 1), ((await Oneri(c, tekrar, 2)).Durum, (await Oneri(c, tekrar, 2)).Hafiza!.Adet));
    }

    [Fact]
    public async Task Son_iki_karar_farkliysa_doldurmaz_ayniysa_yeni_karar_gecer()
    {
        await using var f = new Fabrika();
        using var c = await Editor(f);
        var kanallar = await Kanallar(c);
        var d1 = await Yukle(f, c, 30, ["DENİZ TİCARET -100,00"]);
        await Kaydet(c, d1, Satir(d1, 1));
        var d2 = await Yukle(f, c, 20, ["DENİZ TİCARET -200,00"]);
        await Kaydet(c, d2, Satir(d2, 1, kanallar: [kanallar[0]]));
        var d3 = await Yukle(f, c, 10, ["DENİZ TİCARET -300,00"]);
        var o = await Oneri(c, d3);
        Assert.Equal((EkstreHafizaDurumlari.Kontrol, (string?)null, (string?)null, 0), (o.Durum, o.IslemTuru, o.DagilimTuru, o.KanalIds.Count));
        Assert.StartsWith("Daha önce farklı işlendi", o.Aciklama);
        await Kaydet(c, d3, Satir(d3, 1, kanallar: [kanallar[0]]));
        o = await Oneri(c, await Yukle(f, c, 1, ["DENİZ TİCARET -400,00"]));
        Assert.Equal((EkstreHafizaDurumlari.Hazir, DagilimBicimleri.Esit, 2), (o.Durum, o.DagilimTuru, o.Hafiza!.Adet));
    }

    [Fact]
    public async Task Iptal_edilen_kayit_hafizadan_cikar_yok_sayilan_satir_yok_say_onerir_geri_alinca_kalkar()
    {
        await using var f = new Fabrika();
        using var c = await Editor(f);
        var d = await Yukle(f, c, 10, ["DENİZ TİCARET -100,00", "VİRMAN VADELİ HESAP -500,00"]);
        var k = await Kaydet(c, d, Satir(d, 1));
        var kapali = await YokSay(c, k, true, 2);
        var yeni = await Yukle(f, c, 1, ["DENİZ TİCARET -100,00", "VİRMAN VADELİ HESAP -700,00"]);
        var liste = await Oneriler(c, yeni);
        Assert.Equal([EkstreHafizaDurumlari.Hazir, EkstreHafizaDurumlari.Hazir], liste.Select(o => o.Durum));
        Assert.Equal([EkstreIslemTurleri.Gider, EkstreIslemTurleri.Atla], liste.Select(o => o.IslemTuru));
        Assert.StartsWith("Hafızadan: 1 kez yok sayıldı", liste[1].Aciklama);
        var iptal = await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/kayitlar/{kapali.Kayitlar[0].Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış"));
        await YokSay(c, iptal, false, 2);
        Assert.All(await Oneriler(c, yeni), o => Assert.Equal("Yok", o.Durum));
    }

    [Fact]
    public async Task Genel_sozcuklu_aciklama_iki_ayni_karar_ister()
    {
        await using var f = new Fabrika();
        using var c = await Editor(f);
        var d1 = await Yukle(f, c, 30, ["GELEN HAVALE 100,00"]);
        await Kaydet(c, d1, Satir(d1, 1, EkstreIslemTurleri.Gelir));
        var d2 = await Yukle(f, c, 20, ["GELEN HAVALE 900,00"]);
        Assert.Equal("Yok", (await Oneri(c, d2)).Durum);
        await Kaydet(c, d2, Satir(d2, 1, EkstreIslemTurleri.Gelir));
        Assert.Equal(EkstreHafizaDurumlari.Hazir, (await Oneri(c, await Yukle(f, c, 1, ["GELEN HAVALE 333,00"]))).Durum);
    }

    [Fact]
    public async Task En_yakin_kapsam_secilir_once_ayni_hesap_sonra_banka_sonra_hepsi()
    {
        await using var f = new Fabrika();
        using var c = await Editor(f);
        var kanallar = await Kanallar(c);
        var akbank = await Yukle(f, c, 30, ["DENİZ TİCARET -100,00"]);
        await Kaydet(c, akbank, Satir(akbank, 1));
        var garanti = await Yukle(f, c, 20, ["DENİZ TİCARET -100,00"], "Garanti", "Garanti hesabı");
        await Kaydet(c, garanti, Satir(garanti, 1, kanallar: [kanallar[0]]));
        async Task<string?> Dagilim(string banka, string hesap) => (await Oneri(c, await Yukle(f, c, 1, ["DENİZ TİCARET -5,00"], banka, hesap))).DagilimTuru;
        Assert.Equal(DagilimBicimleri.Genel, await Dagilim("Akbank", "Ana hesap"));
        Assert.Equal(DagilimBicimleri.Genel, await Dagilim("Akbank", "Başka hesap"));
        Assert.Equal(DagilimBicimleri.Esit, await Dagilim("Garanti", "Yeni hesap"));
        // Kapsam dışı bankada bütün kararlar: son ikisi farklı olduğundan doldurulmaz.
        Assert.Null(await Dagilim("QNB", "QNB hesabı"));
    }

    [Fact]
    public async Task Duzeltilmis_aciklama_rakam_ya_da_ay_adi_iceriyorsa_tasinmaz()
    {
        await using var f = new Fabrika();
        using var c = await Editor(f);
        var d = await Yukle(f, c, 30, ["AHMET YILMAZ -100,00", "VELİ KAYA -100,00", "ZEYNEP AK -100,00"]);
        await Kaydet(c, d, Satir(d, 1, aciklama: "Kira Kasım"), Satir(d, 2, aciklama: "Kira 2026"), Satir(d, 3, aciklama: "Muhasebeci"));
        var liste = await Oneriler(c, await Yukle(f, c, 1, ["AHMET YILMAZ -5,00", "VELİ KAYA -5,00", "ZEYNEP AK -5,00"]));
        Assert.Equal([null, null, "Muhasebeci"], liste.Select(o => o.Hafiza!.Aciklama));
    }

    // ---- Kontrol nedenleri ----

    [Fact]
    public async Task Kesin_olmayan_satir_doldurulur_ama_hazir_sayilmaz()
    {
        await using var f = new Fabrika();
        using var c = await Editor(f);
        var kanallar = await Kanallar(c);
        var d = await Yukle(f, c, 30, ["DENİZ TİCARET 100,00", "ÖZEL PAYLI -100,00", "KANALLI FİRMA -100,00", "VİRMAN HESAPLAR ARASI -100,00"]);
        Assert.Equal("Belirsiz", d.Satirlar[0].Yon);
        await Kaydet(c, d, Satir(d, 1), Satir(d, 2) with { DagilimTuru = DagilimBicimleri.Ozel, Dagilimlar = [new(kanallar[0], 60m), new(kanallar[1], 40m)] },
            Satir(d, 3, kanallar: [kanallar[2]]), Satir(d, 4));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Kanallar.Single(k => k.Id == kanallar[2]).Aktif = false;
            db.SaveChanges();
        }
        var liste = await Oneriler(c, await Yukle(f, c, 1, ["DENİZ TİCARET 5,00", "ÖZEL PAYLI -5,00", "KANALLI FİRMA -5,00", "VİRMAN HESAPLAR ARASI -5,00"]));
        Assert.All(liste, o => Assert.Equal((EkstreHafizaDurumlari.Kontrol, EkstreIslemTurleri.Gider), (o.Durum, o.IslemTuru)));
        Assert.Contains("giriş/çıkış yönü okunamadı", liste[0].Aciklama);
        Assert.Contains("kanal tutarları o satıra özeldi", liste[1].Aciklama);
        Assert.Equal((DagilimBicimleri.Ozel, 2), (liste[1].DagilimTuru, liste[1].KanalIds.Count));
        Assert.Contains("kanalı pasif ya da silinmiş", liste[2].Aciklama);
        Assert.Equal((null, 0), (liste[2].DagilimTuru, liste[2].KanalIds.Count));
        Assert.Contains("hesaplar arası transfer olabilir", liste[3].Aciklama);
    }

    [Fact]
    public async Task Kisisel_kural_hafizadan_onceliklidir()
    {
        await using var f = new Fabrika();
        using var c = await Editor(f);
        var kanallar = await Kanallar(c);
        var d = await Yukle(f, c, 30, ["DENİZ TİCARET -100,00"]);
        await Kaydet(c, d, Satir(d, 1));
        (await c.PostAsJsonAsync("/api/ekstre-aktar/kurallar", new EkstreKuralYaz(Guid.NewGuid(), 0, "Deniz", "Banka", null, "DENİZ TİCARET", null, "Gider", "Esit", [kanallar[0]]), Ct)).EnsureSuccessStatusCode();
        var o = await Oneri(c, await Yukle(f, c, 1, ["DENİZ TİCARET -5,00"]));
        Assert.Equal(("Oneri", "Esit", (EkstreHafizaOnerisi?)null), (o.Durum, o.DagilimTuru, o.Hafiza));
    }

    [Fact]
    public async Task Kart_ekstresi_kendi_kartinin_hafizasini_kullanir_banka_hafizasi_karismaz()
    {
        await using var f = new Fabrika();
        using var c = await Editor(f);
        var kanallar = await Kanallar(c);
        int kart;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var k = new KrediKartiEntity { Ad = "Kart" };
            db.KrediKartlari.Add(k);
            db.SaveChanges();
            kart = k.Id;
            EkstreBelgeEntity Belge(string kaynak, int? kartId, params EkstreOkunanSatir[] satirlar) => new()
            {
                Kaynak = kaynak,
                Banka = "Akbank",
                HesapAdi = kartId is null ? "Ana" : "",
                KartId = kartId,
                DosyaAdi = "e.pdf",
                DosyaOzeti = Guid.NewGuid().ToString(),
                SatirlarJson = JsonSerializer.Serialize(satirlar),
            };
            EkstreOkunanSatir Okunan(int gun, string tur) => new(1, 1, "MIGROS", Bugun.AddDays(-gun), "MIGROS ISTANBUL", 10m, "Cikis", tur, "", "TRY", []);
            var eski = Belge("Kart", kart, Okunan(30, EkstreIslemTurleri.KartHarcama));
            var banka = Belge("Banka", null, Okunan(20, EkstreIslemTurleri.Gider));
            db.EkstreBelgeler.AddRange(eski, banka, Belge("Kart", kart, Okunan(1, EkstreIslemTurleri.KartHarcama)));
            db.SaveChanges();
            var paylar = JsonSerializer.Serialize(new[] { new TakipKanalPayi(kanallar[0], "x", 10m) });
            void Kayit(int belge, string tur, string dagilim, string json, int? kartId) => db.Database.ExecuteSql(
                $"INSERT INTO EkstreKayitlar (BelgeId, SatirNo, Tarih, Aciklama, Tutar, IslemTuru, DagilimTuru, DagilimJson, KrediKartiId, Iptal) VALUES ({belge}, 1, {Bugun}, 'MIGROS ISTANBUL', '10.0', {tur}, {dagilim}, {json}, {kartId}, 0)");
            Kayit(eski.Id, EkstreIslemTurleri.KartHarcama, DagilimBicimleri.Ozel, paylar, kart);
            Kayit(banka.Id, EkstreIslemTurleri.Gider, DagilimBicimleri.Genel, "[]", null);
        }
        var belgeler = (await c.GetFromJsonAsync<List<EkstreBelgeOzetDto>>("/api/ekstre-aktar", Ct))!;
        var o = (await c.GetFromJsonAsync<List<EkstreKuralOnerisi>>($"/api/ekstre-aktar/{belgeler.Max(b => b.Id)}/oneriler", Ct))!.Single();
        // Tek kanallı özel dağılım eşit tek kanal olarak taşınır.
        Assert.Equal((EkstreHafizaDurumlari.Hazir, EkstreIslemTurleri.KartHarcama, DagilimBicimleri.Esit, kanallar[0], 1), (o.Durum, o.IslemTuru, o.DagilimTuru, o.KanalIds.Single(), o.Hafiza!.Adet));
    }

    // ---- Liste ve unutma ----

    [Fact]
    public async Task Unutulan_anahtar_onerilmez_listeden_duser_yeni_kayit_yeniden_ogretir()
    {
        await using var f = new Fabrika();
        using var c = await Editor(f);
        var d = await Yukle(f, c, 30, ["DENİZ TİCARET -100,00", "DENİZ TİCARET 70,00", "VİRMAN VADELİ -5,00"]);
        Assert.Equal("Belirsiz", d.Satirlar[1].Yon);
        var k = await Kaydet(c, d, Satir(d, 1), Satir(d, 2, EkstreIslemTurleri.Gelir));
        await YokSay(c, k, true, 3);
        var panel = await c.GetStringAsync("/api/rapor/panel", Ct);
        var liste = (await c.GetFromJsonAsync<List<EkstreHafizaDto>>("/api/ekstre-aktar/hafiza", Ct))!;
        Assert.Equal([("DENIZ TICARET", "Belirsiz", EkstreIslemTurleri.Gelir), ("DENIZ TICARET", "Cikis", EkstreIslemTurleri.Gider), ("VIRMAN VADELI", "Cikis", EkstreIslemTurleri.Atla)],
            liste.Select(h => (h.Anahtar, h.Yon, h.IslemTuru)).OrderBy(h => h));
        Assert.All(liste, h => Assert.Equal((1, Bugun.AddDays(-30), false, EkstreKaynaklari.Banka), (h.Adet, h.SonTarih, h.Celiskili, h.Kaynak)));

        var istek = new EkstreHafizaUnutYaz(Guid.NewGuid(), "Banka", "DENIZ TICARET", "Cikis");
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/ekstre-aktar/hafiza/unut", istek, Ct)).StatusCode);
        Assert.Equal(panel, await c.GetStringAsync("/api/rapor/panel", Ct));
        var yeni = await Yukle(f, c, 10, ["DENİZ TİCARET -5,00", "VİRMAN VADELİ -5,00"]);
        Assert.Equal(["Yok", EkstreHafizaDurumlari.Hazir], (await Oneriler(c, yeni)).Select(o => o.Durum));
        Assert.Equal(2, (await c.GetFromJsonAsync<List<EkstreHafizaDto>>("/api/ekstre-aktar/hafiza", Ct))!.Count);
        // Kayıt sonraki karardır: yeniden öğrenilir. Yanıtı kaybolan unutma isteği tekrarlanırsa yeni karar unutulmaz.
        await Kaydet(c, yeni, Satir(yeni, 1));
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/ekstre-aktar/hafiza/unut", istek, Ct)).StatusCode);
        var son = await Yukle(f, c, 1, ["DENİZ TİCARET -6,00", "VİRMAN VADELİ -5,00"]);
        Assert.Equal((EkstreHafizaDurumlari.Hazir, 1), ((await Oneri(c, son)).Durum, (await Oneri(c, son)).Hafiza!.Adet));
        // Yok sayma kararı da unutulur; ikinci unutma aynı satırı günceller.
        foreach (var yeniIstek in new[] { new EkstreHafizaUnutYaz(Guid.NewGuid(), "Banka", "VIRMAN VADELI", "Cikis"), istek with { IstekId = Guid.NewGuid() } })
            Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/ekstre-aktar/hafiza/unut", yeniIstek, Ct)).StatusCode);
        Assert.All(await Oneriler(c, son), o => Assert.Equal("Yok", o.Durum));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(2, db.EkstreHafizaUnutulanlar.Count());
        Assert.Equal(3, db.DenetimOlaylari.AsNoTracking().Count(x => x.Varlik == "EkstreHafizaUnutulan"));
    }

    [Fact]
    public async Task Unutma_gecersiz_ve_hafizada_olmayan_istegi_reddeder()
    {
        await using var f = new Fabrika();
        using var c = await Editor(f);
        async Task<HttpStatusCode> Unut(object govde) => (await c.PostAsJsonAsync("/api/ekstre-aktar/hafiza/unut", govde, Ct)).StatusCode;
        Assert.Equal(HttpStatusCode.NotFound, await Unut(new EkstreHafizaUnutYaz(Guid.NewGuid(), "Banka", "YOK", "Cikis")));
        Assert.Equal(HttpStatusCode.BadRequest, await Unut(new EkstreHafizaUnutYaz(Guid.Empty, "Banka", "YOK", "Cikis")));
        foreach (var govde in new[] { new EkstreHafizaUnutYaz(Guid.NewGuid(), "X", "YOK", "Cikis"), new(Guid.NewGuid(), "Banka", "", "Cikis"), new(Guid.NewGuid(), "Banka", "YOK", "X") })
            Assert.Equal(HttpStatusCode.BadRequest, await Unut(govde));
    }
}
