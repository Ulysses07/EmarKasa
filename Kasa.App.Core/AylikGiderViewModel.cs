using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

public record GiderSecimi(string Kod, string Ad);
public partial class AylikGiderViewModel(IAylikGiderApi api, IKasaApi finans, AuthViewModel auth) : OturumluViewModel(auth), IKaydedilmemisForm
{
    /// <summary>Şablon formunun hataları (tasarım 2026-10-02 §1). Sunucu aylık gider hatalarını alan adı olmadan ({ hata }) döndürür:
    /// sunucu iletisi genel hataya gider.</summary>
    public AlanHatalari SablonHatalari { get; } = new();
    /// <summary>"Bu ayın ödemesini kaydet" formunun hataları.</summary>
    public AlanHatalari OdemeHatalari { get; } = new();
    protected override IEnumerable<AlanHatalari> Formlar => [SablonHatalari, OdemeHatalari];

    /// <summary>Şablon formunun açıldığı andaki değerleri (yeni ya da düzenlenen şablon): kaydedilmemiş değişiklik ölçütü (tasarım §2).</summary>
    private KaydedilmemisDegisiklik? _formIzi;
    private KaydedilmemisDegisiklik FormIzi => _formIzi ??= new(() => new
    {
        Ad,
        Tur = Tur?.Kod,
        Tutar,
        OdemeGunu,
        Dagilim = DagilimTuru?.Kod,
        GecerliAy,
        Aktif,
        Kanallar = KanalSecimleri.Where(k => k.Secili).Select(k => k.Veri.Id).ToList(),
        Paylar = Paylar.Select(p => new { Kanal = p.Kanal?.Id, p.Tutar }).ToList(),
    });
    public bool KaydedilmemisDegisiklikVar => FormIzi.Var;
    /// <summary>Yenileme açık formu korur (Ö-4): kabuk sormadan yeniler.</summary>
    public bool YenilemeFormuKorur => true;

    /// <summary>Kabuktan çıkışta "Bırak": şablon formu boş yeni şablona döner.</summary>
    public void DegisiklikleriBirak() => YeniForm();

    /// <summary>Listedeki satırın düğmesi (AG-02): ödenmemiş satırda "Öde", ödenmiş satırda "Ödemeyi iptal et".</summary>
    public static string SatirDugmesi(AylikGiderSatiri satir) => satir.OdendiMi ? "Ödemeyi iptal et" : "Öde";

    /// <summary>"Öde" tıklamasının kendisi (görev 14-19 incelemesi): aynı satıra yeniden basılınca <see cref="SeciliOdeme"/>
    /// değişmediği için PropertyChanged tetiklenmez; sayfa formu görünür yere kaydırmak için buna bağlanır.</summary>
    public event EventHandler? OdemeSecIstendi;

    /// <summary>Ödeme kaydedildi ya da iptal edildi (Y-1): form kapanıp sayfa kısalınca başarı iletisi ve güncellenen satır görünür
    /// alanın dışında kalıyordu; sayfa bununla sonucu görünür yere kaydırır.</summary>
    public event EventHandler? OdemeSonucuGosterIstendi;

    /// <summary>Son yükleme tamamlanmamışken (soluk, eski liste ya da yükleme sürerken) "Öde" ve "Ödemeyi iptal et" aynı ölçütle
    /// (<see cref="OturumluViewModel.VeriHazir"/>) durur ve bu iletiyi verir.</summary>
    public const string ListeGuncelDegil = "Liste güncel değil; yenileyin.";

