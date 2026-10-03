using System.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Uygulamanın tek bağlantı durumu (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §3). API istemcisinin ağ hatası ve
/// süre sınırı durumu kopuk, alınan her yanıt bağlı yapar (<see cref="IBaglantiBildirimleri"/>). Kopuk/bağlı geçişi
/// <see cref="Interlocked.Exchange(ref int, int)"/> ile atomiktir: eşzamanlı çağrılarda <see cref="BaglantiGeldi"/> ve
/// <see cref="BaglantiKoptu"/> geçiş başına tam bir kez tetiklenir (K-1). <see cref="Kopuk"/> bildirim geldiği an değişir (okuma
/// hatası sayfaya yazılırken doğru okunur); geçiş olayları (<see cref="BaglantiGeldi"/>, <see cref="BaglantiKoptu"/>) her zaman
/// yakalanan UI bağlamına <c>Post</c> ile gönderilir, isteğin kendi çağrı yığınından satır içi tetiklenmez (K-3); değişiklik
/// bildirimleri (<see cref="PropertyChanged"/>) modelin kurulduğu UI bağlamında (gerekirse Post ile) gelir. Kabuk şeridi
/// (BaglantiSeridi) buna bağlanır. Kopukken arka planda <see cref="YoklamaAraligi"/>'nda bir hafif yoklama yapılır (ekran denemesi
/// H-2): sunucu geri gelince istek beklenmeden durum bağlı olur, şerit kalkar ve <see cref="BaglantiGeldi"/> açık sayfayı yeniler.
/// Yoklama bağlıyken ve <see cref="Dispose"/>'dan (uygulama kapanışı) sonra çalışmaz.
/// </summary>
public sealed class BaglantiDurumu : INotifyPropertyChanged, IDisposable
{
    /// <summary>Kopukken bağlantı yoklamasının aralığı.</summary>
    public static readonly TimeSpan YoklamaAraligi = TimeSpan.FromSeconds(15);

    private readonly TimeProvider _zaman;
    private readonly SynchronizationContext? _ui;
    private readonly IBaglantiYoklamasi? _yoklama;
    private readonly Lock _yoklamaKilidi = new();
    private ITimer? _yoklamaZamanlayicisi;
    private int _yoklaniyor;
    private bool _kapandi;
    private int _kopukMu;   // 0 = bağlı, 1 = kopuk; Interlocked.Exchange ile atomik okunur/yazılır (K-1).
    private DateTimeOffset? _sonBaglanti;

