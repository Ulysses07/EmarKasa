using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Excel'den toplu yükleme: yapıştırılan TSV ya da seçilen xlsx / CSV önizleme tablosuna dökülür, her
/// satır yazarken doğrulanır, "Hepsini kaydet" tek istekte (sunucuda tek transaction, ya hep ya hiç)
/// gönderir. Bir tutarı seçili kanallara bölme (kuruş artığı ilk kanala) desteklenir. Banka dökümünde
/// (borç/alacak/bakiye başlığı ya da hem eksi hem artı tutar) yalnız gider tarafı kaydedilir.
/// </summary>
public partial class TopluGirisViewModel : TemelViewModel
{
    private readonly IKasaApi _api;
    private readonly IDosyaSecici? _secici;

    public TopluGirisViewModel(IKasaApi api, TimeProvider? zaman = null, IDosyaSecici? secici = null) : base(zaman)
    {
        _api = api;
        _secici = secici;
    }

    public const string SatirYokMesaji = "Önce Excel'den satırları yapıştırın ya da dosya seçin (Excel xlsx ya da CSV).";
    public const string SeciciYokMesaji = "Bu cihazda dosya seçilemiyor; satırları yapıştırın.";
    public const string DosyaBuyukMesaji = "Dosya çok büyük (en fazla 2 MB).";
    public const string BolmeKanalMesaji = "Bölmek için en az iki kanal seçin.";
    public const string EskiXlsMesaji = "Eski Excel (.xls) dosyası okunamaz. Excel'de 'Farklı kaydet' ile .xlsx ya da CSV seçin.";
    public const string XlsxOkunamadiMesaji = "Excel dosyası okunamadı. Dosya bozuk olabilir; Excel'de açıp .xlsx ya da CSV olarak yeniden kaydedin.";
    public const string AciklamaCariNotu = "Cari, 'Açıklama' sütunundan alındı.";
    public const string YeniCariKapaliNotu =
        "Her açıklama yeni cari olmasın diye \"Kayıtlı olmayan carileri ekle\" kapatıldı; cari hücrelerini kayıtlı adlarla düzeltin.";
    public const string BankaDokumuNotu = "Banka dökümü: borç (eksi) tutarlar gider, alacak (artı) tutarlar kaydedilmez.";
    public static string AtlananSatirNotu(int n) => $"Başlık satırından önceki {n} satır (hesap bilgisi vb.) atlandı.";
    public static string CokSatirMesaji => $"Tek seferde en fazla {TopluMetin.EnFazlaSatir} satır yüklenebilir.";
    public static string HataliSatirMesaji(int n) => $"{n} satırda hata var. Düzeltin ya da satırı kaldırın; hiçbir satır kaydedilmedi.";

    public ObservableCollection<TopluSatir> Satirlar { get; } = new();

    /// <summary>Boş kanal hücreleri için varsayılan kanal çipleri (aktif kanallar + Ortak).</summary>
    public ObservableCollection<SecimCipi> KanalCipleri { get; } = new();

    /// <summary>Bölme için seçilecek kanallar (aktif kanallar).</summary>
    public ObservableCollection<SecimCipi> BolmeKanallari { get; } = new();

    [ObservableProperty] private bool _editorMu;

    /// <summary>Yapıştırma kutusu; her değişiklikte önizleme yeniden kurulur.</summary>
    [ObservableProperty] private string _yapistirilan = "";

    /// <summary>Kayıtlı olmayan carileri kayıtla birlikte ekle (kapalıysa bu satırlar hatalıdır).</summary>
    [ObservableProperty] private bool _yeniCarileriEkle = true;

    [ObservableProperty] private string? _varsayilanKanal;

    /// <summary>Son başarılı kaydın özeti.</summary>
    [ObservableProperty] private string? _sonuc;

    /// <summary>Son seçilen dosyanın adı.</summary>
    [ObservableProperty] private string? _dosyaAdi;

    [ObservableProperty] private string _ozet = "";
    [ObservableProperty] private int _hataliSayisi;

    /// <summary>Kaynak hakkında bilgi (atlanan satırlar, banka dökümü, açıklamadan cari); yoksa null.</summary>
    [ObservableProperty] private string? _kaynakNotu;

