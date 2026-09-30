using Kasa.Core.Kodlar;

namespace Kasa.Core.Tests;

/// <summary>Kasa.Core.Kodlar değerleri API sözleşmesi ve veritabanı değeridir: burada dize olarak sabitlenir, sabit yanlışlıkla
/// değişirse test kırmızı olur. Beklenen değerler bilerek literal yazılır.</summary>
public class KodlarTests
{
    [Fact]
    public void Kanal_etiketleri() => Esit(
        ("Ortak", KanalEtiketleri.Ortak), ("Dağılım bekliyor", KanalEtiketleri.DagilimBekliyor), ("Genel kasa", KanalEtiketleri.GenelKasa));

    [Fact]
    public void Alis_durumlari() => Esit(
        ("Taslak", AlisDurumlari.Taslak), ("Incelemede", AlisDurumlari.Incelemede), ("Onaylandi", AlisDurumlari.Onaylandi));

    private static void Esit(params (string Beklenen, string Gercek)[] ciftler) =>
        Assert.All(ciftler, c => Assert.Equal(c.Beklenen, c.Gercek));
}
