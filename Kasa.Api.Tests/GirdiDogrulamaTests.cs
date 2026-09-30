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
    [InlineData(0x0000)]
    [InlineData(0x0001)]
    [InlineData(0x0008)]
    [InlineData(0x000B)]
    [InlineData(0x000C)]
    [InlineData(0x000E)]
    [InlineData(0x001F)]
    [InlineData(0x007F)]
    [InlineData(0x0085)]
    [InlineData(0x009F)]
    [InlineData(0xFFFE)]
    [InlineData(0xFFFF)]
    [InlineData(0xD83D)]
    [InlineData(0xDE00)]
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
        var ct = TestContext.Current.CancellationToken;
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        var cari = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Bugun, "Kira\u000B", 10m, "MEZAT", GiderTipi.Cari), cancellationToken: ct);
        Assert.Equal(HttpStatusCode.BadRequest, cari.StatusCode);
        Assert.Contains("kontrol karakteri", AlanHatasi(await cari.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct), "cari"));
        var not = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Bugun, "Kira", 10m, "MEZAT", GiderTipi.Cari, Not: "Açıklama\u0001"), cancellationToken: ct);
        Assert.Equal(HttpStatusCode.BadRequest, not.StatusCode);
        Assert.Contains("kontrol karakteri", AlanHatasi(await not.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct), "not"));
        var kanal = await c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("Yeni\u0007kanal"), cancellationToken: ct);
        Assert.Equal(HttpStatusCode.BadRequest, kanal.StatusCode);
        Assert.Contains("kontrol karakteri", AlanHatasi(await kanal.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct), "ad"));
        var kontrol = await c.PostAsJsonAsync("/api/kasa-kontrol/onizleme", new KasaKontrolOnizle(0m, "Sayım\u001F"), cancellationToken: ct);
        Assert.Equal(HttpStatusCode.BadRequest, kontrol.StatusCode);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.Empty(db.Islemler);
            Assert.DoesNotContain(db.Kanallar, k => k.Ad.StartsWith("Yeni"));
        }
        // Sekme ve satır sonu geçerli metindir.
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Bugun, "Kira\tEylül", 10m, "MEZAT", GiderTipi.Cari, Not: "Satır 1\r\nSatır 2"), cancellationToken: ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Mevcut_kontrol_karakterli_kanal_adina_gider_ve_gelir_girilebilir_yeni_ad_reddedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        // Metin kuralından önce kaydedilmiş, canlı veride bulunabilecek kanal adı: seçim kayıtlı kanalla eşleşir,
        // kontrol karakteri kuralı yalnız yeni ad oluşturmada ve yeniden adlandırmada uygulanır.
        const string eskiAd = "ESKİ\u0001KANAL";
        int kanalId;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var kanal = new KanalEntity { Ad = eskiAd, Sira = 9 };
            db.Kanallar.Add(kanal);
            db.SaveChanges();
            kanalId = kanal.Id;
        }
        var gider = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Bugun, "Kira", 10m, eskiAd, GiderTipi.Cari), cancellationToken: ct);
        Assert.Equal(HttpStatusCode.Created, gider.StatusCode);
        var kayit = await gider.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        Assert.Equal((kanalId, eskiAd), (kayit.GetProperty("kanalId").GetInt32(), kayit.GetProperty("kanal").GetString()));
        (await c.PutAsJsonAsync($"/api/islemler/{kayit.GetProperty("id").GetInt32()}", new IslemYazDto(Bugun, "Kira", 20m, eskiAd, GiderTipi.Cari), cancellationToken: ct)).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Bugun, eskiAd, 50m), cancellationToken: ct)).EnsureSuccessStatusCode();

        var yok = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Bugun, "Kira", 10m, "YOK\u0001KANAL", GiderTipi.Cari), cancellationToken: ct);
        Assert.Equal(HttpStatusCode.BadRequest, yok.StatusCode);
        Assert.Equal("Kayıtlı bir kanal seçin.", AlanHatasi(await yok.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct), "kanal"));
        var yeniAd = await c.PutAsJsonAsync($"/api/kanallar/{kanalId}", new KanalYazDto("YENİ\u0002KANAL", Sira: 9), cancellationToken: ct);
        Assert.Equal(HttpStatusCode.BadRequest, yeniAd.StatusCode);
        Assert.Contains("kontrol karakteri", AlanHatasi(await yeniAd.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct), "ad"));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.Equal(eskiAd, db.Kanallar.Single(k => k.Id == kanalId).Ad);
            Assert.Equal(20m, db.Islemler.Single(i => i.KanalId == kanalId).TutarTl);
            Assert.Equal(50m, db.Gelenler.Single(g => g.KanalId == kanalId).TutarTl);
        }
    }

    [Fact]
    public async Task Aylik_gider_serbest_metinleri_ayni_kontrol_karakteri_kuralina_baglidir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        var ay = new DateOnly(Bugun.Year, Bugun.Month, 1);
        const string ileti = "karakterde görünmeyen bir kontrol karakteri ya da geçersiz bir karakter var. Metni yeniden yazın.";
        var ad = await c.PostAsJsonAsync("/api/aylik-giderler/sablonlar", new AylikGiderSablonYaz(Guid.NewGuid(), 0, "Kira\u0007", "Kira", 100m, 1, "Genel", [], ay), cancellationToken: ct);
        Assert.Equal(HttpStatusCode.BadRequest, ad.StatusCode);
        Assert.Equal($"Gider adı: 5. {ileti}", await Hata(ad));
        var sablon = await AylikGiderTests.Post<AylikGiderSablonDto>(c, "/api/aylik-giderler/sablonlar", new AylikGiderSablonYaz(Guid.NewGuid(), 0, "Kira", "Kira", 100m, 1, "Genel", [], ay));

        var not = await c.PostAsJsonAsync($"/api/aylik-giderler/{sablon.Id}/ode",
            new AylikGiderOdemeYaz(Guid.NewGuid(), sablon.Surum, ay.Year, ay.Month, Bugun, "Dekont\u0001"), cancellationToken: ct);
        Assert.Equal(HttpStatusCode.BadRequest, not.StatusCode);
        Assert.Equal($"Not: 7. {ileti}", await Hata(not));
        var odeme = await AylikGiderTests.Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{sablon.Id}/ode", new AylikGiderOdemeYaz(Guid.NewGuid(), sablon.Surum, ay.Year, ay.Month, Bugun, "Dekont\tno 5"));

        var iptal = await c.PostAsJsonAsync($"/api/aylik-giderler/odemeler/{odeme.OdemeId}/iptal", new AylikGiderIptalYaz(Guid.NewGuid(), "Hatalı\u001F ödeme"), cancellationToken: ct);
        Assert.Equal(HttpStatusCode.BadRequest, iptal.StatusCode);
        Assert.Equal($"İptal gerekçesi: 7. {ileti}", await Hata(iptal));
        await AylikGiderTests.Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/odemeler/{odeme.OdemeId}/iptal", new AylikGiderIptalYaz(Guid.NewGuid(), "Hatalı ödeme"));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(["Kira"], db.AylikGiderRevizyonlar.Select(r => r.Ad).ToList());
        Assert.Equal("Hatalı ödeme", db.AylikGiderOdemeler.Single().IptalAciklamasi);
    }

    [Fact]
    public void Serbest_metin_iletisi_Metin_kuraliyla_aynidir()
    {
        Assert.Null(GirdiDogrulama.GecersizKarakterIletisi(null));
        Assert.Null(GirdiDogrulama.GecersizKarakterIletisi("Sekme\tve satır\r\nsonu 😀"));
        var v = new GirdiDogrulama();
        v.Metin("😀 a\u0001", "not", 2000, zorunlu: false);
        var hata = Assert.IsType<HttpValidationProblemDetails>(Assert.IsAssignableFrom<IValueHttpResult>(v.Sonuc()).Value);
        Assert.Equal(Assert.Single(hata.Errors["not"]), GirdiDogrulama.GecersizKarakterIletisi("😀 a\u0001"));
    }

    [Fact]
    public void Ekstre_okuyucu_PDF_aciklamasini_ve_kaynak_satiri_temizler()
    {
        var sonuc = EkstreMetinOkuyucu.Oku(PdfMetni("MARKET\u0001ALIŞVERİŞİ\u0007"), "Banka", "Akbank");
        var satir = Assert.Single(sonuc.Satirlar);
        Assert.StartsWith("MARKET ALIŞVERİŞİ", satir.Aciklama);
        Assert.Equal(-1, GirdiDogrulama.GecersizKarakterKonumu(satir.Aciklama));
        Assert.Equal(-1, GirdiDogrulama.GecersizKarakterKonumu(satir.KaynakSatir));
        Assert.Equal(10m, satir.Tutar);
        Assert.Equal("Gider", satir.OnerilenIslem);
    }

    [Fact]
    public async Task Ekstre_yolunda_kontrol_karakterli_satir_temizlenerek_kaydedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = new PdfFabrikasi(PdfMetni("MARKET\u0001ALIŞVERİŞİ\u0007")) { Saat = new SabitSaat(Bugun) };
        using var c = await f.EditorClientAsync();
        EkstreBelgeDto belge;
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent("Banka"), "kaynak");
            form.Add(new StringContent("Akbank"), "banka");
            form.Add(new StringContent("Ana hesap"), "hesapAdi");
            form.Add(new ByteArrayContent("%PDF-1.7 ekstre"u8.ToArray()), "dosya", "ekstre.pdf");
            using var r = await c.PostAsync("/api/ekstre-aktar/yukle", form, ct);
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync(ct));
            belge = (await r.Content.ReadFromJsonAsync<EkstreBelgeDto>(cancellationToken: ct))!;
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
            var eski = new EkstreBelgeEntity
            {
                Kaynak = "Banka",
                Banka = "QNB",
                HesapAdi = "Eski hesap",
                DosyaAdi = "eski.pdf",
                DosyaOzeti = Guid.NewGuid().ToString(),
                Yuklendi = f.Saat!.GetUtcNow().ToUnixTimeMilliseconds(),
                SatirlarJson = JsonSerializer.Serialize(new[] { new EkstreOkunanSatir(1, 1, "kaynak\u0001satır", Bugun, "ESKİ\u0001BELGE\u0007", 20m, "Cikis", "Gider", "Hareket", "TRY", []) })
            };
            db.EkstreBelgeler.Add(eski);
            db.SaveChanges();
            eskiId = eski.Id;
        }
        var eskiBelge = (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{eskiId}", cancellationToken: ct))!;
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
        (await c.GetAsync($"/api/disari-aktar?baslangic={Bugun:yyyy-MM-dd}&bitis={Bugun:yyyy-MM-dd}&bicim=xlsx", ct)).EnsureSuccessStatusCode();
        // İptal gerekçesi kullanıcı metnidir: kontrol karakteri reddedilir.
        var kayit = Assert.Single(eskiBelge.Kayitlar);
        var iptal = await c.PostAsJsonAsync($"/api/ekstre-aktar/{eskiId}/kayitlar/{kayit.Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Hatalı\u0001 satır"), cancellationToken: ct);
        Assert.Equal(HttpStatusCode.BadRequest, iptal.StatusCode);
        Assert.Equal("İptal gerekçesi: 7. karakterde görünmeyen bir kontrol karakteri ya da geçersiz bir karakter var. Metni yeniden yazın.", await Hata(iptal));
    }

    private static string AlanHatasi(JsonElement govde, string alan) => govde.GetProperty("errors").GetProperty(alan)[0].GetString()!;
    private static async Task<string> Hata(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString()!;

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
