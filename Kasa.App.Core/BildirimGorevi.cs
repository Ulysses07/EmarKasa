using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Kasa.App.Core;

/// <summary>Bu bilgisayardaki bildirim görevini kuran ve silen arayüz (BildirimNobetcisi); testler sahtesini kullanır.</summary>
public interface IBildirimGorevi
{
    /// <summary>Görevi sunucu bildirim saatine göre kurar ya da günceller; görev aynı saatle zaten duruyorsa dokunmaz. Bu çağrıda
    /// kurulduysa true; değişiklik gerekmediyse ya da kurulamadıysa false. Hata dışarı çıkmaz.</summary>
    Task<bool> GuncelleAsync(int saat, int dakika);

    /// <summary>Görevi siler. Silindiyse ya da zaten yoksa true; ancak o zaman <see cref="Kurulu"/> false olur (silinemeyen görev
    /// kurulu görünmeye devam eder ve yeniden silinebilir). Hata dışarı çıkmaz.</summary>
    Task<bool> SilAsync();

    /// <summary>Görev bu bilgisayarda kurulu görünüyor (ucuz denetim: yerel işaret dosyası; schtasks çalıştırılmaz).</summary>
    bool Kurulu { get; }
}

/// <summary>
/// Masaüstü bildirimlerinin kullanıcı düzeyi Windows zamanlanmış görevi (<see cref="GorevAdi"/>): uygulamayı pencere açmadan
/// <see cref="KontrolArgumani"/> ile çalıştırır (BildirimKontrolu). Tek görevde iki tetikleyici: her gün sunucu bildirim saatinden 5 dk
/// sonra (<see cref="BildirimGorevZamani"/>) ve bu kullanıcının Windows oturumu açılınca (1 dk gecikmeyle). Görev XML ile kurulur
/// (schtasks /Create /XML): "/SC ONLOGON" her kullanıcının oturumu demektir ve yönetici hakkı ister; XML'deki LogonTrigger UserId'si
/// görevi bu kullanıcıyla sınırlar. Principal InteractiveToken + LeastPrivilege: parola sorulmaz, görev yalnız kullanıcı oturumu
/// açıkken çalışır. XML dosyası UTF-16 (BOM'lu) yazılır; schtasks UTF-8 bildirimli dosyayı reddeder. Kurulum imzası (saat, exe yolu,
/// kullanıcı) yerel işaret dosyasındadır: imza aynı ve görev duruyorsa schtasks /Create çalıştırılmaz. EskiHatirlatmaGorevi deseni:
/// pencere yok (CreateNoWindow), süre sınırı, hatalar yutulur.
/// </summary>
public sealed class BildirimGorevi : IBildirimGorevi
{
    public const string GorevAdi = "EmarKasaBildirim";
    /// <summary>Görevin exe'ye verdiği argüman: pencere açılmaz, bakılır, çıkılır (Platforms/Windows/App.xaml.cs).</summary>
    public const string KontrolArgumani = "--bildirim-kontrol";
    /// <summary>Geçici görev XML'inin adı: önek + çağrıya özel Guid + ".xml"; eşzamanlı iki kurma birbirinin dosyasını ezmez ya da
    /// silmez.</summary>
    public const string XmlDosyaOneki = "bildirim-gorevi-";
    public const string IsaretDosyaAdi = "bildirim-gorevi.txt";
    private static readonly XNamespace Ad = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly string _klasor;
    private readonly string _exeYolu;
    private readonly string _kullanici;
    private readonly Func<ProcessStartInfo, Task<int?>> _calistir;

    /// <param name="calistir">Komutu çalıştırıp çıkış kodunu döner; başlatılamaz ya da süre dolarsa null.</param>
    public BildirimGorevi(string klasor, string exeYolu, string kullanici, Func<ProcessStartInfo, Task<int?>> calistir)
    {
        _klasor = klasor;
        _exeYolu = exeYolu;
        _kullanici = kullanici;
        _calistir = calistir;
    }

    /// <summary>Gerçek ortam: %LOCALAPPDATA%\EmarKasa, çalışan exe, ETKİALANI\kullanıcı, gerçek schtasks (5 sn sınırı). Çalışan exe'nin
    /// yolu bilinmiyorsa (Environment.ProcessPath null) yol boş kalır ve görev kurulmaz (<see cref="GuncelleAsync"/> false).</summary>
    public static BildirimGorevi Varsayilan() => new(YerelKlasor.Yol, Environment.ProcessPath ?? "",
        $@"{Environment.UserDomainName}\{Environment.UserName}", k => EskiHatirlatmaGorevi.CalistirAsync(k, EskiHatirlatmaGorevi.ZamanAsimi));

    public static ProcessStartInfo KurmaKomutu(string xmlYolu) => Komut("/Create", "/TN", GorevAdi, "/XML", xmlYolu, "/F");
    public static ProcessStartInfo SilmeKomutu() => Komut("/Delete", "/TN", GorevAdi, "/F");
    public static ProcessStartInfo SorguKomutu() => Komut("/Query", "/TN", GorevAdi);

