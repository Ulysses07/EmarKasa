using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>Kart son ödeme tarihi (bulgu 3) ve kart durumu (bulgu 13) kilit testleri.</summary>
public class KartSonOdemeTests
{
    [Theory]
    [InlineData("2026-09-15", 25, "2026-09-25")]   // normal: ödeme günü kesimden sonra → aynı ay
    [InlineData("2026-09-25", 5, "2026-10-05")]    // ödeme günü küçük → ertesi ay
    [InlineData("2026-09-15", 15, "2026-10-15")]   // eşit gün → ertesi ay (kesimle aynı gün DEĞİL)
    [InlineData("2026-12-20", 10, "2027-01-10")]   // yıl sarması
    [InlineData("2027-02-28", 29, "2027-03-29")]   // kesim 30 → 28 Şubat; ödeme 29 → 28 Şubat'a çökmez
    [InlineData("2027-02-28", 30, "2027-03-30")]   // kesim 31 → 28 Şubat; ödeme 30
    [InlineData("2028-02-29", 29, "2028-03-29")]   // artık yıl
    [InlineData("2027-01-30", 29, "2027-02-28")]   // Ocak ekstresinin son ödemesi Şubat sonuna kırpılır
    public void SonOdeme_kesimden_sonraki_ilk_odeme_gunudur(string kesim, int sonOdemeGunu, string beklenen)
        => Assert.Equal(DateOnly.Parse(beklenen), KartDonem.SonOdeme(DateOnly.Parse(kesim), sonOdemeGunu));

    [Fact]
    public void Esit_gunlerde_odeme_bekliyor_her_gun_acik_kalmaz()
    {
        // kesim 15 / son ödeme 15: eski hesapla son ödeme = kesim günü → şerit her gün açıktı.
        int acikGun = 0, toplam = 0;
        for (var gun = new DateOnly(2026, 9, 1); gun <= new DateOnly(2026, 11, 30); gun = gun.AddDays(1))
        {
            var e = KartDonem.AcikEkstre(15, 15, gun);
            toplam++;
            if (gun >= e.SonOdeme.AddDays(-3)) acikGun++;
        }
        Assert.Equal(3 * 4, acikGun);   // her ay son ödemeden 3 gün önce + son ödeme günü
        Assert.True(acikGun < toplam);
    }

