using Kasa.Core;

namespace Kasa.Core.Tests;

/// <summary>Nakit tahmini (saf): gün gün kasa, bekleyen kalemler, en düşük gün, kart ve eski K.K kalemleri.</summary>
public class NakitTahminiTests
{
    // 24 Eylül 2026 Perşembe.
    private static readonly DateOnly Bugun = new(2026, 9, 24);

    private static TahminKalemi Kalem(DateOnly t, decimal tutar, TahminKalemTuru tur = TahminKalemTuru.TekrarlayanGider,
        string aciklama = "x", int? cekId = null) => new(t, tur, aciklama, tutar, cekId);

    // ------------------------------------------------------------------ Hesapla

    [Fact]
    public void Sifirinci_gun_bugundur_kasasi_baslangic_kasasidir_ve_kalemsizdir()
    {
        var s = NakitTahmini.Hesapla(Bugun, 12_345.67m, 30, [Kalem(Bugun.AddDays(1), -100m)]);

        Assert.Equal(31, s.Gunler.Count);
        var sifir = s.Gunler[0];
        Assert.Equal(Bugun, sifir.Tarih);
        Assert.Equal(12_345.67m, sifir.Kasa);
        Assert.Equal(12_345.67m, s.BaslangicKasa);
        Assert.Empty(sifir.Kalemler);
        Assert.Equal(0m, sifir.Giris);
        Assert.Equal(0m, sifir.Cikis);
        Assert.Equal(Bugun.AddDays(30), s.Gunler[^1].Tarih);
    }

    [Fact]
    public void Kasa_gun_gun_zincirlenir_giris_ve_cikis_ayri_toplanir()
    {
        var s = NakitTahmini.Hesapla(Bugun, 1_000m, 10,
        [
            Kalem(Bugun.AddDays(2), 500m, TahminKalemTuru.AlinanCek),
            Kalem(Bugun.AddDays(2), -200m),
            Kalem(Bugun.AddDays(5), -2_000m, TahminKalemTuru.KartOdemesi),
        ]);

        Assert.Equal(1_000m, s.Gunler[1].Kasa);
        var g2 = s.Gunler[2];
        Assert.Equal(500m, g2.Giris);
        Assert.Equal(200m, g2.Cikis);
        Assert.Equal(1_300m, g2.Kasa);
        Assert.Equal(2, g2.Kalemler.Count);
        Assert.Equal(1_300m, s.Gunler[4].Kasa);
        Assert.Equal(-700m, s.Gunler[5].Kasa);
        Assert.Equal(-700m, s.SonKasa);
        Assert.Equal(500m, s.ToplamGiris);
        Assert.Equal(2_200m, s.ToplamCikis);
    }

    [Fact]
    public void Bugun_vadeli_ve_gecikmis_kalemler_yarina_yazilir_gecikmis_isaretlenir()
    {
        var s = NakitTahmini.Hesapla(Bugun, 0m, 30,
        [
            Kalem(Bugun, -100m, aciklama: "bugün"),
            Kalem(Bugun.AddDays(-10), -50m, aciklama: "gecikmiş"),
            Kalem(Bugun.AddDays(1), -1m, aciklama: "yarın"),
        ]);

        Assert.Empty(s.Gunler[0].Kalemler);
        Assert.Equal(0m, s.Gunler[0].Kasa);
        var yarin = s.Gunler[1];
        Assert.Equal(3, yarin.Kalemler.Count);
        Assert.Equal(-151m, yarin.Kasa);
        // Asıl günü korunur; yalnız bugünden öncekiler gecikmiş sayılır. Sıra asıl güne göre.
        Assert.Equal(["gecikmiş", "bugün", "yarın"], yarin.Kalemler.Select(k => k.Aciklama));
        Assert.True(yarin.Kalemler[0].Gecikmis);
        Assert.Equal(Bugun.AddDays(-10), yarin.Kalemler[0].Tarih);
        Assert.False(yarin.Kalemler[1].Gecikmis);
        Assert.False(yarin.Kalemler[2].Gecikmis);
    }

