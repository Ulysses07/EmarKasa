using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Haftalık dönemin kanal satırı: gelen, giden, sonuç; dokununca kanalın o haftaki işlemleri.</summary>
public sealed record HaftaKanalSatiri(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, IslemSuzgeci Suzgec)
{
    public string GelenMetni => Bicim.Tl(Gelen);
    public string GidenMetni => Bicim.Tl(Giden);
}

/// <summary>
/// Paket B · Haftalık ekleri: bir döneme dokununca altında o dönemin kanal rakamları (İşlemler'e iner)
/// ve "Kasa neden değişti?" dökümü açılır.
/// </summary>
public partial class HaftalikViewModel
{
    public HaftalikViewModel(IKasaApi api, IDosyaKaydedici? kaydedici, IGezinti? gezinti) : this(api, kaydedici)
        => Gezinti = gezinti;

    public IGezinti? Gezinti { get; set; }

    /// <summary>Ayrıntısı açık dönem (null = kapalı).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetayVar), nameof(DetayBaslik))]
    private HaftalikOzetDto? _detay;
    public bool DetayVar => Detay is not null;
    public string DetayBaslik => Detay is { } d ? KasaDokumuGorunum.AralikMetni(d.Donem.Start, d.Donem.End) : "";

    public ObservableCollection<HaftaKanalSatiri> DetayKanallari { get; } = new();
    public ObservableCollection<KasaDokumSatiri> DetayAdimlari { get; } = new();
    [ObservableProperty] private string? _detayOzeti;

    private int _detaySurumu;

    /// <summary>Dönemi açar (aynı döneme yeniden dokunmak kapatır) ve kasa dökümünü yükler.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task DonemSecAsync(HaftalikOzetDto? d) => CalistirAsync(async () =>
    {
        var surum = ++_detaySurumu;
        if (d is null || Detay?.Donem == d.Donem)
        {
            Detay = null;
            DetayKanallari.Clear();
            DetayAdimlari.Clear();
            DetayOzeti = null;
            return;
        }
        Detay = d;
        DetayKanallari.Clear();
        foreach (var k in d.Kanallar)
            DetayKanallari.Add(new HaftaKanalSatiri(k.Kanal, k.Gelen + k.CekGelen, k.Giden + k.CekGiden, k.Sonuc,
                new IslemSuzgeci(d.Donem.Start, d.Donem.End, k.Kanal)));
        DetayAdimlari.Clear();
        DetayOzeti = null;

        KasaDokumuDto dokum;
        try { dokum = await _api.KasaDokumuAsync(d.Donem.Start, d.Donem.End); }
        catch (KasaApiException ex) when (ex.DurumKodu == HttpStatusCode.BadRequest && surum == _detaySurumu)
        {
            DetayOzeti = string.IsNullOrWhiteSpace(ex.SunucuMesaji) ? "Bu dönem için kasa dökümü yok." : ex.SunucuMesaji;
            return;
        }
        catch when (surum != _detaySurumu) { return; }
        if (surum != _detaySurumu) return;
        foreach (var s in KasaDokumuGorunum.Satirlar(dokum)) DetayAdimlari.Add(s);
        DetayOzeti = KasaDokumuGorunum.Ozet(dokum);
    });

    [RelayCommand]
    private Task IslemlereGitAsync(object? hedef) => CalistirAsync(async () =>
    {
        var s = hedef switch
        {
            HaftaKanalSatiri k => k.Suzgec,
            KasaDokumSatiri d => d.Suzgec,
            IslemSuzgeci x => x,
            _ => null,
        };
        if (s is null || Gezinti is null) return;
        await Gezinti.GitAsync(s.Rota());
    });

    /// <summary>Açık dönemin kasa dökümü sayfası (Excel'e aktarılabilir, başka hafta/ay seçilebilir).</summary>
    [RelayCommand]
    private Task KasaDokumunuAcAsync() => CalistirAsync(async () =>
    {
        if (Detay is not { } d || Gezinti is null) return;
        await Gezinti.GitAsync(Rotalar.KasaDokumuRotasi(d.Donem.Start, d.Donem.End));
    });
}
