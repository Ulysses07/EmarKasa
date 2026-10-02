using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Panelin Çekler kutusu (docs/specs/2026-10-01-cekler.md "Panel"): GET /api/takip/cekler/ozet'ten dört satır. Teminat
/// çekleri sunucuda dışarıda bırakılır. Kutudaki düğmeler Çekler sayfasını ilgili hazır süzgeçle açar (//cekler?Suzgec=…).</summary>
public partial class CekOzetViewModel(ICekApi api, AuthViewModel auth) : OturumluViewModel(auth)
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Alinan30Metni), nameof(Verilen30Metni), nameof(GecmisMetni), nameof(VerilenGecmisMetni))]
    private CekOzetDto? _ozet;

    public string Alinan30Metni => CekMetni.Alinan30Metni(Ozet);
    public string Verilen30Metni => CekMetni.Verilen30Metni(Ozet);
    public string GecmisMetni => CekMetni.GecmisMetni(Ozet);
    public string VerilenGecmisMetni => CekMetni.VerilenGecmisMetni(Ozet);

    public Task YukleAsync() => YurutAsync(async n =>
    {
        var ozet = await api.CekOzetAsync();
        if (!Gecerli(n))
            return;
        Ozet = ozet;
        Tamamlandi();
    });

    protected override void OturumTemizle() => Ozet = null;
}
