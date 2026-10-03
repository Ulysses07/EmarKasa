namespace Kasa.App.Core.Tests;

/// <summary>Kabuğun otomatik yenileme kararı (bağlantı geldiğinde; K-1: alt sınır, sondaki kenarda tek tetik, sınır içinde gelen
/// istek düşürülmez; Ö-1: kaydedilmemiş değişiklikte hiç tetiklenmez, form kullanıcı isteği olmadan ezilmez).</summary>
public class OtomatikYenilemeKarariTests
{
    private sealed class AyarlanabilirZaman(DateTimeOffset baslangic) : TimeProvider
    {
        private DateTimeOffset _simdi = baslangic;
        public void Ilerlet(TimeSpan sure) => _simdi += sure;
        public override DateTimeOffset GetUtcNow() => _simdi;
    }

    [Fact]
    public void Sinir_icinde_gelen_istek_dusurulmez_sinirin_sonunda_bir_kez_tetiklenir()
    {
        var saat = new AyarlanabilirZaman(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero));
        var karar = new OtomatikYenilemeKarari(saat, TimeSpan.FromSeconds(3));

        Assert.True(karar.Sor(false, out var bekleme1));
        Assert.Equal(TimeSpan.Zero, bekleme1);

        saat.Ilerlet(TimeSpan.FromSeconds(1));
        Assert.False(karar.Sor(false, out var bekleme2));
        Assert.Equal(TimeSpan.FromSeconds(2), bekleme2);

        // Sınır içinde ikinci istek: zaten bir bekleme zamanlı, yeniden zamanlanmaz (düşürülmez de).
        saat.Ilerlet(TimeSpan.FromSeconds(1));
        Assert.False(karar.Sor(false, out var bekleme3));
        Assert.Equal(TimeSpan.Zero, bekleme3);

        // Sınırın sonu (toplam 3 sn): tek tetik.
        saat.Ilerlet(TimeSpan.FromSeconds(1));
        Assert.True(karar.Sor(false, out var bekleme4));
        Assert.Equal(TimeSpan.Zero, bekleme4);

        // Yeni sınır başladı: hemen ardından gelen istek yine beklemeli.
        saat.Ilerlet(TimeSpan.FromMilliseconds(500));
        Assert.False(karar.Sor(false, out var bekleme5));
        Assert.Equal(TimeSpan.FromMilliseconds(2500), bekleme5);
    }

    [Fact]
    public void Kaydedilmemis_degisiklikte_hic_tetiklenmez_bekleyen_istek_dusurulur()
    {
        var saat = new AyarlanabilirZaman(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero));
        var karar = new OtomatikYenilemeKarari(saat, TimeSpan.FromSeconds(3));
        Assert.True(karar.Sor(false, out _));

        saat.Ilerlet(TimeSpan.FromSeconds(1));
        Assert.False(karar.Sor(true, out var bekleme));
        Assert.Equal(TimeSpan.Zero, bekleme);

        // Form kirliyken sınır dolsa da tetiklenmez.
        saat.Ilerlet(TimeSpan.FromSeconds(5));
        Assert.False(karar.Sor(true, out var bekleme2));
        Assert.Equal(TimeSpan.Zero, bekleme2);

        // Kirlilik kalkınca normal akışa döner: sınır zaten dolu olduğu için hemen tetiklenir.
        Assert.True(karar.Sor(false, out var bekleme3));
        Assert.Equal(TimeSpan.Zero, bekleme3);
    }

    /// <summary>Ö-2: otomatik yenileme bağlantıyı yeniden kopardıysa (ağır istek zaman aşımı, kalıcı proxy hatası) sonraki geçişte
    /// yenilenmez, yalnız şerit kalkar; böylece kopuş → yoklama → geliş → yenileme → kopuş döngüsü oluşmaz. Ondan sonraki geçiş
    /// yine yeniler.</summary>
    [Fact]
    public void Son_otomatik_yenileme_kopusla_bittiyse_sonraki_geciste_yenilemez()
    {
        var saat = new AyarlanabilirZaman(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero));
        var karar = new OtomatikYenilemeKarari(saat, TimeSpan.FromSeconds(3));
        Assert.True(karar.Sor(false, out _));
        karar.YenilemeBitti(kopusla: true);

        saat.Ilerlet(TimeSpan.FromSeconds(20));
        Assert.False(karar.Sor(false, out var bekleme));
        Assert.Equal(TimeSpan.Zero, bekleme);

        saat.Ilerlet(TimeSpan.FromSeconds(20));
        Assert.True(karar.Sor(false, out _));
        karar.YenilemeBitti(kopusla: false);

        saat.Ilerlet(TimeSpan.FromSeconds(20));
        Assert.True(karar.Sor(false, out _));
    }
}
