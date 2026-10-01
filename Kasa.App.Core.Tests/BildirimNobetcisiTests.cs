using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Uygulama açıkken bildirimler (tasarım 2026-09-30 masaüstü bildirimleri §1-2): editör oturumu açılınca görev sunucu saatine
/// göre kurulur ve bir kez bakılır; editör olmayan oturumda bakılmaz; çıkışta ve rol değişiminde rozet ve durum sıfırlanır; anahtar
/// kapatılınca görev silinir, açılınca kurulur; saat kaydedilince görev güncellenir; deneme ve tıklama nöbetçiden geçer.</summary>
public class BildirimNobetcisiTests
{
    private static BildirimDto B(int id) => SahteBildirimApi.Bildirim(id, BildirimOrtami.Bugun);

    [Fact]
    public async Task Editor_oturumu_acilinca_gorev_sunucu_saatine_gore_kurulur_ve_bir_kez_bakilir()
    {
        var o = new BildirimOrtami();
        o.Api.Ayar = new BildirimAyarDto(true, 10, 30, "Europe/Istanbul", 4);
        o.Api.Liste = [B(1)];
        await o.Nobetci.OturumAcildiAsync();
        Assert.Equal(["kur 10:30"], o.Gorev.Cagrilar);
        Assert.Equal([1], o.Gosterici.Gosterilenler.Select(b => b.Id));
        Assert.Equal(1, o.Yoklayici.Okunmamis);
        Assert.Equal("Son kontrol 14:05 · 1 yeni bildirim", o.Nobetci.DurumMetni);
        Assert.Equal(TimeSpan.FromMinutes(5), BildirimNobetcisi.Aralik);
    }

    [Theory]
    [InlineData(Rol.Izleyici)]
    [InlineData(Rol.Alici)]
    public async Task Editor_olmayan_oturumda_bakilmaz_gorev_kurulmaz(Rol rol)
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1)];
        o.Auth.AktifRol = rol;
        await o.Nobetci.OturumAcildiAsync();
        Assert.Null(await o.Nobetci.TikAsync());
        Assert.Empty(o.Gorev.Cagrilar);
        Assert.Equal(0, o.Api.ListeCagri);
    }

    [Fact]
    public async Task Cikista_ve_rol_degisiminde_rozet_ve_durum_sifirlanir()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1), B(2)];
        await o.Nobetci.TikAsync();
        Assert.Equal(2, o.Yoklayici.Okunmamis);
        o.Auth.GirisYapildi = false;
        Assert.Equal(0, o.Yoklayici.Okunmamis);
        Assert.Null(o.Yoklayici.SonSonuc);
        Assert.Null(await o.Nobetci.TikAsync());
        o.Auth.GirisYapildi = true;
        await o.Nobetci.TikAsync();
        Assert.Equal(2, o.Yoklayici.Okunmamis);
        o.Auth.AktifRol = Rol.Izleyici;
        Assert.Equal(0, o.Yoklayici.Okunmamis);
    }

    [Fact]
    public async Task Ayar_kapatilinca_gorev_silinir_bakma_durur_acilinca_kurulur_ve_bakilir()
    {
        var o = new BildirimOrtami();
        o.Api.Liste = [B(1)];
        await o.Nobetci.AcikAyarlaAsync(false);
        Assert.False(o.Ayar.Acik);
        Assert.Equal(["sil"], o.Gorev.Cagrilar);
        Assert.Null(await o.Nobetci.TikAsync());
        Assert.Equal("Bu bilgisayarda Windows bildirimleri kapalı.", o.Nobetci.DurumMetni);
        await o.Nobetci.SaatDegistiAsync(11, 0);
        Assert.Equal(["sil"], o.Gorev.Cagrilar);
        await o.Nobetci.AcikAyarlaAsync(true);
        Assert.Equal(["sil", "kur 09:00"], o.Gorev.Cagrilar);
        Assert.Equal([1], o.Gosterici.Gosterilenler.Select(b => b.Id));
        // Değişmeyen değer: görev yeniden kurulmaz.
        await o.Nobetci.AcikAyarlaAsync(true);
        Assert.Equal(2, o.Gorev.Cagrilar.Count);
    }

    [Fact]
    public async Task Saat_kaydedilince_gorev_guncellenir_sunucu_saati_alinamazsa_bakma_surer()
    {
        var o = new BildirimOrtami();
        await o.Nobetci.SaatDegistiAsync(8, 15);
        Assert.Equal(["kur 08:15"], o.Gorev.Cagrilar);
        o.Api.AyarHatasi = new HttpRequestException("bağlantı yok");
        o.Api.Liste = [B(1)];
        await o.Nobetci.OturumAcildiAsync();
        Assert.Equal(["kur 08:15"], o.Gorev.Cagrilar);
        Assert.Single(o.Gosterici.Gosterilenler);
    }

    [Fact]
    public async Task Deneme_bildirimi_windows_ayari_ve_tiklama_nobetciden_gecer()
    {
        var o = new BildirimOrtami();
        Assert.True(o.Nobetci.DenemeGoster());
        Assert.Equal(1, o.Gosterici.DenemeSayisi);
        Assert.Equal(0, o.Api.ListeCagri);
        o.Gosterici.WindowsAyarindaKapali = true;
        Assert.True(o.Nobetci.WindowsAyarindaKapali);
        Assert.Equal("//kartlar?KartId=4", await o.Nobetci.TiklamayiIsleAsync(new BildirimTiklamasi(9, "/#cards/4")));
        Assert.Equal([9], o.Api.Okunanlar);
        o.Auth.GirisYapildi = false;
        Assert.Null(await o.Nobetci.TiklamayiIsleAsync(new BildirimTiklamasi(10, "/#cards/4")));
        Assert.Equal([9], o.Api.Okunanlar);
    }
}
