using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Paket D (özellik 34) — kart ekstresi mutabakatı. Banka ekstresindeki dönem borcu yazılır;
/// uygulamanın hesapladığı dönem sonu borcu ve dönemdeki kartlı işlemler tik kutularıyla gösterilir,
/// fark kırmızı çıkar (girilmemiş harcama, faiz, ücret). Ayrı bir kayıttır: kart borcu hesabı değişmez.
/// </summary>
public partial class KartMutabakatViewModel : TemelViewModel
{
    private readonly IKasaApi _api;

    public KartMutabakatViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman) => _api = api;

    public const string DonemSecinMesaji = "Önce bir ekstre dönemi seçin.";
    public const string EkstreTutariMesaji = "Ekstredeki dönem borcunu yazın.";

    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] private int _kartId;
    [ObservableProperty] private string _kartAdi = "";
    /// <summary>Kayıt / silme sonrası bilgi satırı.</summary>
    [ObservableProperty] private string? _bilgi;

    /// <summary>Kapanmış ekstre dönemleri, en yeni önce.</summary>
    public ObservableCollection<KartDonemSatiri> Donemler { get; } = new();

    /// <summary>Seçili dönemin ayrıntısı (yüklenene kadar null).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetayVar))]
    private KartMutabakatDetayDto? _detay;
    public bool DetayVar => Detay is not null;

    public ObservableCollection<MutabakatIslemSatiri> Islemler { get; } = new();
    public ObservableCollection<KartMutabakatOdemeDto> Odemeler { get; } = new();

    /// <summary>Ekstredeki dönem borcu (kullanıcı yazar; alacaklı kartta eksi).</summary>
    [ObservableProperty] private decimal _ekstreTutari;
    /// <summary>Ekstre tutarı yazıldı mı (ya da kayıtlı mı)? Yazılmadan fark gösterilmez.</summary>
    [ObservableProperty] private bool _ekstreGirildi;
    /// <summary>
    /// Ekstre kutusunun metni (sayfa buna bağlanır). Boş kutu "yazılmadı", "0" ise "ekstre borcu sıfır"
    /// demektir; ikisi aynı tutar olduğu için yazıldı bilgisi tutardan değil metinden gelir. Fazla ödenmiş
    /// kartın ekstresi eksi yazılabilir ("-250,00"; <see cref="ParaGiris.AyristirIsaretli"/>).
    /// </summary>
    [ObservableProperty] private string _ekstreGiris = "";
    /// <summary>Kutudaki metin geçersizse nedeni (kaydetmeyi durdurur; son geçerli tutar kalır).</summary>
    [ObservableProperty] private string? _ekstreGirisHatasi;
    [ObservableProperty] private string? _not;

    private bool _yukleniyor;
    private bool _girisYaziliyor;
    private int _detaySurumu;

    // ---------------------------------------------------------------- hesaplanan metinler

    /// <summary>Ekstre − uygulamanın hesapladığı dönem sonu borcu (ekstre yazılmadıysa null).</summary>
    public decimal? Fark => Detay is { } d && EkstreGirildi ? EkstreTutari - d.HesaplananBorc : null;
    public bool FarkVar => Fark is { } f && f != 0m;
    public bool Uyusuyor => Fark == 0m;
    public string FarkMetni => Fark is not { } f ? "" : f switch
    {
        0m => "Ekstre uygulamanın hesabıyla uyuşuyor.",
        // Uygulamaya göre kart alacaklıyken ekstre sıfır/borç: çoğu kez alacak bakiye eksisiz yazılmıştır.
        > 0m when Detay!.HesaplananBorc < 0m && EkstreTutari >= 0m =>
            $"Ekstre {Bicim.Tl(f)} ₺ fazla. Uygulamaya göre kart {Bicim.Tl(-Detay.HesaplananBorc)} ₺ alacaklı; ekstrede alacak bakiye varsa eksiyle yazın (ör. -{Bicim.Tl(-Detay.HesaplananBorc)}). Yoksa girilmemiş harcama, faiz ya da ücret olabilir.",
        > 0m => $"Ekstre {Bicim.Tl(f)} ₺ fazla: girilmemiş harcama, faiz ya da ücret olabilir.",
        _ => $"Ekstre {Bicim.Tl(-f)} ₺ eksik: uygulamada fazladan ya da yanlış tarihli harcama olabilir.",
    };
    /// <summary>"+120,00" / "0,00"; ekstre yazılmadıysa "—".</summary>
    public string FarkYazi => Fark is { } f ? KasaSayimiViewModel.FarkBicimi(f) : "—";
    /// <summary>ParaRenk için: fark 0 → yeşil, aksi kırmızı.</summary>
    public decimal FarkRenkDegeri => -Math.Abs(Fark ?? 0m);

    /// <summary>Tiklenmemiş işlemlerin toplamı (ekstrede görülmeyenler).</summary>
    public decimal TiksizToplam => Islemler.Where(i => !i.Tikli).Sum(i => i.Tutar);
    public int TikliAdet => Islemler.Count(i => i.Tikli);
    public string TikOzeti => Islemler.Count == 0
        ? "Bu dönemde karta bağlı işlem yok."
        : $"{TikliAdet}/{Islemler.Count} işlem ekstrede işaretlendi · işaretlenmeyenler {Bicim.Tl(TiksizToplam)} ₺";

    public string DonemBasligi => Detay is { } d
        ? $"{d.Baslangic.ToString("d MMM", Kultur.Turkce)} – {d.Kesim.ToString("d MMM yyyy", Kultur.Turkce)} dönemi · son ödeme {d.SonOdeme.ToString("d MMM", Kultur.Turkce)}"
        : "";
    /// <summary>"Devreden 1.000,00 + harcama 2.500,00 − ödeme 1.000,00 = 2.500,00".</summary>
    public string HesapKirilimi => Detay is { } d
        ? $"Devreden {Bicim.Tl(d.DevredenBorc)} + harcama {Bicim.Tl(d.DonemHarcama)} − ödeme {Bicim.Tl(d.DonemOdeme)} = {Bicim.Tl(d.HesaplananBorc)} ₺"
        : "";

    /// <summary>Kayıttan sonra dönemin işlemleri değiştiyse uyarı (kayıttaki hesap ≠ bugünkü hesap).</summary>
    public string DegisimUyarisi => Detay is { KayittakiHesaplanan: { } k } d && k != d.HesaplananBorc
        ? $"Mutabakat kaydedildiğinde hesaplanan borç {Bicim.Tl(k)} ₺ idi; dönem kayıtları sonradan değişti."
        : "";
    public bool DegisimVar => DegisimUyarisi.Length > 0;

    public string DurumMetni => Detay?.Durum switch
    {
        null => "Bu dönem için mutabakat kaydı yok.",
        KartMutabakatDurumu.Mutabik => "Mutabık",
        KartMutabakatDurumu.FarkKabul => "Fark kabul edildi",
        _ => "Açık: fark açıklanmadı",
    };
    public bool KayitVar => Detay?.MutabakatId is not null;

    private void Bildir()
    {
        OnPropertyChanged(nameof(Fark));
        OnPropertyChanged(nameof(FarkVar));
        OnPropertyChanged(nameof(Uyusuyor));
        OnPropertyChanged(nameof(FarkMetni));
        OnPropertyChanged(nameof(FarkYazi));
        OnPropertyChanged(nameof(FarkRenkDegeri));
        OnPropertyChanged(nameof(TiksizToplam));
        OnPropertyChanged(nameof(TikliAdet));
        OnPropertyChanged(nameof(TikOzeti));
    }

    partial void OnEkstreTutariChanged(decimal value)
    {
        // Kod tutarı doğrudan yazdıysa (kutudan değil) kutu da ona uyar.
        if (!_yukleniyor && !_girisYaziliyor)
        {
            EkstreGirildi = true;
            KutuyuYaz(value);
        }
        Bildir();
    }

    partial void OnEkstreGirisChanged(string value)
    {
        if (_yukleniyor || _girisYaziliyor) return;
        var s = ParaGiris.AyristirIsaretli(value);
        EkstreGirisHatasi = s.Gecerli ? null : s.Hata;
        if (!s.Gecerli) return;
        _girisYaziliyor = true;
        try
        {
            EkstreTutari = s.Tutar;
            EkstreGirildi = !string.IsNullOrWhiteSpace(value);
        }
        finally { _girisYaziliyor = false; }
        Bildir();
    }

    /// <summary>Kutuya tutarın metnini yazar; sıfır da "0" görünür (boş kutu "yazılmadı" demektir).</summary>
    private void KutuyuYaz(decimal? tutar)
    {
        _girisYaziliyor = true;
        try
        {
            EkstreGiris = tutar is not { } t ? "" : t == 0m ? "0" : ParaGiris.Bicimle(t);
            EkstreGirisHatasi = null;
        }
        finally { _girisYaziliyor = false; }
    }

    partial void OnEkstreGirildiChanged(bool value) => Bildir();

    partial void OnDetayChanged(KartMutabakatDetayDto? value)
    {
        Bildir();
        OnPropertyChanged(nameof(DonemBasligi));
        OnPropertyChanged(nameof(HesapKirilimi));
        OnPropertyChanged(nameof(DegisimUyarisi));
        OnPropertyChanged(nameof(DegisimVar));
        OnPropertyChanged(nameof(DurumMetni));
        OnPropertyChanged(nameof(KayitVar));
    }

    // ---------------------------------------------------------------- yükleme

    /// <summary>Kartın dönemlerini yükler ve en yeni dönemi (ya da önceden seçileni) açar.</summary>
    public Task YukleAsync(int kartId) => CalistirAsync(async () =>
    {
        Bilgi = null;
        var ayniKart = kartId == KartId;
        KartId = kartId;
        var seciliKesim = ayniKart ? Detay?.Kesim : null;
        await DonemleriYukleAsync();
        var hedef = Donemler.FirstOrDefault(d => d.Kesim == seciliKesim) ?? Donemler.FirstOrDefault();
        if (hedef is null) { Detay = null; Islemler.Clear(); Odemeler.Clear(); return; }
        await DetayYukleAsync(hedef.Kesim);
    });

    private async Task DonemleriYukleAsync()
    {
        var liste = await _api.KartDonemleriAsync(KartId);
        var secili = Detay?.Kesim;
        Donemler.Clear();
        foreach (var d in liste) Donemler.Add(new KartDonemSatiri(d) { Secili = d.Kesim == secili });
    }

    [RelayCommand]
    private Task SecDonemAsync(KartDonemSatiri s) => CalistirAsync(async () =>
    {
        Bilgi = null;
        await DetayYukleAsync(s.Kesim);
    });

    private async Task DetayYukleAsync(DateOnly kesim)
    {
        var surum = ++_detaySurumu;
        KartMutabakatDetayDto d;
        try { d = await _api.KartMutabakatAsync(KartId, kesim); }
        catch when (surum != _detaySurumu) { return; }
        if (surum != _detaySurumu) return;
        DetayiKur(d);
    }

    private void DetayiKur(KartMutabakatDetayDto d)
    {
        _yukleniyor = true;
        try
        {
            KartAdi = d.KartAdi;
            foreach (var i in Islemler) i.PropertyChanged -= IslemDegisti;
            Islemler.Clear();
            foreach (var i in d.Islemler)
            {
                var s = new MutabakatIslemSatiri(i);
                s.PropertyChanged += IslemDegisti;
                Islemler.Add(s);
            }
            Odemeler.Clear();
            foreach (var o in d.Odemeler) Odemeler.Add(o);
            EkstreTutari = d.EkstreTutari ?? 0m;
            EkstreGirildi = d.EkstreTutari is not null;
            KutuyuYaz(d.EkstreTutari);
            Not = d.Not;
            Detay = d;
            foreach (var s in Donemler) s.Secili = s.Kesim == d.Kesim;
        }
        finally { _yukleniyor = false; }
        Bildir();
    }

    private void IslemDegisti(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MutabakatIslemSatiri.Tikli)) Bildir();
    }

    [RelayCommand]
    private void TumunuTikle()
    {
        var hepsi = Islemler.All(i => i.Tikli);
        foreach (var i in Islemler) i.Tikli = !hepsi;
    }

    // ---------------------------------------------------------------- kaydet / farkı kabul / sil

    [RelayCommand]
    private Task KaydetAsync() => KaydetIcAsync(farkKabul: false);

    /// <summary>Fark açıklandı (faiz, ücret…): dönem "Fark kabul edildi" olarak kapanır; not önerilir.</summary>
    [RelayCommand]
    private Task FarkiKabulEtAsync() => KaydetIcAsync(farkKabul: true);

    private Task KaydetIcAsync(bool farkKabul) => CalistirAsync(async () =>
    {
        Bilgi = null;
        Dogrula(EditorMu, HataMesaji.Yetkisiz);
        Dogrula(Detay is not null, DonemSecinMesaji);
        Dogrula(EkstreGirisHatasi is null, EkstreGirisHatasi ?? "");
        Dogrula(EkstreGirildi, EkstreTutariMesaji);
        var d = Detay!;
        var tikli = Islemler.Where(i => i.Tikli).Select(i => i.Id).ToList();
        var not = string.IsNullOrWhiteSpace(Not) ? null : Not.Trim();
        var yeni = await _api.KartMutabakatKaydetAsync(new KartMutabakatYaz(KartId, d.Kesim, EkstreTutari, tikli, not, farkKabul));
        DetayiKur(yeni);
        Bilgi = yeni.Durum switch
        {
            KartMutabakatDurumu.Mutabik => "Mutabakat kaydedildi: ekstre uyuşuyor.",
            KartMutabakatDurumu.FarkKabul => "Mutabakat kaydedildi: fark kabul edildi.",
            _ => "Mutabakat kaydedildi; fark açık duruyor.",
        };
        await DonemleriYukleAsync();
    });

    [RelayCommand]
    private Task SilAsync() => CalistirAsync(async () =>
    {
        Bilgi = null;
        Dogrula(EditorMu, HataMesaji.Yetkisiz);
        if (Detay is not { MutabakatId: { } id } d) return;
        await _api.KartMutabakatSilAsync(id);
        Bilgi = "Mutabakat kaydı silindi.";
        await DetayYukleAsync(d.Kesim);
        await DonemleriYukleAsync();
    });
}

