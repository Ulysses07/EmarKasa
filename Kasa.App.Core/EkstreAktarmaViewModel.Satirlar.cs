using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

/// <summary>Belge satırlarının sekmeleri; sırası <see cref="EkstreAktarmaViewModel.Sekmeler"/> ile aynı.</summary>
public enum EkstreSekmesi { Bekleyen, Kapanan }

/// <summary>Sayısı değişen sekme çipi (CipGrubu: Ad, Secili).</summary>
public partial class EkstreSekmeCipi : ObservableObject
{
    [ObservableProperty] private string _ad = "";
    [ObservableProperty] private bool _secili;
}

/// <summary>Etkisi gösterilemeyen işlemin bir nedeni; <see cref="SatirNo"/> doluysa o satırındır.</summary>
public sealed record EkstreSatirHatasi(int? SatirNo, string Ileti)
{
    public string Metin => SatirNo is { } no ? $"Satır {no}: {Ileti}" : Ileti;
    public bool SatirVar => SatirNo is not null;
}

/// <summary>Görünümün kaydırması istenen yer: etki gösteriminin sonucu/hataları ya da satır işlemlerinin iletisi (sekmelerin altı).</summary>
public enum EkstreSonucu { Onizleme, Satirlar }

// Bekleyen/Kapanan sekmeleri, yok sayma, yeniden okuma, toplu uygulama, toplamlar ve etki gösteriminin hataları
// (docs/specs/2026-11-25-pdf-ekrani.md).
public partial class EkstreAktarmaViewModel
{
    /// <summary>Toplu uygulamada türü değiştirmeyen seçenek (Kod boş).</summary>
    private static readonly EkstreSecenek TurKalsin = new("", "Satırdaki tür kalsın");
    private readonly TekrarAnahtari _yokSayKey = new(), _yenidenOkuKey = new();
    private bool _topluDegisiklik;
    /// <summary>Bu belgede yok sayılan satırların kapanmadan önceki modelleri: geri alınınca taslak geri gelir.</summary>
    private readonly Dictionary<int, EkstreSatirEditor> _yokSayilanTaslak = new();

    public ObservableCollection<EkstreSatirEditor> Bekleyenler { get; } = new();
    public ObservableCollection<EkstreSatirEditor> Kapananlar { get; } = new();
    /// <summary>Belgenin iptal edilmiş kayıtları (Kapanan sekmesinin altında; satırları yeniden bekler).</summary>
    public ObservableCollection<EkstreKayitSatiri> IptalEdilenler { get; } = new();
    public IReadOnlyList<EkstreSekmeCipi> Sekmeler { get; } = [new() { Secili = true }, new()];
    [ObservableProperty] private EkstreSekmesi _seciliSekme;
    public bool IptalEdilenVar => IptalEdilenler.Count > 0;

    /// <summary>"Etkisini göster" yapılamadıysa nedenleri (düğmenin altında); satır değişince o satırınki kalkar.</summary>
    public ObservableCollection<EkstreSatirHatasi> OnizlemeHatalari { get; } = new();
    public bool OnizlemeHataVar => OnizlemeHatalari.Count > 0;
    /// <summary>Satır işlemlerinin (yok say, geri al, toplu uygula) sonucu ve hatası; sekmelerin altında görünür.</summary>
    [ObservableProperty] private string? _satirMesaji;
    [ObservableProperty] private string? _satirHatasi;
    /// <summary>İşlem bitti: görünüm sonucuna kaydırır (istenen yer görünür sekmededir).</summary>
    public event EventHandler<EkstreSonucu>? SonucGosterIstendi;
    private void SonucuGoster(EkstreSonucu sonuc) => SonucGosterIstendi?.Invoke(this, sonuc);
    /// <summary>Satıra gidilmesi istendi (hata listesinden): görünüm listeyi satıra kaydırır.</summary>
    public event EventHandler<EkstreSatirEditor>? SatirGosterIstendi;

