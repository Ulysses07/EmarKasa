using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Silmede ikinci onay ve "Silindi · Geri al" şeridi (bütün sayfalarda ortak).
/// <list type="number">
/// <item>İlk basış kaydı "bekleyen" yapar; düğme "Emin misiniz?" olur. <see cref="OnaySuresi"/> içinde aynı
///       kayda ikinci basış silmeyi başlatır; süre geçince ya da başka kayda basılınca onay düşer.</item>
/// <item>Silme geri alınabiliyorsa (geçmişte satırı bulunur) şerit görünür; "Geri al" mevcut geri alma
///       ucunu (<see cref="IKasaApi.GeriAlAsync"/>) çağırır. Geri alınamayan türlerde onay metni bunu söyler.</item>
/// </list>
/// </summary>
public sealed partial class SilmeOnayi : ObservableObject
{
    /// <summary>İkinci basış için süre.</summary>
    public static readonly TimeSpan OnaySuresi = TimeSpan.FromSeconds(5);

    public const string OnayDugmesiGeriAlinir = "Emin misiniz?";
    public const string OnayDugmesiGeriAlinmaz = "Geri alınamaz · Emin misiniz?";
    public const string OnayMetniGeriAlinir = "Silmek için 5 saniye içinde tekrar basın. Silinen kayıt 30 gün içinde geri alınabilir.";
    public const string OnayMetniGeriAlinmaz = "Silmek için 5 saniye içinde tekrar basın. Bu silme geri alınamaz.";

    private readonly IKasaApi _api;
    private readonly TimeProvider _zaman;
    private DateTimeOffset _bekleyenZamani;
    private ITimer? _zamanlayici;
    private int _surum;

    public SilmeOnayi(IKasaApi api, TimeProvider? zaman = null)
    {
        _api = api;
        _zaman = zaman ?? TimeProvider.System;
    }

    /// <summary>İkinci basışı bekleyen kayıt (düğme metni bununla karşılaştırılır); yoksa null.</summary>
    [ObservableProperty] private object? _bekleyen;

    /// <summary>Bekleyen kaydın düğme metni ("Emin misiniz?" / geri alınamıyorsa uyarılı).</summary>
    [ObservableProperty] private string _onayDugmesi = OnayDugmesiGeriAlinir;

    /// <summary>Bekleyen onayın açıklaması; onay yoksa null.</summary>
    [ObservableProperty] private string? _onayMetni;

    /// <summary>Geri alınabilecek son silmenin geçmiş satırı; şerit bununla görünür.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeritGorunur), nameof(Gorunur))]
    private DegisiklikDto? _geriAlinacak;

    /// <summary>Şerit metni ("Silindi: …").</summary>
    [ObservableProperty] private string? _seritMetni;

    public bool SeritGorunur => GeriAlinacak is not null;

    /// <summary>Onay bekleniyor ya da "Geri al" şeridi açık. Onay yalnız düğme metnini değiştirir (sayfa kaymaz).</summary>
    public bool Gorunur => OnayBekliyor || SeritGorunur;

    /// <summary>
    /// Satırdaki Sil düğmesinin metni: bu satır ikinci basışı bekliyorsa onay metni, değilse
    /// <paramref name="varsayilan"/> ("Sil").
    /// </summary>
    public static string DugmeMetni(object? kayit, object? bekleyen, string? onayDugmesi, string varsayilan = "Sil")
        => kayit is not null && Equals(kayit, bekleyen) && !string.IsNullOrEmpty(onayDugmesi) ? onayDugmesi : varsayilan;

    /// <summary>
    /// Sil düğmesine basıldı. İlk basışta kaydı bekleyen yapar ve false döner; süresi içinde aynı
    /// kayda ikinci basışta onayı tüketir ve true döner (çağıran siler).
    /// </summary>
    public bool OnayIste(object kayit, bool geriAlinabilir)
    {
        var simdi = _zaman.GetUtcNow();
        if (Bekleyen is not null && Equals(Bekleyen, kayit) && simdi - _bekleyenZamani <= OnaySuresi)
        {
            Vazgec();
            return true;
        }
        _bekleyenZamani = simdi;
        OnayDugmesi = geriAlinabilir ? OnayDugmesiGeriAlinir : OnayDugmesiGeriAlinmaz;
        OnayMetni = geriAlinabilir ? OnayMetniGeriAlinir : OnayMetniGeriAlinmaz;
        Bekleyen = kayit;
        ZamanlayiciKur();
        return false;
    }

    /// <summary>Bekleyen onayı düşürür (Esc, süre doldu, sayfa yenilendi).</summary>
    public void Vazgec()
    {
        _surum++;
        _zamanlayici?.Dispose();
        _zamanlayici = null;
        Bekleyen = null;
        OnayMetni = null;
    }

    public bool OnayBekliyor => Bekleyen is not null;

    /// <summary>Süre dolunca düğme kendiliğinden "Sil"e döner (UI iş parçacığına postalanır).</summary>
    private void ZamanlayiciKur()
    {
        var surum = ++_surum;
        var baglam = SynchronizationContext.Current;
        _zamanlayici?.Dispose();
        _zamanlayici = _zaman.CreateTimer(_ =>
        {
            void Sifirla() { if (surum == _surum) Vazgec(); }
            if (baglam is null) Sifirla();
            else baglam.Post(_ => Sifirla(), null);
        }, null, OnaySuresi, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Silme başarılı olduktan sonra çağrılır: geçmişte bu kaydın son silinme satırını arar; geri
    /// alınabiliyorsa şeridi gösterir. Arama başarısız olursa (eski sunucu, ağ) şerit gösterilmez;
    /// silme zaten olmuştur.
    /// </summary>
    public async Task SilindiAsync(string tur, int kayitId, string ozet)
    {
        GeriAlinacak = null;
        SeritMetni = null;
        DegisiklikDto? d;
        try { d = await _api.SonSilmeAsync(tur, kayitId); }
        catch (Exception) { return; }
        if (d is not { GeriAlinabilir: true }) return;
        SeritMetni = $"Silindi: {ozet}";
        GeriAlinacak = d;
    }

    /// <summary>Şeritteki "Geri al": silinen kaydı geri getirir ve şeridi kapatır. Hata istisna olarak yükselir.</summary>
    public async Task GeriAlAsync()
    {
        if (GeriAlinacak is not { } d) return;
        await _api.GeriAlAsync(d.Id);
        Kapat();
    }

    /// <summary>Şeridi kapatır.</summary>
    public void Kapat()
    {
        GeriAlinacak = null;
        SeritMetni = null;
    }

    partial void OnBekleyenChanged(object? value)
    {
        OnPropertyChanged(nameof(OnayBekliyor));
        OnPropertyChanged(nameof(Gorunur));
    }
}
