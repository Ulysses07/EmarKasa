using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class HaftalikViewModel : RaporViewModel
{
    private readonly IKasaApi _api;
    public HaftalikViewModel(IKasaApi api, BaglantiDurumu? baglanti = null) : base(baglanti) => _api = api;

    public ObservableCollection<HaftalikSatir> Donemler { get; } = new();
    /// <summary>Sunucunun veri sağlığı uyarısı (ör. rapor ufkunun ötesinde tarihli kayıt). Yalnız son dönemde gelir ama
    /// raporun tamamı için geçerlidir; son başarılı yüklemeden kalır.</summary>
    [ObservableProperty] private string? _veriSagligiUyarisi;

    public override Task YukleAsync()
    {
        return RaporYukleAsync(ct => _api.HaftalikAsync(ct), liste =>
        {
            // HF-01: sunucu eskiden yeniye döner; içinde bulunulan hafta her seferinde sona kaydırmadan görünsün diye
            // istemci yeniden eskiye sıralar. Veri sağlığı uyarısı sunucunun döndürdüğü (eskiden yeniye) sırayla, yalnız
            // son dönemde aranır.
            Donemler.Clear();
            foreach (var d in liste.OrderByDescending(d => d.Donem.Start))
                Donemler.Add(new HaftalikSatir(d));
            VeriSagligiUyarisi = liste.LastOrDefault(d => !string.IsNullOrWhiteSpace(d.VeriSagligiUyarisi))?.VeriSagligiUyarisi;
        });
    }
}

/// <summary>Haftalık rapor satırı: dönem özeti ve görünüm metinleri. "Dağılım bekleyen" yalnız tutar sıfırdan farklıyken
/// görünür; tutar uygulamanın para biçimiyle (Bicim.Tl) yazılır.</summary>
public sealed record HaftalikSatir(HaftalikOzetDto Veri)
{
    public DonemDto Donem => Veri.Donem;
    public decimal KasaSonucu => Veri.KasaSonucu;
    public bool DagilimBekliyor => Veri.DagilimBekleyenTutar != 0;
    public string DagilimBekleyenMetni => $"Dağılım bekleyen: {Bicim.Tl(Veri.DagilimBekleyenTutar)} ₺";
}