    /// <param name="bildirimler">API istemcisi; verilmezse durum yalnız <see cref="Ulasildi"/> / <see cref="Ulasilamadi"/> ile değişir.</param>
    /// <param name="zaman">Son bağlantı saati ve yoklama zamanlayıcısı; verilmezse sistem saati.</param>
    /// <param name="yoklama">Kopukken arka plan yoklaması (GET /health); verilmezse yoklama yapılmaz (durum yalnız isteklerle değişir).</param>
    public BaglantiDurumu(IBaglantiBildirimleri? bildirimler = null, TimeProvider? zaman = null, IBaglantiYoklamasi? yoklama = null)
    {
        _zaman = zaman ?? TimeProvider.System;
        _ui = SynchronizationContext.Current;
        _yoklama = yoklama;
        if (bildirimler is null)
            return;
        bildirimler.SunucuyaUlasildi += (_, _) => Ulasildi();
        bildirimler.SunucuyaUlasilamadi += (_, _) => Ulasilamadi();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Bağlantı kopuktan bağlıya döndü: açık sayfa bir kez yenilenir (kabuk dinler). Her zaman Post ile gelir (K-3).</summary>
    public event EventHandler? BaglantiGeldi;

    /// <summary>Bağlantı bağlıdan kopuğa döndü (kabuk şeridi çıkar). Her zaman Post ile gelir (K-3).</summary>
    public event EventHandler? BaglantiKoptu;

    /// <summary>Son istek sunucuya ulaşamadı (ağ hatası ya da süre sınırı).</summary>
    public bool Kopuk => Volatile.Read(ref _kopukMu) != 0;

    /// <summary>Sunucudan son yanıtın alındığı an (yerel saat); hiç yanıt yoksa null.</summary>
    public DateTimeOffset? SonBaglanti => _sonBaglanti;

    /// <summary>Kabuk şeridinin metni: "Sunucuya ulaşılamıyor · Son bağlantı 14:05".</summary>
    public string SeritMetni => _sonBaglanti is { } zaman ? $"Sunucuya ulaşılamıyor · Son bağlantı {zaman:HH:mm}" : "Sunucuya ulaşılamıyor";

    public void Ulasildi()
    {
        _sonBaglanti = _zaman.GetLocalNow();
        var oncedenKopuk = Interlocked.Exchange(ref _kopukMu, 0) != 0;
        UiBaglaminda(() =>
        {
            Bildir(nameof(SonBaglanti));
            Bildir(nameof(SeritMetni));
            if (oncedenKopuk)
                Bildir(nameof(Kopuk));
        });
        if (oncedenKopuk)
        {
            YoklamayiDurdur();
            GecisiBildir(() => BaglantiGeldi?.Invoke(this, EventArgs.Empty));
        }
    }

    public void Ulasilamadi()
    {
        var oncedenBagliydi = Interlocked.Exchange(ref _kopukMu, 1) == 0;
        if (!oncedenBagliydi)
            return;
        YoklamayiBaslat();
        UiBaglaminda(() => Bildir(nameof(Kopuk)));
        GecisiBildir(() => BaglantiKoptu?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Son başlatılan yoklama (testler bekler).</summary>
    internal Task SonYoklama { get; private set; } = Task.CompletedTask;

    private void YoklamayiBaslat()
    {
        if (_yoklama is null)
            return;
        lock (_yoklamaKilidi)
        {
            if (_kapandi || _yoklamaZamanlayicisi is not null)
                return;
            _yoklamaZamanlayicisi = _zaman.CreateTimer(_ => SonYoklama = YoklaAsync(), null, YoklamaAraligi, YoklamaAraligi);
        }
    }

    private void YoklamayiDurdur()
    {
        lock (_yoklamaKilidi)
        {
            _yoklamaZamanlayicisi?.Dispose();
            _yoklamaZamanlayicisi = null;
        }
    }

    /// <summary>Tek yoklama: yalnız kopukken ve önceki yoklama bitmişse. Başarılıysa durum bağlı olur (istemci de bildirir);
    /// başarısızsa kopuk kalır, sonraki aralıkta yeniden denenir. Hata dışarı çıkmaz (zamanlayıcı geri çağrısı).</summary>
    private async Task YoklaAsync()
    {
        if (_yoklama is null || !Kopuk || Interlocked.Exchange(ref _yoklaniyor, 1) == 1)
            return;
        try
        {
            await _yoklama.YoklaAsync().ConfigureAwait(false);
            Ulasildi();
        }
        catch { /* ulaşılamadı: istemci bildirdi; durum kopuk kalır */ }
        finally { Volatile.Write(ref _yoklaniyor, 0); }
    }

    /// <summary>Uygulama kapanıyor: yoklama durur ve yeniden başlamaz.</summary>
    public void Dispose()
    {
        lock (_yoklamaKilidi)
            _kapandi = true;
        YoklamayiDurdur();
    }

    private void UiBaglaminda(Action eylem)
    {
        if (_ui is not null && SynchronizationContext.Current != _ui)
            _ui.Post(_ => eylem(), null);
        else
            eylem();
    }

    /// <summary>Geçiş olaylarını (BaglantiGeldi, BaglantiKoptu) her zaman yakalanan UI bağlamına Post ile gönderir: başarılı/
    /// başarısız isteğin kendi çağrı yığınından satır içi tetiklenmez (K-3). Yakalanan bağlam yoksa (test, arka plan hizmeti)
    /// doğrudan çağrılır. Dinleyicinin istisnası isteği düşürmesin diye yalıtılır.</summary>
    private void GecisiBildir(Action eylem)
    {
        if (_ui is not null)
            _ui.Post(_ => Guvenli(eylem), null);
        else
            Guvenli(eylem);
    }

    private static void Guvenli(Action eylem)
    {
        try
        { eylem(); }
        catch { /* dinleyicinin hatası isteği düşürmesin */ }
    }

    private void Bildir(string ad) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(ad));
}
