using Kasa.App.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.App.Views;

/// <summary>
/// Paket A — bildirim anahtarları. BindingContext kendi VM'idir (bulunduğu sayfanın VM'inden bağımsız);
/// değerler yerel depoda (bu bilgisayar) saklanır, arka plan hatırlatıcısı da aynı depoyu okur.
/// </summary>
public partial class BildirimAyarlariView : ContentView
{
    public BildirimAyarlariView()
    {
        InitializeComponent();
        BindingContext = IPlatformApplication.Current?.Services.GetService<BildirimAyarlariViewModel>()
                         ?? new BildirimAyarlariViewModel(DosyaYerelDepo.Varsayilan());
    }
}
