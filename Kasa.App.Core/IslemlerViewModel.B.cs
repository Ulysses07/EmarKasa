using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>22 · Rapordan İşlemler'e iniş: Haftalık/Aylık/Kasa dökümündeki rakama dokununca gelinen süzgeç.</summary>
public partial class IslemlerViewModel
{
    /// <summary>
    /// Rapordan gelen gider tipi süzgeci (null = tüm tipler). Sunucu tipe göre süzmediği için yüklenen
    /// sayfa istemcide süzülür; "Tip süzgecini kaldır" ya da yeni bir rapor inişi temizler.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TipSuzgeciVar), nameof(TipSuzgeciMetni))]
    private GiderTipi? _suzgecTip;

    public bool TipSuzgeciVar => SuzgecTip is not null;
    public string TipSuzgeciMetni => SuzgecTip is { } t ? $"Yalnız {IslemSuzgeci.TipAdi(t).ToLower(Kultur.Turkce)} işlemleri" : "";

    /// <summary>
    /// Rapordan gelen süzgeci uygular: tarih aralığı + kanal (+ tip). Hızlı zaman çipi ve hafta seçimi
    /// temizlenir; liste yeni süzgeçle yeniden yüklenir.
    /// </summary>
    public Task SuzgecUygulaAsync(IslemSuzgeci s)
    {
        _filtreZamanKod = null;
        _sonSeciliDonem = null;
        _donemlerKuruluyor = true;          // Picker temizlenirken OnSeciliDonemChanged liste yenilemesin
        try { SeciliDonem = null; }
        finally { _donemlerKuruluyor = false; }
        FiltreBaslangic = s.Baslangic;
        FiltreBitis = s.Bitis;
        FiltreKanal = s.Kanal;
        SuzgecTip = s.Tip;
        FiltreVurgu();
        return YenidenListele();
    }

    [RelayCommand]
    private Task TipSuzgeciniKaldirAsync()
    {
        SuzgecTip = null;
        return YenidenListele();
    }

    /// <summary>Tip süzgeci varsa yalnız o tipteki işlemler.</summary>
    private IEnumerable<IslemDto> TipSuz(IEnumerable<IslemDto> kayitlar)
        => SuzgecTip is { } t ? kayitlar.Where(i => i.Tip == t) : kayitlar;
}
