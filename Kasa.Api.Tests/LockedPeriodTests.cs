using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.MonthlyExpenseTests;

namespace Kasa.Api.Tests;

public class LockedPeriodTests
{
    private static DateOnly Old => Month.AddMonths(-1);

    [Fact]
    public async Task Acik_tarihli_alis_odemesi_duzeltilerek_daha_sonra_girilen_kapali_odemenin_kurusu_tasinamaz()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var purchase = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Old, "Kuruş", null, [new("Mal", .03m, [new(1, .01m), new(2, .02m)])]));
        purchase = await Post<AlisDto>(c, $"/api/alis/{purchase.Id}/odemeler", new AlisOdemeYaz(purchase.Surum, Guid.NewGuid(), Today, .01m));
        var first = purchase.Odemeler.Single();
        purchase = await Post<AlisDto>(c, $"/api/alis/{purchase.Id}/odemeler", new AlisOdemeYaz(purchase.Surum, Guid.NewGuid(), Old, .01m));
        purchase = await Post<AlisDto>(c, $"/api/alis/{purchase.Id}/gonder", new AlisDurumYaz(purchase.Surum));
        purchase = await Post<AlisDto>(c, $"/api/alis/{purchase.Id}/onayla", new AlisDurumYaz(purchase.Surum));
        var url = $"/api/rapor/aylik?yil={Old.Year}&ay={Old.Month}"; var before = await c.GetStringAsync(url);
        await Close(c);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/alis/{purchase.Id}/odemeler/{first.Id}",
            new AlisOdemeDuzelt(purchase.Surum, Guid.NewGuid(), Today, .02m, "Önceki ödeme artışı"))).StatusCode);
        Assert.Equal(before, await c.GetStringAsync(url));
    }

    [Fact]
    public async Task Once_girilmis_acik_ay_odemesi_iptal_edilerek_sonradan_girilen_kilitli_odemenin_kurusu_tasinamaz()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var card = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Kuruş", 1000m, 5, 25, Old, .02m, [new(1, .01m), new(2, .01m)]));
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, .01m));
        var earlierId = card.Odemeler.Single().Id;
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Old, .01m));
        var url = $"/api/rapor/aylik?yil={Old.Year}&ay={Old.Month}";
        var before = await c.GetStringAsync(url);
        await Close(c);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/odemeler/{earlierId}/iptal", new TakipIptalYaz(Guid.NewGuid(), card.Surum, "Ters tarih sırası"))).StatusCode);
        Assert.Equal(before, await c.GetStringAsync(url));
    }

    [Fact]
    public async Task Kanal_sirasi_kapali_ayin_ortak_gider_kurusunu_baska_kanala_tasiyamaz()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old, "Ortak kuruş", .01m, Kanallar.Ortak, GiderTipi.Cari));
        var url = $"/api/rapor/aylik?yil={Old.Year}&ay={Old.Month}";
        var before = await c.GetStringAsync(url);
        await Close(c);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", Sira: 99))).StatusCode);
        Assert.Equal(before, await c.GetStringAsync(url));
    }

    [Fact]
    public async Task Kilit_tarihli_hareket_gelir_rawsql_ve_dolayli_acilis_degisikliklerini_engeller()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var expense = await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old, "Önceki ay", 50m, "MEZAT", GiderTipi.Cari));
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Old, "MEZAT", 100m))).EnsureSuccessStatusCode();
        await Close(c);
        var report = await c.GetStringAsync("/api/rapor/panel");
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Old, "Geçmiş", 10m, "MEZAT", GiderTipi.Cari))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/islemler/{expense.Id}", new IslemYazDto(Today, "İleri taşı", 50m, "MEZAT", GiderTipi.Cari))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/islemler/{expense.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Old, "MEZAT", 200m))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Old, "PERAKENDE", 200m))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", false))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", AcilisDevri: 99m))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Month.AddMonths(-3), kasaAcilisDevri = 2000m })).StatusCode);
        Assert.Equal(report, await c.GetStringAsync("/api/rapor/panel"));
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Today, "Cari ay açık", 20m, "MEZAT", GiderTipi.Cari))).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Month, "MEZAT", 20m))).EnsureSuccessStatusCode();
        Assert.Equal(1050m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Gerekceli_acma_surum_ve_istek_kimligiyle_guvenlidir_onceki_aylar_kilitli_kalir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var state = await Close(c);
        var request = new AyKilidiYaz(Guid.NewGuid(), state.Surum, Old.Year, Old.Month, "Eksik dekont için açıldı");
        state = await Post<AyKilidiDto>(c, "/api/ay-kilidi/ac", request);
        Assert.Equal(Old.AddDays(-1), state.KilitliSonTarih);
        var replay = await Post<AyKilidiDto>(c, "/api/ay-kilidi/ac", request);
        Assert.Equal(state.Surum, replay.Surum); Assert.Equal(2, replay.Gecmis.Count);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/ay-kilidi/kapat", request with { IstekId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), state.Surum, Today.Year, Today.Month, "Henüz bitmedi"))).StatusCode);
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Old, "Açılan ay", 10m, "MEZAT", GiderTipi.Cari))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Old.AddDays(-1), "Daha eski", 10m, "MEZAT", GiderTipi.Cari))).StatusCode);
    }

    [Fact]
    public async Task Eski_context_ile_SaveChangesAsync_yeni_kilidi_atlayamaz()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var expense = await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Old, "Kayıt", 10m, "MEZAT", GiderTipi.Cari));
        using var stale = f.Services.CreateScope(); var db = stale.ServiceProvider.GetRequiredService<KasaDbContext>();
        var loaded = await db.Islemler.SingleAsync(i => i.Id == expense.Id);
        await Close(c);
        loaded.TutarTl = 99m;
        await Assert.ThrowsAsync<KilitliDonemException>(() => db.SaveChangesAsync());
        Assert.Equal(990m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Kapali_ayin_aylik_odemesi_iptal_edilemez_okunabilir_ve_ileri_sablon_eklenebilir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        int payment;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var t = new AylikGiderSablonEntity(); db.AylikGiderSablonlar.Add(t); db.SaveChanges();
            var r = new AylikGiderRevizyonEntity { SablonId = t.Id, Surum = 1, Ad = "Eski kira", Tur = "Kira", Tutar = 100, OdemeGunu = 1, GecerliAy = Old };
            var i = new IslemEntity { Tarih = Old, TutarTl = 100, Cari = "Eski kira", Tip = GiderTipi.SabitGider, Kanal = "Genel kasa" };
            db.AylikGiderRevizyonlar.Add(r); db.Islemler.Add(i); db.SaveChanges();
            var p = new AylikGiderOdemeEntity { SablonId = t.Id, RevizyonId = r.Id, Ay = Old, Tarih = Old, Tutar = 100, IslemId = i.Id };
            db.AylikGiderOdemeler.Add(p); db.SaveChanges(); payment = p.Id;
        }
        await Close(c);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/aylik-giderler/odemeler/{payment}/iptal", new AylikGiderIptalYaz(Guid.NewGuid(), "Eskiyi iptal"))).StatusCode);
        var rows = (await c.GetFromJsonAsync<AylikGiderAyDto>($"/api/aylik-giderler?yil={Old.Year}&ay={Old.Month}"))!;
        Assert.Equal(100m, rows.OdenenToplam); Assert.Equal(900m, (await Panel(c)).GuncelKasa);
        await Create(c, "Genel", []);
    }

    [Fact]
    public async Task Eski_alis_odemelerinin_onay_ve_kanal_paylari_kilitli_ama_yeni_odeme_aciktir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var draft = await Purchase(c);
        draft = await Post<AlisDto>(c, $"/api/alis/{draft.Id}/odemeler", new AlisOdemeYaz(draft.Surum, Guid.NewGuid(), Old, 40m));
        draft = await Post<AlisDto>(c, $"/api/alis/{draft.Id}/gonder", new AlisDurumYaz(draft.Surum));
        var approved = await Purchase(c);
        approved = await Post<AlisDto>(c, $"/api/alis/{approved.Id}/odemeler", new AlisOdemeYaz(approved.Surum, Guid.NewGuid(), Old, 40m));
        approved = await Post<AlisDto>(c, $"/api/alis/{approved.Id}/gonder", new AlisDurumYaz(approved.Surum));
        approved = await Post<AlisDto>(c, $"/api/alis/{approved.Id}/onayla", new AlisDurumYaz(approved.Surum));
        await Close(c);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/alis/{draft.Id}/onayla", new AlisDurumYaz(draft.Surum))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/alis/{approved.Id}/iade", new AlisDurumYaz(approved.Surum, "Pay değiştirme"))).StatusCode);
        approved = await Post<AlisDto>(c, $"/api/alis/{approved.Id}/odemeler", new AlisOdemeYaz(approved.Surum, Guid.NewGuid(), Today, 20m));
        Assert.Equal(60m, approved.Odenen); Assert.Equal(900m, (await Panel(c)).GuncelKasa);
        using var scope = f.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var part = db.AlisDagilimlar.Include(d => d.KanalKaydi).First(d => d.AlisKalemId == approved.Kalemler.Single().Id);
        part.KanalId = 2;
        Assert.Throws<KilitliDonemException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task Kilitli_kart_avansi_yeni_harcamaya_sessiz_baglanmaz_okumalar_calismaya_devam_eder()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var card = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Avans", 1000m, 5, 25, Old, 0m, []));
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Old, 50m));
        await Close(c);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Today, "Avansı dağıtacak", 100m, 1, null, [new(1, 100m)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Today, "Aynı kaçış", 100m, "MEZAT", GiderTipi.KrediKarti, KrediKartiId: card.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/odemeler/{card.Odemeler.Single().Id}/iptal", new TakipIptalYaz(Guid.NewGuid(), card.Surum, "Eski avans"))).StatusCode);
        var read = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}"))!;
        Assert.Empty(read.Harcamalar); Assert.Equal(-50m, read.Borc); Assert.Equal(950m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Eski_kart_kaynak_iadesi_gecmis_payi_oynatamaz_fakat_cari_ay_odeme_yapilabilir()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f);
        var card = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Borç", 1000m, 5, 25, Old, 100m, [new(1, 100m)]));
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Old, 20m));
        await Close(c);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Today, "Geçmişe bağlı iade", -10m, 1, null, [], card.Harcamalar.Single().Id))).StatusCode);
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, 30m));
        Assert.Equal(50m, card.Borc); Assert.Equal(950m, (await Panel(c)).GuncelKasa);
    }

    [Fact]
    public async Task Mevcut_kredi_ileri_plani_kilitli_cekim_tarihine_ragmen_eklenebilir_yeni_cekim_yazilamaz()
    {
        await using var f = new KasaWebFactory(); using var c = await Editor(f); await Close(c);
        var input = new KrediTakipYaz(Guid.NewGuid(), "Mevcut kredi", 1000m, Old, Today.AddMonths(1), 3, 100m, [1, 2], true);
        var loan = await Post<KrediTakipDto>(c, "/api/takip/krediler", input);
        Assert.Equal(300m, loan.KalanPlanliOdeme); Assert.Equal(1000m, (await Panel(c)).GuncelKasa);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/takip/krediler", input with { IstekId = Guid.NewGuid(), MevcutKredi = false })).StatusCode);
        var first = loan.Taksitler.First();
        var update = await c.PutAsJsonAsync($"/api/takip/krediler/{loan.Id}/taksitler/{first.Id}", new KrediTaksitYaz(Guid.NewGuid(), loan.Surum, first.Tarih.AddDays(1), 90m, null, false, "Yeni banka planı"));
        update.EnsureSuccessStatusCode(); Assert.Equal(1000m, (await Panel(c)).GuncelKasa);
    }

    private static async Task<AyKilidiDto> Close(HttpClient c)
    {
        var state = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi"))!;
        return await Post<AyKilidiDto>(c, "/api/ay-kilidi/kapat", new AyKilidiYaz(Guid.NewGuid(), state.Surum, Old.Year, Old.Month, "Ay tamamlandı"));
    }
    private static Task<AlisDto> Purchase(HttpClient c) => Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Old, "Satıcı", null, [new("Mal", 100m, [new(1, 100m)])]));
}
