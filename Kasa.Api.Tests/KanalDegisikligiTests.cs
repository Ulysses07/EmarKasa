using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.MonthlyExpenseTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Kanal değişikliği (ops-1, ops-2, statement-6, gap-veri-degismezleri-patlama-yaricapi-6): kanal kimliği mali bağdır, kayıtlardaki
/// Kanal metni yalnız görünen ad kopyasıdır. Tamamlanmış ayın kanal kümesi (Ortak gider dağılımı ve rapor satırları) kanal
/// değişikliğinden önce ve ay kapatılırken dondurulur (AyKanalKumesi, core-1); bu yüzden kilit varken de ad, kanal ekleme, aktiflik
/// ve sıra serbesttir: kapatılmış ay sonradan açılınca raporu kapanıştakiyle aynı kalır. Açılış devri kilitte değişmez; tamamlanmış
/// bir ayın kümesinde yer alan kanal silinmez. Bugün 25 Eylül 2026 (sabit saat), takip başlangıcı Haziran 2026 başı, kilitlenen ay
/// Ağustos 2026.
/// </summary>
public class KanalDegisikligiTests
{
    private static DateOnly Old => Month.AddMonths(-1);
    private static DateOnly OldSonu => Month.AddDays(-1);
    private static string AylikUrl(DateOnly ay) => $"/api/rapor/aylik?yil={ay.Year}&ay={ay.Month}";

    [Fact]
    public async Task Tek_kanalli_aylik_gider_odemesi_olan_kanalin_adi_degisir_tutarlar_ve_kaynak_kurali_korunur()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var sablon = await Create(c, "Ozel", [new(1, 100m)]);
        var odeme = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{sablon.Id}/ode", Payment(sablon));
        var once = await c.GetStringAsync(AylikUrl(Month));
        var kasa = (await Panel(c)).GuncelKasa;

        // Önceden: 409 "Aylık gider ödemesini ... iptal edip yeniden kaydedin" (kilit yokken bile).
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT MAĞAZA")));

        var satir = (await IslemListesi(c)).Single(i => (int)i!["id"]! == odeme.IslemId)!;
        Assert.Equal("MEZAT MAĞAZA", (string)satir["kanal"]!);
        Assert.Equal(1, (int)satir["kanalId"]!);
        Assert.Equal(AdlarHaric(once), AdlarHaric(await c.GetStringAsync(AylikUrl(Month))));
        Assert.Contains("MEZAT MAĞAZA", await c.GetStringAsync(AylikUrl(Month)));
        Assert.Equal(kasa, (await Panel(c)).GuncelKasa);