    /// <summary>Sunucu yok saymayı biliyor (eski sunucu alanı göndermez).</summary>
    public bool YokSayDestekleniyor => Belge?.YokSayilanlar is not null;
    public bool YenidenOkunabilir => Belge?.YenidenOkunabilir == true;
    public IReadOnlyList<EkstreAtlananSatir> Atlananlar => Belge?.Atlananlar ?? [];
    public bool AtlananVar => Atlananlar.Count > 0;
    /// <summary>Satırın düzenleyicisini açar; açıksa kapatır (satıra ikinci dokunuş). Mali seçimi değiştirmez.</summary>
    public void SatiriAcKapat(EkstreSatirEditor satir) => SeciliSatir = ReferenceEquals(SeciliSatir, satir) ? null : satir;
    [ObservableProperty] private bool _atlananAcik;
    public string AtlananDugmesi => $"Atlanan satırlar ({Atlananlar.Count}) {(AtlananAcik ? "gizle" : "göster")}";
    public string AtlananMetni => AtlananAcik ? string.Join("\n", Atlananlar.Select(a => $"Sayfa {a.Sayfa} · {a.Neden}: {a.Metin}")) : "";
    partial void OnAtlananAcikChanged(bool value) { OnPropertyChanged(nameof(AtlananDugmesi)); OnPropertyChanged(nameof(AtlananMetni)); }
    [RelayCommand]
    private void AtlananAcKapat() => AtlananAcik = !AtlananAcik;

    [RelayCommand]
    private void SekmeSec(EkstreSekmeCipi sekme) => SeciliSekme = ReferenceEquals(sekme, Sekmeler[1]) ? EkstreSekmesi.Kapanan : EkstreSekmesi.Bekleyen;
    partial void OnSeciliSekmeChanged(EkstreSekmesi value)
    {
        for (var i = 0; i < Sekmeler.Count; i++)
            Sekmeler[i].Secili = i == (int)value;
    }

    // ---- Toplamlar ----

    /// <summary>Belgenin TL satırlarının giriş, çıkış toplamı ve belirsiz sayısı (PDF'in toplamlarıyla karşılaştırmak için).</summary>
    public string YonOzeti => BelgeVar ? Toplam(Satirlar.Where(s => s.ToplamaGirer)) : "";
    /// <summary>İşaretli satırların toplamı; hiçbiri işaretli değilse ne yapılacağı.</summary>
    public string SecimOzeti => Satirlar.Count(s => s.Secili) is var n and > 0 ? $"{n} satır işaretli · {Toplam(Satirlar.Where(s => s.Secili))}" : "Hiç satır işaretli değil.";
    private static string Toplam(IEnumerable<EkstreSatirEditor> satirlar)
    {
        var liste = satirlar.ToList();
        string Yon(string yon, string ad)
        {
            var s = liste.Where(x => x.EtkinYon == yon).ToList();
            return $"{ad}{Bicim.Tl(s.Sum(x => x.EtkinTutar ?? 0))} ₺ ({s.Count} satır)";
        }
        var belirsiz = liste.Count(x => x.EtkinYon is not ("Giris" or "Cikis"));
        var tutarsiz = liste.Count(x => x.EtkinYon is "Giris" or "Cikis" && x.EtkinTutar is null);
        return $"{Yon("Giris", "Giriş +")} · {Yon("Cikis", "Çıkış −")}" + (belirsiz > 0 ? $" · Belirsiz {belirsiz} satır" : "")
            + (tutarsiz > 0 ? $" · {tutarsiz} satırın tutarı okunamadı, toplama girmedi" : "");
    }

    // ---- Toplu uygulama ----

