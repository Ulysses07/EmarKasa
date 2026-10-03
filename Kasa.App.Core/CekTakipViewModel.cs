using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;
using CekKurallari = Kasa.Core.CekKurallari;

namespace Kasa.App.Core;

/// <summary>
/// Çekler ekranı (docs/specs/2026-10-01-cekler.md "Masaüstü ekranı"). Süzgeçler (yön, durum, arama, vade aralığı) sunucuda uygulanır:
/// her değişiklik listeyi yeniden yükler. Liste ve özet okuması son istek kazanır hattıdır (<see cref="SonIstekHatti"/>): süzgeç
/// istek anında yakalanır, süren yükleme yeni süzgeci engellemez, eski süzgecin geç yanıtı yeni seçimi ezmez. Yazmadan sonra özet
/// yenilenir; kaydedilen çek seçili süzgece artık uymuyorsa listeden düşer ve bu söylenir. Açılan çekin ayrıntısı satırının hemen altında görünür (<see cref="OncekiSatirlar"/>, açık
/// çek, <see cref="SonrakiSatirlar"/>). Hareket düğmeleri sunucunun izin verdiği türlerdir (<see cref="CekDto.IzinliHareketler"/>);
/// geçiş kuralı istemcide yinelenmez. Hareket formu tarih bugün, tutar kalan ve son seçilen kasayla açılır. Yeni çekte aynı yön,
/// banka ve numaralı kayıt kaydetmeden önce uyarılır; kullanıcı onaylarsa kaydedilir.
/// </summary>
/// <param name="zaman">Vade rozeti ve hazır süzgeçlerin günü (yerel); verilmezse sistem saati.</param>
public partial class CekTakipViewModel(ICekApi api, IKasaApi finans, AuthViewModel auth, TimeProvider? zaman = null) : OturumluViewModel(auth), IKaydedilmemisForm
{
    private readonly TimeProvider _zaman = zaman ?? TimeProvider.System;
    private readonly TekrarAnahtari _kayit = new();
    // Çek başına: bir çekin yanıtı belirsiz kalan isteğinin anahtarı başka çekte yapılan işlemle ezilmez (KartTakipViewModel gibi).
    private readonly KayitBasinaTekrarAnahtari _hareket = new(), _geriAl = new(), _sil = new();
    private SonIstekHatti? _listeHatti;
    private string? _sonKanal;
    private bool _ayniCekOnaylandi;
    // CekKasaSecenekleri'nin tabanı: aktif kanallar ve "Ortak" (en son liste yüklemesinden). Düzeltme formu, düzenlenen çekin
    // kasası pasif bir kanalsa onu da bu tabana ekler (CekKasaSecenekleriniDoldur); yeni çek formu yalnız bu tabanı kullanır.
    private List<string> _aktifCekKasalari = [];
    // Düzeltme formu açılırken çekin sürümü: kaydetme bunu gönderir (listeden okunmaz); liste yenilenince sürüm değiştiyse form kapanır.
    private int _duzenlenenSurum;

    /// <summary>Çek / senet formunun (yeni ya da düzeltme) hataları (tasarım 2026-10-02 §1; ÇK-01): alan → ileti ve formun genel hatası.
    /// Sunucu çek hatalarını alan adı olmadan ({ hata }) döndürür: sunucu iletisi genel hataya gider.</summary>
    public AlanHatalari Hatalar { get; } = new();
    /// <summary>Açık çekin hareket formunun hataları.</summary>
    public AlanHatalari HareketHatalari { get; } = new();
    protected override IEnumerable<AlanHatalari> Formlar => [Hatalar, HareketHatalari];

    /// <summary>Çek formunun açıldığı andaki değerleri: kaydedilmemiş değişiklik ölçütü (form kapalıyken değişiklik sayılmaz).</summary>
    private KaydedilmemisDegisiklik? _formIzi;
    private KaydedilmemisDegisiklik FormIzi => _formIzi ??= new(() => new
    {
        Yon = FormYon?.Kod,
        Tur = FormTur?.Kod,
        No,
        Banka,
        Kisi,
        Tutar,
        Vade,
        CekKasasi,
        Teminat,
        Konum = Konum?.Kod,
        Not,
    });
    /// <summary>Açık çekin hareket formunun açıldığı andaki değerleri (tarih, tutar, net, kasa, karşı taraf).</summary>
    private KaydedilmemisDegisiklik? _hareketIzi;
    private KaydedilmemisDegisiklik HareketIzi => _hareketIzi ??= new(() => new
    {
        HareketTarihi,
        HareketTutari,
        NetTutar,
        HareketKasasi,
        Karsi,
    });
    public bool KaydedilmemisDegisiklikVar => FormIzi.Var || HareketIzi.Var;

    /// <summary>Kabuktan çıkışta "Bırak": çek formu ve hareket formu kapanır.</summary>
    public void DegisiklikleriBirak()
    {
        FormuKapatOnaysiz();
        HareketFormunuKapat();
    }

