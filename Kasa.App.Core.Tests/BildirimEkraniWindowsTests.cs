namespace Kasa.App.Core.Tests;

/// <summary>Bildirimler ekranındaki "Bu bilgisayarda Windows bildirimleri" kartı (tasarım 2026-09-30 masaüstü bildirimleri §2):
/// anahtar bu bilgisayarın ayarını gösterir, kapatınca görev silinir, açınca kurulur ve bakılır; deneme bildirimi sunucuya gitmez;
/// durum satırı son bakmayı ve Windows ayarı uyarısını gösterir; saat kaydedilince görev güncellenir; liste yüklenince ve okundu
/// işaretlenince menü rozeti güncellenir.</summary>
public class BildirimEkraniWindowsTests
{
    private static (BildirimOrtami O, BildirimViewModel Vm) Kur()
    {
        var o = new BildirimOrtami();
        return (o, new BildirimViewModel(o.Api, o.Auth, o.Nobetci));
    }

    [Fact]
    public void Anahtar_bu_bilgisayarin_ayarini_gosterir_kapatinca_gorev_silinir_acinca_kurulur()
    {
        var (o, vm) = Kur();
        Assert.True(vm.WindowsBildirimleri);
        Assert.Equal("Henüz kontrol edilmedi.", vm.WindowsDurumu);
        vm.WindowsBildirimleri = false;
        Assert.False(o.Ayar.Acik);
        Assert.Equal(["sil"], o.Gorev.Cagrilar);
        Assert.Equal("Bu bilgisayarda Windows bildirimleri kapalı.", vm.WindowsDurumu);
        Assert.Equal("Bu bilgisayarda Windows bildirimleri kapatıldı.", vm.Mesaj);
        vm.WindowsBildirimleri = true;
        Assert.True(o.Ayar.Acik);
        Assert.Equal(["sil", "kur 09:00"], o.Gorev.Cagrilar);
        Assert.Equal("Son kontrol 14:05 · yeni bildirim yok", vm.WindowsDurumu);
        Assert.Equal("Bu bilgisayarda Windows bildirimleri açıldı.", vm.Mesaj);
    }

    /// <summary>Anahtar işleyicisi sonucu beklenmeyen bir görevdir: görev işlemi hata verirse istisna gözlenmeden kaybolmaz,
    /// ekranın hata satırına yazılır; anahtar bu bilgisayarın ayarını göstermeye devam eder.</summary>
    [Fact]
    public void Anahtar_uygulanamazsa_hata_satirina_yazilir()
    {
        var (o, vm) = Kur();
        o.Gorev.SilmeHatasi = new IOException("schtasks başlatılamadı");
        vm.WindowsBildirimleri = false;
        Assert.Equal(["sil"], o.Gorev.Cagrilar);
        Assert.Equal("Bu bilgisayardaki bildirim görevi güncellenemedi. Lütfen yeniden deneyin.", vm.Hata);
        Assert.Null(vm.Mesaj);
        Assert.False(vm.WindowsBildirimleri);
        Assert.Equal("Bu bilgisayarda Windows bildirimleri kapalı.", vm.WindowsDurumu);
    }

    [Fact]
    public void Deneme_bildirimi_sunucuya_gitmeden_gosterilir_windows_ayari_kapaliysa_uyari_cikar()
    {
        var (o, vm) = Kur();
        Assert.False(vm.WindowsAyarindaKapali);
        o.Gosterici.WindowsAyarindaKapali = true;
        vm.DenemeGosterCommand.Execute(null);
        Assert.Equal(1, o.Gosterici.DenemeSayisi);
        Assert.Equal(0, o.Api.ListeCagri);
        Assert.True(vm.WindowsAyarindaKapali);
        Assert.StartsWith("Deneme bildirimi gösterildi.", vm.Mesaj);
        o.Gosterici.DenemeBasarili = false;
        vm.DenemeGosterCommand.Execute(null);
        Assert.StartsWith("Deneme bildirimi gösterilemedi.", vm.Mesaj);
    }

    [Fact]
    public async Task Durum_satiri_son_bakmayi_gosterir()
    {
        var (o, vm) = Kur();
        o.Api.Liste = [SahteBildirimApi.Bildirim(1, BildirimOrtami.Bugun), SahteBildirimApi.Bildirim(2, BildirimOrtami.Bugun)];
        await o.Nobetci.TikAsync();
        Assert.Equal("Son kontrol 14:05 · 2 yeni bildirim", vm.WindowsDurumu);
        o.Api.ListeHatasi = new HttpRequestException("bağlantı yok");
        await o.Nobetci.TikAsync();
        Assert.Equal("Son kontrol 14:05 · sunucuya ulaşılamadı", vm.WindowsDurumu);
    }

    [Fact]
    public async Task Saat_kaydedilince_bu_bilgisayardaki_gorev_guncellenir()
    {
        var (o, vm) = Kur();
        await vm.YukleAsync();
        vm.Saat = 7;
        vm.Dakika = 45;
        await vm.KaydetCommand.ExecuteAsync(null);
        Assert.Equal(["kur 07:45"], o.Gorev.Cagrilar);
        Assert.Equal("Bildirim ayarları kaydedildi. Saat Türkiye saatidir.", vm.Mesaj);
    }

    [Fact]
    public async Task Liste_yuklenince_ve_okundu_isaretlenince_menu_rozeti_guncellenir()
    {
        var (o, vm) = Kur();
        o.Api.Liste =
        [
            SahteBildirimApi.Bildirim(1, BildirimOrtami.Bugun),
            SahteBildirimApi.Bildirim(2, BildirimOrtami.Bugun, okundu: true),
            SahteBildirimApi.Bildirim(3, BildirimOrtami.Bugun.AddDays(-2)),
        ];
        await vm.YukleAsync();
        Assert.Equal(2, o.Yoklayici.Okunmamis);
        await vm.OkunduAsync(vm.Bildirimler.First(b => b.Veri.Id == 1));
        Assert.Equal([1], o.Api.Okunanlar);
        Assert.Equal(1, o.Yoklayici.Okunmamis);
    }
}
