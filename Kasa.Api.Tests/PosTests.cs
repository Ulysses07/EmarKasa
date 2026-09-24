using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.Tests.PaketFYardimci;

namespace Kasa.Api.Tests;

/// <summary>POS tanımları, satışları, özet (bugün = 24.09.2026) ve kasaya/kârlılığa etkisizlik.</summary>
public class PosTests : IClassFixture<PaketFFactory>
{
    private readonly PaketFFactory _f;
    public PosTests(PaketFFactory f) => _f = f;

    private record Tanim(int Id, string Ad, string Saglayici, int? KanalId, string? KanalAd, decimal KomisyonOrani, int BlokajGunu, bool Aktif);
    private record Satis(int Id, DateOnly Tarih, int PosId, string PosAd, string Kanal, decimal BrutTutar, decimal KomisyonOrani,
        decimal Komisyon, decimal Net, int BlokajGunu, DateOnly Valor, bool Bloke, string? Not);
    private record KanalOzet(string Kanal, decimal Brut, decimal Komisyon, decimal Net, int Adet);
    private record Ozet(int Yil, int Ay, DateOnly Bugun, decimal BlokeNet, int BlokeAdet, List<ValorGunu> Valorler,
        List<KanalOzet> Kanallar, decimal ToplamBrut, decimal ToplamKomisyon, decimal ToplamNet);
    private record ValorGunu(DateOnly Valor, decimal Net, int Adet);

    private int KanalId(string ad) => _f.Db(db => db.Kanallar.Single(k => k.Ad == ad).Id);

