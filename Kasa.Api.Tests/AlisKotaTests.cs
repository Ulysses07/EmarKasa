using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.VekilVeHizSiniriTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Alıcı kotaları ve alış yükleme hız sınırı (host-auth-5, purchase-3): en düşük yetkili rol belge BLOB'larıyla veritabanını
/// ve yedek diskini dolduramaz. Alıcı başına açık taslak sayısı, taslak başına belge sayısı ve toplam boyutu, son 24 saatteki
/// yükleme hacmi ile kalıcı üst sınırlar (onay bekleyen alış sayısı ve onlardaki belge hacmi; taslağı incelemeye göndermek yer
/// açmaz, editörün onayı açar) yapılandırılabilir (Kasa:AliciKota); aşım Türkçe 409 döner. Belge yükleme ve alış oluşturma (kullanıcı, IP)
/// başına ayrı bir pencerede sınırlanır (Kasa:HizSiniri:AlisYuklemeIzni; editör ayrı ve yüksek sınırda); aşım Türkçe 429 döner.
/// </summary>
public class AlisKotaTests
{
    private static readonly DateOnly Bugun = KasaWebFactory.VarsayilanBugun;

    [Fact]
    public async Task Alicinin_acik_taslak_sayisi_sinirlidir_editor_etkilenmez()
    {
        await using var f = AlisTestYardimcisi.KotaFabrikasi(new() { ["Kasa:AliciKota:AcikTaslak"] = "2" });
        using var editor = await f.EditorClientAsync();
        using var alici = await AlisTestYardimcisi.Alici(f, editor, "kota-taslak");
        await AlisTestYardimcisi.Taslak(alici, "Birinci");
        var ikinci = await AlisTestYardimcisi.Taslak(alici, "İkinci");

        using var ucuncu = await alici.PostAsJsonAsync("/api/alis", AlisTestYardimcisi.Govde("Üçüncü"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, ucuncu.StatusCode);
        Assert.Contains("En fazla 2 açık taslak", await AlisTestYardimcisi.Hata(ucuncu));

        // Taslak incelemeye gönderilince açık taslak kotasında yer açılır (onay bekleyen kotası ayrıca sınırlar, aşağıda);
        // editörün kendi taslakları hiç sayılmaz.
        (await alici.PostAsJsonAsync($"/api/alis/{ikinci.Id}/gonder", new AlisDurumYaz(ikinci.Surum), cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        await AlisTestYardimcisi.Taslak(alici, "Üçüncü");
        for (var i = 0; i < 3; i++)
            await AlisTestYardimcisi.Taslak(editor, $"Editör {i}");
    }

    [Fact]
    public async Task Onay_bekleyen_alis_sayisi_gonder_donguyle_asilamaz_yalniz_editor_onayi_yer_acar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = AlisTestYardimcisi.KotaFabrikasi(new() { ["Kasa:AliciKota:AcikTaslak"] = "2", ["Kasa:AliciKota:OnayBekleyen"] = "3" });
        using var editor = await f.EditorClientAsync();
        using var alici = await AlisTestYardimcisi.Alici(f, editor, "kota-bekleyen");

        // 'Oluştur, gönder' döngüsü: açık taslak kotası her gönderimde boşalır, onay bekleyen kotası boşalmaz.
        var gonderilenler = new List<AlisDto>();
        for (var i = 0; i < 3; i++)
            gonderilenler.Add(await AlisTestYardimcisi.Gonder(alici, await AlisTestYardimcisi.Taslak(alici, $"Döngü {i}")));
        using (var dorduncu = await alici.PostAsJsonAsync("/api/alis", AlisTestYardimcisi.Govde("Dördüncü"), cancellationToken: ct))
        {
            Assert.Equal(HttpStatusCode.Conflict, dorduncu.StatusCode);
            Assert.Contains("Onay bekleyen (taslak ya da incelemedeki) en fazla 3 alışınız", await AlisTestYardimcisi.Hata(dorduncu));
        }

        // Editörün iadesi yer açmaz (alış yeniden taslaktır ve hâlâ onay bekler); onay açar.
        var iade = gonderilenler[0];
        (await editor.PostAsJsonAsync($"/api/alis/{iade.Id}/iade", new AlisDurumYaz(iade.Surum, "Belgeyi ekleyin"), cancellationToken: ct)).EnsureSuccessStatusCode();
        using (var iadeSonrasi = await alici.PostAsJsonAsync("/api/alis", AlisTestYardimcisi.Govde("İade sonrası"), cancellationToken: ct))
            Assert.Equal(HttpStatusCode.Conflict, iadeSonrasi.StatusCode);
        (await editor.PostAsJsonAsync($"/api/alis/{gonderilenler[1].Id}/onayla", new AlisDurumYaz(gonderilenler[1].Surum), cancellationToken: ct)).EnsureSuccessStatusCode();
        await AlisTestYardimcisi.Taslak(alici, "Onay sonrası");
        using (var yine = await alici.PostAsJsonAsync("/api/alis", AlisTestYardimcisi.Govde("Yine"), cancellationToken: ct))
            Assert.Equal(HttpStatusCode.Conflict, yine.StatusCode);

        // Kota alıcı başınadır ve IP'den bağımsızdır: aynı alıcının başka bir oturumu da aşamaz; editör etkilenmez.
        using var ikinciOturum = f.CreateClient();
        (await ikinciOturum.PostAsJsonAsync("/api/auth/login", new { kullanici = "kota-bekleyen", sifre = "alici-sifre-1" }, cancellationToken: ct)).EnsureSuccessStatusCode();
        using (var baskaOturum = await ikinciOturum.PostAsJsonAsync("/api/alis", AlisTestYardimcisi.Govde("Başka oturum"), cancellationToken: ct))
            Assert.Equal(HttpStatusCode.Conflict, baskaOturum.StatusCode);
        for (var i = 0; i < 4; i++)
            await AlisTestYardimcisi.Taslak(editor, $"Editör {i}");
    }

    [Fact]
    public async Task Onay_bekleyen_alislardaki_toplam_belge_boyutu_sinirlidir_editor_onayi_yer_acar()
    {
        await using var f = AlisTestYardimcisi.KotaFabrikasi(new() { ["Kasa:AliciKota:TaslakBelgeMb"] = "1", ["Kasa:AliciKota:OnayBekleyenBelgeMb"] = "2" });
        using var editor = await f.EditorClientAsync();
        using var alici = await AlisTestYardimcisi.Alici(f, editor, "kota-bekleyen-belge");

        // Her taslak kendi 1 MB sınırında kalır ve incelemeye gönderilir: belge hacmi taslak değiştirerek büyütülemez.
        var ilk = await AlisTestYardimcisi.Taslak(alici, "Birinci");
        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(alici, ilk.Id, 900 * 1024));
        ilk = await AlisTestYardimcisi.Gonder(alici, ilk);
        var ikinci = await AlisTestYardimcisi.Taslak(alici, "İkinci");
        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(alici, ikinci.Id, 900 * 1024));
        await AlisTestYardimcisi.Gonder(alici, ikinci);
        var ucuncu = await AlisTestYardimcisi.Taslak(alici, "Üçüncü");
        using (var fazla = await AlisTestYardimcisi.YukleYanit(alici, ucuncu.Id, 300 * 1024))
        {
            Assert.Equal(HttpStatusCode.Conflict, fazla.StatusCode);
            Assert.Contains("Onay bekleyen alışlarınızdaki belgelerin toplam boyutu en fazla 2 MB", await AlisTestYardimcisi.Hata(fazla));
        }

        // Editör onaylayınca onaylı alışın belgeleri bekleyen hacimden çıkar; editörün kendi yüklemesi kotaya takılmaz.
        (await editor.PostAsJsonAsync($"/api/alis/{ilk.Id}/onayla", new AlisDurumYaz(ilk.Surum), cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(alici, ucuncu.Id, 300 * 1024));
        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(editor, ucuncu.Id, 900 * 1024));
    }

