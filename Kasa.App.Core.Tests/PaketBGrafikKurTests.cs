using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Paket B · 04: grafik verisi biçimleme (birim dönüşümü, geçen yıl, "kur yok"), kur girişi ve kur tablosu.</summary>
public class PaketBGrafikKurTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    private static GrafikAyDto Ay(int yil, int ay, decimal mezat, decimal perakende, decimal? tufe = null, decimal? usd = null,
        decimal? eur = null, decimal? altin = null, bool takipOncesi = false)
        => new(yil, ay, takipOncesi, new List<GrafikKanalDto> { new("MEZAT", mezat, mezat / 2), new("PERAKENDE", perakende, -perakende) },
            tufe, usd, eur, altin);

    /// <summary>Eyl 2024 – Ağu 2026 (24 ay). Takip Ocak 2025'te başlar; kurlar yalnız bazı aylarda.</summary>
    private static GrafikDto Veri()
    {
        var aylar = new List<GrafikAyDto>();
        var t = new DateOnly(2024, 9, 1);
        for (int i = 0; i < 24; i++, t = t.AddMonths(1))
        {
            bool once = t < new DateOnly(2025, 1, 1);
            decimal? tufe = t.Year == 2025 && t.Month == 8 ? 3000m : t.Year == 2026 && t.Month == 8 ? 3600m : null;
            decimal? usd = t.Month == 8 ? (t.Year == 2026 ? 40m : 32m) : null;
            aylar.Add(Ay(t.Year, t.Month, once ? 0m : 1000m * (i + 1), once ? 0m : 100m, tufe, usd, t.Month == 8 ? 45m : null,
                t == new DateOnly(2026, 8, 1) ? 4000m : null, once));
        }
        return new GrafikDto(2026, 8, new List<string> { "MEZAT", "PERAKENDE" }, aylar);
    }

    [Fact]
    public void Donustur_her_birim_ve_eksik_kur_null()
    {
        var a = Ay(2026, 8, 0m, 0m, tufe: 3600m, usd: 40m, eur: 45m, altin: 4000m);
        Assert.Equal(12_000m, GrafikVerisi.Donustur(12_000m, a, GrafikBirimi.NominalTl, null));
        Assert.Equal(300m, GrafikVerisi.Donustur(12_000m, a, GrafikBirimi.Usd, null));
        Assert.Equal(266.67m, GrafikVerisi.Donustur(12_000m, a, GrafikBirimi.Eur, null));
        Assert.Equal(3m, GrafikVerisi.Donustur(12_000m, a, GrafikBirimi.AltinGram, null));
        Assert.Equal(14_400m, GrafikVerisi.Donustur(12_000m, a with { TufeEndeksi = 3000m }, GrafikBirimi.ReelTl, 3600m));

        var bos = Ay(2026, 7, 0m, 0m);
        foreach (var b in new[] { GrafikBirimi.ReelTl, GrafikBirimi.Usd, GrafikBirimi.Eur, GrafikBirimi.AltinGram })
            Assert.Null(GrafikVerisi.Donustur(12_000m, bos, b, 3600m));
        Assert.Null(GrafikVerisi.Donustur(12_000m, a, GrafikBirimi.ReelTl, null));            // referans TÜFE yok
        Assert.Null(GrafikVerisi.Donustur(12_000m, a with { UsdTry = 0m }, GrafikBirimi.Usd, null));
    }

    [Fact]
    public void Cubuklar_son_12_ay_gecen_yilla_ve_takip_oncesi()
    {
        var c = GrafikVerisi.Cubuklar(Veri(), GrafikVerisi.Toplam, GrafikOlcusu.Gelir, GrafikBirimi.NominalTl);
        Assert.Equal(12, c.Count);
        Assert.Equal((2025, 9), (c[0].Yil, c[0].Ay));
        Assert.Equal((2026, 8), (c[^1].Yil, c[^1].Ay));
        Assert.Equal("Ağu 26", c[^1].Etiket);
        Assert.Equal(24_000m + 100m, c[^1].Deger);                 // i = 23 → 24.000 MEZAT + 100 PERAKENDE
        Assert.Equal(12_000m + 100m, c[^1].GecenYil);              // Ağu 2025, i = 11
        Assert.Equal(99.2m, c[^1].DegisimYuzdesi);                // (24.100 − 12.100) / 12.100
        Assert.True(c[3].GecenYilTakipOncesi);                    // Ara 2025 ↔ Ara 2024 (takip öncesi)
        Assert.Null(c[3].GecenYil);
        Assert.Null(c[3].DegisimYuzdesi);
        Assert.False(c[4].GecenYilTakipOncesi);                   // Oca 2026 ↔ Oca 2025

        var m = GrafikVerisi.Cubuklar(Veri(), "MEZAT", GrafikOlcusu.AySonucu, GrafikBirimi.NominalTl);
        Assert.Equal(12_000m, m[^1].Deger);
    }

    [Fact]
    public void Kur_olmayan_ay_kur_yok_asla_tahmin_edilmez()
    {
        var usd = GrafikVerisi.Cubuklar(Veri(), GrafikVerisi.Toplam, GrafikOlcusu.Gelir, GrafikBirimi.Usd);
        Assert.Equal(602.5m, usd[^1].Deger);                     // 24.100 / 40
        Assert.Equal(378.13m, usd[^1].GecenYil);                 // 12.100 / 32
        Assert.All(usd.Take(11), x => { Assert.Null(x.Deger); Assert.True(x.KurYok); });
        Assert.Equal("kur yok", usd[0].DegerMetni(GrafikBirimi.Usd));
        Assert.Equal("602,50 USD", usd[^1].DegerMetni(GrafikBirimi.Usd));

        var altin = GrafikVerisi.Cubuklar(Veri(), GrafikVerisi.Toplam, GrafikOlcusu.Gelir, GrafikBirimi.AltinGram);
        Assert.Equal(6.03m, altin[^1].Deger);
        Assert.True(altin[^1].GecenYilKurYok);                   // Ağu 2025 altın yok
        Assert.Equal("kur yok", altin[^1].GecenYilMetni(GrafikBirimi.AltinGram));

        // Reel TL: referans, TÜFE'si olan en son ay (Ağu 2026 = 3600); Ağu 2025 (3000) değeri 1,2 katına çıkar.
        var reel = GrafikVerisi.Cubuklar(Veri(), GrafikVerisi.Toplam, GrafikOlcusu.Gelir, GrafikBirimi.ReelTl);
        Assert.Equal(24_100m, reel[^1].Deger);
        Assert.Equal(14_520m, reel[^1].GecenYil);
        Assert.True(reel[0].KurYok);

        var satirlar = GrafikVerisi.Satirlar(usd, GrafikBirimi.Usd);
        Assert.Equal("Ağustos 2026", satirlar[0].Etiket);          // en yeni önce
        Assert.Equal(("602,50 USD", "378,13 USD", "+59,3 %"), (satirlar[0].Deger, satirlar[0].GecenYil, satirlar[0].Degisim));
        Assert.Equal(("kur yok", "—"), (satirlar[1].Deger, satirlar[1].Degisim));
        Assert.True(satirlar[1].KurYok);
    }

    [Fact]
    public void Olcek_sifiri_icerir_ve_oran()
    {
        var c = GrafikVerisi.Cubuklar(Veri(), "PERAKENDE", GrafikOlcusu.AySonucu, GrafikBirimi.NominalTl);
        var (min, max) = GrafikVerisi.Olcek(c);
        Assert.Equal((-100m, 0m), (min, max));
        Assert.Equal(0d, GrafikVerisi.Oran(-100m, min, max));
        Assert.Equal(1d, GrafikVerisi.Oran(0m, min, max));
        Assert.Equal(0.5d, GrafikVerisi.Oran(50m, 0m, 100m));
        Assert.Equal(1d, GrafikVerisi.Oran(500m, 0m, 100m));          // taşma kırpılır
        Assert.Equal((0m, 1m), GrafikVerisi.Olcek(Array.Empty<GrafikCubugu>()));
    }

    [Fact]
    public async Task Grafikler_vm_yukler_birim_ve_kanal_degisince_yeniden_kurar()
    {
        var api = new SahteApi { Grafik = Veri() };
        var vm = new GrafiklerViewModel(api, new SabitSaat(Bugun));
        vm.Ay = 8;
        await vm.YukleAsync();
        Assert.Null(vm.Hata);
        Assert.Equal((2026, 8), api.SonGrafik);
        Assert.Equal(new[] { "Toplam", "MEZAT", "PERAKENDE" }, vm.KanalCipleri.Select(c => c.Ad));
        Assert.Equal(12, vm.Cubuklar.Count);
        Assert.Equal(12, vm.Satirlar.Count);
        Assert.Equal("Toplam · Gelir · TL", vm.Baslik);
        Assert.Equal(0, vm.KurYokSayisi);
        var surum = vm.CizimSurumu;

        vm.SecBirimCommand.Execute(vm.BirimCipleri.Single(c => c.Ad == "USD"));
        Assert.Equal(GrafikBirimi.Usd, vm.Birim);
        Assert.True(vm.CizimSurumu > surum);
        Assert.Equal(11 + 7, vm.KurYokSayisi);                   // bu yıl 11 ay + geçen yıl Oca–Tem 2025 (takip öncesi 4 ay sayılmaz)
        Assert.Contains("kur yok", vm.BirimNotu);
        Assert.Equal("kur yok", vm.Satirlar[1].Deger);

        vm.SecKanalCommand.Execute(vm.KanalCipleri.Single(c => c.Ad == "MEZAT"));
        vm.SecOlcuCommand.Execute(vm.OlcuCipleri.Single(c => c.Ad == "Ay sonucu"));
        Assert.Equal("MEZAT · Ay sonucu · USD", vm.Baslik);
        Assert.Equal("300,00 USD", vm.Satirlar[0].Deger);          // 12.000 / 40
        Assert.True(vm.KanalCipleri.Single(c => c.Ad == "MEZAT").Secili);

        vm.SecBirimCommand.Execute(vm.BirimCipleri.Single(c => c.Ad == "Reel TL"));
        Assert.StartsWith("Reel TL: Ağustos 2026 fiyatlarıyla", vm.BirimNotu);
    }

    // ---------------------------------------------------------------- kur girişi

    [Theory]
    [InlineData("41,2345", "41.2345")]
    [InlineData("41.25", "41.25")]
    [InlineData("41.2345", "41.2345")]
    [InlineData("4.321", "4321")]
    [InlineData("4.321,5", "4321.5")]
    [InlineData(" 3.600,12 ", "3600.12")]
    [InlineData("48,1234 TL", "48.1234")]
    [InlineData("0,0001", "0.0001")]
    public void Kur_girisi_gecerli(string metin, string beklenen)
    {
        var r = KurGiris.Ayristir(metin);
        Assert.True(r.Gecerli, r.Hata);
        Assert.Equal(decimal.Parse(beklenen, System.Globalization.CultureInfo.InvariantCulture), r.Deger);
    }

    [Theory]
    [InlineData("41,23456", KurGiris.HataOndalik)]
    [InlineData("41.23456", KurGiris.HataOndalik)]
    [InlineData("0", KurGiris.HataSifir)]
    [InlineData("0,00", KurGiris.HataSifir)]
    [InlineData("-41", KurGiris.HataSifir)]
    [InlineData("abc", KurGiris.HataGecersiz)]
    [InlineData("1,2,3", KurGiris.HataGecersiz)]
    [InlineData("12.34.5", KurGiris.HataGecersiz)]
    [InlineData("1234567890", KurGiris.HataCokBuyuk)]
    [InlineData("10000001", KurGiris.HataCokBuyuk)]
    public void Kur_girisi_gecersiz(string metin, string hata)
    {
        var r = KurGiris.Ayristir(metin);
        Assert.False(r.Gecerli);
        Assert.Equal(hata, r.Hata);
    }

    [Fact]
    public void Kur_girisi_bos_deger_yok_ve_bicimle_gidip_gelir()
    {
        Assert.Equal(new KurGirisSonucu(true, null, null), KurGiris.Ayristir("  "));
        Assert.Equal("", KurGiris.Bicimle(null));
        foreach (var d in new[] { 41.2345m, 4321.5m, 3600m, 0.0001m })
            Assert.Equal(d, KurGiris.Ayristir(KurGiris.Bicimle(d)).Deger);
    }

    // ---------------------------------------------------------------- Ayarlar · kur tablosu

    [Fact]
    public async Task Kur_tablosu_yuklenir_form_gecen_ayla_dolar_kaydeder()
    {
        var api = new SahteApi
        {
            KurlarListe = new List<KurDto> { new(new DateOnly(2026, 8, 1), 3600m, 40.5m, null, null), new(new DateOnly(2026, 7, 1), null, 39m, 44m, null) },
        };
        var vm = new AyarlarViewModel(api, new SabitSaat(Bugun));
        await vm.KurlariYukleAsync();
        Assert.Null(vm.Hata);
        Assert.Equal(new DateOnly(2026, 8, 1), vm.KurAy);
        Assert.Equal("Ağustos 2026", vm.KurAyEtiketi);
        Assert.Equal(("3600", "40,5", "", ""), (vm.KurTufe, vm.KurUsd, vm.KurEur, vm.KurAltin));
        Assert.Equal(2, vm.Kurlar.Count);
        Assert.Equal(("Ağustos 2026", "3.600", "40,5", "kur yok"), (vm.Kurlar[0].AyEtiketi, vm.Kurlar[0].TufeMetni, vm.Kurlar[0].UsdMetni, vm.Kurlar[0].EurMetni));

        vm.KurOncekiAyCommand.Execute(null);
        Assert.Equal(("", "39", "44"), (vm.KurTufe, vm.KurUsd, vm.KurEur));

        vm.KurTufe = "3.450,25";
        vm.KurAltin = "3.900";
        await vm.KurKaydetCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal(new KurDto(new DateOnly(2026, 7, 1), 3450.25m, 39m, 44m, 3900m), api.SonKurKaydet);
        Assert.Equal("Temmuz 2026 kurları kaydedildi.", vm.KurBilgi);
        Assert.Equal("3.900", vm.Kurlar.Single(k => k.Dto.Ay.Month == 7).AltinMetni);
    }

    [Fact]
    public async Task Kur_yuklemesi_ayarlar_hatasini_silmez()
    {
        // Sayfa açılışı: önce ayarlar (sunucu hatası), sonra kurlar (başarılı). Hata görünür kalmalı.
        var api = new SahteApi { YuklemeHatasi = new HttpRequestException("ağ"), AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 6, 1), 0m, true) };
        var vm = new AyarlarViewModel(api, new SabitSaat(Bugun));
        await vm.YukleAsync();
        Assert.Equal(HataMesaji.Ulasilamadi, vm.Hata);
        api.YuklemeHatasi = null;
        await vm.KurlariYukleAsync();
        Assert.Equal(HataMesaji.Ulasilamadi, vm.Hata);
        Assert.Equal(1, api.KurlarCagri);

        // Ayarlar yüklendiyse kur hatası gösterilir; ikisi de başarılıysa hata yok.
        await vm.YukleAsync();
        Assert.Null(vm.Hata);
        api.YuklemeHatasi = new KasaApiException(HttpStatusCode.InternalServerError, null);
        await vm.KurlariYukleAsync();
        Assert.Equal(HataMesaji.SunucuHatasi, vm.Hata);
        api.YuklemeHatasi = null;
        await vm.YukleAsync();
        await vm.KurlariYukleAsync();
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Kur_gecersiz_giris_gondermez_bos_satir_siler()
    {
        var api = new SahteApi { KurlarListe = new List<KurDto> { new(new DateOnly(2026, 8, 1), 3600m, null, null, null) } };
        var vm = new AyarlarViewModel(api, new SabitSaat(Bugun));
        await vm.KurlariYukleAsync();
        vm.KurUsd = "40,12345";
        await vm.KurKaydetCommand.ExecuteAsync(null);
        Assert.Equal("USD/TRY: " + KurGiris.HataOndalik, vm.Hata);
        Assert.Null(api.SonKurKaydet);

        vm.KurUsd = "";
        vm.KurTufe = "";
        await vm.KurKaydetCommand.ExecuteAsync(null);
        Assert.Equal(new KurDto(new DateOnly(2026, 8, 1), null, null, null, null), api.SonKurKaydet);
        Assert.Equal("Ağustos 2026 kur satırı silindi.", vm.KurBilgi);
        Assert.Empty(vm.Kurlar);
    }

    [Fact]
    public async Task Tcmb_doldur_usd_eur_yazar_tufe_ve_altin_metnine_dokunmaz()
    {
        var api = new SahteApi();
        var vm = new AyarlarViewModel(api, new SabitSaat(Bugun));
        await vm.KurlariYukleAsync();
        vm.KurTufe = "3600";           // kaydedilmemiş
        await vm.KurTcmbDoldurCommand.ExecuteAsync(null);
        Assert.Null(vm.Hata);
        Assert.Equal(new DateOnly(2026, 8, 1), api.SonTcmbAy);
        Assert.Equal(("3600", "41,25", "48,1234", ""), (vm.KurTufe, vm.KurUsd, vm.KurEur, vm.KurAltin));
        Assert.Equal("TCMB: 3 Ağu – 28 Ağu 2026 arası 20 iş günü döviz satış ortalaması kaydedildi. TÜFE ve gram altın elle girilir.", vm.KurBilgi);
        Assert.Equal("kur yok", Assert.Single(vm.Kurlar).TufeMetni);

        api.TcmbHatasi = new KasaApiException(HttpStatusCode.BadGateway, "TCMB'ye ulaşılamadı; biraz sonra tekrar deneyin.");
        await vm.KurTcmbDoldurCommand.ExecuteAsync(null);
        Assert.Equal("TCMB'ye ulaşılamadı; biraz sonra tekrar deneyin.", vm.Hata);
        Assert.Null(vm.KurBilgi);
    }
}
