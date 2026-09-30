using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.MonthlyExpenseTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Alış ödemesine bağlanabilecek giderler (webui-6, gap-okuma-yolu-maliyet-kilit-cekismesi-12): ödeme diyaloğu bütün gider
/// geçmişini çekmez. GET /api/alis/baglanabilir-giderler yalnız Pay'in kabul edeceği giderleri (cari ya da kredi kartı,
/// pozitif, alışa/krediye/aylık gidere/hesaba bağlı olmayan, takip ve kilit sınırı içinde; banka ekstresi gideri kaynağıyla) tarih/tutar/metin
/// süzgeciyle ve imleçli sayfalarla ({ogeler, sonrakiImlec, devamVar}) döner. Sorgu sayısı kayıt sayısından bağımsızdır.
/// </summary>
public class BaglanabilirGiderTests
{
    private const string Uc = "/api/alis/baglanabilir-giderler";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private sealed record Oge(int Id, DateOnly Tarih, string Cari, decimal TutarTl, GiderTipi Tip, int? KrediKartiId, string? Not, int? EkstreKayitId = null);
    private sealed record Sayfa(List<Oge> Ogeler, string? SonrakiImlec, bool DevamVar);

    private static async Task<Sayfa> Oku(HttpClient c, string sorgu = "")
    {
        using var r = await c.GetAsync(Uc + sorgu);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<Sayfa>(Json))!;
    }

    private static async Task<int> Gider(HttpClient c, DateOnly tarih, string cari, decimal tutar, GiderTipi tip = GiderTipi.Cari, string? not = null)
    {
        using var r = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(tarih, cari, tutar, "MEZAT", tip, not));
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Yalniz_baglanabilir_giderler_listelenir_ve_liste_ile_odeme_kurali_tutarlidir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var old = Month.AddMonths(-1);
        var uygun = await Gider(c, Today.AddDays(-1), "Kargo A.Ş.", 50m, not: "Eylül kargosu");
        var eskiUygun = await Gider(c, Today.AddDays(-20), "Ambalaj", 20m);
        await Gider(c, Today, "Sabit kira", 10m, GiderTipi.SabitGider);
        await Gider(c, Today, "İade", -10m);
        var alisa = await Gider(c, Today, "Alışa bağlı", 30m);
        var alis = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Today, "Tedarikçi", null, [new("Mal", 500m, [new(1, 500m)])]));
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Today, 30m, MevcutIslemId: alisa));
        var sablon = await Create(c, "Genel", []);
        await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{sablon.Id}/ode", Payment(sablon));
        var kilitli = await Gider(c, old.AddDays(3), "Kilitli ay", 15m);
        int ekstreli, kredili, hesapli, takipOncesi;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            IslemEntity Yeni(string cari, DateOnly? tarih = null) => new() { Tarih = tarih ?? Today, Cari = cari, TutarTl = 40m, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.Cari };
            var e = Yeni("Ekstreden");
            var k = Yeni("Kredi taksidi");
            var h = Yeni("Hesap hareketi");
            var t = Yeni("Takip öncesi", Month.AddMonths(-4));
            db.Islemler.AddRange(e, k, h, t);
            var kredi = new KrediEntity { Ad = "Kredi", CekilenTutar = 1000m, CekimTarihi = Today, TaksitSayisi = 2, AylikOdeme = 500m, OdemeGunu = 5, Kanal = "MEZAT", KanalId = 1 };
            var hesap = new HesapEntity { Ad = "Eski hesap", Tur = "Banka", AcilisTarihi = Today };
            var belge = new EkstreBelgeEntity { Kaynak = "Banka", Banka = "Test", HesapAdi = "Hesap", DosyaAdi = "e.pdf", DosyaOzeti = "x" };
            db.AddRange(kredi, hesap, belge);
            db.SaveChanges();
            db.KrediTaksitOdemeler.Add(new KrediTaksitOdemeEntity { KrediId = kredi.Id, TaksitNo = 1, IslemId = k.Id });
            db.HesapHareketler.Add(new HesapHareketEntity { HesapId = hesap.Id, KanalId = 1, Tarih = Today, Tutar = -40m, Aciklama = "Eski bağ", IslemId = h.Id });
            db.SaveChanges();
            // Ekstre kaydı yazma kurallarıyla (yalnız içe aktarma yolu) korunur; test onu doğrudan tabloya ekler.
            db.Database.ExecuteSqlInterpolated($"INSERT INTO EkstreKayitlar (BelgeId, SatirNo, Tarih, Aciklama, Tutar, IslemTuru, DagilimTuru, DagilimJson, IslemId, Iptal) VALUES ({belge.Id}, 1, {Today}, 'Ekstreden', '40.0', 'Gider', 'Genel', '[]', {e.Id}, 0)");
            (ekstreli, kredili, hesapli, takipOncesi) = (e.Id, k.Id, h.Id, t.Id);
        }
        var kilit = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi"))!;
        await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), kilit.Surum, old.Year, old.Month, "Ay tamamlandı"));

        // Banka ekstresi gideri kaynak satırıyla listelenir (gap-coklu-giris-cift-sayim-mutabakat-1: bağlanınca satır eşleşmeye döner).
        var sayfa = await Oku(c);
        Assert.Equal([ekstreli, uygun, eskiUygun], sayfa.Ogeler.Select(o => o.Id));
        Assert.False(sayfa.DevamVar);
        Assert.Null(sayfa.SonrakiImlec);
        Assert.NotNull(sayfa.Ogeler[0].EkstreKayitId);
        Assert.Null(sayfa.Ogeler[1].EkstreKayitId);
        Assert.Equal(("Kargo A.Ş.", 50m, GiderTipi.Cari, "Eylül kargosu"), (sayfa.Ogeler[1].Cari, sayfa.Ogeler[1].TutarTl, sayfa.Ogeler[1].Tip, sayfa.Ogeler[1].Not));
        foreach (var hic in new[] { alisa, kilitli, kredili, hesapli, takipOncesi })
            Assert.DoesNotContain(sayfa.Ogeler, o => o.Id == hic);

        // Listedeki gider ödeme ucunun kurallarından geçer; bağlandıktan sonra listeden çıkar.
        var yeni = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Today, "Bağlanacak", null, [new("Mal", 100m, [new(1, 100m)])]));
        var secilen = sayfa.Ogeler[1];
        await Post<AlisDto>(c, $"/api/alis/{yeni.Id}/odemeler", new AlisOdemeYaz(yeni.Surum, Guid.NewGuid(), secilen.Tarih, secilen.TutarTl, secilen.KrediKartiId, secilen.Id));
        Assert.Equal([ekstreli, eskiUygun], (await Oku(c)).Ogeler.Select(o => o.Id));
    }

    [Fact]
    public async Task Tarih_tutar_ve_metin_suzgeci_ile_imlecli_sayfalama()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var idler = new List<int>();
        for (var i = 0; i < 5; i++)
            idler.Add(await Gider(c, Today.AddDays(-i), $"Tedarik {i}", 10m + i, not: i == 3 ? "özel %_ not" : null));
        var ayniGun = await Gider(c, Today, "Tedarik aynı gün", 99m);

        // Tarih azalan, aynı günde yeni kayıt önce; sayfalar çakışmaz ve eksiksizdir.
        var beklenen = new[] { ayniGun, idler[0], idler[1], idler[2], idler[3], idler[4] };
        var toplanan = new List<int>();
        string? imlec = null;
        do
        {
            var sayfa = await Oku(c, "?limit=2" + (imlec is null ? "" : "&imlec=" + Uri.EscapeDataString(imlec)));
            Assert.True(sayfa.Ogeler.Count <= 2);
            toplanan.AddRange(sayfa.Ogeler.Select(o => o.Id));
            Assert.Equal(sayfa.DevamVar, sayfa.SonrakiImlec is not null);
            imlec = sayfa.SonrakiImlec;
        } while (imlec is not null);
        Assert.Equal(beklenen, toplanan);

        Assert.Equal([idler[2]], (await Oku(c, "?tutar=12")).Ogeler.Select(o => o.Id));
        Assert.Equal([idler[2]], (await Oku(c, "?tutar=12,00")).Ogeler.Select(o => o.Id));
        Assert.Equal([idler[3]], (await Oku(c, "?arama=" + Uri.EscapeDataString("%_"))).Ogeler.Select(o => o.Id));
        Assert.Equal([ayniGun], (await Oku(c, "?arama=ayn")).Ogeler.Select(o => o.Id));
        Assert.Equal([idler[1], idler[2]], (await Oku(c, $"?baslangic={Today.AddDays(-2):yyyy-MM-dd}&bitis={Today.AddDays(-1):yyyy-MM-dd}")).Ogeler.Select(o => o.Id));

        foreach (var hatali in new[] { "?limit=0", "?limit=201", "?imlec=bozuk", "?tutar=abc", "?tutar=-5", $"?baslangic={Today:yyyy-MM-dd}&bitis={Today.AddDays(-1):yyyy-MM-dd}", "?arama=" + new string('a', 201) })
        {
            using var r = await c.GetAsync(Uc + hatali);
            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, hatali);
        }
    }

    [Fact]
    public async Task Tutar_gibi_okunan_arama_metni_tutarla_ya_da_aciklama_ve_notla_eslesir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var aciklamada = await Gider(c, Today, "Fatura 2024", 10m);
        var tutarda = await Gider(c, Today.AddDays(-1), "Kira", 2024m);
        var notta = await Gider(c, Today.AddDays(-2), "Kargo", 5m, not: "sipariş 2024/17");
        await Gider(c, Today.AddDays(-3), "İlgisiz", 7m);

        // Arama metni '2024' hem fatura/sipariş numarası hem tutar olabilir: istemci metnin tutar okumasını da gönderir,
        // sunucu ikisinden birine uyan giderleri döndürür. 'tutar' ise ayrı ve kesin (VE) süzgeçtir.
        Assert.Equal([aciklamada, tutarda, notta], (await Oku(c, "?arama=2024&aramaTutari=2024")).Ogeler.Select(o => o.Id));
        Assert.Equal([aciklamada, notta], (await Oku(c, "?arama=2024")).Ogeler.Select(o => o.Id));
        Assert.Equal([tutarda], (await Oku(c, "?aramaTutari=2024,00")).Ogeler.Select(o => o.Id));
        Assert.Equal([aciklamada], (await Oku(c, "?arama=2024&aramaTutari=2024&tutar=10")).Ogeler.Select(o => o.Id));

        // İmleçli sayfalar aynı birleşik süzgeçle eksiksiz ilerler.
        var ilk = await Oku(c, "?arama=2024&aramaTutari=2024&limit=2");
        Assert.Equal([aciklamada, tutarda], ilk.Ogeler.Select(o => o.Id));
        Assert.Equal([notta], (await Oku(c, "?arama=2024&aramaTutari=2024&limit=2&imlec=" + Uri.EscapeDataString(ilk.SonrakiImlec!))).Ogeler.Select(o => o.Id));

        foreach (var hatali in new[] { "?arama=2024&aramaTutari=abc", "?aramaTutari=0", "?aramaTutari=1.234" })
        {
            using var r = await c.GetAsync(Uc + hatali);
            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, hatali);
        }
    }

    [Fact]
    public async Task Yalniz_editor_gorebilir()
    {
        await using var f = Fabrika();
        using var editor = await Editor(f);
        using var alici = await AlisTestYardimcisi.Alici(f, editor, "baglanabilir");
        Assert.Equal(HttpStatusCode.Forbidden, (await alici.GetAsync(Uc)).StatusCode);
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifresi-12" })).EnsureSuccessStatusCode();
        using var izleyici = f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izleyici-sifresi-12" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.GetAsync(Uc)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync(Uc)).StatusCode);
    }

    [Fact]
    public async Task Sorgu_sayisi_kayit_sayisindan_bagimsizdir()
    {
        await using var f = new KartHesapMaliyetiTests.SayacliFabrika();
        using var c = await Editor(f);
        async Task<int> Sorgular()
        {
            f.Sayac.Sifirla();
            f.Sayac.Etkin = true;
            try
            { await Oku(c, "?limit=200"); }
            finally { f.Sayac.Etkin = false; }
            return f.Sayac.Komutlar.Count;
        }
        for (var i = 0; i < 3; i++)
            await Gider(c, Today.AddDays(-i), $"Az {i}", 5m);
        var az = await Sorgular();
        for (var i = 0; i < 40; i++)
            await Gider(c, Today.AddDays(-(i % 20)), $"Çok {i}", 5m + i);
        Assert.Equal(az, await Sorgular());
        Assert.True(az <= 8, $"Beklenenden çok sorgu: {az}");
    }

    [Fact]
    public async Task Gider_listesi_kanal_adlarini_satir_basina_degil_bir_kez_okur()
    {
        await using var f = new KartHesapMaliyetiTests.SayacliFabrika();
        using var c = await Editor(f);
        var sablon = await Create(c, "Ozel", [new(1, 100m)]);
        await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{sablon.Id}/ode", Payment(sablon));
        for (var i = 0; i < 5; i++)
            await Gider(c, Today.AddDays(-i), $"Gider {i}", 5m);
        f.Sayac.Sifirla();
        f.Sayac.Etkin = true;
        try
        { (await c.GetAsync("/api/islemler")).EnsureSuccessStatusCode(); }
        finally { f.Sayac.Etkin = false; }
        Assert.Equal(1, f.Sayac.Komutlar.Count(k => k.Contains("FROM \"Kanallar\"")));
        // Aylık gider revizyonları yalnız listedeki ödemeler için okunur (bütün tablo değil).
        Assert.All(f.Sayac.Komutlar.Where(k => k.Contains("FROM \"AylikGiderRevizyonlar\"")), k => Assert.Contains("WHERE", k));
    }
}

