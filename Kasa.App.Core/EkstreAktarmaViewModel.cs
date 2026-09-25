using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class EkstreAktarmaViewModel(IEkstreAktarmaApi api, IKasaApi finans, IFinansTakipApi takip, AuthViewModel auth) : OturumluViewModel(auth)
{
    private readonly TekrarAnahtari _kayitKey = new(), _iptalKey = new();
    private int _formSurumu;
    private EkstreKaydetYaz? _onizlemeGirdi;
    private EkstreOnizlemeDto? _onizleme;
    private int _onizlemeForm;
    private int? _gecmisImleci;
    public IReadOnlyList<EkstreSecenek> Kaynaklar { get; } = [new("Kart", "Kredi kartı ekstresi"), new("Banka", "Banka hesap hareketleri")];
    public IReadOnlyList<EkstreSecenek> Bankalar { get; } = [new("Vakifbank", "VakıfBank"), new("Akbank", "Akbank"), new("QNB", "QNB"), new("Isbank", "İş Bankası"), new("Garanti", "Garanti BBVA"), new("Denizbank", "DenizBank")];
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
    [ObservableProperty] private bool _eskiBelgeVar;
    public bool BankaMi => Kaynak?.Kod == "Banka";
    public bool KartMi => Kaynak?.Kod == "Kart";
    public bool BelgeVar => Belge is not null;
    public bool OnizlemeVar => _onizleme is not null;
    public bool TekrarOnayGerekli => _onizleme?.TekrarOnayGerekli ?? false;
    public string BelgeOzeti => Belge is { } b ? $"{b.DosyaAdi} · {b.Banka} · {b.Kaynak} · {(b.KartId is { } id ? (Kartlar.FirstOrDefault(k => k.Id == id)?.Ad ?? "Kart") + $" (#{id})" : b.HesapAdi)}\n{b.Satirlar.Count} satır; {Satirlar.Count(s => s.Secili)} seçildi. Hiçbir satır kendiliğinden kaydedilmez.\n" + string.Join("\n", b.Uyarilar) : "";
    partial void OnKaynakChanged(EkstreSecenek? value) { OnPropertyChanged(nameof(BankaMi)); OnPropertyChanged(nameof(KartMi)); }
    partial void OnBelgeChanged(EkstreBelgeDto? value) { OnPropertyChanged(nameof(BelgeVar)); OnPropertyChanged(nameof(BelgeOzeti)); }
    private void GirdiDegisti()
    {
        _formSurumu++; _onizleme = null; _onizlemeGirdi = null; OnizlemeMetni = ""; Onay = false; TekrarOnay = false;
        OnPropertyChanged(nameof(OnizlemeVar)); OnPropertyChanged(nameof(TekrarOnayGerekli)); OnPropertyChanged(nameof(BelgeOzeti));
    }
    protected override void OturumTemizle()
    {
        GirdiDegisti(); Belge = null; SeciliSatir = null; Kaynak = null; Banka = null; Kart = null; HesapAdi = "";
        Satirlar.Clear(); Kayitlar.Clear(); Gecmis.Clear(); Kartlar.Clear(); Kanallar.Clear(); _kayitKey.Temizle(); _iptalKey.Temizle();
        _gecmisImleci = null; EskiBelgeVar = false;
    }
    public Task YukleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu) return;
        GirdiDegisti(); var id = Belge?.Id;
        var gecmis = await api.EkstreBelgelerAsync(); var kartlar = await takip.TakipKartlarAsync(); var kanallar = await finans.KanallarAsync();
        var belge = id is { } i ? await api.EkstreBelgeAsync(i) : null;
        if (!Gecerli(n) || !EditorMu) return;
        TakipMetni.Doldur(Gecmis, gecmis.Select(x => new EkstreGecmisSatiri(x)));
        _gecmisImleci = gecmis.Count > 0 ? gecmis.Min(g => g.Id) : null; EskiBelgeVar = gecmis.Count == 50;
        TakipMetni.Doldur(Kartlar, kartlar.Where(k => k.YeniTakip)); TakipMetni.Doldur(Kanallar, kanallar.Where(k => k.Aktif).OrderBy(k => k.Sira));
        if (belge is not null) BelgeyiYansit(belge); Tamamlandi();
    });
    public EkstreYuklemeSecimi? YuklemeSecimi()
    {
        if (!EditorMu || !VeriHazir || Mesgul) return null;
        if (Kaynak is null || Banka is null || (BankaMi && (string.IsNullOrWhiteSpace(HesapAdi) || HesapAdi.Trim().Length > 100)) || (KartMi && Kart is null)) { Hata = "Belge türünü, bankayı ve kartı / kısa hesap adını seçin."; return null; }
        return new(OturumNesli, Kaynak.Kod, Banka.Kod, BankaMi ? HesapAdi.Trim() : "", KartMi ? Kart!.Id : null);
    }
    public Task PdfYukleAsync(byte[] icerik, string ad, EkstreYuklemeSecimi secim) => YurutAsync(async n =>
    {
        if (!EditorMu || !VeriHazir || secim.Oturum != n || secim.Kaynak != Kaynak?.Kod || secim.Banka != Banka?.Kod || secim.HesapAdi != (BankaMi ? HesapAdi.Trim() : "") || secim.KartId != (KartMi ? Kart?.Id : null)) { Hata = "Dosya seçimi sırasında oturum veya belge kaynağı değişti. PDF'yi yeniden seçin."; return; }
        if (!ad.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || icerik.Length is 0 or > 10 * 1024 * 1024) { Hata = "En fazla 10 MB büyüklüğünde bir PDF seçin."; return; }
        var b = await api.EkstreYukleAsync(icerik, ad, secim.Kaynak, secim.Banka, secim.HesapAdi, secim.KartId);
        if (!Gecerli(n)) return; BelgeyiYansit(b); Tamamlandi(); Mesaj = "PDF okundu. Kaydetmek istediğiniz satırları tek tek seçip kontrol edin.";
    });
    public Task BelgeAcAsync(int id) => YurutAsync(async n =>
    {
        if (!EditorMu || !VeriHazir) return; GirdiDegisti();
        var b = await api.EkstreBelgeAsync(id); if (!Gecerli(n)) return; BelgeyiYansit(b);
    });
    public Task KaynakAcAsync(int kayitId) => YurutAsync(async n =>
    {
        if (!EditorMu || !VeriHazir) return; GirdiDegisti();
        var b = await api.EkstreKaynakBelgeAsync(kayitId); if (!Gecerli(n)) return; BelgeyiYansit(b);
    });
    [RelayCommand] private Task EskiBelgeleriYukleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || !VeriHazir || !EskiBelgeVar || _gecmisImleci is not { } id) return;
        var eski = await api.EkstreBelgelerAsync(id); if (!Gecerli(n)) return;
        foreach (var b in eski) if (!Gecmis.Any(g => g.Veri.Id == b.Id)) Gecmis.Add(new(b));
        _gecmisImleci = eski.Count > 0 ? eski.Min(g => g.Id) : null; EskiBelgeVar = eski.Count == 50;
    });
    private void BelgeyiYansit(EkstreBelgeDto b)
    {
        GirdiDegisti(); SeciliSatir = null; Belge = b;
        TakipMetni.Doldur(Satirlar, b.Satirlar.Select(s => new EkstreSatirEditor(s, b, Kanallar.ToArray(), Kartlar.ToArray(), GirdiDegisti)));
        TakipMetni.Doldur(Kayitlar, b.Kayitlar.Select(k => new EkstreKayitSatiri(k)));
        var eski = Gecmis.FirstOrDefault(g => g.Veri.Id == b.Id); if (eski is not null) Gecmis.Remove(eski);
        Gecmis.Insert(0, new(new(b.Id, b.Surum, b.Kaynak, b.Banka, b.HesapAdi, b.KartId, b.DosyaAdi, b.Yuklendi, b.Satirlar.Count, b.Kayitlar.Count(k => !k.Iptal))));
        OnPropertyChanged(nameof(BelgeOzeti));
    }
    private EkstreKaydetYaz Girdi()
    {
        if (Belge is null || !Satirlar.Any(s => s.Secili)) throw new KasaApiException(HttpStatusCode.BadRequest, "Kaydedilecek en az bir satırı seçin.");
        var g = new EkstreKaydetYaz(Guid.Empty, Belge.Surum, Satirlar.Where(s => s.Secili).Select(s => s.Yaz()).ToArray());
        return g with { IstekId = _kayitKey.Al(new { Belge.Id, g }) };
    }
    [RelayCommand] private Task OnizleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || !VeriHazir || Belge is null) return;
        var id = Belge.Id; var rev = _formSurumu; var g = Girdi();
        var p = await api.EkstreOnizlemeAsync(id, g);
        if (!Gecerli(n) || rev != _formSurumu || Belge?.Id != id) return;
        _onizleme = p; _onizlemeGirdi = g; _onizlemeForm = rev; Onay = false; TekrarOnay = false;
        OnizlemeMetni = $"{p.Satirlar.Count} satır kaydedilecek. Genel kasa etkisi: {Bicim.Tl(p.KasaEtkisi)} ₺\n" + string.Join("\n", p.Uyarilar) + "\n\n" + string.Join("\n\n", p.Satirlar.Select(s => $"Satır {s.SatirNo} · {s.Tarih:dd.MM.yyyy} · {s.Aciklama} · {Bicim.Tl(s.Tutar)} ₺ · {s.IslemTuru}\nKasa etkisi: {Bicim.Tl(s.KasaEtkisi)} ₺ · {(g.Satirlar.Single(x => x.SatirNo == s.SatirNo).DagilimTuru == "Genel" ? "Yalnız genel kasa" : TakipMetni.Paylar(s.Dagilimlar))}\n{string.Join("\n", s.Uyarilar)}"));
        OnPropertyChanged(nameof(OnizlemeVar)); OnPropertyChanged(nameof(TekrarOnayGerekli));
    });
    [RelayCommand] private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || !VeriHazir || Belge is null || _onizleme is null || _onizlemeGirdi is null || _formSurumu != _onizlemeForm || !Onay || (TekrarOnayGerekli && !TekrarOnay)) { Hata = "Önizlemeyi kontrol edin ve gerekli onayları işaretleyin."; return; }
        var id = Belge.Id; var g = _onizlemeGirdi with { OnizlemeOzeti = _onizleme.OnizlemeOzeti, TekrarOnay = TekrarOnay };
        try { var b = await api.EkstreKaydetAsync(id, g); if (!Gecerli(n)) return; BelgeyiYansit(b); _kayitKey.Temizle(); Mesaj = "Seçilen satırlar kaydedildi. Diğer satırlar değişmedi."; }
        catch (KasaApiException e) when (e.DurumKodu == HttpStatusCode.Conflict) { if (Gecerli(n)) GirdiDegisti(); throw; }
    });
    public Task KayitIptalAsync(EkstreKayitSatiri satir, string aciklama, int onayOturumu, int belgeId) => YurutAsync(async n =>
    {
        if (!EditorMu || !VeriHazir || onayOturumu != n || Belge?.Id != belgeId || !Kayitlar.Any(k => ReferenceEquals(k, satir)) || satir.Veri.Iptal || string.IsNullOrWhiteSpace(aciklama)) { Hata = "Geçerli kaydı seçip gerekçeyi yeniden onaylayın."; return; }
        var g = new EkstreIptalYaz(_iptalKey.Al(new { belgeId, satir.Veri.Id, Aciklama = aciklama.Trim() }), aciklama.Trim());
        var b = await api.EkstreKayitIptalAsync(belgeId, satir.Veri.Id, g); if (!Gecerli(n)) return; BelgeyiYansit(b); _iptalKey.Temizle(); Mesaj = "Kaydın iptali işlendi; kaynak ve geçmiş korundu.";
    });
    public async Task<IndirilenDosya?> DosyaAsync()
    {
        IndirilenDosya? sonuc = null; await YurutAsync(async n => { if (!EditorMu || Belge is null) return; var id = Belge.Id; var s = await api.EkstreDosyaAsync(id); if (Gecerli(n) && Belge?.Id == id) sonuc = s; }); return sonuc;
    }
}
