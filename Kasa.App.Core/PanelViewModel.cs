using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class PanelViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public PanelViewModel(IKasaApi api) => _api = api;

    [ObservableProperty] private decimal _guncelKasa;
    [ObservableProperty] private decimal _buHaftaSonucu;
    [ObservableProperty] private decimal _buAySonucu;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DagilimBekliyor))]
    private decimal _dagilimBekleyenTutar;
    public bool DagilimBekliyor => DagilimBekleyenTutar > 0;
    public ObservableCollection<KanalKasaSatiri> Kanallar { get; } = new();

    public void KartBorclariniYansit(IReadOnlyList<TakipKanalPayi>? borclar)
    {
        for (var i = 0; i < Kanallar.Count; i++)
        {
            var satir = Kanallar[i];
            Kanallar[i] = satir with { KartBorcu = borclar is null || satir.KanalId is null ? null : borclar.Where(p => p.KanalId == satir.KanalId).Sum(p => Math.Max(0, p.Tutar)) };
        }
    }

    public override Task YukleAsync() => RaporYukleAsync(_api.PanelAsync, p =>
    {
        GuncelKasa = p.GuncelKasa;
        BuHaftaSonucu = p.BuHaftaSonucu;
        BuAySonucu = p.BuAySonucu;
        DagilimBekleyenTutar = p.DagilimBekleyenTutar;
        Kanallar.Clear();
        foreach (var k in p.Kanallar) Kanallar.Add(new(k.Kanal, k.Bakiye, k.KanalId));
    });
}

public record KanalKasaSatiri(string Kanal, decimal Bakiye, int? KanalId, decimal? KartBorcu = null)
{
    public string KartBorcuMetni => KartBorcu is { } borc ? $"Kalan kart borcu {Bicim.Tl(borc)} ₺" : "Kart borcu bilgisi alınmadı.";
}
