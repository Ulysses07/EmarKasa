using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kasa.Api.Data;
using Kasa.Api.Denetim;
using Kasa.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Kasa.Api.Tests.MonthlyExpenseTests;

namespace Kasa.Api.Tests;

/// <summary>
/// Merkezi denetim izi (DenetimOlaylari): çekirdek kasa varlıklarının ekleme/değiştirme/silmesi önceki ve yeni değeri,
/// zamanı, aktörü ve gerekçesiyle, ana işlemle aynı transaction'da yazılır; ham SQL gelir upsert'i dahil. Olay
/// değiştirilemez. Ay kilidi açılışının penceresine düşen değişiklik açılışa bağlanır. Takip ve alış gerekçeleri atılmaz,
/// GET /api/denetim ile (yalnız editör) okunur. Saat sabittir (<see cref="KasaWebFactory.Sabit"/>).
/// </summary>
public class DenetimIziTests
{
    private static long SabitAn => new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(Today.ToDateTime(new TimeOnly(12, 0)), KasaSaati.Istanbul), TimeSpan.Zero).ToUnixTimeMilliseconds();
    private static DateOnly GecenAy => Month.AddMonths(-1);

    [Fact]
    public async Task Gider_ekleme_degistirme_ve_silme_onceki_yeni_deger_zaman_ve_aktorle_yazilir()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        var gider = await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Today, "Toptancı", 12500m, "MEZAT", GiderTipi.Cari, "Fatura 12"));
        (await c.PutAsJsonAsync($"/api/islemler/{gider.Id}", new IslemYazDto(Today, "Toptancı", 13000m, "MEZAT", GiderTipi.Cari, "Fatura 12"))).EnsureSuccessStatusCode();
        (await c.DeleteAsync($"/api/islemler/{gider.Id}")).EnsureSuccessStatusCode();

        var olaylar = Olaylar(f, "Islem", gider.Id);
        Assert.Equal(["Ekle", "Degistir", "Sil"], olaylar.Select(o => o.Tur));
        Assert.All(olaylar, o =>
        {
            Assert.Equal("editor", o.AktorRol); Assert.Null(o.AktorId); Assert.Equal(SabitAn, o.ZamanUtc);
            Assert.False(string.IsNullOrEmpty(o.TraceId)); Assert.Null(o.KilitAcmaOlayiId);
        });
        var eklenen = J(olaylar[0].YeniJson);
        Assert.Null(olaylar[0].OncekiJson);
        Assert.Equal(12500m, eklenen["TutarTl"]!.GetValue<decimal>()); Assert.Equal("Toptancı", (string?)eklenen["Cari"]); Assert.Equal("Cari", (string?)eklenen["Tip"]);
        // Değişiklik yalnız değişen alanı iki tarafta taşır.
        Assert.Equal("""{"TutarTl":12500}""", olaylar[1].OncekiJson);
        Assert.Equal("""{"TutarTl":13000}""", olaylar[1].YeniJson);
        var silinen = J(olaylar[2].OncekiJson);
        Assert.Null(olaylar[2].YeniJson);
        Assert.Equal(13000m, silinen["TutarTl"]!.GetValue<decimal>()); Assert.Equal("Fatura 12", (string?)silinen["Not"]);
        Assert.Equal(Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), (string?)silinen["Tarih"]);
    }

    [Fact]
    public async Task Ham_sql_gelir_upserti_onceki_ve_yeni_tutari_olay_olarak_yazar_degismeyen_tekrar_olay_uretmez()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Month, "PERAKENDE", 48000m))).EnsureSuccessStatusCode();
        var gelen = (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Month, "PERAKENDE", 44000m)));
        gelen.EnsureSuccessStatusCode();
        var id = (await gelen.Content.ReadFromJsonAsync<GelenEntity>())!.Id;
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Month, "PERAKENDE", 44000m))).EnsureSuccessStatusCode();

        var olaylar = Olaylar(f, "Gelen", id);
        Assert.Equal(["Ekle", "Degistir"], olaylar.Select(o => o.Tur));
        Assert.Equal(48000m, J(olaylar[0].YeniJson)["TutarTl"]!.GetValue<decimal>());
        Assert.Equal("""{"TutarTl":48000}""", olaylar[1].OncekiJson);
        Assert.Equal("""{"TutarTl":44000}""", olaylar[1].YeniJson);
        Assert.All(olaylar, o => Assert.Equal("editor", o.AktorRol));
    }

    [Fact]
    public async Task Genel_kasa_acilis_devri_ve_kanal_degisikligi_yazilir_kanal_adi_esitlemesi_ayrica_olay_uretmez()
    {
        await using var f = Fabrika(); using var c = await Editor(f); // Editor(): açılış devri 0 → 1000
        var ayar = Assert.Single(Olaylar(f, "Ayar"), o => o.Tur == "Degistir");
        Assert.Equal(0m, J(ayar.OncekiJson)["KasaAcilisDevri"]!.GetValue<decimal>());
        Assert.Equal(1000m, J(ayar.YeniJson)["KasaAcilisDevri"]!.GetValue<decimal>());

        var gider = await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Today, "Kira", 500m, "MEZAT", GiderTipi.Cari));
        (await c.PutAsJsonAsync("/api/gelenler", new GelenUpsertDto(Month, "MEZAT", 100m))).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/kanallar/1", new KanalYazDto("MEZAT MERKEZ", AcilisDevri: 250m))).EnsureSuccessStatusCode();

        var kanal = Assert.Single(Olaylar(f, "Kanal", 1), o => o.Tur == "Degistir");
        Assert.Equal("MEZAT", (string?)J(kanal.OncekiJson)["Ad"]); Assert.Equal("MEZAT MERKEZ", (string?)J(kanal.YeniJson)["Ad"]);
        Assert.Equal(250m, J(kanal.YeniJson)["AcilisDevri"]!.GetValue<decimal>());
        // Geçmiş gider/gelir satırlarındaki kanal metni kanal kimliğiyle eşitlenir; bu eşitleme ayrı olay değildir.
        Assert.Equal(["Ekle"], Olaylar(f, "Islem", gider.Id).Select(o => o.Tur));
        Assert.Equal(["Ekle"], Olaylar(f, "Gelen").Select(o => o.Tur));
    }

    [Fact]
    public async Task Olay_ana_islemle_ayni_transactionda_yazilir_biri_geri_alinirsa_digeri_de_kalmaz()
    {
        await using var f = Fabrika(); _ = f.Services;
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        using (db.Database.BeginTransaction())
        {
            db.Islemler.Add(new IslemEntity { Tarih = Today, Cari = "Geri alınan", TutarTl = 10m, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.Cari });
            db.SaveChanges();
            Assert.Equal(1, db.DenetimOlaylari.Count(o => o.Varlik == "Islem"));
        } // commit edilmeden kapanır: gider de olay da geri alınır
        db.ChangeTracker.Clear();
        Assert.False(db.Islemler.Any(i => i.Cari == "Geri alınan"));
        Assert.Equal(0, db.DenetimOlaylari.Count(o => o.Varlik == "Islem"));

        // Olay yazılamazsa değişiklik de kaydedilmez.
        db.Database.ExecuteSqlRaw("CREATE TRIGGER TR_Test_Olay_Reddi BEFORE INSERT ON DenetimOlaylari BEGIN SELECT RAISE(ABORT,'test: olay yazilamadi'); END;");
        db.Islemler.Add(new IslemEntity { Tarih = Today, Cari = "Olaysız", TutarTl = 10m, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.Cari });
        Assert.Contains("olay yazilamadi", Assert.ThrowsAny<Exception>(() => db.SaveChanges()).ToString());
        db.ChangeTracker.Clear();
        Assert.False(db.Islemler.Any(i => i.Cari == "Olaysız"));
        db.Database.ExecuteSqlRaw("DROP TRIGGER TR_Test_Olay_Reddi;");

        // İstek dışı yazma (bakım, açılış) 'sistem' aktörüyle yazılır.
        db.Islemler.Add(new IslemEntity { Tarih = Today, Cari = "Bakım", TutarTl = 10m, Kanal = "MEZAT", KanalId = 1, Tip = GiderTipi.Cari });
        db.SaveChanges();
        Assert.Equal("sistem", db.DenetimOlaylari.AsNoTracking().Single(o => o.Varlik == "Islem").AktorRol);
    }

    [Fact]
    public async Task Olay_satiri_ham_sql_ile_degistirilemez_ve_silinemez()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Today, "Kalıcı", 1m, "MEZAT", GiderTipi.Cari));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var once = db.DenetimOlaylari.AsNoTracking().OrderBy(o => o.Id).Select(o => o.YeniJson).ToList();
        Assert.Contains("Denetim kaydi degistirilemez.", Assert.Throws<SqliteException>(() => db.Database.ExecuteSqlRaw("UPDATE DenetimOlaylari SET YeniJson = 'sahte';")).Message);
        Assert.Contains("Denetim kaydi silinemez.", Assert.Throws<SqliteException>(() => db.Database.ExecuteSqlRaw("DELETE FROM DenetimOlaylari;")).Message);
        Assert.Equal(once, db.DenetimOlaylari.AsNoTracking().OrderBy(o => o.Id).Select(o => o.YeniJson).ToList());
    }

    [Fact]
    public async Task Ay_kilidi_acilisi_olaydir_penceresine_dusen_degisiklikler_acilisa_baglanir_yeniden_kilitte_pencere_kapanir()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        var fatura = await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(GecenAy.AddDays(4), "Ağustos faturası", 1000m, "MEZAT", GiderTipi.Cari));
        var nakit = await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(GecenAy.AddDays(9), "Nakit gider", 3000m, "MEZAT", GiderTipi.Cari));
        var kilit = await Kilit(c, "kapat", "Ağustos tamamlandı");
        kilit = await Kilit(c, "ac", "Ağustos faturası düzeltmesi");
        var acilis = kilit.Gecmis.First();
        Assert.Equal(GecenAy.AddDays(-1), acilis.YeniSonTarih);

        // Pencerede: fatura düzeltilir, nakit gider silinir; pencere dışı (hiç kilitlenmemiş) bugünkü gider.
        (await c.PutAsJsonAsync($"/api/islemler/{fatura.Id}", new IslemYazDto(GecenAy.AddDays(4), "Ağustos faturası", 1200m, "MEZAT", GiderTipi.Cari))).EnsureSuccessStatusCode();
        (await c.DeleteAsync($"/api/islemler/{nakit.Id}")).EnsureSuccessStatusCode();
        var bugunku = await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Today, "Eylül gideri", 50m, "MEZAT", GiderTipi.Cari));
        kilit = await Kilit(c, "kapat", "Ağustos yeniden kapatıldı");
        (await c.PutAsJsonAsync($"/api/islemler/{bugunku.Id}", new IslemYazDto(Today, "Eylül gideri", 60m, "MEZAT", GiderTipi.Cari))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/islemler/{fatura.Id}")).StatusCode);

        var faturaOlaylari = Olaylar(f, "Islem", fatura.Id);
        Assert.Equal([null, acilis.Id], faturaOlaylari.Select(o => o.KilitAcmaOlayiId));
        var silme = Assert.Single(Olaylar(f, "Islem", nakit.Id), o => o.Tur == "Sil");
        Assert.Equal(acilis.Id, silme.KilitAcmaOlayiId);
        Assert.Equal(3000m, J(silme.OncekiJson)["TutarTl"]!.GetValue<decimal>());
        Assert.All(Olaylar(f, "Islem", bugunku.Id), o => Assert.Null(o.KilitAcmaOlayiId));

        var kilitOlaylari = Olaylar(f, "AyKilidi");
        Assert.Equal(["KilitKapat", "KilitAc", "KilitKapat"], kilitOlaylari.Select(o => o.Tur));
        Assert.Null(kilitOlaylari[0].KilitAcmaOlayiId);
        Assert.Equal(("Ağustos faturası düzeltmesi", acilis.Id), (kilitOlaylari[1].Gerekce, kilitOlaylari[1].KilitAcmaOlayiId));
        Assert.Equal(GecenAy.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), (string?)J(kilitOlaylari[1].OncekiJson)["KilitliSonTarih"]);
        Assert.Equal(acilis.Id, kilitOlaylari[2].KilitAcmaOlayiId);
        Assert.Equal([acilis.Id], J(kilitOlaylari[2].YeniJson)["KapatilanPencereler"]!.AsArray().Select(n => n!.GetValue<int>()));

        // "Bu açılışta ne değişti?": açılışın kimliğiyle süzülen geçmiş.
        var pencere = (await c.GetFromJsonAsync<List<DenetimOlayDto>>($"/api/denetim?kilitAcmaOlayiId={acilis.Id}&varlik=Islem"))!;
        Assert.Equal([("Sil", nakit.Id.ToString(CultureInfo.InvariantCulture)), ("Degistir", fatura.Id.ToString(CultureInfo.InvariantCulture))],
            pencere.Select(o => (o.Tur, o.VarlikId!)));
    }

    [Fact]
    public async Task Takip_uclarinin_zorunlu_gerekcesi_atilmaz_onceki_durum_ve_istek_kimligiyle_okunur()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        var kart = await Post<KartTakipDto>(c, "/api/takip/kartlar", new KartTakipYaz(Guid.NewGuid(), 0, "İş kartı", 20000m, 5, 25, GecenAy, 0m, []));
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler", new KartTakipOdemeYaz(Guid.NewGuid(), kart.Surum, Today, 15000m));
        var odemeId = kart.Odemeler.Single().Id;
        var istek = Guid.NewGuid();
        kart = await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/odemeler/{odemeId}/iptal", new TakipIptalYaz(istek, kart.Surum, "bankadan iade geldi"));
        var ekstre = kart.Ekstreler.OrderBy(e => e.KesimTarihi).First();
        var yeniVade = ekstre.SonOdemeTarihi.AddDays(2);
        var ekstreYaniti = await c.PutAsJsonAsync($"/api/takip/kartlar/{kart.Id}/ekstreler/{ekstre.Id}", new KartEkstreYaz(Guid.NewGuid(), kart.Surum, yeniVade, 500m, "Banka son ödemeyi öteledi"));
        ekstreYaniti.EnsureSuccessStatusCode();
        kart = (await ekstreYaniti.Content.ReadFromJsonAsync<KartTakipDto>(Json()))!;
        await Post<KartTakipDto>(c, $"/api/takip/kartlar/{kart.Id}/durum", new TakipDurumYaz(Guid.NewGuid(), kart.Surum, false, "Kart kayboldu"));

        var iptal = Assert.Single(Olaylar(f, "TakipKartOdeme", odemeId), o => o.Tur == "Degistir");
        Assert.Equal("bankadan iade geldi", iptal.Gerekce);
        Assert.Equal(istek, iptal.IstekId);
        Assert.Equal("""{"Iptal":false}""", iptal.OncekiJson); Assert.Equal("""{"Iptal":true}""", iptal.YeniJson);
        var vade = Assert.Single(Olaylar(f, "TakipEkstre", ekstre.Id), o => o.Tur == "Degistir");
        Assert.Equal("Banka son ödemeyi öteledi", vade.Gerekce);
        Assert.Equal(ekstre.SonOdemeTarihi.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), (string?)J(vade.OncekiJson)["SonOdemeTarihi"]);
        Assert.Equal(500m, J(vade.YeniJson)["AsgariOdeme"]!.GetValue<decimal>());
        var durum = Assert.Single(Olaylar(f, "TakipKart", kart.Id), o => o.Tur == "Degistir");
        Assert.Equal(("Kart kayboldu", """{"Aktif":true}"""), (durum.Gerekce, durum.OncekiJson));

        // Aynı bilgi editörün geçmiş ucundan okunur.
        var gecmis = (await c.GetFromJsonAsync<List<DenetimOlayDto>>($"/api/denetim?varlik=TakipKartOdeme&varlikId={odemeId}"))!;
        Assert.Contains(gecmis, o => o.Gerekce == "bankadan iade geldi" && o.IstekId == istek && o.OncekiJson == """{"Iptal":false}""");
    }

    [Fact]
    public async Task Degisiklik_gecmisi_yalniz_editore_acik_varlik_ve_sayfa_suzgecleri_calisir()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        var gider = await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(Today, "Süzgeç", 1m, "MEZAT", GiderTipi.Cari));
        for (var i = 2; i <= 4; i++)
            (await c.PutAsJsonAsync($"/api/islemler/{gider.Id}", new IslemYazDto(Today, "Süzgeç", i, "MEZAT", GiderTipi.Cari))).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-sifre-123" })).EnsureSuccessStatusCode();
        using var izleyici = f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izleyici-sifre-123" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.GetAsync("/api/denetim")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("/api/denetim")).StatusCode);

        var tumu = (await c.GetFromJsonAsync<List<DenetimOlayDto>>($"/api/denetim?varlik=Islem&varlikId={gider.Id}"))!;
        Assert.Equal(["Degistir", "Degistir", "Degistir", "Ekle"], tumu.Select(o => o.Tur));
        Assert.All(tumu, o => Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(SabitAn), o.Zaman));
        var ilkSayfa = (await c.GetFromJsonAsync<List<DenetimOlayDto>>($"/api/denetim?varlik=Islem&varlikId={gider.Id}&adet=2"))!;
        var ikinciSayfa = (await c.GetFromJsonAsync<List<DenetimOlayDto>>($"/api/denetim?varlik=Islem&varlikId={gider.Id}&adet=2&oncekiId={ilkSayfa[^1].Id}"))!;
        Assert.Equal(tumu.Select(o => o.Id), ilkSayfa.Concat(ikinciSayfa).Select(o => o.Id));
        Assert.Contains((await c.GetFromJsonAsync<List<DenetimOlayDto>>("/api/denetim?tur=IzleyiciSifresiDegisti"))!, o => o.Varlik == "Ayar");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/denetim?adet=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/denetim?adet=201")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/denetim?varlikId={gider.Id}")).StatusCode);
    }

    [Fact]
    public async Task Alis_iade_gerekcesi_notsuz_onayda_kaybolmaz_ve_gecmis_ay_kanal_etkisi_iz_ve_yanitta_gorunur()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        var alis = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, GecenAy, "Tedarikçi", null, [new("Mal", 30000m, [new(1, 30000m)])]));
        (alis, _) = await Yanit<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), GecenAy.AddDays(4), 10000m));
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/gonder", new AlisDurumYaz(alis.Surum));
        (alis, var onay) = await Yanit<AlisDto>(c, $"/api/alis/{alis.Id}/onayla", new AlisDurumYaz(alis.Surum));
        Assert.Equal(GecenAy.ToString("yyyy-MM", CultureInfo.InvariantCulture), Assert.Single(onay.Headers.GetValues(AlisDurumEtkisi.YanitBasligi)));
        (alis, var iade) = await Yanit<AlisDto>(c, $"/api/alis/{alis.Id}/iade", new AlisDurumYaz(alis.Surum, "2 koli fazla yazılmış"));
        Assert.True(iade.Headers.Contains(AlisDurumEtkisi.YanitBasligi));
        var duzeltme = await c.PutAsJsonAsync($"/api/alis/{alis.Id}", new AlisYaz(alis.Surum, GecenAy, "Tedarikçi", null, [new("Mal", 30000m, [new(1, 10000m), new(2, 20000m)])]));
        duzeltme.EnsureSuccessStatusCode();
        alis = (await duzeltme.Content.ReadFromJsonAsync<AlisDto>(Json()))!;
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/gonder", new AlisDurumYaz(alis.Surum));
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/onayla", new AlisDurumYaz(alis.Surum));
        Assert.Null(alis.EditorNotu); // davranış değişmedi: notsuz onay editör notunu boşaltır

        // İade gerekçesi olayda kalır; notsuz onay onu silmez.
        var durumlar = Olaylar(f, "Alis", alis.Id).Where(o => o.Tur == "Degistir" && o.YeniJson!.Contains("\"Durum\"")).ToList();
        var iadeOlayi = Assert.Single(durumlar, o => (string?)J(o.YeniJson)["Durum"] == "Taslak" && (string?)J(o.OncekiJson)["Durum"] == "Onaylandi");
        Assert.Equal(("2 koli fazla yazılmış", "2 koli fazla yazılmış"), (iadeOlayi.Gerekce, (string?)J(iadeOlayi.YeniJson)["EditorNotu"]));
        Assert.Equal(5, durumlar.Count); // gönder, onay, iade, yeniden gönder, notsuz onay
        var gecmis = (await c.GetFromJsonAsync<List<DenetimOlayDto>>($"/api/denetim?varlik=Alis&varlikId={alis.Id}"))!;
        Assert.Contains(gecmis, o => o.Gerekce == "2 koli fazla yazılmış" && o.Tur == "Degistir");

        // Geçmiş ay etkisi: her iz ödemenin önceki ve yeni kanal paylarını taşır.
        var etkiler = Olaylar(f, "Alis", alis.Id).Where(o => o.Tur == "GecmisAyEtkisi").ToList();
        Assert.Equal(4, etkiler.Count); // geçen ay tarihli ödeme, onay, iade, yeniden onay
        var yenidenOnay = J(etkiler[^1].YeniJson);
        Assert.Equal(GecenAy.ToString("yyyy-MM", CultureInfo.InvariantCulture), (string?)yenidenOnay["GecmisAylar"]![0]);
        var paylar = yenidenOnay["Odemeler"]![0]!["Paylar"]!.AsArray();
        Assert.Equal([1, 2], paylar.Select(p => p!["KanalId"]!.GetValue<int>()));
        Assert.Equal(10000m, paylar.Sum(p => p!["Tutar"]!.GetValue<decimal>()));
        Assert.Empty(J(etkiler[^1].OncekiJson)["Odemeler"]![0]!["Paylar"]!.AsArray());
        Assert.Equal("2 koli fazla yazılmış", etkiler[2].Gerekce);
    }

    [Fact]
    public async Task Gecmis_aydaki_gideri_alisa_baglamak_iz_ve_yanit_uretir_bu_ayki_onay_uretmez()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        var gider = await Post<IslemEntity>(c, "/api/islemler", new IslemYazDto(GecenAy.AddDays(2), "Tedarikçi", 5000m, "MEZAT", GiderTipi.Cari));
        var alis = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, GecenAy, "Tedarikçi", null, [new("Mal", 5000m, [new(2, 5000m)])]));
        alis = await Post<AlisDto>(c, $"/api/alis/{alis.Id}/gonder", new AlisDurumYaz(alis.Surum));
        (alis, var onay) = await Yanit<AlisDto>(c, $"/api/alis/{alis.Id}/onayla", new AlisDurumYaz(alis.Surum));
        Assert.False(onay.Headers.Contains(AlisDurumEtkisi.YanitBasligi)); // ödemesi yok: kanal sonucu değişmez
        (alis, var bagla) = await Yanit<AlisDto>(c, $"/api/alis/{alis.Id}/odemeler", new AlisOdemeYaz(alis.Surum, Guid.NewGuid(), gider.Tarih, 5000m, MevcutIslemId: gider.Id));
        Assert.Equal(GecenAy.ToString("yyyy-MM", CultureInfo.InvariantCulture), Assert.Single(bagla.Headers.GetValues(AlisDurumEtkisi.YanitBasligi)));
        var etki = Assert.Single(Olaylar(f, "Alis", alis.Id), o => o.Tur == "GecmisAyEtkisi");
        var once = J(etki.OncekiJson)["Odemeler"]![0]!["Paylar"]![0]!;
        Assert.Equal((1, "MEZAT", 5000m), (once["KanalId"]!.GetValue<int>(), (string?)once["Etiket"], once["Tutar"]!.GetValue<decimal>()));
        var sonra = J(etki.YeniJson)["Odemeler"]![0]!["Paylar"]![0]!;
        Assert.Equal((2, 5000m), (sonra["KanalId"]!.GetValue<int>(), sonra["Tutar"]!.GetValue<decimal>()));

        var buAy = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Today, "Bu ay", null, [new("Mal", 100m, [new(1, 100m)])]));
        (buAy, var odeme) = await Yanit<AlisDto>(c, $"/api/alis/{buAy.Id}/odemeler", new AlisOdemeYaz(buAy.Surum, Guid.NewGuid(), Today, 100m));
        buAy = await Post<AlisDto>(c, $"/api/alis/{buAy.Id}/gonder", new AlisDurumYaz(buAy.Surum));
        (_, var buAyOnay) = await Yanit<AlisDto>(c, $"/api/alis/{buAy.Id}/onayla", new AlisDurumYaz(buAy.Surum));
        Assert.False(odeme.Headers.Contains(AlisDurumEtkisi.YanitBasligi));
        Assert.False(buAyOnay.Headers.Contains(AlisDurumEtkisi.YanitBasligi));
        Assert.DoesNotContain(Olaylar(f, "Alis", buAy.Id), o => o.Tur == "GecmisAyEtkisi");
    }

    [Fact]
    public async Task Belge_aylik_gider_ve_kasa_kontrolu_olaylari_icerik_yazmadan_gerekce_ve_istek_kimligiyle_yazilir()
    {
        await using var f = Fabrika(); using var c = await Editor(f);
        var alis = await Post<AlisDto>(c, "/api/alis", new AlisYaz(0, Today, "Belgeli", null, [new("Mal", 100m, [new(1, 100m)])]));
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("%PDF-1.7 gizli fatura"u8.ToArray()), "dosya", "fatura.pdf");
        using var yukle = await c.PostAsync($"/api/alis/{alis.Id}/belgeler", form);
        Assert.Equal(HttpStatusCode.Created, yukle.StatusCode);
        var belge = (await yukle.Content.ReadFromJsonAsync<BelgeDto>())!;
        (await c.DeleteAsync($"/api/belgeler/{belge.Id}")).EnsureSuccessStatusCode();
        var belgeOlaylari = Olaylar(f, "Belge", belge.Id);
        Assert.Equal(["Ekle", "Sil"], belgeOlaylari.Select(o => o.Tur));
        Assert.Equal("21 bayt", (string?)J(belgeOlaylari[0].YeniJson)["Icerik"]);
        Assert.DoesNotContain(belgeOlaylari, o => (o.YeniJson + o.OncekiJson).Contains("gizli", StringComparison.Ordinal));

        var sablon = await Create(c, "Ozel", [new(1, 100m)]);
        var odeme = await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/{sablon.Id}/ode", Payment(sablon));
        var istek = Guid.NewGuid();
        await Post<AylikGiderSatirDto>(c, $"/api/aylik-giderler/odemeler/{odeme.OdemeId}/iptal", new AylikGiderIptalYaz(istek, "Hatalı ödeme"));
        var iptal = Assert.Single(Olaylar(f, "AylikGiderOdeme", odeme.OdemeId), o => o.Tur == "Degistir");
        Assert.Equal(("Hatalı ödeme", istek), (iptal.Gerekce, iptal.IstekId));
        var silinenGider = Assert.Single(Olaylar(f, "Islem", odeme.IslemId), o => o.Tur == "Sil");
        Assert.Equal(("Hatalı ödeme", istek), (silinenGider.Gerekce, silinenGider.IstekId));

        var onizleme = await Post<KasaKontrolOnizlemeDto>(c, "/api/kasa-kontrol/onizleme", new KasaKontrolOnizle(900m, "Sayım"));
        var kontrol = await Post<KasaKontrolDto>(c, "/api/kasa-kontrol", new KasaKontrolYaz(Guid.NewGuid(), 900m, onizleme.KontrolOzeti, "Sayım"));
        var kontrolOlayi = Assert.Single(Olaylar(f, "KasaKontrol", kontrol.Id));
        Assert.Equal(900m, J(kontrolOlayi.YeniJson)["GercekBakiye"]!.GetValue<decimal>());
    }

    internal static List<DenetimOlayEntity> Olaylar(KasaWebFactory f, string varlik, object? varlikId = null)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var q = db.DenetimOlaylari.AsNoTracking().Where(o => o.Varlik == varlik);
        if (varlikId is not null)
        {
            var anahtar = Convert.ToString(varlikId, CultureInfo.InvariantCulture);
            q = q.Where(o => o.VarlikId == anahtar);
        }
        return q.OrderBy(o => o.Id).ToList();
    }

    internal static JsonNode J(string? json) => JsonNode.Parse(json!)!;

    private static JsonSerializerOptions Json()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web); json.Converters.Add(new JsonStringEnumConverter());
        return json;
    }

    private static async Task<(T Govde, HttpResponseMessage Yanit)> Yanit<T>(HttpClient c, string yol, object govde)
    {
        var r = await c.PostAsJsonAsync(yol, govde);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return ((await r.Content.ReadFromJsonAsync<T>(Json()))!, r);
    }

    private static async Task<AyKilidiDto> Kilit(HttpClient c, string islem, string aciklama)
    {
        var durum = (await c.GetFromJsonAsync<AyKilidiDto>("/api/ay-kilidi"))!;
        return await Post<AyKilidiDto>(c, $"/api/ay-kilidi/{islem}", new AyKilidiYaz(Guid.NewGuid(), durum.Surum, GecenAy.Year, GecenAy.Month, aciklama));
    }
}