    /// <summary>Satır düğmesinin ("Öde" / "Ödemeyi iptal et") ortak ölçütü: liste güncel değilse <see cref="ListeGuncelDegil"/>
    /// yazılır ve false döner. Sayfa iptal gerekçesini sormadan önce de bunu denetler.</summary>
    public bool ListeGuncelMi()
    {
        if (VeriHazir)
            return true;
        Hata = ListeGuncelDegil;
        return false;
    }
    private readonly TekrarAnahtari _sablonKey = new(), _odemeKey = new(), _iptalKey = new();
    private AylikGiderAyDto? _ayVerisi;
    private AylikGiderSablonDto? _duzenlenen;
    public IReadOnlyList<GiderSecimi> Turler { get; } = new[] { new GiderSecimi("Kira", "Kira"), new("Maas", "Maaş"), new("Fatura", "Fatura"), new("Diger", "Diğer") };
    public IReadOnlyList<GiderSecimi> DagilimTurleri { get; } = new[] { new GiderSecimi(DagilimBicimleri.Genel, "Yalnız genel kasa"), new(DagilimBicimleri.Esit, "Seçilen kanallara eşit"), new(DagilimBicimleri.Ozel, "Kanallara tutar girerek") };
    public ObservableCollection<AylikGiderSatiri> Kayitlar { get; } = new();
    /// <summary>Ayın iptal edilmiş ödemeleri (salt okunur; gerekçe ve iptal anıyla). Toplamlara girmez; planı <see cref="Kayitlar"/>'da
    /// yeniden ödeme bekler. Alanı taşımayan eski sunucuda boştur.</summary>
    public ObservableCollection<AylikGiderIptalSatiri> Iptaller { get; } = new();
    public bool IptalVar => Iptaller.Count > 0;
    public ObservableCollection<AylikSablonSatiri> Sablonlar { get; } = new();
    public ObservableCollection<KanalDto> Kanallar { get; } = new();
    public ObservableCollection<TakipKanalSecimi> KanalSecimleri { get; } = new();
    public ObservableCollection<TakipPayEditor> Paylar { get; } = new();
    [ObservableProperty] private DateTime _ayTarihi = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private string _ayOzeti = "";
    [ObservableProperty] private AylikGiderSatiri? _seciliOdeme;
    [ObservableProperty] private DateTime _odemeTarihi = DateTime.Today;
    [ObservableProperty] private string _odemeNotu = "";
    [ObservableProperty] private bool _odemeOnay;
    [ObservableProperty] private string _ad = "";
    [ObservableProperty] private GiderSecimi? _tur;
    [ObservableProperty] private decimal _tutar;
    [ObservableProperty] private int _odemeGunu = 1;
    [ObservableProperty] private GiderSecimi? _dagilimTuru;
    [ObservableProperty] private DateTime _gecerliAy = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private bool _aktif = true;
    public bool EsitDagilim => DagilimTuru?.Kod == DagilimBicimleri.Esit;
    public bool OzelDagilim => DagilimTuru?.Kod == DagilimBicimleri.Ozel;
    public bool OdemeSecili => SeciliOdeme is not null;
    public string OdemeEtkisi => SeciliOdeme is { } s ? $"{s.Baslik}\nGenel kasadan {Bicim.Tl(s.Veri.Tutar)} ₺ çıkar.\n" + (s.Veri.DagilimTuru == DagilimBicimleri.Genel ? "Kanal bakiyeleri değişmez." : TakipMetni.Paylar(s.Veri.Dagilimlar)) : "Ödenecek aylık gideri seçin.";
    public string SablonBasligi => _duzenlenen is null ? "Yeni aylık gider şablonu" : $"Şablonu düzenle: {_duzenlenen.Ad}";
    partial void OnDagilimTuruChanged(GiderSecimi? value) { OnPropertyChanged(nameof(EsitDagilim)); OnPropertyChanged(nameof(OzelDagilim)); }
    partial void OnSeciliOdemeChanged(AylikGiderSatiri? value) { OdemeOnay = false; OnPropertyChanged(nameof(OdemeEtkisi)); OnPropertyChanged(nameof(OdemeSecili)); }
    partial void OnOdemeTarihiChanged(DateTime value) => OdemeOnay = false;
    partial void OnOdemeNotuChanged(string value) => OdemeOnay = false;
    partial void OnAyTarihiChanged(DateTime value) { SeciliOdeme = null; OnPropertyChanged(nameof(AySecimiDegisti)); }
    public bool AySecimiDegisti => _ayVerisi is null || _ayVerisi.Yil != AyTarihi.Year || _ayVerisi.Ay != AyTarihi.Month;
    /// <summary>Ayın giderleri, şablonlar ve kanallar; hata son başarılı veriyi silmez, eski işaretler (tasarım 2026-10-02 §3).</summary>
    public Task YukleAsync()
    {
        SonVeriyiGoster();
        return VeriYukleAsync(async n =>
        {
            VeriHazir = false;
            var ay = AyTarihi;
            var s = await api.AylikGiderSablonlariAsync();
            var k = await finans.KanallarAsync();
            var a = await api.AylikGiderlerAsync(ay.Year, ay.Month);
            if (!Gecerli(n) || ay.Year != AyTarihi.Year || ay.Month != AyTarihi.Month)
                return;
            Yansit(s, k, a);
            Tamamlandi($"{ay:yyyy-MM}", (s, k, a));
        });
    }

