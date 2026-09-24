using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket B · 40: Çekler, Kasa Sayımı ve Geçmiş sayfalarına "Excel'e aktar". Diğer sayfalardaki gibi
// dosya Belgeler\Emar Kasa'ya kaydedilir ve açılır; kaydedici DI ile ikinci kurucudan gelir.

public partial class CeklerViewModel
{
    private readonly IDosyaKaydedici? _kaydedici;

    public CeklerViewModel(IKasaApi api, TimeProvider? zaman, IDosyaKaydedici? kaydedici) : this(api, zaman)
        => _kaydedici = kaydedici;

    /// <summary>Son "Excel'e aktar"ın kaydettiği dosyanın tam yolu (sayfada gösterilir).</summary>
    [ObservableProperty] private string? _aktarilanDosya;

    /// <summary>Seçili yön/durum süzgecine uyan tüm çekler (yön başına toplam satırıyla).</summary>
    [RelayCommand]
    private Task ExceleAktarAsync() => CalistirAsync(async () =>
    {
        var (yon, durum) = (FiltreYon, FiltreDurum);
        AktarilanDosya = null;
        AktarilanDosya = await ExcelAktarma.AktarAsync(_kaydedici, () => _api.CeklerCsvAsync(yon, durum));
    });
}

public partial class KasaSayimiViewModel
{
    private readonly IDosyaKaydedici? _kaydedici;

    public KasaSayimiViewModel(IKasaApi api, TimeProvider? zaman, IDosyaKaydedici? kaydedici) : this(api, zaman)
        => _kaydedici = kaydedici;

    [ObservableProperty] private string? _aktarilanDosya;

    /// <summary>Tüm sayımlar (sayılan, o günkü defter, fark, bugünkü defter, not).</summary>
    [RelayCommand]
    private Task ExceleAktarAsync() => CalistirAsync(async () =>
    {
        AktarilanDosya = null;
        AktarilanDosya = await ExcelAktarma.AktarAsync(_kaydedici, _api.KasaSayimlariCsvAsync);
    });
}

public partial class GecmisViewModel
{
    private readonly IDosyaKaydedici? _kaydedici;

    public GecmisViewModel(IKasaApi api, TimeProvider? zaman, IDosyaKaydedici? kaydedici) : this(api, zaman)
        => _kaydedici = kaydedici;

    [ObservableProperty] private string? _aktarilanDosya;

    /// <summary>Seçili tür süzgecine uyan tüm geçmiş satırları (yüklenmemiş eski sayfalar dahil).</summary>
    [RelayCommand]
    private Task ExceleAktarAsync() => CalistirAsync(async () =>
    {
        var tur = FiltreTur;
        AktarilanDosya = null;
        AktarilanDosya = await ExcelAktarma.AktarAsync(_kaydedici, () => _api.GecmisCsvAsync(tur));
    });
}
