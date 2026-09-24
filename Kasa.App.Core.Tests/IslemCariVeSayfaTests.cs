using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>API sözleşmesi: işlem carisi kayıtlı olmalı; işlem listesi sayfalı (limit/offset + X-Toplam-Kayit); 400/409 {hata} gösterilir.</summary>
public class IslemCariVeSayfaTests
{
    private static readonly DateTime Bugun = new(2026, 9, 24);

    private static (SahteApi api, IslemlerViewModel vm) Kur(Action<SahteApi>? ayar = null)
    {
        var api = new SahteApi
        {
            KanallarListe = new[] { new KanalDto(1, "MEZAT", true, 0, 0m) },
            DonemlerListe = new[] { new DonemDto(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 2026, 9) },
            CarilerListe = new[]
            {
                new CariDto(1, "IŞIK Ltd", true), new CariDto(2, "İnci Gıda", true),
                new CariDto(3, "Eski Tedarikçi", false), new CariDto(4, "Market", true),
            },
        };
        ayar?.Invoke(api);
        return (api, new IslemlerViewModel(api, new SabitSaat(Bugun.AddHours(10))));
    }

    private static void FormuDoldur(IslemlerViewModel vm, string cari)
    {
        vm.DuzenCari = cari; vm.DuzenTutar = 5m; vm.DuzenKanal = "MEZAT"; vm.DuzenTarih = Bugun;
    }

    // ---- Cari seçimi ----

    [Fact]
    public async Task Oneriler_turkce_buyuk_kucuk_harf_duyarsiz_ve_yalniz_aktif()
    {
        var (_, vm) = Kur();
        await vm.YukleAsync();

        vm.DuzenCari = "ışık";
        Assert.Equal(new[] { "IŞIK Ltd" }, vm.CariOnerileri);
        Assert.True(vm.CariOnerileriGorunur);

        vm.DuzenCari = "inci";
        Assert.Equal(new[] { "İnci Gıda" }, vm.CariOnerileri);

        vm.DuzenCari = "eski";
        Assert.Empty(vm.CariOnerileri);             // pasif cari önerilmez
        Assert.False(vm.CariOnerileriGorunur);
    }

    [Fact]
    public async Task Oneri_secilince_cari_atanir_ve_liste_kapanir()
    {
        var (_, vm) = Kur();
        await vm.YukleAsync();
        vm.DuzenCari = "mar";

        vm.SecCariCommand.Execute("Market");

        Assert.Equal("Market", vm.DuzenCari);
        Assert.False(vm.CariOnerileriGorunur);
    }

