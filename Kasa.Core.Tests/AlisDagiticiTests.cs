using Kasa.Core;

namespace Kasa.Core.Tests;

public class AlisDagiticiTests
{
    [Fact]
    public void Odeme_orantili_dagilir_ve_kanal_kimligine_gore_siralanir()
    {
        AlisKanalPayi[] paylar = [new(30, 2m), new(10, 5m), new(20, 3m)];

        var sonuc = AlisDagitici.Dagit(paylar, 0m, 4m);

        Assert.Equal(new[] { new AlisKanalPayi(10, 2m), new(20, 1.2m), new(30, 0.8m) }, sonuc);
        Assert.Equal(4m, sonuc.Sum(p => p.Tutar));
    }

    [Fact]
    public void Esit_agirliklarda_artik_kurus_kucuk_kimlikten_baslar()
    {
        AlisKanalPayi[] paylar = [new(30, 0.01m), new(10, 0.01m), new(20, 0.01m)];

        for (int i = 0; i < 3; i++)
        {
            var sonuc = AlisDagitici.Dagit(paylar, i / 100m, 0.01m);
            Assert.Equal((i + 1) * 10, sonuc.Single(p => p.Tutar == 0.01m).KanalId);
            Assert.Equal(2, sonuc.Count(p => p.Tutar == 0m));
        }
    }

    [Fact]
    public void Alabama_paradoksu_orneginde_ek_odeme_negatif_pay_uretmez()
    {
        AlisKanalPayi[] paylar = [new(1, 15m), new(2, 15m), new(3, 9m), new(4, 5m), new(5, 5m), new(6, 2m)];

        var ilk = AlisDagitici.Dagit(paylar, 0m, 0.25m);
        var ek = AlisDagitici.Dagit(paylar, 0.25m, 0.01m);

        Assert.Equal(new[] { 0.08m, 0.08m, 0.04m, 0.02m, 0.02m, 0.01m }, ilk.Select(p => p.Tutar));
        Assert.Equal(3, ek.Single(p => p.Tutar == 0.01m).KanalId);
        Assert.All(ek, p => Assert.True(p.Tutar >= 0m));
        Assert.Equal(0.01m, ek.Sum(p => p.Tutar));
    }

    [Fact]
    public void Kucuk_tum_agirliklar_kurus_kurus_referans_hesapla_ayni_sonucu_verir()
    {
        for (int a = 1; a <= 6; a++)
        for (int b = 1; b <= 6; b++)
        for (int c = 1; c <= 6; c++)
        {
            int[] agirliklar = [a, b, c];
            int[] kumulatif = [0, 0, 0];
            AlisKanalPayi[] paylar = [new(3, c / 100m), new(1, a / 100m), new(2, b / 100m)];
            for (int onceki = 0; onceki < a + b + c; onceki++)
            {
                // Bağımsız referans: her kuruş için ağırlık/(önceki pay+1) en büyük olanı seç.
                int secilen = 0;
                for (int i = 1; i < 3; i++)
                    if (agirliklar[i] * (kumulatif[secilen] + 1) > agirliklar[secilen] * (kumulatif[i] + 1))
                        secilen = i;
                kumulatif[secilen]++;

                var sonuc = AlisDagitici.Dagit(paylar, onceki / 100m, 0.01m);
                Assert.Equal(secilen + 1, sonuc.Single(p => p.Tutar == 0.01m).KanalId);
                Assert.All(sonuc, p => Assert.True(p.Tutar >= 0m));
                Assert.Equal(0.01m, sonuc.Sum(p => p.Tutar));
            }
            Assert.Equal(agirliklar, kumulatif);
        }
    }

