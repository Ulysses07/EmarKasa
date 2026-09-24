using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

// Paket A — Panel durum kartları, nakit tahmini, bugün yapılacaklar, geçmiş "yeni" işaretleri,
// kart limit uyarısı, yerel depo ve bildirim planlayıcısı.

internal static class PaketAOrnek
{
    public static readonly DateOnly Bugun = new(2026, 9, 24);   // Perşembe
    public static SabitSaat Saat(int saat = 10) => new(Bugun.ToDateTime(new TimeOnly(saat, 0)));

    public static PanelDto Panel(decimal kasa = 58_900m) => new(kasa, new List<KanalBakiyeDto> { new("MEZAT", kasa) }, 0m, 0m);

    public static CekDto Cek(int id, CekYonu yon, decimal tutar, DateOnly vade, string kisi = "Ahmet")
        => new(id, yon, null, null, kisi, tutar, new DateOnly(2026, 8, 1), vade, "MEZAT", CekDurumu.Portfoyde, null, null);

    public static CekOzetDto CekOzeti(IEnumerable<CekDto>? yaklasan = null, IEnumerable<CekDto>? gecen = null,
        decimal alinan = 0m, int alinanAdet = 0, decimal verilen = 0m, int verilenAdet = 0)
        => new(alinan, alinanAdet, verilen, verilenAdet, 30, (yaklasan ?? []).ToList(), (gecen ?? []).ToList());

    public static KrediKartiDto Kart(int id, string ad, int kesim, int sonOdeme, decimal limit, decimal guncel, decimal ekstre)
        => new(id, ad, new DateOnly(2026, 1, kesim), new DateOnly(2026, 1, sonOdeme), limit, 0m, guncel, 0m, 0m, 0m, ekstre);

    public static KasaSayimDto Sayim(int id, DateOnly tarih, decimal fark)
        => new(id, tarih, 1000m + fark, 1000m, fark, 1000m, null, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc));

    public static DegisiklikDto Degisiklik(int id, bool gecmiseDonuk = false, DateTime? zaman = null, string tur = "İşlem")
        => new(id, zaman ?? new DateTime(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc), "editor", tur, id, "Eklendi", $"{tur} eklendi #{id}",
            null, "{}", false, null, false, gecmiseDonuk);
}

public class YerelDepoTests : IDisposable
{
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "emarkasa-test-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_klasor, true); } catch { } }

    [Fact]
    public void Dosya_depo_yazar_okur_siler_ve_yeni_ornekte_kalir()
    {
        var d = new DosyaYerelDepo(_klasor);
        Assert.Null(d.Oku("tahmin.gun"));
        d.YazInt("tahmin.gun", 60);
        Assert.Equal(60, new DosyaYerelDepo(_klasor).OkuInt("tahmin.gun"));
        d.Yaz("tahmin.gun", null);
        Assert.Null(d.Oku("tahmin.gun"));
        d.Yaz("yok", null);   // olmayanı silmek hata değil
    }

    [Fact]
    public void Dosya_adi_guvenli_klasor_disina_cikmaz()
    {
        var d = new DosyaYerelDepo(_klasor);
        foreach (var anahtar in new[] { "../../etc/passwd", "a/b\\c", "..", "", "çek:1" })
        {
            var yol = Path.GetFullPath(d.Yol(anahtar));
            Assert.Equal(Path.GetFullPath(_klasor), Path.GetDirectoryName(yol));
            d.Yaz(anahtar, "x");
            Assert.Equal("x", d.Oku(anahtar));
        }
    }

    [Fact]
    public void Dosya_depo_yazamazsa_sessizce_gecer()
    {
        var dosya = Path.Combine(Path.GetTempPath(), "emarkasa-dosya-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(dosya, "klasör değil");
        try
        {
            var d = new DosyaYerelDepo(Path.Combine(dosya, "alt"));   // klasör oluşturulamaz
            d.Yaz("a", "1");
            Assert.Null(d.Oku("a"));
        }
        finally { File.Delete(dosya); }
    }

    [Fact]
    public void Uzantilar_bool_tarih_ve_id_listesi()
    {
        var d = new BellekYerelDepo();
        Assert.True(d.OkuBool("b", true));
        Assert.False(d.OkuBool("b", false));
        d.YazBool("b", false);
        Assert.False(d.OkuBool("b", true));
        d.Yaz("b", "bozuk");
        Assert.True(d.OkuBool("b", true));

        d.YazTarih("t", new DateOnly(2026, 9, 21));
        Assert.Equal("2026-09-21", d.Oku("t"));
        Assert.Equal(new DateOnly(2026, 9, 21), d.OkuTarih("t"));
        d.Yaz("t", "21.09.2026");
        Assert.Null(d.OkuTarih("t"));

        d.YazIdler("h", [7, 3, 7, 12]);
        Assert.Equal("3,7,12", d.Oku("h"));
        d.Yaz("h", "3, x,-1, 9");
        Assert.Equal([3, 9], d.OkuIdler("h"));
        d.YazIdler("h", []);
        Assert.Null(d.Oku("h"));
        Assert.Null(d.OkuInt("yok"));
    }
}

public class PanelMetinTests
{
    [Theory]
    [InlineData(1, "i")] [InlineData(2, "si")] [InlineData(3, "ü")] [InlineData(4, "ü")] [InlineData(5, "i")]
    [InlineData(6, "sı")] [InlineData(7, "si")] [InlineData(8, "i")] [InlineData(9, "u")] [InlineData(10, "u")]
    [InlineData(12, "si")] [InlineData(20, "si")] [InlineData(30, "u")] [InlineData(40, "ı")] [InlineData(50, "si")]
    [InlineData(60, "ı")] [InlineData(70, "i")] [InlineData(80, "i")] [InlineData(85, "i")] [InlineData(90, "ı")]
    [InlineData(100, "ü")] [InlineData(112, "si")] [InlineData(200, "ü")] [InlineData(1000, "i")] [InlineData(0, "ı")]
    public void Sayi_eki(int n, string ek) => Assert.Equal(ek, PanelMetin.SayiEki(n));

