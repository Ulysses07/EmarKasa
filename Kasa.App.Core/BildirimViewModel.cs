using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public record BildirimSatiri(BildirimDto Veri)
{
    public string Baslik => (Veri.Okundu ? "" : "● ") + Veri.Baslik;
    public string Ozet => $"{Veri.Tarih:dd.MM.yyyy} · {Veri.Mesaj}";
}
public record BildirimCihaziSatiri(BildirimCihaziDto Veri)
{
    public string Baslik => Veri.CihazAdi + (Veri.Etkin ? " · açık" : " · kapalı");
    public string Ozet => Veri.SonBasarili is { } t ? $"Son iletim: {t.ToLocalTime():dd.MM.yyyy HH:mm}" : "Henüz başarılı iletim kaydı yok.";
}

/// <summary>
/// Bildirimler ekranı: bu bilgisayarın Windows bildirimleri (anahtar, deneme bildirimi, durum satırı, Windows ayarı uyarısı;
/// BildirimNobetcisi), hatırlatma saati (sunucu; kaydedilince bu bilgisayardaki zamanlanmış görev de güncellenir), hatırlatma listesi
/// (yüklenince ve okundu işaretlenince menü rozeti güncellenir) ve izin verilmiş tarayıcı/telefon cihazları (tasarım 2026-09-30
/// masaüstü bildirimleri §2).
/// </summary>
public partial class BildirimViewModel : OturumluViewModel
{
    private readonly IBildirimApi _api;
    private readonly BildirimNobetcisi _nobetci;
    private int _surum;

    public BildirimViewModel(IBildirimApi api, AuthViewModel auth, BildirimNobetcisi nobetci) : base(auth)
    {
        _api = api;
        _nobetci = nobetci;
        nobetci.Yoklayici.PropertyChanged += YoklamaDegisti;
        WindowsDurumunuYenile();
    }

    public ObservableCollection<BildirimSatiri> Bildirimler { get; } = new();
    public ObservableCollection<BildirimCihaziSatiri> Cihazlar { get; } = new();
    [ObservableProperty] private bool _etkin;
    [ObservableProperty] private int _saat = 9;
    [ObservableProperty] private int _dakika;
    [ObservableProperty] private string _cihazDurumu = "Cihaz bildirimlerinin durumu alınmadı.";
    [ObservableProperty] private bool _cihazBildirimiEtkin;
    /// <summary>"Bu bilgisayarda Windows bildirimleri" anahtarı (yalnız bu bilgisayar).</summary>
    [ObservableProperty] private bool _windowsBildirimleri;
    /// <summary>Durum satırı: "Son kontrol 14:05 · 2 yeni bildirim", kapalıysa kapalı olduğu.</summary>
    [ObservableProperty] private string _windowsDurumu = "";
    /// <summary>Windows ayarlarında bu uygulamanın bildirimleri kapalı: uyarı ve ayar bağlantısı görünür.</summary>
    [ObservableProperty] private bool _windowsAyarindaKapali;

    /// <summary>Bildirimler, ayar ve cihazlar; hata son başarılı veriyi silmez, eski işaretler (tasarım 2026-10-02 §3).</summary>
    public Task YukleAsync()
    {
        if (EditorMu)
            SonVeriyiGoster();
        return VeriYukleAsync(async n =>
        {
            if (!EditorMu)
                return;
            VeriHazir = false;
            var ayar = await _api.BildirimAyarlariAsync();
            var bildirimler = await _api.BildirimlerAsync();
            var cihazlar = await _api.BildirimCihazlariAsync();
            var anahtar = await _api.BildirimAnahtariAsync();
            if (!Gecerli(n))
                return;
            Yansit(ayar, bildirimler, cihazlar, anahtar);
            _nobetci.Yoklayici.OkunmamisBildir(bildirimler.Count(x => !x.Okundu));
            Tamamlandi("", (ayar, bildirimler, cihazlar, anahtar));
        });
    }

    /// <summary>Son veri önbelleğinden (yeniden kurulan sayfa, H-1); okunmamış sayısı bildirilmez (yalnız güncel yanıt bildirir).</summary>
    public override bool SonVeriyiGoster()
        => OnbellektenUygula<(BildirimAyarDto, IReadOnlyList<BildirimDto>, IReadOnlyList<BildirimCihaziDto>, PushAnahtarDto)>("",
            v => Yansit(v.Item1, v.Item2, v.Item3, v.Item4));

