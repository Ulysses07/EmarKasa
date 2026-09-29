using System.Net;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Önizleme → onay akışları (kart ödemesi, kart faiz / masrafı, kart ve kredi geçişi): onaylanan gövde gösterilen önizlemenin
/// gövdesidir (aynı IstekId). Önizleme yanıtı gelmeden girdi, seçili kayıt ya da oturum değişirse yanıt gösterilmez ve onay
/// yeni önizleme ister; önizlemeden sonra girdi değişirse onay gönderilmez.
/// </summary>
public class OnizlemeOnayTests
{
    private static readonly DateOnly Tarih = new(2026, 9, 23);
    private static AuthViewModel Auth() => new(new SahteApi()) { AktifRol = Rol.Editor };
    private static SahteApi Finans() => new() { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0), new KanalDto(2, "PERAKENDE", true, 1, 0)] };
    private static string Json(object deger) => System.Text.Json.JsonSerializer.Serialize(deger);

    private static async Task<KartTakipViewModel> KartVm(FinansTakipTests.Fake api, AuthViewModel? auth = null, KasaKontrolVeAylikGiderTests.Fake? kontrol = null)
    {
        var vm = new KartTakipViewModel(api, Finans(), auth ?? Auth(), kontrolApi: kontrol);
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Kartlar[0]);
        return vm;
    }

    private static async Task<KrediTakipViewModel> KrediVm(FinansTakipTests.Fake api, AuthViewModel? auth = null)
    {
        var vm = new KrediTakipViewModel(api, Finans(), auth ?? Auth());
        await vm.YukleAsync();
        vm.SecCommand.Execute(vm.Krediler[0]);
        return vm;
    }

    // ---- Kart ödemesi ----

    [Fact]
    public async Task Kart_odemesi_onaylanan_govde_onizlenen_govdedir()
    {
        var api = new FinansTakipTests.Fake();
        var vm = await KartVm(api);
        vm.OdemeTutari = 10;
        await vm.OdemeOnizleCommand.ExecuteAsync(null);
        Assert.Contains("Kasa çıkışı: 10,00 ₺", vm.OdemeOnizleme);
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Equal(api.OnizlenenOdeme, Assert.Single(api.OdemeIstekleri));
        Assert.Null(vm.OdemeOnizleme);
        Assert.Contains("kaydedildi", vm.Mesaj);
    }

    [Fact]
    public async Task Kart_odemesi_onizleme_yaniti_gelmeden_tutar_degisirse_gosterilmez_onay_yeni_onizleme_ister()
    {
        var api = new FinansTakipTests.Fake();
        var vm = await KartVm(api);
        vm.OdemeTutari = 10;
        var kapi = new TaskCompletionSource();
        api.OnizlemeKapisi = kapi.Task;
        var onizleme = vm.OdemeOnizleCommand.ExecuteAsync(null);
        vm.OdemeTutari = 11;
        kapi.SetResult();
        await onizleme;
        Assert.Null(vm.OdemeOnizleme);
        Assert.Null(vm.Hata);

        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.OdemeIstekleri);
        Assert.Equal("Ödeme bilgileri için önce güncel önizlemeyi alın.", vm.Hata);
    }

    [Fact]
    public async Task Kart_odemesi_onizleme_yaniti_gelmeden_kart_degisirse_uygulanmaz()
    {
        var api = new FinansTakipTests.Fake();
        api.KartlarYaniti = Task.FromResult<IReadOnlyList<KartTakipDto>>([FinansTakipTests.Fake.OrnekKart(), FinansTakipTests.Fake.OrnekKart() with { Id = 2, Ad = "İkinci kart" }]);
        var vm = await KartVm(api);
        vm.OdemeTutari = 10;
        var kapi = new TaskCompletionSource();
        api.OnizlemeKapisi = kapi.Task;
        var onizleme = vm.OdemeOnizleCommand.ExecuteAsync(null);
        vm.SecCommand.Execute(vm.Kartlar[1]);
        kapi.SetResult();
        await onizleme;
        Assert.Equal(2, vm.Secili!.Id);
        Assert.Null(vm.OdemeOnizleme);

        vm.OdemeTutari = 10;
        await vm.OdemeKaydetCommand.ExecuteAsync(null);
        Assert.Empty(api.OdemeIstekleri);
    }

    [Fact]
    public async Task Kart_odemesi_onizleme_yaniti_gelmeden_oturum_degisirse_uygulanmaz()
    {
        var api = new FinansTakipTests.Fake();
        var auth = Auth();
        var vm = await KartVm(api, auth);
        vm.OdemeTutari = 10;
        var kapi = new TaskCompletionSource();
        api.OnizlemeKapisi = kapi.Task;
        var onizleme = vm.OdemeOnizleCommand.ExecuteAsync(null);
        auth.OturumSurumu++;
        kapi.SetResult();
        await onizleme;
        Assert.Null(vm.OdemeOnizleme);
        Assert.Null(vm.Hata);
        Assert.False(vm.Mesgul);
        Assert.Null(vm.Secili);
    }

    // ---- Kart faiz / masrafı ----

    private static async Task<(KartTakipViewModel Vm, KasaKontrolVeAylikGiderTests.Fake Kontrol)> MasrafVm()
    {
        var kontrol = new KasaKontrolVeAylikGiderTests.Fake();
        var vm = await KartVm(new FinansTakipTests.Fake(), kontrol: kontrol);
        vm.MasrafEkstresi = vm.MasrafEkstreleri[0];
        vm.MasrafTarihi = Tarih.ToDateTime(TimeOnly.MinValue);
        vm.MasrafTutari = 10;
        vm.MasrafAciklama = "Banka faizi";
        return (vm, kontrol);
    }

    [Fact]
    public async Task Kart_masrafi_onizleme_yaniti_gelmeden_tutar_degisirse_gosterilmez_kayit_yeni_onizleme_ister()
    {
        var (vm, kontrol) = await MasrafVm();
        var kapi = new TaskCompletionSource();
        kontrol.MasrafOnizlemeKapisi = kapi.Task;
        var onizleme = vm.MasrafOnizleCommand.ExecuteAsync(null);
        vm.MasrafTutari = 12;
        kapi.SetResult();
        await onizleme;
        Assert.Null(vm.MasrafOnizleme);

        await vm.MasrafKaydetCommand.ExecuteAsync(null);
        Assert.Empty(kontrol.Masraflar);
        Assert.Equal("Faiz / masraf için önce güncel kanal dağılımını gösterin.", vm.Hata);
    }

    [Fact]
    public async Task Kart_masrafi_onizlenen_govde_ve_ozetle_kaydedilir_409_onizlemeyi_dusurur()
    {
        var (vm, kontrol) = await MasrafVm();
        await vm.MasrafOnizleCommand.ExecuteAsync(null);
        Assert.Contains("Dağıtılacak faiz / masraf: 10,00 ₺", vm.MasrafOnizleme);
        kontrol.MasrafIstisnasi = new KasaApiException(HttpStatusCode.Conflict, "Kart dağılımı değişti.");
        await vm.MasrafKaydetCommand.ExecuteAsync(null);
        Assert.Equal("Kart dağılımı değişti.", vm.Hata);
        Assert.Null(vm.MasrafOnizleme);
        Assert.Equal("pay-hash", Assert.Single(kontrol.Masraflar).DagilimOzeti);

        kontrol.MasrafIstisnasi = null;
        await vm.MasrafKaydetCommand.ExecuteAsync(null);
        Assert.Single(kontrol.Masraflar);
        Assert.Equal("Faiz / masraf için önce güncel kanal dağılımını gösterin.", vm.Hata);

        await vm.MasrafOnizleCommand.ExecuteAsync(null);
        await vm.MasrafKaydetCommand.ExecuteAsync(null);
        Assert.Equal(2, kontrol.Masraflar.Count);
        Assert.Equal(kontrol.Masraflar[0], kontrol.Masraflar[1]);
        Assert.Contains("kaydedildi", vm.Mesaj);
    }

    // ---- Kart geçişi ----

    private static async Task<(KartTakipViewModel Vm, FinansTakipTests.Fake Api)> KartGecisVm(AuthViewModel? auth = null)
    {
        var api = new FinansTakipTests.Fake { Kart = FinansTakipTests.Fake.OrnekKart() with { YeniTakip = false } };
        var vm = await KartVm(api, auth);
        vm.GecisAciklama = "Banka ekstresiyle kontrol edildi";
        return (vm, api);
    }

    [Fact]
    public async Task Kart_gecisi_onaylanan_govde_onizlenen_govdedir()
    {
        var (vm, api) = await KartGecisVm();
        await vm.GecisOnizleCommand.ExecuteAsync(null);
        Assert.True(vm.GecisOnaylanabilir);
        vm.GecisOnay = true;
        await vm.GecisiOnaylaCommand.ExecuteAsync(null);
        var onizlenen = Assert.Single(api.KartGecisOnizlemeleri);
        Assert.Equal(Json(onizlenen with { Onay = true }), Json(api.KartGecis!));
        Assert.Contains("Yeni takip açıldı", vm.Mesaj);
    }

    [Fact]
    public async Task Kart_gecisi_onizleme_yaniti_gelmeden_girdi_degisirse_gosterilmez_onaylanamaz()
    {
        var (vm, api) = await KartGecisVm();
        var kapi = new TaskCompletionSource();
        api.OnizlemeKapisi = kapi.Task;
        var onizleme = vm.GecisOnizleCommand.ExecuteAsync(null);
        vm.GecisAciklama = "Açıklama değişti";
        kapi.SetResult();
        await onizleme;
        Assert.Null(vm.GecisOnizleme);
        Assert.False(vm.GecisOnaylanabilir);

        vm.GecisOnay = true;
        await vm.GecisiOnaylaCommand.ExecuteAsync(null);
        Assert.Null(api.KartGecis);
        Assert.Equal("Güncel geçiş önizlemesini inceleyip onay kutusunu işaretleyin.", vm.Hata);
    }

    [Fact]
    public async Task Kart_gecisi_onizleme_yaniti_gelmeden_oturum_degisirse_uygulanmaz()
    {
        var auth = Auth();
        var (vm, api) = await KartGecisVm(auth);
        var kapi = new TaskCompletionSource();
        api.OnizlemeKapisi = kapi.Task;
        var onizleme = vm.GecisOnizleCommand.ExecuteAsync(null);
        auth.OturumSurumu++;
        kapi.SetResult();
        await onizleme;
        Assert.Null(vm.GecisOnizleme);
        Assert.False(vm.GecisOnaylanabilir);
        Assert.Null(vm.Hata);
    }

    // ---- Kredi geçişi ----

    private static async Task<(KrediTakipViewModel Vm, FinansTakipTests.Fake Api)> KrediGecisVm()
    {
        var api = new FinansTakipTests.Fake { Kredi = FinansTakipTests.Fake.OrnekKredi() with { YeniTakip = false } };
        var vm = await KrediVm(api);
        vm.GecisAciklama = "Kontrol edildi";
        return (vm, api);
    }

    [Fact]
    public async Task Kredi_gecisi_onaylanan_govde_onizlenen_govdedir()
    {
        var (vm, api) = await KrediGecisVm();
        await vm.GecisOnizleCommand.ExecuteAsync(null);
        Assert.NotNull(vm.GecisOnizleme);
        vm.GecisOnay = true;
        await vm.GecisiOnaylaCommand.ExecuteAsync(null);
        var onizlenen = Assert.Single(api.KrediGecisOnizlemeleri);
        Assert.Equal(onizlenen.IstekId, api.KrediGecis!.IstekId);
        Assert.True(api.KrediGecis.Onay);
        Assert.Equal(onizlenen.KanalIdleri, api.KrediGecis.KanalIdleri);
        Assert.Contains("Yeni kredi takibi açıldı", vm.Mesaj);
    }

    [Fact]
    public async Task Kredi_gecisi_onizleme_yaniti_gelmeden_kanal_degisirse_gosterilmez_onaylanamaz()
    {
        var (vm, api) = await KrediGecisVm();
        var kapi = new TaskCompletionSource();
        api.OnizlemeKapisi = kapi.Task;
        var onizleme = vm.GecisOnizleCommand.ExecuteAsync(null);
        vm.Kanallar[1].Secili = true;
        kapi.SetResult();
        await onizleme;
        Assert.Null(vm.GecisOnizleme);

        vm.GecisOnay = true;
        await vm.GecisiOnaylaCommand.ExecuteAsync(null);
        Assert.Null(api.KrediGecis);
        Assert.Equal("Güncel geçiş önizlemesini inceleyip onay kutusunu işaretleyin.", vm.Hata);
    }

    [Fact]
    public async Task Kredi_gecisi_kabul_edilemez_onizlemeyle_onaylanamaz()
    {
        var (vm, api) = await KrediGecisVm();
        api.KrediGecisYaniti = g => new("Kredi", 2, g.Baslangic, -10, 0, 0, ["Kabul edilemez"], false);
        await vm.GecisOnizleCommand.ExecuteAsync(null);
        Assert.NotNull(vm.GecisOnizleme);
        vm.GecisOnay = true;
        await vm.GecisiOnaylaCommand.ExecuteAsync(null);
        Assert.Null(api.KrediGecis);
        Assert.Equal("Güncel geçiş önizlemesini inceleyip onay kutusunu işaretleyin.", vm.Hata);
    }
}
