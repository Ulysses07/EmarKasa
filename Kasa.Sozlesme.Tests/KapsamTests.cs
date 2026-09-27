using System.Reflection;
using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>Kapsam bekçisi: KasaApiClient'ın uyguladığı arayüzlerdeki her metot en az bir sözleşme testinde gerçek
/// sunucuya karşı çağrılır. Arayüze yeni metot eklenip sözleşme testi yazılmazsa bu test metodun adıyla kırılır.</summary>
public class KapsamTests
{
    private static IReadOnlyList<(string Arayuz, string Metot)> IstemciMetotlari() => typeof(KasaApiClient).GetInterfaces()
        .SelectMany(a => a.GetMethods().Where(m => !m.IsSpecialName).Select(m => (a.Name, m.Name))).Distinct().ToList();

    private static IReadOnlyList<(string Test, string Metot)> Isaretler() => typeof(KapsamTests).Assembly.GetTypes()
        .SelectMany(t => t.GetMethods()).SelectMany(m => m.GetCustomAttributes<SozlesmeKapsamiAttribute>()
            .SelectMany(a => a.Metotlar.Select(x => ($"{m.DeclaringType!.Name}.{m.Name}", x)))).ToList();

    [Fact]
    public void Istemci_arayuzlerindeki_her_metot_bir_sozlesme_testinde_cagrilir()
    {
        var metotlar = IstemciMetotlari();
        Assert.True(metotlar.Count > 90, $"İstemci metotları okunamadı ({metotlar.Count}).");
        var isaretli = Isaretler().Select(i => i.Metot).ToHashSet();
        var eksik = metotlar.Where(m => !isaretli.Contains(m.Metot)).Select(m => $"{m.Arayuz}.{m.Metot}").ToList();
        Assert.True(eksik.Count == 0, "Sözleşme testi olmayan istemci metotları (gerçek sunucuya karşı bir test yazıp [SozlesmeKapsami] ile işaretleyin):\n" + string.Join("\n", eksik));
    }

    [Fact]
    public void Kapsam_isaretleri_var_olan_metotlari_gosterir()
    {
        var adlar = IstemciMetotlari().Select(m => m.Metot).ToHashSet();
        var bilinmeyen = Isaretler().Where(i => !adlar.Contains(i.Metot)).Select(i => $"{i.Test}: {i.Metot}").ToList();
        Assert.True(bilinmeyen.Count == 0, "İstemcide bulunmayan metot işaretleri: " + string.Join(", ", bilinmeyen));
    }

    [Fact]
    public void Izin_listesi_satirlari_gerekceli_ve_tekil()
    {
        Assert.All(SozlesmeIzinleri.Liste, i => Assert.False(string.IsNullOrWhiteSpace(i.Gerekce)));
        Assert.Equal(SozlesmeIzinleri.Liste.Count, SozlesmeIzinleri.Liste.Select(i => (i.Yon, i.Tur, i.Alan.ToLowerInvariant(), i.Uc)).Distinct().Count());
    }
}
