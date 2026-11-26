using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

public record EkstreSecenek(string Kod, string Ad);
public record EkstreYuklemeSecimi(int Oturum, string Kaynak, string Banka, string HesapAdi, int? KartId);

public partial class EkstreSatirEditor : ObservableObject
{
    private readonly Action _degisti;
    public EkstreOkunanSatir Kaynak { get; }
    public bool Kayitli { get; }
    /// <summary>Satırın iptal edilmemiş kaydı mevcut bir kayıtla eşleştirildiyse durumu ("Mevcut kayıtla eşleşti (#id)"); yoksa null.</summary>
    public string? EslesmeDurumu { get; }
    private bool TlMi => Kaynak.ParaBirimi is "TRY" or "TL" or "Belirsiz";
    public bool Secilebilir => !Kapali && TlMi;
    public string KaynakMetni => $"Sayfa {Kaynak.Sayfa} · satır {Kaynak.No} · {YonAdi(Kaynak.Yon)} · {Kaynak.ParaBirimi}\n{Kaynak.KaynakSatir}";
    public string Uyarilar => string.Join("\n", Kaynak.Uyarilar.Concat(Kayitli ? [EslesmeDurumu is { } durum ? $"{durum}. Bağı kaldırmak için kaydını iptal edin." : "Bu satır zaten kaydedildi. Yeniden kaydetmek için önce kaydını iptal edin."] : []).Concat(!TlMi && !Kapali ? ["Yalnız TL hareketleri kaydedilebilir."] : []));
    public string Ozet => $"#{Kaynak.No} · {TarihYazisi} · {Aciklama} · {TutarYazisi}" + (EslesmeDurumu is { } durum ? $" · {durum}" : "");
    public IReadOnlyList<EkstreSecenek> IslemTurleri { get; }
    public IReadOnlyList<EkstreSecenek> DagilimTurleri { get; } = [new(DagilimBicimleri.Genel, "Yalnız genel kasa"), new(DagilimBicimleri.Esit, "Seçilen kanallara eşit"), new(DagilimBicimleri.Ozel, "Özel kanal tutarları"), new(DagilimBicimleri.Otomatik, "Kartın kayıtlı dağılımı")];
    public IReadOnlyList<KartTakipDto> Kartlar { get; }
    public IReadOnlyList<KanalDto> Kanallar { get; }
    public ObservableCollection<TakipPayEditor> Paylar { get; } = new();
    public ObservableCollection<HarcamaSatiri> KaynakHarcamalar { get; } = new();
    /// <summary>'Eslestir' türünde satırın bağlanabileceği mevcut kayıtlar (sunucunun eşleşme adayı ucu, satırın tarih ve tutarıyla).</summary>
    public ObservableCollection<EslesmeAdayiSatiri> EslesmeAdaylari { get; } = new();
    [ObservableProperty] private EslesmeAdayiSatiri? _seciliAday;
    [ObservableProperty] private bool _secili;
    [ObservableProperty] private string _tarihMetni = "";
    [ObservableProperty] private string _aciklama = "";
    [ObservableProperty] private string _tutarMetni = "";
    [ObservableProperty] private EkstreSecenek? _islemTuru;
    [ObservableProperty] private EkstreSecenek? _dagilimTuru;
    [ObservableProperty] private KartTakipDto? _kart;
    [ObservableProperty] private HarcamaSatiri? _kaynakHarcama;
    public bool KartSecimiGorunur { get; }
    public bool IadeMi => IslemTuru?.Kod == EkstreIslemTurleri.KartIade;
    /// <summary>Satır mevcut bir kayda bağlanır (yeni kayıt, kasa ve kart borcu etkisi yok); kanal dağılımı seçilmez.</summary>
    public bool EslesmeMi => IslemTuru?.Kod == EkstreIslemTurleri.Eslestir;
    public bool DagilimGorunur => !EslesmeMi;

