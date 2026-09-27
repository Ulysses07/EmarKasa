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

    /// <summary>Ana sayfa isteğinin takip özeti ufku (gün); sayfa gün seçimini buraya da yazar.</summary>
    public int TakipGunu { get; set; } = 30;
    /// <summary>Son başarılı yüklemede panelle aynı anlık görüntüden gelen kanal eşikleri; eski sunucuda null (ayrıca yüklenir).</summary>
    public IReadOnlyList<KasaEsikDto>? KasaEsikleri { get; private set; }
    /// <summary>Son başarılı yüklemede panelle aynı anlık görüntüden gelen takip özeti; eski sunucuda null (ayrıca yüklenir).</summary>
    public TakipOzetDto? TakipOzeti { get; private set; }
    /// <summary><see cref="TakipOzeti"/>'nin istendiği gün ufku.</summary>
    public int TakipOzetiGunu { get; private set; }

    /// <summary>Panel, kanal eşikleri ve takip özeti tek istekte (GET /api/rapor/ana-sayfa): bakiye, eşik uyarısı ve kart
    /// borcu aynı andan gelir. Değerler <see cref="RaporViewModel.VeriVar"/> true olmadan önce yazılır.</summary>
    public override Task YukleAsync()
    {
        var gun = TakipGunu;
        return RaporYukleAsync(ct => _api.AnaSayfaAsync(gun, ct), a =>
        {
            var p = a.Panel;
            GuncelKasa = p.GuncelKasa;
            BuHaftaSonucu = p.BuHaftaSonucu;
            BuAySonucu = p.BuAySonucu;
            DagilimBekleyenTutar = p.DagilimBekleyenTutar;
            Kanallar.Clear();
            foreach (var k in p.Kanallar) Kanallar.Add(new(k.Kanal, k.Bakiye, k.KanalId));
            KasaEsikleri = a.KasaEsikleri; TakipOzeti = a.TakipOzeti; TakipOzetiGunu = gun;
        });
    }
}

public record KanalKasaSatiri(string Kanal, decimal Bakiye, int? KanalId, decimal? KartBorcu = null)
{
    public string KartBorcuMetni => KartBorcu is { } borc ? $"Kalan kart borcu {Bicim.Tl(borc)} ₺" : "Kart borcu bilgisi alınmadı.";
}