    [Fact]
    public void Ufuk_disindaki_ve_sifir_tutarli_kalemler_atlanir_tutar_kurusa_yuvarlanir()
    {
        var s = NakitTahmini.Hesapla(Bugun, 0m, 30,
        [
            Kalem(Bugun.AddDays(30), -10.005m),
            Kalem(Bugun.AddDays(31), -99m),
            Kalem(Bugun.AddDays(3), 0m),
        ]);

        Assert.Single(s.Gunler.SelectMany(g => g.Kalemler));
        Assert.Equal(-10.01m, s.Gunler[30].Kalemler.Single().Tutar);
        Assert.Equal(-10.01m, s.SonKasa);
    }

    [Fact]
    public void En_dusuk_gun_bulunur_esitlikte_en_erken_gun()
    {
        var s = NakitTahmini.Hesapla(Bugun, 1_000m, 60,
        [
            Kalem(Bugun.AddDays(5), -13_500m),
            Kalem(Bugun.AddDays(10), 1_000m, TahminKalemTuru.AlinanCek),
            Kalem(Bugun.AddDays(20), -1_000m),     // yine −12.500'e iner
        ]);

        // 5. gün: 1.000 − 13.500 = −12.500; 10. gün −11.500; 20. gün −12.500 (eşit) → en erken: 5. gün.
        Assert.Equal(Bugun.AddDays(5), s.EnDusukTarih);
        Assert.Equal(-12_500m, s.EnDusukKasa);
    }

    [Fact]
    public void Hic_cikis_yoksa_en_dusuk_gun_bugundur()
    {
        var s = NakitTahmini.Hesapla(Bugun, 500m, 30, [Kalem(Bugun.AddDays(3), 100m, TahminKalemTuru.AlinanCek)]);
        Assert.Equal(Bugun, s.EnDusukTarih);
        Assert.Equal(500m, s.EnDusukKasa);
    }

    [Fact]
    public void Haric_kalemler_hesaba_girmez_ufuk_icindekiler_ayrica_doner()
    {
        var haric = new[]
        {
            Kalem(Bugun.AddDays(4), 9_000m, TahminKalemTuru.AlinanCek, cekId: 7),
            Kalem(Bugun.AddDays(-2), -3_000m, TahminKalemTuru.VerilenCek, cekId: 8),
            Kalem(Bugun.AddDays(40), 1m, TahminKalemTuru.AlinanCek, cekId: 9),
        };
        var s = NakitTahmini.Hesapla(Bugun, 100m, 30, [], haric);

        Assert.Equal(100m, s.SonKasa);
        Assert.Equal([8, 7], s.HaricKalemler.Select(k => k.CekId));
        Assert.True(s.HaricKalemler[0].Gecikmis);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(367)]
    public void Gecersiz_gun_sayisi_reddedilir(int gun)
        => Assert.Throws<ArgumentOutOfRangeException>(() => NakitTahmini.Hesapla(Bugun, 0m, gun, []));

    // ------------------------------------------------------------------ Kart

    private static IReadOnlyList<TahminKalemi> Kart(decimal ekstre, decimal guncel, int gun,
        KartHarcama[]? harcama = null, KartOdeme[]? odeme = null, int kesim = 15, int sonOdeme = 5, DateOnly? bugun = null)
    {
        var b = bugun ?? Bugun;
        return NakitTahmini.KartOdemeleri("Bonus", kesim, sonOdeme, ekstre, guncel,
            harcama ?? [], odeme ?? [], b, b.AddDays(gun));
    }

    [Fact]
    public void Acik_ekstre_son_odeme_gununde_cikar_sonraki_ekstre_ufuk_disinda()
    {
        // Kesim 15 / son ödeme 5: açık ekstre 15 Eyl → 5 Eki. Sonraki: 15 Eki → 5 Kas (30 günlük ufkun dışında).
        var k = Assert.Single(Kart(10_000m, 13_000m, 30));
        Assert.Equal(new DateOnly(2026, 10, 5), k.Tarih);
        Assert.Equal(-10_000m, k.Tutar);
        Assert.Equal(TahminKalemTuru.KartOdemesi, k.Tur);
        Assert.Equal("Bonus · ekstre ödemesi", k.Aciklama);
    }

    [Fact]
    public void Uzun_ufukta_kesim_sonrasi_borc_ve_ileri_tarihli_harcamalar_sonraki_ekstrelere_girer()
    {
        var l = Kart(10_000m, 13_000m, 90,
            harcama: [new(new DateOnly(2026, 10, 10), 500m), new(new DateOnly(2026, 10, 20), 700m), new(Bugun, 999m)]);

        Assert.Equal(
            [(new DateOnly(2026, 10, 5), -10_000m), (new DateOnly(2026, 11, 5), -3_500m), (new DateOnly(2026, 12, 5), -700m)],
            l.Select(k => (k.Tarih, k.Tutar)));
    }

