using Kasa.App.Core;
using static Kasa.App.Views.TakipUi;

namespace Kasa.App.Views;

/// <summary>Cariler (docs/specs/2026-11-25-erp12-aktarimi.md): ERP12'den aktarılan hareketlerin cari listesi; carinin hareketleri listenin üstünde açılır.</summary>
public sealed class CarilerPage : TakipSayfasi<CarilerViewModel>
{
    public CarilerPage(CarilerViewModel vm) : base(vm, "Cariler",
        "ERP12'den aktarılan cari hareketleri. Kayıtların aslı ERP12'dedir; bakiye yalnız aktarılan hareketlere göredir ve kasayı etkilemez.", vm.YukleAsync)
    {
        var acik = Kart("Cari hareketleri", BagliBuyuk(nameof(vm.AcikBasligi)), Liste<CariHareketSatiri>(nameof(vm.Hareketler)));
        Govde.Add(Kart("Özet", Bagli(nameof(vm.Ozet)), Alan("Cari adı ya da kodu", Girdi(nameof(vm.Ara)))));
        Govde.Add(Goster(acik, nameof(vm.CariAcik)));
        Govde.Add(Kart("Cariler", Liste<CariSatiri>(nameof(vm.Cariler), async s =>
        {
            await vm.SecCommand.ExecuteAsync(s);
            if (vm.CariAcik)
                Gorunur.Yap(acik, KaydirmaHesabi.FormKaydirmasi);
        }, "Hareketleri", aciklama: s => $"Cari hareketleri: {s.Baslik}")));
    }
}