/// <summary>Dönem listesi satırı.</summary>
public sealed partial class KartDonemSatiri : ObservableObject
{
    public KartDonemSatiri(KartDonemDto d)
    {
        Dto = d;
        DonemMetni = $"{d.Baslangic.ToString("d MMM", Kultur.Turkce)} – {d.Kesim.ToString("d MMM yyyy", Kultur.Turkce)}";
        HesaplananMetni = $"Hesaplanan {Bicim.Tl(d.HesaplananBorc)} ₺";
        (DurumMetni, Ton) = d.Durum switch
        {
            null => ("Mutabakat yok", "notr"),
            KartMutabakatDurumu.Mutabik => ("Mutabık", "olumlu"),
            KartMutabakatDurumu.FarkKabul => ($"Fark kabul · {Bicim.ImzaliTl(d.Fark ?? 0m)}", "bekliyor"),
            _ => ($"Fark {Bicim.ImzaliTl(d.Fark ?? 0m)}", "olumsuz"),
        };
    }

    public KartDonemDto Dto { get; }
    public DateOnly Kesim => Dto.Kesim;
    /// <summary>"1 Ağu – 31 Ağu 2026".</summary>
    public string DonemMetni { get; }
    public string HesaplananMetni { get; }
    public string DurumMetni { get; }
    /// <summary>Rozet tonu: olumlu / bekliyor / olumsuz / notr (çek rozetleriyle aynı).</summary>
    public string Ton { get; }
    [ObservableProperty] private bool _secili;
}

/// <summary>Dönemdeki kartlı işlem; tik ekstrede görüldüğünü gösterir.</summary>
public sealed partial class MutabakatIslemSatiri : ObservableObject
{
    public MutabakatIslemSatiri(KartMutabakatIslemDto d)
    {
        Id = d.Id; Tarih = d.Tarih; Cari = d.Cari; Tutar = d.Tutar; Not = d.Not;
        _tikli = d.Tikli;
    }

    public int Id { get; }
    public DateOnly Tarih { get; }
    public string Cari { get; }
    public decimal Tutar { get; }
    public string? Not { get; }
    public string Ayrinti => string.IsNullOrWhiteSpace(Not) ? Tarih.ToString("d MMM yyyy", Kultur.Turkce)
        : $"{Tarih.ToString("d MMM yyyy", Kultur.Turkce)} · {Not}";
    [ObservableProperty] private bool _tikli;
}
