using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Paket D (özellik 35) — kasa sayımının devamı: satırlı sayım (nakit + küpür sayacı, bankalar, POS),
/// fark durumu ve açıklaması, "Neden değişti?" listesi, ay ay açıklanmamış fark ve son sayım
/// hatırlatması. <see cref="SayilanTutar"/> satırların toplamıdır; kasa rakamı yine değişmez.
/// </summary>
public partial class KasaSayimiViewModel
{
    public const int EnFazlaSatir = 20;
    public const int HatirlatmaGunu = 7;
    public const string SatirAdiMesaji = "Her sayım satırının bir adı olmalı.";
    public const string AciklamaMesaji = "Farkın açıklamasını yazın.";
    public static readonly string SatirSiniriMesaji = $"Bir sayımda en çok {EnFazlaSatir} satır olabilir.";

    // ---------------------------------------------------------------- satırlı sayım

    /// <summary>Sayım satırları. Hiçbirine tutar yazılmazsa sayım eskisi gibi tek tutarlıdır.</summary>
    public ObservableCollection<SayimSatiriGorunum> SayimSatirlari => _sayimSatirlari ??= SatirlariOlustur();
    private ObservableCollection<SayimSatiriGorunum>? _sayimSatirlari;

    /// <summary>Satırlardan birine tutar yazıldıysa toplam satırlardan gelir (tek tutar kutusu gizlenir).</summary>
    public bool SatirlarKullaniliyor => SayimSatirlari.Any(s => s.Tutar != 0m);
    public string SatirToplamiYazi => Bicim.Tl(SayimSatirlari.Sum(s => s.Tutar));

    /// <summary>
    /// Varsayılan satırlar: son satırlı sayımın satır adları; yoksa Nakit, İş Bankası, Ziraat,
    /// POS'ta bekleyen.
    /// </summary>
    public static ObservableCollection<SayimSatiriGorunum> VarsayilanSatirlar(IReadOnlyList<SayimSatiriDto>? son)
    {
        var l = new ObservableCollection<SayimSatiriGorunum>();
        if (son is { Count: > 0 })
            foreach (var s in son) l.Add(new SayimSatiriGorunum(s.Tur, s.Ad));
        else
        {
            l.Add(new SayimSatiriGorunum(SayimSatirTuru.Nakit, "Nakit"));
            l.Add(new SayimSatiriGorunum(SayimSatirTuru.Banka, "İş Bankası"));
            l.Add(new SayimSatiriGorunum(SayimSatirTuru.Banka, "Ziraat"));
            l.Add(new SayimSatiriGorunum(SayimSatirTuru.Pos, "POS'ta bekleyen"));
        }
        return l;
    }

    /// <summary>
    /// Satır listesini kurar ve izler. Tutarlı bir satır eklenip çıkınca toplam yeniden yazılır;
    /// boş satırlar tek tutarlı girişe dokunmaz.
    /// </summary>
    private ObservableCollection<SayimSatiriGorunum> SatirlariOlustur()
    {
        var l = VarsayilanSatirlar(null);
        foreach (var s in l) s.PropertyChanged += SatirDegisti;
        l.CollectionChanged += (_, e) =>
        {
            var tutarli = false;
            if (e.NewItems is not null)
                foreach (SayimSatiriGorunum s in e.NewItems) { s.PropertyChanged += SatirDegisti; tutarli |= s.Tutar != 0m; }
            if (e.OldItems is not null)
                foreach (SayimSatiriGorunum s in e.OldItems) { s.PropertyChanged -= SatirDegisti; tutarli |= s.Tutar != 0m; }
            if (tutarli) SatirToplaminiYaz();
            else
            {
                OnPropertyChanged(nameof(SatirlarKullaniliyor));
                OnPropertyChanged(nameof(SatirToplamiYazi));
            }
        };
        return l;
    }

