using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Değişiklik geçmişi: kim (rol) ne zaman neyi ekledi, düzenledi, sildi. Her iki rol görür; editör
/// silinen kaydı (sunucu izin veriyorsa: 30 gün içinde, desteklenen türde) "Geri al" ile geri getirir.
/// Liste sayfalıdır: en yeni <see cref="SayfaBoyutu"/> satır yüklenir, "Daha fazla" bir sayfa ekler.
/// </summary>
public partial class GecmisViewModel : TemelViewModel
{
    /// <summary>Tek istekte çekilen geçmiş satırı sayısı.</summary>
    public const int SayfaBoyutu = 50;

    /// <summary>Tür filtresi "tüm türler" çipi (gerçek tür olamaz).</summary>
    public const string TumTurler = "Tümü";

    private readonly IKasaApi _api;
    /// <summary>
    /// Tek kurucu: DI (MAUI) hem <see cref="IYerelDepo"/> (paket A) hem <see cref="IDosyaKaydedici"/> (paket B)
    /// kayıtlıyken aynı uzunlukta iki kurucu "ambiguous constructors" hatası verir ve Geçmiş sayfası açılmaz.
    /// </summary>
    /// <param name="depo">Cihaza özel "son görülen" satır (yeni satır vurgusu); verilmezse bellekte.</param>
    /// <param name="kaydedici">"Excel'e aktar" dosyasını kaydeden servis; verilmezse aktarma hata gösterir.</param>
    public GecmisViewModel(IKasaApi api, TimeProvider? zaman = null, IYerelDepo? depo = null, IDosyaKaydedici? kaydedici = null)
        : base(zaman)
    {
        _api = api;
        _depo = depo ?? new BellekYerelDepo();
        _kaydedici = kaydedici;
    }

    /// <summary>Yüklü satırlar, en yeni önce.</summary>
    public ObservableCollection<GecmisSatiri> Kayitlar { get; } = new();

    /// <summary>Tür filtresi çipleri: "Tümü" + geçmişte satırı olan türler.</summary>
    public ObservableCollection<SecimCipi> TurCipleri { get; } = new();

    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] private string? _filtreTur;        // null = tüm türler
    [ObservableProperty] private int _toplamKayit;           // filtreye uyan toplam (sunucudan)
    [ObservableProperty] private bool _dahaFazlaVar;
    [ObservableProperty] private string _ozet = "";
    /// <summary>Başarılı geri alma bildirimi (hata değil).</summary>
    [ObservableProperty] private string? _bilgi;

    /// <summary>Liste istek sürümü: eski (geç gelen) yanıt yeni filtrenin sonucunu ezmesin.</summary>
    private int _surum;
    /// <summary>
    /// Sonraki sayfanın sunucu konumu. Yüklü satır sayısından ayrı tutulur: bu arada en üste yeni
    /// satır eklenirse sayfa kayar; tekrar gelen satırlar atlanır ama konum yine de ilerler (takılmaz,
    /// eski satır da atlanmaz: yeni satırlar hep en üste eklenir).
    /// </summary>
    private int _sonrakiOfset;
    private bool _dahaYukleniyor;

    public Task YukleAsync()
    {
        Bilgi = null;
        YeniSiniriniAl();   // Paket A: son bakıştan sonraki satırlar "Yeni" (GecmisViewModel.A.cs)
        return CalistirAsync(DoldurAsync);
    }

    private async Task DoldurAsync()
    {
        var turGorevi = _api.GecmisTurleriAsync();
        var listeGorevi = ListeyiYukleAsync();
        await Task.WhenAll(turGorevi, listeGorevi);
        TurleriKur(turGorevi.Result);
    }

    private void TurleriKur(IReadOnlyList<string> turler)
    {
        var adlar = new List<string> { TumTurler };
        adlar.AddRange(turler);
        if (FiltreTur is { } f && !adlar.Contains(f)) adlar.Add(f);   // seçili filtre çipi kaybolmasın
        if (!adlar.SequenceEqual(TurCipleri.Select(c => c.Ad)))
        {
            TurCipleri.Clear();
            foreach (var a in adlar) TurCipleri.Add(new SecimCipi(a));
        }
        TurVurgu();
    }

    private void TurVurgu()
    {
        foreach (var c in TurCipleri)
            c.Secili = (c.Ad == TumTurler && FiltreTur is null) || c.Ad == FiltreTur;
    }

    /// <summary>Seçili filtreyle ilk sayfayı yükler (listeyi baştan kurar).</summary>
    private async Task ListeyiYukleAsync()
    {
        var surum = ++_surum;
        var tur = FiltreTur;
        DegisiklikSayfasi sayfa;
        try { sayfa = await _api.GecmisAsync(tur, SayfaBoyutu, 0); }
        catch when (surum != _surum) { return; }   // bu arada yeni filtre istendi
        if (surum != _surum) return;

        Kayitlar.Clear();
        foreach (var d in sayfa.Kayitlar) Kayitlar.Add(SatirOlustur(d));
        _sonrakiOfset = sayfa.Kayitlar.Count;
        ToplamKayit = Math.Max(sayfa.Toplam, Kayitlar.Count);
        OzetiGuncelle(sayfaBos: sayfa.Kayitlar.Count == 0);
        GorulduIsaretle();
    }

    /// <summary>Bir sayfa daha eski satırı listenin sonuna ekler.</summary>
    [RelayCommand]
    private Task DahaFazlaYukleAsync() => CalistirAsync(async () =>
    {
        if (!DahaFazlaVar || _dahaYukleniyor) return;
        _dahaYukleniyor = true;
        try
        {
            var surum = _surum;
            var sayfa = await _api.GecmisAsync(FiltreTur, SayfaBoyutu, _sonrakiOfset);
            if (surum != _surum) return;   // bu arada liste yeniden yüklendi

            // Sunucu Id azalan (en yeni önce) döner: yalnız yüklü en eski satırdan eskiler eklenir.
            // Daha yeniler ya zaten listede ya da bu arada eklenmiştir (yenileyince üstte görünür).
            var enEski = Kayitlar.Count > 0 ? Kayitlar[^1].Id : int.MaxValue;
            foreach (var d in sayfa.Kayitlar.OrderByDescending(d => d.Id))
                if (d.Id < enEski) { Kayitlar.Add(SatirOlustur(d)); enEski = d.Id; }
            _sonrakiOfset += sayfa.Kayitlar.Count;
            ToplamKayit = Math.Max(sayfa.Toplam, Kayitlar.Count);
            OzetiGuncelle(sayfaBos: sayfa.Kayitlar.Count == 0);
        }
        finally { _dahaYukleniyor = false; }
    });

    private void OzetiGuncelle(bool sayfaBos)
    {
        DahaFazlaVar = !sayfaBos && _sonrakiOfset < ToplamKayit;
        var kapsam = FiltreTur ?? "Tüm kayıtlar";
        Ozet = DahaFazlaVar
            ? $"{kapsam} · {ToplamKayit} değişiklik (en yeni {Kayitlar.Count} gösteriliyor)"
            : $"{kapsam} · {ToplamKayit} değişiklik";
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SecTurAsync(SecimCipi s)
    {
        Bilgi = null;
        FiltreTur = s.Ad == TumTurler ? null : s.Ad;
        TurVurgu();
        return CalistirAsync(ListeyiYukleAsync);
    }

    /// <summary>Silinen kaydı geri getirir (yalnız editör; sunucu da doğrular) ve listeyi yeniler.</summary>
    [RelayCommand]
    private Task GeriAlAsync(GecmisSatiri s) => CalistirAsync(async () =>
    {
        Bilgi = null;
        Dogrula(EditorMu, HataMesaji.Yetkisiz);
        Dogrula(s.Dto.GeriAlinabilir, GeriAlinamazMesaji);
        await _api.GeriAlAsync(s.Id);
        Bilgi = GeriAlmaMesaji(s);                               // Paket D: güncellemede "önceki haline döndürüldü"
        await ListeyiYukleAsync();
    });

    public const string GeriAlinamazMesaji = "Bu kayıt geri alınamaz.";
    public static string GeriAlindiMesaji(string tur) => $"{tur} geri alındı; geçmişe \"Eklendi (geri alındı)\" olarak yazıldı.";

    partial void OnEditorMuChanged(bool value)
    {
        foreach (var k in Kayitlar) k.GeriAlGorunur = value && k.Dto.GeriAlinabilir;
    }

    private GecmisSatiri SatirOlustur(DegisiklikDto d)
        => new(d, Zaman.LocalTimeZone) { GeriAlGorunur = EditorMu && d.GeriAlinabilir, Yeni = YeniMi(d) };
}

