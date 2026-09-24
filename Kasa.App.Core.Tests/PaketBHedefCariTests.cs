using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket B · 05 hedef/bütçe düzenleme ve 07 cari özeti.</summary>
public class PaketBHedefCariTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    private static HedefButceDto Hedef() => new(2026, 8,
        new List<KanalHedefDto>
        {
            new(1, "MEZAT", true, 50_000m, 40_000m, 80.0m),
            new(2, "PERAKENDE", true, null, 5_000m, null),
            new(3, "ESKİ", false, null, 0m, null),               // pasif, değeri yok → gizli
            new(4, "KAPALI", false, 1_000m, 0m, 0m),             // pasif ama hedefi var → görünür
        },
        new List<GiderButceDto>
        {
            new(802, "Kira", true, 42_000m, 45_000m, 107.1m, 40_000m),
            new(801, "SGK", true, null, 0m, null, null),
        });

    [Fact]
    public async Task Hedef_butce_yukler_pasifleri_degerliyse_gosterir()
    {
        var api = new SahteApi { HedefButce = Hedef() };
        var vm = new HedefButceViewModel(api, new SabitSaat(Bugun));
        vm.AyUygula(2026, 8);
        await vm.YukleAsync();
        Assert.Equal((2026, 8), api.HedefButceCagrilari.Single());
        Assert.Equal("Ağustos 2026", vm.AyEtiketi);
        Assert.Equal(new[] { "MEZAT", "PERAKENDE", "KAPALI" }, vm.Kanallar.Select(k => k.Ad));
        Assert.Equal(new[] { "50000", "", "1000" }, vm.Kanallar.Select(k => k.Metin));
        Assert.Equal(("Gerçekleşen 40.000,00 ₺", "%80,0"), (vm.Kanallar[0].GerceklesenMetni, vm.Kanallar[0].YuzdeMetni));
        Assert.Equal("Tekrarlayan şablon 40.000,00 ₺", vm.Kalemler[0].SablonMetni);
        Assert.False(vm.Kalemler[1].SablonVar);

        vm.AyUygula(2026, 13);                                      // bozuk sorgu yok sayılır
        Assert.Equal(8, vm.Ay);
    }

    [Fact]
    public async Task Kaydet_yalniz_degisenleri_gonderir_bos_siler()
    {
        var api = new SahteApi { HedefButce = Hedef() };
        var vm = new HedefButceViewModel(api, new SabitSaat(Bugun)) { EditorMu = true };
        vm.AyUygula(2026, 8);
        await vm.YukleAsync();

        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(HedefButceViewModel.DegisiklikYok, vm.Bilgi);
        Assert.Null(api.SonHedefButceKaydet);

        vm.Kanallar[0].Metin = "50.000";                            // aynı değer, farklı yazım → gönderilmez
        vm.Kanallar[1].Metin = "20.000,50";
        vm.Kanallar[2].Metin = "";                                  // hedef silinir
        vm.Kalemler[1].Metin = "0";                                 // 0 bütçe geçerli
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        var g = api.SonHedefButceKaydet!;
        Assert.Equal(new DateOnly(2026, 8, 1), g.Ay);
        Assert.Equal(new[] { new HedefYaz(2, 20_000.50m), new HedefYaz(4, null) }, g.Kanallar);
        Assert.Equal(new[] { new ButceYaz(801, 0m) }, g.Kalemler);
        Assert.Equal("Ağustos 2026 hedef ve bütçeleri kaydedildi (3 satır).", vm.Bilgi);
        Assert.Equal("20000,5", vm.Kanallar[1].Metin);               // sunucu yanıtından yeniden kuruldu
        Assert.Equal("%25,0", vm.Kanallar[1].YuzdeMetni);
    }

    [Fact]
    public async Task Kaydet_gecersiz_tutarda_hic_gondermez_izleyici_yazamaz()
    {
        var api = new SahteApi { HedefButce = Hedef() };
        var vm = new HedefButceViewModel(api, new SabitSaat(Bugun)) { EditorMu = true };
        vm.AyUygula(2026, 8);
        await vm.YukleAsync();
        vm.Kanallar[1].Metin = "10.000";
        vm.Kalemler[0].Metin = "12,345";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal("Kira: " + ParaGiris.HataKurus, vm.Hata);
        Assert.Null(api.SonHedefButceKaydet);

        vm.Kalemler[0].Metin = "-5";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal("Kira: " + ParaGiris.HataNegatif, vm.Hata);

        vm.EditorMu = false;
        vm.Kalemler[0].Metin = "5";
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(HataMesaji.Yetkisiz, vm.Hata);
        await vm.GecenAydanKopyalaCommand.ExecuteAsync(null);
        Assert.Equal(HataMesaji.Yetkisiz, vm.Hata);
        Assert.Null(api.SonHedefButceKaydet);
        Assert.Null(api.SonKopyala);
    }

    [Fact]
    public async Task Gecen_aydan_kopyala_bilgi_verir_ve_yeniler()
    {
        var api = new SahteApi { HedefButce = Hedef() };
        var vm = new HedefButceViewModel(api, new SabitSaat(Bugun)) { EditorMu = true };
        vm.AyUygula(2026, 8);
        await vm.YukleAsync();
        await vm.GecenAydanKopyalaCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal(new DateOnly(2026, 8, 1), api.SonKopyala);
        Assert.Equal("Geçen aydan 2 satır kopyalandı; bu ayda zaten olan 1 satır korundu.", vm.Bilgi);
        Assert.Equal(2, api.HedefButceCagrilari.Count);

        api.KopyalaHatasi = new KasaApiException(System.Net.HttpStatusCode.BadRequest, "Temmuz 2026 için hedef ya da bütçe yok; kopyalanacak bir şey yok.");
        await vm.GecenAydanKopyalaCommand.ExecuteAsync(null);
        Assert.Equal("Temmuz 2026 için hedef ya da bütçe yok; kopyalanacak bir şey yok.", vm.Hata);
        Assert.Null(vm.Bilgi);

        await vm.OncekiAyCommand.ExecuteAsync(null);
        Assert.Equal((2026, 7), api.HedefButceCagrilari[^1]);
    }

    // ---------------------------------------------------------------- 07 · Cari özeti

    [Fact]
    public async Task Cari_ozeti_ad_secince_yil_ozetini_yukler()
    {
        var aylar = Enumerable.Range(1, 12).Select(a => a == 8
            ? new CariOzetiAyDto(8, 10_000m, 2_000m, 5_000m, 17_000m, 3, null, null)
            : new CariOzetiAyDto(a, 0m, 0m, 0m, 0m, 0, null, null)).ToList();
        var api = new SahteApi
        {
            CarilerListe = new List<CariDto> { new(1, "Zeynep Ltd", true), new(2, "Ahmet", true), new(3, "Eski", false) },
            CariOzeti = new CariOzetiDto("Ahmet", 2026, "cari", aylar, 17_000m, null),
        };
        var vm = new CariOzetiViewModel(api, new SabitSaat(Bugun));
        await vm.YukleAsync();
        Assert.Equal(new[] { "Ahmet", "Zeynep Ltd", "Eski" }, vm.Adlar);
        Assert.Empty(api.CariOzetiCagrilari);                         // ad seçilmeden istek yok
        Assert.False(vm.OzetVar);

        vm.SeciliAd = "Ahmet";
        await vm.SonYukleme!;
        Assert.Equal(("Ahmet", 2026, CariOzetiTuru.Cari), api.CariOzetiCagrilari.Single());
        Assert.Equal(12, vm.Aylar.Count);
        var agu = vm.Aylar[7];
        Assert.Equal(("Ağustos", "10.000,00", "2.000,00", "5.000,00", "17.000,00", "3 kayıt"),
            (agu.AyAdi, agu.NakitMetni, agu.KrediKartiMetni, agu.CekMetni, agu.ToplamMetni, agu.AdetMetni));
        Assert.True(vm.Aylar[0].Bos);
        Assert.Equal("—", vm.Aylar[0].ToplamMetni);
        Assert.Equal("17.000,00 ₺", vm.ToplamMetni);

        await vm.OncekiYilCommand.ExecuteAsync(null);
        Assert.Equal(("Ahmet", 2025, CariOzetiTuru.Cari), api.CariOzetiCagrilari[^1]);

        // Sayfa yeniden açılınca seçim korunur.
        await vm.YukleAsync();
        Assert.Equal("Ahmet", vm.SeciliAd);
        Assert.Equal(("Ahmet", 2025, CariOzetiTuru.Cari), api.CariOzetiCagrilari[^1]);
    }

    [Fact]
    public async Task Kalem_ozeti_sablonu_ve_karari_gosterir()
    {
        var aylar = Enumerable.Range(1, 12).Select(a => a switch
        {
            7 => new CariOzetiAyDto(7, 40_000m, 0m, 0m, 40_000m, 1, 40_000m, "Girildi"),
            8 => new CariOzetiAyDto(8, 45_000m, 0m, 0m, 45_000m, 1, 40_000m, "Girildi"),
            9 => new CariOzetiAyDto(9, 0m, 0m, 0m, 0m, 0, 40_000m, "Atlandı"),
            _ => new CariOzetiAyDto(a, 0m, 0m, 0m, 0m, 0, null, null),
        }).ToList();
        var api = new SahteApi { CariOzeti = new CariOzetiDto("Kira", 2026, "kalem", aylar, 85_000m, 120_000m) };
        var vm = new CariOzetiViewModel(api, new SabitSaat(Bugun));
        await vm.YukleAsync();
        await vm.SecTurCommand.ExecuteAsync(vm.TurCipleri.Single(c => c.Ad == CariOzetiViewModel.KalemCipi));
        Assert.True(vm.KalemMi);
        Assert.Equal("Sabit gider kalemi", vm.AdEtiketi);
        Assert.Contains("Kira", vm.Adlar);                            // gider kalemleri listesi
        Assert.DoesNotContain("MEZAT alış", vm.Adlar);

        vm.SeciliAd = "Kira";
        await vm.SonYukleme!;
        Assert.Equal(("Kira", 2026, CariOzetiTuru.Kalem), api.CariOzetiCagrilari.Single());
        Assert.Equal(("40.000,00", "Girildi", false), (vm.Aylar[6].SablonMetni, vm.Aylar[6].KararMetni, vm.Aylar[6].SablondanFarkli));
        Assert.True(vm.Aylar[7].SablondanFarkli);                     // 45.000 girildi, şablon 40.000
        Assert.Equal("Atlandı", vm.Aylar[8].KararMetni);
        Assert.False(vm.Aylar[8].SablondanFarkli);
        Assert.Equal("Şablon toplamı 120.000,00 ₺", vm.SablonToplamMetni);

        // Türe dönünce seçim temizlenir, istek atılmaz.
        await vm.SecTurCommand.ExecuteAsync(vm.TurCipleri.Single(c => c.Ad == CariOzetiViewModel.CariCipi));
        Assert.Null(vm.SeciliAd);
        Assert.False(vm.OzetVar);
        Assert.Single(api.CariOzetiCagrilari);
    }
}
