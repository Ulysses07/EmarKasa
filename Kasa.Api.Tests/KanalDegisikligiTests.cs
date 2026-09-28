using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.MonthlyExpenseTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Kanal değişikliği (ops-1, ops-2, statement-6, gap-veri-degismezleri-patlama-yaricapi-6): kanal kimliği mali bağdır, kayıtlardaki
/// Kanal metni yalnız görünen ad kopyasıdır. Kilitli ayların aylık raporu kapanışta dondurulduğu için kilit varken kanal eklemek,
/// adını, sırasını ve aktifliğini değiştirmek serbesttir; açılış devri kilitte değişmez. Bugün 25 Eylül 2026 (sabit saat), takip
/// başlangıcı Haziran 2026 başı, kilitlenen ay Ağustos 2026.
/// </summary>
public class KanalDegisikligiTests
{
    private static DateOnly Old => Month.AddMonths(-1);
    private static DateOnly OldSonu => Month.AddDays(-1);
    private static string AylikUrl(DateOnly ay) => $"/api/rapor/aylik?yil={ay.Year}&ay={ay.Month}";

    [Fact]
    public async Task Tek_kanalli_aylik_gider_odemesi_olan_kanalin_adi_degisir_tutarlar_ve_kaynak_kurali_korunur()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        var sablon = await Create(c, "Ozel", [new(1, 100m)]);
        var odeme = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{sablon.Id}/ode", Payment(sablon));
        var once = await c.GetStringAsync(AylikUrl(Month)); var kasa = (await Panel(c)).GuncelKasa;

        // Önceden: 409 "Aylık gider ödemesini ... iptal edip yeniden kaydedin" (kilit yokken bile).
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT MAĞAZA")));

        var satir = (await IslemListesi(c)).Single(i => (int)i!["id"]! == odeme.IslemId)!;
        Assert.Equal("MEZAT MAĞAZA", (string)satir["kanal"]!); Assert.Equal(1, (int)satir["kanalId"]!);
        Assert.Equal(AdlarHaric(once), AdlarHaric(await c.GetStringAsync(AylikUrl(Month))));
        Assert.Contains("MEZAT MAĞAZA", await c.GetStringAsync(AylikUrl(Month)));
        Assert.Equal(kasa, (await Panel(c)).GuncelKasa);

