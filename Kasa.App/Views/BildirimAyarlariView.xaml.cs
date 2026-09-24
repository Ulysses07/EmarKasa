using Kasa.App.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.App.Views;

/// <summary>
/// Paket A — bildirim anahtarları. BindingContext kendi VM'idir (bulunduğu sayfanın VM'inden bağımsız);
/// değerler yerel depoda (bu bilgisayar) saklanır, arka plan hatırlatıcısı da aynı depoyu okur.
/// VM tektir (Ayarlar ve Panel → Bildirimler aynı nesneyi gösterir); Shell'in sakladığı Ayarlar sayfası
/// yeniden gösterilince de değerler depodan tazelenir.
/// </summary>
public partial class BildirimAyarlariView : ContentView
{
    public BildirimAyarlariView()
    {
        InitializeComponent();
        var vm = IPlatformApplication.Current?.Services.GetService<BildirimAyarlariViewModel>()
                 ?? new BildirimAyarlariViewModel(DosyaYerelDepo.Varsayilan());
        BindingContext = vm;
        Loaded += (_, _) => vm.Yenile();
    }
}
