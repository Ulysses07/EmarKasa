using CommunityToolkit.Mvvm.Input;

namespace Kasa.App.Core;

// Paket C · 32 ile ortak: silmede ikinci onay. Mutabakat kaydının silinmesi geri alınamaz (geçmişten geri
// getirilen türlerden değil), bu yüzden düğme ilk basışta "Geri alınamaz · Emin misiniz?" olur ve yalnız
// süresi içindeki ikinci basış siler. Şerit yok. Mevcut Sil komutu aynen çalışır.
public partial class KartMutabakatViewModel
{
    private SilmeOnayi? _silme;
    public SilmeOnayi Silme => _silme ??= new SilmeOnayi(_api, Zaman);

    [RelayCommand]
    private async Task OnayliSilAsync()
    {
        if (Detay is not { MutabakatId: { } id }) return;
        if (!Silme.OnayIste(id, geriAlinabilir: false)) return;
        await SilCommand.ExecuteAsync(null);
    }
}
