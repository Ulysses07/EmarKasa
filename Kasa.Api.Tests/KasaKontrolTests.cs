using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public class KasaKontrolTests
{
    // Takvim sınırları (tests-1): aynı testler yıl başında, artık yılın Şubat sonunda ve kırpılan ay sonunda da koşar.
    public sealed class YilBasi() : KasaKontrolTests(new(2027, 1, 1));
    public sealed class ArtikYilSubatSonu() : KasaKontrolTests(new(2028, 2, 29));
    public sealed class KirpilanAySonu() : KasaKontrolTests(new(2027, 3, 31));

    public KasaKontrolTests() : this(KasaWebFactory.VarsayilanBugun) { }
    private KasaKontrolTests(DateOnly bugun) => Today = bugun;
    private DateOnly Today { get; }
    // Takip başlangıcı bugünün ayından 8 ay önce: varsayılan günde 1 Ocak 2026; masraf yazılan ekstre her günde kesilmiş olur.
    private DateOnly Start => new DateOnly(Today.Year, Today.Month, 1).AddMonths(-8);
    private KasaWebFactory Factory() => KasaWebFactory.Sabit(Today);

    [Fact]
    public async Task Kart_masrafi_kalan_borca_dagilir_kasayi_odemeye_kadar_degistirmez_ve_tekrar_cogaltilmaz()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, [new(1, 60m), new(2, 40m)]);
        card = await Pay(c, card, 20m);
        var before = await c.GetStringAsync("/api/rapor/panel", TestContext.Current.CancellationToken);
        var request = Fee(card, 10m);
        var preview = await Post<KartMasrafOnizlemeDto>(c, $"/api/takip/kartlar/{card.Id}/masraf-onizleme", request);
        Assert.Equal(80m, preview.DevredenBorc);
        Shares(preview.Dagilimlar, (1, 6m), (2, 4m));
        request = request with { DagilimOzeti = preview.DagilimOzeti };
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/masraflar", request);
        Shares(card.KanalKartBorclari!, (1, 54m), (2, 36m));
        Assert.Equal(90m, card.Borc);
        Assert.Equal(before, await c.GetStringAsync("/api/rapor/panel", TestContext.Current.CancellationToken));
        var retry = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/masraflar", request);
        Assert.Equal(card.Surum, retry.Surum);
        Assert.Equal(2, retry.Harcamalar.Count);
        card = await Pay(c, card, 90m);
        Assert.Equal(0m, card.Borc);
        Assert.Equal(890m, await Cash(c));
    }

    [Fact]
    public async Task Faiz_gelecek_taksitleri_agirlik_olarak_kullanmaz()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, [new(1, 100m)]);
        card = await Charge(c, card, 200m, [new(2, 200m)], 2);
        var request = Fee(card, 10m);
        var preview = await Post<KartMasrafOnizlemeDto>(c, $"/api/takip/kartlar/{card.Id}/masraf-onizleme", request);
        Assert.Equal(200m, preview.DevredenBorc);
        Shares(preview.Dagilimlar, (1, 5m), (2, 5m));
    }

    [Fact]
    public async Task Bankanin_masrafi_kalan_anaparayi_asabilir_oranlar_korunur()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var card = await Charge(c, await Card(c), .03m, [new(1, .01m), new(2, .02m)]);
        var request = Fee(card, 1m);
        var preview = await Post<KartMasrafOnizlemeDto>(c, $"/api/takip/kartlar/{card.Id}/masraf-onizleme", request);
        Assert.Equal(.03m, preview.DevredenBorc);
        Shares(preview.Dagilimlar, (1, .33m), (2, .67m));
        card = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/masraflar", request with { DagilimOzeti = preview.DagilimOzeti });
        Assert.Equal(1.03m, card.Borc);
        Assert.Equal(1000m, await Cash(c));
        var single = await Charge(c, await Card(c), .01m, [new(1, .01m)]);
        var second = await Post<KartMasrafOnizlemeDto>(c, $"/api/takip/kartlar/{single.Id}/masraf-onizleme", Fee(single, 1m));
        Shares(second.Dagilimlar, (1, 1m));
    }

    [Fact]
    public async Task Masraf_onizlemesinden_sonra_odeme_yapilirsa_eski_onizleme_kaydedilemez()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, [new(1, 60m), new(2, 40m)]);
        var request = Fee(card, 10m);
        var preview = await Post<KartMasrafOnizlemeDto>(c, $"/api/takip/kartlar/{card.Id}/masraf-onizleme", request);
        card = await Pay(c, card, 10m);
        // Even when a client supplies the new version, the old allocation digest is rejected.
        request = request with { Surum = card.Surum, DagilimOzeti = preview.DagilimOzeti };
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/masraflar", request, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        var after = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}", cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Single(after.Harcamalar);
        Assert.Equal(90m, after.Borc);
    }

    [Fact]
    public async Task Dagilimi_bekleyen_alis_borcuna_tahmini_faiz_payi_yazilamaz()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var card = await Card(c);
        var purchase = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Start, "Firma", null, [new("Mal", 100m, [new(1, 100m)])]));
        await Post<AlisDto>(c, $"/api/alis/{purchase.Id}/odemeler", new AlisOdemeYaz(purchase.Surum, Guid.NewGuid(), Start, 100m, card.Id));
        card = (await c.GetFromJsonAsync<KartTakipDto>($"/api/takip/kartlar/{card.Id}", cancellationToken: TestContext.Current.CancellationToken))!;
        var response = await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/masraf-onizleme", Fee(card, 5m), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1000m, await Cash(c));
    }

    [Fact]
    public async Task Baska_kartin_ekstresi_ve_odenmis_ekstreye_masraf_reddedilir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = Factory();
        using var c = await Editor(f);
        var card = await Charge(c, await Card(c), 100m, [new(1, 100m)]);
        var other = await Charge(c, await Card(c), 100m, [new(2, 100m)]);
        var request = Fee(card, 5m) with { EkstreId = other.Ekstreler.First(e => e.Borc > 0).Id };
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/masraf-onizleme", request, cancellationToken: ct)).StatusCode);
        var statement = Fee(card, 5m).EkstreId;
        card = await Pay(c, card, 100m);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/takip/kartlar/{card.Id}/masraf-onizleme", new KartMasrafYaz(Guid.NewGuid(), card.Surum, statement, Today, 5m, "Faiz"), cancellationToken: ct)).StatusCode);
    }

    [Fact]
    public async Task Kasa_kontrolu_farki_saklar_bakiyeyi_degistirmez_ve_tekrar_tek_kayittir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var before = await c.GetStringAsync("/api/rapor/panel", TestContext.Current.CancellationToken);
        var preview = await Post<KasaKontrolOnizlemeDto>(c, "/api/kasa-kontrol/onizleme", new KasaKontrolOnizle(-100m, "Sayım"));
        Assert.Equal(1000m, preview.SistemBakiye);
        Assert.Equal(-1100m, preview.Fark);
        var request = new KasaKontrolYaz(Guid.NewGuid(), -100m, preview.KontrolOzeti, "Sayım");
        var saved = await Post<KasaKontrolDto>(c, "/api/kasa-kontrol", request);
        var retry = await Post<KasaKontrolDto>(c, "/api/kasa-kontrol", request);
        // Kanal bakiyeleri liste alanıdır (kayıt eşitliği başvuruyu karşılaştırır): ayrıca öğe öğe karşılaştırılır.
        Assert.Equal(saved with { KanalBakiyeleri = null }, retry with { KanalBakiyeleri = null });
        Assert.Equal(saved.KanalBakiyeleri, retry.KanalBakiyeleri);
        Assert.Equal(before, await c.GetStringAsync("/api/rapor/panel", TestContext.Current.CancellationToken));
        Assert.Single((await c.GetFromJsonAsync<KasaKontrolDto[]>("/api/kasa-kontrol", cancellationToken: TestContext.Current.CancellationToken))!);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/kasa-kontrol", request with { GercekBakiye = 0 }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Kasa_onizlemesi_sonrasi_bakiye_degisirse_yeniden_karsilastirma_gerekir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = Factory();
        using var c = await Editor(f);
        var preview = await Post<KasaKontrolOnizlemeDto>(c, "/api/kasa-kontrol/onizleme", new KasaKontrolOnizle(1000m));
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Start, kasaAcilisDevri = 900m }, cancellationToken: ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/kasa-kontrol", new KasaKontrolYaz(Guid.NewGuid(), 1000m, preview.KontrolOzeti), cancellationToken: ct)).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<KasaKontrolDto[]>("/api/kasa-kontrol", cancellationToken: ct))!);
    }

    // gap-denetim-izi-gozlemlenebilirlik-3: fark sıfırdan farklıyken açıklama zorunludur. Açıklama önizleme özetine girmez: fark
    // görüldükten sonra yazılabilir.
    [Fact]
    public async Task Fark_varken_aciklama_zorunlu_fark_yokken_istege_bagli()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var preview = await Post<KasaKontrolOnizlemeDto>(c, "/api/kasa-kontrol/onizleme", new KasaKontrolOnizle(900m));
        Assert.Equal(-100m, preview.Fark);
        foreach (var not in new string?[] { null, "   " })
        {
            var r = await c.PostAsJsonAsync("/api/kasa-kontrol", new KasaKontrolYaz(Guid.NewGuid(), 900m, preview.KontrolOzeti, not), cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("Fark varsa açıklama girin.", (string)JsonNode.Parse(await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!["errors"]!["not"]![0]!);
        }
        Assert.Empty((await c.GetFromJsonAsync<KasaKontrolDto[]>("/api/kasa-kontrol", cancellationToken: TestContext.Current.CancellationToken))!);
        var saved = await Post<KasaKontrolDto>(c, "/api/kasa-kontrol", new KasaKontrolYaz(Guid.NewGuid(), 900m, preview.KontrolOzeti, "Kasada 100 TL eksik"));
        Assert.Equal((-100m, "Kasada 100 TL eksik"), (saved.Fark, saved.Not));
        var esit = await Post<KasaKontrolOnizlemeDto>(c, "/api/kasa-kontrol/onizleme", new KasaKontrolOnizle(1000m));
        var sifir = await Post<KasaKontrolDto>(c, "/api/kasa-kontrol", new KasaKontrolYaz(Guid.NewGuid(), 1000m, esit.KontrolOzeti));
        Assert.Equal((0m, (string?)null), (sifir.Fark, sifir.Not));
    }

    [Fact]
    public async Task Kayit_filigrani_hesap_gunu_kanal_bakiyeleri_ve_son_kimlikleri_tasir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        await Gider(c, Today.AddDays(-1), "Nakliye", 40m, "MEZAT");
        await Charge(c, await Card(c), 100m, [new(1, 60m), new(2, 40m)]);
        var panel = (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel", cancellationToken: TestContext.Current.CancellationToken))!;
        var preview = await Post<KasaKontrolOnizlemeDto>(c, "/api/kasa-kontrol/onizleme", new KasaKontrolOnizle(panel.GuncelKasa));
        Assert.Equal(Today, preview.HesapTarihi);
        Assert.Equal(panel.Kanallar.Select(k => (k.KanalId, k.Kanal, k.Bakiye)), preview.KanalBakiyeleri!.Select(k => (k.KanalId, k.Kanal, k.Bakiye)));
        var saved = await Post<KasaKontrolDto>(c, "/api/kasa-kontrol", new KasaKontrolYaz(Guid.NewGuid(), panel.GuncelKasa, preview.KontrolOzeti));
        Assert.Equal((1, (DateOnly?)Today), (saved.Surum, saved.HesapTarihi));
        Assert.Equal(preview.KanalBakiyeleri!.Select(k => (k.KanalId, k.Kanal, k.Bakiye)), saved.KanalBakiyeleri!.Select(k => (k.KanalId, k.Kanal, k.Bakiye)));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var row = db.KasaKontrolleri.Single();
            Assert.Equal(db.Islemler.Max(i => i.Id), row.SonIslemId);
            // Filigran bu kontrolün kendi isteğinden ve denetim olayından öncedir.
            var istek = db.FinansIstekler.Single(x => x.Tur == "KasaKontrol" && x.SonucId == row.Id);
            Assert.Equal(db.FinansIstekler.Where(x => x.Id < istek.Id).Max(x => x.Id), row.SonFinansIstekId);
            var olay = db.DenetimOlaylari.Single(o => o.Varlik == "KasaKontrol" && o.VarlikId == row.Id.ToString());
            Assert.Equal(db.DenetimOlaylari.Where(o => o.Id < olay.Id).Max(o => o.Id), row.SonDenetimOlayId);
        }
        // Değişiklik yoksa liste aynı günün bakiyesini aynı bulur.
        var liste = Assert.Single((await c.GetFromJsonAsync<KasaKontrolDto[]>("/api/kasa-kontrol", cancellationToken: TestContext.Current.CancellationToken))!);
        Assert.Equal(((decimal?)panel.GuncelKasa, (decimal?)0m, false), (liste.GuncelSistemBakiye, liste.GuncelFark, liste.SonradanDegisti));
        Assert.All(liste.KanalBakiyeleri!, k => Assert.Equal(k.Bakiye, k.GuncelBakiye));
    }

    // Senaryo (bulgu): kontrolden sonra kontrol gününden önce tarihli gider silinir, kart ödemesi iptal edilir, kontrol gününe yeni gider
    // girilir; tarih ilerleyince kredi taksidi yazma olmadan kasaya işler. "Kontrolden beri değişenler" hepsini kaynağıyla verir.
    [Fact]
    public async Task Kontrolden_sonra_silinen_gider_iptal_edilen_kart_odemesi_ve_tarihi_gelen_kredi_taksidi_listelenir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var gider = await Gider(c, Today.AddDays(-2), "Mükerrer nakliye", 1500m, "MEZAT");
        var card = await Pay(c, await Charge(c, await Card(c), 100m, [new(1, 100m)]), 20m);
        var odeme = Assert.Single(card.Odemeler);
        await Post<KrediTakipDto>(c, "/api/takip/krediler", new KrediTakipYaz(Guid.NewGuid(), "Takip kredisi", 12_000m, Start, Today.AddDays(2), 12, 1_000m, [1, 2]));
        var sistem = await Cash(c);
        var kontrol = await Kontrol(c, sistem - 1500m, "Kasada 1.500 eksik");

        Assert.Equal(HttpStatusCode.NoContent, (await c.SilIslemAsync(gider, TestContext.Current.CancellationToken)).StatusCode);
        var iptal = new TakipIptalYaz(Guid.NewGuid(), card.Surum, "Mükerrer ödeme");
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler/{odeme.Id}/iptal", iptal);
        var geriye = await Gider(c, Today, "Unutulan fatura", 70m, "PERAKENDE");
        ((SabitSaat)f.Saat!).Ayarla(Today.AddDays(3));

        var sonra = (await c.GetFromJsonAsync<KasaKontrolSonrasiDto>($"/api/kasa-kontrol/{kontrol.Id}/sonrasi", cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal((true, Today, false), (sonra.FiligranVar, sonra.EsasTarih, sonra.Kirpildi));
        Assert.Contains(sonra.Degisiklikler, o => (o.Varlik, o.VarlikId, o.Tur) == ("Islem", gider.ToString(), "Sil"));
        Assert.Contains(sonra.Degisiklikler, o => (o.Varlik, o.VarlikId, o.Tur) == ("TakipKartOdeme", odeme.Id.ToString(), "Degistir"));
        Assert.Contains(sonra.Degisiklikler, o => (o.Varlik, o.VarlikId, o.Tur) == ("Islem", geriye.ToString(), "Ekle"));
        Assert.DoesNotContain(sonra.Degisiklikler, o => o.Varlik is "KasaKontrol" or "Oturum");
        Assert.Contains(sonra.Istekler, x => x.IstekId == iptal.IstekId);
        Assert.DoesNotContain(sonra.Istekler, x => x.Tur == "KasaKontrol");
        // Kendiliğinden işleyen kredi taksidi (kanal paylarıyla) ve kontrol gününe sonradan girilen gider.
        var taksit = sonra.Hareketler.Where(h => h.Tur == "KrediTaksidi").ToList();
        Assert.Equal(-1_000m, taksit.Sum(h => h.GenelKasaEtkisi));
        Assert.All(taksit, h => Assert.True(h.Otomatik && h.EtkiTarihi == Today.AddDays(2) && h.KaynakAnahtari!.StartsWith("TakipKrediTaksit:"), h.ToString()));
        var yeni = Assert.Single(sonra.Hareketler, h => h.KaynakAnahtari == $"Islem:{geriye}");
        Assert.Equal((Today, -70m, false, "Gider"), (yeni.EtkiTarihi, yeni.GenelKasaEtkisi, yeni.Otomatik, yeni.Tur));
        // Aynı gün için bugünkü hesap: silinen 1.500 ve iptal edilen 20 geri döner, geriye dönük 70 düşer; bugüne dek taksit de düşer.
        Assert.Equal((sistem, sistem + 1_450m, sistem + 450m), (sonra.SistemBakiye, sonra.GuncelSistemBakiye, sonra.BugunkuSistemBakiye));
        Assert.Equal(await Cash(c), sonra.BugunkuSistemBakiye);
        Assert.Equal(sonra.BugunkuSistemBakiye - sonra.GuncelSistemBakiye, sonra.Hareketler.Where(h => h.EtkiTarihi > Today).Sum(h => h.GenelKasaEtkisi));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/kasa-kontrol/999/sonrasi", TestContext.Current.CancellationToken)).StatusCode);
    }

    // gap-coklu-giris-cift-sayim-mutabakat-17: kontrol gününe ya da öncesine düşen kayıt sonradan değişince kontrolün kayıtlı
    // değerleri aynen kalır, liste güncel sistem bakiyesini ve güncel farkı gösterip işaretler; sonraki günlere düşen kayıt işaretlemez.
    [Fact]
    public async Task Kontrol_gunune_dusen_kayit_sonradan_degisince_liste_guncel_bakiyeyi_ve_farki_gosterir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var mukerrer = await Gider(c, Today.AddDays(-2), "Mükerrer alış ödemesi", 1500m, "MEZAT");
        var sistem = await Cash(c);
        var ilk = await Kontrol(c, sistem - 1500m, "Sayımda 1.500 eksik");
        ((SabitSaat)f.Saat!).Ayarla(Today.AddDays(2));
        await Gider(c, Today.AddDays(1), "Sonraki gider", 30m, "MEZAT");
        var liste = Assert.Single((await c.GetFromJsonAsync<KasaKontrolDto[]>("/api/kasa-kontrol", cancellationToken: TestContext.Current.CancellationToken))!);
        Assert.Equal(((decimal?)sistem, (decimal?)(-1500m), false), (liste.GuncelSistemBakiye, liste.GuncelFark, liste.SonradanDegisti));

        Assert.Equal(HttpStatusCode.NoContent, (await c.SilIslemAsync(mukerrer, TestContext.Current.CancellationToken)).StatusCode);
        var ikinci = await Kontrol(c, await Cash(c), null);
        var kontroller = (await c.GetFromJsonAsync<KasaKontrolDto[]>("/api/kasa-kontrol", cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal(new[] { ikinci.Id, ilk.Id }, kontroller.Select(k => k.Id));
        var eski = kontroller[1];
        Assert.Equal((sistem, sistem - 1500m, -1500m), (eski.SistemBakiye, eski.GercekBakiye, eski.Fark));
        Assert.Equal(((decimal?)(sistem + 1500m), (decimal?)(-3000m), true), (eski.GuncelSistemBakiye, eski.GuncelFark, eski.SonradanDegisti));
        Assert.Equal((false, (decimal?)0m, (DateOnly?)Today.AddDays(2)), (kontroller[0].SonradanDegisti, kontroller[0].GuncelFark, kontroller[0].HesapTarihi));
        // Kanal kırılımı: değişiklik MEZAT kasasında.
        var mezat = eski.KanalBakiyeleri!.Single(k => k.KanalId == 1);
        Assert.Equal(mezat.Bakiye + 1500m, mezat.GuncelBakiye);
        Assert.All(eski.KanalBakiyeleri!.Where(k => k.KanalId != 1), k => Assert.Equal(k.Bakiye, k.GuncelBakiye));
    }

    [Fact]
    public async Task Fark_aciklamasi_surumle_yazilir_tutarlar_ve_filigran_degismez()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = Factory();
        using var c = await Editor(f);
        var kontrol = await Kontrol(c, 950m, "Sayım");
        var istek = new KasaKontrolAciklamaYaz(Guid.NewGuid(), kontrol.Surum, "  Bankaya yatırılan 50 TL sisteme girilmemiş  ");
        var r = await c.PutAsJsonAsync($"/api/kasa-kontrol/{kontrol.Id}/aciklama", istek, cancellationToken: ct);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync(ct));
        var aciklanan = (await r.Content.ReadFromJsonAsync<KasaKontrolDto>(cancellationToken: ct))!;
        Assert.Equal((2, "Bankaya yatırılan 50 TL sisteme girilmemiş"), (aciklanan.Surum, aciklanan.FarkAciklamasi));
        Assert.NotNull(aciklanan.FarkAciklamaZamani);
        Assert.Equal((kontrol.SistemBakiye, kontrol.GercekBakiye, kontrol.Fark, kontrol.Not, kontrol.HesapTarihi),
            (aciklanan.SistemBakiye, aciklanan.GercekBakiye, aciklanan.Fark, aciklanan.Not, aciklanan.HesapTarihi));
        // Aynı istek kimliği aynı sonucu verir; eski sürümle yeni istek 409; boş açıklama 400; kayıt yoksa 404.
        Assert.Equal(2, (await (await c.PutAsJsonAsync($"/api/kasa-kontrol/{kontrol.Id}/aciklama",
            istek, cancellationToken: ct)).Content.ReadFromJsonAsync<KasaKontrolDto>(cancellationToken: ct))!.Surum);
        var eski = await c.PutAsJsonAsync($"/api/kasa-kontrol/{kontrol.Id}/aciklama", istek with { IstekId = Guid.NewGuid(), Aciklama = "Başka" }, cancellationToken: ct);
        Assert.Equal(HttpStatusCode.Conflict, eski.StatusCode);
        Assert.Contains("Kontrol kaydı değişmiş. Yenileyin.", await eski.Content.ReadAsStringAsync(ct));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/kasa-kontrol/{kontrol.Id}/aciklama",
            new KasaKontrolAciklamaYaz(Guid.NewGuid(), 2, " "), cancellationToken: ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/kasa-kontrol/999/aciklama", new KasaKontrolAciklamaYaz(Guid.NewGuid(), 1, "Yok"), cancellationToken: ct)).StatusCode);
        var liste = Assert.Single((await c.GetFromJsonAsync<KasaKontrolDto[]>("/api/kasa-kontrol", cancellationToken: ct))!);
        Assert.Equal((2, "Bankaya yatırılan 50 TL sisteme girilmemiş", -50m, false), (liste.Surum, liste.FarkAciklamasi, liste.Fark, liste.SonradanDegisti));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var olay = db.DenetimOlaylari.Single(o => o.Varlik == "KasaKontrol" && o.Tur == "Degistir");
        Assert.Equal(istek.IstekId, olay.IstekId);
        Assert.Contains("Bankaya yatırılan", olay.YeniJson);
    }

    // Filigran migration'ından önce kaydedilmiş kontrol: filigran alanları boş; liste kayıt anının İstanbul günüyle karşılaştırır,
    // "sonrası" kayıt anından sonraki denetim olaylarını verir (mali istek sırası bilinmez).
    [Fact]
    public async Task Filigransiz_eski_kayit_kayit_gunuyle_karsilastirilir_ve_sonrasi_zamana_gore_listelenir()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        var gider = await Gider(c, Today.AddDays(-3), "Eski gider", 250m, "MEZAT");
        var sistem = await Cash(c);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            db.KasaKontrolleri.Add(new KasaKontrolEntity { Kaydedildi = f.Saat!.GetUtcNow().ToUnixTimeMilliseconds(), SistemBakiye = sistem, GercekBakiye = sistem, Fark = 0m });
            db.SaveChanges();
        }
        ((SabitSaat)f.Saat!).Ayarla(Today.AddDays(1));
        Assert.Equal(HttpStatusCode.NoContent, (await c.SilIslemAsync(gider, TestContext.Current.CancellationToken)).StatusCode);
        var eski = Assert.Single((await c.GetFromJsonAsync<KasaKontrolDto[]>("/api/kasa-kontrol", cancellationToken: TestContext.Current.CancellationToken))!);
        Assert.Equal((1, (DateOnly?)null, true), (eski.Surum, eski.HesapTarihi, eski.KanalBakiyeleri is null));
        Assert.Equal(((decimal?)(sistem + 250m), true), (eski.GuncelSistemBakiye, eski.SonradanDegisti));
        var sonra = (await c.GetFromJsonAsync<KasaKontrolSonrasiDto>($"/api/kasa-kontrol/{eski.Id}/sonrasi", cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal((false, Today, sistem + 250m), (sonra.FiligranVar, sonra.EsasTarih, sonra.GuncelSistemBakiye));
        Assert.Empty(sonra.Istekler);
        // Fabrikanın ilk ayar kaydı test saatinden değil sistem saatinden damgalıdır: yalnız giderin olayları sayılır.
        var olay = Assert.Single(sonra.Degisiklikler, o => o.Varlik == "Islem");
        Assert.Equal((gider.ToString(), "Sil"), (olay.VarlikId, olay.Tur));
    }

    [Fact]
    public async Task Esik_surumu_ve_para_dogrulanir_izleyici_yazamaz()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = Factory();
        using var c = await Editor(f);
        var first = (await c.GetFromJsonAsync<KasaEsikDto[]>("/api/kasa-esikleri", cancellationToken: ct))!.First();
        Assert.False(first.Etkin);
        Assert.Equal(0, first.Surum);
        var result = await c.PutAsJsonAsync($"/api/kasa-esikleri/{first.KanalId}", new KasaEsikYaz(0, 0m, true), cancellationToken: ct);
        result.EnsureSuccessStatusCode();
        var saved = (await result.Content.ReadFromJsonAsync<KasaEsikDto>(cancellationToken: ct))!;
        Assert.Equal(1, saved.Surum);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/kasa-esikleri/{first.KanalId}", new KasaEsikYaz(0, 5m, true), cancellationToken: ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/kasa-esikleri/{first.KanalId}", new KasaEsikYaz(1, -1m, true), cancellationToken: ct)).StatusCode);
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifre-123" }, cancellationToken: ct)).EnsureSuccessStatusCode();
        using var viewer = f.CreateClient();
        (await viewer.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izleyici-sifre-123" }, cancellationToken: ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/kasa-esikleri", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/kasa-kontrol", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"/api/kasa-esikleri/{first.KanalId}", new KasaEsikYaz(1, 10m, true), cancellationToken: ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/kasa-kontrol/onizleme", new KasaKontrolOnizle(0m), cancellationToken: ct)).StatusCode);
    }

    [Fact]
    public async Task Dusuk_bakiye_olayi_gun_degisiminde_tekrarlamaz_toparlanip_yeniden_dusunce_yenidir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = Factory();
        using var c = await Editor(f);
        // Bakiye bugünkü giderle eşiğin altına iner (150 − 100 = 50): istek dışındaki eşik okuması paneli
        // bağlamın saatine, yani sunucunun gününe göre kurar; sistem takvimine göre kursa gideri görmezdi.
        (await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT", AcilisDevri: 150m), cancellationToken: ct)).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Today, "Bugünkü gider", 100m, "MEZAT", GiderTipi.Cari), cancellationToken: ct)).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/kasa-esikleri/1", new KasaEsikYaz(0, 100m, true), cancellationToken: ct)).EnsureSuccessStatusCode();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var first = Assert.Single(KasaEsikServisi.Oku(db, Today, true));
        Assert.Contains("kasa 50,00 TL", first.Mesaj);
        Assert.Equal(first.Anahtar, Assert.Single(KasaEsikServisi.Oku(db, Today, true)).Anahtar);
        db.ChangeTracker.Clear();
        Assert.Empty(KasaEsikServisi.Oku(db, Today.AddDays(1), true));
        var channel = db.Kanallar.Single(k => k.Id == 1);
        channel.AcilisDevri = 250m;
        db.SaveChanges();
        Assert.Empty(KasaEsikServisi.Oku(db, Today.AddDays(1), true));
        channel.AcilisDevri = 150m;
        db.SaveChanges();
        var next = Assert.Single(KasaEsikServisi.Oku(db, Today.AddDays(1), true));
        Assert.NotEqual(first.Anahtar, next.Anahtar);
    }

    [Fact]
    public async Task Bildirimler_kapaliyken_yeni_esik_olayi_tuketilmez()
    {
        await using var f = Factory();
        using var c = await Editor(f);
        (await c.PutAsJsonAsync("/api/kasa-esikleri/1", new KasaEsikYaz(0, 100m, true), cancellationToken: TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.Empty(KasaEsikServisi.Oku(db, Today, false));
        Assert.False(db.KasaEsikleri.Single().AlarmAcik);
        Assert.Equal(0, db.KasaEsikleri.Single().OlaySayisi);
        Assert.Single(KasaEsikServisi.Oku(db, Today.AddDays(1), true));
    }

    private static void Shares(IReadOnlyList<TakipKanalPayi> actual, params (int? Id, decimal Amount)[] expected) =>
        Assert.Equal(expected.OrderBy(p => p.Id), actual.Select(p => (p.KanalId, p.Tutar)).OrderBy(p => p.KanalId));
    private async Task<HttpClient> Editor(KasaWebFactory f)
    { var c = await f.EditorClientAsync(); (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = Start, kasaAcilisDevri = 1000m })).EnsureSuccessStatusCode(); return c; }
    private static async Task<T> Post<T>(HttpClient c, string path, object body)
    { var r = await c.PostAsJsonAsync(path, body); Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}"); return (await r.Content.ReadFromJsonAsync<T>())!; }
    private Task<KartTakipDto> Card(HttpClient c) => Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "Kart", 10000m, 5, 25, Start, 0m, []));
    private Task<KartTakipDto> Charge(HttpClient c, KartTakipDto card, decimal amount, IReadOnlyList<KanalPayYaz> shares, int installments = 1) =>
        Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/harcamalar", new KartHarcamaYaz(Guid.NewGuid(), card.Surum, Start, "Mal", amount, installments, null, shares));
    private Task<KartTakipDto> Pay(HttpClient c, KartTakipDto card, decimal amount) => Post<KartTakipDto>(c, $"/api/takip/kartlar/{card.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), card.Surum, Today, amount));
    private KartMasrafYaz Fee(KartTakipDto card, decimal amount) => new(Guid.NewGuid(), card.Surum, card.Ekstreler.Where(e => e.Borc > 0).OrderBy(e => e.KesimTarihi).First().Id, Today, amount, "Bankanın bildirdiği faiz");
    private static async Task<decimal> Cash(HttpClient c) => (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel"))!.GuncelKasa;
    /// <summary>Genel gider (Cari) ekler, kimliğini döndürür (yanıtın tipi metin enum taşır).</summary>
    private static async Task<int> Gider(HttpClient c, DateOnly tarih, string cari, decimal tutar, string kanal)
    {
        var r = await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(tarih, cari, tutar, kanal, GiderTipi.Cari));
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (int)JsonNode.Parse(await r.Content.ReadAsStringAsync())!["id"]!;
    }
    private static async Task<KasaKontrolDto> Kontrol(HttpClient c, decimal gercek, string? not)
    {
        var preview = await Post<KasaKontrolOnizlemeDto>(c, "/api/kasa-kontrol/onizleme", new KasaKontrolOnizle(gercek, not));
        return await Post<KasaKontrolDto>(c, "/api/kasa-kontrol", new KasaKontrolYaz(Guid.NewGuid(), gercek, preview.KontrolOzeti, not));
    }
}
