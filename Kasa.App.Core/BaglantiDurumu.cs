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
/// (BaglantiSeridi) buna bağlanır.
/// </summary>
public sealed class BaglantiDurumu : INotifyPropertyChanged
{
    private readonly TimeProvider _zaman;
    private readonly SynchronizationContext? _ui;
    private int _kopukMu;   // 0 = bağlı, 1 = kopuk; Interlocked.Exchange ile atomik okunur/yazılır (K-1).
    private DateTimeOffset? _sonBaglanti;

    /// <param name="bildirimler">API istemcisi; verilmezse durum yalnız <see cref="Ulasildi"/> / <see cref="Ulasilamadi"/> ile değişir.</param>
    /// <param name="zaman">Son bağlantı saati; verilmezse sistem saati.</param>
    public BaglantiDurumu(IBaglantiBildirimleri? bildirimler = null, TimeProvider? zaman = null)
    {
        _zaman = zaman ?? TimeProvider.System;
        _ui = SynchronizationContext.Current;
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
            GecisiBildir(() => BaglantiGeldi?.Invoke(this, EventArgs.Empty));
    }

    public void Ulasilamadi()
    {
        var oncedenBagliydi = Interlocked.Exchange(ref _kopukMu, 1) == 0;
        if (!oncedenBagliydi)
            return;
        UiBaglaminda(() => Bildir(nameof(Kopuk)));
        GecisiBildir(() => BaglantiKoptu?.Invoke(this, EventArgs.Empty));
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
