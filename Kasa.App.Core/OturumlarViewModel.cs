using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Ayarlar › Oturumlar ve giriş günlüğü (editör): açık oturumlar (kim, hangi cihaz, son görülme) tek
/// tek kapatılabilir; giriş günlüğü sayfalıdır, yalnız başarısızlar süzülebilir. Editör oturum süresi
/// (isteğe bağlı 7 gün) burada ayarlanır; izleyici oturumu 30 gün.
/// </summary>
public partial class OturumlarViewModel : TemelViewModel
{
    public const int SayfaBoyu = 50;
    public const int KisaOturumGun = 7;
    public const int UzunOturumGun = 30;

    private readonly IKasaApi _api;
    private int _girisYuklemeNo;

    public OturumlarViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        Oturumlar.CollectionChanged += (_, _) => OnPropertyChanged(nameof(OturumBaslik));
    }

    public ObservableCollection<OturumSatiri> Oturumlar { get; } = new();
    public string OturumBaslik => $"Açık oturumlar ({Oturumlar.Count})";

    public ObservableCollection<GirisSatiri> Girisler { get; } = new();

    [ObservableProperty] private string? _bilgi;

    // ── Giriş günlüğü sayfası ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SayfaMetni), nameof(OncekiVar), nameof(SonrakiVar))]
    private int _offset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SayfaMetni), nameof(OncekiVar), nameof(SonrakiVar))]
    private int _toplam;

    /// <summary>Yalnız başarısız girişler (kod bekleme adımı sayılmaz).</summary>
    [ObservableProperty] private bool _yalnizBasarisiz;

    public bool OncekiVar => Offset > 0;
    public bool SonrakiVar => Offset + SayfaBoyu < Toplam;
    public string SayfaMetni => Toplam == 0 ? "Kayıt yok" : $"{Offset + 1}–{Math.Min(Offset + SayfaBoyu, Toplam)} / {Toplam}";

    // ── Güvenlik ayarı ──
    /// <summary>Editör oturumu 7 günde bitsin (kapalıyken 30 gün).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OturumSuresiMetni))]
    private bool _editorOturumuKisa;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OturumSuresiMetni))]
    private int _editorOturumGun = UzunOturumGun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OturumSuresiMetni))]
    private int _izleyiciOturumGun = UzunOturumGun;

    [ObservableProperty] private int _girisGunluguGun;

    public string OturumSuresiMetni
        => $"Şu an editör oturumu {EditorOturumGun} gün, izleyici oturumu {IzleyiciOturumGun} gün sürer. Giriş günlüğü {GirisGunluguGun} gün saklanır.";

    partial void OnGirisGunluguGunChanged(int value) => OnPropertyChanged(nameof(OturumSuresiMetni));

    public Task YukleAsync() => CalistirAsync(async () =>
    {
        var ayarGorevi = _api.GuvenlikAyariAsync();
        var oturumGorevi = OturumlariDoldurAsync();
        var girisGorevi = GirisleriDoldurAsync();
        var ayar = await ayarGorevi;
        await oturumGorevi;
        await girisGorevi;
        AyariYaz(ayar);
    });

    private void AyariYaz(GuvenlikAyariDto a)
    {
        EditorOturumGun = a.EditorOturumGun;
        IzleyiciOturumGun = a.IzleyiciOturumGun;
        GirisGunluguGun = a.GirisGunluguGun;
        EditorOturumuKisa = a.EditorOturumGun <= KisaOturumGun;
    }

    private async Task OturumlariDoldurAsync()
    {
        var liste = await _api.OturumlarAsync();
        Oturumlar.Clear();
        foreach (var o in liste.OrderByDescending(o => o.BuOturum).ThenByDescending(o => o.SonGorulmeUtc))
            Oturumlar.Add(new OturumSatiri(o, Zaman.LocalTimeZone));
    }

    private async Task GirisleriDoldurAsync()
    {
        var no = ++_girisYuklemeNo;
        var sayfa = await _api.GirisKayitlariAsync(YalnizBasarisiz, SayfaBoyu, Offset);
        if (no != _girisYuklemeNo) return;
        Toplam = sayfa.Toplam;
        Girisler.Clear();
        foreach (var g in sayfa.Kayitlar) Girisler.Add(new GirisSatiri(g, Zaman.LocalTimeZone));
    }

    partial void OnYalnizBasarisizChanged(bool value)
    {
        Offset = 0;
        _ = CalistirAsync(GirisleriDoldurAsync);
    }

    [RelayCommand]
    private Task Onceki()
    {
        if (!OncekiVar) return Task.CompletedTask;
        Offset = Math.Max(0, Offset - SayfaBoyu);
        return CalistirAsync(GirisleriDoldurAsync);
    }

    [RelayCommand]
    private Task Sonraki()
    {
        if (!SonrakiVar) return Task.CompletedTask;
        Offset += SayfaBoyu;
        return CalistirAsync(GirisleriDoldurAsync);
    }

    [RelayCommand]
    private Task Yenile() => YukleAsync();

    /// <summary>Bir oturumu kapatır: o cihaz bir sonraki istekte giriş ekranına döner.</summary>
    [RelayCommand]
    private Task OturumKapat(OturumSatiri? o) => o is null ? Task.CompletedTask : CalistirAsync(async () =>
    {
        Bilgi = null;
        Dogrula(!o.BuOturum, "Bu cihazın oturumunu kapatmak için Çıkış'ı kullanın.");
        await _api.OturumKapatAsync(o.Id);
        Bilgi = $"{o.Baslik} oturumu kapatıldı.";
        await OturumlariDoldurAsync();
    });

    [RelayCommand]
    private Task OturumSuresiKaydet() => CalistirAsync(async () =>
    {
        Bilgi = null;
        var gun = EditorOturumuKisa ? KisaOturumGun : UzunOturumGun;
        await _api.GuvenlikAyariKaydetAsync(gun);
        AyariYaz(await _api.GuvenlikAyariAsync());
        Bilgi = $"Editör oturumu artık {gun} gün sürer; bu süreyi aşmış açık oturumlar bir sonraki istekte kapanır.";
    });
}