    /// <summary>
    /// Tutar işareti anlamlı: tablo banka dökümü ya da hem eksi hem artı tutar var. O zaman yalnız
    /// gider tarafı kaydedilir (<see cref="EksilerGider"/>); öbür taraf satırları hatalıdır.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsaretAciklamasi))]
    private bool _isaretAnlamli;

    /// <summary>İşaret anlamlıyken hangi taraf gider: true = eksiler (banka dökümü, varsayılan), false = artılar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsaretAciklamasi))]
    private bool _eksilerGider = true;

    public string IsaretAciklamasi => EksilerGider
        ? "Eksi (borç, hesaptan çıkan) tutarlar gider sayılır; artı (alacak) satırlar kaydedilmez."
        : "Artı tutarlar gider sayılır; eksi (iade, alacak) satırlar kaydedilmez.";

    /// <summary>Gider tarafında olmayan (kaydedilmeyecek) satır sayısı; "kaldır" düğmesi bununla görünür.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GiderOlmayanVar), nameof(GiderOlmayanDugmesi))]
    private int _giderOlmayanSayisi;

    public bool GiderOlmayanVar => GiderOlmayanSayisi > 0;
    public string GiderOlmayanDugmesi => $"Gider olmayan {GiderOlmayanSayisi} satırı kaldır";

    /// <summary>Kaynak banka dökümü (borç/alacak/bakiye başlığı).</summary>
    private bool _bankaDokumu;

    /// <summary>"Yeni carileri ekle" kaynak yüzünden (açıklamadan cari) kendiliğinden kapatıldı mı.</summary>
    private bool _yeniCariOtomatikKapali;

    private IReadOnlyList<KanalDto> _kanallar = [];
    private IReadOnlyList<string> _cariler = [];
    private IReadOnlyList<string> _kalemler = [];
    private IReadOnlyList<KrediKartiDto> _kartlar = [];
    private bool _kuruluyor;

    public Task YukleAsync() => CalistirAsync(BaglamiYukleAsync);

    private async Task BaglamiYukleAsync()
    {
        var kanalGorevi = _api.KanallarAsync();
        var cariGorevi = _api.CarilerAsync();
        var kalemGorevi = _api.GiderKalemleriAsync();
        var kartGorevi = _api.KrediKartlariAsync();
        await Task.WhenAll(kanalGorevi, cariGorevi, kalemGorevi, kartGorevi);
        _kanallar = kanalGorevi.Result;
        _cariler = cariGorevi.Result.Select(c => c.Ad).ToList();
        _kalemler = kalemGorevi.Result.Select(k => k.Ad).ToList();
        _kartlar = kartGorevi.Result;

        var aktifler = _kanallar.Where(k => k.Aktif).OrderBy(k => k.Sira).Select(k => k.Ad).ToList();
        var seciliBolme = BolmeKanallari.Where(c => c.Secili).Select(c => c.Ad).ToHashSet();
        KanalCipleri.Clear();
        BolmeKanallari.Clear();
        foreach (var ad in aktifler)
        {
            KanalCipleri.Add(new SecimCipi(ad) { Secili = ad == VarsayilanKanal });
            BolmeKanallari.Add(new SecimCipi(ad) { Secili = seciliBolme.Contains(ad) });
        }
        KanalCipleri.Add(new SecimCipi(TopluDogrulama.OrtakKanal) { Secili = VarsayilanKanal == TopluDogrulama.OrtakKanal });
        HepsiniDogrula();
    }

    private TopluBaglam Baglam => new(_kanallar, _cariler, _kalemler, _kartlar, BugunTarih, YeniCarileriEkle, VarsayilanKanal,
        !IsaretAnlamli ? TopluIsaret.Yok : EksilerGider ? TopluIsaret.EksiGider : TopluIsaret.ArtiGider);

    partial void OnYapistirilanChanged(string value)
    {
        if (_kuruluyor) return;
        Sonuc = null;
        OnizlemeKur(value);
    }

    partial void OnYeniCarileriEkleChanged(bool value) => HepsiniDogrula();

    partial void OnEksilerGiderChanged(bool value) => HepsiniDogrula();

