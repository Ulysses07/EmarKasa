using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;
using CekKurallari = Kasa.Core.CekKurallari;

namespace Kasa.App.Core;

/// <summary>
/// Çekler ekranı (docs/specs/2026-10-01-cekler.md "Masaüstü ekranı"). Süzgeçler (yön, durum, arama, vade aralığı) sunucuda uygulanır:
/// her değişiklik listeyi yeniden yükler. Açılan çekin ayrıntısı satırının hemen altında görünür (<see cref="OncekiSatirlar"/>, açık
/// çek, <see cref="SonrakiSatirlar"/>). Hareket düğmeleri sunucunun izin verdiği türlerdir (<see cref="CekDto.IzinliHareketler"/>);
/// geçiş kuralı istemcide yinelenmez. Hareket formu tarih bugün, tutar kalan ve son seçilen kasayla açılır. Yeni çekte aynı yön,
/// banka ve numaralı kayıt kaydetmeden önce uyarılır; kullanıcı onaylarsa kaydedilir.
/// </summary>
/// <param name="zaman">Vade rozeti ve hazır süzgeçlerin günü (yerel); verilmezse sistem saati.</param>
public partial class CekTakipViewModel(ICekApi api, IKasaApi finans, AuthViewModel auth, TimeProvider? zaman = null) : OturumluViewModel(auth)
{
    private readonly TimeProvider _zaman = zaman ?? TimeProvider.System;
    private readonly TekrarAnahtari _kayit = new(), _hareket = new(), _geriAl = new(), _sil = new();
    private string? _sonKanal;
    private bool _ayniCekOnaylandi;

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

    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormBasligi))] private bool _formAcik;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(FormBasligi))] private int? _duzenlenen;
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
    public string FormBasligi => Duzenlenen is null ? "Yeni çek / senet" : "Çeki düzelt";
    public string VadeSuzgeci => (VadeBas, VadeSon) switch
    {
        ({ } bas, { } son) => $"Vade {bas:dd.MM.yyyy} – {son:dd.MM.yyyy}",
        (null, { } son) => $"Vade {son:dd.MM.yyyy} ve öncesi",
        ({ } bas, null) => $"Vade {bas:dd.MM.yyyy} ve sonrası",
        _ => "",
    };
    public string MasrafMetni => NetGerekli && ParaAyristirici.HepsiGecerli(HareketTutari, NetTutar)
        ? $"Masraf: {Bicim.Tl(HareketTutari - NetTutar)} ₺ (karşı tarafa Cari gider). Kasaya {Bicim.Tl(NetTutar)} ₺ girer." : "";
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
    public string PortfoyMetni => Ozet is { } o ? CekMetni.OzetSatiri("Portföydeki alınan", o.PortfoydekiAlinan) : "";
    public string Alinan30Metni => Ozet is { } o ? CekMetni.OzetSatiri("30 gün içinde tahsil edilecek", o.Alinan30) : "";
    public string Verilen30Metni => Ozet is { } o ? CekMetni.OzetSatiri("30 gün içinde ödenecek", o.Verilen30) : "";
    public string GecmisMetni => Ozet is { } o ? CekMetni.OzetSatiri("Vadesi geçmiş, tahsil edilmemiş", o.VadesiGecmis) : "";

    partial void OnOzetChanged(CekOzetDto? value)
    {
        foreach (var p in new[] { nameof(PortfoyMetni), nameof(Alinan30Metni), nameof(Verilen30Metni), nameof(GecmisMetni) })
            OnPropertyChanged(p);
    }

    partial void OnAcikChanged(CekDto? value)
    {
        TakipMetni.Doldur(Hareketler, value?.Hareketler.Select(h => new CekHareketSatiri(h)) ?? []);
        TakipMetni.Doldur(HareketCipleri, value?.IzinliHareketler.Select(t => new KodCipi(t, CekMetni.Hareket(t))) ?? []);
        HareketTuru = null;
        SatirlariBol();
    }

    partial void OnNoChanged(string value) => AyniCekSifirla();
    partial void OnBankaChanged(string value) => AyniCekSifirla();
    partial void OnFormYonChanged(KodCipi? value) => AyniCekSifirla();

    private void AyniCekSifirla()
    {
        _ayniCekOnaylandi = false;
        AyniCekUyarisi = null;
    }

    /// <summary>Liste, özet ve kasa seçenekleri.</summary>
    public Task YukleAsync() => YurutAsync(async n =>
    {
        var kanallar = await finans.KanallarAsync();
        var cekler = await api.CeklerAsync(Yon, Durum, string.IsNullOrWhiteSpace(Ara) ? null : Ara.Trim(), VadeBas, VadeSon);
        var ozet = await api.CekOzetAsync();
        if (!Gecerli(n))
            return;
        Yansit(kanallar, cekler, ozet);
    });

    /// <summary>Bildirimden gelen çek (//cekler?CekId=…): çek okunur, yönüne ve "Hepsi" durumuna geçilir (süzgeç onu gizlemesin),
    /// liste yüklenir. Seçim <see cref="IdIleSec"/> ile yapılır.</summary>
    public Task CekIcinYukleAsync(int id) => YurutAsync(async n =>
    {
        var cek = await api.CekAsync(id);
        if (!Gecerli(n))
            return;
        SuzgecleriYaz(cek.Yon, CekSuzgecleri.Hepsi, null, null);
        Ara = "";
        var kanallar = await finans.KanallarAsync();
        var cekler = await api.CeklerAsync(Yon, Durum, null, null, null);
        var ozet = await api.CekOzetAsync();
        if (!Gecerli(n))
            return;
        Yansit(kanallar, cekler, ozet);
    });

    private void Yansit(IReadOnlyList<KanalDto> kanallar, IReadOnlyList<CekDto> cekler, CekOzetDto ozet)
    {
        TakipMetni.Doldur(KasaSecenekleri, kanallar.Where(k => k.Aktif).Select(k => k.Ad));
        TakipMetni.Doldur(CekKasaSecenekleri, kanallar.Where(k => k.Aktif).Select(k => k.Ad).Prepend(KanalEtiketleri.Ortak));
        TakipMetni.Doldur(Cekler, cekler.Select(c => new CekSatiri(c, Bugun)));
        Ozet = ozet;
        Acik = Acik is { } eski ? cekler.FirstOrDefault(c => c.Id == eski.Id) : null;
        SatirlariBol();
        Tamamlandi();
    }

    private void SatirlariBol()
    {
        var i = Acik is null ? -1 : Cekler.ToList().FindIndex(s => s.Veri.Id == Acik.Id);
        TakipMetni.Doldur(OncekiSatirlar, i < 0 ? Cekler : Cekler.Take(i + 1));
        TakipMetni.Doldur(SonrakiSatirlar, i < 0 ? [] : Cekler.Skip(i + 1));
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
        Sec(satir);
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
            default:
                SuzgecleriYaz(CekYonleri.Alinan, CekSuzgecleri.Portfoyde, null, bugun.AddDays(-1));
                break;
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

    /// <summary>Satıra tıklama: kapalıysa açar, açıksa kapatır.</summary>
    [RelayCommand]
    private void Sec(CekSatiri satir) => Acik = Acik?.Id == satir.Veri.Id ? null : satir.Veri;

    /// <summary>Hareket türü düğmesi: formu tarih bugün, tutar kalan (dönüşte ciro/kırdırma tutarı; karşılıksız ve iadede 0) ve son
    /// seçilen kasayla açar.</summary>
    [RelayCommand]
    private void SecHareket(KodCipi cip)
    {
        if (Acik is not { } c)
            return;
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
    }

    [RelayCommand]
    private Task HareketKaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Acik is not { } c || HareketTuru is not { } tur)
            return;
        if (!ParaAyristirici.HepsiGecerli(HareketTutari, NetTutar))
        {
            Hata = ParaAyristirici.GecersizMesaji;
            return;
        }
        var g = new CekHareketYaz(Guid.Empty, c.Surum, tur, DateOnly.FromDateTime(HareketTarihi), HareketTutari, NetGerekli ? NetTutar : null,
            KasaGerekli ? HareketKasasi : null, KarsiGerekli ? Karsi.Trim() : null);
        g = g with { IstekId = _hareket.Al(new { c.Id, g }) };
        var sonuc = await api.CekHareketEkleAsync(c.Id, g);
        if (!Gecerli(n))
            return;
        _hareket.Temizle();
        if (KasaGerekli)
            _sonKanal = HareketKasasi;
        Guncelle(sonuc);
        Mesaj = $"{CekMetni.Hareket(tur)} kaydedildi.";
    });

    /// <summary>Son hareketi geri alır (sayfa önce onay ister).</summary>
    public Task GeriAlAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Acik is not { Hareketler.Count: > 0 } c)
            return;
        var g = new CekSilYaz(Guid.Empty, c.Surum);
        g = g with { IstekId = _geriAl.Al(new { c.Id, g }) };
        var sonuc = await api.CekHareketGeriAlAsync(c.Id, g);
        if (!Gecerli(n))
            return;
        _geriAl.Temizle();
        Guncelle(sonuc);
        Mesaj = "Son hareket geri alındı.";
    }, mesgulkenBildir: true);

    private void Guncelle(CekDto sonuc)
    {
        var i = Cekler.ToList().FindIndex(s => s.Veri.Id == sonuc.Id);
        if (i >= 0)
            Cekler[i] = new CekSatiri(sonuc, Bugun);
        else
            Cekler.Insert(0, new CekSatiri(sonuc, Bugun));
        Acik = sonuc;
    }

    [RelayCommand]
    private void YeniCek()
    {
        Duzenlenen = null;
        FormYon = YonSecenekleri.First(y => y.Kod == Yon);
        FormTur = TurSecenekleri[0];
        No = Banka = Kisi = Not = "";
        Tutar = 0;
        Vade = Bugun.ToDateTime(TimeOnly.MinValue);
        CekKasasi = null;
        Teminat = false;
        Konum = KonumSecenekleri[0];
        _kayit.Temizle();
        AyniCekSifirla();
        FormAcik = true;
    }

    [RelayCommand]
    private void Duzelt()
    {
        if (Acik is not { } c)
            return;
        Duzenlenen = c.Id;
        FormYon = YonSecenekleri.First(y => y.Kod == c.Yon);
        FormTur = TurSecenekleri.First(t => t.Kod == c.Tur);
        No = c.No;
        Banka = c.Banka ?? "";
        Kisi = c.Kisi;
        Not = c.Not ?? "";
        Tutar = c.Tutar;
        Vade = c.VadeTarihi.ToDateTime(TimeOnly.MinValue);
        CekKasasi = c.Kanal;
        Teminat = c.Teminat;
        Konum = KonumSecenekleri.FirstOrDefault(k => k.Kod == c.Konum) ?? KonumSecenekleri[0];
        _kayit.Temizle();
        AyniCekSifirla();
        FormAcik = true;
    }

    [RelayCommand] private void FormuKapat() => FormAcik = false;

    [RelayCommand]
    private Task YineDeKaydetAsync()
    {
        _ayniCekOnaylandi = true;
        return KaydetAsync();
    }

    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || FormYon is not { } yon || FormTur is not { } tur)
            return;
        if (!ParaAyristirici.GecerliMi(Tutar))
        {
            Hata = ParaAyristirici.GecersizMesaji;
            return;
        }
        var surum = Duzenlenen is { } id ? Cekler.FirstOrDefault(s => s.Veri.Id == id)?.Veri.Surum ?? Acik?.Surum ?? 0 : 0;
        var g = new CekYaz(Guid.Empty, surum, tur.Kod, yon.Kod, No.Trim(), string.IsNullOrWhiteSpace(Banka) ? null : Banka.Trim(), Kisi.Trim(), Tutar,
            DateOnly.FromDateTime(Vade), yon.Kod == CekYonleri.Verilen ? CekKasasi : null, Teminat, yon.Kod == CekYonleri.Alinan ? Konum?.Kod : null,
            string.IsNullOrWhiteSpace(Not) ? null : Not.Trim());
        if (Duzenlenen is null && !_ayniCekOnaylandi)
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
        FormAcik = false;
        Guncelle(sonuc);
        Mesaj = Duzenlenen is null ? "Çek kaydedildi." : "Çek güncellendi.";
    });

    /// <summary>Açık çeki siler (sayfa önce onay ister). Kapatılmış aydaki kasa hareketi olan çeki sunucu reddeder.</summary>
    public Task SilAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Acik is not { } c)
            return;
        var g = new CekSilYaz(Guid.Empty, c.Surum);
        g = g with { IstekId = _sil.Al(new { c.Id, g }) };
        await api.CekSilAsync(c.Id, g);
        if (!Gecerli(n))
            return;
        _sil.Temizle();
        if (Cekler.FirstOrDefault(s => s.Veri.Id == c.Id) is { } satir)
            Cekler.Remove(satir);
        Acik = null;
        Mesaj = "Çek silindi.";
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
        FormAcik = false;
        _sonKanal = null;
        SuzgecleriYaz(CekYonleri.Alinan, CekSuzgecleri.Portfoyde, null, null);
        Ara = "";
        foreach (var anahtar in new[] { _kayit, _hareket, _geriAl, _sil })
            anahtar.Temizle();
    }
}