    private void Yansit(BildirimAyarDto ayar, IReadOnlyList<BildirimDto> bildirimler, IReadOnlyList<BildirimCihaziDto> cihazlar, PushAnahtarDto anahtar)
    {
        AyariYansit(ayar);
        TakipMetni.Doldur(Bildirimler, bildirimler.Select(x => new BildirimSatiri(x)));
        TakipMetni.Doldur(Cihazlar, cihazlar.Select(x => new BildirimCihaziSatiri(x)));
        CihazBildirimiEtkin = anahtar.Etkin;
        CihazDurumu = anahtar.Etkin
            ? "Telefon bildirimleri sunucuda açık. Telefonunuzda izin vermek için aşağıdaki web kurulumunu kullanın."
            : "Telefon bildirimleri sunucuda henüz açılmamış. Hatırlatmalar bu ekranda ve Windows bildirimlerinde görünür.";
        WindowsDurumunuYenile();
    }
    private void AyariYansit(BildirimAyarDto a)
    {
        Etkin = a.Etkin;
        Saat = a.Saat;
        Dakika = a.Dakika;
        _surum = a.Surum;
    }
    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || _surum == 0)
            return;
        if (Saat is < 0 or > 23 || Dakika is < 0 or > 59)
        {
            Hata = "Saati 0–23, dakikayı 0–59 arasında girin.";
            return;
        }
        var a = await _api.BildirimAyarKaydetAsync(new(Etkin, Saat, Dakika, _surum));
        if (!Gecerli(n))
            return;
        AyariYansit(a);
        await _nobetci.SaatDegistiAsync(a.Saat, a.Dakika);
        Tamamlandi();
        Mesaj = "Bildirim ayarları kaydedildi. Saat Türkiye saatidir.";
    });
    public Task OkunduAsync(BildirimSatiri satir) => YurutAsync(async n =>
    {
        if (!EditorMu || satir.Veri.Okundu)
            return;
        await _api.BildirimOkunduAsync(satir.Veri.Id);
        if (!Gecerli(n))
            return;
        var index = Bildirimler.IndexOf(satir);
        if (index >= 0)
            Bildirimler[index] = new(satir.Veri with { Okundu = true });
        _nobetci.Yoklayici.OkunmamisBildir(Bildirimler.Count(x => !x.Veri.Okundu));
    });
    public Task CihaziKaldirAsync(BildirimCihaziSatiri satir) => YurutAsync(async n =>
    {
        if (!EditorMu || !satir.Veri.Etkin)
            return;
        await _api.BildirimCihaziKaldirAsync(satir.Veri.Id);
        if (!Gecerli(n))
            return;
        var index = Cihazlar.IndexOf(satir);
        if (index >= 0)
            Cihazlar[index] = new(satir.Veri with { Etkin = false });
        Mesaj = "Bu cihazın bildirimleri kapatıldı.";
    });

    /// <summary>Anahtar değişti: değer bu bilgisayarın ayarından farklıysa görev kurulur/silinir (BildirimNobetcisi.AcikAyarlaAsync;
    /// hata dışarı çıkmaz). Ekran ayarı yansıtırken (WindowsDurumunuYenile) değer aynıdır, bir şey yapılmaz.</summary>
    partial void OnWindowsBildirimleriChanged(bool value)
    {
        if (value != _nobetci.Acik)
            _ = AnahtarUygulaAsync(value);
    }

    /// <summary>Sonucu beklenmez (<see cref="OnWindowsBildirimleriChanged"/>): hata burada yakalanır ve hata satırına yazılır,
    /// gözlenmeden kaybolmaz. Ayar değeri yazılmış olabilir; anahtar ve durum satırı ayardan yeniden okunur.</summary>
    private async Task AnahtarUygulaAsync(bool acik)
    {
        try
        {
            await _nobetci.AcikAyarlaAsync(acik);
            WindowsDurumunuYenile();
            Mesaj = acik ? "Bu bilgisayarda Windows bildirimleri açıldı." : "Bu bilgisayarda Windows bildirimleri kapatıldı.";
        }
        catch (Exception)
        {
            WindowsDurumunuYenile();
            Hata = "Bu bilgisayardaki bildirim görevi güncellenemedi. Lütfen yeniden deneyin.";
        }
    }

    [RelayCommand]
    private void DenemeGoster()
    {
        Mesaj = _nobetci.DenemeGoster()
            ? "Deneme bildirimi gösterildi. Görmediyseniz Windows bildirim ayarlarını kontrol edin."
            : "Deneme bildirimi gösterilemedi. Windows bildirim ayarlarını kontrol edin.";
        WindowsDurumunuYenile();
    }

    /// <summary>Anahtar, durum satırı ve Windows ayarı uyarısı (açılışta, sayfa her göründüğünde, her bakmadan sonra).</summary>
    public void WindowsDurumunuYenile()
    {
        WindowsBildirimleri = _nobetci.Acik;
        WindowsAyarindaKapali = _nobetci.WindowsAyarindaKapali;
        WindowsDurumu = _nobetci.DurumMetni;
    }

    private void YoklamaDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BildirimYoklayici.SonSonuc))
            WindowsDurumunuYenile();
    }

    public static Uri KurulumAdresi(string? apiAdresi) => new(ApiAdresi.Coz(apiAdresi), "#notifications");
    protected override void OturumTemizle()
    {
        Bildirimler.Clear();
        Cihazlar.Clear();
        _surum = 0;
        Etkin = CihazBildirimiEtkin = false;
        CihazDurumu = "Cihaz bildirimlerinin durumu alınmadı.";
    }
}
