using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class KartTakipViewModel
{
    private readonly TekrarAnahtari _masrafKey = new();
    private KartMasrafYaz? _onizlenenMasraf;
    private string? _masrafOzetAnahtari;
    public ObservableCollection<EkstreSatiri> MasrafEkstreleri { get; } = new();
    [ObservableProperty] private EkstreSatiri? _masrafEkstresi;
    [ObservableProperty] private DateTime _masrafTarihi = DateTime.Today;
    [ObservableProperty] private decimal _masrafTutari;
    [ObservableProperty] private string _masrafAciklama = "";
    [ObservableProperty] private string? _masrafOnizleme;
    private KartMasrafYaz MasrafGovde()
    {
        var g = new KartMasrafYaz(Guid.Empty, Secili!.Surum, MasrafEkstresi?.Veri.Id ?? 0, DateOnly.FromDateTime(MasrafTarihi), MasrafTutari, MasrafAciklama.Trim());
        return g with { IstekId = _masrafKey.Al(new { Secili.Id, g }) };
    }
    [RelayCommand] private Task MasrafOnizleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart) return;
        _onizlenenMasraf = null; _masrafOzetAnahtari = null; MasrafOnizleme = null;
        if (kontrolApi is null) { Hata = "Kart masrafı bağlantısı kullanılamıyor."; return; }
        if (MasrafEkstresi is null || MasrafTutari <= 0 || string.IsNullOrWhiteSpace(MasrafAciklama)) { Hata = "Kesilmiş açık ekstreyi seçin, bankanın bildirdiği pozitif faiz / masraf tutarını ve açıklamayı girin."; return; }
        var g = MasrafGovde(); var s = await kontrolApi.KartMasrafOnizleAsync(kart.Id, g);
        if (!Gecerli(n) || Secili?.Id != kart.Id || !TakipMetni.Ayni(g, MasrafGovde())) return;
        _onizlenenMasraf = g; _masrafOzetAnahtari = s.DagilimOzeti;
        MasrafOnizleme = $"Dağıtılacak faiz / masraf: {Bicim.Tl(s.Tutar)} ₺\nDevreden borç: {Bicim.Tl(s.DevredenBorc)} ₺\n{TakipMetni.Paylar(s.Dagilimlar)}\nKart borcu artar; kasa ancak kart ödemesi kaydedilince azalır.";
    });
    [RelayCommand] private Task MasrafKaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart || kontrolApi is null) return;
        var g = MasrafGovde();
        if (_onizlenenMasraf is null || _masrafOzetAnahtari is null || !TakipMetni.Ayni(g, _onizlenenMasraf)) { Hata = "Faiz / masraf için önce güncel kanal dağılımını gösterin."; return; }
        try
        {
            if (Uygula(await kontrolApi.KartMasrafKaydetAsync(kart.Id, g with { DagilimOzeti = _masrafOzetAnahtari }), n)) { MasrafTemizle(); Mesaj = "Faiz / masraf kanal paylarıyla karta kaydedildi. Henüz kasa çıkışı oluşmadı."; }
        }
        catch (KasaApiException e) when ((int)e.DurumKodu == 409) { if (Gecerli(n)) { _onizlenenMasraf = null; _masrafOzetAnahtari = null; MasrafOnizleme = null; } throw; }
    });
    private void MasrafTemizle() { _masrafKey.Temizle(); _onizlenenMasraf = null; _masrafOzetAnahtari = null; MasrafOnizleme = null; MasrafEkstresi = null; MasrafTutari = 0; MasrafAciklama = ""; MasrafTarihi = DateTime.Today; }
}