    [Fact]
    public void Planli_odeme_tarihinde_cikar_ve_ekstreyi_kapatir_cift_dusulmez()
    {
        var l = Kart(10_000m, 13_000m, 60, odeme: [new(new DateOnly(2026, 10, 1), 4_000m)]);
        Assert.Equal(
            [(new DateOnly(2026, 10, 1), -4_000m, "Bonus · planlanmış kart ödemesi"),
             (new DateOnly(2026, 10, 5), -6_000m, "Bonus · ekstre ödemesi"),
             (new DateOnly(2026, 11, 5), -3_000m, "Bonus · ekstre ödemesi")],
            l.Select(k => (k.Tarih, k.Tutar, k.Aciklama)));

        // Ekstreden fazla planlı ödeme sonraki ekstreyi de azaltır.
        var fazla = Kart(10_000m, 13_000m, 60, odeme: [new(new DateOnly(2026, 10, 1), 12_000m)]);
        Assert.Equal(
            [(new DateOnly(2026, 10, 1), -12_000m), (new DateOnly(2026, 11, 5), -1_000m)],
            fazla.Select(k => (k.Tarih, k.Tutar)));
        Assert.Equal(-13_000m, fazla.Sum(k => k.Tutar));
    }

    [Fact]
    public void Ufuk_disindaki_planli_odeme_yazilmaz_ama_ekstreyi_kapatir()
    {
        // Kullanıcı ödemeyi daha sonraya planlamış: ekstre ufuk içinde ayrıca düşülmez.
        var l = Kart(10_000m, 10_000m, 30, odeme: [new(new DateOnly(2026, 11, 1), 10_000m)]);
        Assert.Empty(l);
    }

    [Fact]
    public void Son_odemesi_gecmis_odenmemis_ekstre_gecikmis_kalem_olur()
    {
        // Kesim 1 / son ödeme 10: açık ekstre 1 Eyl → 10 Eyl (geçti), borç duruyor.
        var k = Assert.Single(Kart(2_000m, 2_000m, 30, kesim: 1, sonOdeme: 10));
        Assert.Equal(new DateOnly(2026, 9, 10), k.Tarih);

        var s = NakitTahmini.Hesapla(Bugun, 0m, 30, [k]);
        var yazilan = s.Gunler[1].Kalemler.Single();
        Assert.True(yazilan.Gecikmis);
        Assert.Equal(-2_000m, s.Gunler[1].Kasa);
    }

    [Fact]
    public void Karttaki_alacak_sonraki_ekstreden_duser_borc_yoksa_kalem_yok()
    {
        // Fazla ödeme: güncel borç −500 (alacak), ekstre 0. 10 Eki'de 800 harcama → 5 Kas ekstresi 300.
        var l = Kart(0m, -500m, 60, harcama: [new(new DateOnly(2026, 10, 10), 800m)]);
        var k = Assert.Single(l);
        Assert.Equal((new DateOnly(2026, 11, 5), -300m), (k.Tarih, k.Tutar));

        Assert.Empty(Kart(0m, 0m, 90));
        Assert.Empty(Kart(0m, -500m, 90, harcama: [new(new DateOnly(2026, 10, 10), 200m)]));
    }

    [Fact]
    public void Ay_sonu_kesim_gunu_kisa_ayda_kirpilir()
    {
        // Kesim 31 / son ödeme 10, bugün 20 Ocak 2026: açık ekstre 31 Ara → 10 Oca (ödenmiş, 0);
        // sonraki kesim 31 Oca → 10 Şub; ardından 28 Şub → 10 Mar.
        var b = new DateOnly(2026, 1, 20);
        var l = Kart(0m, 1_000m, 60, harcama: [new(new DateOnly(2026, 2, 20), 400m)], kesim: 31, sonOdeme: 10, bugun: b);
        Assert.Equal(
            [(new DateOnly(2026, 2, 10), -1_000m), (new DateOnly(2026, 3, 10), -400m)],
            l.Select(k => (k.Tarih, k.Tutar)));
    }

