using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kasa.Api.Servisler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>CSV çıktısını ayrıştıran test yardımcısı (RFC 4180: tırnak içinde ; "" ve satır sonu).</summary>
public static class CsvOkuyucu
{
    public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static List<string[]> Oku(string metin)
    {
        var satirlar = new List<string[]>();
        var hucreler = new List<string>();
        var sb = new StringBuilder();
        bool tirnak = false;
        for (int i = 0; i < metin.Length; i++)
        {
            var c = metin[i];
            if (tirnak)
            {
                if (c == '"' && i + 1 < metin.Length && metin[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') tirnak = false;
                else sb.Append(c);
            }
            else if (c == '"') tirnak = true;
            else if (c == ';') { hucreler.Add(sb.ToString()); sb.Clear(); }
            else if (c == '\r' && i + 1 < metin.Length && metin[i + 1] == '\n')
            {
                hucreler.Add(sb.ToString()); sb.Clear();
                satirlar.Add(hucreler.ToArray()); hucreler.Clear();
                i++;
            }
            else sb.Append(c);
        }
        if (sb.Length > 0 || hucreler.Count > 0) { hucreler.Add(sb.ToString()); satirlar.Add(hucreler.ToArray()); }
        return satirlar;
    }

    /// <summary>Türkçe Excel'in okuyacağı gibi: virgül ondalık, binlik ayırıcı yok.</summary>
    public static decimal Sayi(string s)
    {
        Assert.DoesNotContain(".", s);
        return decimal.Parse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Tr);
    }

    public static DateOnly Tarih(string s) => DateOnly.ParseExact(s, "dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>İndirir; BOM, içerik tipi ve dosya adını doğrular, satırları döner.</summary>
    public static async Task<(List<string[]> Satirlar, string DosyaAdi)> IndirAsync(HttpClient c, string yol)
    {
        var r = await c.GetAsync(yol);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/csv", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", r.Content.Headers.ContentType?.CharSet);
        var cd = r.Content.Headers.ContentDisposition;
        Assert.NotNull(cd);
        Assert.Equal("attachment", cd!.DispositionType);
        var bayt = await r.Content.ReadAsByteArrayAsync();
        Assert.True(bayt.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()), "UTF-8 BOM yok");
        var metin = Encoding.UTF8.GetString(bayt, 3, bayt.Length - 3);
        Assert.EndsWith("\r\n", metin);
        return (Oku(metin), cd.FileName!.Trim('"'));
    }
}

