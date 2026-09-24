using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket C · 32: silmede ikinci onay + "Silindi · Geri al" şeridi (mevcut SilCommand aynen çalışır).
public partial class CeklerViewModel
{
    private SilmeOnayi? _silme;
    public SilmeOnayi Silme => _silme ??= new SilmeOnayi(_api, Zaman);

    [RelayCommand]
    private async Task OnayliSilAsync(CekGorunum g)
    {
        if (!Silme.OnayIste(g, geriAlinabilir: true)) return;
        var silindi = await SilVeTazeleAsync(async () =>
        {
            await _api.CekSilAsync(g.Id);
            if (DuzenId == g.Id) Yeni();
        }, YenileAsync);
        if (!silindi) return;                            // tazeleme hatası şeridi engellemez
        await Silme.SilindiAsync(GecmisTurAdlari.Cek, g.Id, $"{g.YonAdi} çek · {g.Kisi} · {Bicim.Tl(g.Tutar)} ₺");
    }

    [RelayCommand]
    private Task SilmeGeriAlAsync() => CalistirAsync(async () =>
    {
        await Silme.GeriAlAsync();
        await YenileAsync();
    });

    [RelayCommand]
    private void SilmeSeridiKapat() => Silme.Kapat();
}