    [Fact]
    public void Tutar_ve_tarih_metinleri()
    {
        Assert.Equal("−12.500,00 ₺", PanelMetin.Tutar(-12_500m));
        Assert.Equal("12.500,50 ₺", PanelMetin.Tutar(12_500.5m));
        Assert.Equal("+0,00 ₺", PanelMetin.IsaretliTutar(0m));
        Assert.Equal("−3,00 ₺", PanelMetin.IsaretliTutar(-3m));
        var b = PaketAOrnek.Bugun;
        Assert.Equal("14 Kasım", PanelMetin.Gun(new DateOnly(2026, 11, 14), b));
        Assert.Equal("5 Ocak 2027", PanelMetin.Gun(new DateOnly(2027, 1, 5), b));
        Assert.Equal("bugün", PanelMetin.GoreliGun(b, b));
        Assert.Equal("yarın", PanelMetin.GoreliGun(b.AddDays(1), b));
        Assert.Equal("dün", PanelMetin.GoreliGun(b.AddDays(-1), b));
        Assert.Equal("14–20 Eylül", PanelMetin.Aralik(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20)));
        Assert.Equal("28 Eylül – 4 Ekim", PanelMetin.Aralik(new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 4)));
    }

    [Fact]
    public void Gorece_zaman()
    {
        var tz = TimeZoneInfo.Utc;
        var simdi = new DateTimeOffset(2026, 9, 24, 14, 0, 0, TimeSpan.Zero);
        DateTime Once(TimeSpan t) => (simdi - t).UtcDateTime;
        Assert.Equal("az önce", GoreceZaman.Metin(Once(TimeSpan.FromSeconds(20)), simdi, tz));
        Assert.Equal("az önce", GoreceZaman.Metin(Once(TimeSpan.FromMinutes(-5)), simdi, tz));   // saat farkı
        Assert.Equal("5 dakika önce", GoreceZaman.Metin(Once(TimeSpan.FromMinutes(5)), simdi, tz));
        Assert.Equal("2 saat önce", GoreceZaman.Metin(Once(TimeSpan.FromHours(2.5)), simdi, tz));
        Assert.Equal("dün", GoreceZaman.Metin(Once(TimeSpan.FromHours(15)), simdi, tz));   // dün 23:00
        Assert.Equal("dün", GoreceZaman.Metin(Once(TimeSpan.FromHours(30)), simdi, tz));
        Assert.Equal("3 gün önce", GoreceZaman.Metin(Once(TimeSpan.FromDays(3)), simdi, tz));
        Assert.Equal("10 Eylül 2026", GoreceZaman.Metin(Once(TimeSpan.FromDays(14)), simdi, tz));
        // İstanbul saatiyle gün: 23:30 UTC = ertesi gün 02:30 → "dün" değil saat.
        var ist = TimeZoneInfo.CreateCustomTimeZone("TR", TimeSpan.FromHours(3), "TR", "TR");
        var sabah = new DateTimeOffset(2026, 9, 25, 5, 0, 0, TimeSpan.Zero);   // 08:00 İstanbul
        Assert.Equal("dün", GoreceZaman.Metin(new DateTime(2026, 9, 24, 20, 30, 0, DateTimeKind.Utc), sabah, ist));   // 23:30 İst
        Assert.Equal("5 saat önce", GoreceZaman.Metin(new DateTime(2026, 9, 24, 23, 30, 0, DateTimeKind.Utc), sabah, ist));   // 02:30 İst
    }
}

public class KartLimitTests
{
    [Theory]
    [InlineData(0, 10_000, false, null)]
    [InlineData(7_999.99, 10_000, false, null)]
    [InlineData(8_000, 10_000, true, "Limitin %80'i kullanıldı")]
    [InlineData(8_500, 10_000, true, "Limitin %85'i kullanıldı")]
    [InlineData(9_000, 10_000, true, "Limitin %90'ı kullanıldı")]
    [InlineData(10_000, 10_000, true, "Limitin %100'ü kullanıldı")]
    [InlineData(11_250, 10_000, true, "Limit aşıldı (%112)")]
    [InlineData(5_000, 0, false, null)]      // limit girilmemiş
    [InlineData(-2_000, 10_000, false, null)] // alacaklı kart
    public void Esik_yuzde_80(double borc, double limit, bool uyari, string? metin)
    {
        Assert.Equal(uyari, KartLimit.UyariVar((decimal)borc, (decimal)limit));
        Assert.Equal(metin, KartLimit.Metin((decimal)borc, (decimal)limit));
        var g = new KrediKartiGorunum(PaketAOrnek.Kart(1, "Bonus", 5, 15, (decimal)limit, (decimal)borc, 0m), new DateTime(2026, 9, 24));
        Assert.Equal(uyari, g.LimitUyarisi);
        Assert.Equal(metin, g.LimitUyariMetni);
    }
}

public class YapilacakListesiTests
{
    private static readonly DateOnly B = PaketAOrnek.Bugun;

    [Fact]
    public void Kaynak_yoksa_ya_da_bossa_liste_bos_sayim_haric()
    {
        Assert.Empty(YapilacakListesi.Olustur(null, null, null, null, null, B));
        var l = YapilacakListesi.Olustur([], PaketAOrnek.CekOzeti(), [], [], [PaketAOrnek.Sayim(1, B.AddDays(-7), 0m)], B);
        Assert.Empty(l);   // 7 gün önceki sayım henüz eski değil
    }

    [Fact]
    public void Tekrarlayan_giderler_tek_satirda_bekleyen_kartina_gider()
    {
        var bekleyen = new List<BekleyenGiderDto>
        {
            new(1, "Kira", "Ortak", 15_000m, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5)),
            new(2, "SGK", "Ortak", 4_000m, new DateOnly(2026, 9, 1), B),
        };
        var s = Assert.Single(YapilacakListesi.Olustur(bekleyen, null, null, null, null, B));
        Assert.Equal(YapilacakTuru.TekrarlayanGider, s.Tur);
        Assert.Equal("2 tekrarlayan gider onay bekliyor", s.Baslik);
        Assert.Equal("Kira, SGK · toplam 19.000,00 ₺", s.Aciklama);
        Assert.Equal(YapilacakListesi.BekleyenKarti, s.Hedef);
        Assert.Equal("Göster", s.DugmeMetni);
        Assert.True(s.Acil);   // vadesi geçmiş olan var
    }

    [Fact]
    public void Cekler_gecen_bugun_ve_uc_gun_icindekiler_ayri_satirda()
    {
        var ozet = PaketAOrnek.CekOzeti(
            yaklasan:
            [
                PaketAOrnek.Cek(1, CekYonu.Alinan, 5_000m, B, "Veli"),
                PaketAOrnek.Cek(2, CekYonu.Verilen, 2_500m, B.AddDays(3), "Toptancı"),
                PaketAOrnek.Cek(3, CekYonu.Alinan, 1_000m, B.AddDays(4)),   // 3 günden sonra: listelenmez
            ],
            gecen:
            [
                PaketAOrnek.Cek(4, CekYonu.Alinan, 700m, B.AddDays(-2), "A"),
                PaketAOrnek.Cek(5, CekYonu.Alinan, 800m, B.AddDays(-9), "B"),
                PaketAOrnek.Cek(6, CekYonu.Verilen, 900m, B.AddDays(-1), "C"),
            ]);
        var l = YapilacakListesi.Olustur(null, ozet, null, null, null, B);
        Assert.Equal([YapilacakTuru.VadesiGecenCek, YapilacakTuru.BugunVadeliCek, YapilacakTuru.YaklasanCek], l.Select(x => x.Tur));
        Assert.All(l, x => Assert.Equal("//cekler", x.Hedef));
        Assert.Equal("Vadesi geçen 3 çek", l[0].Baslik);
        Assert.Equal("B · +800,00 ₺ (15 Eylül), A · +700,00 ₺ (22 Eylül) ve 1 çek daha", l[0].Aciklama);
        Assert.Equal("Bugün vadesi gelen 1 çek", l[1].Baslik);
        Assert.Equal("Veli · +5.000,00 ₺ (bugün)", l[1].Aciklama);
        Assert.Equal("3 gün içinde vadesi gelecek 1 çek", l[2].Baslik);
        Assert.Equal("Toptancı · −2.500,00 ₺ (27 Eylül)", l[2].Aciklama);
        Assert.True(l[0].Acil && l[1].Acil && !l[2].Acil);
    }

    [Fact]
    public void Karti_odeme_bekleyen_kartlar_listelenir()
    {
        var kartlar = new List<KrediKartiDto>
        {
            PaketAOrnek.Kart(1, "Bonus", 20, 26, 50_000m, 3_000m, 1_000m),   // son ödeme 26 Eylül: 3 gün kala → listelenir
            PaketAOrnek.Kart(2, "Axess", 20, 30, 50_000m, 3_000m, 1_000m),   // son ödeme 30 Eylül: henüz değil
            PaketAOrnek.Kart(3, "World", 1, 24, 50_000m, 3_000m, 500m),      // bugün son gün
            PaketAOrnek.Kart(4, "Maximum", 1, 24, 50_000m, 3_000m, 0m),      // ekstre ödenmiş
        };
        var l = YapilacakListesi.Olustur(null, null, kartlar, null, null, B);
        Assert.Equal(["Bonus · son ödeme 26 Eylül", "World · son ödeme bugün"], l.Select(x => x.Baslik));
        Assert.Equal("Ekstre borcu 1.000,00 ₺", l[0].Aciklama);
        Assert.All(l, x => Assert.Equal("//kartlar", x.Hedef));
        Assert.Equal([false, true], l.Select(x => x.Acil));
    }

    [Fact]
    public void Eksik_gelen_ve_eski_sayim()
    {
        var eksik = new List<EksikGelenDto>
        {
            new(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20), ["MEZAT", "TOPTAN"]),
            new(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 13), []),
        };
        var l = YapilacakListesi.Olustur(null, null, null, eksik, [PaketAOrnek.Sayim(1, B.AddDays(-9), 0m), PaketAOrnek.Sayim(2, B.AddDays(-20), 0m)], B);
        Assert.Equal(2, l.Count);
        Assert.Equal(("Gelen girilmedi: 14–20 Eylül", "MEZAT, TOPTAN", "//islemler"), (l[0].Baslik, l[0].Aciklama, l[0].Hedef));
        Assert.Equal("Gelen gir", l[0].DugmeMetni);
        Assert.Equal(("Kasa sayımı 9 gündür yapılmadı", "Son sayım 15 Eylül", "//kasasayimi"), (l[1].Baslik, l[1].Aciklama, l[1].Hedef));
        Assert.Equal("Henüz kasa sayımı yapılmadı", Assert.Single(YapilacakListesi.Olustur(null, null, null, null, [], B)).Baslik);
    }

    [Fact]
    public void Bildirim_metni_ilk_uc_is_ve_kalan_sayi()
    {
        var s = Enumerable.Range(1, 5).Select(i => new YapilacakSatiri(YapilacakTuru.EksikGelen, $"İş {i}", "", "//islemler", false)).ToList();
        Assert.Equal("İş 1 · İş 2 · İş 3 · +2 iş daha", YapilacakListesi.BildirimMetni(s));
        Assert.Equal("İş 1", YapilacakListesi.BildirimMetni(s.Take(1).ToList()));
    }
}