    [Fact]
    public void AcikEkstre_son_odeme_gunu_yeni_kesimle_ayni_gunse_eski_ekstreyi_dondurur()
    {
        // 15 Ekim hem Eylül ekstresinin son ödemesi hem yeni kesim: bugün ödenecek olan Eylül ekstresi.
        Assert.Equal(new KartEkstreTarihi(new DateOnly(2026, 9, 15), new DateOnly(2026, 10, 15)),
            KartDonem.AcikEkstre(15, 15, new DateOnly(2026, 10, 15)));
        Assert.Equal(new KartEkstreTarihi(new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 15)),
            KartDonem.AcikEkstre(15, 15, new DateOnly(2026, 10, 16)));
        // Şubat: kesim 30 / ödeme 29 — Şubat ekstresinin son ödemesi 29 Mart.
        Assert.Equal(new KartEkstreTarihi(new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 29)),
            KartDonem.AcikEkstre(30, 29, new DateOnly(2027, 3, 1)));
    }

    // Kaba kuvvet referansı: d günü, "gun" için aday mı? (ay kısa ise ay sonu)
    private static bool Aday(DateOnly d, int gun) => d.Day == Math.Min(gun, DateTime.DaysInMonth(d.Year, d.Month));

    [Fact]
    public void Ozellik_SonKesim_ve_SonOdeme_kaba_kuvvet_referansiyla_ayni()
    {
        int karsilastirma = 0;
        // SonKesim: 2023–2029 her gün × kesim günü 1..31 (≈79 bin karşılaştırma).
        for (var bugun = new DateOnly(2023, 1, 1); bugun <= new DateOnly(2029, 12, 31); bugun = bugun.AddDays(1))
            for (int gun = 1; gun <= 31; gun++)
            {
                var r = bugun;
                while (!Aday(r, gun)) r = r.AddDays(-1);
                Assert.Equal(r, KartDonem.SonKesim(gun, bugun));
                karsilastirma++;
            }

        // SonOdeme: her olası kesim tarihi × ödeme günü 1..31: kesimden KESİNLİKLE sonraki ilk aday.
        for (int yil = 2023; yil <= 2029; yil++)
            for (int ay = 1; ay <= 12; ay++)
                for (int kesimGunu = 1; kesimGunu <= 31; kesimGunu++)
                {
                    var kesim = new DateOnly(yil, ay, Math.Min(kesimGunu, DateTime.DaysInMonth(yil, ay)));
                    for (int odemeGunu = 1; odemeGunu <= 31; odemeGunu++)
                    {
                        var r = kesim.AddDays(1);
                        while (!Aday(r, odemeGunu)) r = r.AddDays(1);
                        var sonOdeme = KartDonem.SonOdeme(kesim, odemeGunu);
                        Assert.Equal(r, sonOdeme);
                        Assert.True(sonOdeme > kesim && sonOdeme <= kesim.AddDays(62));
                        karsilastirma++;
                    }
                }

        // AcikEkstre değişmezleri: Kesim ≤ bugün ≤ SonOdeme, SonOdeme = SonOdeme(Kesim) ve
        // Kesim, son kesim ya da ondan bir önceki kesimdir.
        for (var bugun = new DateOnly(2027, 1, 1); bugun <= new DateOnly(2028, 12, 31); bugun = bugun.AddDays(3))
            for (int kesimGunu = 1; kesimGunu <= 31; kesimGunu += 2)
                for (int odemeGunu = 1; odemeGunu <= 31; odemeGunu += 2)
                {
                    var e = KartDonem.AcikEkstre(kesimGunu, odemeGunu, bugun);
                    var son = KartDonem.SonKesim(kesimGunu, bugun);
                    var onceki = KartDonem.SonKesim(kesimGunu, son.AddDays(-1));
                    Assert.True(e.Kesim <= bugun);
                    Assert.Equal(KartDonem.SonOdeme(e.Kesim, odemeGunu), e.SonOdeme);
                    // Önceki ekstre yalnız son ödemesi henüz gelmediyse döner; aksi halde son kesim.
                    if (KartDonem.SonOdeme(onceki, odemeGunu) >= bugun) Assert.Equal(onceki, e.Kesim);
                    else Assert.Equal(son, e.Kesim);
                    // Daha eski hiçbir ekstrenin son ödemesi bugüne/ileriye kalmaz (atlanan ödeme yok).
                    var dahaEski = KartDonem.SonKesim(kesimGunu, onceki.AddDays(-1));
                    Assert.True(KartDonem.SonOdeme(dahaEski, odemeGunu) < bugun);
                    karsilastirma++;
                }

        Assert.True(karsilastirma > 150_000);
    }
}

public class KartDurumuTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 24);

    [Fact]
    public void Ileri_tarihli_harcama_guncel_borca_girmez_ekstre_negatif_olmaz()
    {
        // Denetim A04: kesim 15; 400 kesimden önce, 250 +60 gün ileri tarihli, 600 ödeme.
        var d = KartHesap.Durum(0m,
            [new KartHarcama(new DateOnly(2026, 9, 10), 400m), new KartHarcama(Bugun.AddDays(60), 250m)],
            [new KartOdeme(new DateOnly(2026, 9, 20), 600m)],
            kesimGunu: 15, Bugun);

        Assert.Equal(400m, d.HarcamaToplam);
        Assert.Equal(250m, d.GelecekHarcamaToplam);
        Assert.Equal(600m, d.OdemeToplam);
        Assert.Equal(-200m, d.GuncelBorc);   // 200 TL alacak
        Assert.Equal(0m, d.EkstreBorc);      // eskiden −200
    }

    [Fact]
    public void Fazla_odeme_bir_sonraki_ekstreyi_azaltir()
    {
        // Kesim 15 Eylül: 1000 borç; ödeme 1300; kesim sonrası 500 harcama.
        var harcama = new[] { new KartHarcama(new DateOnly(2026, 9, 10), 1_000m), new KartHarcama(new DateOnly(2026, 9, 18), 500m) };
        var odeme = new[] { new KartOdeme(new DateOnly(2026, 9, 20), 1_300m) };

        var eylul = KartHesap.Durum(0m, harcama, odeme, 15, Bugun);
        Assert.Equal(0m, eylul.EkstreBorc);
        Assert.Equal(200m, eylul.GuncelBorc);

        var ekimKesimSonrasi = KartHesap.Durum(0m, harcama, odeme, 15, new DateOnly(2026, 10, 16));
        Assert.Equal(200m, ekimKesimSonrasi.EkstreBorc);   // 500 − 300 fazla ödeme
    }

    [Fact]
    public void Ileri_tarihli_odeme_bugunku_borcu_dusurmez()
    {
        var d = KartHesap.Durum(1_000m, [], [new KartOdeme(Bugun.AddDays(5), 400m)], 15, Bugun);
        Assert.Equal(1_000m, d.GuncelBorc);
        Assert.Equal(1_000m, d.EkstreBorc);
        Assert.Equal(400m, d.GelecekOdemeToplam);
    }

    [Fact]
    public void Durum_eski_toplam_formulune_tarih_filtresi_disinda_esittir()
    {
        var d = KartHesap.Durum(1_000m, [new KartHarcama(new DateOnly(2026, 9, 1), 500m)],
            [new KartOdeme(new DateOnly(2026, 9, 2), 200m)], 15, Bugun);
        Assert.Equal(KartHesap.GuncelBorc(1_000m, 500m, 200m), d.GuncelBorc);
    }
}

