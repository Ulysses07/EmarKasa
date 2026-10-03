using System.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Uygulamanın tek bağlantı durumu (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §3). API istemcisinin ağ hatası ve
/// süre sınırı durumu kopuk, alınan her yanıt bağlı yapar (<see cref="IBaglantiBildirimleri"/>). <see cref="Kopuk"/> bildirim
/// geldiği an değişir (okuma hatası sayfaya yazılırken doğru okunur); değişiklik bildirimleri ve <see cref="BaglantiGeldi"/>
/// modelin kurulduğu UI bağlamında gelir. Kabuk şeridi (BaglantiSeridi) buna bağlanır.
/// </summary>
public sealed class BaglantiDurumu : INotifyPropertyChanged
{
    private readonly TimeProvider _zaman;
    private readonly SynchronizationContext? _ui;
    private volatile bool _kopuk;
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

    /// <summary>Bağlantı kopuktan bağlıya döndü: açık sayfa bir kez yenilenir (kabuk dinler).</summary>
    public event EventHandler? BaglantiGeldi;

    /// <summary>Son istek sunucuya ulaşamadı (ağ hatası ya da süre sınırı).</summary>
    public bool Kopuk => _kopuk;

    /// <summary>Sunucudan son yanıtın alındığı an (yerel saat); hiç yanıt yoksa null.</summary>
    public DateTimeOffset? SonBaglanti => _sonBaglanti;

    /// <summary>Kabuk şeridinin metni: "Sunucuya ulaşılamıyor · Son bağlantı 14:05".</summary>
    public string SeritMetni => _sonBaglanti is { } zaman ? $"Sunucuya ulaşılamıyor · Son bağlantı {zaman:HH:mm}" : "Sunucuya ulaşılamıyor";

    public void Ulasildi()
    {
        var oncedenKopuk = _kopuk;
        _sonBaglanti = _zaman.GetLocalNow();
        _kopuk = false;
        UiBaglaminda(() =>
        {
            Bildir(nameof(SonBaglanti));
            Bildir(nameof(SeritMetni));
            if (!oncedenKopuk)
                return;
            Bildir(nameof(Kopuk));
            BaglantiGeldi?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Ulasilamadi()
    {
        if (_kopuk)
            return;
        _kopuk = true;
        UiBaglaminda(() => Bildir(nameof(Kopuk)));
    }

    private void UiBaglaminda(Action eylem)
    {
        if (_ui is not null && SynchronizationContext.Current != _ui)
            _ui.Post(_ => eylem(), null);
        else
            eylem();
    }

    private void Bildir(string ad) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(ad));
}
