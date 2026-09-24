using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kasa.Api.Data;
using static Kasa.Api.Tests.PaketB;
using static Kasa.Api.Tests.PaketD;

namespace Kasa.Api.Tests;

/// <summary>
/// Paketlerin birleşiminde (B ay kilidi/ay paketi/hedef-bütçe, C korumalı gelen ve eksik gelen listesi,
/// D çek evrakı/sayım farkı/şablon sıklığı, E kişi-cihaz geçmişi, F belge alanları) bulunan uyuşmazlıklar.
/// </summary>
public class EntegrasyonBfTests : IClassFixture<PaketBFactory>
{
    private readonly PaketBFactory _f;
    public EntegrasyonBfTests(PaketBFactory f) => _f = f;

    private async Task<HttpClient> HazirlaAsync(DateOnly bugun)
    {
        _f.Temizle();
        _f.Saat.Ayarla(bugun);
        var c = await _f.EditorClientAsync();
        await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 6, 1));
        return c;
    }

    private static async Task Kilitle(HttpClient c, int yil, int ay)
        => Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/ay-kapanisi/kilitle", new { yil, ay })).StatusCode);

    private static async Task<JsonElement> KilitliCakisma(HttpResponseMessage r, string ay = "Ağustos 2026")
    {
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(j.GetProperty("kilitli").GetBoolean());
        Assert.Contains(ay, j.GetProperty("hata").GetString());
        return j;
    }

    // ------------------------------------------------------------ B × C: kilitli ay ve gelen

    /// <summary>
    /// Bulgu: ay kilidinin 409'u korumalı gelen yazımının "siz açtıktan sonra değişti" 409'undan ayırt
    /// edilemiyordu (uygulama "Üzerine yaz" sorusu açıyor, o da aynı 409'a dönüyordu); eksik gelen
    /// listeleri de yazılamayan kilitli ayların dönemlerini "Gelen gir" diye gösteriyordu.
    /// </summary>
    [Fact]
    public async Task Kilitli_ay_409u_isaretli_eksik_gelen_listeleri_kilitli_ayi_gostermez()
    {
        var c = await HazirlaAsync(new DateOnly(2026, 9, 10));
        static List<DateOnly> Donemler(JsonElement l) => l.EnumerateArray().Select(x => DateOnly.Parse(x.GetProperty("donemStart").GetString()!)).ToList();

        await GelenYaz(c, new DateOnly(2026, 6, 1), "MEZAT", 1m);   // kanalın başlangıcı bilinsin (eksik liste)
        Assert.Contains(Donemler(await Oku(c, "/api/gelenler/eksik")), d => d.Month == 8);
        Assert.Contains(Donemler(await Oku(c, "/api/gelenler/eksik-liste")), d => d.Month is 6 or 7 or 8);

        await Kilitle(c, 2026, 8);   // Haziran ve Temmuz da (geriye doğru) kilitli sayılır

        var panel = Donemler(await Oku(c, "/api/gelenler/eksik"));
        Assert.NotEmpty(panel);
        Assert.All(panel, d => Assert.True(d >= new DateOnly(2026, 9, 1), $"Kilitli ayın dönemi listelendi: {d}"));
        var liste = Donemler(await Oku(c, "/api/gelenler/eksik-liste"));
        Assert.NotEmpty(liste);
        Assert.All(liste, d => Assert.True(d >= new DateOnly(2026, 9, 1), $"Kilitli ayın dönemi listelendi: {d}"));

        // Düz ve korumalı yazım: ikisi de kilit işaretli 409; korumalı yazım "mevcutTutar" döndürmez.
        await KilitliCakisma(await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-08-24", kanal = "MEZAT", tutarTl = 5m }));
        var korumali = await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-08-24", kanal = "MEZAT", tutarTl = 5m, beklenenTutar = 0m });
        Assert.False((await KilitliCakisma(korumali)).TryGetProperty("mevcutTutar", out _));
        // Kilit dışı çakışma (başkası değiştirmiş) işaretsizdir.
        await GelenYaz(c, new DateOnly(2026, 9, 7), "MEZAT", 10m);
        var cakisma = await c.PutAsJsonAsync("/api/gelenler", new { donemStart = "2026-09-07", kanal = "MEZAT", tutarTl = 20m, beklenenTutar = 0m });
        Assert.Equal(HttpStatusCode.Conflict, cakisma.StatusCode);
        var cj = await cakisma.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10m, cj.GetProperty("mevcutTutar").GetDecimal());
        Assert.False(cj.TryGetProperty("kilitli", out _));
    }

    // ------------------------------------------------------------ D × F: güncellemeyi geri alma

    private static async Task<JsonElement> SonIslemGuncellemesi(HttpClient c, int id)
        => (await Gecmis(c, GecmisTurleri.Islem)).First(g => g.GetProperty("kayitId").ValueKind == JsonValueKind.Number
                                                          && g.GetProperty("kayitId").GetInt32() == id && g.Str("eylem") == "Güncellendi");

    private static async Task<JsonElement> IslemOku(HttpClient c, int id)
        => (await GetJson(c, "/api/islemler?baslangic=2026-08-01&bitis=2026-08-31")).EnumerateArray().Single(i => i.Id() == id);

    /// <summary>
    /// Bulgu: "Önceki haline döndür" belge alanlarını (paket F) geri almıyordu; kilitli ayda "Fatura
    /// geldi" yanlışlıkla işaretlenirse geri alma "başarılı" deyip belgeyi olduğu gibi bırakıyordu.
    /// </summary>
    [Fact]
    public async Task Kilitli_ayda_yalniz_belge_guncellemesi_geri_alinir_belge_alanlari_doner()
    {
        var c = await HazirlaAsync(new DateOnly(2026, 9, 24));
        var id = await IslemEkle(c, new DateOnly(2026, 8, 10), 200m);
        await Basarili(await c.PutAsJsonAsync($"/api/islemler/{id}/belge", new { belgeTuru = (string?)null, belgeNo = (string?)null, faturaBekleniyor = true }));
        await Kilitle(c, 2026, 8);

        await Basarili(await c.PutAsJsonAsync($"/api/islemler/{id}/belge", new { belgeTuru = "EFatura", belgeNo = "F-1", faturaBekleniyor = false }));
        var satir = await SonIslemGuncellemesi(c, id);
        Assert.True(satir.Bool("geriAlinabilir"));

        await Basarili(await GeriAl(c, satir.Id()));
        var islem = await IslemOku(c, id);
        Assert.True(islem.Null("belgeTuru"));
        Assert.True(islem.Null("belgeNo"));
        Assert.True(islem.Bool("faturaBekleniyor"));
        Assert.Equal(200m, islem.Dec("tutarTl"));
        Assert.True((await Gecmis(c, GecmisTurleri.Islem)).Single(g => g.Id() == satir.Id()).Bool("geriAlindi"));
    }

    [Fact]
    public async Task Kilitli_ayda_tutari_da_degistiren_guncelleme_geri_alinamaz_kayit_degismez()
    {
        var c = await HazirlaAsync(new DateOnly(2026, 9, 24));
        var id = await IslemEkle(c, new DateOnly(2026, 8, 10), 200m);
        await Basarili(await c.PutAsJsonAsync($"/api/islemler/{id}", new
        {
            tarih = "2026-08-10", cari = "X", tutarTl = 250m, kanal = "MEZAT", tip = "Cari", belgeTuru = "EFatura", belgeNo = "F-9", faturaBekleniyor = false,
        }));
        var satir = await SonIslemGuncellemesi(c, id);
        await Kilitle(c, 2026, 8);

        await KilitliCakisma(await GeriAl(c, satir.Id()));
        var islem = await IslemOku(c, id);
        Assert.Equal(250m, islem.Dec("tutarTl"));
        Assert.Equal("F-9", islem.Str("belgeNo"));
        Assert.False((await Gecmis(c, GecmisTurleri.Islem)).Single(g => g.Id() == satir.Id()).Bool("geriAlindi"));
    }

    // ------------------------------------------------------------ B × D: kilitli ayda bilgi alanları

    private static Dictionary<string, object?> CekGovdesi(string durum, string? islemTarihi, string? konum = null, string? ciro = null,
        decimal tutar = 1_000m, string tur = "Cek", string yon = "Alinan", string kisi = "Müşteri", string vade = "2026-08-12")
    {
        var d = new Dictionary<string, object?>
        {
            ["yon"] = yon, ["cekNo"] = "001", ["banka"] = "Ziraat", ["kisi"] = kisi, ["tutar"] = tutar,
            ["duzenlemeTarihi"] = "2026-07-01", ["vadeTarihi"] = vade, ["kanal"] = "MEZAT", ["durum"] = durum,
            ["islemTarihi"] = islemTarihi, ["not"] = null, ["ciroEdilenCari"] = ciro, ["tur"] = tur,
        };
        if (konum is not null) d["konum"] = konum;
        return d;
    }

    /// <summary>
    /// Bulgu: paket D'nin kasaya etkisi olmayan alanları (evrakın konumu, ciro edilen cari, sayım farkının
    /// durumu/açıklaması) kilitli ayda da 409 alıyordu; kapanmış ayın sayım farkı açıklanamıyordu.
    /// Para ve tarih alanları, silme ve evrak türü kilitli kalır.
    /// </summary>
    [Fact]
    public async Task Kilitli_ayda_evrak_konumu_ciro_carisi_ve_sayim_farki_yazilir_para_kilitli()
    {
        var c = await HazirlaAsync(new DateOnly(2026, 9, 24));
        var tahsil = await Basarili(await c.PostAsJsonAsync("/api/cekler", CekGovdesi("TahsilEdildi", "2026-08-12", konum: "BankadaTahsilde")));
        var ciro = await Basarili(await c.PostAsJsonAsync("/api/cekler", CekGovdesi("CiroEdildi", "2026-08-14", ciro: "Tedarikçi A", kisi: "Ciro Müşterisi")));
        var defter = (await GetJson(c, "/api/kasasayimlari/hesapla?tarih=2026-08-31")).Dec("hesaplananTutar");
        var sayim = await Basarili(await c.PostAsJsonAsync("/api/kasasayimlari", new { tarih = "2026-08-31", sayilanTutar = defter + 12.5m }));
        await Kilitle(c, 2026, 8);

        var k = await Basarili(await c.PutAsJsonAsync($"/api/cekler/{tahsil.Id()}", CekGovdesi("TahsilEdildi", "2026-08-12", konum: "Elde")));
        Assert.Equal("Elde", k.Str("konum"));
        var cr = await Basarili(await c.PutAsJsonAsync($"/api/cekler/{ciro.Id()}",
            CekGovdesi("CiroEdildi", "2026-08-14", ciro: "Tedarikçi B", kisi: "Ciro Müşterisi")));
        Assert.Equal("Tedarikçi B", cr.Str("ciroEdilenCari"));
        var f = await Basarili(await c.PutAsJsonAsync($"/api/kasasayimlari/{sayim.Id()}/fark", new { durum = "Aciklandi", aciklama = "Bozuk para sayılmadı" }));
        Assert.Equal("Aciklandi", f.Str("farkDurumu"));
        Assert.Equal(12.5m, f.Dec("fark"));

        await KilitliCakisma(await c.PutAsJsonAsync($"/api/cekler/{tahsil.Id()}", CekGovdesi("TahsilEdildi", "2026-08-12", konum: "Elde", tutar: 1_100m)));
        await KilitliCakisma(await c.PutAsJsonAsync($"/api/cekler/{tahsil.Id()}", CekGovdesi("TahsilEdildi", "2026-08-12", konum: "Elde", tur: "Senet")));
        await KilitliCakisma(await c.DeleteAsync($"/api/kasasayimlari/{sayim.Id()}"));
        await KilitliCakisma(await c.DeleteAsync($"/api/cekler/{ciro.Id()}"));
    }

    // ------------------------------------------------------------ B × D: şablon sıklığı, değişken tutar, kart

    private static JsonElement Kalem(JsonElement d, string ad) => d.GetProperty("kalemler").EnumerateArray().Single(k => k.Str("kalem") == ad);

    /// <summary>
    /// Bulgu: hedef-bütçe ve kalem özeti şablonu her ay "aktif şablonların toplamı" sayıyordu: yıllık
    /// sigorta her ay beklenmiş görünüyor, tutarı her seferinde girilen vergi 0 ₺ şablon diye
    /// karşılaştırılıyor, karta bağlı şablonun (kalemi bir cari adı) tutarı aynı adlı kaleme ekleniyordu.
    /// </summary>
    [Fact]
    public async Task Sablon_sikligina_gore_ayina_duser_degisken_tutar_bilinmez_kart_sablonu_kaleme_girmez()
    {
        var c = await HazirlaAsync(new DateOnly(2026, 9, 24));
        foreach (var ad in new[] { "Sigorta", "Vergi", "Aidat" })
            await c.PostAsJsonAsync("/api/giderkalemleri", new { ad, aktif = true });   // varsa 409: önemsiz
        await c.PostAsJsonAsync("/api/cariler", new { ad = "Aidat", aktif = true });
        var kart = await Basarili(await c.PostAsJsonAsync("/api/kredikartlari",
            new { ad = "Bonus", kesimTarihi = "2026-01-15", sonOdemeTarihi = "2026-01-25", limit = 10_000m, borc = 0m }));
        foreach (var govde in new object[]
                 {
                     new { kalem = "Sigorta", kanal = "Ortak", tutar = 12_000m, ayinGunu = 10, aktif = true, baslangicAyi = "2026-07-01", siklik = "Yillik" },
                     new { kalem = "Vergi", kanal = "Ortak", tutar = 0m, ayinGunu = 20, aktif = true, baslangicAyi = "2026-07-01", tutarDegisken = true },
                     new { kalem = "Aidat", kanal = "Ortak", tutar = 1_000m, ayinGunu = 5, aktif = true, baslangicAyi = "2026-07-01" },
                 })
            await Basarili(await c.PostAsJsonAsync("/api/tekrarlayangiderler", govde));
        await Basarili(await c.PostAsJsonAsync("/api/tekrarlayangiderler",
            new { kalem = "Aidat", kanal = "MEZAT", tutar = 99m, ayinGunu = 3, aktif = true, baslangicAyi = "2026-07-01", krediKartiId = kart.Id() }));

        var temmuz = await Oku(c, "/api/hedef-butce?yil=2026&ay=7");
        var agustos = await Oku(c, "/api/hedef-butce?yil=2026&ay=8");
        Assert.Equal(12_000m, Kalem(temmuz, "Sigorta").Dec("sablon"));
        Assert.True(Kalem(agustos, "Sigorta").Null("sablon"));
        Assert.True(Kalem(agustos, "Vergi").Null("sablon"));
        Assert.Equal(1_000m, Kalem(agustos, "Aidat").Dec("sablon"));

        var sigorta = await Oku(c, "/api/rapor/cari-ozeti?ad=Sigorta&yil=2026&tur=kalem");
        var aylar = sigorta.GetProperty("aylar").EnumerateArray().ToList();
        Assert.Equal(12_000m, aylar[6].Dec("sablon"));
        Assert.All(aylar.Where((_, i) => i != 6), a => Assert.True(a.Null("sablon")));
        Assert.Equal(12_000m, sigorta.Dec("sablonToplam"));

        var vergi = await Oku(c, "/api/rapor/cari-ozeti?ad=Vergi&yil=2026&tur=kalem");
        Assert.All(vergi.GetProperty("aylar").EnumerateArray(), a => Assert.True(a.Null("sablon")));
        Assert.True(vergi.Null("sablonToplam"));

        var aidat = await Oku(c, "/api/rapor/cari-ozeti?ad=Aidat&yil=2026&tur=kalem");
        Assert.Equal(1_000m, aidat.GetProperty("aylar")[7].Dec("sablon"));
        Assert.Equal(6 * 1_000m, aidat.Dec("sablonToplam"));
    }

    // ------------------------------------------------------------ B × E: ay kapanışındaki değişiklikler

    /// <summary>
    /// Bulgu: ay kapanışındaki "yayından sonraki değişiklikler" listesi geçmiş ekranından farklı kuruluyordu:
    /// kim/hangi cihaz (paket E) ve geçmişe dönük işareti boş, sonradan yeniden değişen kayıt "geri
    /// alınabilir" görünüyordu. Geçmiş CSV'sinde de kişi ve cihaz yoktu.
    /// </summary>
    [Fact]
    public async Task Ay_kapanisi_degisiklikleri_gecmisle_ayni_kisi_cihaz_ve_geri_alinabilirlik()
    {
        var c = await HazirlaAsync(new DateOnly(2026, 9, 24));
        var id = await IslemEkle(c, new DateOnly(2026, 8, 10), 100m);
        await Basarili(await c.PostAsJsonAsync("/api/ay-kapanisi/yayinla", new { yil = 2026, ay = 8 }));

        var laptop = await PaketEYardimci.EditorAsync(_f, cihaz: "EMAR-LAPTOP");
        foreach (var tutar in new[] { 150m, 175m })
            await Basarili(await laptop.PutAsJsonAsync($"/api/islemler/{id}", new { tarih = "2026-08-10", cari = "X", tutarTl = tutar, kanal = "MEZAT", tip = "Cari" }));

        var ay = (await Oku(c, "/api/ay-kapanisi?yil=2026&ay=8")).GetProperty("degisiklikler").EnumerateArray().ToList();
        var gecmis = (await Gecmis(c)).ToDictionary(g => g.Id());
        var guncellemeler = ay.Where(g => g.Str("eylem") == "Güncellendi").OrderBy(g => g.Id()).ToList();
        Assert.Equal(2, guncellemeler.Count);
        foreach (var g in ay)
        {
            var e = gecmis[g.Id()];
            foreach (var alan in new[] { "kullanici", "cihaz", "gecmiseDonuk", "geriAlinabilir", "geriAlindi", "ozet" })
                Assert.Equal(e.GetProperty(alan).ToString(), g.GetProperty(alan).ToString());
        }
        Assert.All(guncellemeler, g =>
        {
            Assert.Equal("EMAR-LAPTOP", g.Str("cihaz"));
            Assert.False(string.IsNullOrEmpty(g.Str("kullanici")));
            Assert.True(g.Bool("gecmiseDonuk"));
        });
        Assert.False(guncellemeler[0].Bool("geriAlinabilir"));   // kayıt sonra yeniden değişti
        Assert.True(guncellemeler[1].Bool("geriAlinabilir"));

        var csv = Csv(await c.GetByteArrayAsync("/api/disaaktar/gecmis.csv"));
        Assert.Equal(new[] { "Zaman", "Rol", "Tür", "Eylem", "Özet", "Geri alındı", "Kişi", "Cihaz" }, csv[0]);
        Assert.All(csv, s => Assert.Equal(8, s.Length));
        Assert.Equal("EMAR-LAPTOP", csv[1][7]);   // en yeni önce
        Assert.Equal(guncellemeler[1].Str("kullanici"), csv[1][6]);
    }

    // ------------------------------------------------------------ B × D × F: çek CSV'si ve ay paketi

    /// <summary>BOM'lu, ';' ayraçlı CSV; tırnaklı hücre (içinde ';' ya da çift tırnak olabilir) tek hücre okunur.</summary>
    private static List<string[]> Csv(byte[] b)
    {
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, b[..3]);
        return Encoding.UTF8.GetString(b[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Select(Hucreler).ToList();
    }

    private static string[] Hucreler(string satir)
    {
        var l = new List<string>();
        var h = new StringBuilder();
        var tirnakta = false;
        for (var i = 0; i < satir.Length; i++)
        {
            var ch = satir[i];
            if (tirnakta)
            {
                if (ch == '"' && i + 1 < satir.Length && satir[i + 1] == '"') { h.Append('"'); i++; }
                else if (ch == '"') tirnakta = false;
                else h.Append(ch);
            }
            else if (ch == '"') tirnakta = true;
            else if (ch == ';') { l.Add(h.ToString()); h.Clear(); }
            else h.Append(ch);
        }
        l.Add(h.ToString());
        return l.ToArray();
    }

    /// <summary>
    /// Bulgu: Çekler sayfasının tür/konum süzgeci "Excel'e aktar"a gitmiyordu (dosyada süzülmemiş tüm
    /// evrak) ve CSV'de tür, konum, ciro edilen cari sütunları yoktu; toplam satırı senedi "çek" sayıyordu.
    /// </summary>
    [Fact]
    public async Task Cek_csvsi_tur_ve_konum_suzgecini_uygular_evrak_sutunlari_sonda()
    {
        var c = await HazirlaAsync(new DateOnly(2026, 9, 24));
        foreach (var g in new[]
                 {
                     CekGovdesi("Portfoyde", null, konum: "Elde", tutar: 100m, kisi: "Alıcı Çek", vade: "2026-10-01"),
                     CekGovdesi("Portfoyde", null, konum: "Teminatta", tutar: 200m, tur: "Senet", kisi: "Alıcı Senet", vade: "2026-10-02"),
                     CekGovdesi("Portfoyde", null, konum: "Icrada", tutar: 300m, tur: "Senet", yon: "Verilen", kisi: "Satıcı Senet", vade: "2026-10-03"),
                     CekGovdesi("CiroEdildi", "2026-09-05", ciro: "Tedarikçi A", tutar: 400m, kisi: "Ciro Çek", vade: "2026-10-04"),
                 })
            await Basarili(await c.PostAsJsonAsync("/api/cekler", g));

        var tum = Csv(await c.GetByteArrayAsync("/api/disaaktar/cekler.csv"));
        Assert.Equal(new[] { "Tür", "Konum", "Ciro edilen cari" }, tum[0][11..]);
        Assert.Equal("Yön", tum[0][0]);
        Assert.All(tum, s => Assert.Equal(14, s.Length));
        string[] Satir(List<string[]> l, string kisi) => l.Single(s => s[3] == kisi);
        Assert.Equal(new[] { "Senet", "Teminatta", "" }, Satir(tum, "Alıcı Senet")[11..]);
        Assert.Equal(new[] { "Senet", "", "" }, Satir(tum, "Satıcı Senet")[11..]);   // verilen evrakta konum yok
        Assert.Equal(new[] { "Çek", "Elde", "Tedarikçi A" }, Satir(tum, "Ciro Çek")[11..]);
        Assert.Equal("Toplam alınan (2 çek, 1 senet)", tum[^2][0]);
        Assert.Equal("Toplam verilen (1 senet)", tum[^1][0]);
        Assert.Equal("300,00", tum[^1][4]);

        var senet = Csv(await c.GetByteArrayAsync("/api/disaaktar/cekler.csv?tur=Senet"));
        Assert.Equal(new[] { "Alıcı Senet", "Satıcı Senet" }, senet.Skip(1).SkipLast(2).Select(s => s[3]).Order());
        Assert.Equal("Toplam alınan (1 senet)", senet[^2][0]);

        // Konum yalnız alınan evrakta aranır (Çekler sayfasındaki gibi): verilen evrak listeye girmez.
        var elde = Csv(await c.GetByteArrayAsync("/api/disaaktar/cekler.csv?konum=Elde"));
        Assert.Equal(new[] { "Alıcı Çek", "Ciro Çek" }, elde.Skip(1).SkipLast(2).Select(s => s[3]).Order());
        Assert.Equal("Toplam verilen (0 çek)", elde[^1][0]);
        var ikisi = Csv(await c.GetByteArrayAsync("/api/disaaktar/cekler.csv?tur=senet&konum=Teminatta"));
        Assert.Equal("Alıcı Senet", Assert.Single(ikisi.Skip(1).SkipLast(2))[3]);

        foreach (var (url, mesaj) in new[]
                 {
                     ("/api/disaaktar/cekler.csv?tur=Bono", "Geçersiz evrak türü (Cek ya da Senet)."),
                     ("/api/disaaktar/cekler.csv?konum=Kasada", "Geçersiz evrak konumu."),
                 })
        {
            var r = await c.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(mesaj, await HataMetni(r));
        }
    }

    /// <summary>
    /// Bulgu: ay paketi (B) muhasebeciye gidecek belge bilgisini (F: belge türü/no, fatura bekleniyor,
    /// ek sayısı) içermiyordu; ay sonunda muhasebeci listesi ayrıca indirilmek zorundaydı.
    /// </summary>
    [Fact]
    public async Task Ay_paketi_muhasebeci_listesini_de_icerir()
    {
        var c = await HazirlaAsync(new DateOnly(2026, 9, 24));
        var id = await IslemEkle(c, new DateOnly(2026, 8, 4), 300m, "MEZAT", "Cari", "Market");
        await Basarili(await c.PutAsJsonAsync($"/api/islemler/{id}/belge", new { belgeTuru = "EFatura", belgeNo = "F-77", faturaBekleniyor = false }));
        await IslemEkle(c, new DateOnly(2026, 9, 4), 55m);

        var r = await c.GetAsync("/api/disaaktar/ay-paketi.zip?yil=2026&ay=8");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var zip = new ZipArchive(new MemoryStream(await r.Content.ReadAsByteArrayAsync()));
        var giris = zip.GetEntry("muhasebeci-2026-08.csv");
        Assert.NotNull(giris);
        using var s = giris!.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        Assert.Equal(await c.GetByteArrayAsync("/api/disaaktar/muhasebeci.csv?yil=2026&ay=8"), ms.ToArray());
        Assert.Contains("F-77", Encoding.UTF8.GetString(ms.ToArray()));
    }
}
