using System.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>
/// Oturum ve rol: ekranlar rolü oturumdan (<see cref="AuthViewModel.AktifRol"/>) alır. Başka rolle yeni oturum açılınca
/// (<see cref="AuthViewModel.OturumSurumu"/>) önceki oturumun verisi kalkar, <c>EditorMu</c> ve ona bağlı hesaplanan değerler
/// bildirilir; izleyici yazma isteği gönderemez. İşlemler, Alışlar ve takip ekranları aynı sözleşmeyi paylaşır.
/// </summary>
public class OturumVeRolTests
{
    private static readonly DonemDto Hafta = new(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), 2026, 9);

    private static AuthViewModel Oturum(Rol rol) => new(new SahteApi()) { AktifRol = rol };

    private static SahteApi Finans() => new() { KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0m)], DonemlerListe = [Hafta] };

    private static IslemlerViewModel Islemler(SahteApi api, AuthViewModel auth)
        => new(api, auth: auth, zaman: new IslemEditorTests.SabitZaman(new DateOnly(2026, 9, 23)));

    private static void GiderFormu(IslemlerViewModel vm)
    {
        vm.DuzenTarih = new DateTime(2026, 9, 22);
        vm.DuzenCari = "Kargo";
        vm.DuzenTutar = 75m;
        vm.DuzenKanal = "MEZAT";
    }

    /// <summary>Başka rolle yeni oturum: AuthViewModel.GirisAsync gibi önce rol, sonra oturum sürümü değişir.</summary>
    private static void YeniOturum(AuthViewModel auth, Rol rol)
    {
        auth.AktifRol = rol;
        auth.OturumSurumu++;
    }

    private static List<string?> Bildirimler(INotifyPropertyChanged vm)
    {
        var adlar = new List<string?>();
        vm.PropertyChanged += (_, e) => adlar.Add(e.PropertyName);
        return adlar;
    }

    [Fact]
    public async Task Islemler_editor_rolunde_gider_ve_gelir_kaydeder_gelir_formunu_hazirlar()
    {
        var api = Finans();
        var vm = Islemler(api, Oturum(Rol.Editor));
        await vm.YukleAsync();
        Assert.Equal(Hafta.Start, api.SonGelenlerDonem);

        GiderFormu(vm);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(1, api.IslemOlusturCagri);

        vm.SecGelenKanalCommand.Execute(vm.GelenKanallari.First(c => c.Ad == "MEZAT"));
        vm.GelenTutar = 500m;
        await vm.GelenKaydetCommand.ExecuteAsync(null);
        Assert.Equal(1, api.GelenKaydetCagri);
        Assert.Null(vm.Hata);
    }

    [Fact]
    public async Task Islemler_izleyici_rolunde_gider_ve_gelir_gondermez_gelir_formunu_hazirlamaz()
    {
        var api = Finans();
        var vm = Islemler(api, Oturum(Rol.Izleyici));
        await vm.YukleAsync();
        Assert.Empty(api.GelenlerIstekleri);
        Assert.Null(vm.GelenDonem);

        GiderFormu(vm);
        await vm.KaydetCommand.ExecuteAsync(null);
        vm.GelenKanal = "MEZAT";
        vm.GelenTutar = 500m;
        await vm.GelenKaydetCommand.ExecuteAsync(null);

        Assert.Equal(0, api.IslemOlusturCagri);
        Assert.Equal(0, api.GelenKaydetCagri);
        Assert.Null(vm.Hata);
    }

    /// <summary>Rol oturumdan okunur: editörken açılmış ekranda izleyici oturumu açılınca yazma isteği gitmez; sayfanın rolü
    /// ayrıca bildirmesi gerekmez.</summary>
    [Fact]
    public async Task Islemler_yeni_oturumun_rolunu_kullanir()
    {
        var api = Finans();
        var auth = Oturum(Rol.Editor);
        var vm = Islemler(api, auth);
        await vm.YukleAsync();

        YeniOturum(auth, Rol.Izleyici);
        await vm.YukleAsync();
        GiderFormu(vm);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(0, api.IslemOlusturCagri);

        YeniOturum(auth, Rol.Editor);
        await vm.YukleAsync();
        GiderFormu(vm);
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(1, api.IslemOlusturCagri);
    }

    [Fact]
    public async Task Alislar_yeni_oturumda_rolu_ve_role_bagli_durumu_bildirir()
    {
        var auth = Oturum(Rol.Editor);
        var api = new AlislarViewModelTests.SahteAlisApi();
        var vm = new AlislarViewModel(api, new SahteApi(), auth: auth);
        await vm.YukleAsync();
        Assert.True(vm.EditorMu);
        Assert.Equal("Editör", vm.KaydiAcan);
        var adlar = Bildirimler(vm);

        YeniOturum(auth, Rol.Alici);

        Assert.False(vm.EditorMu);
        Assert.Equal("Sizin alışınız", vm.KaydiAcan);
        Assert.Contains(nameof(vm.EditorMu), adlar);
        foreach (var bagli in new[] { nameof(vm.KaydiAcan), nameof(vm.Duzenlenebilir), nameof(vm.Onaylanabilir), nameof(vm.IadeEdilebilir), nameof(vm.OdemeAlaniGorunur) })
            Assert.Contains(bagli, adlar);
        Assert.False(vm.VeriHazir);
        Assert.Single(vm.Kalemler);                                  // yeni oturum boş formla başlar
    }

    [Fact]
    public async Task Takip_ekrani_yeni_oturumda_rolu_bildirir_onceki_veriyi_birakir()
    {
        var auth = Oturum(Rol.Editor);
        var vm = new KartTakipViewModel(new FinansTakipTests.Fake(), Finans(), auth);
        await vm.YukleAsync();
        Assert.True(vm.EditorMu);
        Assert.True(vm.VeriHazir);
        var adlar = Bildirimler(vm);

        YeniOturum(auth, Rol.Izleyici);

        Assert.False(vm.EditorMu);
        Assert.Contains(nameof(vm.EditorMu), adlar);
        Assert.False(vm.VeriHazir);
        Assert.Empty(vm.Kartlar);
        Assert.Null(vm.SonGuncelleme);
    }

    /// <summary>EditorMu yalnız hesaplanan değerdir: rol (AktifRol) oturum sürümü değişmeden değişse de EditorMu ve ona bağlı
    /// hesaplanan değerler bildirilir; bağlı görünümler eski rolde kalmaz. Veri sıfırlanmaz (oturum değişmedi).</summary>
    [Fact]
    public async Task Rol_degisince_EditorMu_ve_bagli_degerler_bildirilir()
    {
        var auth = Oturum(Rol.Editor);
        var takip = new KartTakipViewModel(new FinansTakipTests.Fake(), Finans(), auth);
        await takip.YukleAsync();
        var islemler = Islemler(Finans(), auth);
        var alislar = new AlislarViewModel(new AlislarViewModelTests.SahteAlisApi(), new SahteApi(), auth: auth);
        var (t, i, a) = (Bildirimler(takip), Bildirimler(islemler), Bildirimler(alislar));
        Assert.True(takip.EditorMu && islemler.EditorMu && alislar.EditorMu);

        auth.AktifRol = Rol.Izleyici;

        Assert.False(takip.EditorMu);
        Assert.False(islemler.EditorMu);
        Assert.False(alislar.EditorMu);
        Assert.Contains(nameof(takip.EditorMu), t);
        Assert.Contains(nameof(islemler.EditorMu), i);
        Assert.Contains(nameof(alislar.EditorMu), a);
        foreach (var bagli in new[] { nameof(alislar.KaydiAcan), nameof(alislar.Duzenlenebilir), nameof(alislar.Onaylanabilir), nameof(alislar.IadeEdilebilir), nameof(alislar.OdemeAlaniGorunur), nameof(alislar.DuzeltmeAcik) })
            Assert.Contains(bagli, a);
        Assert.Equal("Sizin alışınız", alislar.KaydiAcan);
        Assert.True(takip.VeriHazir);
        Assert.NotEmpty(takip.Kartlar);
    }

    [Fact]
    public async Task Ayarlar_yeni_oturumda_onceki_oturumun_ayarlarini_birakir()
    {
        var auth = Oturum(Rol.Editor);
        var api = new SahteApi { AyarlarSonuc = new AyarlarDto(new DateOnly(2026, 1, 1), 15000m, false), KanallarListe = [new KanalDto(1, "MEZAT", true, 0, 0m)] };
        var vm = new AyarlarViewModel(api, auth: auth);
        await vm.YukleAsync();
        Assert.True(vm.AyarlarYuklendi);
        Assert.Single(vm.Kanallar);

        YeniOturum(auth, Rol.Editor);

        Assert.False(vm.AyarlarYuklendi);
        Assert.Empty(vm.Kanallar);
        Assert.Equal(0m, vm.KasaAcilisDevri);
    }
}
