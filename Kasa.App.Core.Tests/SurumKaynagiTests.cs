using System.Xml.Linq;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Uygulama sürümünün tek kaynağı depo kökündeki Directory.Build.props'taki KasaSurumu'dur (Aşama 4): Kasa.App.Core onu
/// Version olarak derlemeye yazar, GuvenlikViewModel derleme meta verisinden okur; kodda elle yazılmış sürüm dizesi yoktur.</summary>
public class SurumKaynagiTests
{
    [Fact]
    public void Istemci_surumu_Directory_Build_props_KasaSurumu_ile_ayni()
    {
        var kaynak = KasaSurumu();
        Assert.Matches(@"^\d+\.\d+\.\d+$", kaynak);
        Assert.Equal(kaynak, GuvenlikViewModel.IstemciSurumu);
        // Ekranda gösterilen metin değişmedi: "Uygulama <sürüm>".
        var vm = new GuvenlikViewModel(new KasaApiClient(new HttpClient(), new BellekTokenStore()), new AuthViewModel(new SahteApi()));
        Assert.Equal($"Uygulama {kaynak}", vm.SurumBilgisi);
    }

    internal static string KasaSurumu()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Kasa.slnx")))
                return XDocument.Load(Path.Combine(d.FullName, "Directory.Build.props")).Descendants("KasaSurumu").Single().Value.Trim();
        throw new InvalidOperationException("Depo kökü (Kasa.slnx) bulunamadı.");
    }
}