    public EkstreSatirEditor(EkstreOkunanSatir kaynak, EkstreBelgeDto belge, IReadOnlyList<KanalDto> kanallar, IReadOnlyList<KartTakipDto> kartlar, Action degisti)
    {
        _degisti = () => { };
        Kaynak = kaynak;
        var kayit = belge.Kayitlar.FirstOrDefault(k => k.SatirNo == kaynak.No && !k.Iptal);
        Kayitli = kayit is not null;
        YokSayildi = !Kayitli && belge.YokSayilanlar?.Contains(kaynak.No) == true;
        YokSayilabilir = belge.YokSayilanlar is not null && !Kapali;
        _kartBelgesi = belge.Kaynak == EkstreKaynaklari.Kart;
        if (kayit is { IslemTuru: EkstreIslemTurleri.Eslestir })
            EslesmeDurumu = $"Mevcut kayıtla eşleşti ({EslesmeAdayiSatiri.TurAdi(kayit.EslesmeTuru)} #{kayit.EslesmeId})";
        Kanallar = kanallar;
        Kartlar = kartlar;
        KartSecimiGorunur = belge.Kaynak == EkstreKaynaklari.Banka;
        _erpBelgesi = belge.Kaynak == EkstreKaynaklari.Erp;
        IslemTurleri = _erpBelgesi ? [new(EkstreIslemTurleri.Gelir, "Gelir"), new(EkstreIslemTurleri.Gider, "Gider"), new(EkstreIslemTurleri.Eslestir, "Mevcut kayıtla eşleştir")]
            : belge.Kaynak == EkstreKaynaklari.Kart
            ? [new(EkstreIslemTurleri.KartHarcama, "Kart harcaması"), new(EkstreIslemTurleri.KartIade, "Kart iadesi"), new(EkstreIslemTurleri.KartOdemesi, "Karta ödeme"), new(EkstreIslemTurleri.Eslestir, "Mevcut kayıtla eşleştir")]
            : [new(EkstreIslemTurleri.Gelir, "Gelir"), new(EkstreIslemTurleri.Gider, "Gider"), new(EkstreIslemTurleri.KartOdemesi, "Karta ödeme"), new(EkstreIslemTurleri.Eslestir, "Mevcut kayıtla eşleştir")];
        TarihMetni = kaynak.Tarih?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        Aciklama = kaynak.Aciklama;
        TutarMetni = kaynak.Tutar?.ToString("0.00", CultureInfo.GetCultureInfo("tr-TR")) ?? "";
        IslemTuru = IslemTurleri.FirstOrDefault(t => t.Kod == kaynak.OnerilenIslem);
        Kart = kartlar.FirstOrDefault(k => k.Id == belge.KartId);
        Paylar.CollectionChanged += (_, e) => { if (e.NewItems is not null) foreach (TakipPayEditor p in e.NewItems) p.PropertyChanged += PayDegisti; if (e.OldItems is not null) foreach (TakipPayEditor p in e.OldItems) p.PropertyChanged -= PayDegisti; ElleDegisiklik(); _degisti(); OnPropertyChanged(nameof(KontrolNedeni)); };
        _degisti = degisti;
        _degisiklikIzle = true;
    }
    private void PayDegisti(object? sender, PropertyChangedEventArgs e) { ElleDegisiklik(); _degisti(); OnPropertyChanged(nameof(KontrolNedeni)); }
    public void PayEkle() => Paylar.Add(new(Kanallar));
    partial void OnIslemTuruChanged(EkstreSecenek? value)
    {
        DagilimTuru = value?.Kod is EkstreIslemTurleri.KartOdemesi or EkstreIslemTurleri.KartIade ? DagilimTurleri.Single(x => x.Kod == DagilimBicimleri.Otomatik) : value?.Kod == EkstreIslemTurleri.KartHarcama ? DagilimTurleri.Single(x => x.Kod == DagilimBicimleri.Esit) : null;
        KaynakHarcama = null;
        OnPropertyChanged(nameof(IadeMi));
        OnPropertyChanged(nameof(EslesmeMi));
        OnPropertyChanged(nameof(DagilimGorunur));
        if (!EslesmeMi)
            AdaylariTemizle();
    }
    /// <summary>Adaylar satırın o anki tarih ve tutarıyla sorulur; bu değerler değişince eski adaylar ve seçim geçersizdir.</summary>
    private void AdaylariTemizle() { SeciliAday = null; EslesmeAdaylari.Clear(); }
    /// <summary>Eşleşme adayı sorgusu (satırın tarih ve tutarı); geçersizse <paramref name="hata"/> dolar.</summary>
    public bool AdaySorgusu(out EkstreEslesmeAdayiSorgu sorgu, out string? hata)
    {
        sorgu = null!;
        hata = null;
        if (!DateOnly.TryParseExact(TarihMetni.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tarih))
        { hata = $"Satır {Kaynak.No}: Tarihi yıl-ay-gün biçiminde girin (2026-09-27)."; return false; }
        if (!ParaAyristirici.Coz(TutarMetni, out var tutar, out var paraHatasi) || tutar <= 0 || decimal.Round(tutar, 2) != tutar)
        { hata = $"Satır {Kaynak.No}: {paraHatasi ?? "Tutar pozitif ve kuruş hassasiyetinde olmalı."}"; return false; }
        sorgu = new(tarih, tutar, _erpBelgesi ? Kaynak.No : null);
        return true;
    }
    /// <summary>Sunucudan gelen adayları yansıtır; önceki seçim yeni listede yoksa kalkar.</summary>
    public void AdaylariYansit(IEnumerable<EkstreEslesmeAdayiDto> adaylar)
    {
        var onceki = SeciliAday?.Veri;
        TakipMetni.Doldur(EslesmeAdaylari, adaylar.Select(a => new EslesmeAdayiSatiri(a)));
        SeciliAday = onceki is null ? null : EslesmeAdaylari.FirstOrDefault(a => a.Veri.Tur == onceki.Tur && a.Veri.Id == onceki.Id);
    }
    partial void OnKartChanged(KartTakipDto? value)
    {
        KaynakHarcama = null;
        TakipMetni.Doldur(KaynakHarcamalar, value?.Harcamalar.Where(h => h.Tutar > 0 && !h.Iptal).Select(h => new HarcamaSatiri(h)) ?? []);
    }
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TarihMetni) or nameof(Aciklama) or nameof(TutarMetni) or nameof(IslemTuru) or nameof(DagilimTuru) or nameof(Kart) or nameof(KaynakHarcama) or nameof(SeciliAday))
            ElleDegisiklik();
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(TarihMetni) or nameof(Aciklama) or nameof(TutarMetni))
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Ozet)));
        if (e.PropertyName is nameof(TarihMetni) or nameof(TutarMetni) && EslesmeAdaylari.Count > 0)
            AdaylariTemizle();
        if (e.PropertyName == nameof(IslemTuru))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(EtkinYon)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(YonMetni)));
        }
        // "Kontrol edin" nedeni satırın alanlarından hesaplanır (hareket penceresi): alan değişince yeniden okunur.
        if (e.PropertyName is nameof(IslemTuru) or nameof(DagilimTuru) or nameof(Kart) or nameof(KaynakHarcama) or nameof(TarihMetni) or nameof(TutarMetni)
            or nameof(Aciklama) or nameof(Oneri) or nameof(ElleDegisti) or nameof(SeciliAday))
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(KontrolNedeni)));
        // Öneri gelmesi gibi gösterim değişiklikleri önizlemeyi ve onayı geçersiz kılmaz; yalnız kaydedilecek alanlar ve seçim.
        if (e.PropertyName is nameof(Secili) or nameof(TarihMetni) or nameof(Aciklama) or nameof(TutarMetni) or nameof(IslemTuru) or nameof(DagilimTuru) or nameof(Kart) or nameof(KaynakHarcama) or nameof(SeciliAday))
            _degisti?.Invoke();
    }
    public EkstreSatirYaz Yaz()
    {
        var (yaz, hatalar) = Hazirla();
        return yaz ?? throw new DogrulamaHatasi($"Satır {Kaynak.No}: {hatalar[0]}");
    }
    /// <summary>Satırın gönderilecek biçimi ya da bütün eksikleri (ilk eksikte durmaz; kullanıcı hepsini bir kerede görür).</summary>
    public (EkstreSatirYaz? Yaz, IReadOnlyList<string> Hatalar) Hazirla()
    {
        if (!Secilebilir)
            return (null, [Kayitli ? "Bu satır zaten kayıtlı." : YokSayildi ? "Bu satır yok sayıldı." : "Yalnız TL hareketleri kaydedilebilir."]);
        var hatalar = new List<string>();
        if (!DateOnly.TryParseExact(TarihMetni.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tarih))
            hatalar.Add("Tarihi yıl-ay-gün biçiminde girin (2026-09-27).");
        // Diğer para girişleriyle aynı kural: '1.500' 1,50 TL sayılmaz, hata verir.
        if (!ParaAyristirici.Coz(TutarMetni, out var tutar, out var paraHatasi))
            hatalar.Add(paraHatasi!);
        else if (tutar <= 0 || decimal.Round(tutar, 2) != tutar)
            hatalar.Add("Tutar pozitif ve kuruş hassasiyetinde olmalı.");
        if (string.IsNullOrWhiteSpace(Aciklama))
            hatalar.Add("Açıklamayı doldurun.");
        if (IslemTuru is null || !IslemTurleri.Contains(IslemTuru))
        {
            hatalar.Add("İşlem türünü seçin.");
            return (null, hatalar);
        }
        var tur = IslemTuru.Kod;
        var dagilim = DagilimTuru?.Kod;
        if (tur == EkstreIslemTurleri.Eslestir)
        {
            // Yalnız bağ: kanal dağılımı, kart ve iade kaynağı gönderilmez; kart eşleşen kayıttan okunur.
            if (SeciliAday is null || !EslesmeAdaylari.Contains(SeciliAday))
                hatalar.Add("Eşleşme adaylarını getirip bağlanacak mevcut kaydı seçin.");
            return hatalar.Count > 0 ? (null, hatalar)
                : (new(Kaynak.No, tarih, Aciklama.Trim(), tutar, EkstreIslemTurleri.Eslestir, DagilimBicimleri.Eslesme, [], null, null, SeciliAday!.Veri.Tur, SeciliAday.Veri.Id), []);
        }
        IReadOnlyList<KanalPayYaz> paylar = [];
        if (tur is EkstreIslemTurleri.KartOdemesi or EkstreIslemTurleri.KartIade)
            dagilim = DagilimBicimleri.Otomatik;
        else if (dagilim == DagilimBicimleri.Ozel)
        {
            try
            {
                paylar = TakipMetni.Paylar(Paylar);
                if (paylar.Sum(x => x.Tutar) != tutar)
                    hatalar.Add("Kanal tutarları hareket tutarına eşit olmalı.");
            }
            catch (DogrulamaHatasi e) { hatalar.Add(e.Message); }
        }
        else if (dagilim == DagilimBicimleri.Esit)
        {
            if (Paylar.Count == 0 || Paylar.Any(p => p.Kanal is null) || Paylar.Select(p => p.Kanal!.Id).Distinct().Count() != Paylar.Count)
                hatalar.Add("Dağıtılacak kanalları birer kez seçin.");
            else
                paylar = Paylar.Select(p => new KanalPayYaz(p.Kanal!.Id, 0)).ToList();
        }
        else if (dagilim != DagilimBicimleri.Genel || tur == EkstreIslemTurleri.KartHarcama)
            hatalar.Add("Bu hareket için kanal dağılımını seçin.");
        if (tur is EkstreIslemTurleri.KartHarcama or EkstreIslemTurleri.KartIade or EkstreIslemTurleri.KartOdemesi && Kart is null)
            hatalar.Add("Yeni takibe alınmış kartı seçin.");
        if (tur == EkstreIslemTurleri.KartIade && (KaynakHarcama is null || !KaynakHarcamalar.Contains(KaynakHarcama)))
            hatalar.Add("İadenin kaynak harcamasını seçin.");
        return hatalar.Count > 0 ? (null, hatalar)
            : (new(Kaynak.No, tarih, Aciklama.Trim(), tutar, tur, dagilim!, paylar, tur is EkstreIslemTurleri.KartHarcama or EkstreIslemTurleri.KartIade or EkstreIslemTurleri.KartOdemesi ? Kart?.Id : null, tur == EkstreIslemTurleri.KartIade ? KaynakHarcama?.Veri.Id : null), []);
    }
}

