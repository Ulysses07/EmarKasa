using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;

namespace Kasa.Api.Tests;

/// <summary>
/// Ürün sürümünün tek kaynağı (Aşama 4): depo kökündeki Directory.Build.props'taki KasaSurumu. Kasa.Api ve Kasa.App.Core onu
/// Version, Kasa.App ApplicationDisplayVersion olarak derlemeye yazar; kod sürümü derleme meta verisinden okur, elle yazılmış
/// kopya kalmaz. Dockerfile dosyayı imaja kopyalar (yoksa Kasa.Api derlemesi durur). minimumIstemci ayrı bir kavramdır
/// (desteklenen en eski istemci) ve KasaSurumu'na bağlı değildir.
/// </summary>
public class SurumKaynagiTests
{
    [Fact]
    public async Task Surum_ucu_yaniti_degismedi_surum_KasaSurumu_ndan_gelir()
    {
        await using var f = new KasaWebFactory();
        using var c = f.CreateClient();
        var yanit = await c.GetFromJsonAsync<JsonElement>("/api/surum");
        // Aşama 4 öncesindeki yanıtla aynı alanlar, aynı sırada ve aynı değerlerle; yalnız "surum"un kaynağı değişti.
        Assert.Equal(["surum", "minimumIstemci", "indirmeAdresi", "notlar"], yanit.EnumerateObject().Select(p => p.Name));
        Assert.Equal(KasaSurumu(), yanit.GetProperty("surum").GetString());
        Assert.Equal("2.3.0", yanit.GetProperty("minimumIstemci").GetString());
        Assert.Equal(JsonValueKind.Null, yanit.GetProperty("indirmeAdresi").ValueKind);
        Assert.Equal("Kart ekstresi ve banka hesap hareketi PDF yükleme, seçilen hareketleri önizleyerek işleme ve tekrar kayıt kontrolü.",
            yanit.GetProperty("notlar").GetString());
    }

    [Fact]
    public void Sunucu_surumu_derleme_meta_verisinden_okunur()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", KasaSurumu());
        Assert.Equal(KasaSurumu(), SunucuSurumu.Deger);
        // .NET 8+ SDK git deposunda bilgi sürümüne "+<commit>" ekler; bildirilen sürüm yalnız KasaSurumu'dur.
        Assert.Equal("2.4.1", SunucuSurumu.Ayikla("2.4.1+204ff0a"));
        Assert.Equal("2.4.1", SunucuSurumu.Ayikla("2.4.1"));
        Assert.Throws<InvalidOperationException>(() => SunucuSurumu.Ayikla(null));
    }

    [Theory]
    [InlineData("Kasa.Api", "Version")]
    [InlineData("Kasa.App.Core", "Version")]
    [InlineData("Kasa.App", "ApplicationDisplayVersion")]
    public void Projeler_surumu_KasaSurumu_ndan_alir(string proje, string ozellik)
    {
        var belge = XDocument.Load(DepoDosyasi(proje, proje + ".csproj"));
        Assert.Equal("$(KasaSurumu)", Assert.Single(belge.Descendants(ozellik)).Value.Trim());
    }

    [Fact]
    public void Dockerfile_Directory_Build_props_u_derlemeden_once_kopyalar()
    {
        var satirlar = File.ReadAllLines(DepoDosyasi("Dockerfile")).Select(s => s.Trim()).ToList();
        var kopya = satirlar.FindIndex(s => s.StartsWith("COPY Directory.Build.props ", StringComparison.Ordinal));
        Assert.True(kopya >= 0, "Dockerfile Directory.Build.props'u kopyalamıyor: KasaSurumu olmadan Kasa.Api derlenmez.");
        Assert.True(kopya < satirlar.FindIndex(s => s.StartsWith("RUN dotnet restore", StringComparison.Ordinal)), "Directory.Build.props geri yüklemeden önce kopyalanmalı.");
    }

    /// <summary>Kaynak kodda KasaSurumu'nun elle yazılmış kopyası yok; yalnız ayrı kavram olan minimumIstemci aynı değeri
    /// taşıyabilir (bugün 2.3.0) ve kendi satırında açıkça adlandırılmıştır.</summary>
    [Fact]
    public void Kaynak_kodda_elle_yazilmis_surum_dizesi_yok()
    {
        var aranan = $"\"{KasaSurumu()}\"";
        var bulunan = new[] { "Kasa.Core", "Kasa.Api", "Kasa.ApiClient", "Kasa.App.Core", "Kasa.App" }
            .SelectMany(p => Directory.EnumerateFiles(DepoDosyasi(p), "*.cs", SearchOption.AllDirectories))
            .Where(d => !d.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !d.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(d => File.ReadLines(d).Select((s, i) => (Dosya: d, No: i + 1, Satir: s)))
            .Where(x => x.Satir.Contains(aranan, StringComparison.Ordinal) && !x.Satir.Contains("const string MinimumIstemci", StringComparison.Ordinal))
            .Select(x => $"{Path.GetRelativePath(DepoDosyasi(), x.Dosya)}:{x.No}: {x.Satir.Trim()}")
            .ToList();
        Assert.True(bulunan.Count == 0, "Sürüm Directory.Build.props'taki KasaSurumu'ndan okunmalı:\n" + string.Join("\n", bulunan));
    }

    private static string KasaSurumu() =>
        XDocument.Load(DepoDosyasi("Directory.Build.props")).Descendants("KasaSurumu").Single().Value.Trim();

    private static string DepoDosyasi(params string[] parcalar)
    {
        for (var dizin = new DirectoryInfo(AppContext.BaseDirectory); dizin is not null; dizin = dizin.Parent)
            if (File.Exists(Path.Combine(dizin.FullName, "Kasa.slnx")))
                return Path.Combine([dizin.FullName, .. parcalar]);
        throw new InvalidOperationException("Depo kökü (Kasa.slnx) test çıktısının üst dizinlerinde bulunamadı.");
    }
}