    private void SatirDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SayimSatiriGorunum.Tutar)) SatirToplaminiYaz();
    }

    /// <summary>Satır tutarı değişince toplam sayılan tutara yazılır (canlı fark da güncellenir).</summary>
    private void SatirToplaminiYaz()
    {
        SayilanTutar = SayimSatirlari.Sum(s => s.Tutar);
        OnPropertyChanged(nameof(SatirlarKullaniliyor));
        OnPropertyChanged(nameof(SatirToplamiYazi));
    }

    /// <summary>Sayfa ilk yüklenirken ve kayıttan sonra: satırları son sayımın adlarıyla sıfırdan kurar.</summary>
    private void SatirlariSifirla(IReadOnlyList<SayimSatiriDto>? son)
    {
        var yeni = VarsayilanSatirlar(son);
        var eskiTutarVar = SatirlarKullaniliyor;
        SayimSatirlari.Clear();
        foreach (var s in yeni) SayimSatirlari.Add(s);
        if (!eskiTutarVar) return;   // tek tutarlı girişe dokunma
        SatirToplaminiYaz();
    }

    /// <summary>Kayıttan sonra: satırlar (adlarıyla) kalır, tutar ve küpür adetleri sıfırlanır.</summary>
    private void SatirlariTemizle()
    {
        foreach (var s in SayimSatirlari)
        {
            foreach (var k in s.Kupurler) k.Adet = 0;
            s.Tutar = 0m;
            s.KupurAcik = false;
        }
    }

    [RelayCommand]
    private void SatirEkle(string? tur)
    {
        if (SayimSatirlari.Count >= EnFazlaSatir) { Hata = SatirSiniriMesaji; return; }
        var t = Enum.TryParse<SayimSatirTuru>(tur, out var x) ? x : SayimSatirTuru.Diger;
        SayimSatirlari.Add(new SayimSatiriGorunum(t, SayimSatiriGorunum.VarsayilanAd(t)));
    }

    [RelayCommand]
    private void SatirSil(SayimSatiriGorunum s) => SayimSatirlari.Remove(s);

    [RelayCommand]
    private void KupurAcKapat(SayimSatiriGorunum s) => s.KupurAcik = !s.KupurAcik;

    /// <summary>
    /// Kayda gidecek satırlar: adı olan ya da tutarı olan satırlar (sıfır satır da "sayıldı, boş" demektir
    /// ve bir sonraki sayımın varsayılanı olur). Hiçbir satırda tutar yoksa null = tek tutarlı sayım.
    /// </summary>
    private IReadOnlyList<SayimSatiriDto>? GonderilecekSatirlar()
    {
        if (!SatirlarKullaniliyor) return null;
        var giden = SayimSatirlari.Where(s => s.Tutar != 0m || !string.IsNullOrWhiteSpace(s.Ad)).ToList();
        Dogrula(giden.Count <= EnFazlaSatir, SatirSiniriMesaji);
        Dogrula(giden.All(s => !string.IsNullOrWhiteSpace(s.Ad)), SatirAdiMesaji);
        return giden.Select(s => s.Dto()).ToList();
    }

    // ---------------------------------------------------------------- geçmiş ekleri

    /// <summary>Ay ay açıklanmamış fark (en yeni ay önce).</summary>
    public ObservableCollection<AylikSayimFarki> AylikFarklar { get; } = new();
    public bool AylikFarkVar => AylikFarklar.Count > 0;

    [ObservableProperty] private string _sonSayimMetni = "";
    /// <summary>Son sayımın üzerinden 7 günden fazla geçtiyse (ya da hiç sayım yoksa) hatırlatma.</summary>
    [ObservableProperty] private bool _sayimGecikti;

    /// <summary>Liste yüklenince: ay ay özet, son sayım hatırlatması ve (dokunulmadıysa) varsayılan satırlar.</summary>
    private void SayimEkleriniKur(IReadOnlyList<KasaSayimDto> liste)
    {
        AylikFarklar.Clear();
        foreach (var a in AylikSayimFarki.Hesapla(liste)) AylikFarklar.Add(a);
        OnPropertyChanged(nameof(AylikFarkVar));

        var son = liste.OrderByDescending(s => s.Tarih).ThenByDescending(s => s.Id).FirstOrDefault();
        if (son is null)
        {
            SonSayimMetni = "Henüz sayım yok. Kasayı haftada bir saymanız önerilir.";
            SayimGecikti = true;
        }
        else
        {
            var gun = BugunTarih.DayNumber - son.Tarih.DayNumber;
            SonSayimMetni = gun switch
            {
                <= 0 => "Son sayım bugün yapıldı.",
                1 => "Son sayım dün yapıldı.",
                _ => $"Son sayım {gun} gün önce ({son.Tarih.ToString("d MMMM", Kultur.Turkce)}).",
            };
            if (gun > HatirlatmaGunu) SonSayimMetni += " Haftada bir sayım önerilir.";
            SayimGecikti = gun > HatirlatmaGunu;
        }

        if (!SatirlarKullaniliyor)
        {
            var sonSatirli = liste.OrderByDescending(s => s.Tarih).ThenByDescending(s => s.Id)
                .FirstOrDefault(s => s.Satirlar is { Count: > 0 });
            SatirlariSifirla(sonSatirli?.Satirlar);
        }
    }

    // ---------------------------------------------------------------- fark durumu

    [RelayCommand]
    private void FarkFormuAcKapat(KasaSayimSatiri s)
    {
        s.FarkFormuAcik = !s.FarkFormuAcik;
        if (s.FarkFormuAcik) s.AciklamaGiris = s.FarkAciklamasi;
    }

    [RelayCommand]
    private Task FarkAciklaAsync(KasaSayimSatiri s) => FarkDurumuYazAsync(s, SayimFarkDurumu.Aciklandi);

    [RelayCommand]
    private Task FarkKabulEtAsync(KasaSayimSatiri s) => FarkDurumuYazAsync(s, SayimFarkDurumu.KabulEdildi);

    [RelayCommand]
    private Task FarkAcikYapAsync(KasaSayimSatiri s) => FarkDurumuYazAsync(s, SayimFarkDurumu.Acik);

    private Task FarkDurumuYazAsync(KasaSayimSatiri s, SayimFarkDurumu durum) => CalistirAsync(async () =>
    {
        Dogrula(EditorMu, HataMesaji.Yetkisiz);
        var aciklama = string.IsNullOrWhiteSpace(s.AciklamaGiris) ? null : s.AciklamaGiris.Trim();
        if (durum == SayimFarkDurumu.Aciklandi) Dogrula(aciklama is not null, AciklamaMesaji);
        await _api.SayimFarkiAsync(s.Id, new SayimFarkYaz(durum, aciklama));
        await ListeyiYukleAsync();
    });

    // ---------------------------------------------------------------- neden değişti?

    [RelayCommand]
    private Task NedenDegistiAsync(KasaSayimSatiri s) => CalistirAsync(async () =>
    {
        if (s.NedenAcik) { s.NedenAcik = false; return; }
        var n = await _api.SayimNedenDegistiAsync(s.Id);
        s.NedenleriKur(n, Zaman.LocalTimeZone);
        s.NedenAcik = true;
    });
}

