using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kasa.Api.Tests;

/// <summary>Değişiklik geçmişi: ekle/düzenle/sil kaydı, gizli alanlar, toplu ad değişimi, geri alma.</summary>
public class GecmisTests : IClassFixture<GecmisTests.SabitSaatFactory>
{
    /// <summary>Bugün = 24 Eylül 2026 (geri alma süresi ve rapor takvimi sabit olsun).</summary>
    public class SabitSaatFactory : KasaWebFactory
    {
        public static readonly DateTime SimdiUtc = new(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(new SabitSaat())));
        }

        private sealed class SabitSaat : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(SimdiUtc);
        }
    }

    private readonly SabitSaatFactory _factory;
    public GecmisTests(SabitSaatFactory factory) => _factory = factory;

    private record Satir(int Id, DateTime ZamanUtc, string Rol, string Tur, int? KayitId, string Eylem, string Ozet,
        string? EskiJson, string? YeniJson, bool GeriAlindi, DateTime? GeriAlmaZamaniUtc, bool GeriAlinabilir);

    private record IslemYanit(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, string Tip, string? Not, int? KrediKartiId);
    private record IdYanit(int Id);

    private static async Task<(List<Satir> Satirlar, int Toplam)> GecmisAsync(HttpClient c, string? tur = null, int limit = 1000, int offset = 0)
    {
        var yol = $"/api/gecmis?limit={limit}&offset={offset}" + (tur is null ? "" : $"&tur={Uri.EscapeDataString(tur)}");
        var r = await c.GetAsync(yol);
        r.EnsureSuccessStatusCode();
        var toplam = int.Parse(r.Headers.GetValues("X-Toplam-Kayit").Single());
        return ((await r.Content.ReadFromJsonAsync<List<Satir>>())!, toplam);
    }

    private static async Task<int> CariEkleAsync(HttpClient c, string ad)
    {
        var r = await c.PostAsJsonAsync("/api/cariler", new { ad, aktif = true });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<IdYanit>())!.Id;
    }

    private static async Task<int> IslemEkleAsync(HttpClient c, string cari, decimal tutar, string tarih = "2026-03-12", string? not = null)
    {
        var r = await c.PostAsJsonAsync("/api/islemler", new { tarih, cari, tutarTl = tutar, kanal = "MEZAT", tip = "Cari", not });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<IdYanit>())!.Id;
    }

    private static Task<HttpResponseMessage> GeriAlAsync(HttpClient c, int satirId)
        => c.PostAsync($"/api/gecmis/{satirId}/geri-al", null);

    /// <summary>Yanıttaki { "hata": "..." } metni (JSON kaçışları çözülmüş).</summary>
    private static async Task<string> HataAsync(HttpResponseMessage r)
        => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString()!;

    private async Task<HttpClient> IzleyiciAsync()
    {
        var editor = await _factory.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle-gecmis" })).EnsureSuccessStatusCode();
        var izleyici = _factory.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle-gecmis" })).EnsureSuccessStatusCode();
        return izleyici;
    }

    [Fact]
    public async Task Islem_ekleme_duzenleme_silme_dogru_eylem_ozet_ve_jsonla_yazilir()
    {
        var c = await _factory.EditorClientAsync();
        var id = await IslemEkleAsync(c, "Market", 1250m);
        (await c.PutAsJsonAsync($"/api/islemler/{id}", new
        {
            tarih = "2026-03-12", cari = "Market", tutarTl = 1500m, kanal = "MEZAT", tip = "Cari", not = "fatura",
        })).EnsureSuccessStatusCode();
        (await c.DeleteAsync($"/api/islemler/{id}")).EnsureSuccessStatusCode();

        var (satirlar, toplam) = await GecmisAsync(c, "İşlem");
        Assert.Equal(satirlar.Count, toplam);
        var bu = satirlar.Where(s => s.KayitId == id).ToList();
        Assert.Equal(["Silindi", "Güncellendi", "Eklendi"], bu.Select(s => s.Eylem));   // en yeni önce
        Assert.All(bu, s => { Assert.Equal("editor", s.Rol); Assert.Equal("İşlem", s.Tur); });
        Assert.All(bu, s => Assert.Equal(SabitSaatFactory.SimdiUtc, s.ZamanUtc.ToUniversalTime()));

        var (silindi, guncellendi, eklendi) = (bu[0], bu[1], bu[2]);
        Assert.Equal("İşlem eklendi: 12.03.2026 · Market · 1.250,00 ₺ · MEZAT", eklendi.Ozet);
        Assert.Null(eklendi.EskiJson);
        using (var yeni = JsonDocument.Parse(eklendi.YeniJson!))
        {
            Assert.Equal(id, yeni.RootElement.GetProperty("id").GetInt32());
            Assert.Equal(1250m, yeni.RootElement.GetProperty("tutarTl").GetDecimal());
            Assert.Equal("Cari", yeni.RootElement.GetProperty("tip").GetString());
        }

        Assert.Equal("İşlem güncellendi: 12.03.2026 · Market · 1.500,00 ₺ · MEZAT (Tutar: 1.250,00 ₺ → 1.500,00 ₺; Not: — → fatura)",
            guncellendi.Ozet);
        using (var eski = JsonDocument.Parse(guncellendi.EskiJson!))
            Assert.Equal(1250m, eski.RootElement.GetProperty("tutarTl").GetDecimal());
        using (var yeni = JsonDocument.Parse(guncellendi.YeniJson!))
            Assert.Equal("fatura", yeni.RootElement.GetProperty("not").GetString());
        Assert.False(guncellendi.GeriAlinabilir);

        Assert.Equal("İşlem silindi: 12.03.2026 · Market · 1.500,00 ₺ · MEZAT", silindi.Ozet);
        Assert.Null(silindi.YeniJson);
        using (var eski = JsonDocument.Parse(silindi.EskiJson!))
            Assert.Equal(1500m, eski.RootElement.GetProperty("tutarTl").GetDecimal());
        Assert.True(silindi.GeriAlinabilir);
    }

    [Fact]
    public async Task Degismeyen_guncelleme_gecmise_yazilmaz()
    {
        var c = await _factory.EditorClientAsync();
        var id = await IslemEkleAsync(c, "A", 10m);
        var once = (await GecmisAsync(c)).Toplam;
        (await c.PutAsJsonAsync($"/api/islemler/{id}", new { tarih = "2026-03-12", cari = "A", tutarTl = 10m, kanal = "MEZAT", tip = "Cari" }))
            .EnsureSuccessStatusCode();
        Assert.Equal(once, (await GecmisAsync(c)).Toplam);
    }

    [Fact]
    public async Task Sifre_hashi_ve_oturum_surumleri_hicbir_gecmis_satirinda_yok()
    {
        var c = await _factory.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "cok-gizli-sifre" })).EnsureSuccessStatusCode();
        (await c.PostAsync("/api/ayarlar/oturumlari-kapat", null)).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var hash = db.Ayarlar.AsNoTracking().OrderBy(a => a.Id).First().IzleyiciSifreHash!;
        var parcalar = hash.Split('.');
        var satirlar = db.Degisiklikler.AsNoTracking().ToList();
        Assert.NotEmpty(satirlar);
        foreach (var s in satirlar)
        {
            var metin = string.Join("\n", s.Ozet, s.EskiJson, s.YeniJson);
            Assert.DoesNotContain(hash, metin);
            foreach (var p in parcalar) Assert.DoesNotContain(p, metin);
            Assert.DoesNotContain("cok-gizli-sifre", metin);
            Assert.DoesNotContain("sifreHash", metin, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OturumSurumu", metin, StringComparison.OrdinalIgnoreCase);
        }
        var ayar = satirlar.Where(s => s.Tur == "Ayar").OrderBy(s => s.Id).TakeLast(2).ToList();
        Assert.Equal(["İzleyici şifresi değiştirildi", "Tüm oturumlar kapatıldı"], ayar.Select(s => s.Ozet));
        Assert.All(ayar, s => Assert.Equal("Güncellendi", s.Eylem));
    }

    [Fact]
    public async Task Ayar_degisikligi_eski_ve_yeni_degerle_ozetlenir()
    {
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 1, 5), kasaAcilis: 100m);
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 1, 5), kasaAcilis: 1250.5m);
        var son = (await GecmisAsync(c, "Ayar")).Satirlar[0];
        Assert.Equal("Ayar güncellendi (Kasa açılış devri: 100,00 ₺ → 1.250,50 ₺)", son.Ozet);
    }

    [Fact]
    public async Task Cari_kalem_ve_kanal_adi_degisince_toplu_guncelleme_tek_satirla_ozetlenir()
    {
        var c = await _factory.EditorClientAsync();
        var cariId = await CariEkleAsync(c, "Geçmiş Cari A");
        await IslemEkleAsync(c, "Geçmiş Cari A", 5m);
        await IslemEkleAsync(c, "Geçmiş Cari A", 7m);
        (await c.PutAsJsonAsync($"/api/cariler/{cariId}", new { ad = "Geçmiş Cari B", aktif = true })).EnsureSuccessStatusCode();

        var cari = (await GecmisAsync(c, "Cari")).Satirlar.Where(s => s.KayitId == cariId).ToList();
        Assert.Contains(cari, s => s.Ozet == "Cari adı değişti: Geçmiş Cari A → Geçmiş Cari B (2 işlem güncellendi)" && s.Eylem == "Güncellendi");
        Assert.Contains(cari, s => s.Ozet == "Cari güncellendi: Geçmiş Cari B (Ad: Geçmiş Cari A → Geçmiş Cari B)");

        var kalem = await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = "Geçmiş Kira", aktif = true });
        var kalemId = (await kalem.Content.ReadFromJsonAsync<IdYanit>())!.Id;
        (await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-03-01", cari = "Geçmiş Kira", tutarTl = 9m, kanal = "Ortak", tip = "SabitGider" }))
            .EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/tekrarlayangiderler", new { kalem = "Geçmiş Kira", kanal = "Ortak", tutar = 9m, ayinGunu = 5, aktif = true }))
            .EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync($"/api/giderkalemleri/{kalemId}", new { ad = "Geçmiş Kira 2", aktif = true })).EnsureSuccessStatusCode();
        Assert.Contains((await GecmisAsync(c, "Gider kalemi")).Satirlar,
            s => s.Ozet == "Gider kalemi adı değişti: Geçmiş Kira → Geçmiş Kira 2 (1 işlem, 1 tekrarlayan gider güncellendi)");

        var kanal = await c.PostAsJsonAsync("/api/kanallar", new { ad = "GKANAL", aktif = true, sira = 9, acilisDevri = 0m });
        var kanalId = (await kanal.Content.ReadFromJsonAsync<IdYanit>())!.Id;
        (await c.PostAsJsonAsync("/api/islemler", new { tarih = "2026-03-01", cari = "A", tutarTl = 3m, kanal = "GKANAL", tip = "Cari" }))
            .EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/cekler", CekGovde(kanal: "GKANAL"))).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync($"/api/kanallar/{kanalId}", new { ad = "GKANAL2", aktif = true, sira = 9, acilisDevri = 0m })).EnsureSuccessStatusCode();
        Assert.Contains((await GecmisAsync(c, "Kanal")).Satirlar,
            s => s.KayitId == kanalId && s.Ozet == "Kanal adı değişti: GKANAL → GKANAL2 (1 işlem, 1 çek güncellendi)");
    }

    [Fact]
    public async Task Tekrarlayan_gider_ve_ay_karari_turkce_ozetlenir()
    {
        var c = await _factory.EditorClientAsync();
        (await c.PostAsJsonAsync("/api/giderkalemleri", new { ad = "Geçmiş SGK", aktif = true })).EnsureSuccessStatusCode();
        var r = await c.PostAsJsonAsync("/api/tekrarlayangiderler", new { kalem = "Geçmiş SGK", kanal = "Ortak", tutar = 4_200m, ayinGunu = 15, aktif = true });
        r.EnsureSuccessStatusCode();
        var id = (await r.Content.ReadFromJsonAsync<IdYanit>())!.Id;
        (await c.PostAsJsonAsync($"/api/tekrarlayangiderler/{id}/atla", new { ay = "2026-09-01" })).EnsureSuccessStatusCode();

        Assert.Contains((await GecmisAsync(c, "Tekrarlayan gider")).Satirlar,
            s => s.KayitId == id && s.Eylem == "Eklendi" && s.Ozet == "Tekrarlayan gider eklendi: Geçmiş SGK · Ortak · 4.200,00 ₺");
        Assert.Contains((await GecmisAsync(c, "Tekrarlayan gider kararı")).Satirlar,
            s => s.Ozet == "Tekrarlayan gider kararı eklendi: Geçmiş SGK · 01.09.2026 · Atlandı");
    }

    private static object CekGovde(string kanal = "MEZAT", string durum = "Portfoyde", string? islemTarihi = null, decimal tutar = 1_500m)
        => new
        {
            yon = "Alinan", cekNo = "0099", banka = "Ziraat", kisi = "Geçmiş Firma", tutar,
            duzenlemeTarihi = "2026-09-01", vadeTarihi = "2026-10-15", kanal, durum, islemTarihi, not = "geçmiş notu",
        };

    [Fact]
    public async Task Cek_ekleme_durum_degisimi_ve_silme_turkce_ozetlenir_silinen_cek_geri_alinir()
    {
        var c = await _factory.EditorClientAsync();
        var r = await c.PostAsJsonAsync("/api/cekler", CekGovde());
        r.EnsureSuccessStatusCode();
        var id = (await r.Content.ReadFromJsonAsync<IdYanit>())!.Id;
        (await c.PutAsJsonAsync($"/api/cekler/{id}", CekGovde(durum: "TahsilEdildi", islemTarihi: "2026-09-20"))).EnsureSuccessStatusCode();
        var asil = (await c.GetFromJsonAsync<List<CekYanit>>("/api/cekler", CekJson))!.Single(x => x.Id == id);

        var satirlar = (await GecmisAsync(c, "Çek")).Satirlar.Where(s => s.KayitId == id).ToList();
        Assert.Contains(satirlar, s => s.Eylem == "Eklendi" && s.Ozet == "Çek eklendi: Alınan · Geçmiş Firma · 1.500,00 ₺ · 15.10.2026");
        Assert.Contains(satirlar, s => s.Eylem == "Güncellendi"
            && s.Ozet == "Çek güncellendi: Alınan · Geçmiş Firma · 1.500,00 ₺ · 15.10.2026 (Durum: Portföyde → Tahsil edildi; İşlem tarihi: — → 20.09.2026)");

        (await c.DeleteAsync($"/api/cekler/{id}")).EnsureSuccessStatusCode();
        var silindi = (await GecmisAsync(c, "Çek")).Satirlar.Single(s => s.KayitId == id && s.Eylem == "Silindi");
        Assert.True(silindi.GeriAlinabilir);

        var g = await GeriAlAsync(c, silindi.Id);
        Assert.Equal(HttpStatusCode.OK, g.StatusCode);
        var yeniId = (await g.Content.ReadFromJsonAsync<IdYanit>())!.Id;
        // Aynı alanlarla (tahsil tarihi dahil) geri gelir: kasa etkisi de geri döner.
        Assert.Equal(asil with { Id = yeniId }, (await c.GetFromJsonAsync<List<CekYanit>>("/api/cekler", CekJson))!.Single(x => x.Id == yeniId));
        Assert.Contains((await GecmisAsync(c, "Çek")).Satirlar, s => s.KayitId == yeniId && s.Eylem == "Eklendi (geri alındı)");
    }

    [Fact]
    public async Task Kanali_silinmis_cek_geri_alinamaz()
    {
        var c = await _factory.EditorClientAsync();
        var kanal = await c.PostAsJsonAsync("/api/kanallar", new { ad = "GCEKKANAL", aktif = true, sira = 11, acilisDevri = 0m });
        var kanalId = (await kanal.Content.ReadFromJsonAsync<IdYanit>())!.Id;
        var r = await c.PostAsJsonAsync("/api/cekler", CekGovde(kanal: "GCEKKANAL"));
        var id = (await r.Content.ReadFromJsonAsync<IdYanit>())!.Id;
        (await c.DeleteAsync($"/api/cekler/{id}")).EnsureSuccessStatusCode();
        (await c.DeleteAsync($"/api/kanallar/{kanalId}")).EnsureSuccessStatusCode();

        var silindi = (await GecmisAsync(c, "Çek")).Satirlar.Single(s => s.KayitId == id && s.Eylem == "Silindi");
        var g = await GeriAlAsync(c, silindi.Id);
        Assert.Equal(HttpStatusCode.BadRequest, g.StatusCode);
        Assert.Contains("Geri alınamadı: 'GCEKKANAL' adında bir kanal yok", await HataAsync(g));
    }

    [Fact]
    public async Task Silinen_kasa_sayimi_ayni_defter_degeriyle_geri_alinir()
    {
        var c = await _factory.EditorClientAsync();
        var r = await c.PostAsJsonAsync("/api/kasasayimlari", new { tarih = "2026-09-24", sayilanTutar = 777.25m, not = "akşam sayımı" });
        r.EnsureSuccessStatusCode();
        var id = (await r.Content.ReadFromJsonAsync<IdYanit>())!.Id;
        var asil = (await c.GetFromJsonAsync<List<SayimYanit>>("/api/kasasayimlari"))!.Single(s => s.Id == id);

        (await c.DeleteAsync($"/api/kasasayimlari/{id}")).EnsureSuccessStatusCode();
        var silindi = (await GecmisAsync(c, "Kasa sayımı")).Satirlar.Single(s => s.KayitId == id && s.Eylem == "Silindi");
        Assert.Equal("Kasa sayımı silindi: 24.09.2026 · 777,25 ₺", silindi.Ozet);

        var g = await GeriAlAsync(c, silindi.Id);
        Assert.Equal(HttpStatusCode.OK, g.StatusCode);
        var yeniId = (await g.Content.ReadFromJsonAsync<IdYanit>())!.Id;
        var geri = (await c.GetFromJsonAsync<List<SayimYanit>>("/api/kasasayimlari"))!.Single(s => s.Id == yeniId);
        Assert.Equal(asil with { Id = yeniId }, geri);
    }

    private record CekYanit(int Id, string Yon, string? CekNo, string? Banka, string Kisi, decimal Tutar, DateOnly DuzenlemeTarihi,
        DateOnly VadeTarihi, string Kanal, string Durum, DateOnly? IslemTarihi, string? Not);
    private static readonly JsonSerializerOptions CekJson = new(JsonSerializerDefaults.Web);

    private record SayimYanit(int Id, DateOnly Tarih, decimal SayilanTutar, decimal HesaplananTutar, string? Not, DateTime KayitZamaniUtc);

    [Fact]
    public async Task Geri_al_islemi_ayni_alanlarla_getirir_ve_haftalik_rapor_eski_rakamlara_doner()
    {
        var c = await _factory.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 1, 5), kasaAcilis: 1_000m);
        (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-06-08", kanal = "MEZAT", tutarTl = 5_000m })).EnsureSuccessStatusCode();
        var id = await IslemEkleAsync(c, "Market", 1_234.56m, tarih: "2026-06-10", not: "geri alınacak");
        var once = await c.GetStringAsync("/api/rapor/haftalik");
        var islemler = await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler", KasaApiJson);
        var asil = islemler!.Single(i => i.Id == id);

        (await c.DeleteAsync($"/api/islemler/{id}")).EnsureSuccessStatusCode();
        Assert.NotEqual(once, await c.GetStringAsync("/api/rapor/haftalik"));

        var silindi = (await GecmisAsync(c, "İşlem")).Satirlar.Single(s => s.KayitId == id && s.Eylem == "Silindi");
        var r = await GeriAlAsync(c, silindi.Id);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var yeniId = (await r.Content.ReadFromJsonAsync<IdYanit>())!.Id;
        Assert.NotEqual(id, yeniId);

        var geri = (await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler", KasaApiJson))!.Single(i => i.Id == yeniId);
        Assert.Equal(asil with { Id = yeniId }, geri);
        Assert.Equal(once, await c.GetStringAsync("/api/rapor/haftalik"));

        var satirlar = (await GecmisAsync(c, "İşlem")).Satirlar;
        var eskiSatir = satirlar.Single(s => s.Id == silindi.Id);
        Assert.True(eskiSatir.GeriAlindi);
        Assert.False(eskiSatir.GeriAlinabilir);
        Assert.Equal(SabitSaatFactory.SimdiUtc, eskiSatir.GeriAlmaZamaniUtc!.Value.ToUniversalTime());
        var geriSatir = satirlar.Single(s => s.KayitId == yeniId);
        Assert.Equal("Eklendi (geri alındı)", geriSatir.Eylem);
        Assert.Equal("İşlem eklendi (geri alındı): 10.06.2026 · Market · 1.234,56 ₺ · MEZAT", geriSatir.Ozet);

        // İkinci kez geri alınamaz.
        var ikinci = await GeriAlAsync(c, silindi.Id);
        Assert.Equal(HttpStatusCode.Conflict, ikinci.StatusCode);
        Assert.Contains("zaten geri alındı", await HataAsync(ikinci));
        Assert.Single((await c.GetFromJsonAsync<List<IslemYanit>>("/api/islemler", KasaApiJson))!,
            i => i.Not == "geri alınacak");
    }

    private static readonly JsonSerializerOptions KasaApiJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Carisi_silinmis_islem_geri_alinamaz_400_cari_geri_alininca_alinir()
    {
        var c = await _factory.EditorClientAsync();
        var cariId = await CariEkleAsync(c, "Silinecek Cari");
        var id = await IslemEkleAsync(c, "Silinecek Cari", 42m);
        (await c.DeleteAsync($"/api/islemler/{id}")).EnsureSuccessStatusCode();
        (await c.DeleteAsync($"/api/cariler/{cariId}")).EnsureSuccessStatusCode();

        var islemSatiri = (await GecmisAsync(c, "İşlem")).Satirlar.Single(s => s.KayitId == id && s.Eylem == "Silindi");
        var r = await GeriAlAsync(c, islemSatiri.Id);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Geri alınamadı: 'Silinecek Cari' adında bir cari yok", await HataAsync(r));
        Assert.True((await GecmisAsync(c, "İşlem")).Satirlar.Single(s => s.Id == islemSatiri.Id).GeriAlinabilir);   // hâlâ denenebilir

        var cariSatiri = (await GecmisAsync(c, "Cari")).Satirlar.Single(s => s.KayitId == cariId && s.Eylem == "Silindi");
        Assert.Equal("Cari silindi: Silinecek Cari", cariSatiri.Ozet);
        Assert.Equal(HttpStatusCode.OK, (await GeriAlAsync(c, cariSatiri.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GeriAlAsync(c, islemSatiri.Id)).StatusCode);
    }

    [Fact]
    public async Task Ayni_adla_cari_varsa_silinen_cari_geri_alinamaz()
    {
        var c = await _factory.EditorClientAsync();
        var cariId = await CariEkleAsync(c, "Tekil Cari");
        (await c.DeleteAsync($"/api/cariler/{cariId}")).EnsureSuccessStatusCode();
        await CariEkleAsync(c, "TEKİL CARİ");
        var satir = (await GecmisAsync(c, "Cari")).Satirlar.Single(s => s.KayitId == cariId && s.Eylem == "Silindi");
        var r = await GeriAlAsync(c, satir.Id);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("adında bir cari zaten var", await HataAsync(r));
    }

    [Fact]
    public async Task Kart_odemesi_silinince_geri_alinir_ozet_kart_adini_gosterir()
    {
        var c = await _factory.EditorClientAsync();
        var kart = await c.PostAsJsonAsync("/api/kredikartlari", new
        {
            ad = "Geçmiş Bonus", kesimTarihi = "2026-09-05", sonOdemeTarihi = "2026-09-15", limit = 10_000m, borc = 0m,
        });
        var kartId = (await kart.Content.ReadFromJsonAsync<IdYanit>())!.Id;
        var odeme = await c.PostAsJsonAsync("/api/kartodemeler", new { krediKartiId = kartId, tarih = "2026-09-10", tutar = 300m });
        var odemeId = (await odeme.Content.ReadFromJsonAsync<IdYanit>())!.Id;
        (await c.DeleteAsync($"/api/kartodemeler/{odemeId}")).EnsureSuccessStatusCode();

        var satir = (await GecmisAsync(c, "Kart ödemesi")).Satirlar.Single(s => s.KayitId == odemeId && s.Eylem == "Silindi");
        Assert.Equal("Kart ödemesi silindi: 10.09.2026 · Geçmiş Bonus · 300,00 ₺", satir.Ozet);
        Assert.Equal(HttpStatusCode.OK, (await GeriAlAsync(c, satir.Id)).StatusCode);
        var odemeler = await c.GetFromJsonAsync<List<JsonElement>>($"/api/kartodemeler?krediKartiId={kartId}");
        Assert.Single(odemeler!);
    }

    [Fact]
    public async Task Guncelleme_ve_30_gunden_eski_silme_geri_alinamaz()
    {
        var c = await _factory.EditorClientAsync();
        var id = await IslemEkleAsync(c, "B", 8m);
        var eklendi = (await GecmisAsync(c, "İşlem")).Satirlar.Single(s => s.KayitId == id);
        var r = await GeriAlAsync(c, eklendi.Id);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("Yalnız silinen kayıtlar geri alınabilir.", await HataAsync(r));

        (await c.DeleteAsync($"/api/islemler/{id}")).EnsureSuccessStatusCode();
        int satirId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var satir = db.Degisiklikler.Single(s => s.KayitId == id && s.Tur == "İşlem" && s.Eylem == "Silindi");
            satir.ZamanUtc = SabitSaatFactory.SimdiUtc.AddDays(-31);
            db.SaveChanges();
            satirId = satir.Id;
        }
        Assert.False((await GecmisAsync(c, "İşlem")).Satirlar.Single(s => s.Id == satirId).GeriAlinabilir);
        var eski = await GeriAlAsync(c, satirId);
        Assert.Equal(HttpStatusCode.BadRequest, eski.StatusCode);
        Assert.Contains("30 günden eski", await HataAsync(eski));
        Assert.Equal(HttpStatusCode.NotFound, (await GeriAlAsync(c, 987_654)).StatusCode);
    }

    [Fact]
    public async Task Izleyici_gecmisi_okur_ama_geri_alamaz()
    {
        var editor = await _factory.EditorClientAsync();
        var id = await IslemEkleAsync(editor, "X", 11m);
        (await editor.DeleteAsync($"/api/islemler/{id}")).EnsureSuccessStatusCode();
        var satir = (await GecmisAsync(editor, "İşlem")).Satirlar.Single(s => s.KayitId == id && s.Eylem == "Silindi");

        var izleyici = await IzleyiciAsync();
        var (satirlar, _) = await GecmisAsync(izleyici, "İşlem");
        Assert.Contains(satirlar, s => s.Id == satir.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await GeriAlAsync(izleyici, satir.Id)).StatusCode);
        var turler = await izleyici.GetFromJsonAsync<List<string>>("/api/gecmis/turler");
        Assert.Contains("İşlem", turler!);
    }

    [Fact]
    public async Task Sayfalama_en_yeni_once_ve_toplam_basligi()
    {
        var c = await _factory.EditorClientAsync();
        for (int i = 0; i < 3; i++) await IslemEkleAsync(c, "A", 1m + i);
        var (hepsi, toplam) = await GecmisAsync(c);
        Assert.Equal(hepsi.Count, toplam);
        Assert.Equal(hepsi.OrderByDescending(s => s.Id).Select(s => s.Id), hepsi.Select(s => s.Id));
        var (sayfa, toplam2) = await GecmisAsync(c, limit: 2, offset: 1);
        Assert.Equal(toplam, toplam2);
        Assert.Equal(hepsi.Skip(1).Take(2).Select(s => s.Id), sayfa.Select(s => s.Id));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/gecmis?limit=0")).StatusCode);
    }
}