public class PanelPaketATests
{
    private static (SahteApi api, PanelViewModel vm, BellekYerelDepo depo) Kur(bool editor = true, BellekYerelDepo? depo = null)
    {
        var api = new SahteApi { Panel = PaketAOrnek.Panel() };
        depo ??= new BellekYerelDepo();
        var vm = new PanelViewModel(api, PaketAOrnek.Saat(), depo) { EditorMu = editor };
        return (api, vm, depo);
    }

    [Fact]
    public async Task Durum_kartlari_kart_cek_sayim_ve_defter()
    {
        var (api, vm, _) = Kur(editor: false);
        api.KrediKartlariListe = [PaketAOrnek.Kart(1, "Bonus", 20, 30, 10_000m, 8_500m, 1_000m), PaketAOrnek.Kart(2, "Axess", 1, 10, 5_000m, 6_000m, 2_000m),
                                  PaketAOrnek.Kart(3, "World", 1, 10, 0m, 100m, 0m)];
        api.CekOzeti = PaketAOrnek.CekOzeti(gecen: [PaketAOrnek.Cek(9, CekYonu.Alinan, 700m, PaketAOrnek.Bugun.AddDays(-3))],
            alinan: 12_000m, alinanAdet: 3, verilen: 4_000m, verilenAdet: 2);
        api.KasaSayimlariListe = [PaketAOrnek.Sayim(5, PaketAOrnek.Bugun.AddDays(-4), 150m), PaketAOrnek.Sayim(4, PaketAOrnek.Bugun.AddDays(-10), -20m)];
        api.GecmisListe = [PaketAOrnek.Degisiklik(7, zaman: new DateTime(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc))];

        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.True(vm.KartDurumuVar);
        Assert.Equal(3_000m, vm.KartEkstreToplam);
        // Axess: kesim 1 / son ödeme 10 → 1 Eylül ekstresinin son ödemesi 10 Eylül geçti, borç duruyor
        // (Bonus'un 30 Eylül'ünden önce gelir ve vurgulanır).
        Assert.Equal("En yakın son ödeme: Axess · 10 Eylül (geçti)", vm.KartVadeMetni);
        Assert.True(vm.KartVadeAcil);
        Assert.Equal("Limit uyarısı: Bonus %85 · Axess limit aşıldı", vm.LimitUyariMetni);

        Assert.True(vm.CekDurumuVar);
        Assert.Equal((12_000m, "Tahsil edilecek 12.000,00 ₺ · 3 çek"), (vm.CekTahsilToplam, vm.CekTahsilMetni));
        Assert.Equal((4_000m, "Ödenecek 4.000,00 ₺ · 2 çek"), (vm.CekOdemeToplam, vm.CekOdemeMetni));
        Assert.Equal("1 çekin vadesi geçti · tahsil edilecek +700,00 ₺", vm.CekGecikmeMetni);

        Assert.True(vm.SayimDurumuVar);
        Assert.Equal("20 Eylül (4 gün önce)", vm.SayimMetni);
        Assert.Equal(150m, vm.SayimFarki);
        Assert.Equal("Fark +150,00 ₺", vm.SayimFarkMetni);

        Assert.Equal("Defter en son 2 saat önce güncellendi", vm.DefterGuncellemeMetni);
        // İzleyici: yapılacaklar yok, bekleyen ve eksik gelen istenmez.
        Assert.False(vm.YapilacakVar);
        Assert.Equal(0, api.BekleyenCagri);
        Assert.Equal(0, api.EksikGelenCagri);
    }

    [Fact]
    public async Task Vadesi_gecen_alinan_ve_verilen_cek_ayri_toplanir()
    {
        var (api, vm, _) = Kur(editor: false);
        api.CekOzeti = PaketAOrnek.CekOzeti(gecen: [PaketAOrnek.Cek(1, CekYonu.Alinan, 5_000m, PaketAOrnek.Bugun.AddDays(-3)),
                                                    PaketAOrnek.Cek(2, CekYonu.Verilen, 3_000m, PaketAOrnek.Bugun.AddDays(-1))]);
        await vm.YukleAsync();
        Assert.Equal("2 çekin vadesi geçti · tahsil edilecek +5.000,00 ₺ · ödenecek −3.000,00 ₺", vm.CekGecikmeMetni);

        api.CekOzeti = PaketAOrnek.CekOzeti(gecen: [PaketAOrnek.Cek(2, CekYonu.Verilen, 3_000m, PaketAOrnek.Bugun.AddDays(-1)),
                                                    PaketAOrnek.Cek(3, CekYonu.Verilen, 1_250.5m, PaketAOrnek.Bugun.AddDays(-9))]);
        await vm.YukleAsync();
        Assert.Equal("2 çekin vadesi geçti · ödenecek −4.250,50 ₺", vm.CekGecikmeMetni);
    }

