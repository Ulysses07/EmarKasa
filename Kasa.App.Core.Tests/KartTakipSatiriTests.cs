using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Kart kutusunun içeriği KartTakipSatiri'nın hesaplanmış özelliklerinden gelir (tasarım 2026-09-30 §2 Kart kutuları):
/// doluluk oranı, borç ve limit metni, ilk açık ekstrenin son ödemesi, en çok iki durum etiketi (önem sırasıyla) ve renk.
/// "Son ödeme geçti" kuralının günü TimeProvider'dan gelir; testte sabittir.</summary>
public class KartTakipSatiriTests
{
    private static readonly DateOnly Bugun = new(2026, 9, 21);

    private static KartTakipDto Kart(decimal borc = 250, decimal limit = 1000, bool yeniTakip = true, bool aktif = true,
        KartGecisDto? gecis = null, KartEkstreDto[]? ekstreler = null)
        => new(1, 1, "Garanti Bonus", yeniTakip, aktif, null, 10, 20, limit, borc, 0, ekstreler ?? [], [], [], null, gecis);

    private static KartEkstreDto Ekstre(DateOnly sonOdeme, decimal kalan) => new(7, sonOdeme.AddDays(-10), sonOdeme, kalan, 0, kalan, null);

    private static KartTakipSatiri Satir(KartTakipDto kart, DateOnly? bugun = null) => new(kart, new IslemEditorTests.SabitZaman(bugun ?? Bugun));

    [Theory]
    [InlineData(250, 1000, 0.25)]
    [InlineData(1000, 1000, 1.0)]
    [InlineData(1500, 1000, 1.0)]   // borç limitten büyük: çubuk dolu
    [InlineData(-50, 1000, 0.0)]    // kart alacaklı: çubuk boş
    public void Doluluk_orani_borcun_limite_orani_0_ile_1_arasinda(decimal borc, decimal limit, double beklenen)
    {
        var satir = Satir(Kart(borc, limit));
        Assert.Equal(beklenen, satir.Doluluk);
        Assert.True(satir.DolulukVar);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Limit_sifir_ya_da_eksiyse_cubuk_yok(decimal limit)
    {
        var satir = Satir(Kart(limit: limit));
        Assert.Null(satir.Doluluk);
        Assert.False(satir.DolulukVar);
    }

    [Fact]
    public void Borc_ve_limit_metni_tl_bicimindedir()
    {
        var satir = Satir(Kart(borc: 12500, limit: 40000));
        Assert.Equal("12.500,00 ₺", satir.BorcMetni);
        Assert.Equal("Limit 40.000,00 ₺", satir.LimitMetni);
    }

    [Fact]
    public void Son_odeme_metni_kalan_borcu_olan_en_yakin_ekstreden_gelir()
    {
        var satir = Satir(Kart(ekstreler: [Ekstre(new(2026, 10, 20), 50), Ekstre(new(2026, 10, 5), 30), Ekstre(new(2026, 9, 5), 0)]));
        Assert.Equal("Son ödeme 05.10.2026", satir.SonOdemeMetni);
        Assert.Equal(new DateOnly(2026, 10, 5), satir.IlkAcikEkstre!.SonOdemeTarihi);
    }

    [Fact]
    public void Acik_ekstre_yoksa_son_odeme_yazilmaz()
    {
        Assert.Null(Satir(Kart(ekstreler: [Ekstre(new(2026, 9, 5), 0)])).SonOdemeMetni);
        Assert.Null(Satir(Kart()).SonOdemeMetni);
    }

    [Fact]
    public void Son_odeme_gecti_kalan_borclu_ekstrenin_son_odemesi_bugunden_onceyse()
    {
        var kart = Kart(ekstreler: [Ekstre(new(2026, 9, 20), 10)]);
        Assert.True(Satir(kart, new DateOnly(2026, 9, 21)).SonOdemeGecti);
        Assert.False(Satir(kart, new DateOnly(2026, 9, 20)).SonOdemeGecti);   // son ödeme günü henüz geçmedi
        Assert.False(Satir(Kart(ekstreler: [Ekstre(new(2026, 9, 20), 0)])).SonOdemeGecti);
    }

    [Fact]
    public void Etiketler_onem_sirasiyla_en_cok_iki_tanedir()
    {
        var gecis = new KartGecisDto("EtkiTarihi", null, null, TahminiKasaFarki: 120);
        var satir = Satir(Kart(yeniTakip: false, aktif: false, gecis: gecis, ekstreler: [Ekstre(new(2026, 9, 20), 10)]));
        Assert.Equal(new[] { "Son ödeme geçti", "Geçiş farkını doğrulayın" }, satir.Etiketler.Select(e => e.Metin));
        Assert.Equal(new[] { KartEtiketTuru.Tehlike, KartEtiketTuru.Uyari }, satir.Etiketler.Select(e => e.Tur));
    }

    [Fact]
    public void Eski_takip_ve_pasif_etiketleri_notr()
    {
        var satir = Satir(Kart(yeniTakip: false, aktif: false));
        Assert.Equal(new[] { "Eski takip", "Pasif" }, satir.Etiketler.Select(e => e.Metin));
        Assert.All(satir.Etiketler, e => Assert.Equal(KartEtiketTuru.Notr, e.Tur));
        Assert.Empty(Satir(Kart()).Etiketler);
    }

    [Fact]
    public void Tahmini_kasa_farki_sifirsa_gecis_etiketi_cikmaz()
    {
        var gecis = new KartGecisDto("EtkiTarihi", null, null, TahminiKasaFarki: 0);
        var satir = Satir(Kart(gecis: gecis));
        Assert.DoesNotContain(satir.Etiketler, e => e.Metin == "Geçiş farkını doğrulayın");
    }

    [Fact]
    public void Gecis_farkiyla_eski_takip_ve_pasifte_ilk_iki_etiket_gecis_ve_eski_takiptir()
    {
        var gecis = new KartGecisDto("EtkiTarihi", null, null, TahminiKasaFarki: 75);
        var satir = Satir(Kart(yeniTakip: false, aktif: false, gecis: gecis));
        Assert.Equal(new[] { "Geçiş farkını doğrulayın", "Eski takip" }, satir.Etiketler.Select(e => e.Metin));
    }

    [Fact]
    public void Ozet_ikinci_satirinda_ilk_acik_ekstrenin_son_odemesi_yazar()
    {
        var satir = Satir(Kart(ekstreler: [Ekstre(new(2026, 10, 5), 30)]));
        Assert.Contains("İlk açık ekstrenin son ödemesi: 05.10.2026", satir.Ozet);
    }

    [Fact]
    public void Renk_kart_adindaki_bankadan_gelir()
        => Assert.Equal(KartRenkAilesi.Yesil, Satir(Kart()).Renk);
}