/// <summary>Açılış (seed) ve şema göçü geçmişe satır yazmaz; eski satırlar açılışta temizlenir.</summary>
public class GecmisAcilisTests
{
    [Fact]
    public void Yeni_acilis_ve_test_tohumlamasi_gecmise_yazmaz()
    {
        using var f = new KasaWebFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        Assert.True(db.Kanallar.Any());   // seed yapıldı
        Assert.True(db.Cariler.Any());    // test carileri eklendi
        Assert.Equal(0, db.Degisiklikler.Count());
    }

    private static KasaDbContext Ac(SqliteConnection conn)
        => new(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(conn).Options, new HttpContextAccessor());

    private static void Calistir(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Var_olan_dbde_tablo_eklenir_goc_ve_seed_satir_yazmaz()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var db = Ac(conn)) db.Database.EnsureCreated();
        // Geçmiş tablosu olmayan eski bir DB + gider kalemi tablosu da yok (göç veri ekler).
        Calistir(conn, "DROP TABLE \"Degisiklikler\"");
        Calistir(conn, "DROP TABLE \"GiderKalemleri\"");
        Calistir(conn, "INSERT INTO \"Islemler\" (\"Tarih\", \"Cari\", \"TutarTl\", \"Kanal\", \"Tip\") VALUES ('2026-06-01', 'Kira', '5.0', 'Ortak', 1)");