    [Fact]
    public void Cek_yon_toplamlari_metni()
    {
        var b = PaketAOrnek.Bugun;
        Assert.Equal("", PanelMetin.CekYonToplamlari([]));
        Assert.Equal("tahsil edilecek +1.500,00 ₺",
            PanelMetin.CekYonToplamlari([PaketAOrnek.Cek(1, CekYonu.Alinan, 1_000m, b), PaketAOrnek.Cek(2, CekYonu.Alinan, 500m, b)]));
        Assert.Equal("tahsil edilecek +1.000,00 ₺ · ödenecek −1.000,00 ₺",   // net 0 değil, iki yön ayrı
            PanelMetin.CekYonToplamlari([PaketAOrnek.Cek(2, CekYonu.Verilen, 1_000m, b), PaketAOrnek.Cek(1, CekYonu.Alinan, 1_000m, b)]));
    }

    [Fact]
    public async Task Bos_veride_durum_metinleri()
    {
        var (_, vm, _) = Kur();
        await vm.YukleAsync();
        Assert.False(vm.KartDurumuVar);   // kart yok
        Assert.Equal("Kayıtlı kart yok", vm.KartVadeMetni);
        Assert.Null(vm.LimitUyariMetni);
        Assert.True(vm.TahminVar);
        Assert.True(vm.TahminHareketYok);   // düz tahmin: gösterilecek gün yok
        Assert.Empty(vm.TahminGunleri);
        Assert.Equal("En düşük: bugün, 58.900,00 ₺", vm.EnDusukMetni);
        Assert.Null(vm.CekGecikmeMetni);
        Assert.Equal("Henüz sayım yok", vm.SayimMetni);
        Assert.Null(vm.SayimFarki);
        Assert.Null(vm.DefterGuncellemeMetni);   // geçmiş boş
        Assert.Null(vm.YeniDegisiklikMetni);
    }