/// <summary>Excel'e aktar: CSV biçimi (birim).</summary>
public class CsvYaziciTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+90 555", "'+90 555")]
    [InlineData("-5", "'-5")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tgizli", "'\tgizli")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("iki\nsatır", "\"iki\nsatır\"")]
    [InlineData(" boşluk", "\" boşluk\"")]
    [InlineData("Şeker Ünlü", "Şeker Ünlü")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Metin_hucresi_korunur_ve_tirnaklanir(string? girdi, string beklenen)
        => Assert.Equal(beklenen, CsvYazici.Metin(girdi));

    [Theory]
    [InlineData("1234567.5", "1234567,50")]
    [InlineData("-1019375", "-1019375,00")]
    [InlineData("0", "0,00")]
    [InlineData("0.01", "0,01")]
    public void Sayi_virgul_ondalikli_binlik_ayiricisiz(string girdi, string beklenen)
        => Assert.Equal(beklenen, CsvYazici.Sayi(decimal.Parse(girdi, CultureInfo.InvariantCulture)));

    [Fact]
    public void Tarih_gun_ay_yil()
        => Assert.Equal("05.09.2026", CsvYazici.Tarih(new DateOnly(2026, 9, 5)));

    [Theory]
    [InlineData("PERAKENDE Şube", "perakende-sube")]
    [InlineData("ÇİĞ ÖĞÜŞ ıi", "cig-ogus-ii")]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("Ortak", "ortak")]
    [InlineData("  ", "")]
    public void Dosya_adi_parcasi_ascii(string girdi, string beklenen)
        => Assert.Equal(beklenen, CsvYazici.DosyaAdiParcasi(girdi));

    [Fact]
    public void Islem_dosya_adi_filtreden_turer()
    {
        Assert.Equal("kasa-islemler-2026-09.csv", CsvRaporlari.IslemDosyaAdi(new(2026, 9, 1), new(2026, 9, 30), null, null));
        Assert.Equal("kasa-islemler-2026-02.csv", CsvRaporlari.IslemDosyaAdi(new(2026, 2, 1), new(2026, 2, 28), null, null));
        Assert.Equal("kasa-islemler-2026-09-07_2026-09-13-mezat.csv", CsvRaporlari.IslemDosyaAdi(new(2026, 9, 7), new(2026, 9, 13), "MEZAT", null));
        Assert.Equal("kasa-islemler-2026-09-07.csv", CsvRaporlari.IslemDosyaAdi(new(2026, 9, 7), new(2026, 9, 7), null, " "));
        Assert.Equal("kasa-islemler-tumu-ortak-sut.csv", CsvRaporlari.IslemDosyaAdi(null, null, "Ortak", "Süt"));
        Assert.Equal("kasa-islemler-2026-09-01-sonrasi.csv", CsvRaporlari.IslemDosyaAdi(new(2026, 9, 1), null, null, null));
        Assert.Equal("kasa-islemler-2026-09-30-oncesi.csv", CsvRaporlari.IslemDosyaAdi(null, new(2026, 9, 30), null, null));
    }

    [Fact]
    public void Baytlar_bom_ile_baslar_satirlar_crlf()
    {
        var b = new CsvYazici().Baslik("Tarih", "Tutar").Satir("01.09.2026", "1,00").Baytlar();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, b[..3]);
        Assert.Equal("Tarih;Tutar\r\n01.09.2026;1,00\r\n", Encoding.UTF8.GetString(b, 3, b.Length - 3));
    }
}

