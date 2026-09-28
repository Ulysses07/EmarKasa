using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Yürütücünün görünüm modelinde yazdığı durum (<see cref="TemelViewModel"/> uygular).</summary>
public interface IYurutmeYuzeyi
{
    bool Mesgul { get; set; }
    string? Hata { get; set; }
    /// <summary>Yeni işlem başlarken önceki işlemin başarı iletisini (Mesaj) kaldırır; iletisi olmayan modelde boştur.</summary>
    void IletiyiTemizle();
}

/// <summary>Görünüm modellerinin tek async yürütme deseni (appcore-10). Her ekran aynı garantileri buradan alır:
/// <list type="bullet">
/// <item>Nesil: oturum değişimi ve ekrandan ayrılma <see cref="GecersizKil"/> ile bekleyen işleri eskitir. Eski işin hatası ve
/// bitişi (Mesgul) yeni durumu ezmez; sonucunu işlem kendi <see cref="Gecerli"/> denetimiyle bırakır. Eskiyen iş Mesgul'u
/// indirmediği için eskiten taraf (oturum sıfırlaması) göstergeyi kendisi indirir.</item>
/// <item>Tekil işlem (<see cref="YurutAsync"/>; yazma ve ekran yüklemesi): sürerken ikincisi başlamaz (çift tıklama, aynı anda
/// iki düğme); başlarken Hata ve Mesaj temizlenir; hata <see cref="HataMesaji"/> ile yazılır.</item>
/// <item>Son istek kazanır (<see cref="SonIstekHatti"/>; okuma): yeni istek öncekini eskitir ve iptal belirteciyle ağda da
/// bırakır; iptal hata sayılmaz, hata <see cref="OkumaHataMesaji"/> ile yazılır.</item>
/// </list>
/// Tekil işlem ile yüzeydeki okuma (<see cref="SonIstekHatti.YukleAsync"/>) aynı Mesgul'u paylaşır; gösterge ikisinden biri
/// sürdükçe açık kalır ve ikisi arasındaki öncelik açıktır:
/// <list type="bullet">
/// <item>Okuma sürerken başlayan tekil işlem (yazma) engellenmez: süren okuma eskitilir ve iptal edilir (sonucu, hatası ve bitişi
/// yansımaz); yazmanın ardından ekran kendi yenilemesini yapar. Yazmanın dayandığı ekran yüklemesi (form doldurma) bu yüzden okuma
/// hattıyla değil tekil işlemle yapılır: o sürerken yazma başlamaz (Ayarlar).</item>
/// <item>Tekil işlem sürerken başlayan okuma çalışır (son istek kazanır) ama yazmanın hatasını ve iletisini temizlemez; biten okuma
/// göstergeyi yazma sürüyorsa indirmez, biten yazma okuma sürüyorsa indirmez. Okuma sürse de yazma sürdükçe ikinci tekil işlem
/// başlamaz.</item>
/// </list>
/// Kendi göstergesini taşıyan okuma hatları Mesgul'a dokunmayan <see cref="SonIstekHatti.Baslat"/>/<see cref="SonIstekHatti.Guncel"/>
/// kullanır (İşlemler listesi); ekranın tek göstergesiyle okuyan ekran <see cref="SonIstekHatti.YukleAsync"/> kullanır (Rapor).</summary>
public sealed class Yurutucu(IYurutmeYuzeyi yuzey)
{
    private int _nesil;
    // Göstergenin sahipleri: süren tekil işlemin nesli ve yüzeyde yükleyen son okuma. Nesil değişince (GecersizKil) ikisi de
    // kendiliğinden düşer (tekil işlem neslinden, okuma biletinden eskir). Yalnız UI bağlamında yazılır ve okunur.
    private int? _tekilNesli;
    private SonIstekHatti? _okuyanHat;
    private IstekBileti? _okumaBileti;

    /// <summary>Şu anki nesil; işlem başlarken yakalanır, sonuç uygulanmadan önce <see cref="Gecerli"/> ile denetlenir.</summary>
    public int Nesil => Volatile.Read(ref _nesil);
    public bool Gecerli(int nesil) => nesil == Nesil;
    /// <summary>Bekleyen bütün işleri (tekil ve son istek biletleri) eskitir: sonuçları, hataları ve bitişleri yansımaz.</summary>
    public void GecersizKil() => Interlocked.Increment(ref _nesil);

    /// <summary>Bu nesilde tekil işlem sürüyor mu (eskiyen işlem sayılmaz).</summary>
    internal bool TekilSuruyor => _tekilNesli == Nesil;
    private bool OkumaSuruyor => _okuyanHat is { } hat && _okumaBileti is { } bilet && hat.Guncel(bilet);

