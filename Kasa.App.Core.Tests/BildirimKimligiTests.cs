namespace Kasa.App.Core.Tests;

/// <summary>Bildirim kimliği ve Başlat menüsü kısayolu (tasarım 2026-09-30 masaüstü bildirimleri, "Uygulamada verilen kararlar"):
/// kısayol yalnız farklıysa yeniden yazılır; Windows App SDK'nın COM sunucusu kaydı başka exe'yi gösteriyorsa düzeltilir. Yollar
/// düz metindir: testler Windows yol API'sine dayanmaz.</summary>
public class BildirimKimligiTests
{
    private const string Exe = @"C:\Uygulamalar\Emar Kasa 2.4.0\Kasa.App.exe";
    private static readonly Guid Clsid = new("40d47555-e48b-4d78-8fca-22e2b9eb4bf2");

    private static KisayolBilgisi Istenen() => new(Exe, @"C:\Uygulamalar\Emar Kasa 2.4.0", Exe, 0, BildirimKimligi.Aumid, Clsid);

    [Fact]
    public void Ayni_kisayol_yeniden_yazilmaz() => Assert.True(Istenen().AyniMi(Istenen()));

    [Fact]
    public void Yollarda_buyuk_kucuk_harf_farki_ayni_sayilir()
    {
        var mevcut = Istenen() with { Hedef = Exe.ToLowerInvariant(), CalismaDizini = @"c:\uygulamalar\emar kasa 2.4.0", SimgeDosyasi = Exe.ToUpperInvariant() };
        Assert.True(mevcut.AyniMi(Istenen()));
    }

    [Theory]
    [InlineData("eski sürümün exe'si")]
    [InlineData("başka çalışma klasörü")]
    [InlineData("başka simge dosyası")]
    [InlineData("başka simge sırası")]
    [InlineData("kimliksiz")]
    [InlineData("exe yolundan üretilen kimlik")]
    [InlineData("kimlikte harf farkı")]
    [InlineData("etkinleştiricisiz")]
    [InlineData("başka etkinleştirici")]
    public void Farkli_kisayol_guncellenir(string fark)
    {
        var mevcut = fark switch
        {
            "eski sürümün exe'si" => Istenen() with { Hedef = @"C:\Uygulamalar\Emar Kasa 2.3.0\Kasa.App.exe" },
            "başka çalışma klasörü" => Istenen() with { CalismaDizini = @"C:\Uygulamalar" },
            "başka simge dosyası" => Istenen() with { SimgeDosyasi = @"C:\Windows\System32\shell32.dll" },
            "başka simge sırası" => Istenen() with { SimgeSirasi = 1 },
            "kimliksiz" => Istenen() with { Aumid = null },
            "exe yolundan üretilen kimlik" => Istenen() with { Aumid = "{561032CB-76AB-43D7-B128-75517C4BC27B}" },
            "kimlikte harf farkı" => Istenen() with { Aumid = "emarkasa.masaustu" },
            "etkinleştiricisiz" => Istenen() with { EtkinlestiriciClsid = null },
            "başka etkinleştirici" => Istenen() with { EtkinlestiriciClsid = Guid.Empty },
            _ => throw new ArgumentOutOfRangeException(nameof(fark)),
        };
        Assert.False(mevcut.AyniMi(Istenen()));
    }

    [Fact]
    public void Etkinlestirici_bilinmiyorsa_clsidsiz_kisayol_ayni_sayilir()
    {
        var istenen = Istenen() with { EtkinlestiriciClsid = null };
        Assert.True((Istenen() with { EtkinlestiriciClsid = null }).AyniMi(istenen));
        Assert.False(Istenen().AyniMi(istenen));
    }

    [Fact]
    public void Com_sunucusu_komutu_windows_app_sdk_bicimindedir() =>
        Assert.Equal("\"" + Exe + "\" ----AppNotificationActivated:", BildirimKimligi.ComSunucusuKomutu(Exe));

    [Theory]
    [InlineData("\"c:\\uygulamalar\\emar kasa 2.4.0\\kasa.app.exe\" ----AppNotificationActivated:")]
    [InlineData("\"C:\\Uygulamalar\\Emar Kasa 2.4.0\\Kasa.App.exe\" ----AppNotificationActivated:  ")]
    public void Ayni_exeyi_gosteren_com_kaydi_guncel_sayilir(string kayitli) => Assert.True(BildirimKimligi.ComSunucusuGuncelMi(kayitli, Exe));

    [Theory]
    [InlineData("\"c:\\uygulamalar\\emar kasa 2.3.0\\kasa.app.exe\" ----AppNotificationActivated:")]
    [InlineData("\"C:\\Uygulamalar\\Emar Kasa 2.4.0\\Kasa.App.exe\"")]
    [InlineData("C:\\Uygulamalar\\Emar Kasa 2.4.0\\Kasa.App.exe ----AppNotificationActivated:")]
    [InlineData("")]
    [InlineData(null)]
    public void Baska_exeyi_gosteren_ya_da_eksik_com_kaydi_duzeltilir(string? kayitli) => Assert.False(BildirimKimligi.ComSunucusuGuncelMi(kayitli, Exe));
}