    /// <summary>Düzeltme formu açıkken düzenlenen çekin kimliği (listede satırın vurgusu); yoksa null.</summary>
    public int? DuzenlenenCekId => FormAcik ? Duzenlenen : null;

    /// <summary>Kaydet düğmesi: yeni çekte "Kaydet", düzeltmede "Değişikliği kaydet".</summary>
    public string KaydetMetni => Duzenlenen is null ? "Kaydet" : "Değişikliği kaydet";

    /// <summary>Liste ve özet okumasının son istek kazanır hattı (yükleme göstergesi ve hatası ekranın Mesgul ve Hata'sıdır).</summary>
    private SonIstekHatti ListeHatti => _listeHatti ??= new(Yurutucu);

    public ObservableCollection<CekSatiri> Cekler { get; } = new();
    public ObservableCollection<CekSatiri> OncekiSatirlar { get; } = new();
    public ObservableCollection<CekSatiri> SonrakiSatirlar { get; } = new();
    public ObservableCollection<CekHareketSatiri> Hareketler { get; } = new();
    public ObservableCollection<KodCipi> YonCipleri { get; } = Cipler([CekYonleri.Alinan, CekYonleri.Verilen], CekMetni.Yon);
    public ObservableCollection<KodCipi> DurumCipleri { get; } =
        Cipler([CekSuzgecleri.Portfoyde, CekSuzgecleri.Karsiliksiz, CekSuzgecleri.Kapanan, CekSuzgecleri.Hepsi], CekMetni.Suzgec);
    public ObservableCollection<KodCipi> HareketCipleri { get; } = new();
    public ObservableCollection<string> KasaSecenekleri { get; } = new();
    public ObservableCollection<string> CekKasaSecenekleri { get; } = new();
    public IReadOnlyList<KodCipi> KonumSecenekleri { get; } =
        Cipler([CekKonumlari.Elde, CekKonumlari.BankadaTahsilde, CekKonumlari.Teminatta, CekKonumlari.Icrada], k => CekMetni.Konum(k));
    public IReadOnlyList<KodCipi> TurSecenekleri { get; } = Cipler([CekTurleri.Cek, CekTurleri.Senet], CekMetni.Tur);
    public IReadOnlyList<KodCipi> YonSecenekleri { get; } = Cipler([CekYonleri.Alinan, CekYonleri.Verilen], CekMetni.Yon);

    /// <summary>Kodlardan çipler; ilki seçili.</summary>
    private static ObservableCollection<KodCipi> Cipler(string[] kodlar, Func<string, string> ad) =>
        new(kodlar.Select((k, i) => new KodCipi(k, ad(k)) { Secili = i == 0 }));

