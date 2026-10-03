using Kasa.ApiClient;

namespace Kasa.App.Core.Tests;

/// <summary>Ekran denemesi H-2: sunucu geri gelince şerit kendiliğinden kalkar. Bağlantı kopukken <see cref="BaglantiDurumu"/>
/// arka planda <see cref="BaglantiDurumu.YoklamaAraligi"/>'nda bir hafif yoklama yapar (GET /health); başarılı yoklama durumu
/// bağlı yapar ve <see cref="BaglantiDurumu.BaglantiGeldi"/> tetiklenir. Yoklama yalnız kopukken çalışır, bağlıyken ve uygulama
/// kapanınca (<see cref="BaglantiDurumu.Dispose"/>) durur.</summary>
public class BaglantiYoklamasiTests
{
    /// <summary>Zamanlayıcıları elle tetiklenen saat.</summary>
    private sealed class ElleZaman : TimeProvider
    {
        public List<ElleZamanlayici> Zamanlayicilar { get; } = [];
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 3, 15, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var z = new ElleZamanlayici(() => callback(state), dueTime, period);
            Zamanlayicilar.Add(z);
            return z;
        }
        public ElleZamanlayici? Calisan => Zamanlayicilar.SingleOrDefault(z => !z.Durdu);
    }

    private sealed class ElleZamanlayici(Action tik, TimeSpan due, TimeSpan period) : ITimer
    {
        public TimeSpan Due { get; private set; } = due;
        public TimeSpan Period { get; private set; } = period;
        public bool Durdu { get; private set; }
        public void Tetikle() => tik();
        public bool Change(TimeSpan dueTime, TimeSpan period) { (Due, Period) = (dueTime, period); return true; }
        public void Dispose() => Durdu = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class SahteIstemci : IBaglantiBildirimleri, IBaglantiYoklamasi
    {
        public event EventHandler? SunucuyaUlasildi;
        public event EventHandler<Exception>? SunucuyaUlasilamadi;
        public int Yoklama;
        public bool SunucuVar;
        public void Kop() => SunucuyaUlasilamadi?.Invoke(this, new HttpRequestException());
        public void Ulas() => SunucuyaUlasildi?.Invoke(this, EventArgs.Empty);
        public Task YoklaAsync(CancellationToken ct = default)
        {
            Yoklama++;
            if (!SunucuVar)
            {
                Kop();
                return Task.FromException(new HttpRequestException());
            }
            Ulas();
            return Task.CompletedTask;
        }
    }

    private static (BaglantiDurumu Durum, SahteIstemci Istemci, ElleZaman Zaman, List<string> Olaylar) Kur()
    {
        var istemci = new SahteIstemci();
        var zaman = new ElleZaman();
        var durum = new BaglantiDurumu(istemci, zaman, istemci);
        var olaylar = new List<string>();
        durum.BaglantiGeldi += (_, _) => olaylar.Add("geldi");
        return (durum, istemci, zaman, olaylar);
    }

    [Fact]
    public async Task Kopukken_yoklama_baslar_sunucu_gelince_bagli_yapar_ve_durur()
    {
        var (durum, istemci, zaman, olaylar) = Kur();
        Assert.Empty(zaman.Zamanlayicilar);   // bağlıyken yoklama yok

        istemci.Kop();
        var z = Assert.IsType<ElleZamanlayici>(zaman.Calisan);
        Assert.Equal((BaglantiDurumu.YoklamaAraligi, BaglantiDurumu.YoklamaAraligi), (z.Due, z.Period));
        Assert.Equal(TimeSpan.FromSeconds(15), BaglantiDurumu.YoklamaAraligi);

        z.Tetikle();                          // sunucu hâlâ yok: kopuk kalır, yoklama sürer
        await durum.SonYoklama;
        Assert.True(durum.Kopuk);
        Assert.False(z.Durdu);

        istemci.SunucuVar = true;
        z.Tetikle();
        await durum.SonYoklama;

        Assert.Equal(2, istemci.Yoklama);
        Assert.False(durum.Kopuk);
        Assert.Equal(["geldi"], olaylar);
        Assert.True(z.Durdu);
        Assert.Null(zaman.Calisan);
    }

    [Fact]
    public void Baska_istegin_yaniti_yoklamayi_durdurur_yeniden_kopunca_yeniden_baslar()
    {
        var (_, istemci, zaman, _) = Kur();
        istemci.Kop();
        var ilk = zaman.Calisan!;

        istemci.Ulas();
        Assert.True(ilk.Durdu);

        istemci.Kop();
        istemci.Kop();
        Assert.Equal(2, zaman.Zamanlayicilar.Count);
        Assert.NotNull(zaman.Calisan);
    }

    [Fact]
    public void Kapaninca_yoklama_durur_ve_yeniden_baslamaz()
    {
        var (durum, istemci, zaman, _) = Kur();
        istemci.Kop();
        var z = zaman.Calisan!;

        durum.Dispose();
        Assert.True(z.Durdu);

        istemci.Ulas();
        istemci.Kop();
        Assert.Null(zaman.Calisan);
        Assert.Single(zaman.Zamanlayicilar);
    }

    [Fact]
    public void Yoklamasiz_durum_zamanlayici_kurmaz()
    {
        var istemci = new SahteIstemci();
        var zaman = new ElleZaman();
        var durum = new BaglantiDurumu(istemci, zaman);

        istemci.Kop();

        Assert.True(durum.Kopuk);
        Assert.Empty(zaman.Zamanlayicilar);
    }
}
