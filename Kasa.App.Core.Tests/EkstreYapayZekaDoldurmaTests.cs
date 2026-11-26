using System.Net;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core.Tests;

// Yapay zekâ belge açılınca hiç sorulmamış satırları kendiliğinden doldurur (docs/specs/2026-11-26-ekstre-hareket-penceresi.md §3): onay penceresi
// yok, en çok 10 tur, ilerleme ya da sorulacak satır yoksa ya da hata olursa durur.
public partial class EkstreAktarmaTests
{
    private static Sahte YzBelgesi(params EkstreKuralOnerisi[] oneriler) => new()
    {
        Veri = DurumBelgesi([.. oneriler.Select(o => Satir(o.SatirNo))]),
        Oneriler = oneriler,
        YzDurumu = YzDurum(),
    };

    [Fact]
    public async Task Belge_acilinca_yapay_zeka_kendiliginden_sorar_ve_isaretler()
    {
        var api = YzBelgesi(Yok(1), Yok(2));
        api.YzOnizleme = new(2, 0, 0, ["1 | CIKIS | 100-1000 | KARGO", "2 | CIKIS | 100-1000 | ELEKTRIK"], 0.001m);
        api.YzSorYaniti = _ => { api.Oneriler = [YzHazir(1), YzHazir(2)]; return new(2, 2, 0, 0.0004m, null); };
        var (vm, _, _) = await Hazir(api);
        var soru = Assert.Single(api.YzSorulari);
        Assert.True(soru.YalnizSorulmamis);
        Assert.All(api.YzOnizlemeBayraklari, b => Assert.True(b));
        Assert.Equal([true, true], vm.Satirlar.Select(s => s.Secili));
        Assert.Equal("Yapay zekâ 2 satırı doldurdu", vm.YapayZekaIlerleme);
        Assert.Equal(["1 | CIKIS | 100-1000 | KARGO", "2 | CIKIS | 100-1000 | ELEKTRIK"], vm.SonGonderilenler);
        Assert.Null(vm.YapayZekaHatasi);
    }

    [Fact]
    public async Task Sorulacak_satir_yoksa_sessizce_biter()
    {
        var api = YzBelgesi(Yok(1));
        api.YzOnizleme = new(0, 0, 0, [], 0);
        var (vm, _, _) = await Hazir(api);
        Assert.Empty(api.YzSorulari);
        Assert.Equal(((string?)null, (string?)null), (vm.YapayZekaIlerleme, vm.YapayZekaHatasi));
    }

    [Fact]
    public async Task Sunucu_sorulacak_satir_yok_derse_sessizce_biter()
    {
        var api = YzBelgesi(Yok(1));
        api.YzSorYaniti = _ => throw new KasaApiException(HttpStatusCode.Conflict, "Yapay zekâya sorulacak satır yok: bekleyen satırlar kural ya da hafızayla tanınıyor.");
        var (vm, _, _) = await Hazir(api);
        Assert.Single(api.YzSorulari);
        Assert.Null(vm.YapayZekaHatasi);
    }

    [Fact]
    public async Task Ilerleme_yoksa_dongu_durur()
    {
        var api = YzBelgesi(Yok(1));
        api.YzSorYaniti = _ => new(1, 0, 5, 0.0001m, "Bu satırlar için güvenilir öneri gelmedi.");
        await Hazir(api);
        Assert.Single(api.YzSorulari);
    }

    [Fact]
    public async Task En_cok_on_tur_sorar()
    {
        var api = YzBelgesi(Yok(1));
        api.YzSorYaniti = _ => new(1, 1, 5, 0.0001m, null);
        var (vm, _, _) = await Hazir(api);
        Assert.Equal(10, api.YzSorulari.Count);
        Assert.Equal(10, api.YzSorulari.Select(s => s.IstekId).Distinct().Count());
        Assert.Equal("Yapay zekâ 10 satırı doldurdu", vm.YapayZekaIlerleme);
    }

    [Fact]
    public async Task Hata_donguyu_durdurur_ve_mesaj_yazar()
    {
        var api = YzBelgesi(Yok(1));
        api.YzSorYaniti = _ => throw new KasaApiException(HttpStatusCode.ServiceUnavailable, "Yapay zekâ hizmetine ulaşılamadı.");
        var (vm, _, _) = await Hazir(api);
        Assert.Single(api.YzSorulari);
        Assert.Contains("Yapay zekâ hizmetine ulaşılamadı", vm.YapayZekaHatasi);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Erp_belgesinde_ve_kapaliyken_sorulmaz()
    {
        var erp = new Sahte { Veri = ErpBelge(ErpSatiri(1)), Oneriler = [Yok(1)], YzDurumu = YzDurum() };
        await Hazir(erp);
        Assert.Empty(erp.YzSorulari);
        var kapali = YzBelgesi(Yok(1));
        kapali.YzDurumu = YzDurum(false, "Yapay zekâ sunucuda kapalı.");
        await Hazir(kapali);
        Assert.Empty(kapali.YzSorulari);
    }

    /// <summary>İşlemler sırayla yürür: döngü sürerken başka belge açılamaz; oturum değişirse eski oturum için soru gitmez.</summary>
    [Fact]
    public async Task Dongu_oturum_degisince_durur()
    {
        var api = YzBelgesi(Yok(1));
        AuthViewModel? auth = null;
        api.YzSorYaniti = _ => { auth!.OturumSurumu++; return new(1, 1, 5, 0.0001m, null); };
        var vm = new EkstreAktarmaViewModel(api, new SahteApi { KanallarListe = [new(1, "MEZAT", true, 0, 0)] }, new FinansTakipTests.Sahte(),
            auth = new AuthViewModel(new SahteApi()) { AktifRol = Rol.Editor });
        await vm.YukleAsync();
        await vm.BelgeAcAsync(1);
        Assert.Single(api.YzSorulari);
    }

    [Fact]
    public async Task Bos_belgede_dongu_sormaz()
    {
        var api = new Sahte { Veri = DurumBelgesi(), YzDurumu = YzDurum() };
        await Hazir(api);
        Assert.Equal((0, 0), (api.YzSorulari.Count, api.YzOnizlemeSayisi));
    }
}