/// <summary>Excel'e aktar uç noktaları: rakamlar JSON uç noktalarıyla birebir aynı.</summary>
public class DisaAktarmaApiTests : IClassFixture<DisaAktarmaApiTests.Factory>
{
    /// <summary>Bugün = 24 Eylül 2026 Perşembe (İstanbul).</summary>
    public class Factory : KasaWebFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(new Saat())));
        }
        private sealed class Saat : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
        }

        /// <summary>Sınıftaki testler aynı veriyi paylaşır; bir kez tohumlanır.</summary>
        public bool Tohumlandi;
        public readonly SemaphoreSlim Kilit = new(1, 1);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private record DonemY(DateOnly Start, DateOnly End);
    private record KanalHaftalikY(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir,
        decimal CekGelen = 0m, decimal CekGiden = 0m);
    private record HaftalikY(DonemY Donem, List<KanalHaftalikY> Kanallar, decimal ToplamGelen, decimal ToplamGiden, decimal KasaSonucu, decimal KasaDevir,
        decimal ToplamCekGelen = 0m, decimal ToplamCekGiden = 0m);
    private record KanalAylikY(string Kanal, decimal Gelen, decimal CariGiden, decimal SabitGider, decimal KrediKarti, decimal OrtakPay, decimal AySonucu,
        decimal CekGelen = 0m, decimal CekGiden = 0m);
    private record AylikY(int Yil, int Ay, List<KanalAylikY> Kanallar);
    private record IslemY(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, string Tip, string? Not, int? KrediKartiId);
    private record IdY(int Id);

    private readonly Factory _f;
    public DisaAktarmaApiTests(Factory f) => _f = f;

    /// <summary>Temmuz–Eylül 2026: gelen, cari, sabit gider, kartsız ve kartlı K.K, kart ödemesi.</summary>
    private async Task<HttpClient> TohumlaAsync()
    {
        var c = await _f.EditorClientAsync();
        await _f.Kilit.WaitAsync();
        try
        {
            if (_f.Tohumlandi) return c;
            await KasaWebFactory.TakipBaslangiciAyarla(c, new DateOnly(2026, 7, 1), kasaAcilis: 10_000m);
            async Task Gelen(string d, string k, decimal t)
                => (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = d, kanal = k, tutarTl = t })).EnsureSuccessStatusCode();
            async Task<int> Post(string yol, object govde)
            {
                var r = await c.PostAsJsonAsync(yol, govde, Json);
                r.EnsureSuccessStatusCode();
                return (await r.Content.ReadFromJsonAsync<IdY>(Json))!.Id;
            }
            await Gelen("2026-07-06", "MEZAT", 5_000m);
            await Gelen("2026-07-13", "PERAKENDE", 3_000.50m);
            await Gelen("2026-08-03", "TOPTAN", 2_000m);
            await Gelen("2026-09-07", "MEZAT", 1_234.56m);
            await Post("/api/giderkalemleri", new { ad = "Kira", aktif = true });
            await Post("/api/cariler", new { ad = "=1+2", aktif = true });
            var kart = await Post("/api/kredikartlari", new { ad = "Bonus; Gold", kesimTarihi = "2026-08-05", sonOdemeTarihi = "2026-08-15", limit = 50_000m, borc = 0m });
            await Post("/api/islemler", new { tarih = "2026-07-07", cari = "Market", tutarTl = 1_000m, kanal = "MEZAT", tip = "Cari", not = "=HYPERLINK(\"http://kotu\")" });
            await Post("/api/islemler", new { tarih = "2026-07-15", cari = "Kira", tutarTl = 2_500m, kanal = "Ortak", tip = "SabitGider", not = "a;b \"c\"" });
            await Post("/api/islemler", new { tarih = "2026-07-20", cari = "A", tutarTl = 700m, kanal = "PERAKENDE", tip = "KrediKarti" });   // kartsız (eski) K.K
            await Post("/api/islemler", new { tarih = "2026-08-10", cari = "B", tutarTl = 400.25m, kanal = "TOPTAN", tip = "Cari", krediKartiId = kart });
            await Post("/api/islemler", new { tarih = "2026-09-02", cari = "=1+2", tutarTl = 99.99m, kanal = "MEZAT", tip = "Cari" });
            await Post("/api/islemler", new { tarih = "2026-09-21", cari = "X", tutarTl = 150m, kanal = "TOPTAN", tip = "Cari" });
            await Post("/api/kartodemeler", new { krediKartiId = kart, tarih = "2026-09-10", tutar = 300m });
            // Çekler: tahsil edilen alınan (MEZAT) ve ödenen Ortak verilen; ikisi de CSV'de ayrı sütunda.
            await Post("/api/cekler", new { yon = "Alinan", cekNo = "1", banka = "Ziraat", kisi = "Müşteri", tutar = 800m,
                duzenlemeTarihi = "2026-08-01", vadeTarihi = "2026-08-12", kanal = "MEZAT", durum = "TahsilEdildi", islemTarihi = "2026-08-12" });
            await Post("/api/cekler", new { yon = "Verilen", cekNo = "2", banka = "Ziraat", kisi = "Tedarikçi", tutar = 250.50m,
                duzenlemeTarihi = "2026-09-01", vadeTarihi = "2026-09-15", kanal = "Ortak", durum = "Odendi", islemTarihi = "2026-09-15" });
            _f.Tohumlandi = true;
            return c;
        }
        finally { _f.Kilit.Release(); }
    }

    [Fact]
    public async Task Islemler_csv_liste_ile_ayni_satirlar_turkce_tip_kart_adi_ve_toplam()
    {
        var c = await TohumlaAsync();
        var json = (await c.GetFromJsonAsync<List<IslemY>>("/api/islemler?baslangic=2026-07-01&bitis=2026-09-30", Json))!;
        var (satirlar, ad) = await CsvOkuyucu.IndirAsync(c, "/api/disaaktar/islemler.csv?baslangic=2026-07-01&bitis=2026-09-30");

        Assert.Equal("kasa-islemler-2026-07-01_2026-09-30.csv", ad);
        Assert.Equal(["Tarih", "Cari/Kalem", "Kanal", "Tip", "Kart", "Tutar", "Not"], satirlar[0]);
        var govde = satirlar.Skip(1).SkipLast(1).ToList();
        Assert.Equal(json.Count, govde.Count);
        for (int i = 0; i < json.Count; i++)
        {
            Assert.Equal(json[i].Tarih, CsvOkuyucu.Tarih(govde[i][0]));
            Assert.Equal(json[i].TutarTl, CsvOkuyucu.Sayi(govde[i][5]));
            Assert.Equal(json[i].Kanal, govde[i][2]);
        }

        var toplam = satirlar[^1];
        Assert.Equal($"Toplam ({json.Count} işlem)", toplam[0]);
        Assert.Equal(json.Sum(i => i.TutarTl), CsvOkuyucu.Sayi(toplam[5]));
        Assert.Equal(4_850.24m, CsvOkuyucu.Sayi(toplam[5]));

        var satir = (string cari) => govde.Single(s => s[1] == cari);
        Assert.Equal("Sabit gider", satir("Kira")[3]);
        Assert.Equal("a;b \"c\"", satir("Kira")[6]);               // ; ve " tırnakla korunur
        Assert.Equal("Kredi kartı", satir("A")[3]);                // kartsız eski K.K
        Assert.Equal("", satir("A")[4]);
        Assert.Equal("Kredi kartı", satir("B")[3]);                // kartlı: tip K.K, kart adı dolu
        Assert.Equal("Bonus; Gold", satir("B")[4]);
        Assert.Equal("Cari", satir("Market")[3]);
        // Formül enjeksiyonu: = ile başlayan metin hücresi ' ile başlar.
        Assert.Equal("'=HYPERLINK(\"http://kotu\")", satir("Market")[6]);
        Assert.Equal("MEZAT", satir("'=1+2")[2]);
    }

    [Fact]
    public async Task Islemler_csv_kanal_ve_cari_filtresi_listeyle_ayni()
    {
        var c = await TohumlaAsync();
        foreach (var sorgu in new[] { "kanal=MEZAT", "cari=market", "baslangic=2026-09-01&bitis=2026-09-30&kanal=TOPTAN", "kanal=Ortak" })
        {
            var json = (await c.GetFromJsonAsync<List<IslemY>>("/api/islemler?" + sorgu, Json))!;
            var (satirlar, _) = await CsvOkuyucu.IndirAsync(c, "/api/disaaktar/islemler.csv?" + sorgu);
            var govde = satirlar.Skip(1).SkipLast(1).ToList();
            Assert.Equal(json.Select(i => (i.Tarih, i.TutarTl)), govde.Select(s => (CsvOkuyucu.Tarih(s[0]), CsvOkuyucu.Sayi(s[5]))));
        }
        var (_, ad) = await CsvOkuyucu.IndirAsync(c, "/api/disaaktar/islemler.csv?baslangic=2026-09-01&bitis=2026-09-30");
        Assert.Equal("kasa-islemler-2026-09.csv", ad);
    }

    [Fact]
    public async Task Haftalik_csv_json_raporla_ayni_rakamlar()
    {
        var c = await TohumlaAsync();
        var json = (await c.GetFromJsonAsync<List<HaftalikY>>("/api/rapor/haftalik", Json))!;
        var (satirlar, ad) = await CsvOkuyucu.IndirAsync(c, "/api/disaaktar/haftalik.csv");

        Assert.Equal("kasa-haftalik-2026-09-24.csv", ad);
        Assert.Equal(["Dönem başı", "Dönem sonu", "Kanal", "Gelen", "Giden", "Sonuç", "Devir", "Çek tahsilat", "Çek ödeme"], satirlar[0]);
        var govde = satirlar.Skip(1).ToList();
        Assert.Equal(json.Sum(o => o.Kanallar.Count + 1), govde.Count);

        int n = 0;
        foreach (var o in json)
        {
            foreach (var k in o.Kanallar.Select(k => (k.Kanal, k.Gelen, k.Giden, k.Sonuc, k.Devir, k.CekGelen, k.CekGiden))
                         .Append((CsvRaporlari.KasaSatiri, o.ToplamGelen, o.ToplamGiden, o.KasaSonucu, o.KasaDevir, o.ToplamCekGelen, o.ToplamCekGiden)))
            {
                var s = govde[n++];
                Assert.Equal(o.Donem.Start, CsvOkuyucu.Tarih(s[0]));
                Assert.Equal(o.Donem.End, CsvOkuyucu.Tarih(s[1]));
                Assert.Equal(k.Item1, s[2]);
                Assert.Equal((k.Item2, k.Item3, k.Item4, k.Item5),
                    (CsvOkuyucu.Sayi(s[3]), CsvOkuyucu.Sayi(s[4]), CsvOkuyucu.Sayi(s[5]), CsvOkuyucu.Sayi(s[6])));
                Assert.Equal((k.Item6, k.Item7), (CsvOkuyucu.Sayi(s[7]), CsvOkuyucu.Sayi(s[8])));
            }
        }
        // Çek sütunları dolu (tohumdaki iki çek).
        Assert.Equal(800m, json.Sum(o => o.ToplamCekGelen));
        Assert.Equal(250.50m, json.Sum(o => o.ToplamCekGiden));
        // Son kasa devri paneldeki güncel kasadır.
        var panel = await c.GetFromJsonAsync<JsonElement>("/api/rapor/panel");
        Assert.Equal(panel.GetProperty("guncelKasa").GetDecimal(), CsvOkuyucu.Sayi(govde[^1][6]));
    }

    [Theory]
    [InlineData(2026, 7)]
    [InlineData(2026, 8)]
    [InlineData(2026, 9)]
    public async Task Aylik_csv_json_raporla_ayni_rakamlar_ve_toplam(int yil, int ay)
    {
        var c = await TohumlaAsync();
        var json = (await c.GetFromJsonAsync<AylikY>($"/api/rapor/aylik?yil={yil}&ay={ay}", Json))!;
        var (satirlar, ad) = await CsvOkuyucu.IndirAsync(c, $"/api/disaaktar/aylik.csv?yil={yil}&ay={ay}");

        Assert.Equal($"kasa-aylik-{yil}-{ay:D2}.csv", ad);
        Assert.Equal(["Kanal", "Gelen", "Cari giden", "Sabit gider", "Kredi kartı", "Ortak pay", "Ay sonucu", "Çek tahsilat", "Çek ödeme"], satirlar[0]);
        var govde = satirlar.Skip(1).SkipLast(1).ToList();
        Assert.Equal(json.Kanallar.Count, govde.Count);
        static decimal[] Rakam(string[] s) => s.Skip(1).Select(CsvOkuyucu.Sayi).ToArray();
        for (int i = 0; i < govde.Count; i++)
        {
            var k = json.Kanallar[i];
            Assert.Equal(k.Kanal, govde[i][0]);
            Assert.Equal([k.Gelen, k.CariGiden, k.SabitGider, k.KrediKarti, k.OrtakPay, k.AySonucu, k.CekGelen, k.CekGiden], Rakam(govde[i]));
        }
        Assert.Equal("Toplam", satirlar[^1][0]);
        var l = json.Kanallar;
        Assert.Equal([l.Sum(k => k.Gelen), l.Sum(k => k.CariGiden), l.Sum(k => k.SabitGider), l.Sum(k => k.KrediKarti),
            l.Sum(k => k.OrtakPay), l.Sum(k => k.AySonucu), l.Sum(k => k.CekGelen), l.Sum(k => k.CekGiden)], Rakam(satirlar[^1]));
    }

    [Fact]
    public async Task Aylik_csv_gecersiz_ay_400()
    {
        var c = await TohumlaAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/disaaktar/aylik.csv?yil=2026&ay=13")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/disaaktar/aylik.csv?yil=1999&ay=1")).StatusCode);
    }

    [Fact]
    public async Task Izleyici_indirebilir_oturumsuz_indiremez()
    {
        var editor = await TohumlaAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle123" })).EnsureSuccessStatusCode();
        var izleyici = _f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izle123" })).EnsureSuccessStatusCode();
        foreach (var yol in new[] { "/api/disaaktar/islemler.csv", "/api/disaaktar/haftalik.csv", "/api/disaaktar/aylik.csv?yil=2026&ay=9" })
        {
            await CsvOkuyucu.IndirAsync(izleyici, yol);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _f.CreateClient().GetAsync(yol)).StatusCode);
        }
    }
}