/// <summary>Geçmiş listesinin bir satırı: sunucu satırı + görünüm için biçimlenmiş metinler.</summary>
public partial class GecmisSatiri : ObservableObject
{
    public GecmisSatiri(DegisiklikDto d, TimeZoneInfo yerelSaatDilimi)
    {
        Dto = d;
        Zaman = Yerel(d.ZamanUtc, yerelSaatDilimi).ToString("dd.MM.yyyy HH:mm", Kultur.Turkce);
        RolAdi = RolMetni(d.Rol);
        GeriAlmaNotu = !d.GeriAlindi ? null
            : d.GeriAlmaZamaniUtc is { } g ? $"Geri alındı · {Yerel(g, yerelSaatDilimi).ToString("dd.MM.yyyy HH:mm", Kultur.Turkce)}"
            : "Geri alındı";
    }

    public DegisiklikDto Dto { get; }
    public int Id => Dto.Id;
    public string Tur => Dto.Tur;
    public string Eylem => Dto.Eylem;
    public string Ozet => Dto.Ozet;

    /// <summary>Yerel saatle "24.09.2026 12:00".</summary>
    public string Zaman { get; }
    /// <summary>"Editör" / "İzleyici".</summary>
    public string RolAdi { get; }
    /// <summary>Alt satır: zaman · rol (· kişi · cihaz) · tür.</summary>
    public string Ayrinti => $"{Zaman} · {Kim} · {Tur}";
    /// <summary>Silme geri alındıysa "Geri alındı · zaman", değilse null.</summary>
    public string? GeriAlmaNotu { get; }

    /// <summary>Eylem rozeti rengi için.</summary>
    public bool SilmeMi => Eylem == "Silindi";
    public bool EklemeMi => Eylem.StartsWith("Eklendi", StringComparison.Ordinal);
    public bool GuncellemeMi => !SilmeMi && !EklemeMi;

    /// <summary>"Geri al" düğmesi: yalnız editörde ve sunucu geri alınabilir dediyse.</summary>
    [ObservableProperty] private bool _geriAlGorunur;

    public static string RolMetni(string? rol) => rol switch
    {
        "editor" => "Editör",
        "viewer" => "İzleyici",
        null or "" => "—",
        _ => rol,
    };

    private static DateTime Yerel(DateTime zaman, TimeZoneInfo tz)
    {
        var utc = zaman.Kind == DateTimeKind.Local ? zaman.ToUniversalTime() : DateTime.SpecifyKind(zaman, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }
}
