using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kasa.Api.Tests;

/// <summary>Paket B testleri: ayarlanabilir saat (varsayılan 24.09.2026) ve sahte TCMB.</summary>
public class PaketBFactory : KasaWebFactory
{
    public TekrarlayanGiderTests.AyarliSaat Saat { get; } = new();
    public SahteTcmb Tcmb { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s =>
        {
            s.Replace(ServiceDescriptor.Singleton<TimeProvider>(Saat));
            s.AddHttpClient<TcmbKurServisi>().ConfigurePrimaryHttpMessageHandler(() => Tcmb);
        });
    }

    /// <summary>Doğrudan DB erişimi (tohumlama/temizlik; HTTP dışı yazma geçmişe yazılmaz).</summary>
    public void Db(Action<KasaDbContext> islem)
    {
        using var scope = Services.CreateScope();
        islem(scope.ServiceProvider.GetRequiredService<KasaDbContext>());
    }

    public T Db<T>(Func<KasaDbContext, T> islem)
    {
        using var scope = Services.CreateScope();
        return islem(scope.ServiceProvider.GetRequiredService<KasaDbContext>());
    }

    /// <summary>İşlem/gelen/çek/ödeme/sayım/kilit/yayın/hedef/kur tablolarını boşaltır.</summary>
    public void Temizle() => Db(db =>
    {
        db.AyKilitleri.ExecuteDelete();
        db.AyYayinlari.ExecuteDelete();
        db.KanalHedefleri.ExecuteDelete();
        db.GiderButceleri.ExecuteDelete();
        db.Kurlar.ExecuteDelete();
        db.TekrarlayanGirisler.ExecuteDelete();
        db.TekrarlayanGiderler.ExecuteDelete();
        db.Islemler.ExecuteDelete();
        db.Gelenler.ExecuteDelete();
        db.Cekler.ExecuteDelete();
        db.KartOdemeler.ExecuteDelete();
        db.KasaSayimlari.ExecuteDelete();
        db.KrediKartlari.ExecuteDelete();
    });
}

/// <summary>Sahte TCMB: gün adresine göre yanıt üretir, istenen adresleri kaydeder.</summary>
public sealed class SahteTcmb : HttpMessageHandler
{
    private readonly object _kilit = new();
    public List<Uri> Istekler { get; } = new();
    public Func<HttpRequestMessage, HttpResponseMessage> Yanit { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_kilit) Istekler.Add(request.RequestUri!);
        return Task.FromResult(Yanit(request));
    }

    public static string Xml(decimal usd, decimal eur, int birim = 1) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Tarih_Date Tarih="01.09.2026" Date="09/01/2026" Bulten_No="2026/165">
          <Currency CrossOrder="0" Kod="USD" CurrencyCode="USD">
            <Unit>{birim}</Unit><Isim>ABD DOLARI</Isim><CurrencyName>US DOLLAR</CurrencyName>
            <ForexBuying>1.0</ForexBuying><ForexSelling>{usd.ToString(System.Globalization.CultureInfo.InvariantCulture)}</ForexSelling>
          </Currency>
          <Currency CrossOrder="9" Kod="EUR" CurrencyCode="EUR">
            <Unit>{birim}</Unit><Isim>EURO</Isim><CurrencyName>EURO</CurrencyName>
            <ForexBuying>1.0</ForexBuying><ForexSelling>{eur.ToString(System.Globalization.CultureInfo.InvariantCulture)}</ForexSelling>
          </Currency>
        </Tarih_Date>
        """;
}

public static class PaketB
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<HttpClient> IzleyiciAsync(KasaWebFactory f)
    {
        var editor = await f.EditorClientAsync();
        (await editor.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izle-b" })).EnsureSuccessStatusCode();
        var izleyici = f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = (string?)null, sifre = "izle-b" })).EnsureSuccessStatusCode();
        return izleyici;
    }

    public static string G(DateOnly d) => d.ToString("yyyy-MM-dd");

    public static async Task<HttpResponseMessage> IslemYaz(HttpClient c, DateOnly tarih, decimal tutar, string kanal = "MEZAT",
        string tip = "Cari", string cari = "X", int? kart = null)
        => await c.PostAsJsonAsync("/api/islemler", new { tarih = G(tarih), cari, tutarTl = tutar, kanal, tip, krediKartiId = kart });

    public static async Task<int> IslemEkle(HttpClient c, DateOnly tarih, decimal tutar, string kanal = "MEZAT",
        string tip = "Cari", string cari = "X", int? kart = null)
    {
        var r = await IslemYaz(c, tarih, tutar, kanal, tip, cari, kart);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    public static async Task GelenYaz(HttpClient c, DateOnly tarih, string kanal, decimal tutar)
        => (await c.PutAsJsonAsync("/api/gelenler", new { donemStart = G(tarih), kanal, tutarTl = tutar })).EnsureSuccessStatusCode();

    public static async Task<JsonElement> Oku(HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        Assert.True(r.IsSuccessStatusCode, $"{url} → {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<string> HataMetni(HttpResponseMessage r)
        => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hata").GetString()!;

    public static decimal D(JsonElement e, string ad) => e.GetProperty(ad).GetDecimal();
}
