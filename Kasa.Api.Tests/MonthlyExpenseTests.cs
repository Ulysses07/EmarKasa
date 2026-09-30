using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

public class AylikGiderTests
{
    // Sunucu saati sabittir (KilitliDonemTests de bu günü ve Fabrika'yı kullanır); test sonucu takvime bağlı değildir.
    internal static DateOnly Today => KasaWebFactory.VarsayilanBugun;
    internal static DateOnly Month => new(Today.Year, Today.Month, 1);
    internal static KasaWebFactory Fabrika() => KasaWebFactory.Sabit(Today);

    [Theory]
    [InlineData("Genel", 0)]
    [InlineData("Esit", 1)]
    [InlineData("Ozel", 2)]
    public async Task Plan_kasayi_degistirmez_manuel_odeme_genel_kasaya_bir_kez_kanallara_secilen_payla_yansir(string mode, int variant)
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var template = await Create(c, mode, variant == 0 ? [] : variant == 1 ? [new(1, 0), new(2, 0), new(3, 0)] : [new(1, 70m), new(2, 30m)]);
        var plan = (await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={Today.Year}&ay={Today.Month}"))!;
        Assert.Equal(100m, plan.PlanlananToplam);
        Assert.Equal(0m, plan.OdenenToplam);
        Assert.Equal(1000m, (await Panel(c)).GuncelKasa);
        var request = Payment(template);
        var paid = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{template.Id}/ode", request);
        Assert.Equal("Odendi", paid.Durum);
        var result = await Panel(c);
        Assert.Equal(900m, result.GuncelKasa);
        Assert.Equal(-100m, result.BuAySonucu);
        Assert.Equal(0m, result.DagilimBekleyenTutar);
        Assert.Equal(variant == 0 ? 0m : -100m, result.Kanallar.Sum(k => k.Bakiye));
        if (variant == 1)
            Assert.Equal(new[] { -33.34m, -33.33m, -33.33m }, result.Kanallar.Select(k => k.Bakiye));
        if (variant == 2)
            Assert.Equal(-70m, result.Kanallar.Single(k => k.KanalId == 1).Bakiye);
        var replay = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{template.Id}/ode", request);
        Assert.Equal(paid.OdemeId, replay.OdemeId);
        Assert.Equal(900m, (await Panel(c)).GuncelKasa);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/aylik-giderler/{template.Id}/ode", request with { IstekId = Guid.NewGuid() })).StatusCode);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter());
        var expense = Assert.Single((await c.GetFromJsonAsync<List<IslemOkuDto>>("/api/islemler", json))!);
        var paidMonth = (await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={Today.Year}&ay={Today.Month}"))!;
        Assert.Equal(100m, paidMonth.PlanlananToplam);
        Assert.Equal(100m, paidMonth.OdenenToplam);
        Assert.Equal(paid.OdemeId, expense.AylikGiderOdemeId);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/islemler/{expense.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/islemler/{expense.Id}", new IslemYazDto(Today, "Değişiklik", 200, "MEZAT", GiderTipi.SabitGider))).StatusCode);
        Assert.Equal(900m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Sablon_degisse_ve_pasife_alinsa_bile_odeme_kopyasi_korunur_iptal_yeniden_odeme_tek_kaydi_yaratir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var template = await Create(c, "Ozel", [new(1, 100m)]);
        var paid = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{template.Id}/ode", Payment(template));
        var updated = await c.PutAsJsonAsync($"/api/aylik-giderler/sablonlar/{template.Id}", new AylikGiderSablonYaz(Guid.NewGuid(), template.Surum, "Yeni kira", "Kira", 200m, 31, "Genel", [], Month));
        updated.EnsureSuccessStatusCode();
        template = (await updated.Content.ReadFromJsonAsync<AylikGiderSablonDto>())!;
        var old = Assert.Single((await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={Month.Year}&ay={Month.Month}"))!.Kayitlar);
        Assert.Equal(100m, old.Tutar);
        Assert.Equal("Kira", old.Ad);
        Assert.Equal(1, Assert.Single(old.Dagilimlar).KanalId);
        var cancelled = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/odemeler/{paid.OdemeId}/iptal", new AylikGiderIptalYaz(Guid.NewGuid(), "Hatalı ödeme"));
        Assert.Equal("Iptal", cancelled.Durum);
        Assert.Equal(1000m, (await Panel(c)).GuncelKasa);
        var next = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{template.Id}/ode", Payment(template));
        Assert.Equal(200m, next.Tutar);
        Assert.NotEqual(paid.OdemeId, next.OdemeId);
        Assert.Equal(800m, (await Panel(c)).GuncelKasa);
        var archive = await c.PutAsJsonAsync($"/api/aylik-giderler/sablonlar/{template.Id}", new AylikGiderSablonYaz(Guid.NewGuid(), template.Surum, "Arşiv", "Kira", 300m, 1, "Genel", [], Month, false));
        archive.EnsureSuccessStatusCode();
        Assert.Equal(200m, Assert.Single((await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={Month.Year}&ay={Month.Month}"))!.Kayitlar).Tutar);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Equal(2, db.AylikGiderOdemeler.Count());
        Assert.Single(db.Islemler);
        Assert.Equal(3, db.AylikGiderRevizyonlar.Count());
    }

    [Fact]
    public async Task Ileri_ay_revizyonu_onceki_planlari_degistirmez_ve_kisa_ay_odemesi_son_gune_uyarlanir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var template = await Create(c, "Genel", []);
        // İlk kısa ileri ay (varsayılan günde Kasım): 31'inci gün ödemesi ayın son gününe uyarlanır.
        var nextMonth = Month.AddMonths(1);
        while (DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month) == 31)
            nextMonth = nextMonth.AddMonths(1);
        var update = await c.PutAsJsonAsync($"/api/aylik-giderler/sablonlar/{template.Id}", new AylikGiderSablonYaz(Guid.NewGuid(), template.Surum, "Yeni", "Maas", 250m, 31, "Genel", [], nextMonth));
        update.EnsureSuccessStatusCode();
        Assert.Equal(100m, Assert.Single((await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={Month.Year}&ay={Month.Month}"))!.Kayitlar).Tutar);
        var future = Assert.Single((await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={nextMonth.Year}&ay={nextMonth.Month}"))!.Kayitlar);
        Assert.Equal(250m, future.Tutar);
        Assert.Equal(DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month), future.PlanlananTarih.Day);
        Assert.Equal(1000m, (await Panel(c)).GuncelKasa);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/aylik-giderler/sablonlar", new AylikGiderSablonYaz(Guid.NewGuid(), 0, "Eski", "Kira", 100m, 1, "Genel", [], Month.AddMonths(-1)))).StatusCode);
    }

    [Fact]
    public async Task Dagilim_kimlikleri_sabittir_yeni_kanal_eklemek_ve_pasiflik_odeme_paylarini_degistirmez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var template = await Create(c, "Esit", [new(1, 0), new(2, 0)]);
        (await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", false))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/kanallar", new KanalYazDto("Yeni kanal"))).EnsureSuccessStatusCode();
        var paid = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{template.Id}/ode", Payment(template));
        Assert.Equal(new[] { 1, 2 }, paid.Dagilimlar.Select(p => p.KanalId!.Value));
        Assert.All(paid.Dagilimlar, p => Assert.Equal(50m, p.Tutar));
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync("/api/kanallar/2")).StatusCode);
    }

    [Fact]
    public async Task Aylik_odeme_ayri_alisa_baglanamaz_ve_alici_erisemez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var t = await Create(c, "Genel", []);
        var paid = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{t.Id}/ode", Payment(t));
        var purchase = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Today, "Satıcı", null, [new("Mal", 100, [new(1, 100)])]));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/alis/{purchase.Id}/odemeler", new AlisOdemeYaz(purchase.Surum, Guid.NewGuid(), Today, 100, MevcutIslemId: paid.IslemId))).StatusCode);
        var buyer = await Post<AliciDto>(c, "/api/alicilar", new AliciYaz("aylik-alici", "Alıcı", "alici12345"));
        using var b = f.CreateClient();
        (await b.PostAsJsonAsync("/api/auth/login", new { kullanici = buyer.Kullanici, sifre = "alici12345" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("/api/aylik-giderler/sablonlar")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("/api/ay-kilidi")).StatusCode);
    }

    [Fact]
    public async Task Ayni_odeme_eszamanli_farkli_baglantilardan_tekrarlansa_bir_nakit_cikisi_olusur()
    {
        var path = Path.Combine(Path.GetTempPath(), "kasa-monthly-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using var f = new AylikDosyaFabrikasi(path) { Saat = new SabitSaat(Today) };
            using var c = await Editor(f);
            var t = await Create(c, "Genel", []);
            var request = Payment(t);
            using var start = new Barrier(3);
            var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
            { Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10))); return await c.PostAsJsonAsync($"/api/aylik-giderler/{t.Id}/ode", request); })));
            foreach (var r in results)
            { Assert.Equal(HttpStatusCode.OK, r.StatusCode); r.Dispose(); }
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.Single(db.AylikGiderOdemeler);
            Assert.Single(db.Islemler);
            Assert.Equal(900m, (await Panel(c)).GuncelKasa);
        }
        finally { foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(path + suffix); }
    }

    [Fact]
    public async Task Bankadan_islenen_genel_gider_aylik_odeme_benzerliginde_gorunur_odenen_aylik_gider_de_manuel_gider_sorgusunda()
    {
        // gap-coklu-giris-cift-sayim-mutabakat-8: kira bankadan "Yalnız genel kasa" gideri olarak işlendikten iki gün sonra
        // aynı tutar Aylık Giderler'den ödenmek istenir. İstemci ödeme öncesi aynı benzerlik ucunu 'AylikGider' türüyle sorar.
        await using var f = Fabrika();
        using var c = await Editor(f);
        var t = await Create(c, "Esit", [new(1, 0), new(3, 0)]);
        var (_, satir) = await BenzerKayitCaprazTests.EkstreGideri(f, c, Today.AddDays(-2), 100m, "Genel", []);
        var benzer = Assert.Single(await BenzerKayitCaprazTests.Bul(c, new("AylikGider", Today, 100m)));
        Assert.Equal(("Islem", satir.IslemId, (int?)satir.Id, "Genel kasa"), (benzer.Kaynak, (int?)benzer.Id, benzer.EkstreKayitId, benzer.KanalEtiketi));
        Assert.Empty(await BenzerKayitCaprazTests.Bul(c, new("AylikGider", Today, 100.01m)));
        // Uyarı onaylanıp ayrı ödeme kaydedilebilir (benzerlik bir uyarıdır, yasak değildir).
        var paid = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{t.Id}/ode", Payment(t) with { BenzerOnay = true });
        Assert.Equal(800m, (await Panel(c)).GuncelKasa);
        // Ters yön: MEZAT için elle girilecek aynı tutar, çok kanallı aylık ödemeyi ve genel kasa banka giderini birlikte gösterir.
        var rows = await BenzerKayitCaprazTests.Bul(c, new("Gider", Today.AddDays(1), 100m, Kanal: "MEZAT"));
        Assert.Equal(2, rows.Count);
        var monthly = Assert.Single(rows, r => r.AylikGiderOdemeId == paid.OdemeId);
        Assert.Equal((paid.IslemId, "MEZAT / TOPTAN"), ((int?)monthly.Id, monthly.KanalEtiketi));
        Assert.Single(rows, r => r.EkstreKayitId == satir.Id);
        // Kanalı kesişmeyen sorgu çok kanallı ödemeyi göstermez; genel kasa gideri her kanal sorgusunda görünür.
        Assert.Equal(satir.Id, Assert.Single(await BenzerKayitCaprazTests.Bul(c, new("Gider", Today, 100m, Kanal: "PERAKENDE"))).EkstreKayitId);
    }

    /// <summary>Ödeme ucunun benzer kayıt yanıtı (409): okunur ileti ve istemcinin onay panelinde göstereceği kayıtlar.</summary>
    private sealed record BenzerCakismasi(string Hata, List<BenzerKayitDto> Benzerler);

    [Fact]
    public async Task Benzer_onayi_isteyen_odeme_bankadan_islenmis_kirada_409_ve_liste_doner_onayla_bir_kez_kaydedilir()
    {
        // gap-coklu-giris-cift-sayim-mutabakat-8 senaryosu: kira bankadan "Yalnız genel kasa" gideri olarak işlendi; iki gün
        // sonra Aylık Giderler'den "Ödendi" denir. BenzerOnay=false gönderen istemcide ödeme ucu benzerliği yazma
        // transaction'ında denetler: benzer kayıt varsa hiçbir şey yazmaz, 409 ile kayıtları döner.
        await using var f = Fabrika();
        using var c = await Editor(f);
        var t = await Create(c, "Genel", []);
        var (_, satir) = await BenzerKayitCaprazTests.EkstreGideri(f, c, Today.AddDays(-2), 100m, "Genel", []);
        var istek = Payment(t) with { BenzerOnay = false };
        var cakisma = await Cakisma(c, t, istek);
        var benzer = Assert.Single(cakisma.Benzerler);
        Assert.Equal(("Islem", satir.IslemId, (int?)satir.Id, "Genel kasa"), (benzer.Kaynak, (int?)benzer.Id, benzer.EkstreKayitId, benzer.KanalEtiketi));
        Assert.Contains($"Banka gideri #{satir.IslemId} · {Today.AddDays(-2):dd.MM.yyyy} · 100,00 TL · Genel kasa", cakisma.Hata);
        Assert.Equal(900m, (await Panel(c)).GuncelKasa);
        Assert.Equal("Planlandi", Assert.Single((await Ay(c)).Kayitlar).Durum);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.Empty(db.AylikGiderOdemeler);
            Assert.Single(db.Islemler);
            Assert.Empty(db.FinansIstekler.Where(i => i.Tur == "AylikGiderOdeme"));
        }
        // Açık protokolde onay yalnız BenzerOnay=true'dur: aynı isteğin onaysız yeniden gönderimi yine 409 alır.
        Assert.Single((await Cakisma(c, t, istek)).Benzerler);
        Assert.Equal(900m, (await Panel(c)).GuncelKasa);
        // Kullanıcı benzer kaydı gördü ve ayrı ödeme olduğunu onayladı: reddedilen istek kimliği tüketmediğinden aynı kimlikle kaydedilir.
        var paid = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{t.Id}/ode", istek with { BenzerOnay = true });
        Assert.Equal("Odendi", paid.Durum);
        Assert.Equal(800m, (await Panel(c)).GuncelKasa);
        // Onay alanı özete girmez: onaylı isteğin ve onaysız biçiminin tekrarı aynı ödemeyi döner, ödemenin kendi gideri 409'a yol açmaz.
        Assert.Equal(paid.OdemeId, (await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{t.Id}/ode", istek with { BenzerOnay = true })).OdemeId);
        Assert.Equal(paid.OdemeId, (await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{t.Id}/ode", istek)).OdemeId);
        Assert.Equal(800m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Benzer_denetimi_sablonun_kanal_kumesiyle_yapilir_dogrulama_once_gelir_eski_istemci_uyarilip_yeniden_gonderince_oder()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        // Kanalı kesin olarak başka olan (PERAKENDE) elle gider, MEZAT / TOPTAN şablonunun ödemesine benzemez.
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Today.AddDays(-1), "Perakende gideri", 100m, "PERAKENDE", GiderTipi.Cari))).EnsureSuccessStatusCode();
        var cok = await Create(c, "Esit", [new(1, 0), new(3, 0)]);
        var cokOdeme = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{cok.Id}/ode", Payment(cok) with { BenzerOnay = false });
        Assert.Equal("Odendi", cokOdeme.Durum);
        // Genel şablon kanal ayırmaz: PERAKENDE gideri ve MEZAT / TOPTAN aylık ödemesi birlikte listelenir.
        var genel = await Post<AylikGiderSablonDto>(c, "/api/aylik-giderler/sablonlar", new AylikGiderSablonYaz(Guid.NewGuid(), 0, "Elektrik", "Fatura", 100m, 31, "Genel", [], Month));
        var cakisma = await Cakisma(c, genel, Payment(genel) with { BenzerOnay = false });
        Assert.Equal(new[] { cokOdeme.OdemeId }, cakisma.Benzerler.Where(b => b.AylikGiderOdemeId is not null).Select(b => b.AylikGiderOdemeId));
        Assert.Equal(2, cakisma.Benzerler.Count);
        Assert.Contains("Aylık gider ödemesi #", cakisma.Hata);
        Assert.Contains("Gider #", cakisma.Hata);
        // Doğrulama benzerlikten önce gelir: geçersiz tarih 400, eski şablon sürümü benzer listesi olmadan 409 döner.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/aylik-giderler/{genel.Id}/ode", Payment(genel) with { BenzerOnay = false, Tarih = Today.AddDays(1) })).StatusCode);
        var eskiSurum = await c.PostAsJsonAsync($"/api/aylik-giderler/{genel.Id}/ode", Payment(genel) with { BenzerOnay = false, Surum = genel.Surum + 1 });
        Assert.Equal(HttpStatusCode.Conflict, eskiSurum.StatusCode);
        Assert.False((await eskiSurum.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("benzerler", out _));
        // Onay alanını bilmeyen istemci (web ve MAUI'nin bugünkü gövdesi) de aynı denetimle uyarılır; aynı isteği yeniden
        // gönderince öder. Doğrulama burada da önce gelir.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/aylik-giderler/{genel.Id}/ode", EskiGovde(genel, Guid.NewGuid(), Today.AddDays(1)))).StatusCode);
        var eskiGovde = EskiGovde(genel, Guid.NewGuid());
        Assert.Equal(2, (await Cakisma(c, genel, eskiGovde)).Benzerler.Count);
        Assert.Equal(800m, (await Panel(c)).GuncelKasa);
        var eski = await c.PostAsJsonAsync($"/api/aylik-giderler/{genel.Id}/ode", eskiGovde);
        Assert.Equal(HttpStatusCode.OK, eski.StatusCode);
        Assert.Equal(700m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Onay_alanini_bilmeyen_istemci_bankadan_islenmis_kirada_uyarilir_ayni_istegi_yeniden_gonderince_bir_kez_kaydedilir()
    {
        // gap-coklu-giris-cift-sayim-mutabakat-8'in kullanıcı senaryosu bugünkü web ve MAUI gövdesiyle (BenzerOnay alanı yok):
        // kira bankadan "Yalnız genel kasa" gideri olarak işlendi; iki gün sonra Aylık Giderler'den "Ödendi" denir. İlk istek
        // hiçbir şey yazmadan 409 ile uyarılır; iki istemci de sunucunun 'hata' iletisini ödeme formunda gösterir ve formu açık
        // tutar. Değişmeyen gövdeye aynı istek kimliği verilir (web requestIdentity, MAUI TekrarAnahtari): kullanıcı uyarıyı
        // görüp aynı ödemeyi yeniden kaydederse ikinci istek onay sayılır.
        await using var f = Fabrika();
        using var c = await Editor(f);
        var t = await Create(c, "Genel", []);
        var (_, satir) = await BenzerKayitCaprazTests.EkstreGideri(f, c, Today.AddDays(-2), 100m, "Genel", []);
        var govde = EskiGovde(t, Guid.NewGuid());
        var cakisma = await Cakisma(c, t, govde);
        var benzer = Assert.Single(cakisma.Benzerler);
        Assert.Equal(("Islem", satir.IslemId, (int?)satir.Id, "Genel kasa"), (benzer.Kaynak, (int?)benzer.Id, benzer.EkstreKayitId, benzer.KanalEtiketi));
        Assert.StartsWith($"Aynı tutarda yakın tarihli kayıt var: Banka gideri #{satir.IslemId} · {Today.AddDays(-2):dd.MM.yyyy} · 100,00 TL · Genel kasa.", cakisma.Hata, StringComparison.Ordinal);
        Assert.Contains("Ayrı bir ödemeyse bilgileri değiştirmeden ödemeyi yeniden kaydedin.", cakisma.Hata);
        Assert.Equal(900m, (await Panel(c)).GuncelKasa);
        Assert.Equal("Planlandi", Assert.Single((await Ay(c)).Kayitlar).Durum);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            Assert.Empty(db.AylikGiderOdemeler);
            Assert.Single(db.Islemler);
            Assert.Empty(db.FinansIstekler.Where(i => i.Tur == "AylikGiderOdeme"));
        }
        // Kullanıcı uyarıyı gördü, ayrı ödeme olduğundan emin: aynı gövdeyi yeniden gönderir.
        var paid = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{t.Id}/ode", govde);
        Assert.Equal("Odendi", paid.Durum);
        Assert.Equal(800m, (await Panel(c)).GuncelKasa);
        // Kaydedilmiş isteğin tekrarı (ör. yanıt kaybolduktan sonra) aynı ödemeyi döner; ikinci gider oluşmaz.
        Assert.Equal(paid.OdemeId, (await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{t.Id}/ode", govde)).OdemeId);
        Assert.Equal(800m, (await Panel(c)).GuncelKasa);
        // Benzer kayıt yoksa onay alanını bilmeyen istemci tek istekte öder (uyarı yalnız benzer kayıt varken verilir).
        var elektrik = await Post<AylikGiderSablonDto>(c, "/api/aylik-giderler/sablonlar", new AylikGiderSablonYaz(Guid.NewGuid(), 0, "Elektrik", "Fatura", 55m, 31, "Genel", [], Month));
        Assert.Equal("Odendi", (await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{elektrik.Id}/ode", EskiGovde(elektrik, Guid.NewGuid()))).Durum);
    }

    [Fact]
    public async Task Yeniden_gonderim_yalniz_ayni_icerik_ve_gorulmus_kayitlar_icin_onaydir_uyari_otuz_dakika_gecerlidir()
    {
        var saat = new IlerleyenSaat(Today);
        await using var f = new KasaWebFactory { Saat = saat };
        using var c = await Editor(f);
        var t = await Create(c, "Genel", []);
        async Task Gider(DateOnly tarih) => (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(tarih, "Elle kira", 100m, "MEZAT", GiderTipi.Cari))).EnsureSuccessStatusCode();
        await Gider(Today.AddDays(-1));
        var id = Guid.NewGuid();
        Assert.Single((await Cakisma(c, t, EskiGovde(t, id))).Benzerler);
        // Uyarıdan sonra girilen benzer kayıt: yeniden gönderim onu da gösterir; kullanıcı görmediği kayda onay vermiş sayılmaz.
        await Gider(Today);
        var yeni = await Cakisma(c, t, EskiGovde(t, id));
        Assert.Equal(2, yeni.Benzerler.Count);
        // Aynı kimlik farklı içerikle (başka ödeme tarihi) gelirse o içerik ayrıca uyarılır; önceki içeriğin uyarısı da geçersizleşir.
        Assert.Equal(2, (await Cakisma(c, t, EskiGovde(t, id, Today.AddDays(-1)))).Benzerler.Count);
        Assert.Equal(2, (await Cakisma(c, t, EskiGovde(t, id))).Benzerler.Count);
        // Uyarı 30 dakika geçerlidir: 29 dakika sonra aynı istek onaydır.
        saat.Ilerlet(TimeSpan.FromMinutes(29));
        Assert.Equal("Odendi", (await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{t.Id}/ode", EskiGovde(t, id))).Durum);
        Assert.Equal(700m, (await Panel(c)).GuncelKasa);

        // Süresi geçen uyarı onay sayılmaz: 31 dakika sonra aynı istek yeniden uyarılır, ardından yeniden gönderimle kaydedilir.
        var fatura = await Post<AylikGiderSablonDto>(c, "/api/aylik-giderler/sablonlar", new AylikGiderSablonYaz(Guid.NewGuid(), 0, "Fatura", "Fatura", 100m, 31, "Genel", [], Month));
        var ikinci = Guid.NewGuid();
        Assert.Equal(3, (await Cakisma(c, fatura, EskiGovde(fatura, ikinci))).Benzerler.Count);
        saat.Ilerlet(TimeSpan.FromMinutes(31));
        Assert.Equal(3, (await Cakisma(c, fatura, EskiGovde(fatura, ikinci))).Benzerler.Count);
        Assert.Equal("Odendi", (await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{fatura.Id}/ode", EskiGovde(fatura, ikinci))).Durum);
        Assert.Equal(600m, (await Panel(c)).GuncelKasa);
    }

    /// <summary>Bugünkü web ve MAUI istemcisinin ödeme gövdesi: BenzerOnay alanı yoktur.</summary>
    private static object EskiGovde(AylikGiderSablonDto t, Guid istekId, DateOnly? tarih = null) =>
        new { istekId, surum = t.Surum, yil = Month.Year, ay = Month.Month, tarih = tarih ?? Today, not = (string?)null };

    /// <summary>Test saati: <see cref="SabitSaat"/> gibi durmuştur, test dakika dakika ilerletebilir.</summary>
    private sealed class IlerleyenSaat(DateOnly gun) : TimeProvider
    {
        private long _utcTicks = new SabitSaat(gun).GetUtcNow().UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
        public void Ilerlet(TimeSpan sure) => Interlocked.Add(ref _utcTicks, sure.Ticks);
    }

    private static async Task<BenzerCakismasi> Cakisma(HttpClient c, AylikGiderSablonDto t, object istek)
    {
        var r = await c.PostAsJsonAsync($"/api/aylik-giderler/{t.Id}/ode", istek);
        Assert.True(r.StatusCode == HttpStatusCode.Conflict, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<BenzerCakismasi>())!;
    }
    private static async Task<AylikGiderAyDto> Ay(HttpClient c) => (await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={Month.Year}&ay={Month.Month}"))!;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Aylik_gider_okumalari_yazma_kilidi_almaz_ve_paralel_yazma_beklemez(bool ayListesi)
    {
        // Ay listesi şablonun geçerli olduğu ayı (sabit saatin ayı) okur: saat değişirse sorgu da onunla değişir.
        var uc = ayListesi ? $"/api/aylik-giderler?yil={Month.Year}&ay={Month.Month}" : "/api/aylik-giderler/sablonlar";
        var kapi = new OkumaYoluTests.OkumaKapisi();
        await using var f = new DosyaFabrikasi { Kesiciler = [kapi] };
        using var c = await Editor(f);
        await Create(c, "Genel", []);
        kapi.Kur("AylikGiderRevizyonlar");
        var okuma = c.GetAsync(uc);
        Assert.True(await kapi.Girildi(), "Okuma aylık gider tablosuna ulaşmadı.");
        HttpResponseMessage yazma;
        var sure = System.Diagnostics.Stopwatch.StartNew();
        try
        { yazma = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Today, "Okuma sırasında", 25m, "MEZAT", GiderTipi.Cari)); }
        finally { sure.Stop(); kapi.Birak(); }
        var yanit = await okuma;
        Assert.Equal(HttpStatusCode.Created, yazma.StatusCode);
        Assert.True(sure.Elapsed < TimeSpan.FromSeconds(3), $"Yazma okumayı {sure.ElapsedMilliseconds} ms bekledi.");
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        // Okuma yolunda da doğrulama hatası aynı biçimde döner.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/aylik-giderler?yil={Month.Year}&ay=13")).StatusCode);
    }

    internal sealed class AylikDosyaFabrikasi(string path) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(o => o.UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString()));
            });
        }
    }
    internal static async Task<HttpClient> Editor(KasaWebFactory f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Month.AddMonths(-3), kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
        return c;
    }
    internal static Task<AylikGiderSablonDto> Create(HttpClient c, string mode, IReadOnlyList<KanalPayYaz> shares) => Post<AylikGiderSablonDto>(c, "/api/aylik-giderler/sablonlar", new AylikGiderSablonYaz(Guid.NewGuid(), 0, "Kira", "Kira", 100m, 31, mode, shares, Month));
    internal static AylikGiderOdemeYaz Payment(AylikGiderSablonDto t) => new(Guid.NewGuid(), t.Surum, Month.Year, Month.Month, Today);
    internal static async Task<T> Post<T>(HttpClient c, string path, object body)
    {
        var r = await c.PostAsJsonAsync(path, body);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter());
        return (await r.Content.ReadFromJsonAsync<T>(json))!;
    }
    internal static async Task<PanelDto> Panel(HttpClient c) => (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!;
}