/// <summary>Sayım formunun bir satırı (nakit, banka, POS, diğer); nakitte isteğe bağlı küpür sayacı.</summary>
public sealed partial class SayimSatiriGorunum : ObservableObject
{
    /// <summary>Küpürler (kuruş): 200, 100, 50, 20, 10, 5 TL banknot; 1 TL, 50/25/10/5 kuruş.</summary>
    public static readonly IReadOnlyList<int> KupurDegerleri = [20000, 10000, 5000, 2000, 1000, 500, 100, 50, 25, 10, 5];

    public SayimSatiriGorunum(SayimSatirTuru tur, string ad)
    {
        Tur = tur;
        _ad = ad;
        if (tur == SayimSatirTuru.Nakit)
            foreach (var k in KupurDegerleri)
            {
                var g = new KupurGorunum(k);
                g.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(KupurGorunum.Adet)) KupurToplaminiYaz(); };
                Kupurler.Add(g);
            }
    }

    public SayimSatirTuru Tur { get; }
    public string TurAdi => TurMetni(Tur);
    public bool NakitMi => Tur == SayimSatirTuru.Nakit;

    [ObservableProperty] private string _ad;
    [ObservableProperty] private decimal _tutar;
    [ObservableProperty] private bool _kupurAcik;

    public ObservableCollection<KupurGorunum> Kupurler { get; } = new();
    /// <summary>Küpür sayıldıysa satır tutarı küpürlerden gelir (tutar kutusu kilitlenir).</summary>
    public bool KupurKullaniliyor => Kupurler.Any(k => k.Adet > 0);
    public bool TutarDuzenlenebilir => !KupurKullaniliyor;
    public string KupurOzeti => KupurKullaniliyor ? $"Küpürlerden: {Bicim.Tl(Tutar)} ₺" : "Küpür sayacı";

    private void KupurToplaminiYaz()
    {
        Tutar = Kupurler.Sum(k => k.Tutar);
        OnPropertyChanged(nameof(KupurKullaniliyor));
        OnPropertyChanged(nameof(TutarDuzenlenebilir));
        OnPropertyChanged(nameof(KupurOzeti));
    }

    public SayimSatiriDto Dto() => new(Tur, Ad.Trim(), Tutar,
        KupurKullaniliyor ? Kupurler.Where(k => k.Adet > 0).Select(k => new KupurAdetDto(k.Kurus, k.Adet)).ToList() : null);

    public static string TurMetni(SayimSatirTuru t) => t switch
    {
        SayimSatirTuru.Nakit => "Nakit",
        SayimSatirTuru.Banka => "Banka",
        SayimSatirTuru.Pos => "POS",
        _ => "Diğer",
    };

    public static string VarsayilanAd(SayimSatirTuru t) => t switch
    {
        SayimSatirTuru.Nakit => "Nakit",
        SayimSatirTuru.Banka => "Banka",
        SayimSatirTuru.Pos => "POS'ta bekleyen",
        _ => "Diğer",
    };
}

