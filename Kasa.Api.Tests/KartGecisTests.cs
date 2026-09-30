using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kasa.Api.Tests;

/// <summary>
/// Eski karttan yeni takibe geçiş (finance-1). Muhasebe senaryoları sabit ve geçmiş
/// tarihlidir: bütün etki tarihleri geride kaldığından sonuç takvimden bağımsızdır.
/// Uç (önizleme/onay) senaryoları sunucunun sabit "bugün"üne göre kurulur (geçiş tarihi
/// kuralı gereği bugün veya sonrası olmalıdır) ve takvim sınırı günlerinde de koşar.
/// </summary>
public class KartGecisTests
{
    private static DateOnly Today => KasaWebFactory.VarsayilanBugun;
    private static KasaWebFactory Factory() => KasaWebFactory.Sabit(Today);
    // Bugüne göre kurulan uç senaryoları varsayılan günde ve takvim sınırlarında (yıl başı, artık yılın
    // Şubat sonu, önceki ayı kısa olan ay sonu) koşar.
    public static TheoryData<int, int, int> TakvimSinirlari => new()
    {
        { Today.Year, Today.Month, Today.Day }, { 2027, 1, 1 }, { 2028, 2, 29 }, { 2027, 3, 31 },
    };
    private static DateOnly AySonu(DateOnly d) => new(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));

    [Fact]
    public async Task Bankaya_onceden_odenmis_bekleyen_eski_gider_geciste_kaybolmaz_bir_kez_duser()
    {
        await using var f = Factory();
        using var c = await Editor(f, new(2025, 1, 1));
        // Kesim 5, son ödeme 15: 3 Ağustos gideri 15 Ağustos'ta bankaya ödendi, eski kural 30 Eylül'de düşer.
        var id = EskiKart(f, 0m, (new(2025, 8, 3), 1000m, 1));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.KartOdemeler.Add(new KartOdemeEntity { KrediKartiId = id, Tarih = new(2025, 8, 15), Tutar = 1000m });
            db.SaveChanges();
        }
        var before = await Raporlar(c, 2025, 8, 9, 10);
        Assert.Equal(0m, await Cash(c));

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var ozet = KartGecisHesabi.Hesapla(db, id, new(2025, 9, 20), 0m);
            Assert.Equal(new KartGecisOzeti(0m, 0m, 1000m, new(2025, 9, 30), 0m), ozet);
            FinansTakipServisi.KartGecisiYaz(db, id, new(2025, 9, 20), 0m, ozet.OnerilenKasadaSayilanTutar, [], " Banka borcu sıfır ");
        }

        Assert.Equal(before, await Raporlar(c, 2025, 8, 9, 10));
        var gecis = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{id}"))!.Gecis!;
        Assert.Equal(("IslemTarihi", "Banka borcu sıfır", 0m, (DateOnly?)null, (string?)null), (gecis.Kural, gecis.Aciklama, gecis.RaporDisiEskiDusumTutari, gecis.RaporDisiSonDusumTarihi, gecis.Uyari));
        Assert.Equal(new KartGecisKaydi(Today, 0m, 0m, 0m, 0m, 1000m, new(2025, 9, 30), 0m), gecis.Onizleme);
        var eylul = (await c.GetFromJsonAsync<AylikRapor>("/api/rapor/aylik?yil=2025&ay=9"))!;
        Assert.Equal(1000m, eylul.Kanallar.Single(k => k.Kanal == "MEZAT").KrediKarti);
        Assert.Equal(0m, await Cash(c));
    }

    [Fact]
    public async Task Bulgu_senaryosu_onerilen_tutarla_gecis_ay_sonu_dusumunu_bir_kez_yapar_odeme_ikinci_kez_dusurmez()
    {
        await using var f = Factory();
        using var c = await Editor(f, new(2025, 1, 1));
        var id = EskiKart(f, 0m, (new(2025, 9, 10), 1000m, 1));
        var before = await Raporlar(c, 2025, 9, 10, 11);
        Assert.Equal(0m, await Cash(c));

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var ozet = KartGecisHesabi.Hesapla(db, id, new(2025, 9, 25), 1000m);
            Assert.Equal(new KartGecisOzeti(1000m, 0m, 1000m, new(2025, 10, 31), 1000m), ozet);
            FinansTakipServisi.KartGecisiYaz(db, id, new(2025, 9, 25), 1000m, ozet.OnerilenKasadaSayilanTutar, [new(1, 1000m)], "Bulgu senaryosu");
        }
        var card = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{id}"))!;
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, new(2025, 10, 5), 1000m));
        Assert.Equal(0m, Assert.Single(card.Odemeler).KasaEtkisi);
        Assert.Equal(0m, card.Borc);

        // 31 Ekim düşümü korunur, 5 Ekim ödemesi ikinci kez düşmez: kasa tam bir kez 1.000 TL azalır.
        Assert.Equal(before, await Raporlar(c, 2025, 9, 10, 11));
        Assert.Equal(0m, await Cash(c));
    }

    [Fact]
    public async Task Eski_kuralla_dusulmus_borcun_odemesi_onerilen_tutarla_ikinci_kez_dusmez()
    {
        await using var f = Factory();
        using var c = await Editor(f, new(2025, 1, 1));
        var id = EskiKart(f, 0m, (new(2025, 8, 25), 1000m, 1));
        var before = await Raporlar(c, 2025, 9, 10);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var ozet = KartGecisHesabi.Hesapla(db, id, new(2025, 10, 1), 1000m);
            Assert.Equal(new KartGecisOzeti(1000m, 1000m, 0m, null, 1000m), ozet);
            FinansTakipServisi.KartGecisiYaz(db, id, new(2025, 10, 1), 1000m, ozet.OnerilenKasadaSayilanTutar, [new(1, 1000m)], "Ters yön");
        }
        var card = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{id}"))!;
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, new(2025, 10, 10), 1000m));
        Assert.Equal(0m, Assert.Single(card.Odemeler).KasaEtkisi);
        Assert.Equal(before, await Raporlar(c, 2025, 9, 10));
        Assert.Equal(0m, await Cash(c));
    }

    [Fact]
    public void Onerilen_tutar_kalan_borc_ile_sistem_borcunun_kucugudur_acilis_borcu_dahil()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = Context(connection);
        KasaVeritabaniBaslatici.Baslat(db);
        db.Kanallar.Add(new KanalEntity { Id = 1, Ad = "MEZAT" });
        db.KrediKartlari.Add(new KrediKartiEntity { Id = 1, Ad = "Eski", KesimTarihi = new(2000, 1, 5), SonOdemeTarihi = new(2000, 1, 15), Borc = 200m });
        db.Islemler.AddRange(Gider(1, new(2025, 7, 10), 300m, 1), Gider(2, new(2025, 8, 20), 500m, 1), Gider(3, new(2025, 9, 2), -50m, 1),
            new IslemEntity { Id = 4, Tarih = new(2025, 9, 3), Cari = "Kartla cari", TutarTl = 40m, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.Cari, KrediKartiId = 1 });
        db.KartOdemeler.Add(new KartOdemeEntity { KrediKartiId = 1, Tarih = new(2025, 8, 15), Tutar = 300m });
        db.SaveChanges();
        // Sistem borcu = açılış 200 + giderler 790 − ödeme 300. Eski etki: 31 Ağu (300), 30 Eyl (500),
        // 31 Eki (−50 iade); kart bağlı cari gider kendi tarihinde (3 Eyl) düşmüştür.
        Assert.Equal(new KartGecisOzeti(690m, 340m, 450m, new(2025, 10, 31), 690m), KartGecisHesabi.Hesapla(db, 1, new(2025, 9, 10), 1000m));
        Assert.Equal(400m, KartGecisHesabi.Hesapla(db, 1, new(2025, 9, 10), 400m).OnerilenKasadaSayilanTutar);
        Assert.Equal(0m, KartGecisHesabi.Hesapla(db, 1, new(2025, 9, 10), -20m).OnerilenKasadaSayilanTutar);
        Assert.Equal(new KartGecisOzeti(690m, 790m, 0m, null, 690m), KartGecisHesabi.Hesapla(db, 1, new(2025, 11, 1), 700m));
        // İleri başlangıçta "başlangıçtan önce eski kuralla düşen/düşecek" tutarın 30 Eylül (500) ve
        // 31 Ekim (−50) etkileri bugünden (15 Eylül) sonra, başlangıçtan önce düşecektir.
        Assert.Equal(new KartGecisOzeti(690m, 790m, 0m, null, 690m, 450m), KartGecisHesabi.Hesapla(db, 1, new(2025, 11, 1), 700m, new(2025, 9, 15)));
        Assert.Equal(0m, KartGecisHesabi.Hesapla(db, 1, new(2025, 11, 1), 700m, new(2025, 10, 31)).BaslangicaKadarDusecekTutar);
        // K alt sınırı: önerilenin altı ödemede ikinci kez düşer; yalnız açılış borcu kadar altı seçilebilir.
        Assert.Equal(490m, KartGecisHesabi.EnAzKasadaSayilanTutar(KartGecisHesabi.Hesapla(db, 1, new(2025, 9, 10), 1000m), 200m));
        Assert.Equal(0m, KartGecisHesabi.EnAzKasadaSayilanTutar(KartGecisHesabi.Hesapla(db, 1, new(2025, 9, 10), 150m), 200m));
        Assert.Equal(150m, KartGecisHesabi.EnAzKasadaSayilanTutar(KartGecisHesabi.Hesapla(db, 1, new(2025, 9, 10), 150m), -20m));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [MemberData(nameof(TakvimSinirlari))]
    public async Task Onizleme_bekleyen_eski_dusumu_ve_onerilen_tutari_gosterir_onayli_geciste_rapor_degismez(int yil, int ay, int gun)
    {
        var bugun = new DateOnly(yil, ay, gun);
        var gider = new DateOnly(bugun.Year, bugun.Month, 1).AddMonths(-1);
        await using var f = KasaWebFactory.Sabit(bugun);
        using var c = await Editor(f, gider);
        var id = EskiKart(f, 0m, (gider, 1000m, 1));
        var before = await Raporlar(c, bugun.Year, bugun.Month);
        var request = new KartGecisYaz(Guid.NewGuid(), 0, bugun, 1000m, 1000m, [new(1, 1000m)], "Bekleyen düşüm doğrulandı", false);
        var preview = await Post<TakipGecisDto>(c, $"/api/takip/kartlar/{id}/gecis-onizleme", request);
        Assert.Equal(new KartGecisOzeti(1000m, 0m, 1000m, AySonu(bugun), 1000m), new KartGecisOzeti(preview.SistemKartBorcu!.Value, preview.EskiKuraldaIslenenTutar!.Value,
            preview.BekleyenEskiDusumTutari!.Value, preview.SonBekleyenDusumTarihi, preview.OnerilenKasadaSayilanTutar!.Value));
        Assert.Equal((0m, 0m, true), (preview.GenelKasaAnlikFarki, preview.KanalAnlikFarki, preview.KabulEdilebilir));
        Assert.Contains(preview.Aciklamalar, a => a.Contains("1.000,00 TL eski kart gideri") && a.Contains(AySonu(bugun).ToString("dd.MM.yyyy")));
        Assert.Contains(preview.Aciklamalar, a => a.Contains("düşen/düşecek kart gideri: 0,00 TL"));

        var card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/gecis", request with { Onay = true });
        Assert.True(card.YeniTakip);
        // Denetim izi: açıklama, girilen tutarlar ve onay anındaki önizleme özeti saklanır.
        var gecis = card.Gecis!;
        Assert.Equal(("IslemTarihi", "Bekleyen düşüm doğrulandı", 0m, (string?)null), (gecis.Kural, gecis.Aciklama, gecis.RaporDisiEskiDusumTutari, gecis.Uyari));
        Assert.Equal(new KartGecisKaydi(bugun, 1000m, 1000m, 1000m, 0m, 1000m, AySonu(bugun), 1000m), gecis.Onizleme);
        Assert.Equal(gecis, (await c.GetFromJsonAsync<List<KartTakipDto>>("/api/takip/kartlar"))!.Single(k => k.Id == id).Gecis);
        Assert.Equal(before, await Raporlar(c, bugun.Year, bugun.Month));
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, bugun, 1000m));
        Assert.Equal(0m, Assert.Single(card.Odemeler).KasaEtkisi);
        Assert.Equal(before, await Raporlar(c, bugun.Year, bugun.Month));
        var buAy = (await c.GetFromJsonAsync<AylikRapor>($"/api/rapor/aylik?yil={bugun.Year}&ay={bugun.Month}"))!;
        Assert.Equal(1000m, buAy.Kanallar.Single(k => k.Kanal == "MEZAT").KrediKarti);
    }

    [Theory]
    [MemberData(nameof(TakvimSinirlari))]
    public async Task Eski_kart_gideri_etki_ayi_icinde_bekler_etki_gunu_bir_kez_duser_gecis_ve_odeme_ikinci_kez_dusurmez(int yil, int ay, int gun)
    {
        var bugun = new DateOnly(yil, ay, gun);
        // tests-2: önceki ayın gideri eski kuralla bu ayın son günü düşer. Saat etki gününden bir gün önce
        // başlar ve test ilerletir; takvim sınırı satırlarında etki günü 31 Ocak, 29 Şubat (artık yıl) ve 31 Mart olur.
        var etki = AySonu(bugun);
        var gider = new DateOnly(bugun.Year, bugun.Month, 1).AddMonths(-1);
        var saat = new SabitSaat(etki.AddDays(-1));
        await using var f = new KasaWebFactory { Saat = saat };
        using var c = await Editor(f, gider);
        var id = EskiKart(f, 0m, (gider, 1000m, 1));
        Assert.Equal(1000m, await Cash(c));
        var request = new KartGecisYaz(Guid.NewGuid(), 0, f.Bugun, 1000m, 1000m, [new(1, 1000m)], "Etki gününden önce geçiş", false);
        var preview = await Post<TakipGecisDto>(c, $"/api/takip/kartlar/{id}/gecis-onizleme", request);
        Assert.Equal((0m, 1000m, (DateOnly?)etki, 1000m, true), (preview.EskiKuraldaIslenenTutar!.Value, preview.BekleyenEskiDusumTutari!.Value,
            preview.SonBekleyenDusumTarihi, preview.OnerilenKasadaSayilanTutar!.Value, preview.KabulEdilebilir));
        var card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/gecis", request with { Onay = true });
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, f.Bugun, 1000m));
        Assert.Equal(0m, Assert.Single(card.Odemeler).KasaEtkisi);
        // Etki ayı içinde ödeme kasadan düşmez, eski düşüm henüz gelmedi.
        Assert.Equal(1000m, await Cash(c));
        // Etki günü eski kural düşer; ay dışına çıkınca düşüm tam bir kez kalır.
        saat.Ayarla(etki);
        Assert.Equal(0m, await Cash(c));
        saat.Ayarla(etki.AddDays(1));
        Assert.Equal(0m, await Cash(c));
        var etkiAyi = (await c.GetFromJsonAsync<AylikRapor>($"/api/rapor/aylik?yil={etki.Year}&ay={etki.Month}"))!;
        Assert.Equal(1000m, etkiAyi.Kanallar.Single(k => k.Kanal == "MEZAT").KrediKarti);
    }

    [Theory]
    [MemberData(nameof(TakvimSinirlari))]
    public async Task Onerilenden_buyuk_tutar_reddedilir_kucuk_tutar_ikinci_kez_dusum_uyarisiyla_kabul_edilir(int yil, int ay, int gun)
    {
        var bugun = new DateOnly(yil, ay, gun);
        await using var f = KasaWebFactory.Sabit(bugun);
        using var c = await Editor(f, new(bugun.Year, 1, 1));
        var id = EskiKart(f, 100m);
        var request = new KartGecisYaz(Guid.NewGuid(), 0, bugun, 300m, 200m, [new(1, 300m)], "Banka borcu", false);

        var fazla = await Post<TakipGecisDto>(c, $"/api/takip/kartlar/{id}/gecis-onizleme", request);
        Assert.Equal((100m, 100m, 100m, false), (fazla.OnerilenKasadaSayilanTutar!.Value, fazla.GenelKasaAnlikFarki, fazla.KanalAnlikFarki, fazla.KabulEdilebilir));
        Assert.Contains(fazla.Aciklamalar, a => a.Contains("hiçbir zaman"));
        Assert.Contains(fazla.Aciklamalar, a => a.Contains("açılış borcu (100,00 TL) eski modelde kasadan hiç düşmedi"));
        var rejected = await c.PostAsJsonAsync($"/api/takip/kartlar/{id}/gecis", request with { Onay = true });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("aşamaz", await rejected.Content.ReadAsStringAsync());
        Assert.False((await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{id}"))!.YeniTakip);

        var eksik = await Post<TakipGecisDto>(c, $"/api/takip/kartlar/{id}/gecis-onizleme", request with { IstekId = Guid.NewGuid(), KasadaOncedenSayilanTutar = 50m });
        Assert.Equal((-50m, -50m, true), (eksik.GenelKasaAnlikFarki, eksik.KanalAnlikFarki, eksik.KabulEdilebilir));
        Assert.Contains(eksik.Aciklamalar, a => a.Contains("ikinci kez") && a.Contains("50,00 TL"));
        var belirsiz = await Post<TakipGecisDto>(c, $"/api/takip/kartlar/{id}/gecis-onizleme", request with { IstekId = Guid.NewGuid(), KasadaOncedenSayilanTutar = 50m, Dagilimlar = [] });
        Assert.Equal((-50m, 0m), (belirsiz.GenelKasaAnlikFarki, belirsiz.KanalAnlikFarki));
        var tutarli = await Post<TakipGecisDto>(c, $"/api/takip/kartlar/{id}/gecis-onizleme", request with { IstekId = Guid.NewGuid(), KasadaOncedenSayilanTutar = 100m });
        Assert.Equal((0m, 0m, true), (tutarli.GenelKasaAnlikFarki, tutarli.KanalAnlikFarki, tutarli.KabulEdilebilir));

        var card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/gecis", request with { IstekId = Guid.NewGuid(), KasadaOncedenSayilanTutar = 50m, Onay = true });
        Assert.True(card.YeniTakip);
        using var scope = f.Services.CreateScope();
        Assert.Equal(EskiDusumKurali.IslemTarihi, scope.ServiceProvider.GetRequiredService<KasaDbContext>().TakipKartlar.Single(t => t.KrediKartiId == id).EskiDusumKurali);
    }

    [Theory]
    [MemberData(nameof(TakvimSinirlari))]
    public async Task Onerilenin_acilis_borcundan_fazla_altindaki_tutar_ikinci_kez_dusecegi_icin_reddedilir(int yil, int ay, int gun)
    {
        var bugun = new DateOnly(yil, ay, gun);
        var gider = new DateOnly(bugun.Year, bugun.Month, 1).AddMonths(-1);
        await using var f = KasaWebFactory.Sabit(bugun);
        using var c = await Editor(f, gider);
        // Sistem borcu = önerilen = 100 açılış + 1.000 eski gider. Eski gider eski kuralla bir kez düşer;
        // kasadan ayrıca ödenebilecek tek kısım eski modelde hiç düşmemiş 100 TL açılış borcudur.
        var id = EskiKart(f, 100m, (gider, 1000m, 1));
        // Eski Windows istemcisi önceden sayılan tutarı varsayılan 0 gönderir.
        var request = new KartGecisYaz(Guid.NewGuid(), 0, bugun, 1100m, 0m, [new(1, 1100m)], "Varsayılan tutar", false);

        var sifir = await Post<TakipGecisDto>(c, $"/api/takip/kartlar/{id}/gecis-onizleme", request);
        Assert.Equal((1100m, -1100m, false), (sifir.OnerilenKasadaSayilanTutar!.Value, sifir.GenelKasaAnlikFarki, sifir.KabulEdilebilir));
        Assert.Contains(sifir.Aciklamalar, a => a.Contains("ikinci kez") && a.Contains("en az 1.000,00 TL"));
        var rejected = await c.PostAsJsonAsync($"/api/takip/kartlar/{id}/gecis", request with { Onay = true });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("en az 1.000,00 TL", await rejected.Content.ReadAsStringAsync());
        Assert.False((await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{id}"))!.YeniTakip);

        var sinir = await Post<TakipGecisDto>(c, $"/api/takip/kartlar/{id}/gecis-onizleme", request with { IstekId = Guid.NewGuid(), KasadaOncedenSayilanTutar = 999.99m });
        Assert.False(sinir.KabulEdilebilir);
        var acilis = await Post<TakipGecisDto>(c, $"/api/takip/kartlar/{id}/gecis-onizleme", request with { IstekId = Guid.NewGuid(), KasadaOncedenSayilanTutar = 1000m });
        Assert.Equal((-100m, true), (acilis.GenelKasaAnlikFarki, acilis.KabulEdilebilir));
        Assert.Contains(acilis.Aciklamalar, a => a.Contains("ikinci kez") && a.Contains("açılış borcu"));
        var card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/gecis", request with { IstekId = Guid.NewGuid(), KasadaOncedenSayilanTutar = 1000m, Onay = true });
        Assert.True(card.YeniTakip);
    }

    [Fact]
    public async Task Ilk_surum_kuraliyla_yapilmis_gecisin_rapora_girmeyen_eski_dusumu_kartta_ve_acilis_logunda_gorunur_raporlar_degismez()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var db = Context(connection))
        {
            db.GetService<IMigrator>().Migrate("20260927000100_StatementImports");
            // Önceki sürümün şeması: çekirdek sürüm sütunları yok.
            using var eski = SurumOncesiBaglam.Ayni(db);
            EskiGecisVerisi(eski);
        }
        var logs = new UyariToplayici();
        await using var f = new HazirFactory(connection, logs) { Saat = new SabitSaat(Today) };
        using var c = await f.EditorClientAsync();
        // Tespit veri dönüştürmez: raporlar 2.3.0 çıktısıyla birebir aynı kalır.
        Assert.Equal(OncekiPanel, await c.GetStringAsync("/api/rapor/panel"));
        for (var ay = 7; ay <= 11; ay++)
            Assert.Equal(KuralIkiAlanlari(OncekiAylik[ay - 7]), await c.GetStringAsync($"/api/rapor/aylik?yil=2025&ay={ay}"));

        var gecis = (await c.GetFromJsonAsync<KartTakipDto>("/api/takip/kartlar/1"))!.Gecis!;
        Assert.Equal(("EtkiTarihi", (string?)null, (KartGecisKaydi?)null), (gecis.Kural, gecis.Aciklama, gecis.Onizleme));
        // 3 Ağustos (1.000, eski etki 30 Eylül) ve 10 Eylül (400, eski etki 31 Ekim) giderleri 20 Eylül
        // başlangıcında atlandı; 10 Temmuz gideri (etki 31 Ağustos) raporda, 25 Eylül gideri takipte.
        Assert.Equal((1400m, new DateOnly(2025, 9, 30), new DateOnly(2025, 10, 31)), (gecis.RaporDisiEskiDusumTutari, gecis.RaporDisiIlkDusumTarihi, gecis.RaporDisiSonDusumTarihi));
        Assert.Contains("1.400,00 TL", gecis.Uyari);
        Assert.Contains("30.09.2025", gecis.Uyari);
        Assert.Contains("31.10.2025", gecis.Uyari);
        Assert.Contains("banka/kasa kayıtlarıyla doğrulayın", gecis.Uyari);
        // Girilen kalan borç = sistem borcu = 1.900, K = 1.000: ilk sürümde tutarlı K 1.900 − 1.400 = 500 olurdu;
        // 500 TL ne ay sonunda ne ödemede düşüyor (1.500 TL ödemenin kasa etkisi yalnız 500 TL).
        Assert.Equal(500m, gecis.TahminiKasaFarki);
        Assert.Contains("500,00 TL kasadan hiçbir zaman düşmüyor", gecis.Uyari);
        Assert.Equal(gecis, (await c.GetFromJsonAsync<List<KartTakipDto>>("/api/takip/kartlar"))!.Single().Gecis);
        Assert.Contains(logs.Uyarilar, m => m.Contains("Kart 1 (Eski kart)") && m.Contains("1.400,00 TL") && m.Contains("31.10.2025"));

        using var check = connection.CreateCommand();
        check.CommandText = "SELECT EskiDusumKurali, GecisAciklamasi IS NULL, GecisOzetiJson IS NULL FROM TakipKartlar;";
        using (var reader = check.ExecuteReader())
        {
            Assert.True(reader.Read());
            Assert.Equal((0L, 1L, 1L), (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)));
        }
        Assert.Equal(OncekiPanel, await c.GetStringAsync("/api/rapor/panel"));
    }

    [Fact]
    public void Ilk_surum_tespiti_rapor_disi_dusumu_ve_girilen_tutarlara_gore_iki_yonlu_kasa_farkini_bulur()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = Context(connection);
        KasaVeritabaniBaslatici.Baslat(db);
        db.Kanallar.Add(new KanalEntity { Id = 1, Ad = "MEZAT" });
        KrediKartiEntity Kart(int id, string ad, decimal borc = 0) => new() { Id = id, Ad = ad, KesimTarihi = new(2000, 1, 5), SonOdemeTarihi = new(2000, 1, 15), Borc = borc };
        db.KrediKartlari.AddRange(Kart(1, "Devirsiz"), Kart(2, "Yeni kural"), Kart(3, "Ters yön", 100m), Kart(4, "Tutarlı ödeme"), Kart(5, "Kalıntısız"));
        // Kural 0 tipten bağımsız sonraki ay sonuna bakar (2.3.0): kartla ödenen cari gider de atlanır.
        db.Islemler.AddRange(Gider(1, new(2025, 8, 3), 1000m, 1), Gider(2, new(2025, 7, 10), 300m, 1), Gider(3, new(2025, 9, 1), -50m, 1),
            new IslemEntity { Id = 4, Tarih = new(2025, 9, 3), Cari = "Kartla cari", TutarTl = 40m, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.Cari, KrediKartiId = 1 },
            Gider(5, new(2025, 8, 3), 700m, 1, kart: 2), Gider(6, new(2025, 8, 25), 1000m, 1, kart: 3), Gider(7, new(2025, 9, 10), 1000m, 1, kart: 4),
            Gider(8, new(2025, 8, 25), 1000m, 1, kart: 5));
        db.SaveChanges();
        TakipKartEntity Takip(int kart, DateOnly baslangic, EskiDusumKurali kural = EskiDusumKurali.EtkiTarihi) => new() { KrediKartiId = kart, Baslangic = baslangic, EskiKayit = true, EskiDusumKurali = kural };
        db.TakipKartlar.AddRange(Takip(1, new(2025, 9, 20)), Takip(2, new(2025, 9, 20), EskiDusumKurali.IslemTarihi), Takip(3, new(2025, 10, 1)), Takip(4, new(2025, 9, 25)), Takip(5, new(2025, 10, 1)));
        db.SaveChanges();
        TakipHarcamaEntity Devir(int kart, DateOnly tarih, decimal r, decimal k, bool iptal = false) => new() { KrediKartiId = kart, Tarih = tarih, Aciklama = "Onaylanan eski borç devri", Tutar = r, KasadaOncedenSayilanTutar = k, Iptal = iptal };
        // İlk sürüm web formu K'yı varsayılan 0 gönderiyordu; iptal edilen devir hesaba girmez.
        db.TakipHarcamalar.AddRange(Devir(3, new(2025, 10, 1), 1100m, 0m), Devir(4, new(2025, 9, 25), 1000m, 0m), Devir(5, new(2025, 10, 1), 1000m, 0m, iptal: true), Devir(5, new(2025, 10, 1), 1000m, 1000m));
        db.SaveChanges();

        var kalintilar = KartGecisHesabi.IlkSurumKalintilari(db);
        Assert.Equal(new[] { 1, 3, 4 }, kalintilar.Select(k => k.KartId));
        // Devirsiz: 1.000 + (−50) + 40 rapor dışı; kalan borç 0 girildiği için hiçbiri ödemede de düşmez.
        Assert.Equal(new IlkSurumKalintisi(1, "Devirsiz", new(2025, 9, 20), 990m, new(2025, 9, 30), new(2025, 10, 31), 1290m, 0m, 0m, 0m, 990m), kalintilar[0]);
        // Ters yön: 25 Ağustos gideri 30 Eylül'de eski kuralla düştü; K = 0 ile ödemede ikinci kez düşer.
        Assert.Equal(new IlkSurumKalintisi(3, "Ters yön", new(2025, 10, 1), 0m, null, null, 1100m, 1100m, 0m, 100m, -1100m), kalintilar[1]);
        Assert.Contains("1.100,00 TL eski kuralla düşülmüş borç ödendiğinde kasadan ikinci kez düşüyor (en çok 100,00 TL", KartGecisHesabi.Uyari(kalintilar[1]));
        Assert.Contains("raporlara girmeyen eski ay sonu düşümü yok", KartGecisHesabi.Uyari(kalintilar[1]));
        // Tutarlı: bekleyen 1.000 TL K = 0 ile ödemede düşer; toplam etki doğru, yalnız tarih farklı.
        Assert.Equal((1000m, 0m), (kalintilar[2].RaporDisiTutar, kalintilar[2].TahminiKasaFarki));
        Assert.Contains("toplam kasa etkisi tutarlı", KartGecisHesabi.Uyari(kalintilar[2]));
        Assert.Null(KartGecisHesabi.IlkSurumKalintisi(db, db.TakipKartlar.AsNoTracking().Single(t => t.KrediKartiId == 2)));
        Assert.Null(KartGecisHesabi.IlkSurumKalintisi(db, db.TakipKartlar.AsNoTracking().Single(t => t.KrediKartiId == 5)));
        var takip = db.TakipKartlar.AsNoTracking().Single(t => t.KrediKartiId == 1);
        Assert.Equal(new[] { false, true, true, true }, new[] { new DateOnly(2025, 7, 10), new(2025, 8, 3), new(2025, 9, 1), new(2025, 9, 3) }.Select(t => KartGecisHesabi.IlkSurumdeAtlanir(takip, t)));
        Assert.False(KartGecisHesabi.IlkSurumdeAtlanir(takip, new(2025, 9, 20)));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Kural_sutunu_migrationi_mevcut_gecisin_panel_ve_aylik_raporlarini_birebir_korur()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var db = Context(connection))
        {
            db.GetService<IMigrator>().Migrate("20260927000100_StatementImports");
            // Önceki sürümün şeması: çekirdek sürüm sütunları yok.
            using var eski = SurumOncesiBaglam.Ayni(db);
            EskiGecisVerisi(eski);
        }
        await using var f = new HazirFactory(connection) { Saat = new SabitSaat(Today) };
        using var c = await f.EditorClientAsync();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT EskiDusumKurali FROM TakipKartlar;";
            Assert.Equal(0L, check.ExecuteScalar());
        }
        // Beklenen JSON'lar bu paketten önceki kodun (2.3.0, fce8578) aynı eski şemalı veride ürettiği çıktıdır.
        Assert.Equal(OncekiPanel, await c.GetStringAsync("/api/rapor/panel"));
        for (var ay = 7; ay <= 11; ay++)
            Assert.Equal(KuralIkiAlanlari(OncekiAylik[ay - 7]), await c.GetStringAsync($"/api/rapor/aylik?yil=2025&ay={ay}"));
    }

    // K2 (aylık rapor kural 2): açık ay yanıtı tutarları değiştirmeden ayın kredi girişi toplamını ve kural sürümünü ekler.
    private static string KuralIkiAlanlari(string oncekiAylik) => oncekiAylik[..^1] + ",\"krediGirisi\":0,\"kuralSurumu\":2}";

    private const string OncekiPanel = """{"guncelKasa":14000.0,"kanallar":[{"kanal":"MEZAT","bakiye":4500.0,"kanalId":1},{"kanal":"PERAKENDE","bakiye":0.0,"kanalId":2},{"kanal":"TOPTAN","bakiye":-200.0,"kanalId":3}],"buHaftaSonucu":0,"buAySonucu":0,"dagilimBekleyenTutar":0}""";
    private static readonly string[] OncekiAylik =
    [
        """{"yil":2025,"ay":7,"kanallar":[{"kanal":"MEZAT","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0}],"dagilimBekleyenTutar":0,"genelGider":0,"genelGelir":0}""",
        """{"yil":2025,"ay":8,"kanallar":[{"kanal":"MEZAT","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":300.0,"ortakPay":0,"aySonucu":-300.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0}],"dagilimBekleyenTutar":0,"genelGider":0,"genelGelir":0}""",
        """{"yil":2025,"ay":9,"kanallar":[{"kanal":"MEZAT","gelen":5000.0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":5000.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"cariGiden":200.0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":-200.0,"krediGirisi":0}],"dagilimBekleyenTutar":0,"genelGider":0,"genelGelir":0}""",
        """{"yil":2025,"ay":10,"kanallar":[{"kanal":"MEZAT","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":500,"ortakPay":0,"aySonucu":-500,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0}],"dagilimBekleyenTutar":0,"genelGider":0,"genelGelir":0}""",
        """{"yil":2025,"ay":11,"kanallar":[{"kanal":"MEZAT","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0}],"dagilimBekleyenTutar":0,"genelGider":0,"genelGelir":0}""",
    ];

    // Kural sütunu yokken (2.3.0) canlıdaki gibi yapılmış eski kart geçişi: 2 ve 3 numaralı
    // giderlerin eski ay sonu etkisi başlangıçtan sonra olduğundan eski kuralda atlanıyordu.
    private static void EskiGecisVerisi(KasaDbContext db)
    {
        db.Kanallar.AddRange(new KanalEntity { Id = 1, Ad = "MEZAT", Sira = 0 }, new KanalEntity { Id = 2, Ad = "PERAKENDE", Sira = 1 }, new KanalEntity { Id = 3, Ad = "TOPTAN", Sira = 2 });
        db.Ayarlar.Add(new AyarEntity { Id = 1, TakipBaslangic = new(2025, 1, 1), KasaAcilisDevri = 10000m });
        db.KrediKartlari.Add(new KrediKartiEntity { Id = 1, Ad = "Eski kart", KesimTarihi = new(2000, 1, 5), SonOdemeTarihi = new(2000, 1, 15), Limit = 50000m, Borc = 500m });
        db.Islemler.AddRange(Gider(1, new(2025, 7, 10), 300m, 1), Gider(2, new(2025, 8, 3), 1000m, 1), Gider(3, new(2025, 9, 10), 400m, 2), Gider(5, new(2025, 9, 25), 250m, 3),
            new IslemEntity { Id = 4, Tarih = new(2025, 9, 5), Cari = "Nakit gider", TutarTl = 200m, Kanal = "TOPTAN", KanalId = 3, Tip = GiderTipi.Cari });
        db.Gelenler.Add(new GelenEntity { Id = 1, DonemStart = new(2025, 9, 1), Kanal = "MEZAT", KanalId = 1, TutarTl = 5000m });
        db.KartOdemeler.Add(new KartOdemeEntity { Id = 1, KrediKartiId = 1, Tarih = new(2025, 8, 15), Tutar = 300m, Not = "Eski ekstre" });
        db.SaveChanges();
        // TakipKartlar bu şemada kural sütunu taşımaz; satır o günkü sütunlarla ham SQL ile yazılır.
        db.Database.ExecuteSqlRaw("INSERT INTO TakipKartlar (KrediKartiId, Surum, Baslangic, Aktif, EskiKayit) VALUES (1, 2, '2025-09-20', 1, 1);");
        db.TakipEkstreler.Add(new TakipEkstreEntity { Id = 1, KrediKartiId = 1, KesimTarihi = new(2025, 10, 5), SonOdemeTarihi = new(2025, 10, 15) });
        db.TakipHarcamalar.Add(new TakipHarcamaEntity
        {
            Id = 1,
            KrediKartiId = 1,
            Tarih = new(2025, 9, 20),
            Aciklama = "Onaylanan eski borç devri",
            Tutar = 1900m,
            DagilimJson = "[{\"KanalId\":1,\"Tutar\":1900}]",
            KasadaOncedenSayilanTutar = 1000m
        });
        db.TakipKartTaksitler.Add(new TakipKartTaksitEntity { Id = 1, HarcamaId = 1, EkstreId = 1, Tutar = 1900m });
        db.TakipKartOdemeler.Add(new TakipKartOdemeEntity { Id = 1, KrediKartiId = 1, Tarih = new(2025, 10, 15), Tutar = 1500m, PaylarJson = "[{\"TaksitId\":1,\"Tutar\":1500,\"OncedenOdenen\":0}]" });
        db.SaveChanges();
    }

    private static IslemEntity Gider(int id, DateOnly tarih, decimal tutar, int kanal, int kart = 1) => new()
    {
        Id = id,
        Tarih = tarih,
        Cari = "Kart gideri " + id,
        TutarTl = tutar,
        Kanal = kanal switch { 1 => "MEZAT", 2 => "PERAKENDE", _ => "TOPTAN" },
        KanalId = kanal,
        Tip = GiderTipi.KrediKarti,
        KrediKartiId = kart
    };
    // Kesim 5, son ödeme 15. günlü, henüz takibe alınmamış eski kart ve kart giderleri.
    private static int EskiKart(KasaWebFactory f, decimal borc, params (DateOnly Tarih, decimal Tutar, int Kanal)[] giderler)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var card = new KrediKartiEntity { Ad = "Eski kart", KesimTarihi = new(2000, 1, 5), SonOdemeTarihi = new(2000, 1, 15), Limit = 50000m, Borc = borc };
        db.KrediKartlari.Add(card);
        db.SaveChanges();
        foreach (var (tarih, tutar, kanal) in giderler)
            db.Islemler.Add(Gider(0, tarih, tutar, kanal, card.Id));
        db.SaveChanges();
        return card.Id;
    }
    private static async Task<HttpClient> Editor(KasaWebFactory f, DateOnly baslangic)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = baslangic, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode();
        return c;
    }
    private static async Task<T> Post<T>(HttpClient c, string path, object body)
    {
        var r = await c.PostAsJsonAsync(path, body);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<List<string>> Raporlar(HttpClient c, int yil, params int[] aylar)
    {
        var result = new List<string> { await c.GetStringAsync("/api/rapor/panel") };
        foreach (var ay in aylar)
            result.Add(await c.GetStringAsync($"/api/rapor/aylik?yil={yil}&ay={ay}"));
        return result;
    }
    private static async Task<decimal> Cash(HttpClient c) => (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa;
    private static KasaDbContext Context(SqliteConnection connection) => new(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connection).Options);

    // Uygulamayı önceden hazırlanmış (eski şemalı) bağlantı üzerinde başlatır; açılışta migration çalışır.
    private sealed class HazirFactory(SqliteConnection hazir, UyariToplayici? logs = null) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            if (logs is not null)
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(o => o.UseSqlite(hazir));
            });
        }
    }
}