    partial void OnVarsayilanKanalChanged(string? value)
    {
        foreach (var c in KanalCipleri) c.Secili = c.Ad == value;
        HepsiniDogrula();
    }

    /// <summary>
    /// Metni satırlara döker. İlk satırlarda başlık aranır (banka dökümünde başlıktan önce hesap bilgisi
    /// olur); bulunursa sütunlar ona göre eşlenir ve öncesi atlanır. Cari "Açıklama"dan geliyorsa ya da
    /// kaynak banka dökümüyse "yeni carileri ekle" kapanır (her banka açıklaması yeni cari olmasın).
    /// </summary>
    private void OnizlemeKur(string metin)
    {
        foreach (var s in Satirlar) s.PropertyChanged -= SatirDegisti;
        Satirlar.Clear();
        Hata = null;
        var ayirici = TopluMetin.AyiriciBul(metin);
        var tablo = TopluMetin.Ayristir(metin, ayirici);
        var bulunan = TopluBaslik.Bul(tablo);
        var harita = bulunan?.Harita ?? TopluBaslik.Varsayilan;
        var bas = bulunan is { } b ? b.Satir + 1 : 0;
        int? sutunSayisi = bulunan is { } b2 ? tablo[b2.Satir].Count : null;
        for (var i = bas; i < tablo.Count; i++) Ekle(TopluSatir.Olustur(tablo[i], harita, ayirici, sutunSayisi));
        if (Satirlar.Count > TopluMetin.EnFazlaSatir) Hata = CokSatirMesaji;

        _bankaDokumu = bulunan is not null && TopluBaslik.BankaDokumu(harita);
        var aciklamadan = bulunan is { } b3 && harita.TryGetValue(TopluSutun.Cari, out var ci)
                          && ci < tablo[b3.Satir].Count && TopluBaslik.AciklamaMi(tablo[b3.Satir][ci]);
        var kapat = aciklamadan || _bankaDokumu;
        var notlar = new List<string>();
        if (bas > 1) notlar.Add(AtlananSatirNotu(bas - 1));
        if (_bankaDokumu) notlar.Add(BankaDokumuNotu);
        if (kapat != _yeniCariOtomatikKapali)
        {
            _yeniCariOtomatikKapali = kapat;
            YeniCarileriEkle = !kapat;   // kaynak türü değişince; kullanıcının sonraki seçimi korunur
        }
        if (aciklamadan) notlar.Add(AciklamaCariNotu);
        if (kapat && !YeniCarileriEkle) notlar.Add(YeniCariKapaliNotu);
        KaynakNotu = notlar.Count > 0 ? string.Join(" ", notlar) : null;

        Numarala();
        HepsiniDogrula();
    }

    /// <summary>
    /// Tutar işaretinin anlamlı olup olmadığını yeniden belirler (banka dökümü ya da geçerli tutarlar
    /// arasında hem eksi hem artı). Değiştiyse true (bütün satırlar yeniden doğrulanmalı).
    /// </summary>
    private bool IsaretiBelirle()
    {
        bool eksi = false, arti = false;
        foreach (var s in Satirlar)
        {
            if (TopluMetin.TutarCoz(s.TutarMetni).Tutar is null) continue;
            if (TopluMetin.EksiMi(s.TutarMetni)) eksi = true;
            else arti = true;
        }
        var anlamli = _bankaDokumu || (eksi && arti);
        if (anlamli == IsaretAnlamli) return false;
        IsaretAnlamli = anlamli;
        return true;
    }

    private void Ekle(TopluSatir s, int? konum = null)
    {
        s.PropertyChanged += SatirDegisti;
        if (konum is { } k) Satirlar.Insert(k, s);
        else Satirlar.Add(s);
    }

    private static readonly HashSet<string> GirdiAlanlari =
    [
        nameof(TopluSatir.TarihMetni), nameof(TopluSatir.Cari), nameof(TopluSatir.TutarMetni), nameof(TopluSatir.Kanal),
        nameof(TopluSatir.TipMetni), nameof(TopluSatir.Not), nameof(TopluSatir.KartMetni),
    ];

