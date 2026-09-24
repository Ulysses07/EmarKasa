using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket B: kasa dökümü (21), rapordan İşlemler'e iniş (22), hedef gösterimi (05), ay kilidi/yayını (11).</summary>
public class PaketBRaporTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    /// <summary>Açılış 1.000; +30.000 gelen, +10.000 çek, −12.000 Cari, −45.000 sabit, −2.000 Ortak, −3.000 kart → −21.000.</summary>
    private static KasaDokumuDto Dokum(DateOnly bas, DateOnly bit)
    {
        var adimlar = new List<(string Tur, string Ad, string? Kanal, decimal Tutar)>
        {
            ("Gelen", "Gelen", "MEZAT", 30_000m),
            ("CekTahsilat", "Çek tahsilatı", "MEZAT", 10_000m),
            ("CariGider", "Cari gider", "MEZAT", -12_000m),
            ("SabitGider", "Sabit gider", "MEZAT", -45_000m),
            ("OrtakGider", "Ortak gider", "Ortak", -2_000m),
            ("KartOdemesi", "Kredi kartı ödemesi", null, -3_000m),
        };
        decimal b = 1_000m;
        var liste = new List<KasaDokumAdimiDto>();
        foreach (var a in adimlar) { b += a.Tutar; liste.Add(new KasaDokumAdimiDto(a.Tur, a.Ad, a.Kanal, a.Tutar, b)); }
        return new KasaDokumuDto(bas, bit, 1_000m, b, 40_000m, 62_000m, liste);
    }

    [Fact]
    public void Dokum_satirlari_acilistan_kapanisa_ve_inis()
    {
        var d = Dokum(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        var s = KasaDokumuGorunum.Satirlar(d);
        Assert.Equal(8, s.Count);
        Assert.Equal(("Açılış", 1_000m, true), (s[0].Etiket, s[0].Bakiye, s[0].UcSatir));
        Assert.Equal(("Kapanış", -21_000m, true), (s[^1].Etiket, s[^1].Bakiye, s[^1].UcSatir));
        Assert.Equal(d.Acilis + s.Where(x => !x.UcSatir).Sum(x => x.Tutar!.Value), d.Kapanis);
        Assert.Equal("MEZAT · Gelen", s[1].Etiket);
        Assert.Equal("+30.000,00", s[1].TutarMetni);
        Assert.Equal("-12.000,00", s[3].TutarMetni);
        Assert.Equal("Kredi kartı ödemesi", s[6].Etiket);

        Assert.Null(s[1].Suzgec);                                                     // gelen işlem değildir
        Assert.Equal(new IslemSuzgeci(d.Baslangic, d.Bitis, "MEZAT", IslemTipSuzgeci.Cari), s[3].Suzgec);
        Assert.Equal(new IslemSuzgeci(d.Baslangic, d.Bitis, "MEZAT", IslemTipSuzgeci.SabitGider), s[4].Suzgec);
        // Ortak gider adımı yalnız kart dışı Ortak işlemleridir (K.K kasadan kendi tarihinde çıkmaz).
        Assert.Equal(new IslemSuzgeci(d.Baslangic, d.Bitis, "Ortak", IslemTipSuzgeci.Nakit), s[5].Suzgec);
        Assert.Null(s[6].Suzgec);
        Assert.Equal("Kasa 1.000,00 ₺ ile açıldı, 40.000,00 ₺ girdi, 62.000,00 ₺ çıktı; -21.000,00 ₺ ile kapandı.", KasaDokumuGorunum.Ozet(d));
    }

    private static AylikRaporDto Rapor(int ay = 8) => new(2026, ay, new List<KanalAylikDto>
    {
        new("MEZAT", 30_000m, 12_000m, 45_000m, 3_000m, 1_000m, -21_000m, 10_000m, 0m),
        new("PERAKENDE", 0m, 0m, 0m, 0m, 1_000m, -1_000m),
    });

    private static HedefButceDto Hedef(int ay = 8) => new(2026, ay,
        new List<KanalHedefDto>
        {
            new(1, "MEZAT", true, 50_000m, 40_000m, 80.0m),
            new(2, "PERAKENDE", true, null, 0m, null),
        },
        new List<GiderButceDto>
        {
            new(802, "Kira", true, 42_000m, 45_000m, 107.1m, 40_000m),
            new(801, "SGK", true, null, 0m, null, null),
        });

    [Fact]
    public async Task Aylik_ekleri_yukler_kanal_satiri_hedef_ve_inis_rakamlari()
    {
        var api = new SahteApi { AylikRapor = Rapor(), HedefButce = Hedef(), KasaDokumuUret = (b, s) => Task.FromResult(Dokum(b, s)) };
        var gez = new SahteGezinti();
        var vm = new AylikViewModel(api, new SabitSaat(Bugun), null, gez) { Yil = 2026, Ay = 8 };
        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.Equal((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), api.KasaDokumuCagrilari.Single());
        Assert.Equal(8, vm.KasaAdimlari.Count);
        Assert.StartsWith("Kasa 1.000,00 ₺ ile açıldı", vm.KasaOzeti);
        Assert.Equal((2026, 8), api.AyKapanisiCagrilari.Single());
        Assert.Equal((2026, 8), api.HedefButceCagrilari.Single());

        Assert.Equal(2, vm.KanalSatirlari.Count);
        var m = vm.KanalSatirlari[0];
        Assert.True(m.HedefVar);
        Assert.Equal("Hedef 50.000,00 ₺ · gerçekleşen 40.000,00 ₺ · %80,0", m.HedefMetni);
        Assert.Equal(0.8, m.HedefOrani, 3);
        Assert.False(vm.KanalSatirlari[1].HedefVar);
        Assert.Equal(new[] { "Gelen", "Cari", "Sabit gider", "Kredi kartı (geçen ay)", "Ortak pay" }, m.Rakamlar.Select(r => r.Etiket));
        Assert.Equal(new[] { "Ortak pay" }, vm.KanalSatirlari[1].Rakamlar.Select(r => r.Etiket));

        // Bütçe kartı: bütçesi/şablonu/gerçekleşeni olan kalemler.
        Assert.True(vm.ButceVar);
        var kira = Assert.Single(vm.ButceSatirlari);
        Assert.Equal(("Kira", "42.000,00 ₺", "45.000,00 ₺", "%107,1", "Şablon 40.000,00 ₺", true),
            (kira.Kalem, kira.ButceMetni, kira.GerceklesenMetni, kira.YuzdeMetni, kira.SablonMetni, kira.Asti));

        // Satıra dokunmak: kanalın o ayki tüm işlemleri; K.K rakamı (kart kuralı) geçen aya iner.
        await vm.IslemlereGitCommand.ExecuteAsync(m);
        await vm.IslemlereGitCommand.ExecuteAsync(m.Rakamlar[2]);
        await vm.IslemlereGitCommand.ExecuteAsync(m.Rakamlar[3]);
        await vm.IslemlereGitCommand.ExecuteAsync(m.Rakamlar[4]);
        await vm.IslemlereGitCommand.ExecuteAsync(m.Rakamlar[0]);                  // gelen: iniş yok
        await vm.IslemlereGitCommand.ExecuteAsync(vm.KasaAdimlari[3]);
        Assert.Equal(new[]
        {
            "//islemler?baslangic=2026-08-01&bitis=2026-08-31&kanal=MEZAT",
            "//islemler?baslangic=2026-08-01&bitis=2026-08-31&kanal=MEZAT&tip=SabitGider",
            "//islemler?baslangic=2026-07-01&bitis=2026-07-31&kanal=MEZAT&tip=KrediKarti",
            "//islemler?baslangic=2026-08-01&bitis=2026-08-31&kanal=Ortak&tip=Nakit",
            "//islemler?baslangic=2026-08-01&bitis=2026-08-31&kanal=MEZAT&tip=Cari",
        }, gez.Rotalar);

        await vm.KasaDokumunuAcCommand.ExecuteAsync(null);
        await vm.HedefleriDuzenleCommand.ExecuteAsync(null);
        Assert.Equal("kasadokumu?baslangic=2026-08-01&bitis=2026-08-31", gez.Rotalar[^2]);
        Assert.Equal("hedefbutce?yil=2026&ay=8", gez.Rotalar[^1]);
    }

    [Fact]
    public async Task Aylik_takip_donemi_olmayan_ayda_dokum_yerine_aciklama_hata_yok()
    {
        var api = new SahteApi
        {
            AylikRapor = new AylikRaporDto(2026, 1, new List<KanalAylikDto>()),
            KasaDokumuUret = (_, _) => Task.FromException<KasaDokumuDto>(new KasaApiException(HttpStatusCode.BadRequest, "Bu aralıkta takip dönemi yok.")),
        };
        var vm = new AylikViewModel(api, new SabitSaat(Bugun)) { Yil = 2026, Ay = 1 };
        await vm.YukleAsync();
        Assert.Null(vm.Hata);
        Assert.NotNull(vm.Rapor);
        Assert.False(vm.KasaDokumuVar);
        Assert.Empty(vm.KasaAdimlari);
        Assert.Equal("Bu aralıkta takip dönemi yok.", vm.KasaOzeti);
    }

    [Fact]
    public async Task Aylik_ek_hatasi_raporu_gizlemez()
    {
        var api = new SahteApi
        {
            AylikRapor = Rapor(),
            AyKapanisiHatasi = new HttpRequestException("ağ"),
        };
        var vm = new AylikViewModel(api, new SabitSaat(Bugun)) { Yil = 2026, Ay = 8 };
        await vm.YukleAsync();
        Assert.NotNull(vm.Rapor);
        Assert.Equal(2, vm.KanalSatirlari.Count);
        Assert.Null(vm.Kapanis);
        Assert.Equal(HataMesaji.Ulasilamadi, vm.Hata);
    }

    [Fact]
    public async Task Aylik_eski_ayin_gec_gelen_ekleri_yeni_ayi_ezmez()
    {
        var bekleyen = new TaskCompletionSource<KasaDokumuDto>();
        var api = new SahteApi
        {
            AylikUret = (y, a) => Task.FromResult(Rapor(a)),
            KasaDokumuUret = (b, s) => b.Month == 7 ? bekleyen.Task : Task.FromResult(Dokum(b, s)),
        };
        var vm = new AylikViewModel(api, new SabitSaat(Bugun)) { Yil = 2026, Ay = 7 };
        var eski = vm.YukleAsync();
        vm.Ay = 8;
        await vm.YukleAsync();
        bekleyen.SetResult(Dokum(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)));
        await eski;
        Assert.Equal(new DateOnly(2026, 8, 1), vm.KasaDokumu!.Baslangic);
        Assert.Equal(8, vm.Rapor!.Ay);
    }

    [Fact]
    public async Task Hedef_baska_aya_aitse_kanal_satirinda_gosterilmez()
    {
        var api = new SahteApi { AylikRapor = Rapor(8), HedefButce = Hedef(7) };
        var vm = new AylikViewModel(api, new SabitSaat(Bugun)) { Yil = 2026, Ay = 8 };
        await vm.YukleAsync();
        Assert.All(vm.KanalSatirlari, k => Assert.False(k.HedefVar));
        Assert.False(vm.ButceVar);
    }

    // ---------------------------------------------------------------- 11 · Ay kilidi ve yayını

    [Fact]
    public async Task Ay_kilitle_kilidi_ac_yayinla_yalniz_editor()
    {
        var api = new SahteApi { AylikRapor = Rapor() };
        var vm = new AylikViewModel(api, new SabitSaat(Bugun)) { Yil = 2026, Ay = 8 };
        await vm.YukleAsync();
        Assert.Equal("Açık", vm.KapanisMetni);
        Assert.Equal("Yayınlanmadı", vm.YayinMetni);
        Assert.False(vm.KilitleGorunur);                        // izleyici

        await vm.AyiKilitleCommand.ExecuteAsync(null);
        Assert.Equal(HataMesaji.Yetkisiz, vm.Hata);
        Assert.Empty(api.AyEylemleri);

        vm.EditorMu = true;
        Assert.True(vm.KilitleGorunur);
        Assert.False(vm.KilidiAcGorunur);
        await vm.AyiKilitleCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal(("kilitle", 2026, 8), api.AyEylemleri.Single());
        Assert.True(vm.Kilitli);
        Assert.StartsWith("Kilitli · 1 Eyl 2026", vm.KapanisMetni);
        Assert.StartsWith("Ağustos 2026 kilitlendi.", vm.Bilgi);
        Assert.False(vm.KilitleGorunur);
        Assert.True(vm.KilidiAcGorunur);

        await vm.AyiYayinlaCommand.ExecuteAsync(null);
        Assert.StartsWith("Yayınlandı · ", vm.YayinMetni);
        Assert.Equal("Yeniden yayınla", vm.YayinDugmesiMetni);
        Assert.False(vm.DegisiklikUyarisi);

        await vm.AyKilidiniAcCommand.ExecuteAsync(null);
        Assert.False(vm.Kilitli);
        Assert.StartsWith("Ağustos 2026 kilidi açıldı.", vm.Bilgi);
    }

    [Fact]
    public async Task Bitmemis_ay_kilitlenemez_sunucu_hatasi_gosterilir()
    {
        var api = new SahteApi { AylikRapor = Rapor(9) };
        var vm = new AylikViewModel(api, new SabitSaat(Bugun)) { Yil = 2026, Ay = 9, EditorMu = true };
        await vm.YukleAsync();
        Assert.Equal("Açık · ay henüz bitmedi", vm.KapanisMetni);
        Assert.False(vm.KilitleGorunur);
        Assert.False(vm.YayinlaGorunur);                        // yalnız bitmiş ay yayınlanır

        api.AyEylemHatasi = new KasaApiException(HttpStatusCode.BadRequest, "Eylül 2026 henüz bitmedi; yalnız bitmiş bir ay kilitlenebilir.");
        await vm.AyiKilitleCommand.ExecuteAsync(null);
        Assert.Equal("Eylül 2026 henüz bitmedi; yalnız bitmiş bir ay kilitlenebilir.", vm.Hata);
        Assert.Null(vm.Bilgi);
    }

    [Fact]
    public async Task Yayindan_sonra_degisiklik_kirmizi_serit()
    {
        var api = new SahteApi { AylikRapor = Rapor() };
        api.YayinlananAylar.Add((2026, 8));
        api.AyFarklari = new List<AyFarkiDto> { new("MEZAT · Gelen", 30_000m, 32_000m), new("Kasa kapanışı", 5m, null) };
        api.AyDegisiklikleri = new List<DegisiklikDto>
        {
            new(77, new DateTime(2026, 9, 10, 9, 30, 0, DateTimeKind.Utc), "editor", "İşlem", 5, "Güncellendi", "Kira 45.000", null, null, false, null, false),
        };
        var vm = new AylikViewModel(api, new SabitSaat(Bugun)) { Yil = 2026, Ay = 8 };
        await vm.YukleAsync();
        Assert.True(vm.DegisiklikUyarisi);
        Assert.Equal(new[] { "MEZAT · Gelen: 30.000,00 → 32.000,00", "Kasa kapanışı: 5,00 → —" }, vm.AyFarklari.Select(f => f.Metin));
        Assert.False(vm.DokunmaNotu);
        Assert.True(vm.DokunanVar);
        var g = Assert.Single(vm.AyaDokunanlar);
        Assert.Equal("10.09.2026 09:30 · Editör · İşlem", g.Ayrinti);

        // Yeniden yayın farkları sıfırlar.
        vm.EditorMu = true;
        await vm.AyiYayinlaCommand.ExecuteAsync(null);
        Assert.False(vm.DegisiklikUyarisi);
        Assert.Empty(vm.AyFarklari);
        Assert.Empty(vm.AyaDokunanlar);
        Assert.False(vm.DokunanVar);
    }

    [Fact]
    public async Task Rakam_degismeden_dokunulan_kayit_kirmizi_serit_degil_notr_not()
    {
        // Yayından sonra yalnız notu değişen işlem ya da eklenen kasa sayımı: fark yok, şerit yok.
        var api = new SahteApi { AylikRapor = Rapor() };
        api.YayinlananAylar.Add((2026, 8));
        api.AyDegisiklikleri = new List<DegisiklikDto>
        {
            new(80, new DateTime(2026, 9, 11, 8, 0, 0, DateTimeKind.Utc), "editor", "Kasa sayımı", 2, "Eklendi", "31.08.2026 sayım", null, null, false, null, false),
        };
        var vm = new AylikViewModel(api, new SabitSaat(Bugun)) { Yil = 2026, Ay = 8 };
        await vm.YukleAsync();
        Assert.False(vm.DegisiklikUyarisi);
        Assert.True(vm.DokunmaNotu);
        Assert.Empty(vm.AyFarklari);
        Assert.Single(vm.AyaDokunanlar);

        // Yayınlanmamış ayda ikisi de yok.
        api.YayinlananAylar.Clear();
        await vm.YukleAsync();
        Assert.False(vm.DegisiklikUyarisi);
        Assert.False(vm.DokunmaNotu);
    }

    [Fact]
    public async Task Kilit_bilgisi_onceki_ve_sonraki_aylari_soyler()
    {
        var api = new SahteApi { AylikRapor = Rapor() };
        var vm = new AylikViewModel(api, new SabitSaat(Bugun)) { Yil = 2026, Ay = 8, EditorMu = true };
        await vm.YukleAsync();
        Assert.True(vm.YayinlaGorunur);                         // bitmiş ay
        await vm.AyiKilitleCommand.ExecuteAsync(null);
        Assert.Contains("önceki aylar da kilitlidir", vm.Bilgi);
        await vm.AyKilidiniAcCommand.ExecuteAsync(null);
        Assert.Contains("Sonraki aylar kilitliyse onların da kilidi açıldı", vm.Bilgi);
    }

    // ---------------------------------------------------------------- Haftalık

    private static HaftalikOzetDto Hafta(int gun) => new(
        new DonemDto(new DateOnly(2026, 8, gun), new DateOnly(2026, 8, gun + 6), 2026, 8),
        new List<KanalHaftalikDto> { new("MEZAT", 30_000m, 57_000m, -17_000m, -16_000m, 10_000m, 0m) },
        30_000m, 57_000m, -17_000m, -16_000m, 10_000m, 0m);

    [Fact]
    public async Task Haftalik_donem_secince_kanal_rakamlari_ve_dokum_acilir()
    {
        var api = new SahteApi { HaftalikListe = new List<HaftalikOzetDto> { Hafta(3), Hafta(10) }, KasaDokumuUret = (b, s) => Task.FromResult(Dokum(b, s)) };
        var gez = new SahteGezinti();
        var vm = new HaftalikViewModel(api, null, gez);
        await vm.YukleAsync();

        await vm.DonemSecCommand.ExecuteAsync(vm.Donemler[1]);
        Assert.True(vm.DetayVar);
        Assert.Equal("10 Ağu 2026 – 16 Ağu 2026", vm.DetayBaslik);
        Assert.Equal((new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 16)), api.KasaDokumuCagrilari.Single());
        var k = Assert.Single(vm.DetayKanallari);
        Assert.Equal((40_000m, 57_000m, -17_000m), (k.Gelen, k.Giden, k.Sonuc));      // gelen + çek tahsilatı
        Assert.Equal(8, vm.DetayAdimlari.Count);

        await vm.IslemlereGitCommand.ExecuteAsync(k);
        await vm.IslemlereGitCommand.ExecuteAsync(vm.DetayAdimlari[4]);
        await vm.KasaDokumunuAcCommand.ExecuteAsync(null);
        Assert.Equal(new[]
        {
            "//islemler?baslangic=2026-08-10&bitis=2026-08-16&kanal=MEZAT&tip=Cari",      // kanal sonucundaki giden yalnız Cari
            "//islemler?baslangic=2026-08-10&bitis=2026-08-16&kanal=MEZAT&tip=SabitGider",
            "kasadokumu?baslangic=2026-08-10&bitis=2026-08-16",
        }, gez.Rotalar);

        // Aynı döneme yeniden dokunmak kapatır.
        await vm.DonemSecCommand.ExecuteAsync(vm.Donemler[1]);
        Assert.False(vm.DetayVar);
        Assert.Empty(vm.DetayKanallari);
        Assert.Empty(vm.DetayAdimlari);
    }

    [Fact]
    public async Task Haftalik_hizli_secimde_eski_dokum_yenisini_ezmez()
    {
        var bekleyen = new TaskCompletionSource<KasaDokumuDto>();
        var api = new SahteApi
        {
            HaftalikListe = new List<HaftalikOzetDto> { Hafta(3), Hafta(10) },
            KasaDokumuUret = (b, s) => b.Day == 3 ? bekleyen.Task : Task.FromResult(Dokum(b, s)),
        };
        var vm = new HaftalikViewModel(api);
        await vm.YukleAsync();
        var eski = vm.DonemSecCommand.ExecuteAsync(vm.Donemler[0]);
        await vm.DonemSecCommand.ExecuteAsync(vm.Donemler[1]);
        bekleyen.SetResult(new KasaDokumuDto(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9), 0m, 0m, 0m, 0m, new List<KasaDokumAdimiDto>()));
        await eski;
        Assert.Equal(8, vm.DetayAdimlari.Count);
        Assert.Equal(new DateOnly(2026, 8, 10), vm.Detay!.Donem.Start);
    }

    // ---------------------------------------------------------------- Kasa dökümü sayfası

    private static List<DonemDto> Donemler() => new()
    {
        new(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9), 2026, 8),
        new(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 16), 2026, 8),
        new(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 2026, 9),
    };

    [Fact]
    public async Task Kasa_dokumu_sayfasi_ay_ve_hafta_kipi()
    {
        var api = new SahteApi { DonemlerListe = Donemler(), KasaDokumuUret = (b, s) => Task.FromResult(Dokum(b, s)) };
        var k = new SahteKaydedici();
        var gez = new SahteGezinti();
        var vm = new KasaDokumuViewModel(api, new SabitSaat(Bugun), k, gez);

        // Aylık'tan: ay aralığı → ay kipi.
        vm.AralikUygula(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        await vm.YukleAsync();
        Assert.True(vm.AyKipinde);
        Assert.Equal("Ağustos 2026", vm.AyEtiketi);
        Assert.Equal((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), api.KasaDokumuCagrilari[^1]);
        Assert.Equal(8, vm.Satirlar.Count);
        Assert.Equal("1 Ağu 2026 – 31 Ağu 2026", vm.AralikMetni);

        await vm.OncekiCommand.ExecuteAsync(null);
        Assert.Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)), api.KasaDokumuCagrilari[^1]);

        // Hafta kipi: bugünü içeren hafta seçilir; önceki hafta bir alttaki.
        await vm.SecKipCommand.ExecuteAsync(vm.KipCipleri.Single(c => c.Ad == KasaDokumuViewModel.HaftaKipi));
        Assert.False(vm.AyKipinde);
        Assert.Equal(new DateOnly(2026, 9, 21), vm.SeciliDonem!.Start);
        Assert.Equal((new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27)), api.KasaDokumuCagrilari[^1]);
        await vm.OncekiCommand.ExecuteAsync(null);
        await vm.SonYukleme!;
        Assert.Equal((new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 16)), api.KasaDokumuCagrilari[^1]);

        await vm.ExceleAktarCommand.ExecuteAsync(null);
        Assert.Equal((new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 16)), api.SonKasaDokumuCsv);
        Assert.NotNull(vm.AktarilanDosya);

        await vm.IslemlereGitCommand.ExecuteAsync(vm.Satirlar[3]);
        await vm.IslemlereGitCommand.ExecuteAsync(vm.Satirlar[0]);                     // açılış: iniş yok
        Assert.Equal("//islemler?baslangic=2026-08-10&bitis=2026-08-16&kanal=MEZAT&tip=Cari", Assert.Single(gez.Rotalar));
    }

    [Fact]
    public async Task Kasa_dokumu_haftadan_gelinince_o_hafta_secilir_takip_disi_aciklama()
    {
        var api = new SahteApi { DonemlerListe = Donemler(), KasaDokumuUret = (b, s) => Task.FromResult(Dokum(b, s)) };
        var vm = new KasaDokumuViewModel(api, new SabitSaat(Bugun));
        vm.AralikUygula(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9));
        await vm.YukleAsync();
        Assert.True(vm.HaftaKipinde);
        Assert.Equal(new DateOnly(2026, 8, 3), vm.SeciliDonem!.Start);
        Assert.Equal((new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9)), api.KasaDokumuCagrilari.Single());
        Assert.True(vm.KipCipleri.Single(c => c.Ad == KasaDokumuViewModel.HaftaKipi).Secili);

        api.KasaDokumuUret = (_, _) => Task.FromException<KasaDokumuDto>(new KasaApiException(HttpStatusCode.BadRequest, "Bu aralıkta takip dönemi yok."));
        await vm.SecKipCommand.ExecuteAsync(vm.KipCipleri.Single(c => c.Ad == KasaDokumuViewModel.AyKipi));
        Assert.Null(vm.Hata);
        Assert.False(vm.DokumVar);
        Assert.Empty(vm.Satirlar);
        Assert.Equal("Bu aralıkta takip dönemi yok.", vm.BosMesaji);
    }

    [Fact]
    public void Kasa_dokumu_rota_sorgusu()
    {
        Assert.Equal("kasadokumu?baslangic=2026-08-01&bitis=2026-08-31", Rotalar.KasaDokumuRotasi(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
        var q = new Dictionary<string, object> { ["baslangic"] = "2026-08-01", ["bitis"] = "2026-08-31" };
        Assert.Equal((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), Rotalar.Aralik(q));
        Assert.Null(Rotalar.Aralik(new Dictionary<string, object> { ["baslangic"] = "x", ["bitis"] = "2026-08-31" }));
        Assert.Equal(8, Rotalar.SayiDegeri(new Dictionary<string, object> { ["ay"] = "8" }, "ay"));
        Assert.Null(Rotalar.SayiDegeri(new Dictionary<string, object> { ["ay"] = "-8" }, "ay"));
    }
}
