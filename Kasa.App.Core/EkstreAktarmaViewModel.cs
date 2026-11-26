using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

public partial class EkstreAktarmaViewModel(IEkstreAktarmaApi api, IKasaApi finans, IFinansTakipApi takip, AuthViewModel auth) : OturumluViewModel(auth)
{
    private readonly TekrarAnahtari _kayitKey = new(), _iptalKey = new();
    private int _formSurumu;
    private EkstreKaydetYaz? _onizlemeGirdi;
    private EkstreOnizlemeDto? _onizleme;
    private int _onizlemeForm;
    private int? _gecmisImleci;
    public IReadOnlyList<EkstreSecenek> Kaynaklar { get; } = [new(EkstreKaynaklari.Kart, "Kredi kartı ekstresi"), new(EkstreKaynaklari.Banka, "Banka hesap hareketleri"), new(EkstreKaynaklari.Erp, "ERP12 cari hareketleri (Excel)")];
    /// <summary>Yükleme formunun bankaları: sunucudan (GET api/ekstre-aktar/bankalar, tek kaynak) yüklenir; istemcide yedek liste
    /// yoktur. Alınamazsa boş kalır ve <see cref="TemelViewModel"/> hatası nedenini söyler (PDF yüklenemez).</summary>
    public ObservableCollection<EkstreSecenek> Bankalar { get; } = new();
    public ObservableCollection<KartTakipDto> Kartlar { get; } = new();
    public ObservableCollection<KanalDto> Kanallar { get; } = new();
    public ObservableCollection<EkstreSatirEditor> Satirlar { get; } = new();
    public ObservableCollection<EkstreGecmisSatiri> Gecmis { get; } = new();
    public ObservableCollection<EkstreKayitSatiri> Kayitlar { get; } = new();
    [ObservableProperty] private EkstreSecenek? _kaynak;
    [ObservableProperty] private EkstreSecenek? _banka;
    [ObservableProperty] private string _hesapAdi = "";
    [ObservableProperty] private KartTakipDto? _kart;
    [ObservableProperty] private EkstreBelgeDto? _belge;
    [ObservableProperty] private EkstreSatirEditor? _seciliSatir;
    [ObservableProperty] private string _onizlemeMetni = "";
    [ObservableProperty] private bool _onay;
    [ObservableProperty] private bool _tekrarOnay;
    /// <summary>Kaydetme yapılamadıysa nedeni (kaydet düğmesinin altında).</summary>
    [ObservableProperty] private string? _kayitHatasi;
    [ObservableProperty] private bool _eskiBelgeVar;
    public bool BankaMi => Kaynak?.Kod == EkstreKaynaklari.Banka;
    public bool KartMi => Kaynak?.Kod == EkstreKaynaklari.Kart;
    /// <summary>ERP12 cari hareketleri: banka, hesap ve kart seçilmez.</summary>
    public bool ErpMi => Kaynak?.Kod == EkstreKaynaklari.Erp;
    public bool BankaSecilir => !ErpMi;
    /// <summary>Açık belge hiçbir satırı işlenmemiş ERP12 belgesi: silinebilir.</summary>
    public bool ErpSilinebilir => Belge is { Kaynak: EkstreKaynaklari.Erp, Kayitlar.Count: 0 };
    public bool BelgeVar => Belge is not null;
    public bool OnizlemeVar => _onizleme is not null;
    public bool TekrarOnayGerekli => _onizleme?.TekrarOnayGerekli ?? false;
    public string BelgeOzeti => Belge is { } b ? $"{b.DosyaAdi} · {EkstreGecmisSatiri.Kimlik(b.Banka, b.Kaynak, b.KartId is { } id ? (Kartlar.FirstOrDefault(k => k.Id == id)?.Ad ?? "Kart") + $" (#{id})" : b.HesapAdi)}\n{b.Satirlar.Count} satır; {Satirlar.Count(s => s.Secili)} seçildi. Hiçbir satır kendiliğinden kaydedilmez.\n" + string.Join("\n", b.Uyarilar) : "";
    /// <summary>Belge incelemeye açıldı (geçmişten, yüklemeyle ya da kaynağa gitmeyle): sayfa hareket penceresini açar. Yenileme istemez.</summary>
    public event EventHandler? PencereAcIstendi;
    /// <summary>Hareket penceresinin başlığı (dosya adı) ve tek satırlık alt bilgisi; belge uyarıları ⋯ menüsündedir.</summary>
    public string PencereBasligi => Belge?.DosyaAdi ?? "";
    public string PencereAltBilgisi => Belge is { } b ? $"{BelgeKimligi(b)} · {b.Satirlar.Count} hareket" : "";
    public string BelgeUyarilariBasligi => $"Belge uyarıları ({Belge?.Uyarilar.Count ?? 0})";
    public string BelgeUyarilariMetni => string.Join("\n", Belge?.Uyarilar ?? []);
    private string BelgeKimligi(EkstreBelgeDto b) => EkstreGecmisSatiri.Kimlik(b.Banka, b.Kaynak,
        b.KartId is { } id ? (Kartlar.FirstOrDefault(k => k.Id == id)?.Ad ?? "Kart") + $" (#{id})" : b.HesapAdi);
    partial void OnKaynakChanged(EkstreSecenek? value)
    {
        foreach (var ad in new[] { nameof(BankaMi), nameof(KartMi), nameof(ErpMi), nameof(BankaSecilir) })
            OnPropertyChanged(ad);
    }
    partial void OnBelgeChanged(EkstreBelgeDto? value)
    {
        OnPropertyChanged(nameof(BelgeVar));
        OnPropertyChanged(nameof(BelgeOzeti));
        OnPropertyChanged(nameof(YokSayDestekleniyor));
        OnPropertyChanged(nameof(YenidenOkunabilir));
        OnPropertyChanged(nameof(ErpSilinebilir));
        OnPropertyChanged(nameof(Atlananlar));
        OnPropertyChanged(nameof(AtlananVar));
        OnPropertyChanged(nameof(AtlananDugmesi));
        OnPropertyChanged(nameof(AtlananMetni));
    }
    private void GirdiDegisti()
    {
        _formSurumu++;
        _onizleme = null;
        _onizlemeGirdi = null;
        OnizlemeMetni = "";
        KayitHatasi = null;
        Onay = false;
        TekrarOnay = false;
        OnPropertyChanged(nameof(OnizlemeVar));
        OnPropertyChanged(nameof(TekrarOnayGerekli));
        OnPropertyChanged(nameof(BelgeOzeti));
        OnPropertyChanged(nameof(YonOzeti));
        OnPropertyChanged(nameof(SecimOzeti));
        HazirlariBildir();
    }
    protected override void OturumTemizle()
    {
        HafizayiTemizle();
        YapayZekayiTemizle();
        GirdiDegisti();
        HatalariTemizle();
        SatirMesaji = null;
        SatirHatasi = null;
        TopluMesaji = null;
        TopluHatasi = null;
        _yokSayilanTaslak.Clear();
        AtlananAcik = false;
        YuklemeHatasi = null;
        Belge = null;
        SeciliSatir = null;
        Kaynak = null;
        Banka = null;
        Kart = null;
        HesapAdi = "";
        Satirlar.Clear();
        Kayitlar.Clear();
        SuzgecleriHesapla();
        SekmeleriDoldur();
        SeciliSekme = EkstreSekmesi.Bekleyen;
        TopluTurler.Clear();
        TopluDagilimlar.Clear();
        TopluKanallar.Clear();
        TopluTur = null;
        TopluDagilim = null;
        _yokSayKey.Temizle();
        _yenidenOkuKey.Temizle();
        Gecmis.Clear();
        Kartlar.Clear();
        Kanallar.Clear();
        Bankalar.Clear();
        Kurallar.Clear();
        KuralDurumu = "";
        KurallarDestekleniyor = false;
        _kayitKey.Temizle();
        _iptalKey.Temizle();
        _gecmisImleci = null;
        EskiBelgeVar = false;
    }
    public Task YukleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu)
            return;
        GirdiDegisti();
        var id = Belge?.Id;
        var (bankalar, bankaHatasi) = await BankalariAlAsync();
        var gecmis = await api.EkstreBelgelerAsync();
        var kartlar = await takip.TakipKartlarAsync();
        var kanallar = await finans.KanallarAsync();
        var belge = id is { } i ? await api.EkstreBelgeAsync(i) : null;
        if (!Gecerli(n) || !EditorMu)
            return;
        TakipMetni.Doldur(Gecmis, gecmis.Select(x => new EkstreGecmisSatiri(x)));
        _gecmisImleci = gecmis.Count > 0 ? gecmis.Min(g => g.Id) : null;
        EskiBelgeVar = gecmis.Count == 50;
        TakipMetni.Doldur(Kartlar, kartlar.Where(k => k.YeniTakip));
        TakipMetni.Doldur(Kanallar, kanallar.Where(k => k.Aktif).OrderBy(k => k.Sira));
        var seciliBanka = Banka?.Kod;
        TakipMetni.Doldur(Bankalar, bankalar.Select(b => new EkstreSecenek(b.Kod, b.Ad)));
        Banka = Bankalar.FirstOrDefault(b => b.Kod == seciliBanka);
        // Yenileme (ve bağlantı dönünce kendiliğinden yenileme) doldurulan satırları ve işaretleri silmez.
        if (belge is not null)
            BelgeyiYansit(belge, koru: true);
        await KurallariAlAsync(n);
        await YapayZekaDurumunuAlAsync(n);
        if (!Gecerli(n))
            return;
        Tamamlandi();
        if (bankaHatasi is not null)
            Hata = bankaHatasi;
    });
    /// <summary>Bankalar sunucudan; alınamazsa (eski sunucuda uç yok, sunucu hatası) boş liste ve anlaşılır ileti döner, geçmiş ve
    /// açık belge yine yüklenir. Oturum sonu ve yetki reddi bütün yüklemeyi durdurur.</summary>
    private async Task<(IReadOnlyList<EkstreBankaDto> Bankalar, string? Hata)> BankalariAlAsync()
    {
        try
        { return (await api.EkstreBankalarAsync(), null); }
        catch (KasaApiException e) when (e.DurumKodu == HttpStatusCode.NotFound)
        { return ([], "Banka listesi alınamadı: sunucu banka listesini vermiyor (sunucu bu uygulama sürümünden eski olabilir). Belge yüklemek için sunucu güncellenmeli."); }
        catch (KasaApiException e) when (e.DurumKodu is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
        { return ([], "Banka listesi alınamadı; belge yüklenemez. " + OkumaHataMesaji(e)); }
    }
    public EkstreYuklemeSecimi? YuklemeSecimi()
    {
        if (!EditorMu || !VeriHazir || Mesgul)
            return null;
        if (ErpMi)
            return new(OturumNesli, EkstreKaynaklari.Erp, "", "", null);
        if (Bankalar.Count == 0)
        { Hata = "Banka listesi sunucudan alınamadı; belge yüklenemez. Ekranı yenileyip yeniden deneyin."; return null; }
        if (Kaynak is null || Banka is null || (BankaMi && (string.IsNullOrWhiteSpace(HesapAdi) || HesapAdi.Trim().Length > 100)) || (KartMi && Kart is null))
        { Hata = "Belge türünü, bankayı ve kartı / kısa hesap adını seçin."; return null; }
        return new(OturumNesli, Kaynak.Kod, Banka.Kod, BankaMi ? HesapAdi.Trim() : "", KartMi ? Kart!.Id : null);
    }
    /// <summary>PDF ya da Excel seçimi (maui-8; önceden EkstreAktarmaPage.PdfSecAsync'teydi): en çok 10 MB; seçim ya da okuma
    /// sürerken oturum değişirse dosya yüklenmez ve eski oturumun hatası yazılmaz. Belge kaynağının değişmediğini
    /// <see cref="PdfYukleAsync"/> denetler.</summary>
    /// <param name="sec">Dosya seçiciyi açar; vazgeçilirse null.</param>
    public async Task PdfSecVeYukleAsync(EkstreYuklemeSecimi secim, Func<Task<SecilenDosya?>> sec)
    {
        bool OturumSuruyor() => OturumNesli == secim.Oturum;
        try
        {
            var dosya = await sec();
            if (dosya is null || !OturumSuruyor())
                return;
            if (!DosyaSecimKurallari.EkstreMi(dosya.Ad))
            { Hata = "Yalnız PDF ya da Excel (.xlsx, .xls) dosyası seçin."; return; }
            DosyaOkumasi okuma;
            await using (var akis = await dosya.Ac())
                okuma = await DosyaSecimKurallari.SinirliOkuAsync(akis, DosyaSecimKurallari.EnFazlaBayt, OturumSuruyor);
            if (okuma.Durum == DosyaOkumaDurumu.Vazgecildi)
                return;
            if (okuma.Durum == DosyaOkumaDurumu.SinirAsildi)
            { Hata = "Dosya en fazla 10 MB olabilir."; return; }
            await PdfYukleAsync(okuma.Icerik!, dosya.Ad, secim);
        }
        catch (Exception) { if (OturumSuruyor()) Hata = "Dosya okunamadı. Kontrol edip yeniden seçin."; }
    }
    /// <summary>Yüklenen dosyanın okunamama nedeni (hatalı Excel'de satır satır liste); yükleme kartında görünür.</summary>
    [ObservableProperty] private string? _yuklemeHatasi;
    /// <summary>Örnek Excel'i <paramref name="hedef"/>'e yazar; hata ya da eski oturumda null.</summary>
    public async Task<IndirmeBilgisi?> SablonAsync(Stream hedef)
    {
        IndirmeBilgisi? sonuc = null;
        await YurutAsync(async n => { if (!EditorMu) return; var s = await api.EkstreSablonAsync(hedef); if (Gecerli(n)) sonuc = s; });
        return sonuc;
    }
    public Task PdfYukleAsync(byte[] icerik, string ad, EkstreYuklemeSecimi secim) => YurutAsync(async n =>
    {
        YuklemeHatasi = null;
        if (!EditorMu || !VeriHazir || secim.Oturum != n || secim.Kaynak != Kaynak?.Kod || secim.Banka != (ErpMi ? "" : Banka?.Kod) || secim.HesapAdi != (BankaMi ? HesapAdi.Trim() : "") || secim.KartId != (KartMi ? Kart?.Id : null))
        { Hata = "Dosya seçimi sırasında oturum veya belge kaynağı değişti. Dosyayı yeniden seçin."; return; }
        if (!DosyaSecimKurallari.EkstreMi(ad) || icerik.Length == 0 || icerik.Length > DosyaSecimKurallari.EnFazlaBayt)
        { Hata = "En fazla 10 MB büyüklüğünde bir PDF ya da Excel seçin."; return; }
        var b = await api.EkstreYukleAsync(icerik, ad, secim.Kaynak, secim.Banka, secim.HesapAdi, secim.KartId);
        if (!Gecerli(n))
            return;
        BelgeyiYansit(b);
        Tamamlandi();
        PencereAcIstendi?.Invoke(this, EventArgs.Empty);
        await KurallariAlAsync(n);
        await YapayZekaDoldurAsync(n);
        if (!Gecerli(n))
            return;
        Mesaj = HazirSayisi > 0 ? $"Belge okundu. {HazirSayisi} satır hazır ve işaretli; {KontrolSayisi} satırı kontrol edin." : "Belge okundu. Kaydetmek istediğiniz satırları tek tek seçip kontrol edin.";
    }, hataIsle: e => YuklemeHatasi = e is HttpRequestException ? Yurutucu.KayitBaglantiIletisi : HataMesaji(e));
    /// <summary>Yanlış yüklenen, işlenmemiş ERP12 belgesini siler (sayfa önce onay ister). Kasa değişmez.</summary>
    public Task ErpBelgeSilAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || !VeriHazir || !ErpSilinebilir || Belge is not { } belge)
            return;
        await api.ErpBelgeSilAsync(belge.Id);
        if (!Gecerli(n))
            return;
        if (Gecmis.FirstOrDefault(g => g.Veri.Id == belge.Id) is { } satir)
            Gecmis.Remove(satir);
        SeciliSatir = null;
        Belge = null;
        Satirlar.Clear();
        Kayitlar.Clear();
        SuzgecleriHesapla();
        SekmeleriDoldur();
        GirdiDegisti();
        Mesaj = "ERP12 belgesi ve hareketleri silindi; kasa değişmedi.";
    }, mesgulkenBildir: true);
    public Task BelgeAcAsync(int id) => YurutAsync(async n =>
    {
        if (!EditorMu || !VeriHazir)
            return;
        GirdiDegisti();
        var b = await api.EkstreBelgeAsync(id);
        if (!Gecerli(n))
            return;
        BelgeyiYansit(b, koru: true);
        PencereAcIstendi?.Invoke(this, EventArgs.Empty);
        await KurallariAlAsync(n);
        await YapayZekaDoldurAsync(n);
    });
    public Task KaynakAcAsync(int kayitId) => YurutAsync(async n =>
    {
        if (!EditorMu || !VeriHazir)
            return;
        GirdiDegisti();
        var b = await api.EkstreKaynakBelgeAsync(kayitId);
        if (!Gecerli(n))
            return;
        BelgeyiYansit(b, koru: true);
        // Aranan kayıt (ya da iptali) Kapanan sekmesindedir.
        SeciliSekme = EkstreSekmesi.Kapanan;
        PencereAcIstendi?.Invoke(this, EventArgs.Empty);
        await KurallariAlAsync(n);
        await YapayZekaDoldurAsync(n);
        if (Gecerli(n) && Kapananlar.FirstOrDefault(s => s.Kayit?.Veri.Id == kayitId) is { } satir)
            SatirGosterIstendi?.Invoke(this, satir);
    });
    [RelayCommand]
    private Task EskiBelgeleriYukleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || !VeriHazir || !EskiBelgeVar || _gecmisImleci is not { } id)
            return;
        var eski = await api.EkstreBelgelerAsync(id);
        if (!Gecerli(n))
            return;
        foreach (var b in eski)
            if (!Gecmis.Any(g => g.Veri.Id == b.Id))
                Gecmis.Add(new(b));
        _gecmisImleci = eski.Count > 0 ? eski.Min(g => g.Id) : null;
        EskiBelgeVar = eski.Count == 50;
    });
    private void BelgeyiYansit(EkstreBelgeDto b, bool koru = false, IReadOnlySet<int>? yenile = null)
    {
        var ayniBelge = Belge?.Id == b.Id;
        GirdiDegisti();
        HatalariTemizle();
        SatirMesaji = null;
        SatirHatasi = null;
        TopluMesaji = null;
        TopluHatasi = null;
        TakipMetni.Doldur(Kayitlar, b.Kayitlar.Select(k => new EkstreKayitSatiri(k)));
        var satirlar = SatirlariKur(b, koru, yenile);
        // Açık düzenleyici aynı bekleyen satırda kalır (model yenilendiyse yenisinde).
        if (SeciliSatir is { } acik && !satirlar.Contains(acik))
            SeciliSatir = koru && ayniBelge && !acik.Kapali ? satirlar.FirstOrDefault(s => s.Kaynak.No == acik.Kaynak.No && !s.Kapali) : null;
        Belge = b;
        TakipMetni.Doldur(Satirlar, satirlar);
        SekmeleriDoldur();
        if (!ayniBelge || TopluTurler.Count == 0)
            TopluSecenekleriKur(b);
        else
            TopluKanallariTazele();
        if (!ayniBelge)
        {
            SeciliSekme = EkstreSekmesi.Bekleyen;
            AtlananAcik = false;
            YapayZekaBelgeDurumunuTemizle();
        }
        var eski = Gecmis.FirstOrDefault(g => g.Veri.Id == b.Id);
        if (eski is not null)
            Gecmis.Remove(eski);
        Gecmis.Insert(0, new(new(b.Id, b.Surum, b.Kaynak, b.Banka, b.HesapAdi, b.KartId, b.DosyaAdi, b.Yuklendi, b.Satirlar.Count, b.Kayitlar.Count(k => !k.Iptal), b.YokSayilanlar?.Count ?? 0)));
        OnPropertyChanged(nameof(BelgeOzeti));
        foreach (var ad in new[] { nameof(PencereBasligi), nameof(PencereAltBilgisi), nameof(BelgeUyarilariBasligi), nameof(BelgeUyarilariMetni) })
            OnPropertyChanged(ad);
        OnPropertyChanged(nameof(YonOzeti));
        OnPropertyChanged(nameof(SecimOzeti));
        SuzgecleriHesapla();
    }
    /// <summary>Önizleme isteği; eksik varsa hepsini <see cref="OnizlemeHatalari"/>'na yazar ve null döner.</summary>
    private EkstreKaydetYaz? Girdi(EkstreBelgeDto belge)
    {
        var secili = Satirlar.Where(s => s.Secili).ToList();
        if (secili.Count == 0)
        {
            HatalariYaz([new(null, SeciliSatir is { Secilebilir: true } acik
                ? $"Satır {acik.Kaynak.No} açık ama işaretli değil. Satırı açmak seçmek değildir; etkisini görmek için satırın başındaki kutuyu işaretleyin."
                : "Kaydedilecek en az bir satırı seçin: satırın başındaki kutuyu işaretleyin.")]);
            return null;
        }
        var satirlar = secili.Select(s => (s.Kaynak.No, Sonuc: s.Hazirla())).ToList();
        var eksikler = satirlar.SelectMany(s => s.Sonuc.Hatalar.Select(h => new EkstreSatirHatasi(s.No, h))).ToList();
        if (eksikler.Count > 0)
        { HatalariYaz(eksikler); return null; }
        var g = new EkstreKaydetYaz(Guid.Empty, belge.Surum, satirlar.Select(s => s.Sonuc.Yaz!).ToArray());
        return g with { IstekId = _kayitKey.Al(new { belge.Id, g }) };
    }
    [RelayCommand]
    private Task OnizleAsync() => YurutAsync(async n =>
    {
        HatalariTemizle();
        if (!EditorMu)
            return;
        if (!VeriHazir || Belge is not { } belge)
        { HatalariYaz([new(null, "Veriler henüz yüklenmedi. Yenileyip yeniden deneyin.")]); return; }
        var id = belge.Id;
        var rev = _formSurumu;
        if (Girdi(belge) is not { } g)
            return;
        var p = await api.EkstreOnizlemeAsync(id, g);
        if (!Gecerli(n) || Belge?.Id != id)
            return;
        if (rev != _formSurumu)
        { HatalariYaz([new(null, "Etki hesaplanırken satırlar değişti. \"Seçilen satırların etkisini göster\"e yeniden basın.")]); return; }
        _onizleme = p;
        _onizlemeGirdi = g;
        _onizlemeForm = rev;
        Onay = false;
        TekrarOnay = false;
        OnizlemeMetni = $"{p.Satirlar.Count} satır kaydedilecek. Genel kasa etkisi: {Bicim.Tl(p.KasaEtkisi)} ₺\n" + string.Join("\n", p.Uyarilar) + "\n\n" + string.Join("\n\n", p.Satirlar.Select(s => $"Satır {s.SatirNo} · {s.Tarih:dd.MM.yyyy} · {s.Aciklama} · {Bicim.Tl(s.Tutar)} ₺ · {TurAdi(s.IslemTuru)}\nKasa etkisi: {Bicim.Tl(s.KasaEtkisi)} ₺ · {Dagilim(g.Satirlar.Single(x => x.SatirNo == s.SatirNo), s)}\n{string.Join("\n", s.Uyarilar)}"));
        OnPropertyChanged(nameof(OnizlemeVar));
        OnPropertyChanged(nameof(TekrarOnayGerekli));
        SonucuGoster(EkstreSonucu.Onizleme);
    }, hataIsle: OnizlemeHatasiniYaz);
    private string TurAdi(string kod) => Satirlar.SelectMany(s => s.IslemTurleri).FirstOrDefault(t => t.Kod == kod)?.Ad ?? kod;
    private static string Dagilim(EkstreSatirYaz girdi, EkstreSatirOnizleme satir) => girdi.IslemTuru == EkstreIslemTurleri.Eslestir
        ? $"Mevcut kayıtla eşleşir ({EslesmeAdayiSatiri.TurAdi(girdi.EslesenKayitTuru)} #{girdi.EslesenKayitId}); kasa ve kart borcu değişmez"
        : girdi.DagilimTuru == DagilimBicimleri.Genel ? "Yalnız genel kasa" : TakipMetni.Paylar(satir.Dagilimlar);
    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(async n =>
    {
        KayitHatasi = null;
        if (!EditorMu || !VeriHazir || Belge is null || _onizleme is null || _onizlemeGirdi is null || _formSurumu != _onizlemeForm || !Onay || (TekrarOnayGerekli && !TekrarOnay))
        { KayitHatasi = "Önizlemeyi kontrol edin ve gerekli onayları işaretleyin."; return; }
        var id = Belge.Id;
        var g = _onizlemeGirdi with { OnizlemeOzeti = _onizleme.OnizlemeOzeti, TekrarOnay = TekrarOnay };
        try
        {
            var b = await api.EkstreKaydetAsync(id, g);
            if (!Gecerli(n))
                return;
            BelgeyiYansit(b, koru: true, yenile: g.Satirlar.Select(s => s.SatirNo).ToHashSet());
            _kayitKey.Temizle();
            Mesaj = "Seçilen satırlar kaydedildi. Diğer satırlar değişmedi.";
            SatirMesaji = $"{g.Satirlar.Count} satır kaydedildi ve Kapanan süzgecine geçti.";
            await KurallariAlAsync(n);
            if (Gecerli(n))
                SonucuGoster(EkstreSonucu.Satirlar);
        }
        catch (KasaApiException e) when (e.DurumKodu == HttpStatusCode.Conflict)
        {
            // Önizleme geçersizdir (onay kartı kapanır): nedeni "etkisini göster" düğmesinin altında kalır.
            if (Gecerli(n))
            {
                GirdiDegisti();
                OnizlemeHatasiniYaz(e);
            }
        }
    }, hataIsle: e => KayitHatasi = e is HttpRequestException ? Yurutucu.KayitBaglantiIletisi : HataMesaji(e));
    /// <summary>Seçili satırın (tarih ve tutarıyla) eşleşebileceği mevcut kayıtları getirir; 'Mevcut kayıtla eşleştir' türünde
    /// satır bunlardan birine bağlanır.</summary>
    [RelayCommand]
    private Task EslesmeAdaylariniGetirAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || !VeriHazir || Belge is null || SeciliSatir is not { EslesmeMi: true } satir)
            return;
        satir.FormMesaji = null;
        if (!satir.AdaySorgusu(out var sorgu, out var hata))
        { Hata = hata; satir.FormMesaji = hata; return; }
        var id = Belge.Id;
        var adaylar = await api.EkstreEslesmeAdaylariAsync(id, sorgu);
        if (!Gecerli(n) || Belge?.Id != id || !Satirlar.Contains(satir) || !satir.EslesmeMi)
            return;
        satir.AdaylariYansit(adaylar);
        Mesaj = adaylar.Count == 0 ? "Bu tutarda, en çok 3 gün farklı tarihte eşleştirilebilecek kayıt yok." : $"{adaylar.Count} aday bulundu; bağlanacak kaydı seçin.";
        satir.FormMesaji = Mesaj;
    }, hataIsle: e =>
    {
        Hata = HataMesaji(e);
        if (SeciliSatir is { } satir)
            satir.FormMesaji = Hata;
    });
    public Task KayitIptalAsync(EkstreKayitSatiri satir, string aciklama, int onayOturumu, int belgeId) => YurutAsync(async n =>
    {
        if (!EditorMu || !VeriHazir || onayOturumu != n || Belge?.Id != belgeId || !Kayitlar.Any(k => ReferenceEquals(k, satir)) || satir.Veri.Iptal || string.IsNullOrWhiteSpace(aciklama))
        {
            Hata = "Geçerli kaydı seçip gerekçeyi yeniden onaylayın.";
            SatirMesaji = null;
            SatirHatasi = Hata;
            SonucuGoster(EkstreSonucu.Satirlar);
            return;
        }
        var g = new EkstreIptalYaz(_iptalKey.Al(new { belgeId, satir.Veri.Id, Aciklama = aciklama.Trim() }), aciklama.Trim());
        var b = await api.EkstreKayitIptalAsync(belgeId, satir.Veri.Id, g);
        if (!Gecerli(n))
            return;
        BelgeyiYansit(b, koru: true);
        _iptalKey.Temizle();
        Mesaj = "Kaydın iptali işlendi; kaynak ve geçmiş korundu.";
        SatirMesaji = $"Satır {satir.Veri.SatirNo} kaydı iptal edildi; satır yeniden bekliyor.";
        await KurallariAlAsync(n);
    }, hataIsle: e =>
    {
        Hata = HataMesaji(e);
        SatirMesaji = null;
        SatirHatasi = Hata;
        SonucuGoster(EkstreSonucu.Satirlar);
    });
    /// <summary>Kaynak PDF'i <paramref name="hedef"/>'e yazar; hata, başka belge ya da eski oturumda null.</summary>
    public async Task<IndirmeBilgisi?> DosyaAsync(Stream hedef)
    {
        IndirmeBilgisi? sonuc = null;
        await YurutAsync(async n => { if (!EditorMu || Belge is null) return; var id = Belge.Id; var s = await api.EkstreDosyaAsync(id, hedef); if (Gecerli(n) && Belge?.Id == id) sonuc = s; });
        return sonuc;
    }
}
