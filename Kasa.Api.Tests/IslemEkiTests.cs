using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.PaketFYardimci;

namespace Kasa.Api.Tests;

/// <summary>İşlem ekleri: yükleme doğrulaması (imza, boyut, sayı), indirme başlıkları, yetki, silme/geri alma, gece temizliği.</summary>
public class IslemEkiTests : IClassFixture<PaketFFactory>
{
    private readonly PaketFFactory _f;
    public IslemEkiTests(PaketFFactory f) => _f = f;

    public record EkYanit(int Id, int IslemId, string Ad, string IcerikTipi, long Boyut, DateTime YuklemeZamaniUtc);
    private record GecmisSatir(int Id, string Tur, int? KayitId, string Eylem, string Ozet);

    public static byte[] Jpeg(int boyut = 64) { var b = new byte[boyut]; b[0] = 0xFF; b[1] = 0xD8; b[2] = 0xFF; b[3] = 0xE0; b[^1] = 0xD9; return b; }
    public static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
    public static byte[] Pdf() => "%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF"u8.ToArray();
    public static byte[] Webp() => [.. "RIFF"u8, 0x10, 0, 0, 0, .. "WEBPVP8 "u8, 0, 0, 0, 0];
    public static byte[] Heic() => [0, 0, 0, 0x18, .. "ftypheic"u8, 0, 0, 0, 0, .. "mif1heic"u8];

    public static MultipartFormDataContent Form(byte[] icerik, string ad, string alan = "dosya")
    {
        var f = new MultipartFormDataContent();
        var d = new ByteArrayContent(icerik);
        d.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        f.Add(d, alan, ad);
        return f;
    }

    private async Task<(HttpClient C, IslemYanit I)> HazirlaAsync(string tarih = "2026-09-15")
    {
        var c = await _f.EditorClientAsync();
        return (c, await IslemEkleAsync(c, "Market", 99m, tarih));
    }

    [Theory]
    [InlineData("fiş 1.jpg", "jpg", "image/jpeg")]
    [InlineData("tarama.PDF", "pdf", "application/pdf")]
    [InlineData("foto.png", "png", "image/png")]
    [InlineData("foto.webp", "webp", "image/webp")]
    [InlineData("IMG_0001.HEIC", "heic", "image/heic")]
    [InlineData("kamera.jpeg", "jpg", "image/jpeg")]
    public async Task Izinli_turler_yuklenir_ve_indirilir(string ad, string uzanti, string tip)
    {
        var (c, i) = await HazirlaAsync();
        var icerik = uzanti switch { "pdf" => Pdf(), "png" => Png(), "webp" => Webp(), "heic" => Heic(), _ => Jpeg() };
        var r = await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(icerik, ad));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var ek = (await r.Content.ReadFromJsonAsync<EkYanit>())!;
        Assert.Equal((i.Id, ad, tip, (long)icerik.Length), (ek.IslemId, ek.Ad, ek.IcerikTipi, ek.Boyut));
        Assert.Equal(PaketFFactory.SimdiUtc, ek.YuklemeZamaniUtc);

        // Diskte rastgele adla durur; kullanıcının verdiği ad kullanılmaz.
        var depo = _f.Db(db => db.IslemEkleri.Single(e => e.Id == ek.Id).DepoAdi);
        Assert.Matches($"^[0-9a-f]{{32}}\\.{uzanti}$", depo);
        Assert.Equal(icerik, File.ReadAllBytes(Path.Combine(_f.BelgeKlasoru, depo)));

        var indir = await c.GetAsync($"/api/ekler/{ek.Id}");
        Assert.Equal(HttpStatusCode.OK, indir.StatusCode);
        Assert.Equal(icerik, await indir.Content.ReadAsByteArrayAsync());
        Assert.Equal(tip, indir.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", indir.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(ad, indir.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal("nosniff", indir.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", indir.Headers.CacheControl!.ToString());