    /// <summary>Hücre düzenlenince yalnız o satır yeniden doğrulanır.</summary>
    private void SatirDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TopluSatir s || e.PropertyName is null || !GirdiAlanlari.Contains(e.PropertyName)) return;
        if (e.PropertyName == nameof(TopluSatir.TutarMetni) && IsaretiBelirle())
        {
            HepsiniDogrula();   // tablo tek işaretten iki işarete geçti (ya da tersi): hepsi etkilenir
            return;
        }
        TopluDogrulama.Dogrula(s, Baglam);
        OzetiGuncelle();
    }

    private void HepsiniDogrula()
    {
        IsaretiBelirle();
        var b = Baglam;
        foreach (var s in Satirlar) TopluDogrulama.Dogrula(s, b);
        OzetiGuncelle();
    }

    private void Numarala()
    {
        for (var i = 0; i < Satirlar.Count; i++) Satirlar[i].Sira = i + 1;
    }

    private void OzetiGuncelle()
    {
        HataliSayisi = Satirlar.Count(s => !s.Gecerli);
        GiderOlmayanSayisi = Satirlar.Count(s => s.GiderDegil);
        var toplam = Satirlar.Where(s => s.Gecerli).Sum(s => s.Tutar ?? 0m);
        var yeni = Satirlar.Where(s => s.YeniCari).Select(s => s.CariAdi!).Distinct(StringComparer.Create(Kultur.Turkce, true)).Count();
        Ozet = Satirlar.Count == 0
            ? ""
            : $"{Satirlar.Count} satır · {Satirlar.Count - HataliSayisi} geçerli · {HataliSayisi} hatalı · toplam {Bicim.Tl(toplam)} ₺"
              + (yeni > 0 ? $" · {yeni} yeni cari" : "");
        OnPropertyChanged(nameof(Kaydedilebilir));
    }

    public bool Kaydedilebilir => Satirlar.Count > 0 && HataliSayisi == 0 && Satirlar.Count <= TopluMetin.EnFazlaSatir;

    [RelayCommand]
    private void SecVarsayilanKanal(SecimCipi s) => VarsayilanKanal = VarsayilanKanal == s.Ad ? null : s.Ad;

    [RelayCommand]
    private void SecBolmeKanal(SecimCipi s) => s.Secili = !s.Secili;

    /// <summary>Satırı kaldırır.</summary>
    [RelayCommand]
    private void SatirKaldir(TopluSatir s)
    {
        s.PropertyChanged -= SatirDegisti;
        Satirlar.Remove(s);
        Numarala();
        OzetiGuncelle();
    }

    /// <summary>Gider tarafında olmayan (alacak / iade) bütün satırları kaldırır.</summary>
    [RelayCommand]
    private void GiderOlmayanlariKaldir()
    {
        foreach (var s in Satirlar.Where(x => x.GiderDegil).ToList())
        {
            s.PropertyChanged -= SatirDegisti;
            Satirlar.Remove(s);
        }
        Numarala();
        HepsiniDogrula();
    }

    /// <summary>
    /// Satırın tutarını seçili kanallara böler: satır, her kanala bir satır olacak şekilde çoğalır;
    /// kuruş artığı ilk kanala eklenir. Tutarın işareti (banka dökümünde borç) parçalarda korunur.
    /// </summary>
    [RelayCommand]
    private void Bol(TopluSatir s)
    {
        Hata = null;
        var kanallar = BolmeKanallari.Where(c => c.Secili).Select(c => c.Ad).ToList();
        try
        {
            Dogrula(kanallar.Count >= 2, BolmeKanalMesaji);
            Dogrula(s.YapiHatasi is null, $"{s.Sira}. satır bölünemedi: {s.YapiHatasi}");
            var (tutar, hata) = TopluMetin.TutarCoz(s.TutarMetni);
            Dogrula(hata is null, $"{s.Sira}. satır bölünemedi: {hata}");
            var isaret = TopluMetin.EksiMi(s.TutarMetni) ? "-" : "";
            var paylar = TopluMetin.Bol(tutar!.Value, kanallar.Count);
            var konum = Satirlar.IndexOf(s);
            if (konum < 0) return;
            s.PropertyChanged -= SatirDegisti;
            Satirlar.RemoveAt(konum);
            for (var i = 0; i < kanallar.Count; i++)
            {
                var y = s.Kopya();
                y.Kanal = kanallar[i];
                y.TutarMetni = isaret + TopluMetin.Bicimle(paylar[i]);
                Ekle(y, konum + i);
            }
            Numarala();
            HepsiniDogrula();
        }
        catch (DogrulamaHatasi ex) { Hata = ex.Message; }
    }

    /// <summary>
    /// Excel (xlsx; ilk sayfa) ya da CSV dosyası seçer (UTF-8 / Windows-1254; ayırıcı ; , ya da sekme)
    /// ve önizlemeye döker. xlsx sekmeyle ayrılmış metne çevrilir, yani yapıştırmayla aynı yoldan geçer.
    /// </summary>
    [RelayCommand]
    private Task DosyaSecAsync() => CalistirAsync(async () =>
    {
        Dogrula(_secici is not null, SeciciYokMesaji);
        var dosya = await _secici!.SecAsync();
        if (dosya is null) return;
        Dogrula(dosya.Icerik.Length <= TopluMetin.EnBuyukDosya, DosyaBuyukMesaji);
        Dogrula(!TopluXlsx.EskiXlsMi(dosya.Icerik), EskiXlsMesaji);
        var metin = TopluXlsx.XlsxMi(dosya.Icerik) ? TopluMetin.TsvYaz(XlsxOku(dosya.Icerik)) : TopluMetin.Coz(dosya.Icerik);
        DosyaAdi = dosya.Ad;
        Yapistirilan = metin;   // OnYapistirilanChanged önizlemeyi kurar
    });

    private static IReadOnlyList<IReadOnlyList<string>> XlsxOku(byte[] icerik)
    {
        try { return TopluXlsx.Oku(icerik); }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or IOException)
        {
            throw new DogrulamaHatasi(XlsxOkunamadiMesaji);
        }
    }

    [RelayCommand]
    private void Temizle()
    {
        _kuruluyor = true;
        try { Yapistirilan = ""; }
        finally { _kuruluyor = false; }
        foreach (var s in Satirlar) s.PropertyChanged -= SatirDegisti;
        Satirlar.Clear();
        DosyaAdi = null;
        Hata = null;
        KaynakNotu = null;
        _bankaDokumu = false;
        IsaretiBelirle();
        OzetiGuncelle();
    }

    /// <summary>
    /// Bütün satırları tek istekte kaydeder. İstemcide hatalı satır varsa hiç gönderilmez; sunucu bir
    /// satırı reddederse (tek transaction) hiçbiri kaydedilmez ve hata o satıra yazılır.
    /// </summary>
    [RelayCommand]
    private Task HepsiniKaydetAsync() => CalistirAsync(async () =>
    {
        Sonuc = null;
        Dogrula(Satirlar.Count > 0, SatirYokMesaji);
        Dogrula(Satirlar.Count <= TopluMetin.EnFazlaSatir, CokSatirMesaji);
        HepsiniDogrula();
        Dogrula(HataliSayisi == 0, HataliSatirMesaji(HataliSayisi));

        var gonderilen = Satirlar.ToList();
        var yeniCariVar = gonderilen.Any(s => s.YeniCari);
        var sonuc = await _api.TopluIslemKaydetAsync(gonderilen.Select(TopluDogrulama.Islem).ToList(), yeniCariVar);
        if (!sonuc.Kaydedildi)
        {
            foreach (var h in sonuc.SatirHatalari)
                if (h.Sira >= 1 && h.Sira <= gonderilen.Count) gonderilen[h.Sira - 1].Hata = h.Hata;
            OzetiGuncelle();
            throw new DogrulamaHatasi(sonuc.Hata ?? HataliSatirMesaji(sonuc.SatirHatalari.Count));
        }

        Sonuc = $"{sonuc.Eklenen} işlem kaydedildi · toplam {Bicim.Tl(sonuc.Toplam)} ₺"
                + (sonuc.YeniCariler.Count > 0 ? $" · {sonuc.YeniCariler.Count} yeni cari eklendi" : "");
        Temizle();
        if (sonuc.YeniCariler.Count > 0) _cariler = (await _api.CarilerAsync()).Select(c => c.Ad).ToList();
    });
}