    /// <summary>Son veri önbelleğinden (yeniden kurulan sayfa, H-1) seçili ayın son başarılı verisi gösterilir.</summary>
    public override bool SonVeriyiGoster()
        => OnbellektenUygula<(IReadOnlyList<AylikGiderSablonDto>, IReadOnlyList<KanalDto>, AylikGiderAyDto)>($"{AyTarihi:yyyy-MM}",
            v => Yansit(v.Item1, v.Item2, v.Item3));

    private void Yansit(IReadOnlyList<AylikGiderSablonDto> s, IReadOnlyList<KanalDto> k, AylikGiderAyDto a)
    {
        TakipMetni.Doldur(Sablonlar, s.Select(x => new AylikSablonSatiri(x)));
        TakipMetni.Doldur(Kanallar, k);
        var secili = KanalSecimleri.Where(x => x.Secili).Select(x => x.Veri.Id).ToHashSet();
        TakipMetni.Doldur(KanalSecimleri, k.Select(x => new TakipKanalSecimi(x) { Secili = secili.Contains(x.Id) }));
        AyiYansit(a);
        if (!FormIzi.Acik)
            FormIzi.Ac();   // ilk yükleme: boş şablon formu kanal seçenekleriyle açıldı
    }
    private void AyiYansit(AylikGiderAyDto a)
    {
        _ayVerisi = a;
        TakipMetni.Doldur(Kayitlar, a.Kayitlar.Select(x => new AylikGiderSatiri(x)));
        TakipMetni.Doldur(Iptaller, (a.Iptaller ?? []).Select(x => new AylikGiderIptalSatiri(x)));
        OnPropertyChanged(nameof(IptalVar));
        AyOzeti = $"{a.Ay:00}.{a.Yil} · Planlanan {Bicim.Tl(a.PlanlananToplam)} ₺ · Ödenen {Bicim.Tl(a.OdenenToplam)} ₺";
        OnPropertyChanged(nameof(AySecimiDegisti));
        OdemeyiYenidenEsle();
    }

    /// <summary>Yenilemede açık ödeme formu korunur (Ö-3): seçili satır yenilenen listede hâlâ ödenmemişse ona yeniden eşlenir.
    /// Satır değişmediyse onay da kalır; değiştiyse (tutar, dağılım, sürüm) onay yeniden istenir; satır kalmadıysa ya da ödendiyse
    /// form kapanır.</summary>
    private void OdemeyiYenidenEsle()
    {
        if (SeciliOdeme is not { } secili)
            return;
        var yeni = Kayitlar.FirstOrDefault(x => x.Veri.SablonId == secili.Veri.SablonId && !x.OdendiMi);
        var onay = OdemeOnay && yeni is not null && TakipMetni.Ayni(yeni.Veri, secili.Veri);
        SeciliOdeme = yeni;
        OdemeOnay = onay;
    }
    public Task AyDegistirAsync(int fark) { if (Mesgul) return Task.CompletedTask; AyTarihi = AyTarihi.AddMonths(fark); return YukleAsync(); }
    /// <summary>"Yeni şablon": yazılmış şablon formu varsa önce onay sorulur (tasarım §2).</summary>
    [RelayCommand]
    private async Task YeniAsync()
    {
        if (await BirakilabilirAsync(FormIzi))
            YeniForm();
    }

    /// <summary>Boş yeni şablon formu; formun hataları kalkar.</summary>
    private void YeniForm()
    {
        _duzenlenen = null;
        Ad = "";
        Tutar = 0;
        Tur = null;
        DagilimTuru = null;
        OdemeGunu = 1;
        Aktif = true;
        GecerliAy = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        Paylar.Clear();
        foreach (var k in KanalSecimleri)
            k.Secili = false;
        OnPropertyChanged(nameof(SablonBasligi));
        SablonHatalari.Temizle();
        FormIzi.Ac();
    }

