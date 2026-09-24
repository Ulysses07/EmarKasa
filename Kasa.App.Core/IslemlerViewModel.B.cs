using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>22 · Rapordan İşlemler'e iniş: Haftalık/Aylık/Kasa dökümündeki rakama dokununca gelinen süzgeç.</summary>
public partial class IslemlerViewModel
{
    /// <summary>
    /// Rapordan gelen gider tipi süzgeci (null = tüm tipler). Sunucu süzer (etkin tip: karta bağlı işlem
    /// K.K sayılır): sayfalar, "N işlem" toplamı ve Excel'e aktar da süzülmüş listeye göredir.
    /// "Tüm tipler" ya da yeni bir rapor inişi temizler.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TipSuzgeciVar), nameof(TipSuzgeciMetni))]
    private IslemTipSuzgeci? _suzgecTip;

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

    /// <summary>Liste sayfası; tip süzgeci varsa sunucu tipe göre süzer (toplam kayıt da süzülmüştür).</summary>
    private Task<IslemSayfasi> IslemSayfasiAsync(DateOnly? bas, DateOnly? bit, string? kanal, string? cari, int limit, int offset)
        => SuzgecTip is { } t
            ? _api.IslemSayfasiTipeGoreAsync(bas, bit, kanal, t, limit, offset)
            : _api.IslemSayfasiAsync(bas, bit, kanal, cari, limit, offset);

    /// <summary>Excel'e aktar: listeyle aynı süzgeç (tip dahil).</summary>
    private Task<IndirilenDosya> IslemlerCsvAsync(DateOnly? bas, DateOnly? bit, string? kanal)
        => SuzgecTip is { } t ? _api.IslemlerCsvTipeGoreAsync(bas, bit, kanal, t) : _api.IslemlerCsvAsync(bas, bit, kanal);
}