    [Fact]
    public async Task Kayitli_olmayan_cari_gonderilmez_ve_mesaj_gosterilir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        FormuDoldur(vm, "Yeni Firma");

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonIslemOlustur);
        Assert.Equal(IslemlerViewModel.CariYokMesaji("Yeni Firma"), vm.Hata);
        Assert.Equal("'Yeni Firma' adında bir cari yok. Önce Cariler sayfasından ekleyin.", vm.Hata);
    }

    [Fact]
    public async Task Bos_cari_gonderilmez()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        FormuDoldur(vm, "  ");

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Null(api.SonIslemOlustur);
        Assert.Equal(IslemlerViewModel.CariBosMesaji, vm.Hata);
    }

    [Fact]
    public async Task Yazim_farki_kayitli_yazima_cevrilir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        FormuDoldur(vm, " ışık ltd ");

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal("IŞIK Ltd", api.SonIslemOlustur!.Cari);
    }

    [Fact]
    public async Task Pasif_cari_ile_kayit_yapilabilir()
    {
        var (api, vm) = Kur();
        await vm.YukleAsync();
        FormuDoldur(vm, "Eski Tedarikçi");

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal("Eski Tedarikçi", api.SonIslemOlustur!.Cari);
    }

    [Fact]
    public async Task Baska_sayfada_eklenen_cari_icin_liste_tazelenir()
    {
        var cagri = 0;
        var (api, vm) = Kur(a => a.CarilerUret = _ =>
        {
            cagri++;
            IReadOnlyList<CariDto> l = cagri == 1
                ? new[] { new CariDto(1, "Market", true) }
                : new[] { new CariDto(1, "Market", true), new CariDto(2, "Yeni Firma", true) };
            return Task.FromResult(l);
        });
        await vm.YukleAsync();
        FormuDoldur(vm, "Yeni Firma");

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(2, cagri);
        Assert.Equal("Yeni Firma", api.SonIslemOlustur!.Cari);
        Assert.Null(vm.Hata);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "'Market' adında bir cari yok. Önce Cariler sayfasından ekleyin.")]
    [InlineData(HttpStatusCode.Conflict, "İşlem kaydedilemedi; ilgili kayıt değişmiş olabilir.")]
    public async Task Sunucunun_400_409_hata_metni_gosterilir(HttpStatusCode kod, string metin)
    {
        var (api, vm) = Kur(a => a.IslemYazHatasi = new KasaApiException(kod, metin));
        await vm.YukleAsync();
        FormuDoldur(vm, "Market");

        await vm.KaydetCommand.ExecuteAsync(null);

        Assert.Equal(metin, vm.Hata);
        Assert.Equal("Market", vm.DuzenCari);        // form korunur, düzeltilip yeniden denenebilir
    }

    [Fact]
    public void Hata_metni_olmayan_409_genel_mesaja_duser()
        => Assert.Equal(HataMesaji.GecersizIstek, HataMesaji.Coz(new KasaApiException(HttpStatusCode.Conflict)));

    // ---- Sayfalı liste ----

    private static IReadOnlyList<IslemDto> CokIslem(int n)
        => Enumerable.Range(1, n)
            .Select(i => new IslemDto(i, new DateOnly(2026, 9, 1).AddDays(i % 24), "Market", 1m, "MEZAT", GiderTipi.Cari, null))
            .ToList();

    private static void EnYeniOnce(IEnumerable<IslemDto> l)
    {
        var liste = l.ToList();
        var beklenen = liste.OrderByDescending(i => i.Tarih).ThenByDescending(i => i.Id).ToList();
        Assert.Equal(beklenen.Select(i => i.Id), liste.Select(i => i.Id));
    }

    [Fact]
    public async Task Kucuk_liste_tek_istekte_gelir()
    {
        var (api, vm) = Kur(a => a.IslemlerListe = CokIslem(30));
        await vm.YukleAsync();

        Assert.Equal(new[] { (IslemlerViewModel.SayfaBoyutu, 0) }, api.SayfaCagrilari);
        Assert.Equal(30, vm.Islemler.Count);
        Assert.False(vm.DahaEskiVar);
        Assert.Equal(30, vm.FiltreToplamKayit);
        Assert.Contains("30 işlem · toplam", vm.FiltreOzet);
    }

    [Fact]
    public async Task Buyuk_liste_en_yeni_sayfa_yuklenir_eskiler_sayfa_sayfa_eklenir()
    {
        var (api, vm) = Kur(a => a.IslemlerListe = CokIslem(1200));
        await vm.YukleAsync();

        Assert.Equal(new[] { (500, 0), (500, 700) }, api.SayfaCagrilari);
        Assert.Equal(500, vm.Islemler.Count);
        Assert.True(vm.DahaEskiVar);
        Assert.Equal(1200, vm.FiltreToplamKayit);
        Assert.Contains("1200 işlem (en yeni 500 gösteriliyor)", vm.FiltreOzet);
        EnYeniOnce(vm.Islemler);
        // Yüklü olanlar gerçekten en yeni 500 işlem.
        var enYeni = api.IslemlerListe.OrderByDescending(i => i.Tarih).ThenByDescending(i => i.Id).Take(500).Select(i => i.Id);
        Assert.Equal(enYeni, vm.Islemler.Select(i => i.Id));

        await vm.DahaEskiYukleCommand.ExecuteAsync(null);
        Assert.Equal((500, 200), api.SayfaCagrilari[^1]);
        Assert.Equal(1000, vm.Islemler.Count);
        Assert.True(vm.DahaEskiVar);

        await vm.DahaEskiYukleCommand.ExecuteAsync(null);
        Assert.Equal((200, 0), api.SayfaCagrilari[^1]);
        Assert.Equal(1200, vm.Islemler.Count);
        Assert.False(vm.DahaEskiVar);
        Assert.Equal(1200, vm.Islemler.Select(i => i.Id).Distinct().Count());
        EnYeniOnce(vm.Islemler);
        Assert.Contains("1200 işlem · toplam", vm.FiltreOzet);

        var cagriSayisi = api.SayfaCagrilari.Count;
        await vm.DahaEskiYukleCommand.ExecuteAsync(null);   // hepsi yüklü: istek yok
        Assert.Equal(cagriSayisi, api.SayfaCagrilari.Count);
    }

    [Fact]
    public async Task Daha_eski_sayfa_ayni_filtreyle_istenir()
    {
        var (api, vm) = Kur(a => a.IslemlerListe = CokIslem(600));
        await vm.YukleAsync();
        var (bas, bit) = (api.SonFiltreBaslangic, api.SonFiltreBitis);

        await vm.DahaEskiYukleCommand.ExecuteAsync(null);

        Assert.Equal(bas, api.SonFiltreBaslangic);
        Assert.Equal(bit, api.SonFiltreBitis);
        Assert.Equal((100, 0), api.SayfaCagrilari[^1]);
        Assert.Equal(600, vm.Islemler.Count);
    }
}
