using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket C · 32: Cariler listesine silme (mevcut CariSilAsync ucu; işlemi olan cari sunucuda 409 ile
// reddedilir ve pasif yapılması önerilir), ikinci onay ve "Silindi · Geri al" şeridi.
public partial class CarilerViewModel
{
    private SilmeOnayi? _silme;
    public SilmeOnayi Silme => _silme ??= new SilmeOnayi(_api, Zaman);

    [RelayCommand]
    private Task SilAsync(CariDto c) => SilVeTazeleAsync(() => CariSilIcAsync(c), DoldurAsync);

    private async Task CariSilIcAsync(CariDto c)
    {
        await _api.CariSilAsync(c.Id);
        if (DuzenId == c.Id) Yeni();
    }

    [RelayCommand]
    private async Task OnayliSilAsync(CariDto c)
    {
        if (!Silme.OnayIste(c, geriAlinabilir: true)) return;
        var silindi = await SilVeTazeleAsync(() => CariSilIcAsync(c), DoldurAsync);
        if (!silindi) return;                            // tazeleme hatası şeridi engellemez
        await Silme.SilindiAsync(GecmisTurAdlari.Cari, c.Id, $"cari {c.Ad}");
    }

    [RelayCommand]
    private Task SilmeGeriAlAsync() => CalistirAsync(async () =>
    {
        await Silme.GeriAlAsync();
        await DoldurAsync();
    });

    [RelayCommand]
    private void SilmeSeridiKapat() => Silme.Kapat();
}