    // ------------------------------------------------------------------ Karta bağlı olmayan eski K.K

    [Fact]
    public void Ayin_son_donem_baslangici_son_gunun_haftasinin_pazartesisidir()
    {
        Assert.Equal(new DateOnly(2026, 9, 28), NakitTahmini.AyinSonDonemBaslangici(2026, 9));   // 30 Eyl Çarşamba
        Assert.Equal(new DateOnly(2026, 10, 26), NakitTahmini.AyinSonDonemBaslangici(2026, 10)); // 31 Eki Cumartesi
        Assert.Equal(new DateOnly(2026, 11, 30), NakitTahmini.AyinSonDonemBaslangici(2026, 11)); // 30 Kas Pazartesi
        Assert.Equal(new DateOnly(2026, 5, 25), NakitTahmini.AyinSonDonemBaslangici(2026, 5));   // 31 May Pazar
    }

    [Fact]
    public void Kartsiz_kk_sonraki_ayin_son_doneminde_duser_karta_bagli_ve_cari_haric()
    {
        var islemler = new[]
        {
            new Islem(new DateOnly(2026, 8, 5), "K.K", 1_000m, "MEZAT", GiderTipi.KrediKarti),
            new Islem(new DateOnly(2026, 8, 20), "K.K", 250.005m, "Ortak", GiderTipi.KrediKarti),
            new Islem(new DateOnly(2026, 8, 21), "K.K", 9_999m, "MEZAT", GiderTipi.KrediKarti, KrediKartiId: 3),
            new Islem(new DateOnly(2026, 8, 22), "Market", 7_777m, "MEZAT", GiderTipi.Cari),
            new Islem(new DateOnly(2026, 9, 3), "K.K", 400m, "MEZAT", GiderTipi.KrediKarti),
            new Islem(new DateOnly(2026, 6, 3), "K.K", 5m, "MEZAT", GiderTipi.KrediKarti),   // Temmuz'da düştü
        };

        var l = NakitTahmini.KartsizKrediKartiDusumleri(islemler, Bugun, Bugun.AddDays(60));

        Assert.Equal(
            [(new DateOnly(2026, 9, 28), -1_250.01m), (new DateOnly(2026, 10, 26), -400m)],
            l.Select(k => (k.Tarih, k.Tutar)));
        Assert.All(l, k => Assert.Equal(TahminKalemTuru.KartsizKrediKarti, k.Tur));
        Assert.Contains("ağustos 2026", l[0].Aciklama, StringComparison.OrdinalIgnoreCase);

        // 30 günlük ufukta Ekim düşümü (26 Eki) dışarıda kalır.
        Assert.Single(NakitTahmini.KartsizKrediKartiDusumleri(islemler, Bugun, Bugun.AddDays(30)));
    }

    [Fact]
    public void Son_donem_basladiysa_dusum_bugunku_kasadadir_tahmine_yazilmaz()
    {
        var islemler = new[] { new Islem(new DateOnly(2026, 8, 5), "K.K", 1_000m, "MEZAT", GiderTipi.KrediKarti) };
        Assert.Empty(NakitTahmini.KartsizKrediKartiDusumleri(islemler, new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 28)));
        Assert.Single(NakitTahmini.KartsizKrediKartiDusumleri(islemler, new DateOnly(2026, 9, 27), new DateOnly(2026, 10, 27)));
    }

    [Fact]
    public void Kartsiz_kk_dusum_gunu_hesap_motorunun_dusurdugu_donemin_baslangicidir()
    {
        // Motorun kendi kuralıyla çapraz doğrulama: Ağustos K.K'sı hangi dönemde kasadan çıkıyor?
        var islemler = new[] { new Islem(new DateOnly(2026, 8, 5), "K.K", 1_000m, "MEZAT", GiderTipi.KrediKarti) };
        var donemler = DonemUretici.Uret(new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 31));
        var haftalik = HesapMotoru.HaftalikHesapla(0m, [new Kanal("MEZAT")], islemler, [], donemler);
        var dusen = Assert.Single(haftalik, h => h.KasaSonucu != 0m);

        var tahmin = Assert.Single(NakitTahmini.KartsizKrediKartiDusumleri(islemler, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31)));
        Assert.Equal(dusen.Donem.Start, tahmin.Tarih);
        Assert.Equal(dusen.KasaSonucu, tahmin.Tutar);
    }
}
