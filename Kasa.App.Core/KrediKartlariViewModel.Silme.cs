using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket C · 32: silmede ikinci onay. Kart silmesi geri alınamaz (onay metni söyler); kart ödemesi
// geri alınabilir ("Silindi · Geri al" şeridi). Mevcut Sil komutları aynen çalışır.
public partial class KrediKartlariViewModel
{
    private SilmeOnayi? _silme;
    public SilmeOnayi Silme => _silme ??= new SilmeOnayi(_api, Zaman);

    [RelayCommand]
    private async Task OnayliSilAsync(KrediKartiGorunum k)
    {
        if (!Silme.OnayIste(k, geriAlinabilir: false)) return;
        await SilCommand.ExecuteAsync(k);
    }

    [RelayCommand]
    private async Task OnayliOdemeSilAsync(KartOdemeDto o)
    {
        if (!Silme.OnayIste(o, geriAlinabilir: true)) return;
        var kart = Kartlar.FirstOrDefault(k => k.Id == o.KrediKartiId)?.Ad ?? "Kart";
        var silindi = await SilVeTazeleAsync(() => _api.KartOdemeSilAsync(o.Id), () => DoldurAsync());
        if (!silindi) return;                            // tazeleme hatası şeridi engellemez
        await Silme.SilindiAsync(GecmisTurAdlari.KartOdemesi, o.Id,
            $"{kart} ödemesi · {o.Tarih.ToString("d MMM yyyy", Kultur.Turkce)} · {Bicim.Tl(o.Tutar)} ₺");
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