    /// <summary>"Şablonu düzenle": yazılmış şablon formu varsa önce onay sorulur.</summary>
    public async Task SablonSecAsync(AylikSablonSatiri satir)
    {
        if (await BirakilabilirAsync(FormIzi))
            SablonSec(satir);
    }

    public void SablonSec(AylikSablonSatiri satir)
    {
        if (!EditorMu || Mesgul)
            return;
        SablonHatalari.Temizle();
        _duzenlenen = satir.Veri;
        Ad = _duzenlenen.Ad;
        Tutar = _duzenlenen.Tutar;
        Tur = Turler.FirstOrDefault(t => t.Kod == _duzenlenen.Tur);
        DagilimTuru = DagilimTurleri.FirstOrDefault(t => t.Kod == _duzenlenen.DagilimTuru);
        OdemeGunu = _duzenlenen.OdemeGunu;
        Aktif = _duzenlenen.Aktif;
        var bugun = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        GecerliAy = (_duzenlenen.GecerliAy > bugun ? _duzenlenen.GecerliAy : bugun).ToDateTime(TimeOnly.MinValue);
        foreach (var k in KanalSecimleri)
            k.Secili = _duzenlenen.Dagilimlar.Any(p => p.KanalId == k.Veri.Id);
        Paylar.Clear();
        foreach (var p in _duzenlenen.Dagilimlar.Where(x => x.KanalId is not null))
            Paylar.Add(new(Kanallar.ToList()) { Kanal = Kanallar.FirstOrDefault(k => k.Id == p.KanalId), Tutar = p.Tutar });
        OnPropertyChanged(nameof(SablonBasligi));
        FormIzi.Ac();
    }
    public void PayEkle() => Paylar.Add(new(Kanallar.ToList()));
    /// <summary>Şablon formunun ön doğrulaması (tasarım §1): her kural kendi alanının altında; özel dağılımın tutar kuralları genel hatada.</summary>
    private bool SablonFormuGecerli()
    {
        var h = SablonHatalari;
        h.Denetle(!string.IsNullOrWhiteSpace(Ad), nameof(Ad), "Ad / açıklama boş olamaz.");
        h.Denetle(Tur is not null, nameof(Tur), "Gider türünü seçin.");
        h.Denetle(ParaAyristirici.GecerliMi(Tutar), nameof(Tutar), ParaAyristirici.GecersizMesaji);
        h.Denetle(Tutar > 0, nameof(Tutar), "Aylık tutar sıfırdan büyük olmalı.");
        h.Denetle(OdemeGunu is >= 1 and <= 31, nameof(OdemeGunu), "Ödeme günü 1 ile 31 arasında olmalı.");
        h.Denetle(DagilimTuru is not null, nameof(DagilimTuru), "Dağılım biçimini seçin.");
        h.Denetle(DagilimTuru?.Kod != DagilimBicimleri.Esit || KanalSecimleri.Any(k => k.Secili), nameof(KanalSecimleri), "Dağıtılacak kanalları seçin.");
        return !h.Var;
    }

