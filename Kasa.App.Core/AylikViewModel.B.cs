using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Paket B · Aylık rapor ekleri: kasa dökümü (21), İşlemler'e iniş (22), yazdırma (24), hedef ve
/// bütçe (05), ay paketi (40), ay kilidi ve yayını (11). Ekler ana raporla aynı anda yüklenir;
/// eski ayın geç gelen yanıtı yeni ayı ezmez (aynı <c>_surum</c> koruması).
/// </summary>
public partial class AylikViewModel
{
    public AylikViewModel(IKasaApi api, TimeProvider? zaman, IDosyaKaydedici? kaydedici, IGezinti? gezinti)
        : this(api, zaman, kaydedici) => Gezinti = gezinti;

    /// <summary>Sayfalar arası gezinme (İşlemler'e iniş, kasa dökümü, hedef düzenleme).</summary>
    public IGezinti? Gezinti { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KilitleGorunur), nameof(KilidiAcGorunur), nameof(YayinlaGorunur))]
    private bool _editorMu;

    /// <summary>Başarılı eylem bildirimi (kilitlendi, yayınlandı…).</summary>
    [ObservableProperty] private string? _bilgi;

    // ---------------------------------------------------------------- 21 · Kasa neden değişti?

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KasaDokumuVar))]
    private KasaDokumuDto? _kasaDokumu;
    public bool KasaDokumuVar => KasaDokumu is not null;
    /// <summary>Açılış → adımlar → kapanış.</summary>
    public ObservableCollection<KasaDokumSatiri> KasaAdimlari { get; } = new();
    /// <summary>Özet cümle ya da "bu ayda takip dönemi yok".</summary>
    [ObservableProperty] private string? _kasaOzeti;

    // ---------------------------------------------------------------- 05 · Hedef ve bütçe

    [ObservableProperty] private HedefButceDto? _hedefButce;
    /// <summary>Kanal satırları: ay sonucu + hedef + inilebilir rakamlar.</summary>
    public ObservableCollection<AylikKanalSatiri> KanalSatirlari { get; } = new();
    public ObservableCollection<ButceSatiri> ButceSatirlari { get; } = new();
    public bool ButceVar => ButceSatirlari.Count > 0;

    // ---------------------------------------------------------------- 11 · Ay kilidi ve yayını

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KilitleGorunur), nameof(KilidiAcGorunur), nameof(YayinlaGorunur), nameof(KapanisMetni),
        nameof(YayinMetni), nameof(DegisiklikUyarisi), nameof(Kilitli), nameof(YayinDugmesiMetni))]
    private AyKapanisDto? _kapanis;

    public bool Kilitli => Kapanis?.Kilitli == true;
    public bool KilitleGorunur => EditorMu && Kapanis is { Kilitli: false, Kilitlenebilir: true };
    public bool KilidiAcGorunur => EditorMu && Kapanis is { Kilitli: true };
    public bool YayinlaGorunur => EditorMu && Kapanis is not null;
    public string YayinDugmesiMetni => Kapanis?.Yayinlandi == true ? "Yeniden yayınla" : "Ayı yayınla";

    /// <summary>"Kilitli · 1 Eyl 2026 12:00" / "Açık" / "Açık · ay bitmedi".</summary>
    public string KapanisMetni => Kapanis switch
    {
        null => "",
        { Kilitli: true, KilitZamaniUtc: { } z } => $"Kilitli · {Yerel(z)}",
        { Kilitli: true } => "Kilitli",
        { Kilitlenebilir: false } => "Açık · ay henüz bitmedi",
        _ => "Açık",
    };

    public string YayinMetni => Kapanis is { Yayinlandi: true, YayinZamaniUtc: { } z } ? $"Yayınlandı · {Yerel(z)}" : "Yayınlanmadı";

    /// <summary>Kırmızı şerit: yayından sonra rakam değişti ya da o aya dokunan kayıt var.</summary>
    public bool DegisiklikUyarisi => Kapanis?.YayindanSonraDegisti == true;
    public ObservableCollection<AyFarkiSatiri> AyFarklari { get; } = new();
    public ObservableCollection<GecmisSatiri> AyaDokunanlar { get; } = new();

    private string Yerel(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zaman.LocalTimeZone)
            .ToString("d MMM yyyy HH:mm", Kultur.Turkce);

    // ---------------------------------------------------------------- yükleme

    private static async Task<(T? Sonuc, Exception? Hata)> Dene<T>(Func<Task<T>> f) where T : class
    {
        try { return (await f(), null); }
        catch (Exception ex) { return (null, ex); }
    }

    /// <summary>
    /// Ekleri (kasa dökümü, ay kapanışı, hedef-bütçe) paralel yükler. Hatalar yakalanır: ana rapor yine
    /// görünür, ilk hata <see cref="TemelViewModel.Hata"/>'ya yazılır. Takip dönemi olmayan ayda döküm
    /// yerine sunucunun açıklaması gösterilir.
    /// </summary>
    private async Task EkleriYukleAsync(int yil, int ay, int surum)
    {
        Bilgi = null;
        var (bas, bit) = KasaDokumuGorunum.AyAraligi(yil, ay);
        var dokumG = Dene(() => _api.KasaDokumuAsync(bas, bit));
        var kapanisG = Dene(() => _api.AyKapanisiAsync(yil, ay));
        var hedefG = Dene(() => _api.HedefButceAsync(yil, ay));
        await Task.WhenAll(dokumG, kapanisG, hedefG);
        if (surum != _surum) return;

        var (dokum, dokumHata) = dokumG.Result;
        var (kapanis, kapanisHata) = kapanisG.Result;
        var (hedef, hedefHata) = hedefG.Result;

        KasaDokumunuKur(dokum);
        if (dokumHata is KasaApiException { DurumKodu: HttpStatusCode.BadRequest } bos)
        {
            KasaOzeti = string.IsNullOrWhiteSpace(bos.SunucuMesaji) ? "Bu ay için kasa dökümü yok." : bos.SunucuMesaji;
            dokumHata = null;
        }
        KapanisiKur(kapanis);
        HedefButce = hedef;
        SatirlariKur();

        if ((dokumHata ?? kapanisHata ?? hedefHata) is { } h) Hata ??= HataMesaji.Coz(h);
    }

    private void KasaDokumunuKur(KasaDokumuDto? d)
    {
        KasaDokumu = d;
        KasaAdimlari.Clear();
        KasaOzeti = null;
        if (d is null) return;
        foreach (var s in KasaDokumuGorunum.Satirlar(d)) KasaAdimlari.Add(s);
        KasaOzeti = KasaDokumuGorunum.Ozet(d);
    }

    private void KapanisiKur(AyKapanisDto? k)
    {
        Kapanis = k;
        AyFarklari.Clear();
        AyaDokunanlar.Clear();
        if (k is null || !k.Yayinlandi) return;
        foreach (var f in k.Farklar) AyFarklari.Add(new AyFarkiSatiri(f));
        foreach (var d in k.Degisiklikler) AyaDokunanlar.Add(new GecmisSatiri(d, Zaman.LocalTimeZone));
    }

    partial void OnRaporChanged(AylikRaporDto? value) => SatirlariKur();

    /// <summary>Kanal ve bütçe satırlarını geçerli rapor + hedef-bütçeden kurar (ay uyuşmazsa hedef yok sayılır).</summary>
    private void SatirlariKur()
    {
        KanalSatirlari.Clear();
        ButceSatirlari.Clear();
        var rapor = Rapor;
        var hedef = HedefButce is { } h && rapor is not null && h.Yil == rapor.Yil && h.Ay == rapor.Ay ? h : null;
        if (rapor is not null)
            foreach (var s in AylikGorunum.KanalSatirlari(rapor, hedef)) KanalSatirlari.Add(s);
        foreach (var b in AylikGorunum.ButceSatirlari(hedef)) ButceSatirlari.Add(b);
        OnPropertyChanged(nameof(ButceVar));
    }

    // ---------------------------------------------------------------- 22 · İşlemler'e iniş

    [RelayCommand]
    private Task IslemlereGitAsync(object? hedef) => CalistirAsync(async () =>
    {
        var s = hedef switch
        {
            DrillRakam r => r.Suzgec,
            AylikKanalSatiri k => k.Suzgec,
            KasaDokumSatiri d => d.Suzgec,
            IslemSuzgeci x => x,
            _ => null,
        };
        if (s is null || Gezinti is null) return;
        await Gezinti.GitAsync(s.Rota());
    });

    /// <summary>Seçili ayın kasa dökümü sayfası (hafta/ay seçilebilir, Excel'e aktarılabilir).</summary>
    [RelayCommand]
    private Task KasaDokumunuAcAsync() => CalistirAsync(async () =>
    {
        if (Gezinti is null) return;
        var (bas, bit) = KasaDokumuGorunum.AyAraligi(Yil, Ay);
        await Gezinti.GitAsync(Rotalar.KasaDokumuRotasi(bas, bit));
    });

    [RelayCommand]
    private Task HedefleriDuzenleAsync() => CalistirAsync(async () =>
    {
        if (Gezinti is null) return;
        await Gezinti.GitAsync(Rotalar.HedefButceRotasi(Yil, Ay));
    });

    // ---------------------------------------------------------------- 24 · Yazdır / 40 · Ay paketi

    /// <summary>Tek sayfalık yazdırılabilir raporu kaydeder ve tarayıcıda açar (Ctrl+P ya da sayfadaki Yazdır).</summary>
    [RelayCommand]
    private Task YazdirAsync() => CalistirAsync(async () =>
    {
        int yil = Yil, ay = Ay;
        AktarilanDosya = null;
        AktarilanDosya = await DosyaAktarma.KaydetAsync(_kaydedici, () => _api.AylikYazdirAsync(yil, ay));
    });

    /// <summary>Ay sonu paketi (ZIP): tüm CSV'ler + yazdırılabilir rapor.</summary>
    [RelayCommand]
    private Task AyPaketiniIndirAsync() => CalistirAsync(async () =>
    {
        int yil = Yil, ay = Ay;
        AktarilanDosya = null;
        AktarilanDosya = await DosyaAktarma.KaydetAsync(_kaydedici, () => _api.AyPaketiAsync(yil, ay));
    });

    // ---------------------------------------------------------------- 11 · Kilitle / kilidi aç / yayınla

    [RelayCommand]
    private Task AyiKilitleAsync() => AyEylemiAsync(_api.AyiKilitleAsync, e => $"{e} kilitlendi. Bu aya ait kayıt artık eklenemez, değiştirilemez ya da silinemez.");

    [RelayCommand]
    private Task AyKilidiniAcAsync() => AyEylemiAsync(_api.AyKilidiniAcAsync, e => $"{e} kilidi açıldı.");

    [RelayCommand]
    private Task AyiYayinlaAsync() => AyEylemiAsync(_api.AyiYayinlaAsync, e => $"{e} yayınlandı: bugünkü rakamlar saklandı. Sonradan değişen rakamlar burada kırmızı şeritte görünür.");

    private Task AyEylemiAsync(Func<int, int, Task<AyKapanisDto>> eylem, Func<string, string> bilgi) => CalistirAsync(async () =>
    {
        Bilgi = null;
        Dogrula(EditorMu, HataMesaji.Yetkisiz);
        int yil = Yil, ay = Ay;
        var surum = _surum;
        var k = await eylem(yil, ay);
        if (surum != _surum || yil != Yil || ay != Ay) return;   // bu arada başka aya geçildi
        KapanisiKur(k);
        Bilgi = bilgi(k.Etiket);
    });
}
