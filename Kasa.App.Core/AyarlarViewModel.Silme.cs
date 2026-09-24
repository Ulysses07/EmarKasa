using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket C · 32: Ayarlar listelerinde silmede ikinci onay. Kanal ve tekrarlayan gider silmesi geri
// alınamaz (onay metni söyler); gider kalemi geri alınabilir ("Silindi · Geri al" şeridi).
public partial class AyarlarViewModel
{
    private SilmeOnayi? _silme;
    public SilmeOnayi Silme => _silme ??= new SilmeOnayi(_api, Zaman);

    [RelayCommand]
    private async Task OnayliKanalSilAsync(KanalDto k)
    {
        if (!Silme.OnayIste(k, geriAlinabilir: false)) return;
        await KanalSilCommand.ExecuteAsync(k);
    }

    [RelayCommand]
    private async Task OnayliKalemSilAsync(GiderKalemiDto k)
    {
        if (!Silme.OnayIste(k, geriAlinabilir: true)) return;
        var silindi = await SilVeTazeleAsync(async () =>
        {
            await _api.GiderKalemiSilAsync(k.Id);
            if (DuzenKalemId == k.Id) YeniKalem();
        }, DoldurAsync);
        if (!silindi) return;                            // tazeleme hatası şeridi engellemez
        await Silme.SilindiAsync(GecmisTurAdlari.GiderKalemi, k.Id, $"gider kalemi {k.Ad}");
    }

    [RelayCommand]
    private async Task OnayliTekrarSilAsync(TekrarlayanGiderSatiri s)
    {
        if (!Silme.OnayIste(s, geriAlinabilir: false)) return;
        await TekrarSilCommand.ExecuteAsync(s);
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
