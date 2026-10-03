using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Tek bağlantı durumu: istemcinin ağ hatası kopuk, yanıtı bağlı yapar; kopuktan bağlıya dönüş bir kez bildirilir;
/// kopukken sayfaların okuma bağlantı hatası yazılmaz.</summary>
public class BaglantiDurumuTests
{
    private sealed class SahteBildirimler : IBaglantiBildirimleri
    {
        public event EventHandler? SunucuyaUlasildi;
        public event EventHandler<Exception>? SunucuyaUlasilamadi;
        public void Ulas() => SunucuyaUlasildi?.Invoke(this, EventArgs.Empty);
        public void Kop() => SunucuyaUlasilamadi?.Invoke(this, new HttpRequestException());
    }

    private static (BaglantiDurumu Durum, SahteBildirimler Istemci, List<string> Bildirimler) Kur()
    {
        var istemci = new SahteBildirimler();
        var durum = new BaglantiDurumu(istemci, new IslemEditorTests.SabitZaman(new DateOnly(2026, 10, 2)));
        var bildirimler = new List<string>();
        durum.PropertyChanged += (_, e) => bildirimler.Add(e.PropertyName!);
        durum.BaglantiGeldi += (_, _) => bildirimler.Add("geldi");
        durum.BaglantiKoptu += (_, _) => bildirimler.Add("koptu");
        return (durum, istemci, bildirimler);
    }

    [Fact]
    public void Ag_hatasi_kopuk_yapar_bir_kez_bildirir()
    {
        var (durum, istemci, bildirimler) = Kur();
        Assert.False(durum.Kopuk);

        istemci.Kop();
        istemci.Kop();

        Assert.True(durum.Kopuk);
        Assert.Equal(["Kopuk", "koptu"], bildirimler);
        Assert.Equal("Sunucuya ulaşılamıyor", durum.SeritMetni);
    }

    [Fact]
    public void Yanit_bagli_yapar_kopuktan_donus_bir_kez_bildirilir_son_baglanti_yazilir()
    {
        var (durum, istemci, bildirimler) = Kur();
        istemci.Ulas();
        Assert.Equal(["SonBaglanti", "SeritMetni"], bildirimler);

        bildirimler.Clear();
        istemci.Kop();
        istemci.Ulas();

        Assert.False(durum.Kopuk);
        Assert.Equal(["Kopuk", "koptu", "SonBaglanti", "SeritMetni", "Kopuk", "geldi"], bildirimler);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero), durum.SonBaglanti);

        istemci.Kop();
        Assert.Equal("Sunucuya ulaşılamıyor · Son bağlantı 12:00", durum.SeritMetni);
    }

    [Fact]
    public void Oturum_bagimli_modeller_auth_uzerinden_ayni_durumu_okur()
    {
        var durum = new BaglantiDurumu();
        var auth = new AuthViewModel(new SahteApi(), durum);
        Assert.Same(durum, auth.Baglanti);
        Assert.NotNull(new AuthViewModel(new SahteApi()).Baglanti);
    }

    [Fact]
    public async Task Kopukken_rapor_okumasinin_baglanti_hatasi_sayfaya_yazilmaz()
    {
        var durum = new BaglantiDurumu();
        var vm = new PanelViewModel(new SahteApi { YuklemeHatasi = new HttpRequestException() }, durum);
        durum.Ulasilamadi();

        await vm.YukleAsync();

        Assert.Null(vm.Hata);
        Assert.False(vm.Mesgul);

        durum.Ulasildi();
        await vm.YukleAsync();
        Assert.Contains("Sunucuya ulaşılamadı", vm.Hata);
    }

    /// <summary>K-1: geçiş atomiktir (Interlocked.Exchange); çok sayıda eşzamanlı Ulasilamadi çağrısı "koptu" olayını tam bir
    /// kez tetikler.</summary>
    [Fact]
    public void Esanli_cok_sayida_Ulasilamadi_cagrisi_koptu_olayini_bir_kez_tetikler()
    {
        var durum = new BaglantiDurumu();
        var koptuSayisi = 0;
        durum.BaglantiKoptu += (_, _) => Interlocked.Increment(ref koptuSayisi);

        Parallel.For(0, 100, _ => durum.Ulasilamadi());

        Assert.Equal(1, koptuSayisi);
        Assert.True(durum.Kopuk);
    }

    /// <summary>K-1: çok sayıda eşzamanlı Ulasildi çağrısı (kopuktan dönüş) "geldi" olayını tam bir kez tetikler.</summary>
    [Fact]
    public void Esanli_cok_sayida_Ulasildi_cagrisi_geldi_olayini_bir_kez_tetikler()
    {
        var durum = new BaglantiDurumu();
        durum.Ulasilamadi();
        var geldiSayisi = 0;
        durum.BaglantiGeldi += (_, _) => Interlocked.Increment(ref geldiSayisi);

        Parallel.For(0, 100, _ => durum.Ulasildi());

        Assert.Equal(1, geldiSayisi);
        Assert.False(durum.Kopuk);
    }
}
