using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket C · 32: silmede ikinci onay + "Silindi · Geri al" şeridi (mevcut SilCommand aynen çalışır).
public partial class KasaSayimiViewModel
{
    private SilmeOnayi? _silme;
    public SilmeOnayi Silme => _silme ??= new SilmeOnayi(_api, Zaman);

    [RelayCommand]
    private async Task OnayliSilAsync(KasaSayimSatiri s)
    {
        if (!Silme.OnayIste(s, geriAlinabilir: true)) return;
        var silindi = await SilVeTazeleAsync(() => _api.KasaSayimSilAsync(s.Id), ListeyiYukleAsync);
        if (!silindi) return;                            // tazeleme hatası şeridi engellemez
        await Silme.SilindiAsync(GecmisTurAdlari.KasaSayimi, s.Id,
            $"{s.Tarih.ToString("d MMM yyyy", Kultur.Turkce)} sayımı · {Bicim.Tl(s.SayilanTutar)} ₺");
    }

    [RelayCommand]
    private Task SilmeGeriAlAsync() => CalistirAsync(async () =>
    {
        await Silme.GeriAlAsync();
        await ListeyiYukleAsync();
    });

    [RelayCommand]
    private void SilmeSeridiKapat() => Silme.Kapat();
}
