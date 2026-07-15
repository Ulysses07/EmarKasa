using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Kart + türetilmiş güncel borç, kalan limit ve ödeme geçmişi (görünüm modeli).</summary>
public sealed partial class KrediKartiGorunum : ObservableObject
{
    /// <summary>Karta özel ödeme ekleme formu tarihi (her kart bağımsız).</summary>
    [ObservableProperty] private DateTime _odemeTarihGiris = DateTime.Today;
    /// <summary>Karta özel ödeme ekleme formu tutarı (her kart bağımsız).</summary>
    [ObservableProperty] private decimal _odemeTutarGiris;
    /// <summary>Uygulama-içi "Ödedin mi?" şeridi görünürlüğü (VM doldurur).</summary>
    [ObservableProperty] private bool _odemeBekliyor;

    public int Id { get; }
    public string Ad { get; }
    public DateOnly KesimTarihi { get; }
    public DateOnly SonOdemeTarihi { get; }
    public decimal Limit { get; }
    public decimal AcilisBorc { get; }
    public decimal HarcamaToplam { get; }
    public decimal OdemeToplam { get; }
    public decimal GuncelBorc { get; }
    public decimal EkstreBorc { get; }
    public decimal KalanLimit => Limit - GuncelBorc;
    /// <summary>ProgressBar için kalan limit oranı (0–1).</summary>
    public double KalanOran => Limit <= 0 ? 0 : Math.Clamp((double)(KalanLimit / Limit), 0, 1);
    /// <summary>Güncel borcun kırılımı: açılış + harcama − ödeme.</summary>
    public string BorcKirilim => $"Açılış {Bicim.Tl(AcilisBorc)} · Harcama +{Bicim.Tl(HarcamaToplam)} · Ödeme −{Bicim.Tl(OdemeToplam)}";
    /// <summary>Kartın ödeme geçmişi (son ödemeler; VM doldurur).</summary>
    public ObservableCollection<KartOdemeDto> Odemeler { get; } = new();

    public KrediKartiGorunum(KrediKartiDto d)
    {
        Id = d.Id; Ad = d.Ad; KesimTarihi = d.KesimTarihi;
        SonOdemeTarihi = d.SonOdemeTarihi; Limit = d.Limit;
        AcilisBorc = d.AcilisBorc; HarcamaToplam = d.HarcamaToplam;
        OdemeToplam = d.OdemeToplam; GuncelBorc = d.GuncelBorc;
        EkstreBorc = d.EkstreBorc;
    }
}