    private static ProcessStartInfo Komut(params string[] argumanlar)
    {
        var komut = new ProcessStartInfo("schtasks") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in argumanlar)
            komut.ArgumentList.Add(a);
        return komut;
    }

    /// <summary>Kurulumun imzası: görev saati, exe yolu ve kullanıcı; biri değişince görev yeniden kurulur.</summary>
    public static string Imza(TimeOnly zaman, string exeYolu, string kullanici)
        => string.Join("|", zaman.ToString("HH:mm", CultureInfo.InvariantCulture), exeYolu, kullanici);

    /// <summary>Task Scheduler 1.2 görev tanımı. StartBoundary'nin tarihi geçmiştedir (tetikleyici o günden beri her gün çalışır);
    /// saat dilimi yazılmaz: görev saati bu bilgisayarın yerel saat dilimine göredir. Sunucu bildirim saatini kendi diliminde
    /// (Europe/Istanbul) uygular; bilgisayarın saat dilimi farklıysa görev o farkla kayık çalışır (oturum açılışı tetikleyicisi ve açık
    /// uygulamanın 5 dakikalık bakması etkilenmez). Kullanıcı adındaki özel karakterler (&amp;, &lt;) XML'de kaçışlanır.</summary>
    public static XDocument GorevBelgesi(TimeOnly zaman, string exeYolu, string kullanici) => new(
        new XElement(Ad + "Task", new XAttribute("version", "1.2"),
            new XElement(Ad + "RegistrationInfo",
                new XElement(Ad + "Description", "Emar Kasa: sunucudaki hatırlatmaları Windows bildirimi olarak gösterir.")),
            new XElement(Ad + "Triggers",
                new XElement(Ad + "CalendarTrigger",
                    new XElement(Ad + "StartBoundary", "2026-01-01T" + zaman.ToString("HH:mm", CultureInfo.InvariantCulture) + ":00"),
                    new XElement(Ad + "ScheduleByDay", new XElement(Ad + "DaysInterval", 1))),
                new XElement(Ad + "LogonTrigger",
                    new XElement(Ad + "UserId", kullanici),
                    new XElement(Ad + "Delay", "PT1M"))),
            new XElement(Ad + "Principals",
                new XElement(Ad + "Principal", new XAttribute("id", "Author"),
                    new XElement(Ad + "UserId", kullanici),
                    new XElement(Ad + "LogonType", "InteractiveToken"),
                    new XElement(Ad + "RunLevel", "LeastPrivilege"))),
            new XElement(Ad + "Settings",
                new XElement(Ad + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ad + "DisallowStartIfOnBatteries", false),
                new XElement(Ad + "StopIfGoingOnBatteries", false),
                new XElement(Ad + "StartWhenAvailable", true),
                new XElement(Ad + "AllowStartOnDemand", true),
                new XElement(Ad + "Enabled", true),
                new XElement(Ad + "ExecutionTimeLimit", "PT2M")),
            new XElement(Ad + "Actions", new XAttribute("Context", "Author"),
                new XElement(Ad + "Exec",
                    new XElement(Ad + "Command", exeYolu),
                    new XElement(Ad + "Arguments", KontrolArgumani)))));

    /// <summary>XML'i UTF-16 LE (BOM'lu, bildirimi encoding="utf-16") yazar.</summary>
    public static void XmlYaz(XDocument belge, string yol)
    {
        using var yazici = XmlWriter.Create(yol, new XmlWriterSettings { Encoding = Encoding.Unicode, Indent = true });
        belge.Save(yazici);
    }

    public async Task<bool> GuncelleAsync(int saat, int dakika)
    {
        try
        {
            // Göreli ya da boş exe yolu görevi Windows'un çalışma klasörüne göre çözülen yanlış bir programa bağlar: kurulmaz.
            if (!Path.IsPathFullyQualified(_exeYolu))
                return false;
            var zaman = BildirimGorevZamani.Hesapla(saat, dakika);
            var imza = Imza(zaman, _exeYolu, _kullanici);
            var isaret = Path.Combine(_klasor, IsaretDosyaAdi);
            if (File.Exists(isaret) && File.ReadAllText(isaret) == imza && await _calistir(SorguKomutu()) == 0)
                return false;
            Directory.CreateDirectory(_klasor);
            var xml = Path.Combine(_klasor, XmlDosyaOneki + Guid.NewGuid().ToString("N") + ".xml");
            XmlYaz(GorevBelgesi(zaman, _exeYolu, _kullanici), xml);
            try
            {
                if (await _calistir(KurmaKomutu(xml)) != 0)
                    return false;
            }
            finally
            {
                XmlSil(xml);
            }
            File.WriteAllText(isaret, imza);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool Kurulu => File.Exists(Path.Combine(_klasor, IsaretDosyaAdi));

    public async Task<bool> SilAsync()
    {
        try
        {
            var sonuc = await _calistir(SilmeKomutu());
            // Silinemedi: görev yoksa (sorgu başarısız) iş bitmiştir; görev duruyor ya da sonuç bilinmiyorsa başarısız.
            var silindi = sonuc == 0 || (sonuc is not null && await _calistir(SorguKomutu()) is not (null or 0));
            // İşaret yalnız görevin artık olmadığı biliniyorsa kalkar: kalırsa nöbetçi sonraki açılışta yeniden siler.
            if (silindi)
                File.Delete(Path.Combine(_klasor, IsaretDosyaAdi));
            return silindi;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Geçici XML'i siler; silinemezse (dosya o an başka süreçte açık) kurulum sonucu değişmez, dosya klasörde kalır.</summary>
    private static void XmlSil(string yol)
    {
        try
        {
            File.Delete(yol);
        }
        catch (Exception) { }
    }
}
