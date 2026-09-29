using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class GuvenlikViewModel(IYonetimApi api, AuthViewModel auth) : OturumluViewModel(auth)
{
    public const string IstemciSurumu = "2.3.0";
    [ObservableProperty] private string _mevcutSifre = "";
    [ObservableProperty] private string _yeniSifre = "";
    /// <summary>Yeni şifrenin tekrarı: değişim token'ı ve kurtarma kodunu hemen geçersiz kıldığı için yazım hatası hesabı
    /// kilitler; uyuşmazsa istek gönderilmez.</summary>
    [ObservableProperty] private string _yeniSifreTekrar = "";
    /// <summary>"Yeni şifreyi göster" kutusu (yalnız yeni şifre alanları; mevcut şifre maskeli kalır). Başarıda, ekrandan
    /// ayrılınca ve oturum sonunda kapanır: bir sonraki giriş açıkta başlamaz.</summary>
    [ObservableProperty] private bool _yeniSifreyiGoster;
    [ObservableProperty] private string? _kurtarmaKodu;
    [ObservableProperty] private string _surumBilgisi = $"Uygulama {IstemciSurumu}";
    [ObservableProperty] private string _yedekBilgisi = "Yedek durumu henüz alınmadı.";
    /// <summary>Sunucunun yedek hatası ya da rotasyon uyarısı (silinemeyen eski yedek); yoksa null.</summary>
    [ObservableProperty] private string? _yedekUyarisi;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(YedekIptalCommand))]
    private bool _yedekIndiriliyor;
    private CancellationTokenSource? _yedekIptal;
    [ObservableProperty] private bool _guncellemeGerekli;
    [ObservableProperty] private string? _indirmeAdresi;
    public Task YukleAsync() => YurutAsync(async n =>
    {
        var s = await api.SurumAsync();
        if (!Gecerli(n)) return;
        GuncellemeGerekli = Version.TryParse(s.MinimumIstemci, out var minimum) && minimum > Version.Parse(IstemciSurumu);
        IndirmeAdresi = Uri.TryCreate(s.IndirmeAdresi, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? uri.AbsoluteUri : null;
        SurumBilgisi = $"Uygulama {IstemciSurumu} · sunucu {s.Surum}" + (GuncellemeGerekli ? "\nDevam etmek için uygulamayı güncelleyin." : "") + (string.IsNullOrWhiteSpace(s.Notlar) ? "" : "\n" + s.Notlar);
        var y = await api.YedekDurumuAsync();
        if (!Gecerli(n)) return;
        YedekBilgisi = $"Otomatik yedek: {(y.OtomatikEtkin ? "açık" : "kapalı")}\nSon yedek: {Zaman(y.SonYedek)}\nSon doğrulama: {Zaman(y.SonDogrulama)}"
            + DiskSatirlari(y) + GeriYuklemeSatirlari(y);
        // Sunucu disk uyarısını (asgari boş alanın altı, toplam boyut sınırı) ve bulunamayan belge uyarısını da verir.
        var uyarilar = new[] { y.Hata, y.RotasyonUyarisi, y.DiskUyarisi, y.BelgeUyarisi }.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
        YedekUyarisi = uyarilar.Count == 0 ? null : string.Join("\n", uyarilar);
    });
    private static string Zaman(DateTimeOffset? tarih) => tarih?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "Henüz yok";

    /// <summary>Yedek ve veri diskinin boş alanı ve yedeklerin toplam boyutu (GB; eski sunucu göndermezse satır yok). Web (app.js)
    /// ile aynı biçim.</summary>
    public static string DiskSatirlari(YedekDurumuDto y)
    {
        var satirlar = new List<string>();
        if (y.YedekDiskiBosAlanBayt is { } yedek) satirlar.Add($"Yedek diski boş alan: {Bicim.Gb(yedek)}");
        if (y.VeriDiskiBosAlanBayt is { } veri) satirlar.Add($"Veri diski boş alan: {Bicim.Gb(veri)}");
        if (y.ToplamYedekBayt is { } toplam) satirlar.Add($"Yedeklerin toplam boyutu: {Bicim.Gb(toplam)}");
        return satirlar.Count == 0 ? "" : "\n" + string.Join("\n", satirlar);
    }
    /// <summary>Son geri yüklemenin anı ve sunucunun raporu (yapılanlar ve yapılması gerekenler; '• ' ile). Hiç geri yükleme
    /// olmadıysa ya da eski sunucu göndermezse satır yok. Web (app.js) ile aynı metin.</summary>
    public static string GeriYuklemeSatirlari(YedekDurumuDto y)
    {
        if (y.SonGeriYukleme is not { } son) return "";
        var satirlar = new List<string> { $"Son geri yükleme: {Zaman(son)}" };
        satirlar.AddRange((y.GeriYuklemeRaporu ?? []).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => "• " + m));
        return "\n" + string.Join("\n", satirlar);
    }
    [RelayCommand] private Task SifreDegistirAsync() => YurutAsync(async n =>
    {
        Mesaj = null;
        if (string.IsNullOrWhiteSpace(MevcutSifre) || YeniSifre.Length < 12 || YeniSifre.Length > 1024) { Hata = "Mevcut şifreyi ve 12–1024 karakterli yeni şifreyi yazın."; return; }
        if (YeniSifre != YeniSifreTekrar) { Hata = YeniSifreUyusmazMesaji; return; }
        await api.SifreDegistirAsync(new(MevcutSifre, YeniSifre));
        if (!Gecerli(n)) return;
        Temizle(); Mesaj = "Şifre değiştirildi. Yeni şifrenizle giriş yapın.";
    });
    [RelayCommand] private Task KurtarmaKoduOlusturAsync() => YurutAsync(async n =>
    {
        Mesaj = null; KurtarmaKodu = null;
        if (string.IsNullOrWhiteSpace(MevcutSifre)) { Hata = "Mevcut şifrenizi yazın."; return; }
        var yanit = await api.KurtarmaKoduOlusturAsync(MevcutSifre);
        if (!Gecerli(n)) return;
        KurtarmaKodu = yanit.Kod;
        MevcutSifre = "";
        Mesaj = "Bu kod yalnız şimdi gösterilir. Güvenli bir yerde saklayın. Yeni kod önceki kodu geçersiz kılar.";
    });
    public void Temizle() { MevcutSifre = ""; YeniSifre = ""; YeniSifreTekrar = ""; KurtarmaKodu = null; YeniSifreyiGoster = false; }
    /// <summary>Web (app.js) ile aynı ileti.</summary>
    public const string YeniSifreUyusmazMesaji = "Yeni şifreler aynı olmalı.";
    /// <summary>Ekrandan ayrılınca süren yedek indirmesi de iptal edilir; sonucu zaten kullanılmayacaktı. Önce bekleyen işler
    /// geçersiz kılınır, sonra iptal edilir: iptal devamı Cancel() içinde ya da hemen başka iş parçacığında çalışabilir ve
    /// ekran hâlâ geçerliyse ayrılınmış ekrana 'iptal edildi' iletisi yazardı.</summary>
    public void EkrandanAyril() { BekleyenleriIptalEt(); _yedekIptal?.Cancel(); Temizle(); }
    protected override void OturumTemizle() { _yedekIptal?.Cancel(); Temizle(); IndirmeAdresi = null; YedekBilgisi = "Yedek durumu henüz alınmadı."; YedekUyarisi = null; SurumBilgisi = $"Uygulama {IstemciSurumu}"; }
    /// <summary>Yedeği sunucudan belleğe almadan <paramref name="hedef"/>'e yazar. Sunucu yedeği isteğin içinde hazırladığı
    /// için büyük veritabanında dakikalar sürebilir; kullanıcı <see cref="YedekIptalCommand"/> ile vazgeçebilir.
    /// Başarıda indirme bilgisi, hata ya da iptalde null döner (hedefteki yarım içerik çağıranca silinir).</summary>
    public async Task<IndirmeBilgisi?> YedekIndirAsync(Stream hedef)
    {
        IndirmeBilgisi? sonuc = null;
        await YurutAsync(async n =>
        {
            using var iptal = new CancellationTokenSource();
            _yedekIptal = iptal; YedekIndiriliyor = true;
            try
            {
                var bilgi = await api.YedekIndirAsync(hedef, iptal.Token);
                if (!Gecerli(n)) return;
                sonuc = bilgi; Mesaj = $"Yedek indirildi ({Bicim.Boyut(bilgi.Boyut)}).";
            }
            catch (OperationCanceledException) when (iptal.IsCancellationRequested) { if (Gecerli(n)) Mesaj = "Yedek indirme iptal edildi."; }
            finally { _yedekIptal = null; YedekIndiriliyor = false; }
        });
        return sonuc;
    }
    [RelayCommand(CanExecute = nameof(YedekIndiriliyor))] private void YedekIptal() => _yedekIptal?.Cancel();
}