        // İstisna yalnız kanalın güncel adına eşitlenen metindir: tutar, tarih ya da başka bir metin kaynak kuralına takılır.
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var islem = db.Islemler.Single(i => i.Id == odeme.IslemId);
        Assert.Equal(("MEZAT MAĞAZA", 1, 100m), (islem.Kanal, islem.KanalId!.Value, islem.TutarTl));
        islem.Kanal = "BAŞKA AD";
        Assert.Contains("Aylık gider", Assert.Throws<KilitliDonemException>(() => db.SaveChanges()).Message);
        islem.Kanal = "MEZAT MAĞAZA";
        islem.TutarTl = 99m;
        Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task Tek_kanalli_ekstre_gideri_olan_kanalin_adi_degisir_ekstre_kaydi_ve_kasa_etkisi_degismez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var belge = await EkstreBelgesi(f, c);
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [new(1, Today, "Banka hareketi 1", 100m, "Gider", "Ozel", [new(1, 100m)])]);
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{belge.Id}/onizleme", istek);
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{belge.Id}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = true });
        var panel = await Panel(c);

        // Önceden: 409 "Ekstreden alınan kaydı ... iptal edip yeniden işleyin".
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT SATIŞ")));

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var kayit = db.EkstreKayitlar.AsNoTracking().Single();
        var islem = db.Islemler.AsNoTracking().Single(i => i.Id == kayit.IslemId);
        Assert.Equal(("MEZAT SATIŞ", 1, 100m, false), (islem.Kanal, islem.KanalId!.Value, islem.TutarTl, kayit.Iptal));
        Assert.Equal(panel.GuncelKasa, (await Panel(c)).GuncelKasa);
        Assert.Equal(panel.Kanallar.Single(k => k.KanalId == 1).Bakiye, (await Panel(c)).Kanallar.Single(k => k.KanalId == 1).Bakiye);
        Assert.Equal("MEZAT SATIŞ", (string)(await IslemListesi(c)).Single(i => (int)i!["id"]! == islem.Id)!["kanal"]!);
    }

    [Fact]
    public async Task Kilit_altinda_ad_pasif_kanal_ve_ortak_sirasini_bozmayan_sira_serbest_ay_acilinca_rapor_kanal_bazinda_ayni_kalir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old, "Ortak kira", 100.01m, KanalEtiketleri.Ortak, GiderTipi.Cari));
        var mezatGideri = await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old.AddDays(2), "Mal", 50m, "MEZAT", GiderTipi.Cari));
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Old, "MEZAT", 300m))).EnsureSuccessStatusCode();
        var aylikIslemId = AylikGiderOdemesi(f, Old.AddDays(9), 40m, 1);
        var kredi = LegacyFinanceSeed.Kredi(f, new KrediYazDto("Kredi", 0m, Old.AddDays(4), 1, 0m, 5, "MEZAT"));
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Today, "Bu ayın ortak gideri", 90m, KanalEtiketleri.Ortak, GiderTipi.Cari));
        var kilitOncesiAylik = await c.GetStringAsync(AylikUrl(Old));
        var kilitOncesiHaftalik = KilitliHaftalar(await c.GetStringAsync("/api/rapor/haftalik"));
        await AyKilidi(c, Old, ac: false);
        var dondurulmus = await c.GetStringAsync(AylikUrl(Old));
        Assert.Equal(AyRaporuAnlikGoruntusuTests.Dondurulmus(kilitOncesiAylik, HesapServisi.AcikAyKurali), dondurulmus);

        // Önceden hepsi 409 "... tarihine kadar dönem kilitli" idi: pasif yeni kanal, pasif kanalın sırası, ad, sıra, geçmişsiz pasif
        // kanalı silme. Ağustos'un kanal kümesi (MEZAT, PERAKENDE, TOPTAN) kapanışta dondurulmuştur.
        var online = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("ONLINE", false, 9));
        await Basarili(await c.PutAsJsonAsync($"/api/kanallar/{online.Id}", new KanalYazDto("ONLINE", false, 1)));
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT MAĞAZA")));
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/3", new KanalYazDto("TOPTAN", Sira: 20)));
        var gecici = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("GEÇİCİ", false));
        await Durum(await c.DeleteAsync($"/api/kanallar/{gecici.Id}"), HttpStatusCode.NoContent);

        // Kilitli ayın raporu (dondurulmuş) adla birlikte birebir aynı.
        Assert.Equal(dondurulmus, await c.GetStringAsync(AylikUrl(Old)));
        // Haftalık rapor canlıdır ve kanalı kimlikten adlandırır: tutarlar aynı, ad güncel; yeni kanal kilitli haftalarda sıfır satırdır.
        var sonra = KilitliHaftalar(await c.GetStringAsync("/api/rapor/haftalik"));
        var adlar = new Dictionary<string, string> { ["MEZAT"] = "MEZAT MAĞAZA", ["PERAKENDE"] = "PERAKENDE", ["TOPTAN"] = "TOPTAN" };
        Assert.Equal(kilitOncesiHaftalik.Count, sonra.Count);
        Assert.NotEmpty(sonra);
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

        // Açık ay güncel kanallarla: bu ayın Ortak gideri aktif üç kanala bölünür, pasif ONLINE pay almaz.
        var buAy = JsonNode.Parse(await c.GetStringAsync(AylikUrl(Month)))!["kanallar"]!.AsArray();
        Assert.Equal(30m, (decimal)buAy.Single(k => (string)k!["kanal"]! == "MEZAT MAĞAZA")!["ortakPay"]!);
        Assert.Equal(0m, (decimal)buAy.Single(k => (string)k!["kanal"]! == "ONLINE")!["ortakPay"]!);

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            // Kilitli ayın gider ve kredi metni kimliğin görünen adına eşitlenir (tutar ve tarih aynı); kilitli gelir satırı veritabanı
            // tetikleyicisiyle hiç değişmez ve kapanıştaki etiketi taşır (gösterim ve hesap kanal kimliğinden yapılır).
            var giderler = db.Islemler.AsNoTracking().Where(i => i.Id == mezatGideri.Id || i.Id == aylikIslemId).OrderBy(i => i.Id).ToList();
            Assert.Equal(new[] { ("MEZAT MAĞAZA", 1, 50m), ("MEZAT MAĞAZA", 1, 40m) }, giderler.Select(i => (i.Kanal, i.KanalId!.Value, i.TutarTl)).ToArray());
            var kayitliKredi = db.Krediler.AsNoTracking().Single(k => k.Id == kredi.Id);
            Assert.Equal(("MEZAT MAĞAZA", 1), (kayitliKredi.Kanal, kayitliKredi.KanalId!.Value));
            var gelen = db.Gelenler.AsNoTracking().Single(g => g.DonemStart == Old);
            Assert.Equal(("MEZAT", 1, 300m), (gelen.Kanal, gelen.KanalId!.Value, gelen.TutarTl));
            Assert.False(db.Kanallar.Single(k => k.Id == online.Id).Aktif);
            // Canlı hesap (ay açılınca gösterilecek rapor) kilit altında da kanal bazında kapanıştakiyle aynı.
            KanalBazindaAyni(kilitOncesiAylik, CanliAylik(scope, Old), adlar);
        }

        // Ay düzeltme için açılınca rapor ayın kümesiyle canlı hesaplanır: kanal bazında kapanıştakiyle aynı (ad güncel; kümeden sonra
        // açılan ONLINE'ın satırı yok).
        await AyKilidi(c, Old, ac: true);
        var acilan = await c.GetStringAsync(AylikUrl(Old));
        KanalBazindaAyni(kilitOncesiAylik, acilan, adlar);
        Assert.DoesNotContain("ONLINE", acilan);
        // Önceki aylar hâlâ kilitli: pasif kanal yine de aktifleştirilir (önceden 409); Ağustos'un raporu değişmez.
        await Basarili(await c.PutAsJsonAsync($"/api/kanallar/{online.Id}", new KanalYazDto("ONLINE", true, 1)));
        Assert.Equal(acilan, await c.GetStringAsync(AylikUrl(Old)));
    }

    /// <summary>İnceleme yeniden üretimi (önceden 409; daha önce de kilitte Ortak kümesi değişip ay açılınca, hiçbir kayıt değişmeden
    /// kapanan ayın Ortak payı yeniden yazılıyordu: ONLINE eklenince 25,01/25/25/25; MEZAT pasifken 0; sıra değişince artık kuruş başka
    /// kanalda). Ağustos'ta açılan ONLINE (aktif) ve YEDEK (pasif) Ağustos'un kümesindedir: Ağustos kapanışta dört aktif kanalla
    /// dondurulur. Kilit altında ekleme, pasife alma, aktifleştirme ve sıra serbesttir; kümedeki kanal silinmez. Kapatılan ayın raporu,
    /// canlı hesabı ve ay açılınca gösterilen raporu kapanıştakiyle birebir aynıdır.</summary>
    [Theory]
    [InlineData("aktif kanal ekle")]
    [InlineData("pasife al")]
    [InlineData("aktiflestir")]
    [InlineData("aktif sirayi degistir")]
    [InlineData("aktif kanali sil")]
    public async Task Kilit_altinda_ortak_kumesini_degistiren_kanal_degisikligi_serbest_kumedeki_kanal_silinmez_ay_acilinca_rapor_birebir_ayni(string degisiklik)
    {
        var saat = new SabitSaat(Old.AddDays(19));
        await using var f = new KasaWebFactory { Saat = saat };
        using var c = await Editor(f);
        var online = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("ONLINE", true, 3));
        var yedek = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("YEDEK", false, 4));
        saat.Ayarla(Today);
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old, "Ortak kira", 100.01m, KanalEtiketleri.Ortak, GiderTipi.Cari));
        var kilitOncesi = await c.GetStringAsync(AylikUrl(Old));
        Assert.Equal(25.01m, (decimal)JsonNode.Parse(kilitOncesi)!["kanallar"]!.AsArray().Single(k => (string)k!["kanal"]! == "MEZAT")!["ortakPay"]!);
        await AyKilidi(c, Old, ac: false);
        var dondurulmus = await c.GetStringAsync(AylikUrl(Old));

        using (var r = degisiklik switch
        {
            "aktif kanal ekle" => await c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("E-TİCARET", true, 5)),
            "pasife al" => await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", false)),
            "aktiflestir" => await c.PutAsJsonAsync($"/api/kanallar/{yedek.Id}", new KanalYazDto("YEDEK", true, 4)),
            "aktif sirayi degistir" => await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", Sira: 99)),
            _ => await c.DeleteAsync($"/api/kanallar/{online.Id}"),
        })
        {
            if (degisiklik == "aktif kanali sil")
            {
                Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
                Assert.Contains("kanal kümesinde", await Hata(r));
            }
            else
                Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        }
        Assert.Equal(dondurulmus, await c.GetStringAsync(AylikUrl(Old)));
        using (var scope = f.Services.CreateScope())
            Assert.Equal(kilitOncesi, CanliAylik(scope, Old));

        // Yazım hatası düzeltmek için ay açılır: rapor ayın kümesiyle canlı hesaplanır ve kapanmadan önce gösterilenle birebir aynıdır.
        await AyKilidi(c, Old, ac: true);
        Assert.Equal(kilitOncesi, await c.GetStringAsync(AylikUrl(Old)));
    }

    [Fact]
    public async Task Kilitli_aydaki_tek_kanalli_ekstre_giderinin_kanali_adlandirilir_tutar_tarih_ve_ekstre_kaydi_degismez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var tarih = Old.AddDays(5);
        var belge = await EkstreBelgesi(f, c, tarih);
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [new(1, tarih, "Banka hareketi 1", 100m, "Gider", "Ozel", [new(1, 100m)])]);
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{belge.Id}/onizleme", istek);
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{belge.Id}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = true });
        var kilitOncesi = await c.GetStringAsync(AylikUrl(Old));
        string kayitOnce;
        using (var scope = f.Services.CreateScope())
            kayitOnce = JsonSerializer.Serialize(scope.ServiceProvider.GetRequiredService<KasaDbContext>().EkstreKayitlar.AsNoTracking().Single());
        await AyKilidi(c, Old, ac: false);
        var dondurulmus = await c.GetStringAsync(AylikUrl(Old));
        var kasa = (await Panel(c)).GuncelKasa;

        // Önceden: 409 "Ekstreden alınan kaydı ... iptal edip yeniden işleyin" (kilitli ayda kaynak kuralı ve dönem kilidi birlikte).
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT SATIŞ")));

        Assert.Equal(dondurulmus, await c.GetStringAsync(AylikUrl(Old)));
        Assert.Equal(kasa, (await Panel(c)).GuncelKasa);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var kayit = db.EkstreKayitlar.AsNoTracking().Single();
            Assert.Equal(kayitOnce, JsonSerializer.Serialize(kayit));
            var islem = db.Islemler.Single(i => i.Id == kayit.IslemId);
            Assert.Equal(("MEZAT SATIŞ", 1, 100m, tarih), (islem.Kanal, islem.KanalId!.Value, islem.TutarTl, islem.Tarih));
            Assert.Equal("MEZAT SATIŞ", (string)(await IslemListesi(c)).Single(i => (int)i!["id"]! == islem.Id)!["kanal"]!);
            Assert.Equal(AdlarHaric(kilitOncesi), AdlarHaric(CanliAylik(scope, Old)));

            // İstisna yalnız ad eşitlemesidir: kilitli ekstre giderinin başka metni ya da tutarı yine değişmez.
            islem.Kanal = "BAŞKA AD";
            Assert.Contains("Ekstre", Assert.Throws<KilitliDonemException>(() => db.SaveChanges()).Message);
            islem.Kanal = "MEZAT SATIŞ";
            islem.TutarTl = 99m;
            Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
        }
    }

    [Fact]
    public async Task Kilit_altinda_acilis_devri_degismez_gecmissiz_kanal_silinir_gecmisli_kanal_silinmez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old, "Mal", 50m, "MEZAT", GiderTipi.Cari));
        await AyKilidi(c, Old, ac: false);
        var haftalik = await c.GetStringAsync("/api/rapor/haftalik");

        var devir = await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", AcilisDevri: 99m));
        Assert.Equal(HttpStatusCode.Conflict, devir.StatusCode);
        Assert.Contains("açılış devri", await Hata(devir));
        var devirli = await c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("ONLINE", AcilisDevri: 50m));
        Assert.Equal(HttpStatusCode.Conflict, devirli.StatusCode);
        Assert.Contains("açılış devri", await Hata(devirli));
        Assert.Equal(haftalik, await c.GetStringAsync("/api/rapor/haftalik"));

        // Kilitte yeni kanal (açılış devri 0) aktif de eklenir; tamamlanmış ayların kümesinden sonra açılan geçmişsiz kanal silinir.
        var yeni = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("ONLINE", true, 3));
        await Durum(await c.DeleteAsync($"/api/kanallar/{yeni.Id}"), HttpStatusCode.NoContent);
        // Geçmişli kanal silinmez; ileti pasife almayı önerir (kilit varken de serbest; önceden "pasife alınamaz" diyordu).
        var gecmisli = await c.DeleteAsync("/api/kanallar/1");
        Assert.Equal(HttpStatusCode.Conflict, gecmisli.StatusCode);
        var ileti = await Hata(gecmisli);
        Assert.Contains("Geçmişi", ileti);
        Assert.Contains("pasifleştirebilirsiniz", ileti);
        Assert.DoesNotContain("pasife alınamaz", ileti);
        Assert.Equal(haftalik, await c.GetStringAsync("/api/rapor/haftalik"));
    }

    /// <summary>Yalnız çok kanallı ekstre dağılımında ya da aylık gider şablonunda kullanılan kanal (gideri kanal kimliği taşımaz):
    /// silme engeli uçta öteki geçmişle aynı iletiyle döner ve kilitli de kilitsiz de pasife almayı önerir (ay kanal kümesiyle
    /// pasife alma kilitte de serbesttir). Önceden kaydetme kuralının iletisi dönüyordu.</summary>
    [Theory]
    [InlineData("ekstre", false)]
    [InlineData("ekstre", true)]
    [InlineData("aylik gider", false)]
    [InlineData("aylik gider", true)]
    public async Task Yalniz_dagilimda_kullanilan_kanal_silinmez_ileti_kilitte_de_pasife_almayi_onerir(string kaynak, bool kilitli)
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var online = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("ONLINE", true, 3));
        KanalPayYaz[] paylar = [new(1, 60m), new(online.Id, 40m)];
        if (kaynak == "ekstre")
        {
            var belge = await EkstreBelgesi(f, c);
            var istek = new EkstreKaydetYaz(Guid.NewGuid(), belge.Surum, [new(1, Today, "Banka hareketi 1", 100m, "Gider", "Ozel", paylar)]);
            var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{belge.Id}/onizleme", istek);
            await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{belge.Id}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = true });
        }
        else
            await Create(c, "Ozel", paylar);
        if (kilitli)
            await AyKilidi(c, Old, ac: false);

        using (var r = await c.DeleteAsync($"/api/kanallar/{online.Id}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var ileti = await Hata(r);
            Assert.StartsWith("Geçmişi", ileti);
            Assert.EndsWith("Kanalı pasifleştirebilirsiniz.", ileti);
            Assert.DoesNotContain("kilit", ileti);
        }
        using var scope = f.Services.CreateScope();
        Assert.True(scope.ServiceProvider.GetRequiredService<KasaDbContext>().Kanallar.Any(k => k.Id == online.Id));
    }

    [Fact]
    public async Task Esik_tanimli_gecmissiz_kanal_esigiyle_birlikte_silinir_gecmisli_kanalin_esigi_kalir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var gecici = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("Geçici"));
        (await c.PutAsJsonAsync($"/api/kasa-esikleri/{gecici.Id}", new KasaEsikYaz(0, 100m, true))).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/kasa-esikleri/1", new KasaEsikYaz(0, 100m, true))).EnsureSuccessStatusCode();
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Today, "Mal", 50m, "MEZAT", GiderTipi.Cari));

        // Önceden: FOREIGN KEY ihlali, 500 "Veri bütünlüğü hatası".
        await Durum(await c.DeleteAsync($"/api/kanallar/{gecici.Id}"), HttpStatusCode.NoContent);
        var gecmisli = await c.DeleteAsync("/api/kanallar/1");
        Assert.Equal(HttpStatusCode.Conflict, gecmisli.StatusCode);
        Assert.Contains("Geçmişi", await Hata(gecmisli));

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.False(db.Kanallar.Any(k => k.Id == gecici.Id));
        Assert.Equal(new[] { 1 }, db.KasaEsikleri.AsNoTracking().Select(x => x.KanalId).ToArray());
        var esikler = (await c.GetFromJsonAsync<KasaEsikDto[]>("/api/kasa-esikleri"))!;
        Assert.DoesNotContain(esikler, x => x.KanalId == gecici.Id);
        Assert.True(esikler.Single(x => x.KanalId == 1).Etkin);
    }

    [Fact]
    public async Task Kilitli_gelirde_kalan_eski_ad_baska_kanala_verilmez_ay_acilinca_etiket_guncellenir_ad_verilir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Old, "MEZAT", 300m))).EnsureSuccessStatusCode();
        await AyKilidi(c, Old, ac: false);
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT MAĞAZA")));

        // Kilitli gelir satırı "MEZAT" etiketini taşır; aynı adlı başka kanal o dönemde gelir tutamayacağından ad verilemez.
        foreach (var istek in new Func<Task<HttpResponseMessage>>[] { () => c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("mezat")),
                     () => c.PutAsJsonAsync("/api/kanallar/2", new KanalYazDto("MEZAT", Sira: 1)) })
        {
            using var r = await istek();
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Contains("MEZAT MAĞAZA", await Hata(r));
        }
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT")));
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT MAĞAZA")));

        // Ağustos açılır (Haziran ve Temmuz kilitli kalır); aynı adla yeni bir pasif kanal açılır.
        await AyKilidi(c, Old, ac: true);
        var yeni = await Post<KanalEntity>(c, "/api/kanallar", new KanalYazDto("MEZAT", false, 5));
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Old, "MEZAT", 10m))).EnsureSuccessStatusCode();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var satirlar = db.Gelenler.AsNoTracking().Where(g => g.DonemStart == Old).OrderBy(g => g.Id).ToList();
        Assert.Equal(new[] { ("MEZAT MAĞAZA", 1, 300m), ("MEZAT", yeni.Id, 10m) }, satirlar.Select(g => (g.Kanal, g.KanalId!.Value, g.TutarTl)).ToArray());
    }

    /// <summary>Görüntüsü olmayan kilitli ay (bu sürümden önce kapatılmış; geçiş tohumu raporuna karantinadaki kayıt girdiği için
    /// dondurmamış olabilir) canlı hesaplanır. Önceden kanal kümesini değiştiren her istek 409 alıyordu ("dondurulmamış"). Artık ilk
    /// kanal değişikliğinden önce kilitli ayların kanal kümesi dondurulur (pasif kanal eklemede de): değişiklikler serbesttir ve ayın
    /// canlı raporu (kural 1) birebir aynı kalır; ad değişikliği yalnız adları değiştirir.</summary>
    [Fact]
    public async Task Kilitli_ayin_raporu_dondurulmamissa_da_kanal_degisikligi_serbest_rapor_ayin_kumesiyle_ayni_kalir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old, "Ortak kuruş", .01m, KanalEtiketleri.Ortak, GiderTipi.Cari));
        // Bu sürümden önce kapatılmış ay (görüntüsüz kilit): açılıştaki geçiş tohumu dondurana kadar rapor canlı hesaplanır.
        using (var scope = f.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<KasaDbContext>().Database.ExecuteSql($"UPDATE AyKilidi SET KilitliSonTarih = {OldSonu}, Surum = Surum + 1 WHERE Id = 1");
        // Görüntüsüz kilitli ayın raporu açık ay kuralıyla değil, kilitlendiği sürümün kuralıyla (kural 1) canlı hesaplanır
        // (HesapServisi.AylikYanit, TohumBekliyor): karşılaştırma tabanı kilitten sonraki rapordur.
        var once = await c.GetStringAsync(AylikUrl(Old));
        Assert.Null(JsonNode.Parse(once)!["dondurulmus"]);

        // İlk istek pasif kanal ekler (Ortak kümesi değişmez): yine de kilitli ayların kümesi dondurulur, yeni kanal satır eklemez.
        foreach (var istek in new Func<Task<HttpResponseMessage>>[] { () => c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("PASİF", false)),
                     () => c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("ONLINE")),
                     () => c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", Sira: 99)), () => c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", false)) })
        {
            await Basarili(await istek());
            Assert.Equal(once, await c.GetStringAsync(AylikUrl(Old)));
        }
        using (var scope = f.Services.CreateScope())
            Assert.All(scope.ServiceProvider.GetRequiredService<KasaDbContext>().AyKanalKumeleri.AsNoTracking().ToList(), k => Assert.Equal(AyKanalKumesi.KanalDegisikligi, k.Kaynak));
        await Basarili(await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT MAĞAZA")));
        Assert.Equal(AdlarHaric(once), AdlarHaric(await c.GetStringAsync(AylikUrl(Old))));
    }

    private static async Task Basarili(HttpResponseMessage r)
    {
        using (r)
            Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }

    private static async Task Durum(HttpResponseMessage r, HttpStatusCode beklenen)
    {
        using (r)
            Assert.True(r.StatusCode == beklenen, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
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
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var ay = new DateOnly(tarih.Year, tarih.Month, 1);
        var sablon = new AylikGiderSablonEntity();
        db.AylikGiderSablonlar.Add(sablon);
        db.SaveChanges();
        var revizyon = new AylikGiderRevizyonEntity
        {
            SablonId = sablon.Id,
            Surum = 1,
            Ad = "Depo kirası",
            Tur = "Kira",
            Tutar = tutar,
            OdemeGunu = tarih.Day,
            GecerliAy = ay,
            DagilimTuru = "Ozel",
            DagilimJson = JsonSerializer.Serialize(new[] { new KanalPayYaz(kanalId, tutar) }),
            Aktif = true
        };
        var islem = new IslemEntity
        {
            Tarih = tarih,
            TutarTl = tutar,
            Cari = "Depo kirası",
            Tip = GiderTipi.SabitGider,
            KanalId = kanalId,
            Kanal = db.Kanallar.Single(k => k.Id == kanalId).Ad
        };
        db.AylikGiderRevizyonlar.Add(revizyon);
        db.Islemler.Add(islem);
        db.SaveChanges();
        db.AylikGiderOdemeler.Add(new AylikGiderOdemeEntity { SablonId = sablon.Id, RevizyonId = revizyon.Id, Ay = ay, Tarih = tarih, Tutar = tutar, IslemId = islem.Id });
        db.SaveChanges();
        return islem.Id;
    }

    /// <summary>Aylık raporun kanal adları çıkarılmış hali: yeniden adlandırmada tutarların aynı kaldığını karşılaştırmak için.</summary>
    private static string AdlarHaric(string rapor)
    {
        var json = JsonNode.Parse(rapor)!.AsObject();
        foreach (var satir in json["kanallar"]!.AsArray())
            satir!.AsObject().Remove("kanal");
        return json.ToJsonString();
    }

    /// <summary>Ayın canlı hesaplanan raporu (kilitliyken görüntüye bakmaz; ay açılınca API'nin döneceği rapor), API biçiminde.</summary>
    private static string CanliAylik(IServiceScope scope, DateOnly ay) => JsonSerializer.Serialize(
        scope.ServiceProvider.GetRequiredService<HesapServisi>().Aylik(ay.Year, ay.Month), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>İki aylık rapor kanal bazında aynı: üst düzey alanlar birebir, kapanıştaki her kanalın tutarları (güncel adıyla) aynı,
    /// sonradan eklenen (pasif) kanalın satırı sıfır.</summary>
    private static void KanalBazindaAyni(string once, string sonra, IReadOnlyDictionary<string, string> adlar)
    {
        var (o, s) = (JsonNode.Parse(once)!.AsObject(), JsonNode.Parse(sonra)!.AsObject());
        var sonrakiSatirlar = s["kanallar"]!.AsArray().ToDictionary(k => (string)k!["kanal"]!, k => k!.AsObject());
        foreach (var eski in o["kanallar"]!.AsArray())
            Assert.Equal(AdsizSatir(eski!.AsObject()), AdsizSatir(sonrakiSatirlar[adlar[(string)eski["kanal"]!]]));
        foreach (var yeni in sonrakiSatirlar.Where(p => !adlar.Values.Contains(p.Key)))
            Assert.All(AdsizSatir(yeni.Value).Select(p => p.Value), v => Assert.Equal(0m, v));
        o.Remove("kanallar");
        s.Remove("kanallar");
        Assert.Equal(o.ToJsonString(), s.ToJsonString());
    }

    /// <summary>Haftalık raporun tamamı kilitli aya (ve öncesine) düşen dönemleri.</summary>
    private static List<JsonObject> KilitliHaftalar(string haftalik) => JsonNode.Parse(haftalik)!.AsArray()
        .Select(h => h!.AsObject()).Where(h => DateOnly.Parse((string)h["donem"]!["end"]!) <= OldSonu).ToList();

    private static Dictionary<string, decimal> AdsizSatir(JsonObject satir) =>
        satir.Where(p => p.Key != "kanal").ToDictionary(p => p.Key, p => (decimal)p.Value!);

    private static async Task<EkstreBelgeDto> EkstreBelgesi(KasaWebFactory f, HttpClient c, DateOnly? tarih = null)
    {
        int id;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var belge = new EkstreBelgeEntity
            {
                Kaynak = "Banka",
                Banka = "Akbank",
                HesapAdi = "İş hesabı",
                DosyaAdi = "test.pdf",
                DosyaOzeti = Guid.NewGuid().ToString(),
                Yuklendi = f.Saat!.GetUtcNow().ToUnixTimeMilliseconds(),
                SatirlarJson = JsonSerializer.Serialize(new[] { new EkstreOkunanSatir(1, 1, "Kaynak 1", tarih ?? Today, "Banka hareketi 1", 100m, "Cikis", "Gider", "Hareket", "TRY", []) })
            };
            db.EkstreBelgeler.Add(belge);
            db.SaveChanges();
            id = belge.Id;
        }
        return (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{id}"))!;
    }
}
