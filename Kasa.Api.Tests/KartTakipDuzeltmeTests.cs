using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Kasa.Api.Tests.MonthlyExpenseTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Kart takibi düzeltmeleri: eski borç devrinin kasada önceden sayılan tutarı (finance-2), kilitli döneme düşen kart
/// avansı (finance-8) ve kart iadesinin kanal payının kaynak alışı izlemesi (gap-coklu-giris-cift-sayim-mutabakat-3).
/// Sunucu saati sabittir (<see cref="MonthlyExpenseTests.Today"/>); takip başlangıcı bugünün ayından üç ay öncedir, kasa
/// açılışı 1.000 TL'dir. Göç senaryosu önceki kodun (5802ccb) aynı eski şemalı veride ürettiği yanıtlarla karşılaştırılır.
/// </summary>
public class KartTakipDuzeltmeTests
{
    // --- finance-2: eski borç devri ---

    [Fact]
    public async Task Devir_iadesi_kasada_onceden_sayilan_kismi_kasaya_dondurur_kalan_odeme_ikinci_kez_dusmez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        // Eski kuralla 31 Ağustos'ta 100 TL düşmüş kart borcu, K = 100 ile yeni takibe geçer.
        var kart = await GecisliKart(f, c, 100m, 100m, 100m);
        Assert.Equal(900m, (await Panel(c)).GuncelKasa);
        var devir = Assert.Single(kart.Harcamalar);

        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, Today, "Satıcı iadesi", -30m, 1, null, [], devir.Id));
        var iade = kart.Harcamalar.Single(h => h.Tutar < 0);
        Assert.Equal(30m, iade.KasadaSayilanDuzeltme);
        Assert.Equal([(1, 30m)], iade.Dagilimlar.Select(p => (p.KanalId!.Value, p.Tutar)));
        // İade edilen 30 TL eski kuralla kasadan düşülmüştü, bankaya hiç ödenmeyecek: iade tarihinde kasaya döner.
        var panel = await Panel(c);
        Assert.Equal(930m, panel.GuncelKasa);
        Assert.Contains("\"krediKarti\":-30", await c.GetStringAsync($"/api/rapor/aylik?yil={Today.Year}&ay={Today.Month}"));

        // Bankanın istediği kalan 70 TL ödenir: kasada önceden sayılmış kabul edilir, ikinci kez düşmez.
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, Today, 70m));
        Assert.Equal(0m, Assert.Single(kart.Odemeler).KasaEtkisi);
        Assert.Equal(0m, kart.Borc);
        // Net kasa etkisi −70 (gerçek ödeme): eskiden −100'de kalıyordu.
        Assert.Equal(930m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Devir_kismen_odendikten_sonra_iade_yalniz_odenmemis_sayilmis_kismi_dondurur()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        // K = 60 (kalan 40 açılış borcu gibi kasadan ayrıca ödenir); 50 ödendi (kasa etkisi 0), 30 iade edildi.
        var kart = await GecisliKart(f, c, 60m, 100m, 60m, acilisBorcu: 40m);
        var devir = Assert.Single(kart.Harcamalar);
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, Today, 50m));
        Assert.Equal(0m, kart.Odemeler.Single().KasaEtkisi);
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, Today, "İade", -30m, 1, null, [], devir.Id));
        // Önceden sayılmış 60 TL'nin 50'si ödemeyle kullanıldı: iade yalnız kalan 10 TL'yi kasaya döndürür.
        Assert.Equal(10m, kart.Harcamalar.Single(h => h.Tutar < 0).KasadaSayilanDuzeltme);
        // Kalan 20 TL ödemesi tamamen kasadan düşer: kasa toplamda gerçek ödeme (70) kadar ve açılış borcu dışında eski
        // kuralla düşmüş 60 TL'yi ikinci kez düşürmeden azalır: 1.000 − 60 (eski kural) + 10 (iade) − 20 (ödeme) = 930.
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, Today, 20m));
        Assert.Equal(20m, kart.Odemeler.Single(o => o.Tutar == 20m).KasaEtkisi);
        Assert.Equal(930m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Kasada_onceden_sayilan_tutari_olan_devir_iptal_edilemez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var kart = await GecisliKart(f, c, 100m, 100m, 100m);
        var r = await c.PostAsJsonAsync($"/api/takip/kartlar/{kart.Id}/harcamalar/{kart.Harcamalar.Single().Id}/iptal", new TakipIptalYaz(Guid.NewGuid(), kart.Surum, "Yanlış devir"));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("Devri düzelt", await r.Content.ReadAsStringAsync());
        // Devir açıklaması kullanıcı harcamasına verilemez (devir bu açıklamayla tanınır).
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/takip/kartlar/{kart.Id}/harcamalar",
            new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, Today, KartGecisHesabi.DevirAciklamasi, 10m, 1, null, [new(1, 10m)]))).StatusCode);
    }

    [Fact]
    public async Task Hatali_devir_gerekceyle_duzeltilir_eski_devir_iptal_yeni_odeme_ikinci_kez_dusmez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        // Eski sistem borcu 80 TL iken kalan borç yanlışlıkla 100 girildi (K = önerilen 80).
        var kart = await GecisliKart(f, c, 80m, 100m, 80m);
        Assert.Equal(920m, (await Panel(c)).GuncelKasa);
        var devir = (await c.GetFromJsonAsync<KartDevirDto>($"/api/takip/kartlar/{kart.Id}/devir"))!;
        Assert.Equal((kart.Harcamalar.Single().Id, Today, 100m, 80m, 0m, "IslemTarihi", 80m, 80m, 80m, true, (string?)null),
            (devir.HarcamaId, devir.Tarih, devir.KalanBorc, devir.KasadaOncedenSayilanTutar, devir.IadeDuzeltmesi, devir.Kural, devir.SistemKartBorcu, devir.OnerilenKasadaSayilanTutar, devir.EnAzKasadaSayilanTutar, devir.Duzeltilebilir, devir.Engel));

        KartDevirDuzeltYaz Duzeltme(decimal kalan, decimal sayilan, int? harcama) => new(Guid.NewGuid(), kart.Surum, harcama, kalan, sayilan, [new(1, kalan)], "Banka ekstresine göre devir 80 TL");
        var fazla = await c.PostAsJsonAsync($"/api/takip/kartlar/{kart.Id}/devir-duzelt", Duzeltme(100m, 90m, devir.HarcamaId));
        Assert.Equal(HttpStatusCode.Conflict, fazla.StatusCode);
        Assert.Contains("önerilen tutarı (80,00 TL) aşamaz", await fazla.Content.ReadAsStringAsync());
        var eksik = await c.PostAsJsonAsync($"/api/takip/kartlar/{kart.Id}/devir-duzelt", Duzeltme(80m, 50m, devir.HarcamaId));
        Assert.Equal(HttpStatusCode.Conflict, eksik.StatusCode);
        Assert.Contains("en az 80,00 TL", await eksik.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{kart.Id}/devir-duzelt", Duzeltme(80m, 80m, null))).StatusCode);

        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/devir-duzelt", Duzeltme(80m, 80m, devir.HarcamaId));
        Assert.True(kart.Harcamalar.Single(h => h.Id == devir.HarcamaId).Iptal);
        var yeni = kart.Harcamalar.Single(h => !h.Iptal);
        Assert.Equal((KartGecisHesabi.DevirAciklamasi, Today, 80m), (yeni.Aciklama, yeni.Tarih, yeni.Tutar));
        Assert.Equal(80m, kart.Borc);
        var sonra = (await c.GetFromJsonAsync<KartDevirDto>($"/api/takip/kartlar/{kart.Id}/devir"))!;
        Assert.Equal((yeni.Id, 80m, 80m), (sonra.HarcamaId, sonra.KalanBorc, sonra.KasadaOncedenSayilanTutar));
        // Düzeltme kasayı değiştirmez; düzeltilmiş devrin ödemesi eski kuralla düşülmüş 80 TL'yi ikinci kez düşürmez.
        Assert.Equal(920m, (await Panel(c)).GuncelKasa);
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, Today, 80m));
        Assert.Equal(0m, Assert.Single(kart.Odemeler).KasaEtkisi);
        Assert.Equal(920m, (await Panel(c)).GuncelKasa);
        // Gerekçe denetim izine, devrin önceki ve yeni hâliyle yazılır.
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var olaylar = db.DenetimOlaylari.AsNoTracking().Where(o => o.Gerekce == "Banka ekstresine göre devir 80 TL" && o.Varlik == "TakipHarcama").ToList();
        Assert.Contains(olaylar, o => o.VarlikId == devir.HarcamaId.ToString() && o.Tur == "Degistir");
        Assert.Contains(olaylar, o => o.VarlikId == yeni.Id.ToString() && o.Tur == "Ekle");
    }

    [Fact]
    public async Task Odemesi_ya_da_iadesi_olan_devir_duzeltilemez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var odenen = await GecisliKart(f, c, 100m, 100m, 100m);
        odenen = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{odenen.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), odenen.Surum, Today, 10m));
        var devir = (await c.GetFromJsonAsync<KartDevirDto>($"/api/takip/kartlar/{odenen.Id}/devir"))!;
        Assert.False(devir.Duzeltilebilir);
        Assert.Contains("ödeme", devir.Engel);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{odenen.Id}/devir-duzelt",
            new KartDevirDuzeltYaz(Guid.NewGuid(), odenen.Surum, devir.HarcamaId, 100m, 100m, [new(1, 100m)], "Deneme"))).StatusCode);

        var iadeli = await GecisliKart(f, c, 100m, 100m, 100m);
        iadeli = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{iadeli.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), iadeli.Surum, Today, "İade", -10m, 1, null, [], iadeli.Harcamalar.Single().Id));
        devir = (await c.GetFromJsonAsync<KartDevirDto>($"/api/takip/kartlar/{iadeli.Id}/devir"))!;
        Assert.Equal((false, 10m), (devir.Duzeltilebilir, devir.IadeDuzeltmesi));
        Assert.Contains("iade", devir.Engel);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{iadeli.Id}/devir-duzelt",
            new KartDevirDuzeltYaz(Guid.NewGuid(), iadeli.Surum, devir.HarcamaId, 90m, 90m, [new(1, 90m)], "Deneme"))).StatusCode);
        // İade (devre ödeme yokken) iptal edilince kasaya dönen tutar da kalkar; devir yeniden düzeltilebilir.
        var once = (await Panel(c)).GuncelKasa;
        iadeli = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{iadeli.Id}/harcamalar/{iadeli.Harcamalar.Single(h => h.Tutar < 0).Id}/iptal", new TakipIptalYaz(Guid.NewGuid(), iadeli.Surum, "Satıcı iadeyi geri aldı"));
        Assert.Equal(once - 10m, (await Panel(c)).GuncelKasa);
        Assert.True((await c.GetFromJsonAsync<KartDevirDto>($"/api/takip/kartlar/{iadeli.Id}/devir"))!.Duzeltilebilir);
        // Geçişle takibe alınmamış kartta devir yoktur.
        var yeniKart = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Yeni kart", 1000m, 5, 25, Month, 0m, []));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/takip/kartlar/{yeniKart.Id}/devir")).StatusCode);
    }

    [Fact]
    public async Task Kilitli_doneme_dusen_devir_duzeltilemez_kilitli_ay_raporu_degismez()
    {
        var saat = new SabitSaat(Today);
        await using var f = new KasaWebFactory { Saat = saat };
        using var c = await Editor(f);
        var kart = await GecisliKart(f, c, 80m, 100m, 80m);
        // Ay biter, geçiş ayı kapatılır (K4): devir tarihi kilitli dönemdedir.
        saat.Ayarla(Today.AddMonths(1).AddDays(-Today.Day + 5));
        var eylul = $"/api/rapor/aylik?yil={Today.Year}&ay={Today.Month}";
        var kilitOncesi = await c.GetStringAsync(eylul);
        var durum = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi"))!;
        await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), durum.Surum, Today.Year, Today.Month, "Ay tamamlandı"));
        var devir = (await c.GetFromJsonAsync<KartDevirDto>($"/api/takip/kartlar/{kart.Id}/devir"))!;
        Assert.False(devir.Duzeltilebilir);
        Assert.Contains("kilitli dönemde", devir.Engel);
        kart = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{kart.Id}"))!;
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{kart.Id}/devir-duzelt",
            new KartDevirDuzeltYaz(Guid.NewGuid(), kart.Surum, devir.HarcamaId, 80m, 80m, [new(1, 80m)], "Kilitli düzeltme"))).StatusCode);
        var beklenen = JsonNode.Parse(kilitOncesi)!.AsObject();
        beklenen["kuralSurumu"] = HesapServisi.AcikAyKurali;
        beklenen["dondurulmus"] = true;
        Assert.Equal(beklenen.ToJsonString(), await c.GetStringAsync(eylul));
        using var scope = f.Services.CreateScope();
        Assert.Equal(kilitOncesi, JsonSerializer.Serialize(scope.ServiceProvider.GetRequiredService<HesapServisi>().Aylik(Today.Year, Today.Month), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(scope.ServiceProvider.GetRequiredService<KasaDbContext>().TakipHarcamalar.Single(h => h.Id == devir.HarcamaId).Iptal);
    }

    [Fact]
    public async Task Duzeltilmemis_eski_devir_iadesi_acilis_uyarisinda_gorunur_rapor_degismez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var kart = await GecisliKart(f, c, 100m, 100m, 100m);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.DoesNotContain(FinansTakipServisi.EskiKuralKalintilari(db), u => u.Contains("eski borç devrine"));
        // Bu sürümden önce girilmiş devir iadesi: hesap kaydı yok, kasada önceden sayılan tutar düzeltilmemiş.
        var devirTaksidi = db.TakipKartTaksitler.Single(t => t.HarcamaId == kart.Harcamalar.Single().Id);
        var eskiIade = new TakipHarcamaEntity { KrediKartiId = kart.Id, Tarih = Today, Aciklama = "Eski iade", Tutar = -30m, KaynakHarcamaId = devirTaksidi.HarcamaId, DagilimJson = "[{\"KanalId\":1,\"Tutar\":30}]" };
        db.TakipHarcamalar.Add(eskiIade);
        db.SaveChanges();
        db.TakipKartTaksitler.Add(new TakipKartTaksitEntity { HarcamaId = eskiIade.Id, EkstreId = devirTaksidi.EkstreId, Tutar = -30m });
        db.SaveChanges();
        var uyari = Assert.Single(FinansTakipServisi.EskiKuralKalintilari(db), u => u.Contains("eski borç devrine"));
        Assert.Contains("1 iade (toplam 30,00 TL", uyari);
        Assert.Equal(900m, (await Panel(c)).GuncelKasa);
    }

    // --- finance-8: kilitli döneme düşen kart avansı ---

    [Fact]
    public async Task Kilitli_doneme_dusen_avans_karti_kapatmaz_kilit_sonrasi_dagitimla_baglanir_kilitli_ay_degismez()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var eski = Month.AddMonths(-1);
        var kart = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Avans", 1000m, 5, 25, eski, 0m, []));
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, eski, 50m));
        var kaynak = Assert.Single(kart.Odemeler);
        await Kilit(c, eski, kapat: true);
        var aylik = $"/api/rapor/aylik?yil={eski.Year}&ay={eski.Month}";
        var aylikOnce = await c.GetStringAsync(aylik);
        var haftalikOnce = await KilitliHaftalar(c, eski);
        Assert.Equal(950m, (await Panel(c)).GuncelKasa);

        // Kart yeni harcamaya açıktır (eskiden 'dönem kilitli' ile reddediliyordu).
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, Today, "Yeni harcama", 100m, 1, null, [new(1, 100m)]));
        Assert.Equal(50m, kart.Borc);
        var dagitim = Assert.Single(kart.Odemeler, o => o.AvansKaynakOdemeId is not null);
        Assert.Equal((Today, 0m, 0m, (int?)kaynak.Id, false), (dagitim.Tarih, dagitim.Tutar, dagitim.KasaEtkisi, dagitim.AvansKaynakOdemeId, dagitim.Iptal));
        Assert.Equal([((int?)null, -50m), (1, 50m)], dagitim.Dagilimlar.Select(p => (p.KanalId, p.Tutar)).OrderBy(p => p.KanalId ?? 0));
        // Kilitli ödemenin payı ve kilitli ayın raporları değişmez; avans bugün MEZAT'a geçer, kasa sabit kalır.
        var kaynakSonra = kart.Odemeler.Single(o => o.Id == kaynak.Id);
        Assert.Equal((50m, 50m), (kaynakSonra.KasaEtkisi, Assert.Single(kaynakSonra.Dagilimlar, p => p.KanalId is null).Tutar));
        Assert.Equal(aylikOnce, await c.GetStringAsync(aylik));
        Assert.Equal(haftalikOnce, await KilitliHaftalar(c, eski));
        var panel = await Panel(c);
        Assert.Equal((950m, -50m), (panel.GuncelKasa, panel.Kanallar.Single(k => k.Kanal == "MEZAT").Bakiye));

        // Gider formundan karta harcama da yazılabilir; avans tükendiği için yeni dağıtım oluşmaz.
        var gider = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Today, "Kartla gider", 30m, "MEZAT", GiderTipi.KrediKarti, KrediKartiId: kart.Id));
        Assert.True(gider.IsSuccessStatusCode, await gider.Content.ReadAsStringAsync());
        kart = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{kart.Id}"))!;
        Assert.Equal(80m, kart.Borc);
        Assert.Single(kart.Odemeler, o => o.AvansKaynakOdemeId is not null);

        // Kilitli avans ödemesi ve dağıtımı ayrı iptal edilemez; dağıtımla ödenmiş harcama iade yerine iptal edilemez.
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{kart.Id}/odemeler/{kaynak.Id}/iptal", new TakipIptalYaz(Guid.NewGuid(), kart.Surum, "Eski avans"))).StatusCode);
        var dagitimIptal = await c.PostAsJsonAsync($"/api/takip/kartlar/{kart.Id}/odemeler/{dagitim.Id}/iptal", new TakipIptalYaz(Guid.NewGuid(), kart.Surum, "Dağıtım"));
        Assert.Equal(HttpStatusCode.Conflict, dagitimIptal.StatusCode);
        Assert.Contains("avans dağıtımı", await dagitimIptal.Content.ReadAsStringAsync());
        var harcama = kart.Harcamalar.Single(h => h.Aciklama == "Yeni harcama");
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{kart.Id}/harcamalar/{harcama.Id}/iptal", new TakipIptalYaz(Guid.NewGuid(), kart.Surum, "Yanlış"))).StatusCode);
        Assert.Equal(950m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Acik_donem_avansi_sonraki_kilitli_odemenin_etkisini_degistirmeden_dagitimla_baglanir()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var eski = Month.AddMonths(-1);
        var kart = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Avans", 1000m, 5, 25, eski, 0m, []));
        // Açık dönemdeki avans (Id 1) ve sonradan geriye tarihli girilen kilitli dönem avansı (Id 2).
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, Today, 50m));
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, eski.AddDays(14), 20m));
        await Kilit(c, eski, kapat: true);
        var aylik = $"/api/rapor/aylik?yil={eski.Year}&ay={eski.Month}";
        var aylikOnce = await c.GetStringAsync(aylik);
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, Today, "Yeni harcama", 100m, 1, null, [new(1, 100m)]));
        Assert.Equal(30m, kart.Borc);
        var kaynaklar = kart.Odemeler.Where(o => o.Tutar > 0).OrderBy(o => o.Id).ToList();
        Assert.Equal(kaynaklar.Select(o => (int?)o.Id), kart.Odemeler.Where(o => o.AvansKaynakOdemeId is not null).Select(o => o.AvansKaynakOdemeId).Order());
        Assert.All(kaynaklar, o => Assert.Equal(o.Tutar, Assert.Single(o.Dagilimlar).Tutar));
        Assert.Equal(aylikOnce, await c.GetStringAsync(aylik));
        Assert.Equal(930m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Kilit_acilinca_avans_odemesinin_iptali_dagitimini_da_iptal_eder()
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var eski = Month.AddMonths(-1);
        var kart = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Avans", 1000m, 5, 25, eski, 0m, []));
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, eski, 50m));
        var kaynak = Assert.Single(kart.Odemeler);
        await Kilit(c, eski, kapat: true);
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, Today, "Yeni harcama", 30m, 1, null, [new(2, 30m)]));
        var dagitim = Assert.Single(kart.Odemeler, o => o.AvansKaynakOdemeId == kaynak.Id);
        Assert.Equal([((int?)null, -30m), (2, 30m)], dagitim.Dagilimlar.Select(p => (p.KanalId, p.Tutar)).OrderBy(p => p.KanalId ?? 0));

        // Kalan 20 TL avans ikinci harcamaya ikinci dağıtımla bağlanır; kilitli ödeme yine değişmez.
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, Today, "İkinci harcama", 40m, 1, null, [new(3, 40m)]));
        Assert.Equal(2, kart.Odemeler.Count(o => o.AvansKaynakOdemeId == kaynak.Id));
        Assert.Equal(20m, kart.Borc);

        await Kilit(c, eski, kapat: false);
        kart = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{kart.Id}"))!;
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler/{kaynak.Id}/iptal", new TakipIptalYaz(Guid.NewGuid(), kart.Surum, "Avans bankaya ulaşmadı"));
        Assert.All(kart.Odemeler, o => Assert.True(o.Iptal));
        Assert.Equal(70m, kart.Borc);
        Assert.Equal(1000m, (await Panel(c)).GuncelKasa);
    }

    // --- gap-coklu-giris-cift-sayim-mutabakat-3: iade payı kaynak alışı izler ---

    [Theory]
    [InlineData(500, 500, 300, 300, 200, 200)]
    [InlineData(200, 800, 120, 480, 80, 320)]
    public async Task Alis_yeniden_dagitilinca_kart_iadesi_ve_odemenin_kanal_etkisi_yeni_orani_izler(int mezat, int toptan, int odemeMezat, int odemeToptan, int iadeMezat, int iadeToptan)
    {
        await using var f = Fabrika();
        using var c = await Editor(f);
        var kart = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Alış kartı", 10000m, 5, 25, Month, 0m, []));
        var alis = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Month, "Tedarikçi", null, [new("Mal", 1000m, [new(1, 1000m)])]));
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/gonder", new AlisDurumYaz(alis.Surum));
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/onayla", new AlisDurumYaz(alis.Surum, "Uygun"));
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), Month.AddDays(1), 1000m, kart.Id));
        kart = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{kart.Id}"))!;
        var kaynak = Assert.Single(kart.Harcamalar);
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), kart.Surum, Month.AddDays(3), "Kısmi iade", -400m, 1, null, [], kaynak.Id));
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, Today, 600m));
        Assert.Equal([(1, 600m)], Assert.Single(kart.Odemeler).Dagilimlar.Select(p => (p.KanalId!.Value, p.Tutar)));

        // Alış açıklamayla iade edilip farklı kanal dağılımıyla yeniden onaylanır.
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/iade", new AlisDurumYaz(alis.Surum, "Kanal dağılımı düzeltilecek"));
        var guncel = await c.PutAsJsonAsync($"/api/alis/{alis.Id}", new AlisYaz(alis.Surum, Month, "Tedarikçi", null, [new("Mal", 1000m, [new(1, mezat), new(3, toptan)])]));
        Assert.True(guncel.IsSuccessStatusCode, await guncel.Content.ReadAsStringAsync());
        alis = (await guncel.Content.ReadFromJsonAsync<AlisDto>())!;
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/gonder", new AlisDurumYaz(alis.Surum));
        await Post<AlisDto>(c, $"/api/alis/{alis.Id}/onayla", new AlisDurumYaz(alis.Surum, "Uygun"));

        kart = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{kart.Id}"))!;
        // İade, kaynağın yeni oranıyla (iade anında ödenmemiş kısımdan) bölünür; ödeme kalan payı izler: para yanlış kanala kaymaz.
        Assert.Equal([(1, (decimal)iadeMezat), (3, iadeToptan)], kart.Harcamalar.Single(h => h.Tutar < 0).Dagilimlar.Select(p => (p.KanalId!.Value, p.Tutar)));
        Assert.Equal([(1, (decimal)odemeMezat), (3, odemeToptan)], Assert.Single(kart.Odemeler).Dagilimlar.Select(p => (p.KanalId!.Value, p.Tutar)).OrderBy(p => p.Item1));
        var rapor = (await c.GetFromJsonAsync<AylikRapor>($"/api/rapor/aylik?yil={Today.Year}&ay={Today.Month}"))!;
        Assert.Equal(((decimal)odemeMezat, (decimal)odemeToptan), (rapor.Kanallar.Single(k => k.Kanal == "MEZAT").KrediKarti, rapor.Kanallar.Single(k => k.Kanal == "TOPTAN").KrediKarti));
        Assert.Equal(0m, kart.Borc);
    }

    // --- Göç: eski iadelerin hesap kaydı, raporlar birebir aynı ---

    [Fact]
    public void Goc_bos_veritabaninda_uygulanir_ve_model_anlik_goruntusu_calisma_modeliyle_eslesir()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connection).Options);
        KasaVeritabaniBaslatici.Baslat(db);
        Assert.Contains(Migrations.KartTakipDuzeltmeleri.Kimlik, db.Database.GetAppliedMigrations());
        Assert.Empty(db.Database.GetPendingMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(db.TakipIadeHesaplari);
        Assert.Empty(db.TakipAvansTahsisleri);
    }

    [Fact]
    public async Task Goc_eski_iadelere_hesap_kaydi_yazar_raporlar_onceki_kodun_ciktisiyla_birebir_ayni()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var db = new KasaDbContext(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connection).Options))
            db.GetService<IMigrator>().Migrate("20260930000200_DenetimGecmisAktarimi");
        using (var komut = connection.CreateCommand())
        { komut.CommandText = EskiIadeVerisi; komut.ExecuteNonQuery(); }
        await using var f = new HazirFactory(connection) { Saat = new SabitSaat(EskiBugun) };
        using var c = await f.EditorClientAsync();

        foreach (var (uc, beklenen) in OncekiYanitlar)
            Assert.True(beklenen == await c.GetStringAsync(uc), $"{uc} göçten sonra değişti.");
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            // İade A (ödeme öncesi) ve C (200 TL ödendikten sonra) türetilen payla eşleşir; B'nin dondurulmuş payı kaynak oranını
            // tutmaz (tamamı MEZAT), eski kuralda kalır.
            Assert.Equal([(2, 0m, 0m), (6, 200m, 0m)], db.TakipIadeHesaplari.AsNoTracking().OrderBy(x => x.HarcamaId).AsEnumerable().Select(x => (x.HarcamaId, x.IadeAnindaOdenen, x.KasadaSayilanDuzeltme)));
        }
        // Eşleşen eski iade artık kaynak alışı izler: alış 500/500 ile yeniden onaylanınca iade ve Mart ödemesi yeni orana geçer.
        var alis = (await c.GetFromJsonAsync<List<AlisDto>>("/api/alis"))!.Single(a => a.Id == 1);
        alis = await Post<AlisDto>(c, "/api/alis/1/iade", new AlisDurumYaz(alis.Surum, "Kanal dağılımı düzeltilecek"));
        var guncel = await c.PutAsJsonAsync("/api/alis/1", new AlisYaz(alis.Surum, alis.Tarih, alis.Tedarikci, null, [new("Mal", 1000m, [new(1, 500m), new(3, 500m)])]));
        Assert.True(guncel.IsSuccessStatusCode, await guncel.Content.ReadAsStringAsync());
        alis = (await guncel.Content.ReadFromJsonAsync<AlisDto>())!;
        alis = await Post<AlisDto>(c, "/api/alis/1/gonder", new AlisDurumYaz(alis.Surum));
        await Post<AlisDto>(c, "/api/alis/1/onayla", new AlisDurumYaz(alis.Surum, "Uygun"));
        var kart = (await c.GetFromJsonAsync<KartTakipDto>("/api/takip/kartlar/1"))!;
        Assert.Equal([(1, 200m), (3, 200m)], kart.Harcamalar.Single(h => h.Id == 2).Dagilimlar.Select(p => (p.KanalId!.Value, p.Tutar)));
        Assert.Equal([(1, 300m), (3, 300m)], kart.Odemeler.Single(o => o.Id == 1).Dagilimlar.Select(p => (p.KanalId!.Value, p.Tutar)).OrderBy(p => p.Item1));
        // Eşleşmeyen eski iade dondurulmuş payıyla kalır.
        Assert.Equal([(1, 100m)], kart.Harcamalar.Single(h => h.Id == 4).Dagilimlar.Select(p => (p.KanalId!.Value, p.Tutar)));
    }

    private static readonly DateOnly EskiBugun = new(2025, 9, 25);

    /// <summary>Önceki kodun (5802ccb) <see cref="EskiIadeVerisi"/> üzerinde ürettiği yanıtlar (göç yokken, aynı sabit günde).</summary>
    private static readonly Dictionary<string, string> OncekiYanitlar = new()
    {
        ["/api/rapor/panel"] = """{"guncelKasa":8800.0,"kanallar":[{"kanal":"MEZAT","bakiye":-680.0,"kanalId":1},{"kanal":"PERAKENDE","bakiye":-80.0,"kanalId":2},{"kanal":"TOPTAN","bakiye":-440.0,"kanalId":3}],"buHaftaSonucu":0,"buAySonucu":0,"dagilimBekleyenTutar":0}""",
        ["/api/rapor/haftalik"] = """[{"donem":{"start":"2025-01-01","end":"2025-01-05","yil":2025,"ay":1},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":10000.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-01-06","end":"2025-01-12","yil":2025,"ay":1},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":10000.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-01-13","end":"2025-01-19","yil":2025,"ay":1},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":10000.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-01-20","end":"2025-01-26","yil":2025,"ay":1},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":10000.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-01-27","end":"2025-01-31","yil":2025,"ay":1},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":10000.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-02-01","end":"2025-02-02","yil":2025,"ay":2},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":10000.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-02-03","end":"2025-02-09","yil":2025,"ay":2},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":10000.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-02-10","end":"2025-02-16","yil":2025,"ay":2},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":10000.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-02-17","end":"2025-02-23","yil":2025,"ay":2},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":10000.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-02-24","end":"2025-02-28","yil":2025,"ay":2},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":0.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":10000.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-03-01","end":"2025-03-02","yil":2025,"ay":3},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":480,"sonuc":-480,"devir":-480.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":80,"sonuc":-80,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":240,"sonuc":-240,"devir":-240.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":800,"kasaSonucu":-800,"kasaDevir":9200.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-03-03","end":"2025-03-09","yil":2025,"ay":3},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-480.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-240.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":9200.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-03-10","end":"2025-03-16","yil":2025,"ay":3},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-480.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-240.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":9200.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-03-17","end":"2025-03-23","yil":2025,"ay":3},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-480.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-240.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":9200.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-03-24","end":"2025-03-30","yil":2025,"ay":3},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-480.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-240.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":9200.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-03-31","end":"2025-03-31","yil":2025,"ay":3},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-480.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-240.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":9200.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-04-01","end":"2025-04-06","yil":2025,"ay":4},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-480.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-240.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":9200.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-04-07","end":"2025-04-13","yil":2025,"ay":4},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-480.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-240.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":9200.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-04-14","end":"2025-04-20","yil":2025,"ay":4},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-480.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-240.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":9200.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-04-21","end":"2025-04-27","yil":2025,"ay":4},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-480.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-240.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":9200.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-04-28","end":"2025-04-30","yil":2025,"ay":4},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-480.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-240.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":9200.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-05-01","end":"2025-05-04","yil":2025,"ay":5},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":200,"sonuc":-200,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":200,"sonuc":-200,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":400,"kasaSonucu":-400,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-05-05","end":"2025-05-11","yil":2025,"ay":5},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-05-12","end":"2025-05-18","yil":2025,"ay":5},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-05-19","end":"2025-05-25","yil":2025,"ay":5},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-05-26","end":"2025-05-31","yil":2025,"ay":5},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-06-01","end":"2025-06-01","yil":2025,"ay":6},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-06-02","end":"2025-06-08","yil":2025,"ay":6},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-06-09","end":"2025-06-15","yil":2025,"ay":6},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-06-16","end":"2025-06-22","yil":2025,"ay":6},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-06-23","end":"2025-06-29","yil":2025,"ay":6},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-06-30","end":"2025-06-30","yil":2025,"ay":6},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-07-01","end":"2025-07-06","yil":2025,"ay":7},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-07-07","end":"2025-07-13","yil":2025,"ay":7},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-07-14","end":"2025-07-20","yil":2025,"ay":7},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-07-21","end":"2025-07-27","yil":2025,"ay":7},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-07-28","end":"2025-07-31","yil":2025,"ay":7},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-08-01","end":"2025-08-03","yil":2025,"ay":8},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-08-04","end":"2025-08-10","yil":2025,"ay":8},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-08-11","end":"2025-08-17","yil":2025,"ay":8},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-08-18","end":"2025-08-24","yil":2025,"ay":8},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-08-25","end":"2025-08-31","yil":2025,"ay":8},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-09-01","end":"2025-09-07","yil":2025,"ay":9},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-09-08","end":"2025-09-14","yil":2025,"ay":9},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-09-15","end":"2025-09-21","yil":2025,"ay":9},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0},{"donem":{"start":"2025-09-22","end":"2025-09-25","yil":2025,"ay":9},"kanallar":[{"kanal":"MEZAT","gelen":0,"giden":0,"sonuc":0,"devir":-680.0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"giden":0,"sonuc":0,"devir":-80.0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"giden":0,"sonuc":0,"devir":-440.0,"krediGirisi":0}],"toplamGelen":0,"toplamGiden":0,"kasaSonucu":0,"kasaDevir":8800.0,"dagilimBekleyenTutar":0}]""",
        ["/api/rapor/aylik?yil=2025&ay=2"] = """{"yil":2025,"ay":2,"kanallar":[{"kanal":"MEZAT","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0}],"dagilimBekleyenTutar":0,"genelGider":0,"genelGelir":0,"krediGirisi":0,"kuralSurumu":2}""",
        ["/api/rapor/aylik?yil=2025&ay=3"] = """{"yil":2025,"ay":3,"kanallar":[{"kanal":"MEZAT","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":480,"ortakPay":0,"aySonucu":-480,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":80,"ortakPay":0,"aySonucu":-80,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":240,"ortakPay":0,"aySonucu":-240,"krediGirisi":0}],"dagilimBekleyenTutar":0,"genelGider":0,"genelGelir":0,"krediGirisi":0,"kuralSurumu":2}""",
        ["/api/rapor/aylik?yil=2025&ay=4"] = """{"yil":2025,"ay":4,"kanallar":[{"kanal":"MEZAT","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0}],"dagilimBekleyenTutar":0,"genelGider":0,"genelGelir":0,"krediGirisi":0,"kuralSurumu":2}""",
        ["/api/rapor/aylik?yil=2025&ay=5"] = """{"yil":2025,"ay":5,"kanallar":[{"kanal":"MEZAT","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":200,"ortakPay":0,"aySonucu":-200,"krediGirisi":0},{"kanal":"PERAKENDE","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":0,"ortakPay":0,"aySonucu":0,"krediGirisi":0},{"kanal":"TOPTAN","gelen":0,"cariGiden":0,"sabitGider":0,"krediKarti":200,"ortakPay":0,"aySonucu":-200,"krediGirisi":0}],"dagilimBekleyenTutar":0,"genelGider":0,"genelGelir":0,"krediGirisi":0,"kuralSurumu":2}""",
        ["/api/takip/kartlar/1"] = """{"id":1,"surum":7,"ad":"Kart A","yeniTakip":true,"aktif":true,"takipBaslangic":"2025-01-01","kesimGunu":5,"sonOdemeGunu":15,"limit":50000.0,"borc":0.0,"ekstreBorc":0.0,"ekstreler":[{"id":1,"kesimTarihi":"2025-02-05","sonOdemeTarihi":"2025-02-15","borc":1000.0,"odenen":600,"kalan":0.0,"asgariOdeme":null,"asgariKalan":null},{"id":2,"kesimTarihi":"2025-03-05","sonOdemeTarihi":"2025-03-15","borc":-400.0,"odenen":0,"kalan":0,"asgariOdeme":null,"asgariKalan":null},{"id":3,"kesimTarihi":"2025-04-05","sonOdemeTarihi":"2025-04-15","borc":400.0,"odenen":400,"kalan":0.0,"asgariOdeme":null,"asgariKalan":null},{"id":0,"kesimTarihi":"2025-09-05","sonOdemeTarihi":"2025-09-15","borc":0,"odenen":0,"kalan":0,"asgariOdeme":null,"asgariKalan":null},{"id":0,"kesimTarihi":"2025-10-05","sonOdemeTarihi":"2025-10-15","borc":0,"odenen":0,"kalan":0,"asgariOdeme":null,"asgariKalan":null}],"harcamalar":[{"id":1,"islemId":1,"tarih":"2025-02-01","aciklama":"Tedarikçi A","tutar":1000.0,"taksitSayisi":1,"iptal":false,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":600},{"kanalId":3,"kanal":"TOPTAN","tutar":400}],"ekstreKayitId":null},{"id":2,"islemId":null,"tarih":"2025-02-10","aciklama":"İade A","tutar":-400.0,"taksitSayisi":1,"iptal":false,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":240},{"kanalId":3,"kanal":"TOPTAN","tutar":160}],"ekstreKayitId":null},{"id":3,"islemId":2,"tarih":"2025-04-01","aciklama":"Tedarikçi B","tutar":500.0,"taksitSayisi":1,"iptal":false,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":300},{"kanalId":3,"kanal":"TOPTAN","tutar":200}],"ekstreKayitId":null},{"id":4,"islemId":null,"tarih":"2025-04-05","aciklama":"İade B","tutar":-100.0,"taksitSayisi":1,"iptal":false,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":100}],"ekstreKayitId":null}],"odemeler":[{"id":1,"tarih":"2025-03-01","tutar":600.0,"kasaEtkisi":600,"not":null,"iptal":false,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":360},{"kanalId":3,"kanal":"TOPTAN","tutar":240}],"ekstreKayitId":null},{"id":2,"tarih":"2025-05-01","tutar":400.0,"kasaEtkisi":400,"not":"Mayıs","iptal":false,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":200},{"kanalId":3,"kanal":"TOPTAN","tutar":200}],"ekstreKayitId":null}],"kanalKartBorclari":[],"gecis":null}""",
        ["/api/takip/kartlar/2"] = """{"id":2,"surum":4,"ad":"Kart B","yeniTakip":true,"aktif":true,"takipBaslangic":"2025-01-01","kesimGunu":5,"sonOdemeGunu":15,"limit":50000.0,"borc":200.0,"ekstreBorc":200.0,"ekstreler":[{"id":4,"kesimTarihi":"2025-03-05","sonOdemeTarihi":"2025-03-15","borc":500.0,"odenen":200,"kalan":200.0,"asgariOdeme":null,"asgariKalan":null},{"id":5,"kesimTarihi":"2025-04-05","sonOdemeTarihi":"2025-04-15","borc":-100.0,"odenen":0,"kalan":0,"asgariOdeme":null,"asgariKalan":null},{"id":0,"kesimTarihi":"2025-09-05","sonOdemeTarihi":"2025-09-15","borc":0,"odenen":0,"kalan":0,"asgariOdeme":null,"asgariKalan":null},{"id":0,"kesimTarihi":"2025-10-05","sonOdemeTarihi":"2025-10-15","borc":0,"odenen":0,"kalan":0,"asgariOdeme":null,"asgariKalan":null}],"harcamalar":[{"id":5,"islemId":null,"tarih":"2025-02-20","aciklama":"Manuel","tutar":500.0,"taksitSayisi":1,"iptal":false,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":300},{"kanalId":2,"kanal":"PERAKENDE","tutar":200}],"ekstreKayitId":null},{"id":6,"islemId":null,"tarih":"2025-03-10","aciklama":"İade C","tutar":-100.0,"taksitSayisi":1,"iptal":false,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":60},{"kanalId":2,"kanal":"PERAKENDE","tutar":40}],"ekstreKayitId":null}],"odemeler":[{"id":3,"tarih":"2025-03-01","tutar":200.0,"kasaEtkisi":200,"not":null,"iptal":false,"dagilimlar":[{"kanalId":1,"kanal":"MEZAT","tutar":120},{"kanalId":2,"kanal":"PERAKENDE","tutar":80}],"ekstreKayitId":null}],"kanalKartBorclari":[{"kanalId":1,"kanal":"MEZAT","tutar":120},{"kanalId":2,"kanal":"PERAKENDE","tutar":80}],"gecis":null}""",
        ["/api/takip/ozet"] = """{"tarih":"2025-09-25","kartBorcu":200.0,"kalanKrediPlani":0,"olaylar":[{"kaynak":"Kart","kaynakId":2,"kalemId":4,"ad":"Kart B","tarih":"2025-03-15","tutar":200.0,"tur":"SonOdeme","otomatikKasa":false},{"kaynak":"Kart","kaynakId":1,"kalemId":0,"ad":"Kart A","tarih":"2025-10-05","tutar":0,"tur":"Kesim","otomatikKasa":false},{"kaynak":"Kart","kaynakId":2,"kalemId":0,"ad":"Kart B","tarih":"2025-10-05","tutar":0,"tur":"Kesim","otomatikKasa":false}],"kanalKartBorclari":[{"kanalId":1,"kanal":"MEZAT","tutar":120},{"kanalId":2,"kanal":"PERAKENDE","tutar":80}],"kartAlacakBakiyesi":0}""",
    };

    // Göçten önceki şemada, önceki kodun yazdığı biçimde (dondurulmuş iade payları) takipli kart verisi. Ham SQL: çalışma
    // zamanı modeli yeni sütunlar taşır, eski şemaya EF ile yazılamaz.
    private const string EskiIadeVerisi = """
        INSERT INTO Kanallar (Id, Ad, Aktif, Sira, AcilisDevri) VALUES (1, 'MEZAT', 1, 0, '0.0'), (2, 'PERAKENDE', 1, 1, '0.0'), (3, 'TOPTAN', 1, 2, '0.0');
        INSERT INTO Ayarlar (Id, TakipBaslangic, KasaAcilisDevri) VALUES (1, '2025-01-01', '10000.0');
        INSERT INTO KrediKartlari (Id, Ad, KesimTarihi, SonOdemeTarihi, "Limit", Borc) VALUES (1, 'Kart A', '2000-01-05', '2000-01-15', '50000.0', '0.0'), (2, 'Kart B', '2000-01-05', '2000-01-15', '50000.0', '0.0');
        INSERT INTO TakipKartlar (KrediKartiId, Surum, Baslangic, Aktif, EskiKayit) VALUES (1, 7, '2025-01-01', 1, 0), (2, 4, '2025-01-01', 1, 0);
        INSERT INTO Islemler (Id, Tarih, Cari, TutarTl, Kanal, Tip, KrediKartiId) VALUES (1, '2025-02-01', 'Tedarikçi A', '1000.0', 'Dağılım bekliyor', 2, 1), (2, '2025-04-01', 'Tedarikçi B', '500.0', 'Dağılım bekliyor', 2, 1);
        INSERT INTO Alislar (Id, Surum, Tarih, Tedarikci, Durum) VALUES (1, 4, '2025-02-01', 'Tedarikçi A', 'Onaylandi'), (2, 4, '2025-04-01', 'Tedarikçi B', 'Onaylandi');
        INSERT INTO AlisKalemler (Id, AlisId, Aciklama, Tutar) VALUES (1, 1, 'Mal', '1000.0'), (2, 2, 'Mal', '500.0');
        INSERT INTO AlisDagilimlar (Id, AlisKalemId, KanalId, Tutar) VALUES (1, 1, 1, '600.0'), (2, 1, 3, '400.0'), (3, 2, 1, '300.0'), (4, 2, 3, '200.0');
        INSERT INTO AlisOdemeler (Id, AlisId, IslemId, IstekId, IstekOzeti) VALUES (1, 1, 1, '6f1c7c1e-1d7b-4a33-9d1c-000000000001', 'eski'), (2, 2, 2, '6f1c7c1e-1d7b-4a33-9d1c-000000000002', 'eski');
        INSERT INTO TakipEkstreler (Id, KrediKartiId, KesimTarihi, SonOdemeTarihi) VALUES (1, 1, '2025-02-05', '2025-02-15'), (2, 1, '2025-03-05', '2025-03-15'), (3, 1, '2025-04-05', '2025-04-15'),
            (4, 2, '2025-03-05', '2025-03-15'), (5, 2, '2025-04-05', '2025-04-15');
        INSERT INTO TakipHarcamalar (Id, KrediKartiId, IslemId, KaynakHarcamaId, Tarih, Aciklama, Tutar, TaksitSayisi, Iptal, DagilimJson, KasadaOncedenSayilanTutar) VALUES
            (1, 1, 1, NULL, '2025-02-01', 'Tedarikçi A', '1000.0', 1, 0, '[]', '0.0'),
            (2, 1, NULL, 1, '2025-02-10', 'İade A', '-400.0', 1, 0, '[{"KanalId":1,"Tutar":240},{"KanalId":3,"Tutar":160}]', '0.0'),
            (3, 1, 2, NULL, '2025-04-01', 'Tedarikçi B', '500.0', 1, 0, '[]', '0.0'),
            (4, 1, NULL, 3, '2025-04-05', 'İade B', '-100.0', 1, 0, '[{"KanalId":1,"Tutar":100}]', '0.0'),
            (5, 2, NULL, NULL, '2025-02-20', 'Manuel', '500.0', 1, 0, '[{"KanalId":1,"Tutar":300},{"KanalId":2,"Tutar":200}]', '0.0'),
            (6, 2, NULL, 5, '2025-03-10', 'İade C', '-100.0', 1, 0, '[{"KanalId":1,"Tutar":60},{"KanalId":2,"Tutar":40}]', '0.0');
        INSERT INTO TakipKartTaksitler (Id, HarcamaId, EkstreId, Tutar) VALUES (1, 1, 1, '1000.0'), (2, 2, 2, '-400.0'), (3, 3, 3, '500.0'), (4, 4, 3, '-100.0'), (5, 5, 4, '500.0'), (6, 6, 5, '-100.0');
        INSERT INTO TakipKartOdemeler (Id, KrediKartiId, Tarih, Tutar, "Not", Iptal, PaylarJson) VALUES
            (1, 1, '2025-03-01', '600.0', NULL, 0, '[{"TaksitId":1,"Tutar":600,"OncedenOdenen":0}]'),
            (2, 1, '2025-05-01', '400.0', 'Mayıs', 0, '[{"TaksitId":3,"Tutar":400,"OncedenOdenen":0}]'),
            (3, 2, '2025-03-01', '200.0', NULL, 0, '[{"TaksitId":5,"Tutar":200,"OncedenOdenen":0}]');
        """;

    // Eski kartı (kesim 5, son ödeme 15) 10 Temmuz tarihli eski kural kart gideriyle kurar ve bugün yeni takibe geçirir.
    private static async Task<KartTakipDto> GecisliKart(KasaWebFactory f, HttpClient c, decimal eskiGider, decimal kalanBorc, decimal oncedenSayilan, decimal acilisBorcu = 0m)
    {
        int id;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var kart = new KrediKartiEntity { Ad = "Eski kart", KesimTarihi = new(2000, 1, 5), SonOdemeTarihi = new(2000, 1, 15), Limit = 50000m, Borc = acilisBorcu };
            db.KrediKartlari.Add(kart);
            db.SaveChanges();
            id = kart.Id;
            db.Islemler.Add(new IslemEntity { Tarih = new(Today.Year, 7, 10), Cari = "Eski kart gideri", TutarTl = eskiGider, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.KrediKarti, KrediKartiId = id });
            db.SaveChanges();
        }
        return await Post<KartTakipDto>(c, $"/api/takip/kartlar/{id}/gecis", new KartGecisYaz(Guid.NewGuid(), 0, Today, kalanBorc, oncedenSayilan, [new(1, kalanBorc)], "Banka borcu doğrulandı", true));
    }
    private static async Task Kilit(HttpClient c, DateOnly ay, bool kapat)
    {
        var durum = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi"))!;
        await Post<AyKilidiDto>(c, kapat ? "/api/ay-kilidi/kapat" : "/api/ay-kilidi/ac", new AyKilidiYaz(Guid.NewGuid(), durum.Surum, ay.Year, ay.Month, kapat ? "Ay tamamlandı" : "Avans kaydı düzeltilecek"));
    }
    // Haftalık raporun verilen aya ait dönem satırları (JSON metni).
    private static async Task<string> KilitliHaftalar(HttpClient c, DateOnly ay) => string.Join(",", JsonNode.Parse(await c.GetStringAsync("/api/rapor/haftalik"))!.AsArray()
        .Where(d => d!["donem"]!["yil"]!.GetValue<int>() == ay.Year && d["donem"]!["ay"]!.GetValue<int>() == ay.Month).Select(d => d!.ToJsonString()));

    // Uygulamayı önceden hazırlanmış (eski şemalı) bağlantı üzerinde başlatır; açılışta göç ve veri adımı çalışır.
    private sealed class HazirFactory(SqliteConnection hazir) : KasaWebFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<KasaDbContext>>();
                services.AddDbContext<KasaDbContext>(o => o.UseSqlite(hazir));
            });
        }
    }
}
