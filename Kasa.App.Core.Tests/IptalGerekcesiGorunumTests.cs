using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// İptal edilen aylık gider ödemesi ve ekstre satırı masaüstünde de gerekçesi ve iptal anıyla görünür
/// (gap-denetim-izi-gozlemlenebilirlik-5): aylık gider ekranı iptalleri plan satırından ayrı, salt okunur listeler;
/// alanı taşımayan eski sunucuda liste boştur. Ay sabittir (takvime bağlı değildir).
/// </summary>
public class IptalGerekcesiGorunumTests
{
    private static readonly DateTimeOffset IptalAni = new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
    private static AylikGiderSatirDto Satir(string durum) => new(1, 4, "Kira", "Kira", 100, new(2026, 9, 5), "Genel", Array.Empty<TakipKanalPayi>(), durum);

    private static async Task<AylikGiderViewModel> Aylik(AylikGiderAyDto ay)
    {
        var f = new KasaKontrolVeAylikGiderTests.Fake { BekleyenAy = Task.FromResult(ay) };
        var v = new AylikGiderViewModel(f, new SahteApi(), new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor }) { AyTarihi = new DateTime(2026, 9, 1) };
        await v.YukleAsync();
        return v;
    }

    [Fact]
    public async Task Aylik_gider_ekrani_iptal_edilen_odemeyi_gerekcesi_ve_iptal_aniyla_ayrica_listeler()
    {
        var iptal = Satir("Iptal") with { OdemeId = 7, OdemeTarihi = new(2026, 9, 5), IptalAciklamasi = "Kira yanlış aya girildi", IptalZamani = IptalAni };
        var eski = iptal with { OdemeId = 6, IptalAciklamasi = "Sürüm öncesi iptal", IptalZamani = null };
        var v = await Aylik(new AylikGiderAyDto(2026, 9, 100, 0, [Satir("Planlandi")], [iptal, eski]));

        Assert.True(v.IptalVar);
        Assert.Equal([7, 6], v.Iptaller.Select(s => s.Veri.OdemeId!.Value));
        Assert.Equal("Kira · 100,00 ₺ · iptal edildi", v.Iptaller[0].Baslik);
        Assert.Equal($"Ödeme 05.09.2026 · iptal {IptalAni.LocalDateTime:dd.MM.yyyy HH:mm}\nGerekçe: Kira yanlış aya girildi", v.Iptaller[0].Ozet);
        Assert.Equal("Ödeme 05.09.2026 · iptal zamanı bilinmiyor (sürüm öncesi)\nGerekçe: Sürüm öncesi iptal", v.Iptaller[1].Ozet);
        // Plan satırı yine ödeme bekler ve seçilebilir; iptal kaydı plan listesine karışmaz.
        var plan = Assert.Single(v.Kayitlar);
        v.OdemeSec(plan);
        Assert.Same(plan, v.SeciliOdeme);
    }

    [Fact]
    public async Task Iptal_listesini_tasimayan_eski_sunucuda_liste_bos_kalir()
    {
        var v = await Aylik(new AylikGiderAyDto(2026, 9, 100, 0, [Satir("Planlandi")]));
        Assert.False(v.IptalVar);
        Assert.Empty(v.Iptaller);
    }

    [Fact]
    public void Ekstre_kaydi_iptal_gerekcesini_ve_anini_gosterir()
    {
        var kayit = new EkstreKayitDto(9, 1, new(2026, 9, 23), "Kira", 100, "Gider", "Genel", Array.Empty<TakipKanalPayi>(), null, null, null, null, true, "Banka hareketi iki kez okundu", IptalAni);
        Assert.Equal($"Gider · İptal edildi · Yalnız genel kasa\nİptal gerekçesi: Banka hareketi iki kez okundu · {IptalAni.LocalDateTime:dd.MM.yyyy HH:mm}", new EkstreKayitSatiri(kayit).Ozet);
        Assert.Equal("Gider · İptal edildi · Yalnız genel kasa\nİptal gerekçesi: Sürüm öncesi · zamanı bilinmiyor", new EkstreKayitSatiri(kayit with { IptalAciklamasi = "Sürüm öncesi", IptalZamani = null }).Ozet);
        Assert.Equal("Gider · Kaydedildi · Yalnız genel kasa", new EkstreKayitSatiri(kayit with { Iptal = false, IptalAciklamasi = null, IptalZamani = null }).Ozet);
    }
}
