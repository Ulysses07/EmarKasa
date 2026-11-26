using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Kasa.App.Core;

/// <summary>Hareket penceresinin süzgeçleri: Hazır ve Kontrol edin bekleyen satırların iki parçasıdır.</summary>
public enum EkstreSuzgec { Tumu, Hazir, Kontrol, Kapanan }

// Hareket penceresi (docs/specs/2026-11-26-ekstre-hareket-penceresi.md §2-3): süzgeç üyeliği hesaplanınca donar (çalışırken satır süzgeç
// değiştirmez), arama açıklamada Türkçe harf ve büyük/küçük harf duyarsız geçer; görünen liste aynı satır nesnelerini gösterir.
public partial class EkstreAktarmaViewModel
{
    private readonly Dictionary<EkstreSatirEditor, EkstreSuzgec> _uyelik = new(ReferenceEqualityComparer.Instance);
    [ObservableProperty] private EkstreSuzgec _suzgec;
    [ObservableProperty] private string _arama = "";
    /// <summary>Süzgeç ve aramadan geçen satırlar. Küçük değişiklik yerinde yapılır (kaydırma korunur); büyük değişiklikte liste bir kerede değişir.</summary>
    [ObservableProperty] private ObservableCollection<EkstreSatirEditor> _gorunenler = new();
    private const int EnCokYerindeDegisiklik = 100;
    /// <summary>Toplu tür/kanal ve alt çubuğun "Yok say"ı yalnız bunlara uygulanır: süzgecin ya da aramanın gizlediği işaretli satır değişmez.</summary>
    private List<EkstreSatirEditor> GorunenIsaretliler => [.. Gorunenler.Where(s => s.Secili)];
    public string TopluHedefMetni => $"Bu listede işaretli {GorunenIsaretliler.Count} satıra uygulanır.";
    public int HazirSayisi => _uyelik.Values.Count(u => u == EkstreSuzgec.Hazir);
    public int KontrolSayisi => _uyelik.Values.Count(u => u == EkstreSuzgec.Kontrol);
    public int KapananSayisi => _uyelik.Values.Count(u => u == EkstreSuzgec.Kapanan);

    private static readonly (EkstreSuzgec Suzgec, string Ad)[] SuzgecAdlari =
        [(EkstreSuzgec.Tumu, "Tümü"), (EkstreSuzgec.Hazir, "Hazır"), (EkstreSuzgec.Kontrol, "Kontrol edin"), (EkstreSuzgec.Kapanan, nameof(EkstreSuzgec.Kapanan))];
    /// <summary>Pencerenin süzgeç çipleri, sayılarıyla ("Hazır 462").</summary>
    public ObservableCollection<EkstreSekmeCipi> SuzgecCipleri { get; } = [new(), new(), new(), new()];

    [RelayCommand]
    private void SuzgecSec(EkstreSekmeCipi cip)
    {
        if (SuzgecCipleri.IndexOf(cip) is var i and >= 0)
            Suzgec = SuzgecAdlari[i].Suzgec;
    }

    private void CipleriYaz()
    {
        for (var i = 0; i < SuzgecAdlari.Length; i++)
        {
            var (suzgec, ad) = SuzgecAdlari[i];
            var sayi = suzgec switch { EkstreSuzgec.Hazir => HazirSayisi, EkstreSuzgec.Kontrol => KontrolSayisi, EkstreSuzgec.Kapanan => KapananSayisi, _ => Satirlar.Count };
            SuzgecCipleri[i].Ad = $"{ad} {sayi}";
            SuzgecCipleri[i].Secili = suzgec == Suzgec;
        }
    }

    /// <summary>Kayıt özeti düştü (seçim ya da satır değişti) ve gösterilecek hata da yok: pencere özeti yeniden göstermeyi önerir.</summary>
    public bool OnizlemeYenilenmeli => !OnizlemeVar && !OnizlemeHataVar;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(OnizlemeVar) or nameof(OnizlemeHataVar))
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(OnizlemeYenilenmeli)));
        if (e.PropertyName == nameof(SecimOzeti))
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(TopluHedefMetni)));
    }

    partial void OnSuzgecChanged(EkstreSuzgec value)
    {
        CipleriYaz();
        GorunenleriDoldur();
    }
    partial void OnAramaChanged(string value) => GorunenleriDoldur();

    /// <summary>Hazırları kendiliğinden işaretler ve her satırın süzgecini yeniden belirler: belge açılınca, öneriler gelince, yapay zekâ turundan
    /// sonra ve yenilemede.</summary>
    public void SuzgecleriHesapla()
    {
        KendiligindenIsaretle();
        _uyelik.Clear();
        foreach (var s in Satirlar)
        {
            _uyelik[s] = s.Kapali ? EkstreSuzgec.Kapanan : s.KontrolNedeni is null ? EkstreSuzgec.Hazir : EkstreSuzgec.Kontrol;
            s.GosterimiYenile();
        }
        foreach (var ad in new[] { nameof(HazirSayisi), nameof(KontrolSayisi), nameof(KapananSayisi) })
            OnPropertyChanged(ad);
        CipleriYaz();
        GorunenleriDoldur();
    }

    private void GorunenleriDoldur()
    {
        var aranan = EkstreSatirEditor.Sade(Arama.Trim());
        List<EkstreSatirEditor> yeni = [.. Satirlar.Where(s => (Suzgec == EkstreSuzgec.Tumu || _uyelik.TryGetValue(s, out var u) && u == Suzgec)
            && (aranan.Length == 0 || s.AramaMetni.Contains(aranan, StringComparison.Ordinal)))];
        var eski = Gorunenler.ToHashSet();
        if (Gorunenler.Count + yeni.Count - 2 * yeni.Count(eski.Contains) > EnCokYerindeDegisiklik)
            Gorunenler = new(yeni);
        else
            Esitle(Gorunenler, yeni);
        OnPropertyChanged(nameof(TopluHedefMetni));
    }
}
