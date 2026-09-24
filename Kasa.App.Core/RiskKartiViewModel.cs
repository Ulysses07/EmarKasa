using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Panel'deki "Sistem ve risk" kartı (yalnız editör, salt okunur): sunucu dışı yedeğin yaşı, boş disk,
/// dünkü başarısız girişler, takipsiz karşılıksız çekler ve gece yedek doğrulamasının sonucu.
/// Kırmızı uyarılar önce gelir. İzleyicide sunucuya hiç sorulmaz.
/// </summary>
public partial class RiskKartiViewModel : TemelViewModel
{
    public const string TonKirmizi = "kirmizi";
    public const string TonSari = "sari";
    public const string TonTamam = "ok";

    private readonly IKasaApi _api;

    public RiskKartiViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        Maddeler.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(DurumMetni)); OnPropertyChanged(nameof(SorunYok)); };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Gorunur))]
    private bool _editorMu;

    public bool Gorunur => EditorMu;

    /// <summary>"ok", "sari" ya da "kirmizi" (kartın başlık rozeti).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurumAdi))]
    private string _durum = TonTamam;

    public ObservableCollection<RiskSatiri> Maddeler { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SorunYok))]
    private bool _yuklendi;

    /// <summary>Okundu ve uyarı yok: "Her şey yolunda" satırı görünür.</summary>
    public bool SorunYok => Yuklendi && Maddeler.Count == 0;

    public string DurumAdi => Durum switch
    {
        TonKirmizi => "Dikkat",
        TonSari => "Uyarı",
        _ => "Yolunda",
    };

    public string DurumMetni
    {
        get
        {
            if (Maddeler.Count == 0) return "Her şey yolunda.";
            var kirmizi = Maddeler.Count(m => m.KirmiziMi);
            var sari = Maddeler.Count - kirmizi;
            var parcalar = new List<string>();
            if (kirmizi > 0) parcalar.Add($"{kirmizi} kırmızı");
            if (sari > 0) parcalar.Add($"{sari} sarı");
            return string.Join(", ", parcalar) + " uyarı";
        }
    }

    /// <summary>"Son yedek doğrulaması: 24.09.2026 03:10 · başarılı" (hiç yapılmadıysa belirtilir).</summary>
    [ObservableProperty] private string? _dogrulamaMetni;
    /// <summary>"Boş disk: 12,3 GB".</summary>
    [ObservableProperty] private string? _diskMetni;
    /// <summary>"Dün başarısız giriş: 0".</summary>
    [ObservableProperty] private string? _girisMetni;

    public Task YukleAsync()
    {
        if (!EditorMu)
        {
            Maddeler.Clear();
            Yuklendi = false;
            return Task.CompletedTask;
        }
        return CalistirAsync(async () =>
        {
            var r = await _api.SistemRiskiAsync();
            Doldur(r);
        });
    }

    [RelayCommand]
    private Task Yenile() => YukleAsync();

    private void Doldur(SistemRiskDto r)
    {
        var tz = Zaman.LocalTimeZone;
        Durum = r.Durum is TonKirmizi or TonSari ? r.Durum : TonTamam;
        Maddeler.Clear();
        foreach (var m in r.Maddeler.OrderByDescending(m => m.Seviye == RiskSeviyesi.Kirmizi))
            Maddeler.Add(new RiskSatiri(m));
        DogrulamaMetni = r.SonDogrulama is { } d
            ? $"Son yedek doğrulaması: {YerelZaman.Metin(d.ZamanUtc, tz)} · {(d.Basarili ? "başarılı" : "BAŞARISIZ")}"
            : "Yedek doğrulaması henüz yapılmadı.";
        DiskMetni = r.DiskBosMb is { } mb ? $"Boş disk: {DiskBoyutu(mb)}" : "Boş disk: okunamadı";
        GirisMetni = $"Dün başarısız giriş: {r.DunBasarisizGiris}";
        Yuklendi = true;
    }

    /// <summary>MB → "850 MB" / "12,3 GB".</summary>
    public static string DiskBoyutu(long mb)
        => mb >= 1024 ? $"{(mb / 1024m).ToString("0.#", Kultur.Turkce)} GB" : $"{mb} MB";
}

/// <summary>Risk kartındaki bir uyarı.</summary>
public sealed class RiskSatiri(RiskMaddesiDto m)
{
    public RiskMaddesiDto Dto { get; } = m;
    public bool KirmiziMi => Dto.Seviye == RiskSeviyesi.Kirmizi;
    /// <summary>Rozet tonu: "kirmizi" ya da "sari".</summary>
    public string Ton => KirmiziMi ? RiskKartiViewModel.TonKirmizi : RiskKartiViewModel.TonSari;
    public string SeviyeAdi => KirmiziMi ? "Kırmızı" : "Sarı";
    public string Baslik => Dto.Baslik;
    public string Aciklama => Dto.Aciklama;
}
