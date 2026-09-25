using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class AlisDagilimEditor : ObservableObject
{
    [ObservableProperty] private AlisKanalDto? _kanal;
    [ObservableProperty] private decimal _tutar;
    public IReadOnlyList<AlisKanalDto> Kanallar { get; }
    public AlisDagilimEditor(IReadOnlyList<AlisKanalDto> kanallar) => Kanallar = kanallar;
}

public partial class AlisKalemEditor : ObservableObject
{
    [ObservableProperty] private string _aciklama = "";
    [ObservableProperty] private decimal _tutar;
    public ObservableCollection<AlisDagilimEditor> Dagilimlar { get; } = new();
    public decimal Dagilan => Dagilimlar.Sum(d => d.Tutar);
    public decimal DagilimFarki => Tutar - Dagilan;
    public string DagilimOzeti => DagilimFarki == 0 && Tutar > 0
        ? "Dağılım tamamlandı"
        : $"Dağıtılacak fark: {Bicim.Tl(DagilimFarki)} ₺";


    public AlisKalemEditor()
    {
        Dagilimlar.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null) foreach (AlisDagilimEditor d in e.OldItems) d.PropertyChanged -= DagilimDegisti;
            if (e.NewItems is not null) foreach (AlisDagilimEditor d in e.NewItems) d.PropertyChanged += DagilimDegisti;
            OzetiYenile();
        };
    }

    partial void OnTutarChanged(decimal value) => OzetiYenile();
    private void DagilimDegisti(object? sender, PropertyChangedEventArgs e) => OzetiYenile();
    private void OzetiYenile()
    {
        OnPropertyChanged(nameof(Dagilan));
        OnPropertyChanged(nameof(DagilimFarki));
        OnPropertyChanged(nameof(DagilimOzeti));
    }
}

public record AlisSatiri(AlisDto Veri)
{
    public string Baslik => $"#{Veri.Id} · {Veri.Tedarikci}";
    public string Alt => $"{Veri.Tarih:dd.MM.yyyy} · {Veri.Alici} · {DurumAdi(Veri.Durum)}";
    public string Tutar => $"{Bicim.Tl(Veri.Toplam)} ₺ · kalan {Bicim.Tl(Veri.Kalan)} ₺";
    public static string DurumAdi(string durum) => durum switch { "Taslak" => "Taslak", "Incelemede" => "İncelemede", "Onaylandi" => "Onaylandı", _ => durum };
}

public record AlisOdemeSatiri(AlisOdemeDto Veri)
{
    public bool KartVar => Veri.KrediKartiId is not null;
    public string OdemeYontemi => KartVar ? string.IsNullOrWhiteSpace(Veri.KrediKartiAdi) ? $"Kart #{Veri.KrediKartiId}" : Veri.KrediKartiAdi : "Ödeme";
    public string Baslik => $"{Veri.Tarih:dd.MM.yyyy} · {Bicim.Tl(Veri.Tutar)} ₺ · {OdemeYontemi} · Gider #{Veri.IslemId}";
    public string Dagilim => Veri.DagilimBekliyor ? "Dağılım bekliyor — alış onaylandığında kanallara yansır."
        : string.Join(" · ", Veri.Dagilimlar.Where(d => d.Tutar != 0).Select(d => $"{d.Kanal}: {Bicim.Tl(d.Tutar)} ₺"));
}

public record OdemeKartiSecenegi(int? Id, string Ad);
public record GiderSecenegi(IslemDto Veri)
{
    public string Ad => $"#{Veri.Id} · {Veri.Tarih:dd.MM.yyyy} · {Veri.Cari} · {Bicim.Tl(Veri.TutarTl)} ₺ · {(Veri.Tip == GiderTipi.KrediKarti ? "Kart harcaması" : "Nakit / banka")}";
}