/// <summary>
/// Ana sayfanın inceleme kutusu (webui-6, ana sayfa bölümü): editörün ana sayfası yalnız inceleme bekleyen alış sayısını ve
/// en yeni birkaç alışı göstermek için bütün alış listesini (kalem, dağılım ve ödeme join'leriyle) indirmez.
/// GET /api/alis/inceleme-ozeti sayıyı COUNT ile, yalnız istenen sayıdaki alışı /api/alis ile aynı sıra ve biçimde döndürür;
/// sorgu sayısı alış sayısından bağımsızdır.
/// </summary>
public class AlisIncelemeOzetiTests
{
    private const string Uc = "/api/alis/inceleme-ozeti";

    private sealed record Ozet(int Sayi, List<AlisDto> Ogeler);

    private static async Task<Ozet> Oku(HttpClient c, string sorgu = "")
    {
        using var r = await c.GetAsync(Uc + sorgu);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<Ozet>())!;
    }

    private static async Task<AlisDto> Incelemede(HttpClient c, DateOnly tarih, string tedarikci)
        => await AlisTestYardimcisi.Gonder(c, await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, tarih, tedarikci, null, [new("Mal", 100m, [new(1, 100m)])])));

    [Fact]
    public async Task Inceleme_bekleyen_sayisini_ve_en_yeni_alislari_liste_sirasi_ve_bicimiyle_doner()
    {
        await using var f = Fabrika();
        using var editor = await Editor(f);
        using var alici = await AlisTestYardimcisi.Alici(f, editor, "inceleme-ozeti");
        var gonderilen = new List<AlisDto>();
        for (var i = 0; i < 6; i++)
            gonderilen.Add(await Incelemede(i % 2 == 0 ? alici : editor, Today.AddDays(-(i % 3)), $"Tedarikçi {i}"));
        await AlisTestYardimcisi.Taslak(alici, "Taslakta kalan");
        (await editor.PostAsJsonAsync($"/api/alis/{gonderilen[0].Id}/onayla", new AlisDurumYaz(gonderilen[0].Surum))).EnsureSuccessStatusCode();

        // Beklenen: tam listenin incelemedeki alışları, aynı sırayla (tarih ve kimlik azalan) ve aynı DTO biçimiyle.
        var bekleyen = (await editor.GetFromJsonAsync<List<AlisDto>>("/api/alis"))!.Where(a => a.Durum == AlisDurumlari.Incelemede).ToList();
        Assert.Equal(5, bekleyen.Count);
        var ozet = await Oku(editor);
        Assert.Equal(5, ozet.Sayi);
        Assert.Equal(JsonSerializer.Serialize(bekleyen.Take(4)), JsonSerializer.Serialize(ozet.Ogeler));
        Assert.Equal(bekleyen.Take(2).Select(a => a.Id), (await Oku(editor, "?adet=2")).Ogeler.Select(a => a.Id));
        var yalnizSayi = await Oku(editor, "?adet=0");
        Assert.Equal((5, 0), (yalnizSayi.Sayi, yalnizSayi.Ogeler.Count));

        foreach (var hatali in new[] { "?adet=-1", "?adet=21" })
        {
            using var r = await editor.GetAsync(Uc + hatali);
            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, hatali);
        }
        // Alıcı yalnız kendi alışlarını görür: bütün alıcıların inceleme sayısı editöre özeldir.
        Assert.Equal(HttpStatusCode.Forbidden, (await alici.GetAsync(Uc)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync(Uc)).StatusCode);
    }

    [Fact]
    public async Task Sorgu_sayisi_alis_sayisindan_bagimsizdir()
    {
        await using var f = new KartHesapMaliyetiTests.SayacliFabrika();
        using var c = await Editor(f);
        async Task<int> Sorgular()
        {
            f.Sayac.Sifirla();
            f.Sayac.Etkin = true;
            try
            { await Oku(c); }
            finally { f.Sayac.Etkin = false; }
            return f.Sayac.Komutlar.Count;
        }
        for (var i = 0; i < 5; i++)
            await Incelemede(c, Today.AddDays(-i), $"Az {i}");
        var az = await Sorgular();
        for (var i = 0; i < 25; i++)
            await Incelemede(c, Today.AddDays(-(i % 7)), $"Çok {i}");
        for (var i = 0; i < 5; i++)
            await AlisTestYardimcisi.Taslak(c, $"Taslak {i}");
        Assert.Equal(az, await Sorgular());
        Assert.True(az <= 10, $"Beklenenden çok sorgu: {az}");
        Assert.Equal(30, (await Oku(c)).Sayi);
    }
}
