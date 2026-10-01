using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Masaüstü Windows bildirimlerinin çekirdeği (tasarım 2026-09-30 masaüstü bildirimleri §1): sunucunun bildirim listesini
/// (IBildirimApi.BildirimlerAsync) çeker; bugünün okunmamış bildirimlerinden bu bilgisayarda henüz gösterilmemiş olanları depodan
/// ayırıp gösterir ve okunmamış sayısını (menü rozeti) tutar. Kendi hatırlatma hesabı yapmaz. Göstermek okundu yapmaz; yalnız tıklama
/// okundu işaretler. Liste bugünle sınırlanır: sunucu web push'ta da yalnız bugünün bildirimlerini gönderir, eski okunmamışlar ilk
/// kurulumda bir kerede gösterilmez. İptal bilgisi listede yoktur (BildirimDto'da alan yok): sunucu iptal edilmiş bildirimi yalnız
/// okunmuş ya da bir tarayıcıya gönderilmişse döndürür. Uygulama içinde (BildirimNobetcisi, 5 dakikada bir) ve pencere açmadan çalışan
/// görevde (BildirimKontrolu) aynı sınıf kullanılır. Hiçbir hata dışarı çıkmaz.
/// <para>İş parçacığı: uygulamada UI iş parçacığından çağrılır (nöbetçi, kabuk); bekleme sonrası devamlar da orada çalışır (aşağıda
/// ConfigureAwait yok), bu yüzden durum alanları kilitsizdir. Pencere açmadan çalışan görev tek çağrı yapar.</para>
/// </summary>
public sealed partial class BildirimYoklayici(IBildirimApi api, IBildirimGosterici gosterici, IGosterilenBildirimDeposu depo,
    IBildirimAyari ayar, TimeProvider? saat = null) : ObservableObject
{
    private readonly TimeProvider _saat = saat ?? TimeProvider.System;

    /// <summary>Sürmekte olan bakma: yeni çağrı ona katılır (aynı Task), sunucu iki kez sorulmaz.</summary>
    private Task<YoklamaSonucu?>? _surenBakma;

    /// <summary><see cref="Sifirla"/> her çağrıldığında artar; sürerken sıfırlanan eski bakma rozeti ve durumu yazmaz.</summary>
    private int _nesil;

    /// <summary>Son listedeki okunmamış bildirimlerin kimlikleri: rozet yalnız bunlardan biri ilk kez tıklanınca düşer.</summary>
    private HashSet<int> _okunmamisKimlikler = [];

    /// <summary>Son listedeki okunmamış bildirim sayısı (menü rozeti); oturum kapanınca ya da ayar kapanınca 0.</summary>
    [ObservableProperty] private int _okunmamis;

    /// <summary>Son bakmanın sonucu (durum satırı); henüz bakılmadıysa null.</summary>
    [ObservableProperty] private YoklamaSonucu? _sonSonuc;

    /// <summary>Son tıklamanın arkada süren okundu işareti (<see cref="Tiklandi"/>); hata içeride yutulur, beklemek güvenlidir.</summary>
    public Task OkunduIsareti { get; private set; } = Task.CompletedTask;

    /// <summary>Editör oturumunda ve ayar açıkken bir kez bakar; aksi halde hiçbir şey yapmaz ve null döner. Sürmekte olan bakma
    /// varsa yenisi başlatılmaz, aynı görev döner. Sürerken <see cref="Sifirla"/> çağrılan bakma null döner ve durumu değiştirmez.</summary>
    public Task<YoklamaSonucu?> YoklaAsync(bool editorOturumu)
    {
        if (!editorOturumu || !ayar.Acik)
            return Task.FromResult<YoklamaSonucu?>(null);
        if (_surenBakma is { IsCompleted: false } suren)
            return suren;
        var bakma = BakAsync(_nesil);
        _surenBakma = bakma.IsCompleted ? null : bakma;
        return bakma;
    }

    // ConfigureAwait(false) bilinçli olarak kullanılmaz: Okunmamis ve SonSonuc ObservableProperty'dir, PropertyChanged bildirimleri
    // bağlı görünümler için UI iş parçacığında kalmalıdır. Depo çağrısı (YenileriAyir) eşzamanlıdır ve dosya kilitliyse UI
    // iş parçacığını en çok ~0,9 sn (10 deneme, aralarda 100 ms) bekletebilir; pratikte kilit milisaniyeler içinde bırakılır.
    private async Task<YoklamaSonucu?> BakAsync(int nesil)
    {
        IReadOnlyList<BildirimDto> liste;
        try
        {
            liste = await api.BildirimlerAsync();
        }
        catch (KasaApiException e) when (e.DurumKodu is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return Sonuc(nesil, YoklamaDurumu.OturumGecersiz, 0);
        }
        catch (KasaApiException)
        {
            return Sonuc(nesil, YoklamaDurumu.SunucuHatasi, 0);
        }
        catch (Exception)
        {
            return Sonuc(nesil, YoklamaDurumu.SunucuyaUlasilamadi, 0);
        }
        if (nesil != _nesil)
            return null;
        _okunmamisKimlikler = liste.Where(b => !b.Okundu).Select(b => b.Id).ToHashSet();
        Okunmamis = _okunmamisKimlikler.Count;
        var bugun = DateOnly.FromDateTime(_saat.GetLocalNow().DateTime);
        var adaylar = liste.Where(b => !b.Okundu && b.Tarih == bugun).OrderBy(b => b.Id).ToList();
        // Windows ayarında kapalıyken gösterim yapılamaz: bildirimler "gösterildi" diye ayrılmaz, ayar açılınca gösterilir.
        if (WindowsAyarindaKapali())
            return Sonuc(nesil, YoklamaDurumu.WindowsAyarindaKapali, 0);
        IReadOnlyList<int> yeniler;
        try
        {
            yeniler = adaylar.Count == 0 ? [] : depo.YenileriAyir(adaylar.Select(b => b.Id).ToList());
        }
        catch (Exception)
        {
            return Sonuc(nesil, YoklamaDurumu.YerelKayitHatasi, 0);
        }
        foreach (var bildirim in adaylar.Where(b => yeniler.Contains(b.Id)))
        {
            try
            {
                gosterici.Goster(bildirim);
            }
            catch (Exception)
            {
                // Tek bildirimin gösterilememesi ötekileri durdurmaz; bildirim listede ve telefonda görünmeye devam eder.
            }
        }
        return Sonuc(nesil, YoklamaDurumu.Basarili, yeniler.Count);
    }

    /// <summary>Tıklanan bildirimin açılacak Shell rotasını hemen döner (sayfa sunucuyu beklemeden açılır); okundu işareti arkada
    /// gönderilir (<see cref="OkunduIsareti"/>; hata yutulur, sonraki bakmada sayı düzelir). Rozet yalnız okundu işareti başarılı
    /// olunca ve son listede okunmamış olan bildirimin ilk tıklamasında düşer: aynı tıklama iki yoldan gelebilir, telefonda okunmuş
    /// bildirim zaten sayılmamıştır. Editör oturumu yoksa hiçbir şey yapmaz ve null döner.</summary>
    public string? Tiklandi(BildirimTiklamasi tiklama, bool editorOturumu)
    {
        if (!editorOturumu)
            return null;
        if (tiklama.BildirimId is { } kimlik)
            OkunduIsareti = OkunduIsaretleAsync(kimlik);
        return BildirimHedefi.Rota(tiklama.Hedef);
    }

    // ConfigureAwait(false) kullanılmaz: rozet (Okunmamis) bağlı görünüm için çağıranın (UI) bağlamında güncellenir. Yanıt gelmeden
    // Sifirla çağrılırsa kimlik kümesi boşalmıştır, rozet düşmez.
    private async Task OkunduIsaretleAsync(int kimlik)
    {
        try
        {
            await api.BildirimOkunduAsync(kimlik);
            if (_okunmamisKimlikler.Remove(kimlik))
                Okunmamis = Math.Max(0, Okunmamis - 1);
        }
        catch (Exception)
        {
            // Okundu işareti bir sonraki tıklamada ya da Bildirimler ekranında verilebilir; sayfa yine açılmıştır.
        }
    }

    /// <summary>Bildirimler ekranı listeyi yükleyince ya da okundu işaretleyince okunmamış sayısını bildirir. Bu bilgisayarda Windows
    /// bildirimleri kapalıyken rozet gizli kalır (0; kullanıcı kararı); ayar açılınca yapılan bakma sayıyı yeniden yazar.</summary>
    public void OkunmamisBildir(int sayi) => Okunmamis = ayar.Acik ? Math.Max(0, sayi) : 0;

    /// <summary>Oturum kapandı, rol editör değil ya da ayar kapandı: rozet gizlenir, durum satırı boşalır; sürmekte olan bakmanın
    /// sonucu yok sayılır ve sonraki çağrı yeniden sorar.</summary>
    public void Sifirla()
    {
        _nesil++;
        _surenBakma = null;
        _okunmamisKimlikler = [];
        Okunmamis = 0;
        SonSonuc = null;
    }

    private bool WindowsAyarindaKapali()
    {
        try
        {
            return gosterici.WindowsAyarindaKapali;
        }
        catch (Exception)
        {
            // Ayar okunamazsa gösterim denenir; gösterim hatası zaten tek tek yutulur.
            return false;
        }
    }

    private YoklamaSonucu? Sonuc(int nesil, YoklamaDurumu durum, int yeni)
    {
        if (nesil != _nesil)
            return null;
        var sonuc = new YoklamaSonucu(_saat.GetLocalNow(), durum, yeni);
        SonSonuc = sonuc;
        return sonuc;
    }
}