        // İstisna yalnız kanalın güncel adına eşitlenen metindir: tutar, tarih ya da başka bir metin kaynak kuralına takılır.
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var islem = db.Islemler.Single(i => i.Id == odeme.IslemId);
        Assert.Equal(("MEZAT MAĞAZA", 1, 100m), (islem.Kanal, islem.KanalId!.Value, islem.TutarTl));
        islem.Kanal = "BAŞKA AD";
        Assert.Contains("Aylık gider", Assert.Throws<KilitliDonemException>(() => db.SaveChanges()).Message);
        islem.Kanal = "MEZAT MAĞAZA"; islem.TutarTl = 99m;
        Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task Tek_kanalli_ekstre_gideri_olan_kanalin_adi_degisir_ekstre_kaydi_ve_kasa_etkisi_degismez()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        var belge = await EkstreBelgesi(f, c);
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [new(1, Today, "Banka hareketi 1", 100m, "Gider", "Ozel", [new(1, 100m)])]);
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{belge.Id}/onizleme", istek);
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{belge.Id}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = true });
        var panel = await Panel(c);

        // Önceden: 409 "Ekstreden alınan kaydı ... iptal edip yeniden işleyin".
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT SATIŞ")));

        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var kayit = db.EkstreKayitlar.AsNoTracking().Single();
        var islem = db.Islemler.AsNoTracking().Single(i => i.Id == kayit.IslemId);
        Assert.Equal(("MEZAT SATIŞ", 1, 100m, false), (islem.Kanal, islem.KanalId!.Value, islem.TutarTl, kayit.Iptal));
        Assert.Equal(panel.GuncelKasa, (await Panel(c)).GuncelKasa);
        Assert.Equal(panel.Kanallar.Single(k => k.KanalId == 1).Bakiye, (await Panel(c)).Kanallar.Single(k => k.KanalId == 1).Bakiye);
        Assert.Equal("MEZAT SATIŞ", (string)(await IslemListesi(c)).Single(i => (int)i!["id"]! == islem.Id)!["kanal"]!);
    }

    [Fact]
    public async Task Kilit_altinda_kanal_eklenir_adlandirilir_pasife_alinir_sirasi_degisir_kilitli_ay_raporlari_ayni_kalir()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old, "Ortak kira", 100.01m, Kanallar.Ortak, GiderTipi.Cari));
        var mezatGideri = await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old.AddDays(2), "Mal", 50m, "MEZAT", GiderTipi.Cari));
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Old, "MEZAT", 300m))).EnsureSuccessStatusCode();
        var aylikIslemId = AylikGiderOdemesi(f, Old.AddDays(9), 40m, 1);
        var kredi = LegacyFinanceSeed.Kredi(f, new KrediYazDto("Kredi", 0m, Old.AddDays(4), 1, 0m, 5, "MEZAT"));
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Today, "Bu ayın ortak gideri", 90m, Kanallar.Ortak, GiderTipi.Cari));
        var kilitOncesiAylik = await c.GetStringAsync(AylikUrl(Old));
        var kilitOncesiHaftalik = KilitliHaftalar(await c.GetStringAsync("/api/rapor/haftalik"));
        await AyKilidi(c, Old, ac: false);
        var dondurulmus = await c.GetStringAsync(AylikUrl(Old));
        Assert.Equal(AyRaporuAnlikGoruntusuTests.Dondurulmus(kilitOncesiAylik, HesapServisi.AcikAyKurali), dondurulmus);

        // Önceden dördü de 409 "... tarihine kadar dönem kilitli" idi.
        var online = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("ONLINE", true, 9));
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT MAĞAZA")));
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT MAĞAZA", false)));
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/2", new KanalYazDto("PERAKENDE", Sira: 7)));

        // Aylık rapor adla birlikte birebir aynı: kapatılan ayın raporu kapanıştaki kanal adı, kümesi ve Ortak payıyla korunur.
        Assert.Equal(dondurulmus, await c.GetStringAsync(AylikUrl(Old)));
        // Haftalık rapor canlıdır ve kanalı kimlikten adlandırır: tutarlar aynı, ad güncel; yeni kanal kilitli haftalarda sıfır satırdır.
        var sonra = KilitliHaftalar(await c.GetStringAsync("/api/rapor/haftalik"));
        var adlar = new Dictionary<string, string> { ["MEZAT"] = "MEZAT MAĞAZA", ["PERAKENDE"] = "PERAKENDE", ["TOPTAN"] = "TOPTAN" };
        Assert.Equal(kilitOncesiHaftalik.Count, sonra.Count); Assert.NotEmpty(sonra);
        for (var i = 0; i < sonra.Count; i++)
        {
            var (onceH, sonraH) = (kilitOncesiHaftalik[i], sonra[i]);
            foreach (var alan in new[] { "donem", "toplamGelen", "toplamGiden", "kasaSonucu", "kasaDevir", "dagilimBekleyenTutar" })
                Assert.Equal(onceH[alan]!.ToJsonString(), sonraH[alan]!.ToJsonString());
            var satirlar = sonraH["kanallar"]!.AsArray().ToDictionary(k => (string)k!["kanal"]!, k => k!.AsObject());
            Assert.Equal(4, satirlar.Count);
            foreach (var eski in onceH["kanallar"]!.AsArray())
                Assert.Equal(AdsizSatir(eski!.AsObject()), AdsizSatir(satirlar[adlar[(string)eski["kanal"]!]]));
            Assert.All(AdsizSatir(satirlar["ONLINE"]).Select(p => p.Value), v => Assert.Equal(0m, v));
        }
        Assert.Contains(sonra, h => (decimal)h["kanallar"]!.AsArray().Single(k => (string)k!["kanal"]! == "MEZAT MAĞAZA")!["gelen"]! == 300m);

        // Açık ayda güncel küme geçerlidir: bu ayın Ortak gideri aktif üç kanala (PERAKENDE, TOPTAN, ONLINE) bölünür.
        var buAy = JsonNode.Parse(await c.GetStringAsync(AylikUrl(Month)))!["kanallar"]!.AsArray();
        Assert.Equal(30m, (decimal)buAy.Single(k => (string)k!["kanal"]! == "ONLINE")!["ortakPay"]!);
        Assert.Equal(0m, (decimal)buAy.Single(k => (string)k!["kanal"]! == "MEZAT MAĞAZA")!["ortakPay"]!);

        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        // Kilitli ayın gider ve kredi metni kimliğin görünen adına eşitlenir (tutar ve tarih aynı); kilitli gelir satırı veritabanı
        // tetikleyicisiyle hiç değişmez ve kapanıştaki etiketi taşır (gösterim ve hesap kanal kimliğinden yapılır).
        var giderler = db.Islemler.AsNoTracking().Where(i => i.Id == mezatGideri.Id || i.Id == aylikIslemId).OrderBy(i => i.Id).ToList();
        Assert.Equal(new[] { ("MEZAT MAĞAZA", 1, 50m), ("MEZAT MAĞAZA", 1, 40m) }, giderler.Select(i => (i.Kanal, i.KanalId!.Value, i.TutarTl)).ToArray());
        var kayitliKredi = db.Krediler.AsNoTracking().Single(k => k.Id == kredi.Id);
        Assert.Equal(("MEZAT MAĞAZA", 1), (kayitliKredi.Kanal, kayitliKredi.KanalId!.Value));
        var gelen = db.Gelenler.AsNoTracking().Single(g => g.DonemStart == Old);
        Assert.Equal(("MEZAT", 1, 300m), (gelen.Kanal, gelen.KanalId!.Value, gelen.TutarTl));
        Assert.True(db.Kanallar.Any(k => k.Id == online.Id && k.Aktif));
    }

    [Fact]
    public async Task Kilit_altinda_acilis_devri_degismez_gecmissiz_kanal_silinir_gecmisli_kanal_silinmez()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old, "Mal", 50m, "MEZAT", GiderTipi.Cari));
        await AyKilidi(c, Old, ac: false);
        var haftalik = await c.GetStringAsync("/api/rapor/haftalik");

        var devir = await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", AcilisDevri: 99m));
        Assert.Equal(HttpStatusCode.Conflict, devir.StatusCode); Assert.Contains("açılış devri", await Hata(devir));
        var devirli = await c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("ONLINE", AcilisDevri: 50m));
        Assert.Equal(HttpStatusCode.Conflict, devirli.StatusCode); Assert.Contains("açılış devri", await Hata(devirli));
        Assert.Equal(haftalik, await c.GetStringAsync("/api/rapor/haftalik"));

        var yeni = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("ONLINE"));
        await Durum(await c.DeleteAsync($"/api/kanallar/{yeni.Id}"), HttpStatusCode.NoContent);
        var gecmisli = await c.DeleteAsync("/api/kanallar/1");
        Assert.Equal(HttpStatusCode.Conflict, gecmisli.StatusCode); Assert.Contains("Geçmişi", await Hata(gecmisli));
        Assert.Equal(haftalik, await c.GetStringAsync("/api/rapor/haftalik"));
    }

    [Fact]
    public async Task Esik_tanimli_gecmissiz_kanal_esigiyle_birlikte_silinir_gecmisli_kanalin_esigi_kalir()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        var gecici = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("Geçici"));
        (await c.PutAsJsonAsync($"/api/kasa-esikleri/{gecici.Id}", new KasaEsikYaz(0, 100m, true))).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/kasa-esikleri/1", new KasaEsikYaz(0, 100m, true))).EnsureSuccessStatusCode();
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Today, "Mal", 50m, "MEZAT", GiderTipi.Cari));

        // Önceden: FOREIGN KEY ihlali, 500 "Veri bütünlüğü hatası".
        await Durum(await c.DeleteAsync($"/api/kanallar/{gecici.Id}"), HttpStatusCode.NoContent);
        var gecmisli = await c.DeleteAsync("/api/kanallar/1");
        Assert.Equal(HttpStatusCode.Conflict, gecmisli.StatusCode); Assert.Contains("Geçmişi", await Hata(gecmisli));

        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.False(db.Kanallar.Any(k => k.Id == gecici.Id));
        Assert.Equal(new[] { 1 }, db.KasaEsikleri.AsNoTracking().Select(x => x.KanalId).ToArray());
        var esikler = (await c.GetFromJsonAsync<KasaEsikDto[]>("/api/kasa-esikleri"))!;
        Assert.DoesNotContain(esikler, x => x.KanalId == gecici.Id);
        Assert.True(esikler.Single(x => x.KanalId == 1).Etkin);
    }

    [Fact]
    public async Task Kilitli_gelirde_kalan_eski_ad_baska_kanala_verilmez_ay_acilinca_etiket_guncellenir_ad_verilir()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Old, "MEZAT", 300m))).EnsureSuccessStatusCode();
        await AyKilidi(c, Old, ac: false);
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT MAĞAZA")));

        // Kilitli gelir satırı "MEZAT" etiketini taşır; aynı adlı başka kanal o dönemde gelir tutamayacağından ad verilemez.
        foreach (var istek in new Func<Task<HttpResponseMessage>>[] { () => c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("mezat")),
                     () => c.PutAsJsonAsync("/api/kanallar/2", new KanalYazDto("MEZAT", Sira: 1)) })
        {
            using var r = await istek();
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Contains("MEZAT MAĞAZA", await Hata(r));
        }
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT")));
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT MAĞAZA")));

        await AyKilidi(c, Old, ac: true);
        var yeni = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("MEZAT", Sira: 5));
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Old, "MEZAT", 10m))).EnsureSuccessStatusCode();
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var satirlar = db.Gelenler.AsNoTracking().Where(g => g.DonemStart == Old).OrderBy(g => g.Id).ToList();
        Assert.Equal(new[] { ("MEZAT MAĞAZA", 1, 300m), ("MEZAT", yeni.Id, 10m) }, satirlar.Select(g => (g.Kanal, g.KanalId!.Value, g.TutarTl)).ToArray());
    }

    [Fact]
    public async Task Kilitli_ayin_raporu_dondurulmamissa_kanal_kumesi_degismez_ad_degisir()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old, "Ortak kuruş", .01m, Kanallar.Ortak, GiderTipi.Cari));
        var once = await c.GetStringAsync(AylikUrl(Old));
        // Bu sürümden önce kapatılmış ay (görüntüsüz kilit): açılıştaki geçiş tohumu dondurana kadar rapor canlı hesaplanır.
        using (var scope = f.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<KasaDbContext>().Database.ExecuteSql($"UPDATE AyKilidi SET KilitliSonTarih = {OldSonu}, Surum = Surum + 1 WHERE Id = 1");

        foreach (var istek in new Func<Task<HttpResponseMessage>>[] { () => c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("ONLINE")),
                     () => c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", Sira: 99)), () => c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", false)) })
        {
            using var r = await istek();
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Contains("dondurulmamış", await Hata(r));
        }
        Assert.Equal(once, await c.GetStringAsync(AylikUrl(Old)));
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT MAĞAZA")));
        Assert.Equal(AdlarHaric(once), AdlarHaric(await c.GetStringAsync(AylikUrl(Old))));
    }

    private static async Task Basarili(HttpResponseMessage r)
    {
        using (r) Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }

    private static async Task Durum(HttpResponseMessage r, HttpStatusCode beklenen)
    {
        using (r) Assert.True(r.StatusCode == beklenen, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }

    private static async Task<string> Hata(HttpResponseMessage r) => (string)(await r.Content.ReadFromJsonAsync<JsonObject>())!["hata"]!;

    private static async Task<JsonArray> IslemListesi(HttpClient c) =>
        (await c.GetFromJsonAsync<JsonArray>($"/api/islemler?baslangic={Month.AddMonths(-3):yyyy-MM-dd}&bitis={Today:yyyy-MM-dd}"))!;

    private static async Task<AyKilidiDto> AyKilidi(HttpClient c, DateOnly ay, bool ac)
    {
        var durum = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi"))!;
        return await Post<AyKilidiDto>(c, ac ? "/api/ay-kilidi/ac" : "/api/ay-kilidi/kapat",
            new AyKilidiYaz(Guid.NewGuid(), durum.Surum, ay.Year, ay.Month, ac ? "Düzeltme için açıldı" : "Ay tamamlandı"));
    }

    /// <summary>Geçmiş ayın tek kanala dağıtılmış aylık gider ödemesi: uç şablonu yalnız cari aydan başlattığı için veri doğrudan,
    /// ödeme ucunun yazdığı biçimde (tek paylı ödemenin gideri kanal kimliği ve adını taşır) yazılır. Giderin kimliğini döner.</summary>
    private static int AylikGiderOdemesi(KasaWebFactory f, DateOnly tarih, decimal tutar, int kanalId)
    {
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var ay = new DateOnly(tarih.Year, tarih.Month, 1);
        var sablon = new AylikGiderSablonEntity(); db.AylikGiderSablonlar.Add(sablon); db.SaveChanges();
        var revizyon = new AylikGiderRevizyonEntity { SablonId = sablon.Id, Surum = 1, Ad = "Depo kirası", Tur = "Kira", Tutar = tutar, OdemeGunu = tarih.Day,
            GecerliAy = ay, DagilimTuru = "Ozel", DagilimJson = JsonSerializer.Serialize(new[] { new KanalPayYaz(kanalId, tutar) }), Aktif = true };
        var islem = new IslemEntity { Tarih = tarih, TutarTl = tutar, Cari = "Depo kirası", Tip = GiderTipi.SabitGider,
            KanalId = kanalId, Kanal = db.Kanallar.Single(k => k.Id == kanalId).Ad };
        db.AylikGiderRevizyonlar.Add(revizyon); db.Islemler.Add(islem); db.SaveChanges();
        db.AylikGiderOdemeler.Add(new AylikGiderOdemeEntity { SablonId = sablon.Id, RevizyonId = revizyon.Id, Ay = ay, Tarih = tarih, Tutar = tutar, IslemId = islem.Id });
        db.SaveChanges();
        return islem.Id;
    }

    /// <summary>Aylık raporun kanal adları çıkarılmış hali: yeniden adlandırmada tutarların aynı kaldığını karşılaştırmak için.</summary>
    private static string AdlarHaric(string rapor)
    {
        var json = JsonNode.Parse(rapor)!.AsObject();
        foreach (var satir in json["kanallar"]!.AsArray()) satir!.AsObject().Remove("kanal");
        return json.ToJsonString();
    }

    /// <summary>Haftalık raporun tamamı kilitli aya (ve öncesine) düşen dönemleri.</summary>
    private static List<JsonObject> KilitliHaftalar(string haftalik) => JsonNode.Parse(haftalik)!.AsArray()
        .Select(h => h!.AsObject()).Where(h => DateOnly.Parse((string)h["donem"]!["end"]!) <= OldSonu).ToList();

    private static Dictionary<string, decimal> AdsizSatir(JsonObject satir) =>
        satir.Where(p => p.Key != "kanal").ToDictionary(p => p.Key, p => (decimal)p.Value!);

    private static async Task<EkstreBelgeDto> EkstreBelgesi(KasaWebFactory f, HttpClient c)
    {
        int id;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var belge = new EkstreBelgeEntity { Kaynak = "Banka", Banka = "Akbank", HesapAdi = "İş hesabı", DosyaAdi = "test.pdf", DosyaOzeti = Guid.NewGuid().ToString(),
                Dosya = "%PDF-test"u8.ToArray(), Yuklendi = f.Saat!.GetUtcNow().ToUnixTimeMilliseconds(),
                SatirlarJson = JsonSerializer.Serialize(new[] { new EkstreOkunanSatir(1, 1, "Kaynak 1", Today, "Banka hareketi 1", 100m, "Cikis", "Gider", "Hareket", "TRY", []) }) };
            db.EkstreBelgeler.Add(belge); db.SaveChanges(); id = belge.Id;
        }
        return (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{id}"))!;
    }
}