    [Fact]
    public async Task Ek_bolum_hatalari_ana_paneli_bozmaz_bekleyen_tek_kez_istenir()
    {
        var (api, vm, _) = Kur();
        api.GecmisOzetHatasi = new KasaApiException(HttpStatusCode.NotFound);
        api.NakitTahminHatasi = new KasaApiException(HttpStatusCode.NotFound);
        api.EksikGelenHatasi = new HttpRequestException("ağ");
        api.BekleyenListe = [new(1, "Kira", "Ortak", 100m, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5))];

        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Equal(58_900m, vm.GuncelKasa);
        Assert.Single(vm.BekleyenGiderler);
        Assert.Equal(1, api.BekleyenCagri);   // yapılacaklar aynı listeyi kullanır
        Assert.False(vm.TahminVar);           // eski sunucu: bölüm gizli
        Assert.Null(vm.TahminHata);
        Assert.False(vm.KartDurumuVar);
        Assert.True(vm.CekDurumuVar);
        Assert.True(vm.SayimDurumuVar);
        Assert.Null(vm.DefterGuncellemeMetni);
        Assert.Contains(vm.Yapilacaklar, y => y.Tur == YapilacakTuru.TekrarlayanGider);
        Assert.DoesNotContain(vm.Yapilacaklar, y => y.Tur == YapilacakTuru.EksikGelen);
    }

    [Fact]
    public async Task Ana_panel_hatasi_yine_hata_olarak_gorunur()
    {
        var (api, vm, _) = Kur();
        api.YuklemeHatasi = new KasaApiException(HttpStatusCode.BadRequest, "bozuk");
        await vm.YukleAsync();
        Assert.Equal("bozuk", vm.Hata);
        Assert.Empty(api.NakitTahminCagrilari);   // ek bölümler ana yükleme başarılıysa istenir
    }

    [Fact]
    public async Task Yapilacaklar_editorde_satir_dokunusu_hedefe_gider()
    {
        var (api, vm, _) = Kur();
        api.EksikGelenlerListe = [new(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20), ["MEZAT"])];
        api.KasaSayimlariListe = [PaketAOrnek.Sayim(1, PaketAOrnek.Bugun.AddDays(-1), 0m)];
        var gidilen = new List<string>();
        vm.GitIstendi += (_, h) => gidilen.Add(h);

        await vm.YukleAsync();

        Assert.True(vm.YapilacakVar);
        var s = Assert.Single(vm.Yapilacaklar);
        Assert.Equal(YapilacakTuru.EksikGelen, s.Tur);
        vm.YapilacakAcCommand.Execute(s);
        vm.GitCommand.Execute("//cekler");
        vm.GitCommand.Execute("");
        Assert.Equal(["//islemler", "//cekler"], gidilen);

        vm.EditorMu = false;
        Assert.False(vm.YapilacakVar);
    }

    [Fact]
    public async Task Son_bakistan_beri_ilk_acilista_baslangic_kaydedilir_sonra_sayilir()
    {
        var (api, vm, depo) = Kur();
        api.GecmisListe = [PaketAOrnek.Degisiklik(10), PaketAOrnek.Degisiklik(9)];

        await vm.YukleAsync();
        Assert.Null(vm.YeniDegisiklikMetni);                        // birikmiş eski satırlar "yeni" değil
        Assert.Equal(10, depo.OkuInt(YerelAnahtarlar.GecmisSonGorulenId));
        Assert.Equal([(int?)null], api.GecmisOzetCagrilari);

        api.GecmisListe = [PaketAOrnek.Degisiklik(13, gecmiseDonuk: true), PaketAOrnek.Degisiklik(12), PaketAOrnek.Degisiklik(11, gecmiseDonuk: true),
                           PaketAOrnek.Degisiklik(10), PaketAOrnek.Degisiklik(9)];
        await vm.YukleAsync();
        Assert.Equal(10, api.GecmisOzetCagrilari[^1]);
        Assert.Equal("Son bakışınızdan beri 3 değişiklik, 2'si geçmiş aylara dokunuyor", vm.YeniDegisiklikMetni);
        Assert.True(vm.GecmiseDonukVar);
        Assert.Equal([13, 11], vm.GecmiseDonukSatirlar.Select(s => s.Id));

        vm.DegisiklikleriGorulduSayCommand.Execute(null);
        Assert.Null(vm.YeniDegisiklikMetni);
        Assert.Empty(vm.GecmiseDonukSatirlar);
        Assert.Equal(13, depo.OkuInt(YerelAnahtarlar.GecmisSonGorulenId));
        await vm.YukleAsync();
        Assert.Null(vm.YeniDegisiklikMetni);
    }

    [Fact]
    public async Task Son_bakis_sunucudan_ilerideyse_sifirlanir()
    {
        var depo = new BellekYerelDepo();
        depo.YazInt(YerelAnahtarlar.GecmisSonGorulenId, 500);
        var (api, vm, _) = Kur(depo: depo);
        api.GecmisListe = [PaketAOrnek.Degisiklik(3)];
        await vm.YukleAsync();
        Assert.Null(vm.YeniDegisiklikMetni);
        Assert.Equal(3, depo.OkuInt(YerelAnahtarlar.GecmisSonGorulenId));
    }

    [Theory]
    [InlineData(12, 0, "Son bakışınızdan beri 12 değişiklik")]
    [InlineData(12, 2, "Son bakışınızdan beri 12 değişiklik, 2'si geçmiş aylara dokunuyor")]
    [InlineData(1, 1, "Son bakışınızdan beri 1 değişiklik, 1'i geçmiş aylara dokunuyor")]
    [InlineData(3, 3, "Son bakışınızdan beri 3 değişiklik, hepsi geçmiş aylara dokunuyor")]
    [InlineData(6, 6, "Son bakışınızdan beri 6 değişiklik, hepsi geçmiş aylara dokunuyor")]
    public void Degisiklik_metni(int toplam, int donuk, string beklenen) => Assert.Equal(beklenen, PanelViewModel.DegisiklikMetni(toplam, donuk));

    private static NakitTahminDto Tahmin(int gun, bool haricli)
    {
        var b = PaketAOrnek.Bugun;
        var cek = new TahminKalemiDto(new DateOnly(2026, 10, 5), TahminKalemTuru.AlinanCek, "Veli · alınan çek", 7_500m, 3);
        var gec = new TahminKalemiDto(new DateOnly(2026, 9, 20), TahminKalemTuru.AlinanCek, "Geciken · alınan çek", 1_000m, 4, true);
        var kira = new TahminKalemiDto(new DateOnly(2026, 9, 5), TahminKalemTuru.TekrarlayanGider, "Kira · tekrarlayan gider (onay bekliyor)", -65_000m, null, true);
        var gunler = new List<TahminGunuDto> { new(b, 0, 0, 58_900m, []) };
        gunler.Add(new(b.AddDays(1), 1_000m, 65_000m, -5_100m, [gec, kira]));
        for (var i = 2; i <= gun; i++)
        {
            var t = b.AddDays(i);
            var kasa = gunler[^1].Kasa;
            gunler.Add(t == cek.Tarih && !haricli ? new(t, 7_500m, 0, kasa + 7_500m, [cek]) : new(t, 0, 0, kasa, []));
        }
        return new NakitTahminDto(b, gun, 58_900m, gunler, b.AddDays(1), -5_100m, gunler[^1].Kasa, haricli ? 1_000m : 8_500m, 65_000m,
            haricli ? [cek] : []);
    }

    [Fact]
    public async Task Tahmin_en_dusuk_gun_ve_hareketli_gunler()
    {
        var (api, vm, _) = Kur();
        api.NakitTahminUret = (g, h) => Task.FromResult(Tahmin(g, h?.Contains(3) == true));

        await vm.YukleAsync();

        Assert.True(vm.TahminVar);
        Assert.Equal(30, vm.TahminGun);
        Assert.Equal(30, api.NakitTahminCagrilari[0].Gun);
        Assert.Empty(api.NakitTahminCagrilari[0].Haric);
        Assert.Equal("En düşük: 25 Eylül, −5.100,00 ₺", vm.EnDusukMetni);
        Assert.True(vm.EnDusukNegatif);
        Assert.Equal("30 gün sonra 2.400,00 ₺ · giriş +8.500,00 ₺ · çıkış −65.000,00 ₺", vm.TahminOzetMetni);
        Assert.Equal([new DateOnly(2026, 9, 25), new DateOnly(2026, 10, 5)], vm.TahminGunleri.Select(g => g.Tarih));
        var ilk = vm.TahminGunleri[0];
        Assert.True(ilk.EnDusukMu);
        Assert.True(ilk.KasaNegatif);
        Assert.Equal("−5.100,00 ₺", ilk.KasaMetni);
        Assert.Equal("25 Eylül Cum", ilk.TarihMetni);
        Assert.Equal("Geciken · alınan çek · vade 20 Eylül", ilk.Kalemler[0].Aciklama);
        Assert.Equal("+1.000,00 ₺", ilk.Kalemler[0].TutarMetni);
        Assert.True(ilk.Kalemler[0].Giris);
        Assert.Equal("−65.000,00 ₺", ilk.Kalemler[1].TutarMetni);
        Assert.Equal([3, 4], vm.TahminCekleri.Select(c => c.CekId).Order());
        Assert.All(vm.TahminCekleri, c => Assert.True(c.Dahil));
    }

    [Fact]
    public async Task Tahmin_ufku_ve_haric_cek_cihazda_saklanir()
    {
        var (api, vm, depo) = Kur();
        api.NakitTahminUret = (g, h) => Task.FromResult(Tahmin(g, h?.Contains(3) == true));
        await vm.YukleAsync();

        await vm.SecTahminGunCommand.ExecuteAsync(vm.TahminCipleri[2]);
        Assert.Equal(90, vm.TahminGun);
        Assert.Equal(90, depo.OkuInt(YerelAnahtarlar.TahminGun));
        Assert.Equal([false, false, true], vm.TahminCipleri.Select(c => c.Secili));
        Assert.Equal(90, api.NakitTahminCagrilari[^1].Gun);

        var veli = vm.TahminCekleri.Single(c => c.CekId == 3);
        Assert.Equal("Hesaptan çıkar", veli.DugmeMetni);
        await vm.TahminCekDegistirCommand.ExecuteAsync(veli);
        Assert.Equal([3], api.NakitTahminCagrilari[^1].Haric);
        Assert.Equal("3", depo.Oku(YerelAnahtarlar.TahminHaricCekler));
        var haric = vm.TahminCekleri.Single(c => c.CekId == 3);
        Assert.False(haric.Dahil);
        Assert.Equal("Hesaba kat", haric.DugmeMetni);

        // Yeni bir panel (uygulama yeniden açıldı): ufuk ve hariç çek hatırlanır.
        var vm2 = new PanelViewModel(api, PaketAOrnek.Saat(), depo);
        await vm2.YukleAsync();
        Assert.Equal(90, vm2.TahminGun);
        Assert.Equal(90, api.NakitTahminCagrilari[^1].Gun);
        Assert.Equal([3], api.NakitTahminCagrilari[^1].Haric);
        Assert.False(vm2.TahminCekleri.Single(c => c.CekId == 3).Dahil);

        await vm2.TahminCekDegistirCommand.ExecuteAsync(vm2.TahminCekleri.Single(c => c.CekId == 3));
        Assert.Null(depo.Oku(YerelAnahtarlar.TahminHaricCekler));
        Assert.Empty(api.NakitTahminCagrilari[^1].Haric);
    }

    [Fact]
    public async Task Tahmin_gecersiz_kayitli_ufuk_30a_doner_hata_bolumde_gosterilir()
    {
        var depo = new BellekYerelDepo();
        depo.YazInt(YerelAnahtarlar.TahminGun, 45);
        var (api, vm, _) = Kur(depo: depo);
        api.NakitTahminHatasi = new KasaApiException(HttpStatusCode.BadRequest, "gun 1 ile 366 arasında olmalı.");
        await vm.YukleAsync();
        Assert.Equal(30, vm.TahminGun);
        Assert.Null(vm.Hata);
        Assert.True(vm.TahminVar);
        Assert.Equal("gun 1 ile 366 arasında olmalı.", vm.TahminHata);
        Assert.False(vm.TahminYukleniyor);
    }

    [Fact]
    public async Task Tahmin_gec_gelen_eski_yanit_yeni_ufku_ezmez()
    {
        var (api, vm, _) = Kur();
        var bekleyen = new Dictionary<int, TaskCompletionSource<NakitTahminDto>>();
        api.NakitTahminUret = (g, _) =>
        {
            var t = new TaskCompletionSource<NakitTahminDto>();
            bekleyen[g] = t;
            return t.Task;
        };
        var yukle = vm.YukleAsync();
        bekleyen[30].SetResult(Tahmin(30, false));
        await yukle;

        var altmis = vm.SecTahminGunCommand.ExecuteAsync(vm.TahminCipleri[1]);
        var doksan = vm.SecTahminGunCommand.ExecuteAsync(vm.TahminCipleri[2]);
        bekleyen[90].SetResult(Tahmin(90, false));
        await doksan;
        bekleyen[60].SetResult(Tahmin(60, false));
        await altmis;
        Assert.Equal(90, vm.SonTahmin!.Gun);
        Assert.Equal(90, vm.TahminGun);
    }
}

