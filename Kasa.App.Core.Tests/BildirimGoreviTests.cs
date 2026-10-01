using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace Kasa.App.Core.Tests;

/// <summary>Masaüstü bildirimlerinin zamanlanmış görevi (EmarKasaBildirim): schtasks pencere açmadan çalışır; görev UTF-16 XML ile
/// kurulur (günlük tetikleyici sunucu saati + 5 dk, bu kullanıcının oturum açılışı tetikleyicisi, yönetici hakkı istemeyen principal);
/// aynı saat/exe/kullanıcı ve görev duruyorsa yeniden kurulmaz; kurulamayan görev işaretlenmez; işaret yalnız görev silinince ya da
/// zaten yoksa kalkar; XML dosyası çağrıya özeldir; hiçbir hata dışarı çıkmaz. Gerçek schtasks Görev 11'de kullanıcının bilgisayarında denenir.</summary>
public sealed class BildirimGoreviTests : IDisposable
{
    private const string Exe = @"C:\Program Files\Emar Kasa\Kasa.App.exe";
    private const string Kullanici = @"MASA\burak";
    private static readonly XNamespace Gorev = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private readonly string _klasor = Path.Combine(Path.GetTempPath(), "kasa-bgorev-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_klasor, true);
        }
        catch (DirectoryNotFoundException) { }
    }

    private string Isaret => Path.Combine(_klasor, BildirimGorevi.IsaretDosyaAdi);

    /// <summary>Klasörde geçici görev XML'i kalmadı.</summary>
    private bool XmlKalmadi => !Directory.Exists(_klasor) || Directory.GetFiles(_klasor, BildirimGorevi.XmlDosyaOneki + "*").Length == 0;

    /// <summary>Çağrıya özel XML dosya adı (önek + 32 onaltılık + .xml) özette "bildirim-gorevi-*.xml" olarak yazılır.</summary>
    private static string XmlAdiOzeti(string yol)
    {
        var ad = Path.GetFileName(yol);
        var ozel = ad.StartsWith(BildirimGorevi.XmlDosyaOneki, StringComparison.Ordinal) && ad.EndsWith(".xml", StringComparison.Ordinal)
            && ad.Length == BildirimGorevi.XmlDosyaOneki.Length + 32 + 4;
        return ozel ? BildirimGorevi.XmlDosyaOneki + "*.xml" : ad;
    }

    /// <summary>Sırayla verilen çıkış kodlarını döner (null: başlatılamadı ya da süre doldu); /XML dosyasının baytlarını saklar.</summary>
    private sealed class Calistirici(params int?[] sonuclar)
    {
        private int _sira;
        public List<string> Ozet { get; } = [];
        public byte[]? Xml { get; private set; }

        public Task<int?> Calistir(ProcessStartInfo komut)
        {
            var argumanlar = komut.ArgumentList.ToList();
            var xmlSirasi = argumanlar.IndexOf("/XML");
            if (xmlSirasi >= 0)
            {
                Xml = File.ReadAllBytes(argumanlar[xmlSirasi + 1]);
                argumanlar[xmlSirasi + 1] = XmlAdiOzeti(argumanlar[xmlSirasi + 1]);
            }
            Ozet.Add(komut.FileName + " " + string.Join(" ", argumanlar));
            return Task.FromResult(sonuclar[_sira++]);
        }

        public List<string> Komutlar => Ozet.Select(o => o.Split(' ')[1]).ToList();
    }

    private BildirimGorevi Kur(Calistirici c) => new(_klasor, Exe, Kullanici, c.Calistir);

    [Fact]
    public void Komutlar_pencere_acmadan_schtasks_ile_calisir()
    {
        var k = BildirimGorevi.KurmaKomutu(@"C:\x\bildirim-gorevi-1.xml");
        Assert.Equal("schtasks", k.FileName);
        Assert.Equal(["/Create", "/TN", "EmarKasaBildirim", "/XML", @"C:\x\bildirim-gorevi-1.xml", "/F"], k.ArgumentList);
        Assert.Equal((false, true, true, true), (k.UseShellExecute, k.CreateNoWindow, k.RedirectStandardOutput, k.RedirectStandardError));
        Assert.Equal(["/Delete", "/TN", "EmarKasaBildirim", "/F"], BildirimGorevi.SilmeKomutu().ArgumentList);
        Assert.Equal(["/Query", "/TN", "EmarKasaBildirim"], BildirimGorevi.SorguKomutu().ArgumentList);
        Assert.Equal("--bildirim-kontrol", BildirimGorevi.KontrolArgumani);
    }

    [Fact]
    public void Gorev_belgesi_gunluk_ve_bu_kullanicinin_oturum_acilisi_tetikleyicisini_tasir()
    {
        var belge = BildirimGorevi.GorevBelgesi(new TimeOnly(0, 3), Exe, Kullanici);
        string Tek(string ad) => belge.Descendants(Gorev + ad).Single().Value;
        Assert.Equal("2026-01-01T00:03:00", Tek("StartBoundary"));
        Assert.Equal("1", Tek("DaysInterval"));
        Assert.Equal([Kullanici, Kullanici], belge.Descendants(Gorev + "UserId").Select(e => e.Value));
        Assert.Equal("LogonTrigger", belge.Descendants(Gorev + "UserId").First().Parent!.Name.LocalName);
        Assert.Equal("PT1M", Tek("Delay"));
        Assert.Equal("InteractiveToken", Tek("LogonType"));
        Assert.Equal("LeastPrivilege", Tek("RunLevel"));
        Assert.Equal("true", Tek("StartWhenAvailable"));
        Assert.Equal("IgnoreNew", Tek("MultipleInstancesPolicy"));
        Assert.Equal("PT2M", Tek("ExecutionTimeLimit"));
        Assert.Equal(Exe, Tek("Command"));
        Assert.Equal("--bildirim-kontrol", Tek("Arguments"));
    }

    [Fact]
    public async Task Gorev_utf16_xml_ile_kurulur_isaret_yazilir_ayni_saatte_yeniden_kurulmaz()
    {
        var c = new Calistirici(0, 0);
        Assert.True(await Kur(c).GuncelleAsync(9, 0));
        Assert.Equal(["schtasks /Create /TN EmarKasaBildirim /XML bildirim-gorevi-*.xml /F"], c.Ozet);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, c.Xml![..2]);
        var metin = Encoding.Unicode.GetString(c.Xml, 2, c.Xml.Length - 2);
        Assert.StartsWith(@"<?xml version=""1.0"" encoding=""utf-16""?>", metin);
        Assert.Contains("2026-01-01T09:05:00", metin);
        Assert.True(XmlKalmadi);
        Assert.Equal(@"09:05|C:\Program Files\Emar Kasa\Kasa.App.exe|MASA\burak", File.ReadAllText(Isaret));
        // Aynı saat, exe ve kullanıcı; görev duruyor (sorgu 0): schtasks /Create yeniden çalışmaz.
        Assert.False(await Kur(c).GuncelleAsync(9, 0));
        Assert.Equal(["/Create", "/Query"], c.Komutlar);
    }

    [Fact]
    public async Task Saat_ya_da_exe_degisince_ya_da_gorev_silinmisse_yeniden_kurulur()
    {
        var c = new Calistirici(0, 0, 0, 1, 0);
        Assert.True(await Kur(c).GuncelleAsync(9, 0));
        Assert.True(await Kur(c).GuncelleAsync(10, 30));
        Assert.Contains("2026-01-01T10:35:00", Encoding.Unicode.GetString(c.Xml!));
        var yeniExe = new BildirimGorevi(_klasor, @"D:\Yeni\Kasa.App.exe", Kullanici, c.Calistir);
        Assert.True(await yeniExe.GuncelleAsync(10, 30));
        // İmza aynı ama görev elle silinmiş (sorgu 1): yeniden kurulur.
        Assert.True(await yeniExe.GuncelleAsync(10, 30));
        Assert.Equal(["/Create", "/Create", "/Create", "/Query", "/Create"], c.Komutlar);
    }

    [Theory]
    [InlineData(new int[] { 1 })]      // schtasks hata verdi
    [InlineData(new int[] { -1 })]     // başlatılamadı ya da süre doldu (null)
    public async Task Kurulamayan_gorev_isaretlenmez_sonraki_acilista_yeniden_denenir(int[] sonuclar)
    {
        var c = new Calistirici([.. sonuclar.Select(s => s < 0 ? (int?)null : s)]);
        Assert.False(await Kur(c).GuncelleAsync(9, 0));
        Assert.False(File.Exists(Isaret));
        Assert.True(XmlKalmadi);
        Assert.True(await Kur(new Calistirici(0)).GuncelleAsync(9, 0));
    }

    [Fact]
    public async Task Silme_isareti_kaldirir_gorev_yoksa_da_basarilidir()
    {
        var c = new Calistirici(0, 0, 1, 1, 1, 0);
        Assert.True(await Kur(c).GuncelleAsync(9, 0));
        Assert.True(await Kur(c).SilAsync());
        Assert.False(File.Exists(Isaret));
        // Silinemedi, sorgu başarısız: görev zaten yok.
        Assert.True(await Kur(c).SilAsync());
        // Silinemedi, sorgu başarılı: görev hâlâ duruyor.
        Assert.False(await Kur(c).SilAsync());
        Assert.Equal(["/Create", "/Delete", "/Delete", "/Query", "/Delete", "/Query"], c.Komutlar);
    }

    [Fact]
    public async Task Silinemeyen_gorevin_isareti_kalir()
    {
        var c = new Calistirici(0, 1, 0, null, 1, 1);
        var gorev = Kur(c);
        Assert.False(gorev.Kurulu);
        Assert.True(await gorev.GuncelleAsync(9, 0));
        Assert.True(gorev.Kurulu);
        // Silinemedi, sorgu başarılı: görev hâlâ duruyor; işaret kalır, sonraki açılışta yeniden silinir.
        Assert.False(await gorev.SilAsync());
        Assert.True(File.Exists(Isaret));
        // Silme başlatılamadı ya da süresi doldu: sonuç bilinmez, işaret kalır.
        Assert.False(await gorev.SilAsync());
        Assert.True(gorev.Kurulu);
        // Silinemedi, sorgu başarısız: görev zaten yok; işaret kalkar.
        Assert.True(await gorev.SilAsync());
        Assert.False(gorev.Kurulu);
        Assert.Equal(["/Create", "/Delete", "/Query", "/Delete", "/Delete", "/Query"], c.Komutlar);
    }

    [Fact]
    public async Task Esszamanli_kurmalar_ayri_xml_dosyasi_kullanir()
    {
        // schtasks dosyayı çalışırken okur. İki kurma da XML'ini yazıp kendi kapısında bekler; kapılar sırayla açılır: ilk kurma
        // okuyup bitirdikten (ve dosyasını sildikten) sonra ikinci kurma okur. Her biri kendi dosyasını okumalıdır.
        TaskCompletionSource[] kapilar = [new(), new()];
        var okunanlar = new List<string>();
        var sira = 0;
        async Task<int?> Calistir(ProcessStartInfo komut)
        {
            var argumanlar = komut.ArgumentList.ToList();
            var xmlSirasi = argumanlar.IndexOf("/XML");
            if (xmlSirasi < 0)
                return 1;
            await kapilar[sira++].Task;
            okunanlar.Add(Encoding.Unicode.GetString(File.ReadAllBytes(argumanlar[xmlSirasi + 1])));
            return 0;
        }
        var gorev = new BildirimGorevi(_klasor, Exe, Kullanici, Calistir);
        var ilk = gorev.GuncelleAsync(9, 0);
        var ikinci = gorev.GuncelleAsync(10, 30);
        kapilar[0].SetResult();
        Assert.True(await ilk);
        kapilar[1].SetResult();
        Assert.True(await ikinci);
        Assert.Equal(2, okunanlar.Count);
        Assert.Contains("2026-01-01T09:05:00", okunanlar[0], StringComparison.Ordinal);
        Assert.Contains("2026-01-01T10:35:00", okunanlar[1], StringComparison.Ordinal);
        Assert.True(XmlKalmadi);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Kasa.App.exe")]
    public async Task Exe_yolu_tam_degilse_gorev_kurulmaz(string exe)
    {
        var c = new Calistirici();
        Assert.False(await new BildirimGorevi(_klasor, exe, Kullanici, c.Calistir).GuncelleAsync(9, 0));
        Assert.Empty(c.Ozet);
        Assert.False(File.Exists(Isaret));
    }

    [Fact]
    public async Task Gecici_xml_silinemese_de_kurulum_basarili_sayilir()
    {
        FileStream? tutulan = null;
        Task<int?> Calistir(ProcessStartInfo komut)
        {
            var argumanlar = komut.ArgumentList.ToList();
            // Dosya silinmeye izin vermeden açık tutulur: kurulumdan sonraki File.Delete IOException fırlatır.
            tutulan = new FileStream(argumanlar[argumanlar.IndexOf("/XML") + 1], FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult<int?>(0);
        }
        try
        {
            Assert.True(await new BildirimGorevi(_klasor, Exe, Kullanici, Calistir).GuncelleAsync(9, 0));
            Assert.True(File.Exists(Isaret));
        }
        finally
        {
            tutulan?.Dispose();
        }
    }

    [Fact]
    public async Task Kullanici_adindaki_ozel_karakterler_xml_de_kacislanir()
    {
        const string ozel = @"MASA\Ali & Veli <x>";
        var c = new Calistirici(0);
        Assert.True(await new BildirimGorevi(_klasor, Exe, ozel, c.Calistir).GuncelleAsync(9, 0));
        var metin = Encoding.Unicode.GetString(c.Xml!, 2, c.Xml!.Length - 2);
        Assert.Contains(@"MASA\Ali &amp; Veli &lt;x&gt;", metin);
        var belge = XDocument.Parse(metin);
        Assert.Equal([ozel, ozel], belge.Descendants(Gorev + "UserId").Select(e => e.Value));
    }

    [Fact]
    public async Task Calistirici_klasor_ya_da_saat_hatasi_disari_cikmaz()
    {
        Assert.False(await new BildirimGorevi(_klasor, Exe, Kullanici, _ => throw new InvalidOperationException("çalıştırılamadı")).GuncelleAsync(9, 0));
        Assert.False(await new BildirimGorevi(_klasor, Exe, Kullanici, _ => throw new InvalidOperationException("çalıştırılamadı")).SilAsync());
        Assert.False(await Kur(new Calistirici(0)).GuncelleAsync(24, 0));
        File.WriteAllText(_klasor + ".dosya", "");
        try
        {
            var gorev = new BildirimGorevi(Path.Combine(_klasor + ".dosya", "alt"), Exe, Kullanici, new Calistirici(0).Calistir);
            Assert.False(await gorev.GuncelleAsync(9, 0));
        }
        finally
        {
            File.Delete(_klasor + ".dosya");
        }
    }
}