/// <summary>Bir küpürün adedi.</summary>
public sealed partial class KupurGorunum : ObservableObject
{
    public KupurGorunum(int kurus) => Kurus = kurus;

    public int Kurus { get; }
    /// <summary>"200 ₺" / "50 kr".</summary>
    public string Ad => Kurus >= 100 ? $"{Kurus / 100} ₺" : $"{Kurus} kr";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tutar))]
    private int _adet;

    public decimal Tutar => Kurus * (decimal)Adet / 100m;

    partial void OnAdetChanged(int value)
    {
        if (value < 0) Adet = 0;
    }
}

/// <summary>Bir ayın açıklanmamış sayım farkı (çizgi/çubuk için).</summary>
public sealed class AylikSayimFarki
{
    public DateOnly Ay { get; init; }
    public string AyAdi => Ay.ToString("MMMM yyyy", Kultur.Turkce);
    public int SayimAdet { get; init; }
    /// <summary>Durumu "Açık" olan, farkı sıfırdan farklı sayım sayısı.</summary>
    public int AcikAdet { get; init; }
    /// <summary>Açık farkların net toplamı (sayılan − defter).</summary>
    public decimal AcikFark { get; init; }
    /// <summary>Açık farkların mutlak toplamı (çubuk uzunluğu).</summary>
    public decimal AcikFarkMutlak { get; init; }
    /// <summary>Çubuk oranı: en büyük ayın mutlak toplamına göre 0–1.</summary>
    public double Oran { get; init; }
    public string Metin => AcikAdet == 0
        ? $"{SayimAdet} sayım · açıklanmamış fark yok"
        : $"{SayimAdet} sayım · {AcikAdet} açık fark · net {KasaSayimiViewModel.FarkBicimi(AcikFark)} ₺";
    public decimal RenkDegeri => KasaSayimiViewModel.RenkDegeri(AcikFarkMutlak);

    /// <summary>Son 12 ay (sayımı olan aylar), en yeni önce.</summary>
    public static IReadOnlyList<AylikSayimFarki> Hesapla(IEnumerable<KasaSayimDto> sayimlar)
    {
        var aylar = sayimlar
            .GroupBy(s => new DateOnly(s.Tarih.Year, s.Tarih.Month, 1))
            .OrderByDescending(g => g.Key)
            .Take(12)
            .Select(g =>
            {
                var acik = g.Where(s => s.Fark != 0m && s.FarkDurumu == SayimFarkDurumu.Acik).ToList();
                return (Ay: g.Key, Adet: g.Count(), Acik: acik.Count, Net: acik.Sum(s => s.Fark), Mutlak: acik.Sum(s => Math.Abs(s.Fark)));
            })
            .ToList();
        var enBuyuk = aylar.Count == 0 ? 0m : aylar.Max(a => a.Mutlak);
        return aylar.Select(a => new AylikSayimFarki
        {
            Ay = a.Ay, SayimAdet = a.Adet, AcikAdet = a.Acik, AcikFark = a.Net, AcikFarkMutlak = a.Mutlak,
            Oran = enBuyuk == 0m ? 0 : (double)(a.Mutlak / enBuyuk),
        }).ToList();
    }
}