public class GecmisPaketATests
{
    [Fact]
    public async Task Son_bakistan_yeni_satirlar_isaretlenir_ve_gorulen_saklanir()
    {
        var depo = new BellekYerelDepo();
        depo.YazInt(YerelAnahtarlar.GecmisSonGorulenId, 10);
        var api = new SahteApi { GecmisListe = [PaketAOrnek.Degisiklik(12, gecmiseDonuk: true), PaketAOrnek.Degisiklik(11), PaketAOrnek.Degisiklik(10), PaketAOrnek.Degisiklik(9, gecmiseDonuk: true)] };
        var vm = new GecmisViewModel(api, PaketAOrnek.Saat(), depo);

        await vm.YukleAsync();

        Assert.Equal([true, true, false, false], vm.Kayitlar.Select(k => k.Yeni));
        Assert.Equal([true, false, false, true], vm.Kayitlar.Select(k => k.GecmiseDonuk));
        Assert.Equal(2, vm.YeniSayisi);
        Assert.Equal("Son bakışınızdan beri 2 yeni değişiklik", vm.YeniMetni);
        Assert.Equal([true, true, false, true], vm.Kayitlar.Select(k => k.EtiketVar));
        Assert.Equal(12, depo.OkuInt(YerelAnahtarlar.GecmisSonGorulenId));

        // Sonraki açılışta aynı satırlar artık yeni değil.
        await vm.YukleAsync();
        Assert.All(vm.Kayitlar, k => Assert.False(k.Yeni));
        Assert.Null(vm.YeniMetni);
    }

    [Fact]
    public async Task Ilk_acilista_hic_satir_yeni_degil_filtreliyken_gorulen_ilerlemez()
    {
        var depo = new BellekYerelDepo();
        var api = new SahteApi
        {
            GecmisListe = [PaketAOrnek.Degisiklik(5, tur: "Çek"), PaketAOrnek.Degisiklik(4)],
            GecmisTurleriListe = ["Çek", "İşlem"],
        };
        var vm = new GecmisViewModel(api, PaketAOrnek.Saat(), depo);
        await vm.YukleAsync();
        Assert.All(vm.Kayitlar, k => Assert.False(k.Yeni));
        Assert.Equal(5, depo.OkuInt(YerelAnahtarlar.GecmisSonGorulenId));

        api.GecmisListe = [PaketAOrnek.Degisiklik(7), PaketAOrnek.Degisiklik(6, tur: "Çek"), .. api.GecmisListe];
        await vm.YukleAsync();   // 6 ve 7 yeni
        depo.YazInt(YerelAnahtarlar.GecmisSonGorulenId, 5);
        await vm.SecTurCommand.ExecuteAsync(vm.TurCipleri.Single(c => c.Ad == "Çek"));
        Assert.Equal(5, depo.OkuInt(YerelAnahtarlar.GecmisSonGorulenId));   // filtreli liste "görüldü" saymaz
    }

    [Fact]
    public async Task Gecmis_bos_ise_gorulen_sifirlanir()
    {
        var depo = new BellekYerelDepo();
        depo.YazInt(YerelAnahtarlar.GecmisSonGorulenId, 40);
        var vm = new GecmisViewModel(new SahteApi(), PaketAOrnek.Saat(), depo);
        await vm.YukleAsync();
        Assert.Equal(0, depo.OkuInt(YerelAnahtarlar.GecmisSonGorulenId));
    }
}

public class YonlendirmePaketATests
{
    [Theory]
    [InlineData("cekler", "//cekler")]
    [InlineData("kasasayimi", "//kasasayimi")]
    [InlineData("gecmis", "//gecmis")]
    [InlineData("haftalik", "//haftalik")]
    [InlineData("bekleyen", null)]
    public void Bildirim_hedefleri(string hedef, string? rota) => Assert.Equal(rota, Yonlendirme.RotaCoz(hedef));
}

public class BildirimPlanlayiciTests
{
    private sealed class SahteBildirim : IKisaBildirim
    {
        public List<KisaBildirim> Gosterilen { get; } = new();
        public void Goster(KisaBildirim b) => Gosterilen.Add(b);
    }

    private static HaftalikOzetDto Hafta(DateOnly bas, DateOnly son, decimal sonuc)
        => new(new DonemDto(bas, son, bas.Year, bas.Month), [], 0m, 0m, sonuc, 0m);

