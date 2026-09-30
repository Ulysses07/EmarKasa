using System.Net;
using System.Net.Http.Json;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>
/// Oluşturma uçlarının tekrar anahtarı (appcore-5): istemci 15 sn'de isteği kesip aynı kaydı yeniden gönderdiğinde
/// (POST /api/islemler, POST /api/alis) aynı istek kimliği ve aynı içerik ilk sonucu döndürür, ikinci kayıt oluşmaz; aynı
/// kimlik farklı içerikle 409 alır. İstek kimliği göndermeyen eski istemci eskisi gibi her istekte yeni kayıt açar.
/// </summary>
public class OlusturmaTekrarTests
{
    private static readonly DateOnly Bugun = KasaWebFactory.VarsayilanBugun;

    private static object Gider(Guid? istekId, decimal tutar = 75m, string cari = "Kargo")
        => new { tarih = Bugun, cari, tutarTl = tutar, kanal = "MEZAT", tip = "Cari", not = (string?)null, krediKartiId = (int?)null, istekId };

    private static int Say<T>(KasaWebFactory f, Func<KasaDbContext, IQueryable<T>> sorgu)
    {
        using var scope = f.Services.CreateScope();
        return sorgu(scope.ServiceProvider.GetRequiredService<KasaDbContext>()).Count();
    }

    [Fact]
    public async Task Ayni_istek_kimligiyle_gider_iki_kez_gonderilince_tek_kayit_olusur()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        var istekId = Guid.NewGuid();

        using var ilk = await c.PostAsJsonAsync("/api/islemler", Gider(istekId), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, ilk.StatusCode);
        using var tekrar = await c.PostAsJsonAsync("/api/islemler", Gider(istekId), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, tekrar.StatusCode);
        static async Task<(int, decimal, string?)> Oku(HttpResponseMessage r)
        {
            var j = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            return (j.GetProperty("id").GetInt32(), j.GetProperty("tutarTl").GetDecimal(), j.GetProperty("cari").GetString());
        }
        Assert.Equal(await Oku(ilk), await Oku(tekrar));
        Assert.Equal(1, Say(f, db => db.Islemler.Where(i => i.Cari == "Kargo")));

        using var farkli = await c.PostAsJsonAsync("/api/islemler", Gider(istekId, 80m), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, farkli.StatusCode);
        Assert.Equal(1, Say(f, db => db.Islemler.Where(i => i.Cari == "Kargo")));

        using var bos = await c.PostAsJsonAsync("/api/islemler", Gider(Guid.Empty), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, bos.StatusCode);
    }

    [Fact]
    public async Task Istek_kimligi_gondermeyen_eski_istemci_her_istekte_yeni_gider_acar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        for (var i = 0; i < 2; i++)
            (await c.PostAsJsonAsync("/api/islemler", new IslemYazDto(Bugun, "Eski istemci", 10m, "MEZAT", Kasa.Core.GiderTipi.Cari), cancellationToken: ct)).EnsureSuccessStatusCode();
        Assert.Equal(2, Say(f, db => db.Islemler.Where(i => i.Cari == "Eski istemci")));
    }

    [Fact]
    public async Task Silinmis_giderin_tekrari_yeniden_olusturmaz_409_doner()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await f.EditorClientAsync();
        var istekId = Guid.NewGuid();
        using var ilk = await c.PostAsJsonAsync("/api/islemler", Gider(istekId, cari: "Silinecek"), cancellationToken: TestContext.Current.CancellationToken);
        var id = (await ilk.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetInt32();
        (await c.DeleteAsync($"/api/islemler/{id}", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        using var tekrar = await c.PostAsJsonAsync("/api/islemler", Gider(istekId, cari: "Silinecek"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, tekrar.StatusCode);
        Assert.Contains("sonradan silinmiş", await AlisTestYardimcisi.Hata(tekrar));
        Assert.Equal(0, Say(f, db => db.Islemler.Where(i => i.Cari == "Silinecek")));
    }

    [Fact]
    public async Task Ayni_istek_kimligiyle_alis_iki_kez_gonderilince_tek_taslak_olusur_baska_alici_kullanamaz()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var editor = await f.EditorClientAsync();
        using var alici = await AlisTestYardimcisi.Alici(f, editor, "tekrar-a");
        using var baska = await AlisTestYardimcisi.Alici(f, editor, "tekrar-b");
        var istekId = Guid.NewGuid();
        object Govde(string tedarikci) => new
        {
            surum = 0,
            tarih = Bugun,
            tedarikci,
            not = (string?)null,
            istekId,
            kalemler = new[] { new { aciklama = "Mal", tutar = 100m, dagilimlar = new[] { new { kanalId = 1, tutar = 100m } } } },
        };

        using var ilk = await alici.PostAsJsonAsync("/api/alis", Govde("Tekrar Ltd"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, ilk.StatusCode);
        using var tekrar = await alici.PostAsJsonAsync("/api/alis", Govde("Tekrar Ltd"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, tekrar.StatusCode);
        var ilkAlis = (await ilk.Content.ReadFromJsonAsync<AlisDto>(cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal(ilkAlis, (await tekrar.Content.ReadFromJsonAsync<AlisDto>(cancellationToken: TestContext.Current.CancellationToken))!, AlisEsitligi.Ornek);
        Assert.Equal(1, Say(f, db => db.Alislar.Where(a => a.Tedarikci == "Tekrar Ltd")));

        using var farkli = await alici.PostAsJsonAsync("/api/alis", Govde("Başka Ltd"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, farkli.StatusCode);
        // Başka alıcı aynı kimliği (ve aynı gövdeyi) gönderse de ilk alıcının alışını göremez.
        using var yabanci = await baska.PostAsJsonAsync("/api/alis", Govde("Tekrar Ltd"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, yabanci.StatusCode);
        Assert.Equal(1, Say(f, db => db.Alislar.Where(a => a.Tedarikci == "Tekrar Ltd")));

        // Editör istek kimliği göndermezse (eski istemci) her istek yeni taslaktır.
        for (var i = 0; i < 2; i++)
            await AlisTestYardimcisi.Taslak(editor, "Eski editör");
        Assert.Equal(2, Say(f, db => db.Alislar.Where(a => a.Tedarikci == "Eski editör")));
    }

    /// <summary>Tekrar yanıtı ilk yanıtla aynı alışı taşır (liste alanları referansla değil içerikle karşılaştırılır).</summary>
    private sealed class AlisEsitligi : IEqualityComparer<AlisDto>
    {
        public static readonly AlisEsitligi Ornek = new();
        public bool Equals(AlisDto? x, AlisDto? y) => x is not null && y is not null
            && System.Text.Json.JsonSerializer.Serialize(x) == System.Text.Json.JsonSerializer.Serialize(y);
        public int GetHashCode(AlisDto obj) => obj.Id;
    }
}