    public ObservableCollection<EkstreSecenek> TopluTurler { get; } = new();
    public ObservableCollection<EkstreSecenek> TopluDagilimlar { get; } = new();
    public ObservableCollection<TakipKanalSecimi> TopluKanallar { get; } = new();
    [ObservableProperty] private EkstreSecenek? _topluTur;
    [ObservableProperty] private EkstreSecenek? _topluDagilim;
    /// <summary>Toplu uygulamanın sonucu ve eksiği (uygula düğmesinin altında).</summary>
    [ObservableProperty] private string? _topluMesaji;
    [ObservableProperty] private string? _topluHatasi;
    public bool TopluKanalGorunur => TopluDagilim?.Kod == DagilimBicimleri.Esit;
    partial void OnTopluDagilimChanged(EkstreSecenek? value) => OnPropertyChanged(nameof(TopluKanalGorunur));

    private void TopluSecenekleriKur(EkstreBelgeDto b)
    {
        TopluMesaji = null;
        TopluHatasi = null;
        var kart = b.Kaynak == EkstreKaynaklari.Kart;
        TakipMetni.Doldur(TopluTurler, kart ? [TurKalsin, new(EkstreIslemTurleri.KartHarcama, "Kart harcaması")]
            : [TurKalsin, new(EkstreIslemTurleri.Gelir, "Gelir"), new(EkstreIslemTurleri.Gider, "Gider")]);
        EkstreSecenek esit = new(DagilimBicimleri.Esit, "Seçilen kanallara eşit");
        TakipMetni.Doldur(TopluDagilimlar, kart ? [esit] : [new(DagilimBicimleri.Genel, "Yalnız genel kasa"), esit]);
        TakipMetni.Doldur(TopluKanallar, Kanallar.Select(k => new TakipKanalSecimi(k)));
        TopluTur = TopluTurler[0];
        TopluDagilim = kart ? TopluDagilimlar[0] : null;
    }
    /// <summary>Yenilemede kanallar değişmiş olabilir (pasif, silinmiş, yeni): işaretler kimlikle korunur.</summary>
    private void TopluKanallariTazele()
    {
        var secili = TopluKanallar.Where(k => k.Secili).Select(k => k.Veri.Id).ToHashSet();
        TakipMetni.Doldur(TopluKanallar, Kanallar.Select(k => new TakipKanalSecimi(k) { Secili = secili.Contains(k.Id) }));
    }

    /// <summary>Türü ve dağılımı işaretli satırlara yazar; kaydetmez, satır seçmez.</summary>
    [RelayCommand]
    private void TopluUygula()
    {
        if (!EditorMu || Mesgul || !VeriHazir || Belge is null)
            return;
        var secili = Satirlar.Where(s => s.Secili).ToList();
        var kanallar = TopluKanallar.Where(k => k.Secili).Select(k => k.Veri.Id).ToList();
        TopluMesaji = null;
        TopluHatasi = secili.Count == 0 ? "Toplu uygulamak için önce satırların kutularını işaretleyin."
            : TopluDagilim is null ? "Toplu uygulamak için kanal dağılımını seçin."
            : TopluKanalGorunur && kanallar.Count == 0 ? "Eşit dağılım için kanalları işaretleyin." : null;
        if (TopluHatasi is not null)
            return;
        var uygulanan = 0;
        _topluDegisiklik = true;
        try
        { uygulanan = secili.Count(s => s.TopluUygula(TopluTur is { Kod.Length: > 0 } tur ? tur.Kod : null, TopluDagilim!.Kod, TopluKanalGorunur ? kanallar : [])); }
        finally { _topluDegisiklik = false; }
        foreach (var s in secili)
            HatalariKaldir(s.Kaynak.No);
        GirdiDegisti();
        TopluMesaji = $"{uygulanan} satıra uygulandı; henüz kaydedilmedi."
            + (uygulanan < secili.Count ? $" {secili.Count - uygulanan} satır değişmedi (karta ödeme, iade, eşleştirme ya da türü seçilmemiş)." : "");
    }

    // ---- Yok say / yeniden oku ----