        var liste = await c.GetFromJsonAsync<List<EkYanit>>($"/api/islemler/{i.Id}/ekler");
        Assert.Contains(liste!, e => e.Id == ek.Id);
    }

    [Theory]
    [InlineData("metin.jpg", "düz metin", "Yalnız JPG, PNG, WEBP, HEIC ya da PDF dosyası eklenebilir.")]
    [InlineData("zararli.exe", "PDF", "Yalnız JPG, PNG, WEBP, HEIC ya da PDF dosyası eklenebilir.")]
    [InlineData("sahte.png", "JPEG", "Dosyanın içeriği uzantısıyla uyuşmuyor.")]
    [InlineData("sayfa.html", "<html>", "Yalnız JPG, PNG, WEBP, HEIC ya da PDF dosyası eklenebilir.")]
    [InlineData("belge.svg", "<svg>", "Yalnız JPG, PNG, WEBP, HEIC ya da PDF dosyası eklenebilir.")]
    public async Task Imza_ve_uzanti_dogrulanir(string ad, string icerik, string mesaj)
    {
        var (c, i) = await HazirlaAsync();
        var baytlar = icerik switch { "PDF" => Pdf(), "JPEG" => Jpeg(), _ => System.Text.Encoding.UTF8.GetBytes(icerik + new string(' ', 40)) };
        var r = await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(baytlar, ad));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(mesaj, await HataAsync(r));
        Assert.Equal(0, _f.Db(db => db.IslemEkleri.Count(e => e.IslemId == i.Id)));
    }

    [Fact]
    public async Task Uzantisiz_ad_icerikten_uzanti_alir_yol_parcalari_atilir()
    {
        var (c, i) = await HazirlaAsync();
        var r1 = await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Pdf(), "tarama"));
        Assert.Equal("tarama.pdf", (await r1.Content.ReadFromJsonAsync<EkYanit>())!.Ad);
        var r2 = await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Pdf(), "../../etc/fatura\u0001.pdf"));
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        Assert.Equal("fatura.pdf", (await r2.Content.ReadFromJsonAsync<EkYanit>())!.Ad);
    }

    [Fact]
    public async Task Bos_buyuk_coklu_ve_hatali_istekler_reddedilir()
    {
        var (c, i) = await HazirlaAsync();
        var bos = await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form([], "bos.pdf"));
        Assert.Equal("Dosya boş.", await HataAsync(bos));

        var buyuk = await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Jpeg((int)BelgeDeposu.EnFazlaBoyut + 1), "buyuk.jpg"));
        Assert.Equal(HttpStatusCode.BadRequest, buyuk.StatusCode);
        Assert.Equal("Dosya en fazla 10 MB olabilir.", await HataAsync(buyuk));

        var tamSinir = await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Jpeg((int)BelgeDeposu.EnFazlaBoyut), "sinir.jpg"));
        Assert.Equal(HttpStatusCode.Created, tamSinir.StatusCode);

        var iki = Form(Pdf(), "a.pdf");
        iki.Add(new ByteArrayContent(Pdf()), "dosya2", "b.pdf");
        Assert.Equal("Tek istekte tek dosya gönderin.", await HataAsync(await c.PostAsync($"/api/islemler/{i.Id}/ekler", iki)));

        var json = await c.PostAsJsonAsync($"/api/islemler/{i.Id}/ekler", new { dosya = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, json.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/islemler/987654/ekler", Form(Pdf(), "a.pdf"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/islemler/987654/ekler")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/ekler/987654")).StatusCode);
    }

    [Fact]
    public async Task Islem_basina_en_fazla_10_ek()
    {
        var (c, i) = await HazirlaAsync();
        for (var n = 0; n < BelgeDeposu.IslemBasinaEnFazla; n++)
            Assert.Equal(HttpStatusCode.Created, (await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Pdf(), $"f{n}.pdf"))).StatusCode);
        var dosyaSayisi = Directory.GetFiles(_f.BelgeKlasoru).Length;
        var r = await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Pdf(), "fazla.pdf"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("Bir işleme en fazla 10 ek eklenebilir.", await HataAsync(r));
        Assert.Equal(dosyaSayisi, Directory.GetFiles(_f.BelgeKlasoru).Length);   // reddedilen dosya diskte kalmaz
    }

    [Fact]
    public async Task Izleyici_listeler_ve_indirir_yukleyemez_silemez_kimliksiz_401()
    {
        var (c, i) = await HazirlaAsync();
        var ek = (await (await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Pdf(), "a.pdf"))).Content.ReadFromJsonAsync<EkYanit>())!;
        var izleyici = await _f.IzleyiciAsync();
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync($"/api/islemler/{i.Id}/ekler")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync($"/api/ekler/{ek.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Pdf(), "b.pdf"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.DeleteAsync($"/api/ekler/{ek.Id}")).StatusCode);
        var anonim = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.GetAsync($"/api/ekler/{ek.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.GetAsync($"/api/islemler/{i.Id}/ekler")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Pdf(), "c.pdf"))).StatusCode);
    }

    [Fact]
    public async Task Ek_silinince_kaydi_ve_dosyasi_gider_gecmise_yazilir()
    {
        var (c, i) = await HazirlaAsync();
        var ek = (await (await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Jpeg(), "fis.jpg"))).Content.ReadFromJsonAsync<EkYanit>())!;
        var depo = _f.Db(db => db.IslemEkleri.Single(e => e.Id == ek.Id).DepoAdi);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/ekler/{ek.Id}")).StatusCode);
        Assert.False(File.Exists(Path.Combine(_f.BelgeKlasoru, depo)));
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/ekler/{ek.Id}")).StatusCode);
        var gecmis = await c.GetFromJsonAsync<List<GecmisSatir>>("/api/gecmis?limit=20&tur=" + Uri.EscapeDataString("İşlem eki"));
        Assert.Contains(gecmis!, g => g.Eylem == "Eklendi" && g.Ozet.Contains("fis.jpg"));
        Assert.Contains(gecmis!, g => g.Eylem == "Silindi" && g.Ozet.Contains("fis.jpg"));
    }

    [Fact]
    public async Task Islem_silinince_ekler_kalir_geri_alininca_yeni_isleme_baglanir()
    {
        var (c, i) = await HazirlaAsync("2026-09-16");
        var ek1 = (await (await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Jpeg(), "1.jpg"))).Content.ReadFromJsonAsync<EkYanit>())!;
        var ek2 = (await (await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Pdf(), "2.pdf"))).Content.ReadFromJsonAsync<EkYanit>())!;
        (await c.DeleteAsync($"/api/islemler/{i.Id}")).EnsureSuccessStatusCode();

        // Silinen işlemin ekleri durur (indirilebilir) ama işlemden ayrılır: eski Id SilinenIslemId'de bekler.
        Assert.Equal(0, _f.Db(db => db.IslemEkleri.Count(e => e.IslemId == i.Id)));
        var bekleyen = _f.Db(db => db.IslemEkleri.Where(e => e.SilinenIslemId == i.Id).ToList());
        Assert.Equal(2, bekleyen.Count);
        Assert.All(bekleyen, e => Assert.Equal((0, (DateTime?)PaketFFactory.SimdiUtc), (e.IslemId, e.SilinmeZamaniUtc)));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/ekler/{ek1.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/islemler/{i.Id}/ekler")).StatusCode);
        var ayrilma = await c.GetFromJsonAsync<List<GecmisSatir>>("/api/gecmis?limit=20&tur=" + Uri.EscapeDataString("İşlem eki"));
        Assert.Contains(ayrilma!, g => g.KayitId == i.Id && g.Ozet.StartsWith("2 ek, silinen işlemle birlikte 30 gün saklanacak"));

        var silme = (await c.GetFromJsonAsync<List<GecmisSatir>>("/api/gecmis?limit=50"))!
            .First(g => g.Tur == "İşlem" && g.KayitId == i.Id && g.Eylem == "Silindi");
        var geri = await c.PostAsync($"/api/gecmis/{silme.Id}/geri-al", null);
        geri.EnsureSuccessStatusCode();
        var yeni = (await geri.Content.ReadFromJsonAsync<IslemYanit>())!;

        var ekler = await c.GetFromJsonAsync<List<EkYanit>>($"/api/islemler/{yeni.Id}/ekler");
        Assert.Equal([ek1.Id, ek2.Id], ekler!.Select(e => e.Id));
        Assert.All(ekler!, e => Assert.Equal(yeni.Id, e.IslemId));
        Assert.All(_f.Db(db => db.IslemEkleri.Where(e => e.IslemId == yeni.Id).ToList()),
            e => Assert.Equal((null, null), (e.SilinenIslemId, e.SilinmeZamaniUtc)));
        var gecmis = await c.GetFromJsonAsync<List<GecmisSatir>>("/api/gecmis?limit=20&tur=" + Uri.EscapeDataString("İşlem eki"));
        Assert.Contains(gecmis!, g => g.Ozet == "2 ek geri alınan işleme bağlandı" && g.KayitId == yeni.Id);
    }

    [Fact]
    public async Task Gece_temizligi_yalniz_suresi_dolan_yetimleri_ve_eski_kayitsiz_dosyalari_siler()
    {
        var (c, i) = await HazirlaAsync("2026-09-17");
        var kalan = await IslemEkleAsync(c, "Market", 1m, "2026-09-17");
        var ekSilinen = (await (await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Pdf(), "silinen.pdf"))).Content.ReadFromJsonAsync<EkYanit>())!;
        var ekKalan = (await (await c.PostAsync($"/api/islemler/{kalan.Id}/ekler", Form(Pdf(), "kalan.pdf"))).Content.ReadFromJsonAsync<EkYanit>())!;
        (await c.DeleteAsync($"/api/islemler/{i.Id}")).EnsureSuccessStatusCode();   // silinme zamanı = SimdiUtc
        var depoSilinen = _f.Db(db => db.IslemEkleri.Single(e => e.Id == ekSilinen.Id).DepoAdi);

        // Kayıtsız dosyalar: eski (silinir), yeni (kalır); yarım kalan geçici dosya.
        var eskiKayitsiz = Path.Combine(_f.BelgeKlasoru, new string('a', 32) + ".pdf");
        var yeniKayitsiz = Path.Combine(_f.BelgeKlasoru, new string('b', 32) + ".pdf");
        var gecici = Path.Combine(_f.BelgeKlasoru, "." + new string('c', 32) + ".pdf.tmp");
        var yabanci = Path.Combine(_f.BelgeKlasoru, "BENIOKU.txt");
        foreach (var y in new[] { eskiKayitsiz, yeniKayitsiz, gecici, yabanci }) File.WriteAllBytes(y, Pdf());
        // Temizlik aşağıda SimdiUtc + 30 gün ile çalışır: yaşlar ona göre.
        File.SetLastWriteTimeUtc(eskiKayitsiz, PaketFFactory.SimdiUtc.AddDays(-1));   // 31 günlük
        File.SetLastWriteTimeUtc(yeniKayitsiz, PaketFFactory.SimdiUtc.AddDays(25));   // 5 günlük
        File.SetLastWriteTimeUtc(gecici, PaketFFactory.SimdiUtc.AddDays(29));         // 1 günlük
        File.SetLastWriteTimeUtc(yabanci, PaketFFactory.SimdiUtc.AddDays(-400));

        BelgeTemizleyici.Sonuc Calistir(DateTime simdi)
        {
            using var scope = _f.Services.CreateScope();
            return BelgeTemizleyici.Temizle(scope.ServiceProvider.GetRequiredService<KasaDbContext>(),
                scope.ServiceProvider.GetRequiredService<BelgeDeposu>(), simdi);
        }

        // Geri alma süresi içinde: yetim ek korunur.
        var s1 = Calistir(PaketFFactory.SimdiUtc.AddDays(30));
        Assert.Equal(0, s1.YetimKayit);
        Assert.True(_f.Db(db => db.IslemEkleri.Any(e => e.Id == ekSilinen.Id)));
        Assert.False(File.Exists(eskiKayitsiz));   // 30 günden eski kayıtsız dosya
        Assert.False(File.Exists(gecici));
        Assert.True(File.Exists(yabanci));         // bizim adlandırmamıza uymayan dosyaya dokunulmaz

        // 31 günü geçince: yetim ek kaydı ve dosyası silinir, diğer işlemin eki kalır.
        var s2 = Calistir(PaketFFactory.SimdiUtc.AddDays(31).AddHours(1));
        Assert.Equal(1, s2.YetimKayit);
        Assert.False(_f.Db(db => db.IslemEkleri.Any(e => e.Id == ekSilinen.Id)));
        Assert.False(File.Exists(Path.Combine(_f.BelgeKlasoru, depoSilinen)));
        Assert.True(_f.Db(db => db.IslemEkleri.Any(e => e.Id == ekKalan.Id)));
        Assert.True(_f.Db(db => db.Degisiklikler.Any(d => d.Tur == GecmisTurleri.IslemEki && d.Rol == "sistem")));
        Assert.True(File.Exists(yeniKayitsiz));
        File.Delete(yeniKayitsiz);
        File.Delete(yabanci);
    }

    /// <summary>
    /// Bulgu: silinen işlemin Id'si yeniden verilirse (ör. tablo yeniden kurulurken sayaç düşerse) yeni işlem
    /// eski işlemin fiş/faturalarını devralmamalı; geri alma yalnız silinen işlemin eklerini taşımalı.
    /// </summary>
    [Fact]
    public async Task Silinen_islemin_Id_si_yeniden_verilse_de_ekleri_yeni_isleme_gecmez_geri_alma_yalniz_kendi_eklerini_tasir()
    {
        var (c, i) = await HazirlaAsync("2026-09-18");
        var eskiEk = (await (await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Pdf(), "eski.pdf"))).Content.ReadFromJsonAsync<EkYanit>())!;
        (await c.DeleteAsync($"/api/islemler/{i.Id}")).EnsureSuccessStatusCode();

        // Id yeniden kullanımı: aynı Id'de yeni bir işlem (doğrudan DB) ve ona yüklenen kendi eki.
        _f.Db(db =>
        {
            db.Islemler.Add(new IslemEntity { Id = i.Id, Tarih = new DateOnly(2026, 9, 19), Cari = "Market", TutarTl = 5m, Kanal = "MEZAT" });
            db.SaveChanges();
        });
        var kendiEk = (await (await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Jpeg(), "yeni.jpg"))).Content.ReadFromJsonAsync<EkYanit>())!;

        // Yeni işlem yalnız kendi ekini görür (işlem formu, fatura takibi, muhasebeci listesi aynı sayımı kullanır).
        var liste = await c.GetFromJsonAsync<List<EkYanit>>($"/api/islemler/{i.Id}/ekler");
        Assert.Equal([kendiEk.Id], liste!.Select(e => e.Id));
        Assert.Equal(1, _f.Db(db => Kasa.Api.Endpoints.FaturaTakibi.EkSayilari(db, [i.Id]).GetValueOrDefault(i.Id)));

        // Eski silme geri alınır: yalnız silinen işlemin eki yeni (geri gelen) işleme gider; aynı Id'deki işlemin eki yerinde kalır.
        var silme = (await c.GetFromJsonAsync<List<GecmisSatir>>("/api/gecmis?limit=50"))!
            .First(g => g.Tur == "İşlem" && g.KayitId == i.Id && g.Eylem == "Silindi");
        var geri = await c.PostAsync($"/api/gecmis/{silme.Id}/geri-al", null);
        geri.EnsureSuccessStatusCode();
        var gelen = (await geri.Content.ReadFromJsonAsync<IslemYanit>())!;
        Assert.NotEqual(i.Id, gelen.Id);
        Assert.Equal([eskiEk.Id], (await c.GetFromJsonAsync<List<EkYanit>>($"/api/islemler/{gelen.Id}/ekler"))!.Select(e => e.Id));
        Assert.Equal([kendiEk.Id], (await c.GetFromJsonAsync<List<EkYanit>>($"/api/islemler/{i.Id}/ekler"))!.Select(e => e.Id));
    }

    [Fact]
    public async Task Gece_temizligi_Id_si_yeniden_verilmis_silinen_islemin_ekini_de_suresi_dolunca_siler()
    {
        var (c, i) = await HazirlaAsync("2026-09-19");
        var ek = (await (await c.PostAsync($"/api/islemler/{i.Id}/ekler", Form(Pdf(), "yetim.pdf"))).Content.ReadFromJsonAsync<EkYanit>())!;
        (await c.DeleteAsync($"/api/islemler/{i.Id}")).EnsureSuccessStatusCode();
        _f.Db(db =>
        {
            db.Islemler.Add(new IslemEntity { Id = i.Id, Tarih = new DateOnly(2026, 9, 19), Cari = "Market", TutarTl = 5m, Kanal = "MEZAT" });
            db.SaveChanges();
        });

        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var depo = scope.ServiceProvider.GetRequiredService<BelgeDeposu>();
        Assert.Equal(0, BelgeTemizleyici.Temizle(db, depo, PaketFFactory.SimdiUtc.AddDays(30)).YetimKayit);
        Assert.True(db.IslemEkleri.Any(e => e.Id == ek.Id));
        Assert.Equal(1, BelgeTemizleyici.Temizle(db, depo, PaketFFactory.SimdiUtc.AddDays(31).AddHours(1)).YetimKayit);
        Assert.False(db.IslemEkleri.Any(e => e.Id == ek.Id));
    }

    [Fact]
    public async Task Eski_yoldan_yetim_kalan_ek_Id_de_islem_yoksa_geri_alinana_baglanir_varsa_dokunulmaz()
    {
        var (c, i) = await HazirlaAsync("2026-09-20");
        (await c.DeleteAsync($"/api/islemler/{i.Id}")).EnsureSuccessStatusCode();
        // İşlemi API dışından silinmiş gibi: ek hâlâ eski Id'yi gösteriyor (SilinenIslemId yok).
        var yetimId = _f.Db(db =>
        {
            var e = new IslemEkiEntity { IslemId = i.Id, OrijinalAd = "eski-yol.pdf", DepoAdi = new string('e', 32) + ".pdf", IcerikTipi = "application/pdf", Boyut = 3 };
            db.IslemEkleri.Add(e);
            db.SaveChanges();
            return e.Id;
        });
        var silme = (await c.GetFromJsonAsync<List<GecmisSatir>>("/api/gecmis?limit=50"))!
            .First(g => g.Tur == "İşlem" && g.KayitId == i.Id && g.Eylem == "Silindi");
        var gelen = (await (await c.PostAsync($"/api/gecmis/{silme.Id}/geri-al", null)).Content.ReadFromJsonAsync<IslemYanit>())!;
        Assert.Equal(gelen.Id, _f.Db(db => db.IslemEkleri.Single(e => e.Id == yetimId).IslemId));

        // Aynı durum ama eski Id'de bugün bir işlem var: ek o işleme ait sayılır, taşınmaz.
        var (_, j) = await HazirlaAsync("2026-09-21");
        (await c.DeleteAsync($"/api/islemler/{j.Id}")).EnsureSuccessStatusCode();
        _f.Db(db =>
        {
            db.Islemler.Add(new IslemEntity { Id = j.Id, Tarih = new DateOnly(2026, 9, 21), Cari = "Market", TutarTl = 5m, Kanal = "MEZAT" });
            db.IslemEkleri.Add(new IslemEkiEntity { IslemId = j.Id, OrijinalAd = "sahibi-var.pdf", DepoAdi = new string('f', 32) + ".pdf", IcerikTipi = "application/pdf", Boyut = 3 });
            db.SaveChanges();
        });
        var silme2 = (await c.GetFromJsonAsync<List<GecmisSatir>>("/api/gecmis?limit=50"))!
            .First(g => g.Tur == "İşlem" && g.KayitId == j.Id && g.Eylem == "Silindi");
        (await c.PostAsync($"/api/gecmis/{silme2.Id}/geri-al", null)).EnsureSuccessStatusCode();
        Assert.Equal(1, _f.Db(db => db.IslemEkleri.Count(e => e.IslemId == j.Id && e.OrijinalAd == "sahibi-var.pdf")));
    }

    /// <summary>
    /// Bulgu: işlem listesinde belge/ek görünmüyordu (ortaklar fişi yalnız fatura takibinden açabiliyordu).
    /// GET /islemler artık her işlemin ek sayısını verir; eki olmayan işlemde alan hiç yazılmaz (eski istemci etkilenmez).
    /// </summary>
    [Fact]
    public async Task Islem_listesi_ek_sayisini_verir_izleyici_de_gorur_eksiz_islemde_alan_yazilmaz()
    {
        var c = await _f.EditorClientAsync();
        var ekli = await IslemEkleAsync(c, "Market", 40m, "2026-09-22", belgeTuru: "Fis", belgeNo: "F-7");
        var eksiz = await IslemEkleAsync(c, "Market", 41m, "2026-09-22");
        foreach (var ad in new[] { "a.pdf", "b.jpg" })
            (await c.PostAsync($"/api/islemler/{ekli.Id}/ekler", Form(ad.EndsWith(".pdf") ? Pdf() : Jpeg(), ad))).EnsureSuccessStatusCode();

        var izleyici = await _f.IzleyiciAsync();
        foreach (var yol in new[] { "/api/islemler?baslangic=2026-09-22&bitis=2026-09-22", "/api/islemler?baslangic=2026-09-22&bitis=2026-09-22&limit=5&offset=0" })
        {
            var liste = JsonDocument.Parse(await izleyici.GetStringAsync(yol)).RootElement.EnumerateArray().ToList();
            var a = liste.Single(x => x.GetProperty("id").GetInt32() == ekli.Id);
            var b = liste.Single(x => x.GetProperty("id").GetInt32() == eksiz.Id);
            Assert.Equal(2, a.GetProperty("ekSayisi").GetInt32());
            Assert.Equal("Fis", a.GetProperty("belgeTuru").GetString());
            Assert.False(b.TryGetProperty("ekSayisi", out _));
        }

        // Tek işlem yanıtları (kaydet/güncelle) ek sayısı taşımaz; sunucu gövdedeki ekSayisi'ni saklamaz.
        var r = await c.PutAsJsonAsync($"/api/islemler/{ekli.Id}", new
        {
            tarih = "2026-09-22", cari = "Market", tutarTl = 40m, kanal = "MEZAT", tip = "Cari", ekSayisi = 99,
        });
        r.EnsureSuccessStatusCode();
        Assert.False(JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.TryGetProperty("ekSayisi", out _));
        var p = await c.PostAsJsonAsync("/api/islemler", new
        {
            tarih = "2026-09-22", cari = "Market", tutarTl = 43m, kanal = "MEZAT", tip = "Cari", ekSayisi = 99,
        });
        p.EnsureSuccessStatusCode();
        Assert.False(JsonDocument.Parse(await p.Content.ReadAsStringAsync()).RootElement.TryGetProperty("ekSayisi", out _));
        Assert.Equal(2, _f.Db(db => db.IslemEkleri.Count(e => e.IslemId == ekli.Id)));
    }
}
