using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace Kasa.App.Core.Tests;

/// <summary>Masaüstü bildirimlerinin zamanlanmış görevi (EmarKasaBildirim): schtasks pencere açmadan çalışır; görev UTF-16 XML ile
/// kurulur (günlük tetikleyici sunucu saati + 5 dk, bu kullanıcının oturum açılışı tetikleyicisi, yönetici hakkı istemeyen principal);
/// aynı saat/exe/kullanıcı ve görev duruyorsa yeniden kurulmaz; kurulamayan görev işaretlenmez; silme işareti kaldırır; hiçbir hata
/// dışarı çıkmaz. Gerçek schtasks Görev 11'de kullanıcının bilgisayarında denenir.</summary>
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
                argumanlar[xmlSirasi + 1] = Path.GetFileName(argumanlar[xmlSirasi + 1]);
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
        var k = BildirimGorevi.KurmaKomutu(@"C:\x\bildirim-gorevi.xml");
        Assert.Equal("schtasks", k.FileName);
        Assert.Equal(["/Create", "/TN", "EmarKasaBildirim", "/XML", @"C:\x\bildirim-gorevi.xml", "/F"], k.ArgumentList);
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
        Assert.Equal(["schtasks /Create /TN EmarKasaBildirim /XML bildirim-gorevi.xml /F"], c.Ozet);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, c.Xml![..2]);
        var metin = Encoding.Unicode.GetString(c.Xml, 2, c.Xml.Length - 2);
        Assert.StartsWith(@"<?xml version=""1.0"" encoding=""utf-16""?>", metin);
        Assert.Contains("2026-01-01T09:05:00", metin);
        Assert.False(File.Exists(Path.Combine(_klasor, BildirimGorevi.XmlDosyaAdi)));
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
        Assert.False(File.Exists(Path.Combine(_klasor, BildirimGorevi.XmlDosyaAdi)));
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