    [RelayCommand]
    private Task SeciliyiYokSayAsync() => YokSayAsync(Satirlar.Where(s => s.Secili).ToArray(), true);

    /// <summary>Satırları kaydetmeden kapatır (Kapanan) ya da geri açar (Bekleyen). Kasa ve kart borcu değişmez; diğer satırların
    /// taslakları korunur.</summary>
    public Task YokSayAsync(IReadOnlyList<EkstreSatirEditor> satirlar, bool yokSay)
    {
        SatirMesaji = null;
        SatirHatasi = null;
        return YurutAsync(async n =>
        {
            if (!EditorMu || !VeriHazir || Belge is null || !YokSayDestekleniyor)
                return;
            if (satirlar.Count == 0)
            {
                SatirHatasi = "Yok saymak için önce satırların kutularını işaretleyin.";
                SonucuGoster(EkstreSonucu.Satirlar);
                return;
            }
            if (satirlar.Any(s => !Satirlar.Contains(s) || s.Kayitli || s.YokSayildi == yokSay))
                return;
            var id = Belge.Id;
            var nolar = satirlar.Select(s => s.Kaynak.No).Order().ToArray();
            var g = new EkstreYokSayYaz(Guid.Empty, Belge.Surum, nolar, yokSay);
            var b = await api.EkstreYokSayAsync(id, g with { IstekId = _yokSayKey.Al(new { id, g }) });
            if (!Gecerli(n))
                return;
            _yokSayKey.Temizle();
            if (yokSay)
                foreach (var s in satirlar)
                    _yokSayilanTaslak[s.Kaynak.No] = s;
            BelgeyiYansit(b, koru: true);
            var ad = nolar.Length == 1 ? $"Satır {nolar[0]}" : $"{nolar.Length} satır";
            SatirMesaji = yokSay ? $"{ad} yok sayıldı; kasa değişmedi. Kapanan süzgecinden geri alabilirsiniz." : $"{ad} yeniden bekliyor.";
            await KurallariAlAsync(n);
            // Tek satırda liste yerinde kalır; toplu işlem listenin altındaki düğmeden yapılır, sonuç listenin üstündedir.
            if (Gecerli(n) && nolar.Length > 1)
                SonucuGoster(EkstreSonucu.Satirlar);
        }, hataIsle: SatirHatasiniYaz);
    }
    /// <summary>Kapanan sekmesindeki yok sayılan satırların sayısı; birden çoksa hepsi tek düğmeyle geri alınır.</summary>
    public int YokSayilanSayisi => Kapananlar.Count(s => s.YokSayildi);
    public bool TopluGeriAlGorunur => YokSayilanSayisi > 1;
    public string TopluGeriAlDugmesi => $"Yok sayılanların hepsini geri al ({YokSayilanSayisi})";
    [RelayCommand]
    private Task YokSayilanlariGeriAlAsync() => YokSayAsync(Kapananlar.Where(s => s.YokSayildi).ToArray(), false);
    private void SatirHatasiniYaz(Exception hata)
    {
        SatirMesaji = null;
        SatirHatasi = HataMesaji(hata);
        SonucuGoster(EkstreSonucu.Satirlar);
    }

    /// <summary>Hiç işlenmemiş belgenin saklanan PDF'ini yeniden okur; satırlar ve taslaklar baştan gelir.</summary>
    public Task YenidenOkuAsync()
    {
        SatirMesaji = null;
        SatirHatasi = null;
        return YurutAsync(async n =>
        {
            if (!EditorMu || !VeriHazir || Belge is null || !YenidenOkunabilir)
                return;
            var id = Belge.Id;
            var g = new EkstreYenidenOkuYaz(Guid.Empty, Belge.Surum);
            var b = await api.EkstreYenidenOkuAsync(id, g with { IstekId = _yenidenOkuKey.Al(new { id, g }) });
            if (!Gecerli(n))
                return;
            _yenidenOkuKey.Temizle();
            BelgeyiYansit(b);
            SatirMesaji = $"Belge yeniden okundu: {b.Satirlar.Count} satır.";
            await KurallariAlAsync(n);
            await YapayZekaDoldurAsync(n);
        }, hataIsle: SatirHatasiniYaz);
    }