    [Fact]
    public async Task Alicinin_taslak_basina_belge_sayisi_ve_boyutu_sinirlidir_editor_etkilenmez()
    {
        await using var f = AlisTestYardimcisi.KotaFabrikasi(new() { ["Kasa:AliciKota:TaslakBelgeSayisi"] = "2", ["Kasa:AliciKota:TaslakBelgeMb"] = "1" });
        using var editor = await f.EditorClientAsync();
        using var alici = await AlisTestYardimcisi.Alici(f, editor, "kota-belge");
        var taslak = await AlisTestYardimcisi.Taslak(alici, "Belgeli");

        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(alici, taslak.Id, 600 * 1024));
        using (var fazla = await AlisTestYardimcisi.YukleYanit(alici, taslak.Id, 500 * 1024))
        {
            Assert.Equal(HttpStatusCode.Conflict, fazla.StatusCode);
            Assert.Contains("toplam boyutu en fazla 1 MB", await AlisTestYardimcisi.Hata(fazla));
        }
        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(alici, taslak.Id, 100 * 1024));
        using (var sayi = await AlisTestYardimcisi.YukleYanit(alici, taslak.Id, 1024))
        {
            Assert.Equal(HttpStatusCode.Conflict, sayi.StatusCode);
            Assert.Contains("en fazla 2 belge", await AlisTestYardimcisi.Hata(sayi));
        }
        // Editör aynı taslağa alıcı kotasından bağımsız ekler (genel sınır: alış başına 30 belge).
        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(editor, taslak.Id, 900 * 1024));
        Assert.Equal(3, (await editor.GetFromJsonAsync<BelgeDto[]>($"/api/alis/{taslak.Id}/belgeler", cancellationToken: TestContext.Current.CancellationToken))!.Length);
    }

    [Fact]
    public async Task Alicinin_son_24_saatteki_yukleme_hacmi_sinirlidir_ertesi_gun_acilir()
    {
        var saat = new SabitSaat(Bugun);
        await using var f = AlisTestYardimcisi.KotaFabrikasi(new() { ["Kasa:AliciKota:GunlukYuklemeMb"] = "1" }, saat);
        using var editor = await f.EditorClientAsync();
        using var alici = await AlisTestYardimcisi.Alici(f, editor, "kota-gunluk");
        var ilk = await AlisTestYardimcisi.Taslak(alici, "Sabah");
        var ikinci = await AlisTestYardimcisi.Taslak(alici, "Öğle");

        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(alici, ilk.Id, 700 * 1024));
        using (var fazla = await AlisTestYardimcisi.YukleYanit(alici, ikinci.Id, 400 * 1024))
        {
            Assert.Equal(HttpStatusCode.Conflict, fazla.StatusCode);
            Assert.Contains("Son 24 saatte en fazla 1 MB", await AlisTestYardimcisi.Hata(fazla));
        }
        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(editor, ikinci.Id, 900 * 1024));

        saat.Ayarla(Bugun.AddDays(1));
        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(alici, ikinci.Id, 400 * 1024));
    }

    /// <summary>24 saat penceresi sorguda (SQLite julianday: saat dilimli metni ana çevirir) süzülür: alıcının bütün belge
    /// geçmişinin satırları belleğe alınmaz. Farklı saat dilimiyle yazılmış kayıtlar metinle değil anla karşılaştırılır.</summary>
    [Fact]
    public async Task Gunluk_yukleme_hacmi_sorguda_suzulur_saat_dilimli_kayitlar_anla_karsilastirilir()
    {
        var f = AlisTestYardimcisi.KotaFabrikasi(new() { ["Kasa:AliciKota:GunlukYuklemeMb"] = "1" });
        await using var _ = f;
        using var editor = await f.EditorClientAsync();
        using var alici = await AlisTestYardimcisi.Alici(f, editor, "kota-dilim");
        var eski = await AlisTestYardimcisi.Taslak(alici, "Eski belgeler");
        var yeni = await AlisTestYardimcisi.Taslak(alici, "Yeni belge");
        var simdi = f.Saat!.GetUtcNow();
        void Ekle(DateTimeOffset an, int boyut)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.Belgeler.Add(new BelgeEntity { AlisId = eski.Id, DosyaAdi = "eski.pdf", IcerikTuru = "application/pdf", Boyut = boyut, Yuklendi = an, IcerikOzeti = TestBelgeDeposu.Ozet([1]) });
            db.SaveChanges();
        }

        // 25 saat önce, +03:00 ile yazılmış: metni pencerenin içinde görünür, anı dışındadır; sayılmaz.
        Ekle(simdi.AddHours(-25).ToOffset(TimeSpan.FromHours(3)), 600 * 1024);
        f.Sayac.Sifirla();
        f.Sayac.Etkin = true;
        try
        { Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(alici, yeni.Id, 500 * 1024)); }
        finally { f.Sayac.Etkin = false; }
        var okuyanlar = f.Sayac.Komutlar.Where(k => k.Contains("\"Yuklendi\"") && !k.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(okuyanlar);
        Assert.All(okuyanlar, k => Assert.Contains("julianday(", k));

        // 23 saat önce, -05:00 ile yazılmış: metni pencerenin dışında görünür, anı içindedir; sayılır (500 + 400 + 200 KB > 1 MB).
        Ekle(simdi.AddHours(-23).ToOffset(TimeSpan.FromHours(-5)), 400 * 1024);
        using (var fazla = await AlisTestYardimcisi.YukleYanit(alici, yeni.Id, 200 * 1024))
        {
            Assert.Equal(HttpStatusCode.Conflict, fazla.StatusCode);
            Assert.Contains("Son 24 saatte en fazla 1 MB", await AlisTestYardimcisi.Hata(fazla));
        }
        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(alici, yeni.Id, 100 * 1024));
    }

    [Fact]
    public async Task Idempotent_tekrar_kota_doluyken_de_ilk_sonucu_doner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = AlisTestYardimcisi.KotaFabrikasi(new() { ["Kasa:AliciKota:AcikTaslak"] = "1" });
        using var editor = await f.EditorClientAsync();
        using var alici = await AlisTestYardimcisi.Alici(f, editor, "kota-tekrar");
        var istek = new { surum = 0, tarih = Bugun, tedarikci = "Tekrar", not = (string?)null, kalemler = Array.Empty<object>(), istekId = Guid.NewGuid() };
        using var ilk = await alici.PostAsJsonAsync("/api/alis", istek, cancellationToken: ct);
        Assert.Equal(HttpStatusCode.Created, ilk.StatusCode);
        using var tekrar = await alici.PostAsJsonAsync("/api/alis", istek, cancellationToken: ct);
        Assert.Equal(HttpStatusCode.OK, tekrar.StatusCode);
        Assert.Equal((await ilk.Content.ReadFromJsonAsync<AlisDto>(cancellationToken: ct))!.Id, (await tekrar.Content.ReadFromJsonAsync<AlisDto>(cancellationToken: ct))!.Id);
        using var yeni = await alici.PostAsJsonAsync("/api/alis", istek with { istekId = Guid.NewGuid() }, cancellationToken: ct);
        Assert.Equal(HttpStatusCode.Conflict, yeni.StatusCode);
    }

    [Fact]
    public async Task Belge_yukleme_ve_alis_olusturma_kullanici_ve_ip_basina_hiz_sinirinda_turkce_429_doner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = new VekilFabrikasi(new() { ["Kasa:HizSiniri:AlisYuklemeIzni"] = "3", ["Kasa:HizSiniri:EditorAlisYuklemeIzni"] = "6" });
        using var kurulum = Istemci(f, "198.51.100.1");
        (await Giris(kurulum, "editor", "kasa123")).EnsureSuccessStatusCode();
        (await kurulum.PostAsJsonAsync("/api/alicilar", new AliciYaz("hiz-alici", "Hız Alıcısı", "alici-sifre-1"), cancellationToken: ct)).EnsureSuccessStatusCode();
        (await kurulum.PostAsJsonAsync("/api/alicilar", new AliciYaz("hiz-komsu", "Aynı ağdaki alıcı", "alici-sifre-1"), cancellationToken: ct)).EnsureSuccessStatusCode();
        using var alici = Istemci(f, "203.0.113.5");
        (await Giris(alici, "hiz-alici", "alici-sifre-1")).EnsureSuccessStatusCode();

        var taslak = await AlisTestYardimcisi.Taslak(alici, "Hız");                                   // 1
        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(alici, taslak.Id, 1024)); // 2
        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(alici, taslak.Id, 1024)); // 3
        using (var red = await AlisTestYardimcisi.YukleYanit(alici, taslak.Id, 1024))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, red.StatusCode);
            Assert.True(red.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Retry-After başlığı saniye olarak gelmeli.");
            Assert.StartsWith("Belge yükleme ve alış kaydı sınırına ulaşıldı: 10 dakikada en çok 3", await AlisTestYardimcisi.Hata(red));
        }
        using (var red = await alici.PostAsJsonAsync("/api/alis", AlisTestYardimcisi.Govde("Sınır sonrası"), cancellationToken: ct))
            Assert.Equal(HttpStatusCode.TooManyRequests, red.StatusCode);
        // Okuma ve düzenleme bu kovayı tüketmez.
        (await alici.GetAsync($"/api/alis/{taslak.Id}/belgeler", ct)).EnsureSuccessStatusCode();

        // Aynı ağdaki başka alıcı ve aynı alıcının başka ağdaki oturumu ayrı kovadadır.
        using var komsu = Istemci(f, "203.0.113.5");
        (await Giris(komsu, "hiz-komsu", "alici-sifre-1")).EnsureSuccessStatusCode();
        await AlisTestYardimcisi.Taslak(komsu, "Komşu");
        using var baskaAg = Istemci(f, "192.0.2.77");
        (await Giris(baskaAg, "hiz-alici", "alici-sifre-1")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(baskaAg, taslak.Id, 1024));

        // Editör ayrı ve yüksek sınırdadır.
        for (var i = 0; i < 6; i++)
            Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(kurulum, taslak.Id, 1024));
        using (var red = await AlisTestYardimcisi.YukleYanit(kurulum, taslak.Id, 1024))
            Assert.Equal(HttpStatusCode.TooManyRequests, red.StatusCode);
    }

    /// <summary>Alıcı için (kullanıcı, IP) penceresinin üstünde IP'den bağımsız saatlik kova: ele geçirilmiş bir alıcı hesabı
    /// farklı ağlardan istek göndererek pencereyi çoğaltamaz. Başka alıcı ve editör bu kovadan etkilenmez.</summary>
    [Fact]
    public async Task Alicinin_saatlik_ust_kovasi_ipden_bagimsizdir_farkli_aglar_cogaltamaz()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = new VekilFabrikasi(new() { ["Kasa:HizSiniri:AlisYuklemeIzni"] = "100", ["Kasa:HizSiniri:AliciSaatlikAlisYuklemeIzni"] = "4" });
        using var kurulum = Istemci(f, "198.51.100.1");
        (await Giris(kurulum, "editor", "kasa123")).EnsureSuccessStatusCode();
        (await kurulum.PostAsJsonAsync("/api/alicilar", new AliciYaz("saatlik-alici", "Saatlik alıcı", "alici-sifre-1"), cancellationToken: ct)).EnsureSuccessStatusCode();
        (await kurulum.PostAsJsonAsync("/api/alicilar", new AliciYaz("saatlik-komsu", "Aynı ağdaki alıcı", "alici-sifre-1"), cancellationToken: ct)).EnsureSuccessStatusCode();
        var aglar = new List<HttpClient>();
        foreach (var ip in new[] { "203.0.113.5", "192.0.2.77", "198.51.100.200" })
        {
            var c = Istemci(f, ip);
            (await Giris(c, "saatlik-alici", "alici-sifre-1")).EnsureSuccessStatusCode();
            aglar.Add(c);
        }
        try
        {
            var taslak = await AlisTestYardimcisi.Taslak(aglar[0], "Saatlik");                                   // 1
            Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(aglar[1], taslak.Id, 1024));    // 2
            Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(aglar[2], taslak.Id, 1024));    // 3
            Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(aglar[0], taslak.Id, 1024));    // 4
            using (var red = await AlisTestYardimcisi.YukleYanit(aglar[1], taslak.Id, 1024))
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, red.StatusCode);
                Assert.True(red.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Retry-After başlığı saniye olarak gelmeli.");
                Assert.StartsWith("Alıcı hesabı için belge yükleme ve alış kaydı sınırına ulaşıldı: 1 saatte en çok 4", await AlisTestYardimcisi.Hata(red));
            }
            using (var red = await aglar[2].PostAsJsonAsync("/api/alis", AlisTestYardimcisi.Govde("Sınır sonrası"), cancellationToken: ct))
                Assert.Equal(HttpStatusCode.TooManyRequests, red.StatusCode);

            // Aynı ağdaki başka alıcı ve editör bu kovadan etkilenmez.
            using var komsu = Istemci(f, "203.0.113.5");
            (await Giris(komsu, "saatlik-komsu", "alici-sifre-1")).EnsureSuccessStatusCode();
            await AlisTestYardimcisi.Taslak(komsu, "Komşu");
            Assert.Equal(HttpStatusCode.Created, await AlisTestYardimcisi.Yukle(kurulum, taslak.Id, 1024));
        }
        finally { foreach (var c in aglar) c.Dispose(); }
    }

    [Fact]
    public void Kota_ayarlari_pozitif_olmali()
    {
        Assert.Empty(new AliciKotaAyarlari().Hatalar());
        Assert.NotEmpty(new AliciKotaAyarlari { AcikTaslak = 0 }.Hatalar());
        Assert.NotEmpty(new AliciKotaAyarlari { GunlukYuklemeMb = -1 }.Hatalar());
        Assert.NotEmpty(new AliciKotaAyarlari { OnayBekleyen = 0 }.Hatalar());
        // Kalıcı üst sınırlar taslak başına sınırlardan küçük olamaz (yoksa taslak sınırı hiç işlemez).
        Assert.Contains(new AliciKotaAyarlari { AcikTaslak = 30, OnayBekleyen = 20 }.Hatalar(), h => h.Contains("OnayBekleyen"));
        Assert.Contains(new AliciKotaAyarlari { TaslakBelgeMb = 300, OnayBekleyenBelgeMb = 200 }.Hatalar(), h => h.Contains("OnayBekleyenBelgeMb"));
        Assert.NotEmpty(new Kasa.Api.Auth.HizSiniriAyarlari { AlisYuklemeIzni = 0 }.Hatalar());
        Assert.NotEmpty(new Kasa.Api.Auth.HizSiniriAyarlari { AliciSaatlikAlisYuklemeIzni = 0 }.Hatalar());
    }
}