    [RelayCommand]
    private Task SablonKaydetAsync() => FormIsleAsync(SablonHatalari, async n =>
    {
        if (!EditorMu || !SablonFormuGecerli())
            return;
        IReadOnlyList<KanalPayYaz> paylar = DagilimTuru!.Kod switch { DagilimBicimleri.Genel => Array.Empty<KanalPayYaz>(), DagilimBicimleri.Esit => KanalSecimleri.Where(k => k.Secili).Select(k => new KanalPayYaz(k.Veri.Id, 0)).ToList(), _ => TakipMetni.Paylar(Paylar) };
        if (DagilimTuru.Kod != DagilimBicimleri.Genel && paylar.Count == 0)
        { SablonHatalari.Genel = "Dağıtılacak kanalları seçin."; return; }
        if (DagilimTuru.Kod == DagilimBicimleri.Ozel && paylar.Sum(p => p.Tutar) != Tutar)
        { SablonHatalari.Genel = "Kanal paylarının toplamı gider tutarıyla aynı olmalıdır."; return; }
        var tur = Tur!;
        var g = new AylikGiderSablonYaz(Guid.Empty, _duzenlenen?.Surum ?? 0, Ad.Trim(), tur.Kod, Tutar, OdemeGunu, DagilimTuru.Kod, paylar, new(GecerliAy.Year, GecerliAy.Month, 1), Aktif);
        var id = _duzenlenen?.Id;
        g = g with { IstekId = _sablonKey.Al(new { id, g }) };
        var sonuc = await api.AylikGiderSablonKaydetAsync(id, g);
        if (!Gecerli(n))
            return;
        _sablonKey.Temizle();
        var eski = Sablonlar.FirstOrDefault(x => x.Veri.Id == sonuc.Id);
        if (eski is not null)
            Sablonlar[Sablonlar.IndexOf(eski)] = new(sonuc);
        else
            Sablonlar.Add(new(sonuc));
        YeniForm();
        Mesaj = "Şablon kaydedildi; ödeme ve kasa hareketi oluşturulmadı.";
        VeriHazir = false;
        var ay = AyTarihi;
        await KayittanSonraAyiYenileAsync(n, ay.Year, ay.Month, () => ay == AyTarihi);
    });