/// <summary>Açık oturum satırı.</summary>
public sealed class OturumSatiri
{
    public OturumSatiri(OturumDto o, TimeZoneInfo tz)
    {
        Dto = o;
        Baslik = $"{(string.IsNullOrWhiteSpace(o.AdSoyad) ? "Bilinmeyen" : o.AdSoyad)} · {GecmisSatiri.RolMetni(o.Rol)}";
        var parcalar = new List<string> { string.IsNullOrWhiteSpace(o.Cihaz) ? "cihaz bilinmiyor" : o.Cihaz! };
        if (!string.IsNullOrWhiteSpace(o.Ip)) parcalar.Add(o.Ip!);
        parcalar.Add($"son görülme {YerelZaman.Metin(o.SonGorulmeUtc, tz)}");
        parcalar.Add($"bitiş {YerelZaman.Gun(o.BitisUtc, tz)}");
        if (o.Eski) parcalar.Add("güncellemeden önce açılmış");
        Ayrinti = string.Join(" · ", parcalar);
    }

    public OturumDto Dto { get; }
    public string Id => Dto.Id;
    public bool BuOturum => Dto.BuOturum;
    public bool Kapatilabilir => !Dto.BuOturum;
    /// <summary>"EMAR · Editör".</summary>
    public string Baslik { get; }
    /// <summary>"EMAR-LAPTOP · 88.1.2.3 · son görülme 24.09.2026 12:05 · bitiş 24.10.2026".</summary>
    public string Ayrinti { get; }
}

/// <summary>
/// Giriş günlüğü satırı. Aynı saatte aynı yerden gelen başarısız denemeler sunucuda tek satırda
/// toplanır ("25 deneme, son 12:40"). "Kod bekleniyor" başarısızlık değil ara adımdır (şifre doğru,
/// iki adım kodu isteniyor): gri yazılır.
/// </summary>
public sealed class GirisSatiri
{
    public const string KodBekleniyor = "Kod bekleniyor";

    public const string TonBasarili = "basarili";
    public const string TonBasarisiz = "basarisiz";
    public const string TonAra = "ara";

    public GirisSatiri(GirisKaydiDto g, TimeZoneInfo tz)
    {
        Dto = g;
        Zaman = YerelZaman.Metin(g.ZamanUtc, tz);
        Kim = string.IsNullOrWhiteSpace(g.AdSoyad)
            ? (string.IsNullOrWhiteSpace(g.KullaniciAdi) ? "—" : g.KullaniciAdi)
            : $"{g.AdSoyad} ({g.KullaniciAdi})";
        AraAdim = !g.Basarili && g.Neden == KodBekleniyor;
        var parcalar = new List<string> { Zaman };
        if (g.Tekrar > 1)
            parcalar.Add(g.SonZamanUtc is { } son
                ? $"{g.Tekrar} deneme, son {YerelZaman.Cevir(son, tz).ToString("HH:mm", Kultur.Turkce)}"
                : $"{g.Tekrar} deneme");
        if (!string.IsNullOrWhiteSpace(g.Rol)) parcalar.Add(GecmisSatiri.RolMetni(g.Rol));
        parcalar.Add(string.IsNullOrWhiteSpace(g.Cihaz) ? "cihaz bilinmiyor" : g.Cihaz!);
        if (!string.IsNullOrWhiteSpace(g.Ip)) parcalar.Add(g.Ip!);
        if (!string.IsNullOrWhiteSpace(g.Neden) && !AraAdim) parcalar.Add(g.Neden!);
        Ayrinti = string.Join(" · ", parcalar);
    }

    public GirisKaydiDto Dto { get; }
    public bool Basarili => Dto.Basarili;
    /// <summary>Şifre doğru, iki adım kodu bekleniyor: başarısız sayılmaz.</summary>
    public bool AraAdim { get; }
    public string SonucAdi => Dto.Basarili ? "Başarılı" : AraAdim ? KodBekleniyor : "Başarısız";
    /// <summary>Durum çipinin rengi: "basarili" (yeşil), "basarisiz" (kırmızı), "ara" (gri).</summary>
    public string Ton => Dto.Basarili ? TonBasarili : AraAdim ? TonAra : TonBasarisiz;
    public string Zaman { get; }
    /// <summary>"EMAR (emar)"; tanınmayan kullanıcı adında yazılan ad.</summary>
    public string Kim { get; }
    /// <summary>"24.09.2026 12:00 · 25 deneme, son 12:40 · Editör · EMAR-LAPTOP · 88.1.2.3 · Hatalı şifre".</summary>
    public string Ayrinti { get; }
}