/// <summary>Alış testlerinin ortak kurulumları: alıcı oturumu, taslak, belge yükleme ve ek ayarlı fabrika.</summary>
internal static class AlisTestYardimcisi
{
    internal static AyarliFabrika KotaFabrikasi(Dictionary<string, string?> ayarlar, SabitSaat? saat = null)
        => new(ayarlar) { Saat = saat ?? new SabitSaat(KasaWebFactory.VarsayilanBugun) };

    internal static async Task<HttpClient> Alici(KasaWebFactory f, HttpClient editor, string kullanici)
    {
        (await editor.PostAsJsonAsync("/api/alicilar", new AliciYaz(kullanici, "Alıcı " + kullanici, "alici-sifre-1"))).EnsureSuccessStatusCode();
        var c = f.CreateClient();
        (await c.PostAsJsonAsync("/api/auth/login", new { kullanici, sifre = "alici-sifre-1" })).EnsureSuccessStatusCode();
        return c;
    }

    internal static AlisYaz Govde(string tedarikci) => new(0, KasaWebFactory.VarsayilanBugun, tedarikci, null, [new("Mal", 100m, [new(1, 100m)])]);

    internal static async Task<AlisDto> Taslak(HttpClient c, string tedarikci)
    {
        using var r = await c.PostAsJsonAsync("/api/alis", Govde(tedarikci));
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<AlisDto>())!;
    }

    internal static async Task<AlisDto> Gonder(HttpClient c, AlisDto alis)
    {
        using var r = await c.PostAsJsonAsync($"/api/alis/{alis.Id}/gonder", new AlisDurumYaz(alis.Surum));
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<AlisDto>())!;
    }

    /// <summary>'%PDF-' ile başlayan, istenen boyutta belge.</summary>
    internal static byte[] Pdf(int boyut)
    {
        var icerik = new byte[boyut];
        "%PDF-1.7\n"u8.CopyTo(icerik);
        return icerik;
    }

    internal static Task<HttpResponseMessage> YukleYanit(HttpClient c, int alisId, int boyut, string ad = "fatura.pdf")
        => YukleYanit(c, alisId, Pdf(boyut), ad);

    internal static async Task<HttpResponseMessage> YukleYanit(HttpClient c, int alisId, byte[] icerik, string ad, string? icerikTuru = null)
    {
        using var form = new MultipartFormDataContent();
        var dosya = new ByteArrayContent(icerik);
        if (icerikTuru is not null)
            dosya.Headers.ContentType = new(icerikTuru);
        form.Add(dosya, "dosya", ad);
        return await c.PostAsync($"/api/alis/{alisId}/belgeler", form);
    }

    internal static async Task<HttpStatusCode> Yukle(HttpClient c, int alisId, int boyut)
    {
        using var r = await YukleYanit(c, alisId, boyut);
        return r.StatusCode;
    }

    internal static async Task<string> Hata(HttpResponseMessage r)
        => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString()!;

    /// <summary>Ek ayarlı fabrika; sorgu sayacı yalnız etkinleştirildiğinde komut kaydeder.</summary>
    internal sealed class AyarliFabrika(Dictionary<string, string?> ayarlar) : KasaWebFactory
    {
        public KartHesapMaliyetiTests.SorguSayaci Sayac { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(ayarlar));
            builder.ConfigureServices(s => s.ConfigureDbContext<KasaDbContext>(o => o.AddInterceptors(Sayac)));
        }
    }
}