    /// <summary>Başka işlem sürerken yapılmayan, kullanıcının onay diyaloğundan sonra istediği işlemin (ör. silme) iletisi.</summary>
    public const string SurenIslemIletisi = "Önceki işlem sürdüğü için bu işlem yapılmadı. İşlem bitince yeniden deneyin.";

    /// <summary>Tekil işlem: Mesgul iken çalışmaz. Varsayılan sessizce dönmektir (çift tıklamanın ikinci basışı iletiyle
    /// karışmasın); <paramref name="mesgulkenBildir"/> onaydan sonra gelen işlemde (silme) yapılmadığını Hata'ya yazar. Göstergeyi
    /// yalnız yüzeydeki okuma tutuyorsa işlem engellenmez: okuma eskitilip iptal edilir (bkz. <see cref="Yurutucu"/>).
    /// <paramref name="islem"/> başladığı nesli alır; sonucu yazmadan önce <see cref="Gecerli"/> ile denetler.</summary>
    public async Task YurutAsync(Func<int, Task> islem, bool mesgulkenBildir = false)
    {
        if (yuzey.Mesgul)
        {
            if (!TekilSuruyor && OkumaSuruyor) _okuyanHat!.Birak();
            else { if (mesgulkenBildir) yuzey.Hata = SurenIslemIletisi; return; }
        }
        var nesil = Nesil;
        _tekilNesli = nesil; _okuyanHat = null; _okumaBileti = null;
        // Önce Mesgul: temizlemenin tetiklediği bildirimden gelen ikinci çağrı da korumaya takılır.
        yuzey.Mesgul = true; yuzey.Hata = null; yuzey.IletiyiTemizle();
        try { await islem(nesil); }
        catch (Exception hata) { if (Gecerli(nesil)) yuzey.Hata = HataMesaji(hata); }
        finally
        {
            if (Gecerli(nesil)) { _tekilNesli = null; yuzey.Mesgul = OkumaSuruyor; }
        }
    }

    /// <summary>Yüzeyde yükleyen okuma başladı: göstergeyi (tekil işlemle birlikte) tutar.</summary>
    internal void OkumaBasladi(SonIstekHatti hat, IstekBileti bilet) { _okuyanHat = hat; _okumaBileti = bilet; }

    /// <summary>Güncel okuma bitti; göstergenin açık kalıp kalmayacağını (tekil işlem sürüyor mu) döner.</summary>
    internal bool OkumaBitti(IstekBileti bilet)
    {
        if (ReferenceEquals(_okumaBileti, bilet)) { _okuyanHat = null; _okumaBileti = null; }
        return TekilSuruyor;
    }

    /// <summary>Son istek kazanır yüklemesinin yüzeyi: yükleme göstergesi Mesgul, hata Hata'dır.</summary>
    internal IYurutmeYuzeyi Yuzey => yuzey;

    /// <summary>Yazma ve tekil işlem hatasının iletisi. 401'de oturumun bittiği söylenir (oturum zaten eskidiyse hiç yazılmaz);
    /// zaman aşımında istek sunucuya ulaşmış olabileceği için önce kontrol istenir.</summary>
    public static string HataMesaji(Exception hata) => hata switch
    {
        KasaApiException { DurumKodu: HttpStatusCode.Unauthorized } => "Oturumunuz sona erdi. Yeniden giriş yapın.",
        KasaApiException { DurumKodu: HttpStatusCode.Forbidden } => "Bu işlem için yetkiniz yok.",
        KasaApiException api when api.DurumKodu is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable => TemelViewModel.HataKoduEkle(api.Message, api),
        KasaApiException api => TemelViewModel.HataKoduEkle("Sunucu işlemi tamamlayamadı. Lütfen yeniden deneyin.", api),
        // İstek sunucuya ulaşmış olabilir: kayıt işlemleri tamamlanmış olabileceği için önce kontrol istenir.
        TimeoutException => "Sunucu zamanında yanıt vermedi. İşlem sunucuda tamamlanmış olabilir; yeniden denemeden önce listeyi yenileyip kontrol edin. Büyük dosyalarda bağlantınızı kontrol edin.",
        HttpRequestException or TaskCanceledException => "Sunucuya ulaşılamadı. Bağlantıyı kontrol edip yeniden deneyin.",
        _ => "İşlem tamamlanamadı. Lütfen yeniden deneyin.",
    };

