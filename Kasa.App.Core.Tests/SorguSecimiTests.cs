namespace Kasa.App.Core.Tests;

/// <summary>Kartlar ve Krediler sayfasının sorgu parametresiyle istenen seçimi (bildirim tıklaması: //kartlar?KartId=3,
/// //krediler?KrediId=2). Sayfa görünürken gelen istek hemen uygulanır (Shell aynı sayfaya gezinmede OnAppearing çağırmayabilir),
/// görünmezken sonraki görünüşe kalır. Başarılı yüklemeden sonra kimlik, kayıt bulunamasa da temizlenir (her ziyarette "bulunamadı"
/// hatası tekrarlanmaz); yükleme başarısızsa sonraki görünüşte yeniden denenir.</summary>
public class SorguSecimiTests
{
    private static Dictionary<string, object> Sorgu(object deger) => new() { ["KrediId"] = deger };

    [Fact]
    public void Gecerli_kimlik_alinir_sayfa_gorunuyorsa_hemen_uygulanmasi_istenir()
    {
        var secim = new SorguSecimi("KrediId");
        Assert.False(secim.Iste(Sorgu("5")));
        Assert.Equal(5, secim.Istenen);
        secim.Gorunuyor = true;
        Assert.True(secim.Iste(Sorgu("7")));
        Assert.Equal(7, secim.Istenen);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("")]
    public void Gecersiz_kimlik_yok_sayilir(string deger)
    {
        var secim = new SorguSecimi("KrediId") { Gorunuyor = true };
        Assert.False(secim.Iste(Sorgu(deger)));
        Assert.False(secim.Iste(new Dictionary<string, object> { ["KartId"] = "3" }));
        Assert.Null(secim.Istenen);
    }

    [Fact]
    public async Task Bekleyen_yoksa_yuklenmez()
    {
        var secim = new SorguSecimi("KrediId");
        var yuklenen = 0;
        Assert.False(await secim.UygulaAsync(() => Task.FromResult(yuklenen++), () => true, _ => true));
        Assert.Equal(0, yuklenen);
    }

    [Fact]
    public async Task Basarili_yuklemeden_sonra_secilir_ve_kimlik_temizlenir()
    {
        var secim = new SorguSecimi("KrediId");
        secim.Iste(Sorgu("2"));
        var secilen = new List<int>();
        Assert.True(await secim.UygulaAsync(() => Task.CompletedTask, () => true, id =>
        {
            secilen.Add(id);
            return true;
        }));
        Assert.Equal([2], secilen);
        Assert.Null(secim.Istenen);
        Assert.False(await secim.UygulaAsync(() => Task.CompletedTask, () => true, _ => true));
    }

    [Fact]
    public async Task Kayit_bulunamasa_da_kimlik_temizlenir()
    {
        var secim = new SorguSecimi("KrediId");
        secim.Iste(Sorgu("99"));
        Assert.False(await secim.UygulaAsync(() => Task.CompletedTask, () => true, _ => false));
        Assert.Null(secim.Istenen);
    }

    [Fact]
    public async Task Yukleme_basarisizsa_kimlik_sonraki_gorunuse_kalir()
    {
        var secim = new SorguSecimi("KrediId");
        secim.Iste(Sorgu("2"));
        var secildi = false;
        Assert.False(await secim.UygulaAsync(() => Task.CompletedTask, () => false, _ => secildi = true));
        Assert.False(secildi);
        Assert.Equal(2, secim.Istenen);
    }

    [Fact]
    public async Task Yukleme_surerken_gelen_yeni_istek_korunur_eszamanli_ikinci_cagri_yuklemez()
    {
        var secim = new SorguSecimi("KrediId");
        secim.Iste(Sorgu("2"));
        var kapi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var yuklenen = 0;
        var secilen = new List<int>();
        var ilk = secim.UygulaAsync(() =>
        {
            yuklenen++;
            return kapi.Task;
        }, () => false, id =>
        {
            secilen.Add(id);
            return true;
        });
        Assert.False(await secim.UygulaAsync(() => Task.FromResult(yuklenen++), () => true, _ => true));
        secim.Iste(Sorgu("4"));
        kapi.SetResult();
        Assert.False(await ilk);
        Assert.Equal(1, yuklenen);
        Assert.Empty(secilen);
        // Başarısız yüklemede eski kimlik geri yazılmaz: bu arada gelen yeni istek geçerlidir.
        Assert.Equal(4, secim.Istenen);
    }

    [Fact]
    public async Task Sonradan_biten_eski_istek_son_tiklamanin_secimini_ezmez()
    {
        // Sayfa açıkken iki bildirime art arda tıklanır; ilk yükleme ikinciden sonra biter. Seçilen son tıklamanın kaydıdır.
        var secim = new SorguSecimi("KrediId") { Gorunuyor = true };
        var ilkKapi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secilen = new List<int>();
        bool Sec(int id)
        {
            secilen.Add(id);
            return true;
        }
        secim.Iste(Sorgu("2"));
        var ilk = secim.UygulaAsync(() => ilkKapi.Task, () => true, Sec);
        secim.Iste(Sorgu("5"));
        Assert.True(await secim.UygulaAsync(() => Task.CompletedTask, () => true, Sec));
        ilkKapi.SetResult();
        Assert.False(await ilk);
        Assert.Equal([5], secilen);
        Assert.Null(secim.Istenen);
    }
}