    private static (SahteApi api, BellekYerelDepo depo, SahteBildirim b, SabitSaat saat, BildirimPlanlayici p) Kur(DateTime yerel)
    {
        var api = new SahteApi
        {
            Panel = PaketAOrnek.Panel(58_900m),
            HaftalikListe =
            [
                Hafta(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 20), 99m),
                Hafta(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 1_000m),
                Hafta(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 30), 2_500m),
                Hafta(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4), -500.50m),
                Hafta(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), 7m),
            ],
        };
        var depo = new BellekYerelDepo();
        var b = new SahteBildirim();
        var saat = new SabitSaat(yerel);
        return (api, depo, b, saat, new BildirimPlanlayici(api, depo, b, saat));
    }

    [Theory]
    [InlineData("2026-10-05T07:59", "2026-09-28")]
    [InlineData("2026-10-05T08:00", "2026-10-05")]
    [InlineData("2026-10-04T23:00", "2026-09-28")]   // Pazar
    [InlineData("2026-10-08T12:00", "2026-10-05")]   // Perşembe
    public void Hafta_anahtari_pazartesi_0800(string simdi, string anahtar)
        => Assert.Equal(DateOnly.Parse(anahtar), BildirimPlanlayici.HaftaAnahtari(DateTime.Parse(simdi)));

    [Fact]
    public async Task Pazartesi_ozeti_bolunmus_haftayi_toplar_ve_haftada_bir_kez_gider()
    {
        var (api, depo, b, saat, p) = Kur(new DateTime(2026, 10, 5, 8, 30, 0));   // Pazartesi
        var g = await p.CalistirAsync(Rol.Izleyici);

        var ozet = Assert.Single(g);
        Assert.Equal("Haftalık özet · 28 Eylül – 4 Ekim", ozet.Baslik);
        Assert.Equal("Geçen hafta kasa sonucu +1.999,50 ₺, güncel kasa 58.900,00 ₺", ozet.Metin);
        Assert.Equal("haftalik", ozet.Hedef);
        Assert.Equal(new DateOnly(2026, 10, 5), depo.OkuTarih(YerelAnahtarlar.SonHaftalikOzet));

        saat.Ilerle(TimeSpan.FromDays(2));
        Assert.Empty(await p.CalistirAsync(Rol.Izleyici));
        saat.Ilerle(TimeSpan.FromDays(5));   // sonraki Pazartesi 08:30
        Assert.Single(await p.CalistirAsync(Rol.Izleyici), x => x.Hedef == "haftalik");
        Assert.Equal(2, b.Gosterilen.Count(x => x.Hedef == "haftalik"));
    }

    [Fact]
    public async Task Vadesi_gecen_cekler_ozetle_ayri_bildirim_anahtarla_kapanir()
    {
        var (api, depo, b, _, p) = Kur(new DateTime(2026, 10, 5, 9, 0, 0));
        api.CekOzeti = PaketAOrnek.CekOzeti(gecen: [PaketAOrnek.Cek(1, CekYonu.Alinan, 700m, new DateOnly(2026, 9, 30), "Veli"),
                                                    PaketAOrnek.Cek(2, CekYonu.Verilen, 300m, new DateOnly(2026, 9, 12), "Nakliye")]);
        depo.YazBool(YerelAnahtarlar.BildirimHaftalikOzet, false);
        var g = await p.CalistirAsync(Rol.Izleyici);
        var c = Assert.Single(g);
        Assert.Equal("Vadesi geçen 2 çek", c.Baslik);
        // Alınan (bize gelecek) ve verilen (bizden çıkacak) çek tek toplamda birleşmez.
        Assert.Equal("Tahsil edilecek +700,00 ₺ · ödenecek −300,00 ₺ · en eskisi Nakliye, vade 12 Eylül", c.Metin);
        Assert.Equal("cekler", c.Hedef);
    }

    [Fact]
    public async Task Okuma_hatasinda_hafta_isaretlenmez_yarim_bildirim_gitmez()
    {
        var (api, depo, b, _, p) = Kur(new DateTime(2026, 10, 5, 9, 0, 0));
        api.CekOzeti = PaketAOrnek.CekOzeti(gecen: [PaketAOrnek.Cek(1, CekYonu.Alinan, 700m, new DateOnly(2026, 9, 30))]);
        api.YuklemeHatasi = new HttpRequestException("ağ yok");
        Assert.Empty(await p.CalistirAsync(Rol.Izleyici));
        Assert.Null(depo.OkuTarih(YerelAnahtarlar.SonHaftalikOzet));
        api.YuklemeHatasi = null;
        Assert.Equal(2, (await p.CalistirAsync(Rol.Izleyici)).Count);
    }

    [Fact]
    public async Task Tum_haftalik_bildirimler_kapaliyken_hafta_isaretlenir_istek_yok()
    {
        var (api, depo, b, _, p) = Kur(new DateTime(2026, 10, 5, 9, 0, 0));
        depo.YazBool(YerelAnahtarlar.BildirimHaftalikOzet, false);
        depo.YazBool(YerelAnahtarlar.BildirimVadesiGecenCek, false);
        depo.YazBool(YerelAnahtarlar.BildirimGecmiseDonuk, false);
        Assert.Empty(await p.CalistirAsync(Rol.Izleyici));
        Assert.Equal(0, api.CekOzetCagri);
        Assert.Empty(api.GecmisOzetCagrilari);
        Assert.Equal(new DateOnly(2026, 10, 5), depo.OkuTarih(YerelAnahtarlar.SonHaftalikOzet));
    }

    [Fact]
    public async Task Gecmise_donuk_ilk_calistirma_baslangic_sonra_yeni_olanlar_gunde_bir_kez()
    {
        var (api, depo, b, saat, p) = Kur(new DateTime(2026, 10, 7, 9, 0, 0));   // Çarşamba
        depo.YazTarih(YerelAnahtarlar.SonHaftalikOzet, new DateOnly(2026, 10, 5));
        api.GecmisListe = [PaketAOrnek.Degisiklik(20, gecmiseDonuk: true)];

        Assert.Empty(await p.CalistirAsync(Rol.Izleyici));   // eski satır bildirilmez
        Assert.Equal(20, depo.OkuInt(YerelAnahtarlar.SonGecmiseDonukId));

        api.GecmisListe = [PaketAOrnek.Degisiklik(23), PaketAOrnek.Degisiklik(22, gecmiseDonuk: true), PaketAOrnek.Degisiklik(21, gecmiseDonuk: true), .. api.GecmisListe];
        var g = Assert.Single(await p.CalistirAsync(Rol.Izleyici));
        Assert.Equal("Geçmişe dönük düzeltme", g.Baslik);
        Assert.Equal("2 değişiklik geçmiş ayların rakamlarını değiştirdi · son: İşlem eklendi #22", g.Metin);
        Assert.Equal("gecmis", g.Hedef);
        Assert.Equal(23, depo.OkuInt(YerelAnahtarlar.SonGecmiseDonukId));

        api.GecmisListe = [PaketAOrnek.Degisiklik(24, gecmiseDonuk: true), .. api.GecmisListe];
        Assert.Empty(await p.CalistirAsync(Rol.Izleyici));   // bugün zaten bildirildi
        saat.Ilerle(TimeSpan.FromDays(1));
        Assert.Equal("İşlem eklendi #24", Assert.Single(await p.CalistirAsync(Rol.Izleyici)).Metin);
    }

    [Fact]
    public async Task Gecmis_sifirlandiysa_baslangic_geri_alinir()
    {
        var (api, depo, _, _, p) = Kur(new DateTime(2026, 10, 7, 9, 0, 0));
        depo.YazTarih(YerelAnahtarlar.SonHaftalikOzet, new DateOnly(2026, 10, 5));
        depo.YazInt(YerelAnahtarlar.SonGecmiseDonukId, 900);
        api.GecmisListe = [PaketAOrnek.Degisiklik(2, gecmiseDonuk: true)];
        Assert.Empty(await p.CalistirAsync(Rol.Izleyici));
        Assert.Equal(2, depo.OkuInt(YerelAnahtarlar.SonGecmiseDonukId));
    }

    [Fact]
    public async Task Bugun_yapilacaklar_yalniz_editor_gunun_ilk_calistirmasinda_bir_kez()
    {
        var (api, depo, b, saat, p) = Kur(new DateTime(2026, 10, 7, 8, 30, 0));   // sabah, 09:00'dan önce
        depo.YazTarih(YerelAnahtarlar.SonHaftalikOzet, new DateOnly(2026, 10, 5));
        depo.YazBool(YerelAnahtarlar.BildirimGecmiseDonuk, false);
        api.BekleyenListe = [new(1, "Kira", "Ortak", 15_000m, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5))];
        api.KasaSayimlariListe = [PaketAOrnek.Sayim(1, new DateOnly(2026, 10, 6), 0m)];

        Assert.Empty(await p.CalistirAsync(Rol.Izleyici));   // izleyiciye yapılacaklar gitmez
        Assert.Equal(0, api.BekleyenCagri);

        // Uygulama sabah açıldı (günün ilk çalıştırması): bildirim gelir.
        var g = Assert.Single(await p.CalistirAsync(Rol.Editor));
        Assert.Equal("Bugün yapılacaklar (1)", g.Baslik);
        Assert.Equal("1 tekrarlayan gider onay bekliyor", g.Metin);
        Assert.Equal("panel", g.Hedef);
        Assert.Equal(new DateOnly(2026, 10, 7), depo.OkuTarih(YerelAnahtarlar.SonYapilacaklar));
        // 09:00 hatırlatıcısı ya da uygulamanın yeniden açılışı aynı gün tekrar göndermez.
        saat.Ilerle(TimeSpan.FromMinutes(30));
        Assert.Empty(await p.CalistirAsync(Rol.Editor));
        Assert.Single(b.Gosterilen, x => x.Hedef == "panel");

        // Ertesi gün uygulama açılmadan 09:00 hatırlatıcısı çalışırsa o gönderir; liste boşsa bildirim yok.
        saat.Ilerle(TimeSpan.FromDays(1));
        api.BekleyenListe = [];
        Assert.Empty(await p.CalistirAsync(Rol.Editor));
        Assert.Equal(new DateOnly(2026, 10, 8), depo.OkuTarih(YerelAnahtarlar.SonYapilacaklar));

        saat.Ilerle(TimeSpan.FromDays(1));
        api.BekleyenListe = [new(1, "Kira", "Ortak", 15_000m, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5))];
        depo.YazBool(YerelAnahtarlar.BildirimBugunYapilacaklar, false);
        Assert.Empty(await p.CalistirAsync(Rol.Editor));
    }

    [Fact]
    public async Task Bugun_yapilacaklar_okuma_hatasinda_isaretlenmez_sonraki_calistirmada_gelir()
    {
        var (api, depo, b, saat, p) = Kur(new DateTime(2026, 10, 7, 8, 30, 0));
        depo.YazTarih(YerelAnahtarlar.SonHaftalikOzet, new DateOnly(2026, 10, 5));
        depo.YazBool(YerelAnahtarlar.BildirimGecmiseDonuk, false);
        api.BekleyenListe = [new(1, "Kira", "Ortak", 15_000m, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5))];
        api.YuklemeHatasi = new HttpRequestException("ağ yok");
        Assert.Empty(await p.CalistirAsync(Rol.Editor));   // açılışta ağ yoktu
        Assert.Null(depo.OkuTarih(YerelAnahtarlar.SonYapilacaklar));
        api.YuklemeHatasi = null;
        saat.Ilerle(TimeSpan.FromMinutes(30));              // 09:00 hatırlatıcısı telafi eder
        Assert.Single(await p.CalistirAsync(Rol.Editor), x => x.Hedef == "panel");
    }

    [Fact]
    public async Task Eski_sunucuda_eksik_gelen_ucu_yoksa_liste_yine_uretilir()
    {
        var (api, depo, _, _, p) = Kur(new DateTime(2026, 10, 7, 9, 0, 0));
        depo.YazTarih(YerelAnahtarlar.SonHaftalikOzet, new DateOnly(2026, 10, 5));
        depo.YazBool(YerelAnahtarlar.BildirimGecmiseDonuk, false);
        api.EksikGelenHatasi = new KasaApiException(HttpStatusCode.NotFound);
        var g = Assert.Single(await p.CalistirAsync(Rol.Editor));
        Assert.Equal("Henüz kasa sayımı yapılmadı", g.Metin);
    }

    [Fact]
    public void Ayarlar_varsayilan_acik_degisiklik_depoya_yazilir()
    {
        var depo = new BellekYerelDepo();
        var vm = new BildirimAyarlariViewModel(depo);
        Assert.True(vm.HaftalikOzet && vm.VadesiGecenCek && vm.GecmiseDonuk && vm.BugunYapilacaklar && vm.KartHatirlatma);
        vm.GecmiseDonuk = false;
        vm.KartHatirlatma = false;
        Assert.False(depo.OkuBool(YerelAnahtarlar.BildirimGecmiseDonuk, true));
        Assert.False(depo.OkuBool(YerelAnahtarlar.BildirimKartHatirlatma, true));
        var vm2 = new BildirimAyarlariViewModel(depo);
        Assert.False(vm2.GecmiseDonuk);
        Assert.False(vm2.KartHatirlatma);
        Assert.True(vm2.HaftalikOzet);
    }

    [Fact]
    public void Ayarlar_yenilenince_baska_yerde_yapilan_degisikligi_gosterir_depoya_yazmaz()
    {
        // Ayarlar sayfası (Shell'de saklı) ve Panel → Bildirimler aynı depoyu kullanır.
        var depo = new SayanYerelDepo();
        var ayarlar = new BildirimAyarlariViewModel(depo);
        var panel = new BildirimAyarlariViewModel(depo);
        Assert.Equal(0, depo.Yazma);
        panel.HaftalikOzet = false;
        panel.BugunYapilacaklar = false;
        Assert.Equal(2, depo.Yazma);
        Assert.True(ayarlar.HaftalikOzet);   // henüz tazelenmedi

        var degisen = new List<string?>();
        ayarlar.PropertyChanged += (_, e) => degisen.Add(e.PropertyName);
        ayarlar.Yenile();
        Assert.False(ayarlar.HaftalikOzet);
        Assert.False(ayarlar.BugunYapilacaklar);
        Assert.True(ayarlar.VadesiGecenCek && ayarlar.GecmiseDonuk && ayarlar.KartHatirlatma);
        Assert.Equal([nameof(BildirimAyarlariViewModel.HaftalikOzet), nameof(BildirimAyarlariViewModel.BugunYapilacaklar)], degisen);
        Assert.Equal(2, depo.Yazma);   // okunan değer geri yazılmadı

        // Yenilemeden sonra anahtar yine depoya yazar.
        ayarlar.HaftalikOzet = true;
        Assert.Equal(3, depo.Yazma);
        panel.Yenile();
        Assert.True(panel.HaftalikOzet);
    }

    private sealed class SayanYerelDepo : IYerelDepo
    {
        private readonly BellekYerelDepo _ic = new();
        public int Yazma;
        public string? Oku(string anahtar) => _ic.Oku(anahtar);
        public void Yaz(string anahtar, string? deger) { Yazma++; _ic.Yaz(anahtar, deger); }
    }
}