public class ParaTests
{
    [Theory]
    [InlineData("100.005", "100.01")]
    [InlineData("-100.005", "-100.01")]
    [InlineData("0.004", "0.00")]
    public void Yuvarla_kurusa_sifirdan_uzaga(string girdi, string beklenen)
        => Assert.Equal(decimal.Parse(beklenen, System.Globalization.CultureInfo.InvariantCulture),
            Para.Yuvarla(decimal.Parse(girdi, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void KurusBol_long_sinirinin_cok_ustunde_tasmaz()
    {
        // long.MaxValue kuruş ≈ 9,2e16 TL; bunun çok üstünde (kuruş hassasiyeti korunarak).
        var toplam = 12_345_678_901_234_567_890_123.45m;
        var paylar = Para.KurusBol(toplam, 7);
        Assert.Equal(toplam, paylar.Sum());
        var negatif = Para.KurusBol(-toplam, 3);
        Assert.Equal(-toplam, negatif.Sum());
        // decimal sınırında da istisna atmaz.
        Assert.Equal(3, Para.KurusBol(decimal.MinValue, 3).Length);
    }

    [Fact]
    public void KurusBol_rastgele_toplamla_mutabik_ve_paylar_en_fazla_bir_kurus_farkli()
    {
        var rnd = new Random(20260924);
        for (int n = 0; n < 20_000; n++)
        {
            var toplam = (decimal)(rnd.NextDouble() * 2 - 1) * (decimal)Math.Pow(10, rnd.Next(0, 15));
            int parca = rnd.Next(1, 12);
            var paylar = Para.KurusBol(toplam, parca);
            Assert.Equal(Para.Yuvarla(toplam), paylar.Sum());
            Assert.True(paylar.Max() - paylar.Min() <= 0.01m);
            Assert.All(paylar, p => Assert.Equal(p, Para.Yuvarla(p)));
            // Artık kuruşlar ilk paylara verilir: paylar mutlak değerce artmayan sıradadır.
            for (int i = 1; i < parca; i++) Assert.True(Math.Abs(paylar[i - 1]) >= Math.Abs(paylar[i]));
        }
    }

    [Fact]
    public void KurusBol_gecersiz_parca_reddedilir()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Para.KurusBol(10m, 0));
}

public class AylikTakipBaslangicTests
{
    private static readonly Kanal[] UcKanal = { new("MEZAT"), new("PERAKENDE"), new("TOPTAN") };

    [Fact]
    public void Acik_takip_baslangici_verilince_oncesindeki_islem_sayilmaz()
    {
        // Dönem listesi daha erken başlasa bile açık takip başlangıcı esas alınır.
        var donemler = DonemUretici.Uret(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 9, 3), "Eski", 700m, "MEZAT", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 9, 16), "Yeni", 100m, "MEZAT", GiderTipi.Cari),
        };
        var ay = HesapMotoru.AylikHesapla(2026, 9, UcKanal, islemler, [], donemler, takipBaslangic: new DateOnly(2026, 9, 15));
        Assert.Equal(100m, ay.Kanallar.Single(k => k.Kanal == "MEZAT").CariGiden);
    }
}