public record EkstreGecmisSatiri(EkstreBelgeOzetDto Veri)
{
    public string Baslik => $"{Veri.Yuklendi.LocalDateTime:dd.MM.yyyy HH:mm} · {Veri.DosyaAdi}";
    /// <summary>Kapanan satır: iptal edilmemiş kaydı olan ya da yok sayılan (bir satır ikisi birden olamaz).</summary>
    public int Kapanan => Veri.KayitSayisi + Veri.YokSayilanSayisi;
    /// <summary>Belgenin banka, tür ve hesabı; ERP12 belgesi bir bankaya ait değildir.</summary>
    public static string Kimlik(string banka, string kaynak, string hesap) => kaynak == EkstreKaynaklari.Erp ? "ERP12 cari hareketleri" : $"{banka} · {kaynak} · {hesap}";
    public string Ozet => $"{Kimlik(Veri.Banka, Veri.Kaynak, Veri.HesapAdi)} · {Veri.SatirSayisi} satır · {Math.Max(0, Veri.SatirSayisi - Kapanan)} bekliyor · {Kapanan} kapandı"
        + (Veri.YokSayilanSayisi > 0 ? $" ({Veri.KayitSayisi} kayıt, {Veri.YokSayilanSayisi} yok sayıldı)" : "");
}
public record EkstreKayitSatiri(EkstreKayitDto Veri)
{
    public string Baslik => $"Satır {Veri.SatirNo} · {Veri.Tarih:dd.MM.yyyy} · {Veri.Aciklama} · {Bicim.Tl(Veri.Tutar)} ₺";
    public string Ozet => Veri.IslemTuru == EkstreIslemTurleri.Eslestir
        ? $"Mevcut kayıtla eşleşme · {(Veri.Iptal ? "İptal edildi" : EslesmeMetni)} · Kasa etkisi yok" + IptalMetni
        : $"{Veri.IslemTuru} · {(Veri.Iptal ? "İptal edildi" : Veri.EslesmeTuru is null ? "Kaydedildi" : EslesmeMetni)} · {(Veri.DagilimTuru == DagilimBicimleri.Genel ? "Yalnız genel kasa" : TakipMetni.Paylar(Veri.Dagilimlar))}"
            + (Veri.ErpCari is { } cari ? $" · ERP12: {cari}" : "") + IptalMetni;
    /// <summary>Eşleşen kaydın durumu: 'Eslestir' satırında bağlandığı kayıt; kaydı alış ödemesine bağlanmış satırda alışın kaydı.</summary>
    private string EslesmeMetni => Veri.EslesmeDurumu == EslesmeDurumlari.KayitYok
        ? $"Eşleştiği kayıt (#{Veri.EslesmeId}) silinmiş ya da iptal edilmiş"
        : Veri.IslemTuru == EkstreIslemTurleri.Eslestir ? $"Mevcut kayıtla eşleşti ({EslesmeAdayiSatiri.TurAdi(Veri.EslesmeTuru)} #{Veri.EslesmeId})"
        : $"Kaydı alış ödemesine bağlandı ({EslesmeAdayiSatiri.TurAdi(Veri.EslesmeTuru)} #{Veri.EslesmeId})";
    private string IptalMetni => Veri.Iptal && Veri.IptalAciklamasi is { } gerekce
        ? $"\nİptal gerekçesi: {gerekce} · {(Veri.IptalZamani is { } z ? $"{z.LocalDateTime:dd.MM.yyyy HH:mm}" : "zamanı bilinmiyor")}" : "";
}
/// <summary>Ekstre satırının eşleşme adayı (seçim listesinde).</summary>
public record EslesmeAdayiSatiri(EkstreEslesmeAdayiDto Veri)
{
    public string Baslik => $"{TurAdi(Veri.Tur)} #{Veri.Id} · {Veri.Tarih:dd.MM.yyyy} · {Bicim.Tl(Veri.Tutar)} ₺ · {Veri.Aciklama}"
        + (Veri.KanalEtiketi is { } kanal ? $" · {kanal}" : "") + (Veri.AlisId is { } alis ? $" · Alış #{alis}" : "") + (Veri.EkstreKayitId is not null ? " · ekstreden" : "");
    public static string TurAdi(string? tur) => tur switch
    {
        EslesmeTurleri.Gider => "Gider",
        EslesmeTurleri.KartHarcama => "Kart harcaması",
        EslesmeTurleri.KartTaksidi => "Kart taksidi",
        EslesmeTurleri.KartOdeme => "Kart ödemesi",
        EslesmeTurleri.EkstreGelir => "Banka girişi",
        EslesmeTurleri.CekKaydi => "Çek",
        EslesmeTurleri.CekCirosu => "Çek cirosu",
        _ => "Kayıt"
    };
}