        using (var db = Ac(conn))
        {
            var yapilan = SemaGuncelleyici.Guncelle(db, NullLogger.Instance);
            Assert.Contains("tablo+ Degisiklikler", yapilan);
            Assert.Contains("tablo+ GiderKalemleri", yapilan);
        }
        using (var db = Ac(conn))
        {
            VeritabaniBaslatici.Baslat(db, NullLogger.Instance, yedekKlasoru: null);
            Assert.True(db.Kanallar.Any());
            Assert.Equal(["Kira"], db.GiderKalemleri.Select(k => k.Ad).ToList());
            Assert.Equal(0, db.Degisiklikler.Count());
        }
    }

    [Fact]
    public void Iki_yildan_eski_gecmis_acilista_silinir()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var db = Ac(conn))
        {
            db.Database.EnsureCreated();
            db.Degisiklikler.AddRange(
                new DegisiklikEntity { ZamanUtc = DateTime.UtcNow.AddYears(-3), Rol = "editor", Tur = "İşlem", Eylem = "Silindi", Ozet = "eski" },
                new DegisiklikEntity { ZamanUtc = DateTime.UtcNow.AddYears(-2).AddDays(-1), Rol = "editor", Tur = "İşlem", Eylem = "Silindi", Ozet = "sınırda" },
                new DegisiklikEntity { ZamanUtc = DateTime.UtcNow.AddYears(-1), Rol = "editor", Tur = "İşlem", Eylem = "Silindi", Ozet = "yeni" });
            db.SaveChanges();
        }
        using (var db = Ac(conn))
        {
            VeritabaniBaslatici.Baslat(db, NullLogger.Instance, yedekKlasoru: null);
            Assert.Equal(["yeni"], db.Degisiklikler.Select(d => d.Ozet).ToList());
        }
    }
}