    /// <summary>Salt okuma çağrısının (liste, rapor) hata iletisi: okuma sunucuda bir şey değiştirmez; zaman aşımında
    /// "işlem sunucuda tamamlanmış olabilir" denmez, yalnız yeniden deneme istenir.</summary>
    public static string OkumaHataMesaji(Exception hata) => hata is TimeoutException
        ? "Sunucu zamanında yanıt vermedi. Bağlantınızı kontrol edip yeniden deneyin."
        : HataMesaji(hata);
}

/// <summary>Son istek kazanır hattının bileti: istek sırası, başladığı yürütücü nesli ve iptal belirteci.</summary>
public sealed class IstekBileti
{
    internal IstekBileti(int no, int nesil, CancellationTokenSource kaynak) { No = no; Nesil = nesil; Kaynak = kaynak; }
    internal int No { get; }
    internal int Nesil { get; }
    internal CancellationTokenSource Kaynak { get; }
    /// <summary>Yeni istek başlayınca ya da hat bırakılınca iptal edilir; belirteç alan çağrı ağda da bırakılır.</summary>
    public CancellationToken Iptal => Kaynak.Token;
}

/// <summary>"Son istek kazanır" hattı (rapor, liste, dönem geliri gibi okumalar): her istek bilet alır, yeni istek öncekini
/// eskitir ve iptal eder. Bilet yalnız hâlâ hattın son isteğiyse ve yürütücünün nesli (oturum) değişmediyse günceldir: eski
/// süzgecin geç yanıtı, hatası ve bitişi yeni durumu ezmez; önce biten eski istek yükleme göstergesini indirmez.</summary>
public sealed class SonIstekHatti(Yurutucu yurutucu)
{
    private int _no;
    // CancelAfter kullanılmadığı için kaynak Dispose gerektirmez; iptal edilen eski kaynak çöp toplayıcıya kalır.
    private CancellationTokenSource? _iptal;

    /// <summary>Yeni istek: önce sıra artar, sonra eski istek iptal edilir (iptalle eşzamanlı çalışan eski devam kendini eski görür).</summary>
    public IstekBileti Baslat()
    {
        var no = Interlocked.Increment(ref _no);
        var iptal = new CancellationTokenSource();
        Interlocked.Exchange(ref _iptal, iptal)?.Cancel();
        return new(no, yurutucu.Nesil, iptal);
    }

    public bool Guncel(IstekBileti bilet) => bilet.No == Volatile.Read(ref _no) && yurutucu.Gecerli(bilet.Nesil);

    /// <summary>Bekleyen isteği eskitir ve iptal eder (ekrandan ayrılma, süzgeç/form sıfırlaması); göstergeyi çağıran indirir.</summary>
    public void Birak()
    {
        Interlocked.Increment(ref _no);
        Interlocked.Exchange(ref _iptal, null)?.Cancel();
    }

    /// <summary>Biten isteğin iptal kaynağını hattan düşürür (yerine yenisi geçtiyse dokunmaz).</summary>
    public void Bitir(IstekBileti bilet) => Interlocked.CompareExchange(ref _iptal, null, bilet.Kaynak);

    /// <summary>Yürütücünün yüzeyinde (Mesgul, Hata) okuma: başlarken Hata ve ileti temizlenir; yalnız son isteğin sonucu
    /// uygulanır, hatası yazılır ve bitişi Mesgul'u indirir. Bu hattın iptali hata sayılmaz. Tekil işlemle birlikte: sürerken
    /// başlayan tekil işlem bu okumayı eskitir; tekil işlem sürerken başlayan okuma yazmanın hatasını ve iletisini temizlemez,
    /// göstergeyi yazma bitene dek indirmez (bkz. <see cref="Yurutucu"/>).</summary>
    public async Task YukleAsync<T>(Func<CancellationToken, Task<T>> getir, Action<T> uygula)
    {
        var yuzey = yurutucu.Yuzey;
        var bilet = Baslat();
        if (!yurutucu.TekilSuruyor) { yuzey.Hata = null; yuzey.IletiyiTemizle(); }
        yurutucu.OkumaBasladi(this, bilet);
        yuzey.Mesgul = true;
        try
        {
            var veri = await getir(bilet.Iptal);
            if (!Guncel(bilet)) return;
            uygula(veri);
        }
        catch (OperationCanceledException) when (bilet.Iptal.IsCancellationRequested) { /* vazgeçildi: hata değil */ }
        catch (Exception hata) { if (Guncel(bilet)) yuzey.Hata = Yurutucu.OkumaHataMesaji(hata); }
        finally
        {
            if (Guncel(bilet)) yuzey.Mesgul = yurutucu.OkumaBitti(bilet);
            Bitir(bilet);
        }
    }
}
