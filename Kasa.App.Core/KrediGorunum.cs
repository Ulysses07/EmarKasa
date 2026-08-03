using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Kredi (banka kredisi) satırı için salt-okunur görünüm modeli.</summary>
public sealed partial class KrediGorunum : ObservableObject
{
    public int Id { get; }
    public string Ad { get; }
    public decimal CekilenTutar { get; }
    public DateOnly CekimTarihi { get; }
    public int TaksitSayisi { get; }
    public decimal AylikOdeme { get; }
    public int OdemeGunu { get; }
    public string Kanal { get; }

    /// <summary>Kredinin toplam geri ödemesi (aylık ödeme × taksit sayısı).</summary>
    public decimal Toplam => AylikOdeme * TaksitSayisi;

    public KrediGorunum(KrediDto d)
    {
        Id = d.Id; Ad = d.Ad; CekilenTutar = d.CekilenTutar;
        CekimTarihi = d.CekimTarihi; TaksitSayisi = d.TaksitSayisi;
        AylikOdeme = d.AylikOdeme; OdemeGunu = d.OdemeGunu; Kanal = d.Kanal;
    }
}
