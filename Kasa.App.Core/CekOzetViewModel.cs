using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Panelin Çekler kutusu (docs/specs/2026-10-01-cekler.md "Panel"): GET /api/takip/cekler/ozet'ten üç satır. Teminat
/// çekleri sunucuda dışarıda bırakılır. Kutudaki düğmeler Çekler sayfasını ilgili hazır süzgeçle açar (//cekler?Suzgec=…).</summary>
public partial class CekOzetViewModel(ICekApi api, AuthViewModel auth) : OturumluViewModel(auth)
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Alinan30Metni), nameof(Verilen30Metni), nameof(GecmisMetni))]
    private CekOzetDto? _ozet;

    public string Alinan30Metni => Ozet is { } o ? CekMetni.OzetSatiri("30 gün içinde tahsil edilecek", o.Alinan30) : "";
    public string Verilen30Metni => Ozet is { } o ? CekMetni.OzetSatiri("30 gün içinde ödenecek", o.Verilen30) : "";
    public string GecmisMetni => Ozet is { } o ? CekMetni.OzetSatiri("Vadesi geçmiş, tahsil edilmemiş", o.VadesiGecmis) : "";

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