    private static async Task<Tanim> TanimEkleAsync(HttpClient c, string ad, int? kanalId, decimal oran, int blokaj, string saglayici = "BankaPosu")
    {
        var r = await c.PostAsJsonAsync("/api/pos/tanimlar", new { ad, saglayici, kanalId, komisyonOrani = oran, blokajGunu = blokaj, aktif = true });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<Tanim>())!;
    }

    private static async Task<Satis> SatisEkleAsync(HttpClient c, int posId, string tarih, decimal brut, decimal? oran = null, int? blokaj = null)
    {
        var r = await c.PostAsJsonAsync("/api/pos/satislar", new { tarih, posId, brutTutar = brut, komisyonOrani = oran, blokajGunu = blokaj, not = (string?)null });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<Satis>())!;
    }

    [Fact]
    public async Task Tanim_dogrulamasi_ve_crud()
    {
        _f.Temizle();
        var c = await _f.EditorClientAsync();
        var t = await TanimEkleAsync(c, "  Ziraat POS ", KanalId("MEZAT"), 1.79m, 1);
        Assert.Equal(("Ziraat POS", "MEZAT", "BankaPosu"), (t.Ad, t.KanalAd, t.Saglayici));

        async Task<string> Hata(object govde) => await HataAsync(await c.PostAsJsonAsync("/api/pos/tanimlar", govde));
        Assert.Equal("POS adı boş olamaz.", await Hata(new { ad = " ", saglayici = "Iyzico", komisyonOrani = 1m, blokajGunu = 0 }));
        Assert.Equal("Komisyon oranı 0 ile 100 arasında olmalı.", await Hata(new { ad = "Y", saglayici = "Iyzico", komisyonOrani = 100.01m, blokajGunu = 0 }));
        Assert.Equal("Komisyon oranı 0 ile 100 arasında olmalı.", await Hata(new { ad = "Y", saglayici = "Iyzico", komisyonOrani = -1m, blokajGunu = 0 }));
        Assert.Equal("Komisyon oranı en fazla 4 ondalık basamak içerebilir.", await Hata(new { ad = "Y", saglayici = "Iyzico", komisyonOrani = 1.23456m, blokajGunu = 0 }));
        Assert.Equal("Blokaj günü 0 ile 365 arasında olmalı.", await Hata(new { ad = "Y", saglayici = "Iyzico", komisyonOrani = 1m, blokajGunu = 366 }));
        Assert.Equal("Kanal bulunamadı.", await Hata(new { ad = "Y", saglayici = "Iyzico", kanalId = 99999, komisyonOrani = 1m, blokajGunu = 0 }));
        Assert.Equal("'ZİRAAT POS' adında bir POS zaten var.", await Hata(new { ad = "ZİRAAT POS", saglayici = "Diger", komisyonOrani = 1m, blokajGunu = 0 }));

        var p = await c.PutAsJsonAsync($"/api/pos/tanimlar/{t.Id}", new { ad = "Ziraat POS", saglayici = "BankaPosu", kanalId = (int?)null, komisyonOrani = 2m, blokajGunu = 3, aktif = false });
        p.EnsureSuccessStatusCode();
        var g = (await p.Content.ReadFromJsonAsync<Tanim>())!;
        Assert.Equal((null, null, 2m, 3, false), (g.KanalId, g.KanalAd, g.KomisyonOrani, g.BlokajGunu, g.Aktif));
        Assert.Equal(HttpStatusCode.NotFound, (await c.PutAsJsonAsync("/api/pos/tanimlar/99999", new { ad = "Q", saglayici = "Diger", komisyonOrani = 1m, blokajGunu = 0 })).StatusCode);

        var liste = await c.GetFromJsonAsync<List<Tanim>>("/api/pos/tanimlar");
        Assert.Single(liste!);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/pos/tanimlar/{t.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/pos/tanimlar/{t.Id}")).StatusCode);
    }

    [Fact]
    public async Task Satis_varsayilanlari_postan_alir_oran_sonradan_degisse_de_eski_satis_degismez()
    {
        _f.Temizle();
        var c = await _f.EditorClientAsync();
        var t = await TanimEkleAsync(c, "iyzico", KanalId("PERAKENDE"), 2.49m, 7, "Iyzico");
        var s = await SatisEkleAsync(c, t.Id, "2026-09-20", 1000m);
        Assert.Equal((2.49m, 7, 24.90m, 975.10m, new DateOnly(2026, 9, 27), true, "PERAKENDE", "iyzico"),
            (s.KomisyonOrani, s.BlokajGunu, s.Komisyon, s.Net, s.Valor, s.Bloke, s.Kanal, s.PosAd));

        (await c.PutAsJsonAsync($"/api/pos/tanimlar/{t.Id}", new { ad = "iyzico", saglayici = "Iyzico", kanalId = KanalId("PERAKENDE"), komisyonOrani = 3m, blokajGunu = 1, aktif = true })).EnsureSuccessStatusCode();
        var liste = await c.GetFromJsonAsync<List<Satis>>("/api/pos/satislar");
        Assert.Equal(2.49m, liste!.Single(x => x.Id == s.Id).KomisyonOrani);

        // Elle oran/blokaj; düzenlemede boş oran kayıtlı değeri korur.
        var elle = await SatisEkleAsync(c, t.Id, "2026-09-24", 200m, oran: 0m, blokaj: 0);
        Assert.Equal((0m, 200m, false), (elle.Komisyon, elle.Net, elle.Bloke));
        var d = await c.PutAsJsonAsync($"/api/pos/satislar/{s.Id}", new { tarih = "2026-09-21", posId = t.Id, brutTutar = 500m, komisyonOrani = (decimal?)null, blokajGunu = (int?)null, not = " iade sonrası " });
        d.EnsureSuccessStatusCode();
        var dd = (await d.Content.ReadFromJsonAsync<Satis>())!;
        Assert.Equal((2.49m, 7, 12.45m, "iade sonrası"), (dd.KomisyonOrani, dd.BlokajGunu, dd.Komisyon, dd.Not));

        async Task<string> Hata(object govde) => await HataAsync(await c.PostAsJsonAsync("/api/pos/satislar", govde));
        Assert.Equal("Brüt tutar sıfırdan büyük olmalı.", await Hata(new { tarih = "2026-09-20", posId = t.Id, brutTutar = 0m }));
        Assert.Equal("Brüt tutar en fazla 2 ondalık basamak içerebilir.", await Hata(new { tarih = "2026-09-20", posId = t.Id, brutTutar = 1.001m }));
        Assert.Equal("POS bulunamadı.", await Hata(new { tarih = "2026-09-20", posId = 99999, brutTutar = 1m }));
        Assert.Equal("Tarih 2000 ile 2100 arasında olmalı.", await Hata(new { tarih = "1999-12-31", posId = t.Id, brutTutar = 1m }));
        Assert.Equal("Komisyon oranı 0 ile 100 arasında olmalı.", await Hata(new { tarih = "2026-09-20", posId = t.Id, brutTutar = 1m, komisyonOrani = 101m }));

        // Satışı olan POS silinemez; satış silinince silinebilir.
        var sil = await c.DeleteAsync($"/api/pos/tanimlar/{t.Id}");
        Assert.Equal(HttpStatusCode.Conflict, sil.StatusCode);
        Assert.Equal("Bu POS'un satış kayıtları var. Silmek yerine pasif yapın.", await HataAsync(sil));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/pos/satislar/{s.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/pos/satislar/{elle.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/pos/tanimlar/{t.Id}")).StatusCode);
    }

    [Fact]
    public async Task Ozet_bloke_valor_ve_aylik_kanal_komisyonu()
    {
        _f.Temizle();
        var c = await _f.EditorClientAsync();
        var mezat = await TanimEkleAsync(c, "Banka MEZAT", KanalId("MEZAT"), 2m, 7);
        var toptan = await TanimEkleAsync(c, "PayTR", KanalId("TOPTAN"), 1m, 5, "PayTr");
        var kanalsiz = await TanimEkleAsync(c, "Eski POS", null, 0m, 0, "Diger");
        await SatisEkleAsync(c, mezat.Id, "2026-09-20", 1000m);    // valör 27 → bloke (net 980)
        await SatisEkleAsync(c, toptan.Id, "2026-09-22", 500m);    // valör 27 → bloke (net 495)
        await SatisEkleAsync(c, mezat.Id, "2026-09-10", 300m);     // valör 17 → geçti
        await SatisEkleAsync(c, kanalsiz.Id, "2026-09-24", 50m);   // blokajsız
        await SatisEkleAsync(c, mezat.Id, "2026-08-31", 100m);     // önceki ay (valör 7 Eylül)
        await SatisEkleAsync(c, toptan.Id, "2026-09-25", 400m, blokaj: 2);   // ileri tarihli: bloke değil

        var o = (await c.GetFromJsonAsync<Ozet>("/api/pos/ozet?yil=2026&ay=9"))!;
        Assert.Equal(new DateOnly(2026, 9, 24), o.Bugun);
        Assert.Equal((1475m, 2), (o.BlokeNet, o.BlokeAdet));
        Assert.Equal([(new DateOnly(2026, 9, 27), 1475m, 2)], o.Valorler.Select(v => (v.Valor, v.Net, v.Adet)));
        Assert.Equal(["MEZAT", "TOPTAN", "Kanalsız"], o.Kanallar.Select(k => k.Kanal));
        Assert.Equal(new KanalOzet("MEZAT", 1300m, 26m, 1274m, 2), o.Kanallar[0]);
        Assert.Equal(new KanalOzet("TOPTAN", 900m, 9m, 891m, 2), o.Kanallar[1]);
        Assert.Equal(new KanalOzet("Kanalsız", 50m, 0m, 50m, 1), o.Kanallar[2]);
        Assert.Equal((2250m, 35m, 2215m), (o.ToplamBrut, o.ToplamKomisyon, o.ToplamNet));

        var agustos = (await c.GetFromJsonAsync<Ozet>("/api/pos/ozet?yil=2026&ay=8"))!;
        Assert.Equal(new KanalOzet("MEZAT", 100m, 2m, 98m, 1), Assert.Single(agustos.Kanallar));
        Assert.Equal(1475m, agustos.BlokeNet);   // bloke her zaman bugüne göre (ay seçiminden bağımsız)
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/pos/ozet?yil=2026&ay=0")).StatusCode);

        var filtre = await c.GetFromJsonAsync<List<Satis>>($"/api/pos/satislar?baslangic=2026-09-01&bitis=2026-09-30&posId={mezat.Id}");
        Assert.Equal([new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 10)], filtre!.Select(s => s.Tarih));
    }

    [Fact]
    public async Task Kanal_silinince_pos_kalir_kanalsiz_olur()
    {
        _f.Temizle();
        var c = await _f.EditorClientAsync();
        var k = await c.PostAsJsonAsync("/api/kanallar", new { ad = "GEÇİCİ", aktif = true, sira = 9, acilisDevri = 0m });
        k.EnsureSuccessStatusCode();
        var kanalId = KanalId("GEÇİCİ");
        var t = await TanimEkleAsync(c, "Geçici POS", kanalId, 1m, 1);
        await SatisEkleAsync(c, t.Id, "2026-09-23", 100m);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/kanallar/{kanalId}")).StatusCode);
        var liste = await c.GetFromJsonAsync<List<Tanim>>("/api/pos/tanimlar");
        Assert.Null(liste!.Single(x => x.Id == t.Id).KanalId);
        var s = await c.GetFromJsonAsync<List<Satis>>("/api/pos/satislar");
        Assert.Equal("Kanalsız", s!.Single().Kanal);
    }

    [Fact]
    public async Task Izleyici_okur_yazamaz_kimliksiz_401()
    {
        _f.Temizle();
        var c = await _f.EditorClientAsync();
        var t = await TanimEkleAsync(c, "Okuma POS", null, 1m, 1);
        var s = await SatisEkleAsync(c, t.Id, "2026-09-23", 10m);
        var iz = await _f.IzleyiciAsync();
        Assert.Equal(HttpStatusCode.OK, (await iz.GetAsync("/api/pos/tanimlar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await iz.GetAsync("/api/pos/satislar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await iz.GetAsync("/api/pos/ozet?yil=2026&ay=9")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await iz.PostAsJsonAsync("/api/pos/tanimlar", new { ad = "Z", saglayici = "Diger", komisyonOrani = 1m, blokajGunu = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await iz.PutAsJsonAsync($"/api/pos/tanimlar/{t.Id}", new { ad = "Z", saglayici = "Diger", komisyonOrani = 1m, blokajGunu = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await iz.DeleteAsync($"/api/pos/tanimlar/{t.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await iz.PostAsJsonAsync("/api/pos/satislar", new { tarih = "2026-09-23", posId = t.Id, brutTutar = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await iz.PutAsJsonAsync($"/api/pos/satislar/{s.Id}", new { tarih = "2026-09-23", posId = t.Id, brutTutar = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await iz.DeleteAsync($"/api/pos/satislar/{s.Id}")).StatusCode);
        var anonim = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.GetAsync("/api/pos/tanimlar")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonim.GetAsync("/api/pos/ozet?yil=2026&ay=9")).StatusCode);
    }

    [Fact]
    public async Task Pos_ve_belge_verisi_kasa_panel_haftalik_ve_aylik_rakamlarini_degistirmez()
    {
        _f.Temizle();
        var c = await _f.EditorClientAsync();
        await FaturaTakibiEkle(c);
        var once = await RaporlarAsync(c);

        // POS tanımları + satışlar, belge alanları, fatura bekleniyor ve ekler eklenir.
        var t = await TanimEkleAsync(c, "Etkisiz POS", KanalId("MEZAT"), 2.5m, 3);
        await SatisEkleAsync(c, t.Id, "2026-09-02", 12_345.67m);
        await SatisEkleAsync(c, t.Id, "2026-09-23", 999m);
        var islemler = _f.Db(db => db.Islemler.AsNoTracking().Select(i => i.Id).ToList());
        foreach (var id in islemler)
        {
            // Belgesiz ile "fatura bekleniyor" birlikte olamaz: işlemlerin yarısı belgesiz, yarısı fatura bekliyor.
            (await c.PutAsJsonAsync($"/api/islemler/{id}/belge", id % 2 == 0
                ? new { belgeTuru = "Belgesiz", belgeNo = "X-" + id, faturaBekleniyor = false }
                : new { belgeTuru = (string?)null, belgeNo = "X-" + id, faturaBekleniyor = true })).EnsureSuccessStatusCode();
            (await c.PostAsync($"/api/islemler/{id}/ekler", IslemEkiTests.Form(IslemEkiTests.Pdf(), "e.pdf"))).EnsureSuccessStatusCode();
        }
        var sonra = await RaporlarAsync(c);
        Assert.Equal(once, sonra);
    }

    private record KanalBloke(string Kanal, decimal Net, int Adet);
    private record OzetB(int Yil, int Ay, decimal BlokeNet, int BlokeAdet, List<KanalOzet> Kanallar, List<KanalBloke> BlokeKanallar);
    private record GecmisSatir(int Id, string Tur, int? KayitId, string Eylem, string Ozet);

    /// <summary>Bulgu: bloke para yalnız tek toplamdı; kanal kanal görünmeli, ay sınırını aşan satış dahil.</summary>
    [Fact]
    public async Task Ozet_bloke_parayi_kanal_kanal_verir_gecen_ayin_hala_bloke_satisi_dahil()
    {
        _f.Temizle();
        var c = await _f.EditorClientAsync();
        var toptan = await TanimEkleAsync(c, "Toptan POS", KanalId("TOPTAN"), 2m, 30);
        var mezat = await TanimEkleAsync(c, "Mezat POS", KanalId("MEZAT"), 1m, 5);
        var kanalsiz = await TanimEkleAsync(c, "Kanalsız POS", null, 0m, 3);
        await SatisEkleAsync(c, toptan.Id, "2026-08-28", 1000m);    // geçen ay; valör 27 Eylül → bugün bloke, net 980
        await SatisEkleAsync(c, mezat.Id, "2026-09-22", 500m);      // valör 27 → bloke, net 495
        await SatisEkleAsync(c, mezat.Id, "2026-09-10", 300m);      // valör 15 → geçti
        await SatisEkleAsync(c, kanalsiz.Id, "2026-09-23", 40m);    // valör 26 → bloke, net 40

        var o = (await c.GetFromJsonAsync<OzetB>("/api/pos/ozet?yil=2026&ay=9"))!;
        Assert.Equal([new KanalBloke("MEZAT", 495m, 1), new KanalBloke("TOPTAN", 980m, 1), new KanalBloke("Kanalsız", 40m, 1)], o.BlokeKanallar);
        Assert.Equal((o.BlokeNet, o.BlokeAdet), (o.BlokeKanallar.Sum(k => k.Net), o.BlokeKanallar.Sum(k => k.Adet)));
        Assert.DoesNotContain(o.Kanallar, k => k.Kanal == "TOPTAN");   // ayın tablosunda yok, blokede var
        // Bloke dökümü bugüne göredir: hangi ay seçilirse seçilsin aynı.
        Assert.Equal(o.BlokeKanallar, (await c.GetFromJsonAsync<OzetB>("/api/pos/ozet?yil=2026&ay=7"))!.BlokeKanallar);
    }

    /// <summary>
    /// Bulgu: satışın kanalı POS'un BUGÜNKÜ kanalından okunuyordu; POS'un kanalı değişince geçmiş satışlar,
    /// blokeler ve geçmiş ayların kanal komisyonu yeni kanala kayıyordu.
    /// </summary>
    [Fact]
    public async Task Pos_kanali_degisince_gecmis_satislar_ve_gecmis_ay_ozeti_eski_kanalda_kalir()
    {
        _f.Temizle();
        var c = await _f.EditorClientAsync();
        int mezatId = KanalId("MEZAT"), toptanId = KanalId("TOPTAN");
        var t = await TanimEkleAsync(c, "Taşınan POS", mezatId, 2m, 7);
        var agustos = await SatisEkleAsync(c, t.Id, "2026-08-20", 1000m);
        var bloke = await SatisEkleAsync(c, t.Id, "2026-09-20", 500m);    // valör 27 → bloke
        var onceAgustos = await c.GetStringAsync("/api/pos/ozet?yil=2026&ay=8");
        var onceEylul = await c.GetStringAsync("/api/pos/ozet?yil=2026&ay=9");

        (await c.PutAsJsonAsync($"/api/pos/tanimlar/{t.Id}", new { ad = "Taşınan POS", saglayici = "BankaPosu", kanalId = toptanId, komisyonOrani = 2m, blokajGunu = 7, aktif = true }))
            .EnsureSuccessStatusCode();

        Assert.Equal(onceAgustos, await c.GetStringAsync("/api/pos/ozet?yil=2026&ay=8"));
        Assert.Equal(onceEylul, await c.GetStringAsync("/api/pos/ozet?yil=2026&ay=9"));
        var eylul = (await c.GetFromJsonAsync<OzetB>("/api/pos/ozet?yil=2026&ay=9"))!;
        Assert.Equal([new KanalBloke("MEZAT", 490m, 1)], eylul.BlokeKanallar);
        Assert.Equal([new KanalOzet("MEZAT", 1000m, 20m, 980m, 1)], (await c.GetFromJsonAsync<OzetB>("/api/pos/ozet?yil=2026&ay=8"))!.Kanallar);
        var satislar = (await c.GetFromJsonAsync<List<Satis>>("/api/pos/satislar"))!;
        Assert.All(satislar, x => Assert.Equal("MEZAT", x.Kanal));

        // Yeni satış POS'un yeni kanalına yazılır; eski satışın aynı POS'la düzenlenmesi kanalını korur.
        var yeni = await SatisEkleAsync(c, t.Id, "2026-09-23", 100m);
        Assert.Equal("TOPTAN", yeni.Kanal);
        var d = await c.PutAsJsonAsync($"/api/pos/satislar/{agustos.Id}", new { tarih = "2026-08-21", posId = t.Id, brutTutar = 1000m });
        Assert.Equal("MEZAT", (await d.Content.ReadFromJsonAsync<Satis>())!.Kanal);

        // Yanlış girilmiş kanalı düzeltmek: "eski satışlara da uygula" tüm satışları yeni kanala taşır, geçmişe yazılır.
        (await c.PutAsJsonAsync($"/api/pos/tanimlar/{t.Id}", new { ad = "Taşınan POS", saglayici = "BankaPosu", kanalId = mezatId, komisyonOrani = 2m, blokajGunu = 7, aktif = true, eskiSatislaraUygula = true }))
            .EnsureSuccessStatusCode();
        Assert.All((await c.GetFromJsonAsync<List<Satis>>("/api/pos/satislar"))!, x => Assert.Equal("MEZAT", x.Kanal));
        var gecmis = await c.GetFromJsonAsync<List<GecmisSatir>>("/api/gecmis?limit=10&tur=" + Uri.EscapeDataString("POS satışı"));
        Assert.Contains(gecmis!, g => g.Ozet == "Taşınan POS: 1 POS satışının kanalı değişti: TOPTAN → MEZAT");

        // Kanal değişmeden işaret gönderilirse bir şey yapılmaz.
        (await c.PutAsJsonAsync($"/api/pos/tanimlar/{t.Id}", new { ad = "Taşınan POS", saglayici = "BankaPosu", kanalId = mezatId, komisyonOrani = 2m, blokajGunu = 7, aktif = true, eskiSatislaraUygula = true }))
            .EnsureSuccessStatusCode();
        Assert.Single((await c.GetFromJsonAsync<List<GecmisSatir>>("/api/gecmis?limit=10&tur=" + Uri.EscapeDataString("POS satışı")))!,
            g => g.Ozet.Contains("kanalı değişti"));

        // Satış başka POS'a alınırsa o POS'un kanalını alır.
        var diger = await TanimEkleAsync(c, "Diğer POS", toptanId, 1m, 0);
        var tasinan = await c.PutAsJsonAsync($"/api/pos/satislar/{bloke.Id}", new { tarih = "2026-09-20", posId = diger.Id, brutTutar = 500m });
        Assert.Equal("TOPTAN", (await tasinan.Content.ReadFromJsonAsync<Satis>())!.Kanal);
    }

    private static async Task FaturaTakibiEkle(HttpClient c)
    {
        await PaketFFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 8, 1), 5_000m);
        await IslemEkleAsync(c, "Market", 1_500m, "2026-09-02");
        await IslemEkleAsync(c, "A", 250.55m, "2026-09-15", kanal: "TOPTAN");
        await IslemEkleAsync(c, "B", 99.99m, "2026-08-20", kanal: "PERAKENDE");
        (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-09-01", kanal = "MEZAT", tutarTl = 10_000m })).EnsureSuccessStatusCode();
    }

    private static async Task<string> RaporlarAsync(HttpClient c)
    {
        var parcalar = new List<string>();
        foreach (var yol in new[] { "/api/rapor/panel", "/api/rapor/haftalik", "/api/rapor/aylik?yil=2026&ay=9", "/api/rapor/aylik?yil=2026&ay=8",
                                    "/api/disaaktar/aylik.csv?yil=2026&ay=9", "/api/kasasayimlari/hesapla?tarih=2026-09-23" })
        {
            var r = await c.GetAsync(yol);
            r.EnsureSuccessStatusCode();
            parcalar.Add(yol + " => " + await r.Content.ReadAsStringAsync());
        }
        return string.Join("\n", parcalar);
    }
}
