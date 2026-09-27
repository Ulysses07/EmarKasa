using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>
/// Metin girdisi (host-auth-6, giriş tarafı): \t, \r ve \n dışındaki kontrol karakterleri, U+FFFE/U+FFFF ve eşi
/// olmayan vekiller XML 1.0'da (XLSX) geçersizdir. Kullanıcı metni alan bazlı Türkçe 400 ile reddedilir; PDF'ten
/// okunan açıklama kullanıcı girdisi olmadığından reddedilmez, aynı kuralla temizlenerek kaydedilir.
/// </summary>
public class GirdiDogrulamaTests
{
    private static readonly DateOnly Bugun = KasaWebFactory.VarsayilanBugun;

    [Theory]
    [InlineData(0x0000)] [InlineData(0x0001)] [InlineData(0x0008)] [InlineData(0x000B)] [InlineData(0x000C)]
    [InlineData(0x000E)] [InlineData(0x001F)] [InlineData(0x007F)] [InlineData(0x0085)] [InlineData(0x009F)]
    [InlineData(0xFFFE)] [InlineData(0xFFFF)] [InlineData(0xD83D)] [InlineData(0xDE00)]
    public void Metin_kontrol_karakterini_ve_gecersiz_Unicodeu_alan_bazli_reddeder(int kod)
    {
        var metin = "Kira ödemesi " + (char)kod + " eylül";
        Assert.Equal(13, GirdiDogrulama.GecersizKarakterKonumu(metin));
        var v = new GirdiDogrulama();
        v.Metin(metin, "cari");
        v.Metin("Geçerli not", "not", 2000, zorunlu: false);
        var sonuc = v.Sonuc();
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(sonuc).StatusCode);
        var hata = Assert.IsType<HttpValidationProblemDetails>(Assert.IsAssignableFrom<IValueHttpResult>(sonuc).Value);
        var alan = Assert.Single(hata.Errors);
        Assert.Equal("cari", alan.Key);
        Assert.Equal("14. karakterde görünmeyen bir kontrol karakteri ya da geçersiz bir karakter var. Metni yeniden yazın.", Assert.Single(alan.Value));
    }

    [Theory]
    [InlineData("Sütun\tA\r\nSatır 2")]
    [InlineData("Emoji \uD83D\uDE00 ve Türkçe İıĞğŞşÖöÜüÇç")]
    [InlineData("Bölünmez boşluk ve sıfır genişlikli​ayırıcı")]
    [InlineData("")]
    public void Metin_sekme_satir_sonu_ve_gecerli_Unicodeu_kabul_eder(string metin)
    {
        Assert.Equal(-1, GirdiDogrulama.GecersizKarakterKonumu(metin));
        var v = new GirdiDogrulama();
        v.Metin(metin, "not", 2000, zorunlu: false);
        Assert.Null(v.Sonuc());
        Assert.Same(metin, GirdiDogrulama.Temizle(metin));
    }

    [Fact]
    public void Hata_konumu_kullanicinin_gordugu_karakterle_sayilir()
    {
        // Emoji iki UTF-16 birimidir ama kullanıcı için tek karakterdir: kontrol karakteri 4. sıradadır.
        var v = new GirdiDogrulama();
        v.Metin(new string([(char)0xD83D, (char)0xDE00]) + " a" + (char)1, "not", 2000, zorunlu: false);
        var hata = Assert.IsType<HttpValidationProblemDetails>(Assert.IsAssignableFrom<IValueHttpResult>(v.Sonuc()).Value);
        Assert.StartsWith("4. karakterde", Assert.Single(hata.Errors["not"]));
    }

    // Eşsiz vekil InlineData ile verilemez: xUnit teori verisini UTF-8 ile taşır ve eşsiz vekili U+FFFD'ye çevirir.
    // Metinler bu yüzden test içinde karakterlerden kurulur.
    [Fact]
    public void Eslesmeyen_vekil_konumuyla_bulunur_ve_temizlenir()
    {
        const char yuksek = (char)0xD83D, dusuk = (char)0xDE00;
        Assert.Equal(-1, GirdiDogrulama.GecersizKarakterKonumu(new string([yuksek, dusuk])));
        Assert.Equal(0, GirdiDogrulama.GecersizKarakterKonumu(new string([dusuk, yuksek]))); // ters sıralı çift: ikisi de eşsiz
        Assert.Equal(2, GirdiDogrulama.GecersizKarakterKonumu("ab" + yuksek));               // sonda kalan yüksek vekil
        Assert.Equal(2, GirdiDogrulama.GecersizKarakterKonumu(new string([yuksek, dusuk, dusuk])));
        Assert.Equal("x ", GirdiDogrulama.Temizle("x" + yuksek));
        Assert.Equal(" ", GirdiDogrulama.Temizle(new string([dusuk, yuksek])));
        Assert.Equal(new string([yuksek, dusuk, ' ']), GirdiDogrulama.Temizle(new string([yuksek, dusuk, dusuk])));
    }

    [Theory]
    [InlineData("MARKET\u0001ALIŞVERİŞ", "MARKET ALIŞVERİŞ")] // kelimeler birleşmez
    [InlineData("MARKET \u0001ALIŞVERİŞ", "MARKET ALIŞVERİŞ")] // boşluk çoğalmaz
    [InlineData("A\u0001\u0002\u0003B", "A B")]
    [InlineData("\u0003KİRA\u0007", " KİRA ")]
    [InlineData("Satır\r\n\tsekme\uFFFE", "Satır\r\n\tsekme ")]
    [InlineData("\uD83D\uDE00\u000B", "\uD83D\uDE00 ")]
    public void Temizle_gecersiz_karakteri_bosluga_cevirir_gecerli_metne_dokunmaz(string kirli, string temiz)
    {
        Assert.Equal(temiz, GirdiDogrulama.Temizle(kirli));
        Assert.Equal(-1, GirdiDogrulama.GecersizKarakterKonumu(GirdiDogrulama.Temizle(kirli)));
    }

    [Fact]
    public async Task Api_denetim_karakterli_metni_kaydetmeden_alan_hatasiyla_reddeder()
    {
        await using var f = KasaWebFactory.Sabit(Bugun); using var c = await f.EditorClientAsync();
        var cari = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Bugun, "Kira\u000B", 10m, "MEZAT", GiderTipi.Cari));
        Assert.Equal(HttpStatusCode.BadRequest, cari.StatusCode);
        Assert.Contains("kontrol karakteri", AlanHatasi(await cari.Content.ReadFromJsonAsync<JsonElement>(), "cari"));
        var not = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Bugun, "Kira", 10m, "MEZAT", GiderTipi.Cari, Not: "Açıklama\u0001"));
        Assert.Equal(HttpStatusCode.BadRequest, not.StatusCode);
        Assert.Contains("kontrol karakteri", AlanHatasi(await not.Content.ReadFromJsonAsync<JsonElement>(), "not"));
        var kanal = await c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("Yeni\u0007kanal"));
        Assert.Equal(HttpStatusCode.BadRequest, kanal.StatusCode);
        Assert.Contains("kontrol karakteri", AlanHatasi(await kanal.Content.ReadFromJsonAsync<JsonElement>(), "ad"));
        var kontrol = await c.PostAsJsonAsync("/api/kasa-kontrol/onizleme", new KasaKontrolOnizle(0m, "Sayım\u001F"));
        Assert.Equal(HttpStatusCode.BadRequest, kontrol.StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.Empty(db.Islemler); Assert.DoesNotContain(db.Kanallar, k => k.Ad.StartsWith("Yeni"));
        }
        // Sekme ve satır sonu geçerli metindir.
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Bugun, "Kira\tEylül", 10m, "MEZAT", GiderTipi.Cari, Not: "Satır 1\r\nSatır 2"))).EnsureSuccessStatusCode();
    }

    [Fact]
    public void Ekstre_okuyucu_PDF_aciklamasini_ve_kaynak_satiri_temizler()
    {
        var sonuc = EkstreMetinOkuyucu.Oku(PdfMetni("MARKET\u0001ALIŞVERİŞİ\u0007"), "Banka", "Akbank");
        var satir = Assert.Single(sonuc.Satirlar);
        Assert.StartsWith("MARKET ALIŞVERİŞİ", satir.Aciklama);
        Assert.Equal(-1, GirdiDogrulama.GecersizKarakterKonumu(satir.Aciklama));
        Assert.Equal(-1, GirdiDogrulama.GecersizKarakterKonumu(satir.KaynakSatir));
        Assert.Equal(10m, satir.Tutar); Assert.Equal("Gider", satir.OnerilenIslem);
    }

    [Fact]
    public async Task Ekstre_yolunda_kontrol_karakterli_satir_temizlenerek_kaydedilir()
    {
        await using var f = new PdfFabrikasi(PdfMetni("MARKET\u0001ALIŞVERİŞİ\u0007")) { Saat = new SabitSaat(Bugun) };
        using var c = await f.EditorClientAsync();
        EkstreBelgeDto belge;
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent("Banka"), "kaynak"); form.Add(new StringContent("Akbank"), "banka"); form.Add(new StringContent("Ana hesap"), "hesapAdi");
            form.Add(new ByteArrayContent("%PDF-1.7 ekstre"u8.ToArray()), "dosya", "ekstre.pdf");
            using var r = await c.PostAsync("/api/ekstre-aktar/yukle", form);
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
            belge = (await r.Content.ReadFromJsonAsync<EkstreBelgeDto>())!;
        }
        var okunan = Assert.Single(belge.Satirlar);
        Assert.StartsWith("MARKET ALIŞVERİŞİ", okunan.Aciklama);
        Assert.Equal(-1, GirdiDogrulama.GecersizKarakterKonumu(okunan.Aciklama));
        belge = await Kaydet(c, belge, new EkstreSatirYaz(okunan.No, Bugun, okunan.Aciklama, 10m, "Gider", "Genel", []));

        // Bu sürümden önce yüklenmiş belge temizlenmemiş açıklama taşır; istemci onu olduğu gibi geri gönderir.
        int eskiId;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var eski = new EkstreBelgeEntity { Kaynak = "Banka", Banka = "QNB", HesapAdi = "Eski hesap", DosyaAdi = "eski.pdf", DosyaOzeti = Guid.NewGuid().ToString(),
                Dosya = "%PDF-eski"u8.ToArray(), Yuklendi = f.Saat!.GetUtcNow().ToUnixTimeMilliseconds(),
                SatirlarJson = JsonSerializer.Serialize(new[] { new EkstreOkunanSatir(1, 1, "kaynak\u0001satır", Bugun, "ESKİ\u0001BELGE\u0007", 20m, "Cikis", "Gider", "Hareket", "TRY", []) }) };
            db.EkstreBelgeler.Add(eski); db.SaveChanges(); eskiId = eski.Id;
        }
        var eskiBelge = (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{eskiId}"))!;
        eskiBelge = await Kaydet(c, eskiBelge, new EkstreSatirYaz(1, Bugun, "ESKİ\u0001BELGE\u0007", 20m, "Gider", "Genel", []));

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.All(db.Islemler.Select(i => i.Cari).ToList(), cari => Assert.Equal(-1, GirdiDogrulama.GecersizKarakterKonumu(cari)));
            Assert.Contains(db.Islemler, i => i.Cari.StartsWith("MARKET ALIŞVERİŞİ"));
            Assert.Equal("ESKİ BELGE", db.Islemler.Single(i => i.TutarTl == 20m).Cari);
            Assert.Equal("ESKİ BELGE", db.EkstreKayitlar.Single(k => k.BelgeId == eskiId).Aciklama);
        }
        // Dışa aktarım (XLSX) kaydedilen metni sorunsuz yazar.
        (await c.GetAsync($"/api/disari-aktar?baslangic={Bugun:yyyy-MM-dd}&bitis={Bugun:yyyy-MM-dd}&bicim=xlsx")).EnsureSuccessStatusCode();
        // İptal gerekçesi kullanıcı metnidir: kontrol karakteri reddedilir.
        var kayit = Assert.Single(eskiBelge.Kayitlar);
        var iptal = await c.PostAsJsonAsync($"/api/ekstre-aktar/{eskiId}/kayitlar/{kayit.Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Hatalı\u0001 satır"));
        Assert.Equal(HttpStatusCode.BadRequest, iptal.StatusCode);
    }

    private static string AlanHatasi(JsonElement govde, string alan) => govde.GetProperty("errors").GetProperty(alan)[0].GetString()!;

    private static async Task<EkstreBelgeDto> Kaydet(HttpClient c, EkstreBelgeDto belge, EkstreSatirYaz satir)
    {
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [satir]);
        var onizleme = await c.PostAsJsonAsync($"/api/ekstre-aktar/{belge.Id}/onizleme", istek);
        Assert.True(onizleme.IsSuccessStatusCode, await onizleme.Content.ReadAsStringAsync());
        var ozet = (await onizleme.Content.ReadFromJsonAsync<EkstreOnizlemeDto>())!;
        Assert.All(ozet.Satirlar, s => Assert.Equal(-1, GirdiDogrulama.GecersizKarakterKonumu(s.Aciklama)));
        var kayit = await c.PostAsJsonAsync($"/api/ekstre-aktar/{belge.Id}/kaydet", istek with { OnizlemeOzeti = ozet.OnizlemeOzeti, TekrarOnay = true });
        Assert.True(kayit.IsSuccessStatusCode, await kayit.Content.ReadAsStringAsync());
        return (await kayit.Content.ReadFromJsonAsync<EkstreBelgeDto>())!;
    }

    // Sütun başlıkları ve tutar konumları okuyucunun tablo düzenine uyar: tutar 40., bakiye 53. karakterde başlar.
    private static string PdfMetni(string aciklama) =>
        "İşlem Tarihi    Açıklama                Tutar        Bakiye\n" +
        $"{Bugun:dd.MM.yyyy}    {aciklama.PadRight(26)}-10,00 TL    990,00 TL\n";

    private sealed class PdfFabrikasi(string metin) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => { services.RemoveAll<IPdfMetinOkuyucu>(); services.AddSingleton<IPdfMetinOkuyucu>(new SabitPdf(metin)); });
        }
    }

    private sealed class SabitPdf(string metin) : IPdfMetinOkuyucu
    {
        public Task<string> OkuAsync(byte[] pdf, CancellationToken ct) => Task.FromResult(metin);
    }
}
