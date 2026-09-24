using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket C · 32: silmede ikinci onay ve "Silindi · Geri al" şeridi. Mevcut SilCommand aynen
// çalışır; düğmeler bu onaylı komuta bağlanır. Silme başarılıysa liste tazelemesi hata verse de
// şerit gösterilir.
public partial class IslemlerViewModel
{
    private SilmeOnayi? _silme;
    public SilmeOnayi Silme => _silme ??= new SilmeOnayi(_api, Zaman);

    /// <summary>İlk basış onay ister; süresi içinde ikinci basış siler ve geri al şeridini hazırlar.</summary>
    [RelayCommand]
    private async Task OnayliSilAsync(IslemDto i)
    {
        if (!Silme.OnayIste(i, geriAlinabilir: true)) return;
        // Silme başarılıysa liste tazelemesi hata verse de şerit gösterilir (Hata ayrıca görünür).
        var silindi = await SilVeTazeleAsync(async () =>
        {
            await _api.IslemSilAsync(i.Id);
            if (DuzenId == i.Id) Yeni();                 // silinen kayıt formda kalmasın
        }, IslemleriYukleAsync);
        if (!silindi) return;
        if (SonKaydedilen is { } s && s.Id == i.Id) SonKaydedilen = null;
        await Silme.SilindiAsync(GecmisTurAdlari.Islem, i.Id,
            $"{i.Tarih.ToString("d MMM", Kultur.Turkce)} · {i.Cari} · {Bicim.Tl(i.TutarTl)} ₺");
    }

    [RelayCommand]
    private Task SilmeGeriAlAsync() => CalistirAsync(async () =>
    {
        await Silme.GeriAlAsync();
        await IslemleriYukleAsync();
    });

    [RelayCommand]
    private void SilmeSeridiKapat() => Silme.Kapat();
}