    [ObservableProperty] private string _yon = CekYonleri.Alinan;
    [ObservableProperty] private string _durum = CekSuzgecleri.Portfoyde;
    [ObservableProperty] private string _ara = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(VadeSuzgeci), nameof(VadeSuzgeciVar))] private DateOnly? _vadeBas;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(VadeSuzgeci), nameof(VadeSuzgeciVar))] private DateOnly? _vadeSon;
    [ObservableProperty] private CekOzetDto? _ozet;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CekAcik), nameof(AcikOzet), nameof(GeriAlinabilir), nameof(KasaGerekli), nameof(HareketFormuAcik))]
    private CekDto? _acik;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HareketFormuAcik), nameof(KasaGerekli), nameof(KarsiGerekli), nameof(NetGerekli), nameof(MasrafMetni))]
    private string? _hareketTuru;
    [ObservableProperty] private DateTime _hareketTarihi;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(MasrafMetni))] private decimal _hareketTutari;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(MasrafMetni))] private decimal _netTutar;
    [ObservableProperty] private string? _hareketKasasi;
    [ObservableProperty] private string _karsi = "";

    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormBasligi), nameof(DuzenlenenCekId))] private bool _formAcik;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormBasligi), nameof(KaydetMetni), nameof(DuzenlenenCekId))] private int? _duzenlenen;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormVerilen))] private KodCipi? _formYon;
    [ObservableProperty] private KodCipi? _formTur;
    [ObservableProperty] private string _no = "";
    [ObservableProperty] private string _banka = "";
    [ObservableProperty] private string _kisi = "";
    [ObservableProperty] private decimal _tutar;
    [ObservableProperty] private DateTime _vade;
    [ObservableProperty] private string? _cekKasasi;
    [ObservableProperty] private bool _teminat;
    [ObservableProperty] private KodCipi? _konum;
    [ObservableProperty] private string _not = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(AyniCekVar))] private string? _ayniCekUyarisi;

    private DateOnly Bugun => DateOnly.FromDateTime(_zaman.GetLocalNow().DateTime);
    public bool CekAcik => Acik is not null;
    public bool HareketFormuAcik => Acik is not null && HareketTuru is not null;
    public bool GeriAlinabilir => Acik is { Hareketler.Count: > 0 };
    public bool KasaGerekli => Acik?.Yon == CekYonleri.Alinan && HareketTuru is CekHareketTurleri.Tahsilat or CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma;
    public bool KarsiGerekli => HareketTuru is CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma;
    public bool NetGerekli => HareketTuru == CekHareketTurleri.Kirdirma;
    public bool FormVerilen => FormYon?.Kod == CekYonleri.Verilen;
    public bool AyniCekVar => AyniCekUyarisi is not null;
    public bool VadeSuzgeciVar => VadeBas is not null || VadeSon is not null;
    /// <summary>Formun başlığı (tasarım §2): "Yeni çek / senet" ya da "Düzenleniyor: 15.10.2026 · Ahmet Yılmaz" (vade · kişi, açıldığı andaki).</summary>
    public string FormBasligi => Duzenlenen is null ? "Yeni çek / senet" : $"Düzenleniyor: {_duzenlenenOzet}";
    private string _duzenlenenOzet = "";
    public string VadeSuzgeci => (VadeBas, VadeSon) switch
    {
        ({ } bas, { } son) => $"Vade {bas:dd.MM.yyyy} – {son:dd.MM.yyyy}",
        (null, { } son) => $"Vade {son:dd.MM.yyyy} ve öncesi",
        ({ } bas, null) => $"Vade {bas:dd.MM.yyyy} ve sonrası",
        _ => "",
    };
    public string MasrafMetni => !NetGerekli || !ParaAyristirici.HepsiGecerli(HareketTutari, NetTutar) ? ""
        : NetTutar > HareketTutari ? "Hesaba geçen tutar çek tutarını aşamaz."
        : $"Masraf: {Bicim.Tl(HareketTutari - NetTutar)} ₺ (karşı tarafa Cari gider). Kasaya {Bicim.Tl(NetTutar)} ₺ girer.";
    public string AcikOzet => Acik is not { } c ? "" : string.Join("\n", new[]
    {
        $"{CekMetni.Yon(c.Yon)} {CekMetni.Tur(c.Tur).ToLowerInvariant()} · {c.Kisi} · {c.Banka ?? "—"} · {c.No}",
        $"Tutar {Bicim.Tl(c.Tutar)} ₺ · kalan {Bicim.Tl(c.Kalan)} ₺ · vade {c.VadeTarihi:dd.MM.yyyy} · {CekMetni.Durum(c.Durum)}",
        c.Kanal is { } kasa ? $"Kasa: {kasa}" : null,
        c.Konum is not null ? $"Konum: {CekMetni.Konum(c.Konum)}" : null,
        c.Teminat ? "Teminat çeki: bildirim ve panel toplamlarına girmez." : null,
        c.Not,
        c.Uyari,
    }.OfType<string>());
    public string PortfoyMetni => CekMetni.PortfoyMetni(Ozet);
    public string Alinan30Metni => CekMetni.Alinan30Metni(Ozet);
    public string Verilen30Metni => CekMetni.Verilen30Metni(Ozet);
    public string GecmisMetni => CekMetni.GecmisMetni(Ozet);
    public string VerilenGecmisMetni => CekMetni.VerilenGecmisMetni(Ozet);

    partial void OnOzetChanged(CekOzetDto? value)
    {
        foreach (var p in new[] { nameof(PortfoyMetni), nameof(Alinan30Metni), nameof(Verilen30Metni), nameof(GecmisMetni), nameof(VerilenGecmisMetni) })
            OnPropertyChanged(p);
    }

    /// <summary>Başka çek açılınca (ya da açık çek başka bir işlemle değişince: sürüm) hareket formu kapanır; aynı çekin aynı
    /// sürümünün yenilenmesi (liste yenilemesi) açık hareket formunu ve yazılanları korur.</summary>
    partial void OnAcikChanged(CekDto? oldValue, CekDto? newValue)
    {
        var ayniSurum = oldValue is not null && newValue is not null && oldValue.Id == newValue.Id && oldValue.Surum == newValue.Surum
            && HareketTuru is { } tur && newValue.IzinliHareketler.Contains(tur);
        TakipMetni.Doldur(Hareketler, newValue?.Hareketler.Select(h => new CekHareketSatiri(h)) ?? []);
        TakipMetni.Doldur(HareketCipleri, newValue?.IzinliHareketler.Select(t => new KodCipi(t, CekMetni.Hareket(t)) { Secili = ayniSurum && t == HareketTuru }) ?? []);
        if (!ayniSurum)
            HareketFormunuKapat();
        SatirlariBol();
    }

    /// <summary>Hareket formu kapanır: tür, hataları ve kaydedilmemiş değişiklik tabanı kalkar.</summary>
    private void HareketFormunuKapat()
    {
        HareketHatalari.Temizle();
        HareketTuru = null;
        HareketIzi.Kapat();
        foreach (var h in HareketCipleri)
            h.Secili = false;
    }

    partial void OnNoChanged(string value) => AyniCekSifirla();
    partial void OnBankaChanged(string value) => AyniCekSifirla();
    partial void OnFormYonChanged(KodCipi? value) => AyniCekSifirla();

    private void AyniCekSifirla()
    {
        _ayniCekOnaylandi = false;
        AyniCekUyarisi = null;
    }

    /// <summary>Liste, özet ve kasa seçenekleri; süzgeç istek anında yakalanır. Son istek kazanır: süren yükleme yeni süzgeci
    /// engellemez, yalnız en son isteğin sonucu uygulanır.</summary>
    public Task YukleAsync()
    {
        var (yon, durum, ara, bas, son) = (Yon, Durum, string.IsNullOrWhiteSpace(Ara) ? null : Ara.Trim(), VadeBas, VadeSon);
        return ListeHatti.YukleAsync(async _ =>
        {
            var kanallar = await finans.KanallarAsync();
            var cekler = await api.CeklerAsync(yon, durum, ara, bas, son);
            var ozet = await api.CekOzetAsync();
            return (kanallar, cekler, ozet);
        }, v => Yansit(v.kanallar, v.cekler, v.ozet));
    }

    /// <summary>Bildirimden gelen çek (//cekler?CekId=…): çek okunur, yönüne ve "Hepsi" durumuna geçilir (süzgeç onu gizlemesin),
    /// liste yüklenir. Seçim <see cref="IdIleSec"/> ile yapılır. Liste yüklemesiyle aynı hattadır: süren yükleme bunu engellemez,
    /// sonradan seçilen süzgeç bunu ezer. Önceki süzgecin listesi bu çeke ait değildir: <see cref="OturumluViewModel.VeriHazir"/>
    /// başta iner, yalnız bu ya da sonraki güncel yükleme uygulanınca kalkar; böylece biten çağrıdan sonra VeriHazir ekrandaki
    /// listenin seçili süzgece ait olduğunu söyler.</summary>
    public Task CekIcinYukleAsync(int id)
    {
        VeriHazir = false;
        return ListeHatti.YukleAsync(async _ =>
        {
            var cek = await api.CekAsync(id);
            var kanallar = await finans.KanallarAsync();
            var cekler = await api.CeklerAsync(cek.Yon, CekSuzgecleri.Hepsi, null, null, null);
            var ozet = await api.CekOzetAsync();
            return (cek.Yon, kanallar, cekler, ozet);
        }, v =>
        {
            SuzgecleriYaz(v.Yon, CekSuzgecleri.Hepsi, null, null);
            Ara = "";
            Yansit(v.kanallar, v.cekler, v.ozet);
        });
    }

    private void Yansit(IReadOnlyList<KanalDto> kanallar, IReadOnlyList<CekDto> cekler, CekOzetDto ozet)
    {
        TakipMetni.Doldur(KasaSecenekleri, kanallar.Where(k => k.Aktif).Select(k => k.Ad));
        _aktifCekKasalari = [.. kanallar.Where(k => k.Aktif).Select(k => k.Ad).Prepend(KanalEtiketleri.Ortak)];
        // Düzenleme formu açıkken liste yeniden yüklenirse (ör. yazmadan sonra) düzenlenen çekin pasif kasası seçeneklerden düşmesin.
        CekKasaSecenekleriniDoldur(Duzenlenen is { } dn ? cekler.FirstOrDefault(c => c.Id == dn)?.Kanal : null);
        TakipMetni.Doldur(Cekler, cekler.Select(c => new CekSatiri(c, Bugun)));
        Ozet = ozet;
        var acik = Acik is { } eski ? cekler.FirstOrDefault(c => c.Id == eski.Id) : null;
        if (HareketFormuAcik && acik is not null && acik.Surum != Acik!.Surum)
            Mesaj = "Çek başka bir işlemle değişti; hareketi yeniden girin.";
        Acik = acik;
        SatirlariBol();
        // Düzeltilen çek arada başka bir işlemle değiştiyse form eski veriyle yeni sürümü ezmesin: kapanır, yeniden açılması istenir.
        if (FormAcik && Duzenlenen is { } d && cekler.FirstOrDefault(c => c.Id == d) is { } guncel && guncel.Surum != _duzenlenenSurum)
        {
            FormuKapatOnaysiz();
            Mesaj = "Çek başka bir işlemle değişti; formu yeniden açın.";
        }
        Tamamlandi();
    }

    private void SatirlariBol()
    {
        var i = Acik is null ? -1 : Cekler.ToList().FindIndex(s => s.Veri.Id == Acik.Id);
        TakipMetni.Doldur(OncekiSatirlar, i < 0 ? Cekler : Cekler.Take(i + 1));
        TakipMetni.Doldur(SonrakiSatirlar, i < 0 ? [] : Cekler.Skip(i + 1));
    }

    /// <summary>CekKasaSecenekleri'ni aktif kanallar tabanıyla doldurur; <paramref name="ekKasa"/> verilip tabanda yoksa (düzenlenen
    /// verilen çekin kasası pasife alınmışsa) sona eklenir ki form o kasayı kaybetmesin.</summary>
    private void CekKasaSecenekleriniDoldur(string? ekKasa)
    {
        var liste = _aktifCekKasalari;
        if (ekKasa is { Length: > 0 } k && !liste.Contains(k))
            liste = [.. liste, k];
        TakipMetni.Doldur(CekKasaSecenekleri, liste);
    }

    private void SuzgecleriYaz(string yon, string durum, DateOnly? bas, DateOnly? son)
    {
        Yon = yon;
        Durum = durum;
        VadeBas = bas;
        VadeSon = son;
        foreach (var c in YonCipleri)
            c.Secili = c.Kod == yon;
        foreach (var c in DurumCipleri)
            c.Secili = c.Kod == durum;
    }

    /// <summary>Kimliği verilen çeki açar (bildirim tıklaması); listede yoksa sayfa hatası yazılır. Alıcı rolü çeke geçemez.</summary>
    public bool IdIleSec(int id)
    {
        if (Auth.AktifRol == Rol.Alici)
            return false;
        var satir = Cekler.FirstOrDefault(s => s.Veri.Id == id);
        if (satir is null)
        {
            Hata = "Çek bulunamadı. Listeyi yenileyip tekrar deneyin.";
            return false;
        }
        // Satır tıklaması gibi aç-kapa değil: çek zaten açıksa açık kalır.
        Acik = satir.Veri;
        return true;
    }

    /// <summary>Üst şerit ya da panel kutusu: süzgeci yazar ve listeyi yükler.</summary>
    public Task HazirSuzgecAsync(CekHazirSuzgec suzgec)
    {
        var bugun = Bugun;
        switch (suzgec)
        {
            case CekHazirSuzgec.Portfoy:
                SuzgecleriYaz(CekYonleri.Alinan, CekSuzgecleri.Portfoyde, null, null);
                break;
            case CekHazirSuzgec.Alinan30:
                SuzgecleriYaz(CekYonleri.Alinan, CekSuzgecleri.Portfoyde, bugun, bugun.AddDays(30));
                break;
            case CekHazirSuzgec.Verilen30:
                SuzgecleriYaz(CekYonleri.Verilen, CekSuzgecleri.Portfoyde, bugun, bugun.AddDays(30));
                break;
            case CekHazirSuzgec.VerilenVadesiGecmis:
                SuzgecleriYaz(CekYonleri.Verilen, CekSuzgecleri.Portfoyde, null, bugun.AddDays(-1));
                break;
            case CekHazirSuzgec.VadesiGecmis:
                SuzgecleriYaz(CekYonleri.Alinan, CekSuzgecleri.Portfoyde, null, bugun.AddDays(-1));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(suzgec), suzgec, null);
        }
        Ara = "";
        return YukleAsync();
    }

    [RelayCommand]
    private Task SecYonAsync(KodCipi cip)
    {
        SuzgecleriYaz(cip.Kod, Durum, VadeBas, VadeSon);
        return YukleAsync();
    }

    [RelayCommand]
    private Task SecDurumAsync(KodCipi cip)
    {
        SuzgecleriYaz(Yon, cip.Kod, VadeBas, VadeSon);
        return YukleAsync();
    }

    [RelayCommand] private Task AraAsync() => YukleAsync();

    [RelayCommand]
    private Task VadeSuzgeciniKaldirAsync()
    {
        SuzgecleriYaz(Yon, Durum, null, null);
        return YukleAsync();
    }

    /// <summary>Satıra tıklama: kapalıysa açar, açıksa kapatır. Hareket formunda yazılmış değişiklik varsa önce onay sorulur
    /// (tasarım 2026-10-02 §2); çek formu satırdan bağımsızdır, açık kalır.</summary>
    [RelayCommand]
    private async Task SecAsync(CekSatiri satir)
    {
        if (!await BirakilabilirAsync(HareketIzi))
            return;
        Acik = Acik?.Id == satir.Veri.Id ? null : satir.Veri;
    }

    /// <summary>Hareket türü düğmesi: formu tarih bugün, tutar kalan (dönüşte ciro/kırdırma tutarı; karşılıksız ve iadede 0) ve son
    /// seçilen kasayla açar.</summary>
    [RelayCommand]
    private void SecHareket(KodCipi cip)
    {
        if (Acik is not { } c)
            return;
        HareketHatalari.Temizle();
        HareketTuru = cip.Kod;
        foreach (var h in HareketCipleri)
            h.Secili = h.Kod == cip.Kod;
        HareketTarihi = Bugun.ToDateTime(TimeOnly.MinValue);
        HareketTutari = cip.Kod switch
        {
            CekHareketTurleri.Karsiliksiz or CekHareketTurleri.Iade => 0m,
            CekHareketTurleri.Donus => c.Hareketler.LastOrDefault(h => h.Tur is CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma)?.Tutar ?? c.Tutar,
            _ => c.Kalan,
        };
        NetTutar = cip.Kod == CekHareketTurleri.Kirdirma ? c.Kalan : 0m;
        HareketKasasi = _sonKanal is { } kanal && KasaSecenekleri.Contains(kanal) ? kanal : KasaSecenekleri.FirstOrDefault();
        Karsi = "";
        HareketIzi.Ac();
    }

    [RelayCommand]
    private Task HareketKaydetAsync() => FormIsleAsync(HareketHatalari, async n =>
    {
        if (!EditorMu || Acik is not { } c || HareketTuru is not { } tur)
            return;
        var h = HareketHatalari;
        h.Denetle(ParaAyristirici.GecerliMi(HareketTutari), nameof(HareketTutari), ParaAyristirici.GecersizMesaji);
        h.Denetle(!NetGerekli || ParaAyristirici.GecerliMi(NetTutar), nameof(NetTutar), ParaAyristirici.GecersizMesaji);
        h.Denetle(!KasaGerekli || !string.IsNullOrWhiteSpace(HareketKasasi), nameof(HareketKasasi), "Kasa (kanal) seçin.");
        h.Denetle(!KarsiGerekli || !string.IsNullOrWhiteSpace(Karsi), nameof(Karsi), "Karşı taraf boş olamaz.");
        if (h.Var)
            return;
        var g = new CekHareketYaz(Guid.Empty, c.Surum, tur, DateOnly.FromDateTime(HareketTarihi), HareketTutari, NetGerekli ? NetTutar : null,
            KasaGerekli ? HareketKasasi : null, KarsiGerekli ? Karsi.Trim() : null);
        g = g with { IstekId = _hareket.Al(c.Id, new { c.Id, g }) };
        var sonuc = await api.CekHareketEkleAsync(c.Id, g);
        if (!Gecerli(n))
            return;
        _hareket.Temizle(c.Id);
        if (KasaGerekli)
            _sonKanal = HareketKasasi;
        Mesaj = $"{CekMetni.Hareket(tur)} kaydedildi" + GorunurlukEki(Guncelle(sonuc));
        await OzetiYenileAsync(n);
    });

    /// <summary>Son hareketi geri alır (sayfa önce onay ister).</summary>
    public Task GeriAlAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Acik is not { Hareketler.Count: > 0 } c)
            return;
        var g = new CekSilYaz(Guid.Empty, c.Surum);
        g = g with { IstekId = _geriAl.Al(c.Id, new { c.Id, g }) };
        var sonuc = await api.CekHareketGeriAlAsync(c.Id, g);
        if (!Gecerli(n))
            return;
        _geriAl.Temizle(c.Id);
        Mesaj = "Son hareket geri alındı" + GorunurlukEki(Guncelle(sonuc));
        await OzetiYenileAsync(n);
    }, mesgulkenBildir: true);

    /// <summary>Sunucunun döndüğü çeki listeye yazar. Seçili süzgece (yön, durum, vade) artık uymuyorsa listeden düşer ve ayrıntısı
    /// kapanır; döner: listede görünüyor mu.</summary>
    private bool Guncelle(CekDto sonuc)
    {
        var i = Cekler.ToList().FindIndex(s => s.Veri.Id == sonuc.Id);
        if (!SuzgecteGorunur(sonuc))
        {
            if (i >= 0)
                Cekler.RemoveAt(i);
            if (Acik?.Id == sonuc.Id)
                Acik = null;
            else
                SatirlariBol();
            return false;
        }
        // Satır çıkarılıp sunucunun sıralamasına (vade, sonra Id) göre doğru yere yeniden eklenir: hem yeni kayıt hem de
        // vadesi değişen düzeltme doğru konuma düşer, eski konumunda kalmaz.
        if (i >= 0)
            Cekler.RemoveAt(i);
        Cekler.Insert(SiraDizini(sonuc), new CekSatiri(sonuc, Bugun));
        Acik = sonuc;
        return true;
    }

    /// <summary>Sunucunun Liste sıralamasıyla (vade, sonra Id — CekServisi.Liste) <paramref name="sonuc"/>'un girmesi gereken
    /// dizin: ilk vadesi büyük (eşitse Id'si büyük) satırın önü.</summary>
    private int SiraDizini(CekDto sonuc)
    {
        for (var j = 0; j < Cekler.Count; j++)
        {
            var o = Cekler[j].Veri;
            if (o.VadeTarihi > sonuc.VadeTarihi || (o.VadeTarihi == sonuc.VadeTarihi && o.Id > sonuc.Id))
                return j;
        }
        return Cekler.Count;
    }

    /// <summary>Çek seçili süzgece uyuyor mu (sunucudaki liste süzgeciyle aynı: CekServisi.Liste). Arama kuralı tek kaynaktır:
    /// <see cref="CekKurallari.AramayaUyar"/> hem sunucuda (CekServisi.Liste) hem burada kullanılır.</summary>
    private bool SuzgecteGorunur(CekDto c) => c.Yon == Yon
        && Durum switch
        {
            CekSuzgecleri.Portfoyde => c.Durum is CekDurumlari.Portfoyde or CekDurumlari.KismenTahsilEdildi or CekDurumlari.KismenOdendi,
            CekSuzgecleri.Karsiliksiz => c.Durum == CekDurumlari.Karsiliksiz,
            CekSuzgecleri.Kapanan => c.Durum is CekDurumlari.TahsilEdildi or CekDurumlari.Odendi or CekDurumlari.CiroEdildi
                or CekDurumlari.Kirdirildi or CekDurumlari.IadeEdildi,
            _ => true,
        }
        && (VadeBas is null || c.VadeTarihi >= VadeBas) && (VadeSon is null || c.VadeTarihi <= VadeSon)
        && CekKurallari.AramayaUyar(c.Kisi, c.Banka, c.No, Ara);

    /// <summary>Başarı iletisinin sonu: görünüyorsa nokta, değilse neden listede olmadığı (İşlemler ekranıyla aynı üslup).</summary>
    private string GorunurlukEki(bool gorunur) => gorunur ? "." : $"; seçili süzgeç ({SuzgecMetni()}) dışında kaldığı için listede görünmüyor.";

    private string SuzgecMetni() => string.Join(" · ", new[] { CekMetni.Yon(Yon), CekMetni.Suzgec(Durum), VadeSuzgeci }.Where(p => p.Length > 0));

    /// <summary>Yazmadan sonra üst şeridin özeti yenilenir. Kayıt alınmıştır: özet okunamazsa başarı iletisi kalır, okuma hatası
    /// ayrıca yazılır.</summary>
    private async Task OzetiYenileAsync(int n)
    {
        try
        {
            var ozet = await api.CekOzetAsync();
            if (Gecerli(n))
                Ozet = ozet;
        }
        catch (Exception hata) when (Gecerli(n))
        {
            Hata = "Özet yenilenemedi. " + OkumaHataMesaji(hata);
        }
    }

    /// <summary>"Yeni çek / senet": yazılmış çek formu varsa önce onay sorulur (tasarım §2).</summary>
    [RelayCommand]
    private async Task YeniCekAsync()
    {
        if (await BirakilabilirAsync(FormIzi))
            YeniCekFormu();
    }

    private void YeniCekFormu()
    {
        Duzenlenen = null;
        FormYon = YonSecenekleri.First(y => y.Kod == Yon);
        FormTur = TurSecenekleri[0];
        No = Banka = Kisi = Not = "";
        Tutar = 0;
        Vade = Bugun.ToDateTime(TimeOnly.MinValue);
        CekKasaSecenekleriniDoldur(null);
        CekKasasi = null;
        Teminat = false;
        Konum = KonumSecenekleri[0];
        _duzenlenenSurum = 0;
        _kayit.Temizle();
        AyniCekSifirla();
        FormuAc();
    }

    /// <summary>"Çeki düzelt": yazılmış çek formu varsa önce onay sorulur; sonra açık çek forma açılır.</summary>
    [RelayCommand]
    private async Task DuzeltAsync()
    {
        if (Acik is not { } c || !await BirakilabilirAsync(FormIzi))
            return;
        _duzenlenenOzet = $"{c.VadeTarihi:dd.MM.yyyy} · {c.Kisi}";
        Duzenlenen = c.Id;
        OnPropertyChanged(nameof(FormBasligi));
        _duzenlenenSurum = c.Surum;
        FormYon = YonSecenekleri.First(y => y.Kod == c.Yon);
        FormTur = TurSecenekleri.First(t => t.Kod == c.Tur);
        No = c.No;
        Banka = c.Banka ?? "";
        Kisi = c.Kisi;
        Not = c.Not ?? "";
        Tutar = c.Tutar;
        Vade = c.VadeTarihi.ToDateTime(TimeOnly.MinValue);
        // Verilen çekin kasası pasife alınmış olabilir: seçeneklerde yine de görünsün ki form kasayı kaybetmesin.
        CekKasaSecenekleriniDoldur(c.Kanal);
        CekKasasi = c.Kanal;
        Teminat = c.Teminat;
        Konum = KonumSecenekleri.FirstOrDefault(k => k.Kod == c.Konum) ?? KonumSecenekleri[0];
        _kayit.Temizle();
        AyniCekSifirla();
        FormuAc();
    }

    /// <summary>Form şimdiki değerleriyle açıldı: hataları kalkar, kaydedilmemiş değişiklik tabanı bu değerlerdir.</summary>
    private void FormuAc()
    {
        Hatalar.Temizle();
        FormAcik = true;
        FormIzi.Ac();
    }

    /// <summary>"Vazgeç": bilerek bırakmaktır, onay sorulmaz; form kapanır.</summary>
    [RelayCommand]
    private void FormuKapat() => FormuKapatOnaysiz();

    private void FormuKapatOnaysiz()
    {
        FormAcik = false;
        FormIzi.Kapat();
        Hatalar.Temizle();
    }

    [RelayCommand]
    private Task YineDeKaydetAsync()
    {
        _ayniCekOnaylandi = true;
        return KaydetAsync();
    }

    /// <summary>Ön doğrulama (tasarım §1): sunucunun çek kuralları (CekEndpoints.CekAlanlari) alanın altında, istek gönderilmeden.</summary>
    private bool CekFormuGecerli(KodCipi yon, KodCipi tur)
    {
        var h = Hatalar;
        h.Denetle(!string.IsNullOrWhiteSpace(No), nameof(No), "Çek / senet numarası boş olamaz.");
        h.Denetle(tur.Kod != CekTurleri.Cek || !string.IsNullOrWhiteSpace(Banka), nameof(Banka), "Banka boş olamaz.");
        h.Denetle(!string.IsNullOrWhiteSpace(Kisi), nameof(Kisi), "Kişi boş olamaz.");
        h.Denetle(ParaAyristirici.GecerliMi(Tutar), nameof(Tutar), ParaAyristirici.GecersizMesaji);
        h.Denetle(Tutar > 0, nameof(Tutar), "Tutar sıfırdan büyük olmalı.");
        h.Denetle(Vade.Year >= 2000, nameof(Vade), "Vade tarihi 01.01.2000 tarihinden önce olamaz.");
        h.Denetle(yon.Kod != CekYonleri.Verilen || !string.IsNullOrWhiteSpace(CekKasasi), nameof(CekKasasi), "Ödeneceği kasayı (kanal ya da Ortak) seçin.");
        return !h.Var;
    }

    [RelayCommand]
    private Task KaydetAsync() => FormIsleAsync(Hatalar, async n =>
    {
        if (!EditorMu || FormYon is not { } yon || FormTur is not { } tur || !CekFormuGecerli(yon, tur))
            return;
        var g = new CekYaz(Guid.Empty, Duzenlenen is null ? 0 : _duzenlenenSurum, tur.Kod, yon.Kod, No.Trim(), string.IsNullOrWhiteSpace(Banka) ? null : Banka.Trim(), Kisi.Trim(), Tutar,
            DateOnly.FromDateTime(Vade), yon.Kod == CekYonleri.Verilen ? CekKasasi : null, Teminat, yon.Kod == CekYonleri.Alinan ? Konum?.Kod : null,
            string.IsNullOrWhiteSpace(Not) ? null : Not.Trim());
        // Numarasız çekte aynı çek denetimi yapılmaz: boş numara her numarasız kaydı "aynı" sayardı.
        if (Duzenlenen is null && !_ayniCekOnaylandi && g.No.Length > 0)
        {
            var ayni = (await api.CeklerAsync(yon.Kod, CekSuzgecleri.Hepsi, g.No, null, null))
                .Where(c => CekKurallari.AyniCek(c.Yon, c.Banka, c.No, g.Yon, g.Banka, g.No)).ToList();
            if (!Gecerli(n))
                return;
            if (ayni.Count > 0)
            {
                AyniCekUyarisi = $"Aynı yön, banka ve numarayla kayıtlı çek var: {string.Join(", ", ayni.Select(c => $"{c.Kisi} · {Bicim.Tl(c.Tutar)} ₺ · vade {c.VadeTarihi:dd.MM.yyyy}"))}. "
                    + "Ayrı bir çekse \"Yine de kaydet\"i seçin.";
                return;
            }
        }
        g = g with { IstekId = _kayit.Al(new { Duzenlenen, g }) };
        var sonuc = await api.CekKaydetAsync(Duzenlenen, g);
        if (!Gecerli(n))
            return;
        _kayit.Temizle();
        AyniCekSifirla();
        FormuKapatOnaysiz();
        Mesaj = (Duzenlenen is null ? "Çek kaydedildi" : "Çek güncellendi") + GorunurlukEki(Guncelle(sonuc));
        await OzetiYenileAsync(n);
    });

    /// <summary>Açık çeki siler (sayfa önce onay ister). Kapatılmış aydaki kasa hareketi olan çeki sunucu reddeder.</summary>
    public Task SilAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Acik is not { } c)
            return;
        var g = new CekSilYaz(Guid.Empty, c.Surum);
        g = g with { IstekId = _sil.Al(c.Id, new { c.Id, g }) };
        await api.CekSilAsync(c.Id, g);
        if (!Gecerli(n))
            return;
        _sil.Temizle(c.Id);
        if (Cekler.FirstOrDefault(s => s.Veri.Id == c.Id) is { } satir)
            Cekler.Remove(satir);
        Acik = null;
        Mesaj = "Çek silindi.";
        await OzetiYenileAsync(n);
    }, mesgulkenBildir: true);

    protected override void OturumTemizle()
    {
        Cekler.Clear();
        OncekiSatirlar.Clear();
        SonrakiSatirlar.Clear();
        KasaSecenekleri.Clear();
        CekKasaSecenekleri.Clear();
        Acik = null;
        Ozet = null;
        FormuKapatOnaysiz();
        _sonKanal = null;
        SuzgecleriYaz(CekYonleri.Alinan, CekSuzgecleri.Portfoyde, null, null);
        Ara = "";
        _listeHatti?.Birak();
        _kayit.Temizle();
        foreach (var anahtar in new[] { _hareket, _geriAl, _sil })
            anahtar.Temizle();
    }
}