    /// <summary>Kayıt alındıktan sonra ayın yenilenmesi: okuma hatası kaydı geri almaz ve formun hatası değildir; sayfa başına okuma
    /// iletisiyle yazılır (kopukken yazılmaz, kabuk şeridi söyler).</summary>
    private async Task KayittanSonraAyiYenileAsync(int n, int yil, int ay, Func<bool> halaAyni)
    {
        try
        {
            var a = await api.AylikGiderlerAsync(yil, ay);
            if (Gecerli(n) && halaAyni())
            { SeciliOdeme = null; AyiYansit(a); Tamamlandi(); }
        }
        catch (Exception hata) when (Gecerli(n))
        {
            Yurutucu.OkumaHatasiniYaz(hata);
            VeriEski = SonGuncelleme is not null;
        }
    }
    public void OdemeSec(AylikGiderSatiri satir)
    {
        if (Mesgul || !EditorMu || AySecimiDegisti || satir.OdendiMi)
            return;
        if (!ListeGuncelMi())
            return;
        OdemeHatalari.Temizle();
        SeciliOdeme = satir;
        OdemeTarihi = DateTime.Today;
        OdemeNotu = "";
        OdemeOnay = false;
        OdemeSecIstendi?.Invoke(this, EventArgs.Empty);
    }
    [RelayCommand]
    private Task OdeAsync() => FormIsleAsync(OdemeHatalari, async n =>
    {
        if (!EditorMu || SeciliOdeme is null || _ayVerisi is null)
            return;
        if (AySecimiDegisti)
        { OdemeHatalari.Genel = "Ay seçimi değişti. Seçilen ayı gösterip ödemeyi yeniden seçin."; return; }
        if (!VeriHazir)
        { OdemeHatalari.Genel = ListeGuncelDegil; return; }
        if (!OdemeHatalari.Denetle(OdemeOnay, nameof(OdemeOnay), "Gösterilen ödeme tutarını ve kanal etkisini onaylayın."))
            return;
        var secili = SeciliOdeme.Veri;
        var ay = _ayVerisi;
        var g = new AylikGiderOdemeYaz(Guid.Empty, secili.SablonSurum, ay.Yil, ay.Ay, DateOnly.FromDateTime(OdemeTarihi), OdemeNotu.Trim());
        g = g with { IstekId = _odemeKey.Al(new { secili.SablonId, g }) };
        var s = await api.AylikGiderOdeAsync(secili.SablonId, g);
        if (!Gecerli(n))
            return;
        _odemeKey.Temizle();
        SeciliOdeme = null;
        Mesaj = "Nakit / havale ödemesi kaydedildi. Kasa etkisi bir kez işlendi.";
        OdemeSonucuGosterIstendi?.Invoke(this, EventArgs.Empty);
        VeriHazir = false;
        await KayittanSonraAyiYenileAsync(n, ay.Yil, ay.Ay, () => ay.Yil == AyTarihi.Year && ay.Ay == AyTarihi.Month);
    });
    public Task IptalAsync(AylikGiderSatiri satir, string aciklama, int onayOturumu) => YurutAsync(async n =>
    {
        if (!EditorMu || !Gecerli(onayOturumu) || satir.Veri.OdemeId is not { } id)
            return;
        if (!ListeGuncelMi())
            return;
        if (AySecimiDegisti || !Kayitlar.Any(x => TakipMetni.Ayni(x.Veri, satir.Veri)))
        { Hata = "Gösterilen aylık gider değişti. Listeyi yenileyip ödemeyi yeniden seçin."; return; }
        if (string.IsNullOrWhiteSpace(aciklama))
        { Hata = "İptal gerekçesi yazın."; return; }
        var g = new AylikGiderIptalYaz(Guid.Empty, aciklama.Trim());
        g = g with { IstekId = _iptalKey.Al(new { id, g }) };
        await api.AylikGiderIptalAsync(id, g);
        if (!Gecerli(n))
            return;
        _iptalKey.Temizle();
        SeciliOdeme = null;
        Mesaj = "Ödeme iptal edildi; gerekçesiyle iptal edilen ödemeler listesinde görünür.";
        OdemeSonucuGosterIstendi?.Invoke(this, EventArgs.Empty);
        VeriHazir = false;
        var ay = AyTarihi;
        var a = await api.AylikGiderlerAsync(ay.Year, ay.Month);
        if (Gecerli(n) && ay == AyTarihi)
        { AyiYansit(a); Tamamlandi(); }
    });
    protected override void OturumTemizle() { _ayVerisi = null; Kayitlar.Clear(); Iptaller.Clear(); OnPropertyChanged(nameof(IptalVar)); Sablonlar.Clear(); Kanallar.Clear(); KanalSecimleri.Clear(); SeciliOdeme = null; AyOzeti = OdemeNotu = ""; YeniForm(); OdemeHatalari.Temizle(); foreach (var k in new[] { _sablonKey, _odemeKey, _iptalKey }) k.Temizle(); }
}
public record AylikGiderSatiri(AylikGiderSatirDto Veri)
{
    /// <summary>Ayın ödemesi kaydedilmiş (sunucu durumu "Odendi"); ödenmemiş plan ödeme bekler.</summary>
    public bool OdendiMi => Veri.Durum == AylikGiderDurumlari.Odendi;
    public string Baslik => $"{Veri.Ad} · {Bicim.Tl(Veri.Tutar)} ₺ · " + (OdendiMi ? "Ödendi" : "Ödeme bekliyor");
    public string Ozet => $"Planlanan {Veri.PlanlananTarih:dd.MM.yyyy}" + (Veri.OdemeTarihi is { } t ? $" · ödeme {t:dd.MM.yyyy}" : "") + "\n" + (Veri.DagilimTuru == DagilimBicimleri.Genel ? "Yalnız genel kasa" : TakipMetni.Paylar(Veri.Dagilimlar));
}
/// <summary>İptal edilmiş aylık gider ödemesi (salt okunur): ödeme tarihi, iptal anı (sürüm öncesi iptalde bilinmez) ve gerekçe.</summary>
public record AylikGiderIptalSatiri(AylikGiderSatirDto Veri)
{
    public string Baslik => $"{Veri.Ad} · {Bicim.Tl(Veri.Tutar)} ₺ · iptal edildi";
    public string Ozet => (Veri.OdemeTarihi is { } t ? $"Ödeme {t:dd.MM.yyyy} · " : "")
        + (Veri.IptalZamani is { } z ? $"iptal {z.LocalDateTime:dd.MM.yyyy HH:mm}" : "iptal zamanı bilinmiyor (sürüm öncesi)")
        + $"\nGerekçe: {Veri.IptalAciklamasi ?? "—"}";
}
public record AylikSablonSatiri(AylikGiderSablonDto Veri)
{
    public string Baslik => Veri.Ad + (Veri.Aktif ? "" : " · arşiv");
    public string Ozet => $"{Bicim.Tl(Veri.Tutar)} ₺ · her ayın {Veri.OdemeGunu}. günü · geçerlilik {Veri.GecerliAy:MM.yyyy}\n" + (Veri.DagilimTuru == DagilimBicimleri.Genel ? "Yalnız genel kasa" : TakipMetni.Paylar(Veri.Dagilimlar));
}
