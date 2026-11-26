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
    public ObservableCollection<EkstreSatirEditor> Gorunenler { get; } = new();
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
        var aranan = Sade(Arama.Trim());
        Esitle(Gorunenler, [.. Satirlar.Where(s => (Suzgec == EkstreSuzgec.Tumu || _uyelik.TryGetValue(s, out var u) && u == Suzgec)
            && (aranan.Length == 0 || Sade(s.Aciklama).Contains(aranan, StringComparison.Ordinal) || Sade(s.Kaynak.Aciklama).Contains(aranan, StringComparison.Ordinal)))]);
    }

    /// <summary>Karşılaştırma biçimi: Türkçe i/ı ve aksanlar sadeleşir, büyük harf.</summary>
    private static string Sade(string metin) => new string(metin.Replace('ı', 'i').Replace('İ', 'I').Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();
}
