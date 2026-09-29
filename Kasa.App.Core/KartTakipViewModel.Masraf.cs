using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class KartTakipViewModel
{
    private readonly TekrarAnahtari _masrafKey = new();
    private readonly OnizlemeOnay<KartMasrafYaz, KartMasrafOnizlemeDto> _masrafOnizlemesi = new();
    public ObservableCollection<EkstreSatiri> MasrafEkstreleri { get; } = new();
    [ObservableProperty] private EkstreSatiri? _masrafEkstresi;
    [ObservableProperty] private DateTime _masrafTarihi = DateTime.Today;
    [ObservableProperty] private decimal _masrafTutari;
    [ObservableProperty] private string _masrafAciklama = "";
    [ObservableProperty] private string? _masrafOnizleme;
    private KartMasrafYaz MasrafGovde()
    {
        ParaAyristirici.Dogrula(MasrafTutari);
        var g = new KartMasrafYaz(Guid.Empty, Secili!.Surum, MasrafEkstresi?.Veri.Id ?? 0, DateOnly.FromDateTime(MasrafTarihi), MasrafTutari, MasrafAciklama.Trim());
        return g with { IstekId = _masrafKey.Al(new { Secili.Id, g }) };
    }
    [RelayCommand]
    private Task MasrafOnizleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart)
            return;
        _masrafOnizlemesi.Temizle();
        MasrafOnizleme = null;
        if (kontrolApi is not { } kontrol)
        { Hata = "Kart masrafı bağlantısı kullanılamıyor."; return; }
        if (!ParaAyristirici.GecerliMi(MasrafTutari))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (MasrafEkstresi is null || MasrafTutari <= 0 || string.IsNullOrWhiteSpace(MasrafAciklama))
        { Hata = "Kesilmiş açık ekstreyi seçin, bankanın bildirdiği pozitif faiz / masraf tutarını ve açıklamayı girin."; return; }
        if (!await _masrafOnizlemesi.IsteAsync(MasrafGovde, g => kontrol.KartMasrafOnizleAsync(kart.Id, g), () => Gecerli(n) && Secili?.Id == kart.Id))
            return;
        var s = _masrafOnizlemesi.Onizleme!;
        MasrafOnizleme = $"Dağıtılacak faiz / masraf: {Bicim.Tl(s.Tutar)} ₺\nDevreden borç: {Bicim.Tl(s.DevredenBorc)} ₺\n{TakipMetni.Paylar(s.Dagilimlar)}\nKart borcu artar; kasa ancak kart ödemesi kaydedilince azalır.";
    });
    [RelayCommand]
    private Task MasrafKaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { YeniTakip: true } kart || kontrolApi is null)
            return;
        var g = MasrafGovde();
        if (!_masrafOnizlemesi.Gecerli(g) || _masrafOnizlemesi.Onizleme?.DagilimOzeti is not { } dagilimOzeti)
        { Hata = "Faiz / masraf için önce güncel kanal dağılımını gösterin."; return; }
        try
        {
            if (Uygula(await kontrolApi.KartMasrafKaydetAsync(kart.Id, g with { DagilimOzeti = dagilimOzeti }), n))
            { MasrafTemizle(); Mesaj = "Faiz / masraf kanal paylarıyla karta kaydedildi. Henüz kasa çıkışı oluşmadı."; }
        }
        catch (KasaApiException e) when ((int)e.DurumKodu == 409) { if (Gecerli(n)) { _masrafOnizlemesi.Temizle(); MasrafOnizleme = null; } throw; }
    });
    private void MasrafTemizle() { _masrafKey.Temizle(); _masrafOnizlemesi.Temizle(); MasrafOnizleme = null; MasrafEkstresi = null; MasrafTutari = 0; MasrafAciklama = ""; MasrafTarihi = DateTime.Today; }
}