public class TekSeferlikTests
{
    [Fact]
    public void Bir_kez_calisir_sonraki_cagrilar_atlanir()
    {
        var t = new TekSeferlik();
        var sayac = 0;
        Assert.False(t.Yapildi);
        Assert.True(t.Calistir(() => sayac++));
        Assert.False(t.Calistir(() => sayac++));
        Assert.False(t.Calistir(() => throw new InvalidOperationException("çağrılmamalı")));
        Assert.Equal(1, sayac);
        Assert.True(t.Yapildi);
    }

    [Fact]
    public void Hata_atan_eylem_yapilmis_sayilmaz_sonra_yeniden_denenir()
    {
        var t = new TekSeferlik();
        Assert.Throws<InvalidOperationException>(() => t.Calistir(() => throw new InvalidOperationException("kayıt başarısız")));
        Assert.False(t.Yapildi);
        var sayac = 0;
        Assert.True(t.Calistir(() => sayac++));
        Assert.Equal(1, sayac);
    }

    [Fact]
    public async Task Ayni_anda_gelen_cagrilardan_yalniz_biri_calistirir()
    {
        var t = new TekSeferlik();
        var sayac = 0;
        using var basla = new ManualResetEventSlim();
        var gorevler = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            basla.Wait();
            return t.Calistir(() => { Interlocked.Increment(ref sayac); Thread.Sleep(20); });
        })).ToList();
        basla.Set();
        var sonuc = await Task.WhenAll(gorevler);
        Assert.Equal(1, sayac);
        Assert.Single(sonuc, x => x);
    }
}