    // ---- Etki gösteriminin hataları ----

    [GeneratedRegex(@"^Satır (?<no>\d+): (?<ileti>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex SatirIletisi();

    private void HatalariYaz(IEnumerable<EkstreSatirHatasi> hatalar)
    {
        TakipMetni.Doldur(OnizlemeHatalari, hatalar);
        OnPropertyChanged(nameof(OnizlemeHataVar));
        // Hatalar Bekleyen bölümündedir; Kapanan açıkken kaydetme çakışırsa neden gizli kalmasın.
        SeciliSekme = EkstreSekmesi.Bekleyen;
        SonucuGoster(EkstreSonucu.Onizleme);
    }
    private void HatalariTemizle()
    {
        if (OnizlemeHatalari.Count == 0)
            return;
        OnizlemeHatalari.Clear();
        OnPropertyChanged(nameof(OnizlemeHataVar));
    }
    /// <summary>Satır düzeltildi: o satırın ve satırsız (ör. "satır seçin") hatalar kalkar, diğer satırlarınki durur.</summary>
    private void HatalariKaldir(int satirNo)
    {
        var kalkan = OnizlemeHatalari.Where(h => h.SatirNo is null || h.SatirNo == satirNo).ToList();
        foreach (var h in kalkan)
            OnizlemeHatalari.Remove(h);
        if (kalkan.Count > 0)
            OnPropertyChanged(nameof(OnizlemeHataVar));
    }
    private void SatirDegisti(EkstreSatirEditor satir)
    {
        if (_topluDegisiklik)
            return;
        HatalariKaldir(satir.Kaynak.No);
        GirdiDegisti();
    }
    /// <summary>Sunucunun iletisi satır satır gelir ("Satır 4: …"); satırsız ileti (bağlantı, çakışma) genel hatadır.</summary>
    private void OnizlemeHatasiniYaz(Exception hata) => HatalariYaz((hata is HttpRequestException
            ? "Sunucuya ulaşılamadı; etki hesaplanmadı. Bağlantı gelince yeniden deneyin." : OkumaHataMesaji(hata))
        .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => SatirIletisi().Match(s) is { Success: true } m
            && int.TryParse(m.Groups["no"].Value, out var no) && Satirlar.Any(x => x.Kaynak.No == no)
            ? new EkstreSatirHatasi(no, m.Groups["ileti"].Value.Trim()) : new(null, s.Trim())));

    /// <summary>Hata listesinden satıra: Bekleyen sekmesine geçer, satırın düzenleyicisini açar, görünümden kaydırmasını ister.</summary>
    public void SatiraGit(EkstreSatirHatasi hata)
    {
        if (Satirlar.FirstOrDefault(s => s.Kaynak.No == hata.SatirNo) is not { } satir)
            return;
        SeciliSekme = satir.Kapali ? EkstreSekmesi.Kapanan : EkstreSekmesi.Bekleyen;
        SeciliSatir = satir;
        SatirGosterIstendi?.Invoke(this, satir);
    }

    // ---- Sekmelerin doldurulması ----

    /// <summary>Belgenin satır modelleri. <paramref name="koru"/>: aynı belgenin bekleyen satırlarının taslağı (doldurulan alanlar,
    /// işaret) korunur. Kanal ve kart listesi aynıysa model aynı kalır (açık düzenleyici ve liste yeri bozulmaz); yenilendiyse taslak
    /// güncel listelere taşınır. <paramref name="yenile"/> korunmayacak satırlardır (az önce gönderilenler).</summary>
    private List<EkstreSatirEditor> SatirlariKur(EkstreBelgeDto b, bool koru, IReadOnlySet<int>? yenile)
    {
        var ayniBelge = koru && Belge?.Id == b.Id;
        if (!ayniBelge)
            _yokSayilanTaslak.Clear();
        var eskiler = ayniBelge ? Satirlar.Where(s => !s.Kapali && yenile?.Contains(s.Kaynak.No) != true).ToDictionary(s => s.Kaynak.No) : [];
        var acikKayitlar = Kayitlar.Where(k => !k.Veri.Iptal).ToDictionary(k => k.Veri.SatirNo);
        var yokSayilan = b.YokSayilanlar?.ToHashSet() ?? [];
        var kanallar = Kanallar.ToArray();
        var kartlar = Kartlar.ToArray();
        return b.Satirlar.Select(s =>
        {
            var kapali = acikKayitlar.ContainsKey(s.No) || yokSayilan.Contains(s.No);
            var eski = !kapali && eskiler.TryGetValue(s.No, out var e) && e.Kaynak.KaynakSatir == s.KaynakSatir ? e : null;
            if (eski is not null && AyniOkuma(eski.Kaynak, s) && eski.Kanallar.SequenceEqual(kanallar) && eski.Kartlar.SequenceEqual(kartlar, ReferenceEqualityComparer.Instance))
                return eski;
            EkstreSatirEditor yeni = null!;
            yeni = new(s, b, kanallar, kartlar, () => SatirDegisti(yeni)) { Kayit = acikKayitlar.GetValueOrDefault(s.No) };
            // Satır bu arada yeniden okunduysa (başka cihaz) eski okumanın değerleri taslak sayılmaz: yalnız elle değiştirilen taşınır,
            // işareti kalkar.
            if (eski is not null && (AyniOkuma(eski.Kaynak, s) || eski.ElleDegisti))
                yeni.TaslagiAl(eski, isaret: AyniOkuma(eski.Kaynak, s));
            else if (!kapali && _yokSayilanTaslak.Remove(s.No, out var taslak) && AyniOkuma(taslak.Kaynak, s))
                yeni.TaslagiAl(taslak, isaret: false);
            if (acikKayitlar.ContainsKey(s.No))
                _yokSayilanTaslak.Remove(s.No);
            return yeni;
        }).ToList();
    }

    private static bool AyniOkuma(EkstreOkunanSatir a, EkstreOkunanSatir b) => a with { Uyarilar = b.Uyarilar } == b && a.Uyarilar.SequenceEqual(b.Uyarilar);

    private void SekmeleriDoldur()
    {
        Esitle(Bekleyenler, Satirlar.Where(s => !s.Kapali).ToList());
        Esitle(Kapananlar, Satirlar.Where(s => s.Kapali).ToList());
        TakipMetni.Doldur(IptalEdilenler, Kayitlar.Where(k => k.Veri.Iptal));
        Sekmeler[0].Ad = $"Bekleyen ({Bekleyenler.Count})";
        Sekmeler[1].Ad = $"Kapanan ({Kapananlar.Count})";
        OnPropertyChanged(nameof(IptalEdilenVar));
        OnPropertyChanged(nameof(YokSayilanSayisi));
        OnPropertyChanged(nameof(TopluGeriAlGorunur));
        OnPropertyChanged(nameof(TopluGeriAlDugmesi));
    }

    /// <summary>Listeyi boşaltmadan eşitler: korunan satırların yeri (ve listenin kaydırması) değişmez.</summary>
    private static void Esitle(ObservableCollection<EkstreSatirEditor> liste, List<EkstreSatirEditor> yeni)
    {
        var kalan = yeni.ToHashSet();
        for (var i = liste.Count - 1; i >= 0; i--)
            if (!kalan.Contains(liste[i]))
                liste.RemoveAt(i);
        for (var i = 0; i < yeni.Count; i++)
            if (i >= liste.Count || !ReferenceEquals(liste[i], yeni[i]))
                liste.Insert(i, yeni[i]);
    }
}