/// <summary>"Neden değişti?" listesinin bir satırı.</summary>
public sealed class SayimDegisikligiGorunum
{
    public SayimDegisikligiGorunum(SayimDegisikligiDto d, TimeZoneInfo tz)
    {
        Ozet = d.Ozet;
        var utc = d.ZamanUtc.Kind == DateTimeKind.Local ? d.ZamanUtc.ToUniversalTime() : DateTime.SpecifyKind(d.ZamanUtc, DateTimeKind.Utc);
        Ayrinti = $"{TimeZoneInfo.ConvertTimeFromUtc(utc, tz).ToString("dd.MM.yyyy HH:mm", Kultur.Turkce)} · {GecmisSatiri.RolMetni(d.Rol)} · {d.Tur} · {d.Eylem}";
    }

    public string Ozet { get; }
    public string Ayrinti { get; }
}

/// <summary>Sayım geçmişi satırının Paket D eki: satırlar, fark durumu, "Neden değişti?".</summary>
public sealed partial class KasaSayimSatiri : ObservableObject
{
    private void EkBilgileriKur(KasaSayimDto d)
    {
        Satirlar = d.Satirlar;
        FarkDurumu = d.FarkDurumu;
        FarkAciklamasi = d.FarkAciklamasi;
    }

    /// <summary>Satırlı sayımın satırları (eski tek tutarlı sayımda null).</summary>
    public IReadOnlyList<SayimSatiriDto>? Satirlar { get; private set; }
    public bool SatirlarVar => Satirlar is { Count: > 0 };
    /// <summary>"Nakit 12.000,00 · İş Bankası 30.000,00 · POS'ta bekleyen 2.500,00".</summary>
    public string SatirlarMetni => Satirlar is { Count: > 0 } l ? string.Join(" · ", l.Select(s => $"{s.Ad} {Bicim.Tl(s.Tutar)}")) : "";

    [ObservableProperty] private SayimFarkDurumu _farkDurumu;
    [ObservableProperty] private string? _farkAciklamasi;
    [ObservableProperty] private bool _farkFormuAcik;
    [ObservableProperty] private string? _aciklamaGiris;

    public bool FarkVar => Fark != 0m;
    public bool FarkAcik => FarkVar && FarkDurumu == SayimFarkDurumu.Acik;
    /// <summary>"Açık" / "Açıklandı: …" / "Kabul edildi"; fark yoksa boş.</summary>
    public string FarkDurumMetni => !FarkVar ? "" : FarkDurumu switch
    {
        SayimFarkDurumu.Aciklandi => string.IsNullOrWhiteSpace(FarkAciklamasi) ? "Açıklandı" : $"Açıklandı: {FarkAciklamasi}",
        SayimFarkDurumu.KabulEdildi => string.IsNullOrWhiteSpace(FarkAciklamasi) ? "Kabul edildi" : $"Kabul edildi: {FarkAciklamasi}",
        _ => "Açık: fark açıklanmadı",
    };

    partial void OnFarkDurumuChanged(SayimFarkDurumu value)
    {
        OnPropertyChanged(nameof(FarkAcik));
        OnPropertyChanged(nameof(FarkDurumMetni));
    }

    partial void OnFarkAciklamasiChanged(string? value) => OnPropertyChanged(nameof(FarkDurumMetni));

    // ---- Neden değişti?
    [ObservableProperty] private bool _nedenAcik;
    [ObservableProperty] private string _nedenOzeti = "";
    public ObservableCollection<SayimDegisikligiGorunum> Nedenler { get; } = new();

    public void NedenleriKur(NedenDegistiDto n, TimeZoneInfo tz)
    {
        Nedenler.Clear();
        foreach (var d in n.Degisiklikler) Nedenler.Add(new SayimDegisikligiGorunum(d, tz));
        var degisim = n.Degisim switch
        {
            null => "Bu tarih artık takip döneminin dışında.",
            0m => "Defter değeri değişmedi.",
            { } x => $"Defter değeri {KasaSayimiViewModel.FarkBicimi(x)} ₺ değişti.",
        };
        NedenOzeti = n.Degisiklikler.Count == 0
            ? $"Sayımdan sonra bu günü etkileyen değişiklik yok. {degisim}"
            : $"Sayımdan sonra bu günü etkileyen {n.Degisiklikler.Count} değişiklik. {degisim}";
    }
}
