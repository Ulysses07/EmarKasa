using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public record GiderSecimi(string Kod, string Ad);
public partial class AylikGiderViewModel(IAylikGiderApi api, IKasaApi finans, AuthViewModel auth) : OturumluViewModel(auth)
{
    private readonly TekrarAnahtari _sablonKey = new(), _odemeKey = new(), _iptalKey = new();
    private AylikGiderAyDto? _ayVerisi;
    private AylikGiderSablonDto? _duzenlenen;
    public IReadOnlyList<GiderSecimi> Turler { get; } = new[] { new GiderSecimi("Kira", "Kira"), new("Maas", "Maaş"), new("Fatura", "Fatura"), new("Diger", "Diğer") };
    public IReadOnlyList<GiderSecimi> DagilimTurleri { get; } = new[] { new GiderSecimi("Genel", "Yalnız genel kasa"), new("Esit", "Seçilen kanallara eşit"), new("Ozel", "Kanallara tutar girerek") };
    public ObservableCollection<AylikGiderSatiri> Kayitlar { get; } = new();
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
    public bool EsitDagilim => DagilimTuru?.Kod == "Esit";
    public bool OzelDagilim => DagilimTuru?.Kod == "Ozel";
    public bool OdemeSecili => SeciliOdeme is not null;
    public string OdemeEtkisi => SeciliOdeme is { } s ? $"{s.Baslik}\nGenel kasadan {Bicim.Tl(s.Veri.Tutar)} ₺ çıkar.\n" + (s.Veri.DagilimTuru == "Genel" ? "Kanal bakiyeleri değişmez." : TakipMetni.Paylar(s.Veri.Dagilimlar)) : "Ödenecek aylık gideri seçin.";
    public string SablonBasligi => _duzenlenen is null ? "Yeni aylık gider şablonu" : $"Şablonu düzenle: {_duzenlenen.Ad}";
    partial void OnDagilimTuruChanged(GiderSecimi? value) { OnPropertyChanged(nameof(EsitDagilim)); OnPropertyChanged(nameof(OzelDagilim)); }
    partial void OnSeciliOdemeChanged(AylikGiderSatiri? value) { OdemeOnay = false; OnPropertyChanged(nameof(OdemeEtkisi)); OnPropertyChanged(nameof(OdemeSecili)); }
    partial void OnOdemeTarihiChanged(DateTime value) => OdemeOnay = false;
    partial void OnOdemeNotuChanged(string value) => OdemeOnay = false;
    partial void OnAyTarihiChanged(DateTime value) { SeciliOdeme = null; OnPropertyChanged(nameof(AySecimiDegisti)); }
    public bool AySecimiDegisti => _ayVerisi is null || _ayVerisi.Yil != AyTarihi.Year || _ayVerisi.Ay != AyTarihi.Month;
    public Task YukleAsync() => YurutAsync(async n =>
    {
        VeriHazir = false; SeciliOdeme = null; var ay = AyTarihi;
        var s = await api.AylikGiderSablonlariAsync(); var k = await finans.KanallarAsync(); var a = await api.AylikGiderlerAsync(ay.Year, ay.Month);
        if (!Gecerli(n) || ay.Year != AyTarihi.Year || ay.Month != AyTarihi.Month) return;
        TakipMetni.Doldur(Sablonlar, s.Select(x => new AylikSablonSatiri(x))); TakipMetni.Doldur(Kanallar, k);
        var secili = KanalSecimleri.Where(x => x.Secili).Select(x => x.Veri.Id).ToHashSet();
        TakipMetni.Doldur(KanalSecimleri, k.Select(x => new TakipKanalSecimi(x) { Secili = secili.Contains(x.Id) }));
        AyiYansit(a); Tamamlandi();
    });
    private void AyiYansit(AylikGiderAyDto a)
    {
        _ayVerisi = a; TakipMetni.Doldur(Kayitlar, a.Kayitlar.Select(x => new AylikGiderSatiri(x)));
        AyOzeti = $"{a.Ay:00}.{a.Yil} · Planlanan {Bicim.Tl(a.PlanlananToplam)} ₺ · Ödenen {Bicim.Tl(a.OdenenToplam)} ₺";
        OnPropertyChanged(nameof(AySecimiDegisti));
    }
    public Task AyDegistirAsync(int fark) { if (Mesgul) return Task.CompletedTask; AyTarihi = AyTarihi.AddMonths(fark); return YukleAsync(); }
    [RelayCommand] private void Yeni() { _duzenlenen = null; Ad = ""; Tutar = 0; Tur = null; DagilimTuru = null; OdemeGunu = 1; Aktif = true; GecerliAy = new(DateTime.Today.Year, DateTime.Today.Month, 1); Paylar.Clear(); foreach (var k in KanalSecimleri) k.Secili = false; OnPropertyChanged(nameof(SablonBasligi)); }
    public void SablonSec(AylikSablonSatiri satir)
    {
        if (!EditorMu || Mesgul) return;
        _duzenlenen = satir.Veri; Ad = _duzenlenen.Ad; Tutar = _duzenlenen.Tutar; Tur = Turler.FirstOrDefault(t => t.Kod == _duzenlenen.Tur); DagilimTuru = DagilimTurleri.FirstOrDefault(t => t.Kod == _duzenlenen.DagilimTuru); OdemeGunu = _duzenlenen.OdemeGunu; Aktif = _duzenlenen.Aktif;
        var bugun = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1); GecerliAy = (_duzenlenen.GecerliAy > bugun ? _duzenlenen.GecerliAy : bugun).ToDateTime(TimeOnly.MinValue);
        foreach (var k in KanalSecimleri) k.Secili = _duzenlenen.Dagilimlar.Any(p => p.KanalId == k.Veri.Id);
        Paylar.Clear(); foreach (var p in _duzenlenen.Dagilimlar.Where(x => x.KanalId is not null)) Paylar.Add(new(Kanallar.ToList()) { Kanal = Kanallar.FirstOrDefault(k => k.Id == p.KanalId), Tutar = p.Tutar });
        OnPropertyChanged(nameof(SablonBasligi));
    }
    public void PayEkle() => Paylar.Add(new(Kanallar.ToList()));
    [RelayCommand] private Task SablonKaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu) return;
        if (string.IsNullOrWhiteSpace(Ad) || Tur is null || DagilimTuru is null || Tutar <= 0 || OdemeGunu is < 1 or > 31) { Hata = "Ad, tür, pozitif tutar, ödeme günü ve dağılım biçimini seçin."; return; }
        IReadOnlyList<KanalPayYaz> paylar = DagilimTuru.Kod switch { "Genel" => Array.Empty<KanalPayYaz>(), "Esit" => KanalSecimleri.Where(k => k.Secili).Select(k => new KanalPayYaz(k.Veri.Id, 0)).ToList(), _ => TakipMetni.Paylar(Paylar) };
        if (DagilimTuru.Kod != "Genel" && paylar.Count == 0) { Hata = "Dağıtılacak kanalları seçin."; return; }
        if (DagilimTuru.Kod == "Ozel" && paylar.Sum(p => p.Tutar) != Tutar) { Hata = "Kanal paylarının toplamı gider tutarıyla aynı olmalıdır."; return; }
        var g = new AylikGiderSablonYaz(Guid.Empty, _duzenlenen?.Surum ?? 0, Ad.Trim(), Tur.Kod, Tutar, OdemeGunu, DagilimTuru.Kod, paylar, new(GecerliAy.Year, GecerliAy.Month, 1), Aktif); var id = _duzenlenen?.Id; g = g with { IstekId = _sablonKey.Al(new { id, g }) };
        var sonuc = await api.AylikGiderSablonKaydetAsync(id, g); if (!Gecerli(n)) return;
        _sablonKey.Temizle(); var eski = Sablonlar.FirstOrDefault(x => x.Veri.Id == sonuc.Id); if (eski is not null) Sablonlar[Sablonlar.IndexOf(eski)] = new(sonuc); else Sablonlar.Add(new(sonuc));
        Yeni(); Mesaj = "Şablon kaydedildi; ödeme ve kasa hareketi oluşturulmadı.";
        VeriHazir = false;
        var ay = AyTarihi; var a = await api.AylikGiderlerAsync(ay.Year, ay.Month); if (Gecerli(n) && ay == AyTarihi) { SeciliOdeme = null; AyiYansit(a); Tamamlandi(); }
    });
    public void OdemeSec(AylikGiderSatiri satir) { if (Mesgul || !EditorMu || AySecimiDegisti || satir.Veri.Durum == "Odendi") return; SeciliOdeme = satir; OdemeTarihi = DateTime.Today; OdemeNotu = ""; OdemeOnay = false; }
    [RelayCommand] private Task OdeAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || SeciliOdeme is null || _ayVerisi is null) return;
        if (AySecimiDegisti || !OdemeOnay) { Hata = "Ayı yenileyin ve gösterilen ödeme tutarı ile kanal etkisini onaylayın."; return; }
        var secili = SeciliOdeme.Veri; var ay = _ayVerisi;
        var g = new AylikGiderOdemeYaz(Guid.Empty, secili.SablonSurum, ay.Yil, ay.Ay, DateOnly.FromDateTime(OdemeTarihi), OdemeNotu.Trim()); g = g with { IstekId = _odemeKey.Al(new { secili.SablonId, g }) };
        var s = await api.AylikGiderOdeAsync(secili.SablonId, g); if (!Gecerli(n)) return;
        _odemeKey.Temizle(); SeciliOdeme = null; Mesaj = "Nakit / havale ödemesi kaydedildi. Kasa etkisi bir kez işlendi.";
        VeriHazir = false;
        var a = await api.AylikGiderlerAsync(ay.Yil, ay.Ay); if (Gecerli(n) && ay.Yil == AyTarihi.Year && ay.Ay == AyTarihi.Month) { AyiYansit(a); Tamamlandi(); }
    });
    public Task IptalAsync(AylikGiderSatiri satir, string aciklama, int onayOturumu) => YurutAsync(async n =>
    {
        if (!EditorMu || !Gecerli(onayOturumu) || satir.Veri.OdemeId is not { } id) return;
        if (!VeriHazir || AySecimiDegisti || !Kayitlar.Any(x => TakipMetni.Ayni(x.Veri, satir.Veri))) { Hata = "Gösterilen aylık gider değişti. Listeyi yenileyip ödemeyi yeniden seçin."; return; }
        if (string.IsNullOrWhiteSpace(aciklama)) { Hata = "İptal gerekçesi yazın."; return; }
        var g = new AylikGiderIptalYaz(Guid.Empty, aciklama.Trim()); g = g with { IstekId = _iptalKey.Al(new { id, g }) };
        await api.AylikGiderIptalAsync(id, g); if (!Gecerli(n)) return;
        _iptalKey.Temizle(); SeciliOdeme = null; Mesaj = "Ödeme iptal edildi; geçmiş izi korundu.";
        VeriHazir = false;
        var ay = AyTarihi; var a = await api.AylikGiderlerAsync(ay.Year, ay.Month); if (Gecerli(n) && ay == AyTarihi) { AyiYansit(a); Tamamlandi(); }
    });
    protected override void OturumTemizle() { _ayVerisi = null; Kayitlar.Clear(); Sablonlar.Clear(); Kanallar.Clear(); KanalSecimleri.Clear(); SeciliOdeme = null; AyOzeti = OdemeNotu = ""; Yeni(); foreach (var k in new[] { _sablonKey, _odemeKey, _iptalKey }) k.Temizle(); }
}
public record AylikGiderSatiri(AylikGiderSatirDto Veri)
{
    public string Baslik => $"{Veri.Ad} · {Bicim.Tl(Veri.Tutar)} ₺ · " + (Veri.Durum == "Odendi" ? "Ödendi" : "Ödeme bekliyor");
    public string Ozet => $"Planlanan {Veri.PlanlananTarih:dd.MM.yyyy}" + (Veri.OdemeTarihi is { } t ? $" · ödeme {t:dd.MM.yyyy}" : "") + "\n" + (Veri.DagilimTuru == "Genel" ? "Yalnız genel kasa" : TakipMetni.Paylar(Veri.Dagilimlar));
}
public record AylikSablonSatiri(AylikGiderSablonDto Veri)
{
    public string Baslik => Veri.Ad + (Veri.Aktif ? "" : " · arşiv");
    public string Ozet => $"{Bicim.Tl(Veri.Tutar)} ₺ · her ayın {Veri.OdemeGunu}. günü · geçerlilik {Veri.GecerliAy:MM.yyyy}\n" + (Veri.DagilimTuru == "Genel" ? "Yalnız genel kasa" : TakipMetni.Paylar(Veri.Dagilimlar));
}