    [Fact]
    public void Parca_parca_odemeler_tek_odeme_ile_ayni_paylari_verir_ve_son_odeme_tamamlar()
    {
        AlisKanalPayi[] paylar = [new(8, 5.17m), new(2, 2.03m), new(4, 3.01m)];
        decimal[] odemeler = [0.01m, 0.07m, 2.81m, 3.16m, 4.16m];
        var biriken = new Dictionary<int, decimal> { [2] = 0m, [4] = 0m, [8] = 0m };
        decimal onceki = 0m;

        foreach (decimal odeme in odemeler)
        {
            var parca = AlisDagitici.Dagit(paylar, onceki, odeme);
            Assert.Equal(odeme, parca.Sum(p => p.Tutar));
            Assert.All(parca, p => Assert.True(p.Tutar >= 0m));
            foreach (var p in parca) biriken[p.KanalId] += p.Tutar;
            onceki += odeme;
            var tek = AlisDagitici.Dagit(paylar.Reverse().ToArray(), 0m, onceki);
            Assert.All(tek, p => Assert.Equal(p.Tutar, biriken[p.KanalId]));
        }
        Assert.Equal(paylar.Sum(p => p.Tutar), onceki);
        Assert.All(paylar, p => Assert.Equal(p.Tutar, biriken[p.KanalId]));
    }

    [Fact]
    public void Cok_buyuk_tutarlarda_kurus_kaybolmaz_ve_tutar_kadar_dongu_yapilmaz()
    {
        AlisKanalPayi[] paylar =
        [
            new(1, 300_000_000_000_000_000_000_000_000m),
            new(2, 200_000_000_000_000_000_000_000_000m),
            new(3, 200_000_000_000_000_000_000_000_000m),
        ];
        const decimal onceki = 350_000_000_000_000_000_000_000_000m;

        var sonuc = AlisDagitici.Dagit(paylar, onceki, 0.01m);

        Assert.Equal(0.01m, sonuc.Sum(p => p.Tutar));
        Assert.All(sonuc, p => Assert.True(p.Tutar >= 0m));
    }

    [Fact]
    public void Kurus_hassasiyetinin_ust_sinirinda_son_kurus_tam_dagilir()
    {
        decimal toplam = decimal.MaxValue / 100m;

        var sonuc = AlisDagitici.Dagit([new(1, toplam)], toplam - 0.01m, 0.01m);

        Assert.Equal(0.01m, sonuc.Single().Tutar);
    }

    [Fact]
    public void Bos_null_ve_tekrarlanan_kanallar_reddedilir()
    {
        Assert.Throws<ArgumentNullException>(() => AlisDagitici.Dagit(null!, 0m, 1m));
        Assert.Throws<ArgumentException>(() => AlisDagitici.Dagit([], 0m, 1m));
        Assert.Throws<ArgumentException>(() => AlisDagitici.Dagit([new(1, 1m), new(1, 1m)], 0m, 1m));
        Assert.Throws<ArgumentException>(() => AlisDagitici.Dagit([new(0, 1m)], 0m, 1m));
        Assert.Throws<ArgumentException>(() => AlisDagitici.Dagit([null!], 0m, 1m));
    }

    [Fact]
    public void Gecersiz_pay_ve_odeme_tutarlari_reddedilir()
    {
        foreach (decimal gecersiz in new[] { -1m, 0m, 0.001m, decimal.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AlisDagitici.Dagit([new(1, gecersiz)], 0m, 1m));
            Assert.Throws<ArgumentOutOfRangeException>(() => AlisDagitici.Dagit([new(1, 1m)], 0m, gecersiz));
        }
        foreach (decimal gecersiz in new[] { -1m, 0.001m, decimal.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => AlisDagitici.Dagit([new(1, 1m)], gecersiz, 0.01m));

        Assert.Throws<ArgumentOutOfRangeException>(() => AlisDagitici.Dagit([new(1, 1m)], 0.99m, 0.02m));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlisDagitici.Dagit([new(1, 1m)], 1m, 0.01m));
        decimal sinir = decimal.MaxValue / 100m;
        Assert.Throws<ArgumentOutOfRangeException>(() => AlisDagitici.Dagit([new(1, sinir), new(2, sinir)], 0m, 1m));
    }
}
