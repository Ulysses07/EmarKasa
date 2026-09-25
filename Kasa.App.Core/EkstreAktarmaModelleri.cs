using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public record EkstreSecenek(string Kod, string Ad);
public record EkstreYuklemeSecimi(int Oturum, string Kaynak, string Banka, string HesapAdi, int? KartId);

public partial class EkstreSatirEditor : ObservableObject
{
    private readonly Action _degisti;
    public EkstreOkunanSatir Kaynak { get; }
    public bool Kayitli { get; }
    public bool Secilebilir => !Kayitli && Kaynak.ParaBirimi is "TRY" or "TL" or "Belirsiz";
    public string KaynakMetni => $"Sayfa {Kaynak.Sayfa} · satır {Kaynak.No} · {Kaynak.Yon} · {Kaynak.ParaBirimi}\n{Kaynak.KaynakSatir}";
    public string Uyarilar => string.Join("\n", Kaynak.Uyarilar.Concat(Kayitli ? ["Bu satır zaten kaydedildi. Yeniden kaydetmek için önce kaydını iptal edin."] : []).Concat(!Secilebilir && !Kayitli ? ["Yalnız TL hareketleri kaydedilebilir."] : []));
    public string Ozet => $"#{Kaynak.No} · {TarihMetni} · {Aciklama} · {TutarMetni} {Kaynak.ParaBirimi}";
    public IReadOnlyList<EkstreSecenek> IslemTurleri { get; }
    public IReadOnlyList<EkstreSecenek> DagilimTurleri { get; } = [new("Genel", "Yalnız genel kasa"), new("Esit", "Seçilen kanallara eşit"), new("Ozel", "Özel kanal tutarları"), new("Otomatik", "Kartın kayıtlı dağılımı")];
    public IReadOnlyList<KartTakipDto> Kartlar { get; }
    public IReadOnlyList<KanalDto> Kanallar { get; }
    public ObservableCollection<TakipPayEditor> Paylar { get; } = new();
    public ObservableCollection<HarcamaSatiri> KaynakHarcamalar { get; } = new();
    [ObservableProperty] private bool _secili;
    [ObservableProperty] private string _tarihMetni = "";
    [ObservableProperty] private string _aciklama = "";
    [ObservableProperty] private string _tutarMetni = "";
    [ObservableProperty] private EkstreSecenek? _islemTuru;
    [ObservableProperty] private EkstreSecenek? _dagilimTuru;
    [ObservableProperty] private KartTakipDto? _kart;
    [ObservableProperty] private HarcamaSatiri? _kaynakHarcama;
    public bool KartSecimiGorunur { get; }
    public bool IadeMi => IslemTuru?.Kod == "KartIade";

    public EkstreSatirEditor(EkstreOkunanSatir kaynak, EkstreBelgeDto belge, IReadOnlyList<KanalDto> kanallar, IReadOnlyList<KartTakipDto> kartlar, Action degisti)
    {
        _degisti = () => { }; Kaynak = kaynak; Kayitli = belge.Kayitlar.Any(k => k.SatirNo == kaynak.No && !k.Iptal);
        Kanallar = kanallar; Kartlar = kartlar; KartSecimiGorunur = belge.Kaynak == "Banka";
        IslemTurleri = belge.Kaynak == "Kart" ? [new("KartHarcama", "Kart harcaması"), new("KartIade", "Kart iadesi"), new("KartOdemesi", "Karta ödeme")] : [new("Gelir", "Gelir"), new("Gider", "Gider"), new("KartOdemesi", "Karta ödeme")];
        TarihMetni = kaynak.Tarih?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        Aciklama = kaynak.Aciklama; TutarMetni = kaynak.Tutar?.ToString("0.00", CultureInfo.GetCultureInfo("tr-TR")) ?? "";
        IslemTuru = IslemTurleri.FirstOrDefault(t => t.Kod == kaynak.OnerilenIslem);
        Kart = kartlar.FirstOrDefault(k => k.Id == belge.KartId);
        Paylar.CollectionChanged += (_, e) => { if (e.NewItems is not null) foreach (TakipPayEditor p in e.NewItems) p.PropertyChanged += PayDegisti; if (e.OldItems is not null) foreach (TakipPayEditor p in e.OldItems) p.PropertyChanged -= PayDegisti; _degisti(); };
        _degisti = degisti;
    }
    private void PayDegisti(object? sender, PropertyChangedEventArgs e) => _degisti();
    public void PayEkle() => Paylar.Add(new(Kanallar));
    partial void OnIslemTuruChanged(EkstreSecenek? value)
    {
        DagilimTuru = value?.Kod is "KartOdemesi" or "KartIade" ? DagilimTurleri.Single(x => x.Kod == "Otomatik") : value?.Kod == "KartHarcama" ? DagilimTurleri.Single(x => x.Kod == "Esit") : null;
        KaynakHarcama = null; OnPropertyChanged(nameof(IadeMi));
    }
    partial void OnKartChanged(KartTakipDto? value)
    {
        KaynakHarcama = null; TakipMetni.Doldur(KaynakHarcamalar, value?.Harcamalar.Where(h => h.Tutar > 0 && !h.Iptal).Select(h => new HarcamaSatiri(h)) ?? []);
    }
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(TarihMetni) or nameof(Aciklama) or nameof(TutarMetni)) base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Ozet)));
        _degisti?.Invoke();
    }
    public EkstreSatirYaz Yaz()
    {
        void Hata(string s) => throw new KasaApiException(HttpStatusCode.BadRequest, $"Satır {Kaynak.No}: {s}");
        if (!Secilebilir) Hata(Kayitli ? "Bu satır zaten kayıtlı." : "Yalnız TL hareketleri kaydedilebilir.");
        if (!DateOnly.TryParseExact(TarihMetni.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tarih)) Hata("Tarihi yıl-ay-gün biçiminde girin (2026-09-27).");
        var metin = TutarMetni.Trim();
        if (!decimal.TryParse(metin, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.GetCultureInfo("tr-TR"), out var tutar) && !decimal.TryParse(metin, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out tutar)) Hata("Geçerli bir tutar girin; binlik ayırıcı kullanmayın.");
        if (tutar <= 0 || decimal.Round(tutar, 2) != tutar) Hata("Tutar pozitif ve kuruş hassasiyetinde olmalı.");
        if (string.IsNullOrWhiteSpace(Aciklama)) Hata("Açıklamayı doldurun.");
        if (IslemTuru is null || !IslemTurleri.Contains(IslemTuru)) Hata("İşlem türünü seçin.");
        var tur = IslemTuru!.Kod; var dagilim = DagilimTuru?.Kod;
        IReadOnlyList<KanalPayYaz> paylar = [];
        if (tur is "KartOdemesi" or "KartIade") dagilim = "Otomatik";
        else if (dagilim == "Ozel") { paylar = TakipMetni.Paylar(Paylar); if (paylar.Sum(x => x.Tutar) != tutar) Hata("Kanal tutarları hareket tutarına eşit olmalı."); }
        else if (dagilim == "Esit") { if (Paylar.Count == 0 || Paylar.Any(p => p.Kanal is null) || Paylar.Select(p => p.Kanal!.Id).Distinct().Count() != Paylar.Count) Hata("Dağıtılacak kanalları birer kez seçin."); paylar = Paylar.Select(p => new KanalPayYaz(p.Kanal!.Id, 0)).ToList(); }
        else if (dagilim != "Genel" || tur == "KartHarcama") Hata("Bu hareket için kanal dağılımını seçin.");
        if (tur is "KartHarcama" or "KartIade" or "KartOdemesi" && Kart is null) Hata("Yeni takibe alınmış kartı seçin.");
        if (tur == "KartIade" && (KaynakHarcama is null || !KaynakHarcamalar.Contains(KaynakHarcama))) Hata("İadenin kaynak harcamasını seçin.");
        return new(Kaynak.No, tarih, Aciklama.Trim(), tutar, tur, dagilim!, paylar, tur is "KartHarcama" or "KartIade" or "KartOdemesi" ? Kart?.Id : null, tur == "KartIade" ? KaynakHarcama?.Veri.Id : null);
    }
}

public record EkstreGecmisSatiri(EkstreBelgeOzetDto Veri)
{
    public string Baslik => $"{Veri.Yuklendi.LocalDateTime:dd.MM.yyyy HH:mm} · {Veri.DosyaAdi}";
    public string Ozet => $"{Veri.Banka} · {Veri.Kaynak} · {Veri.HesapAdi} · {Veri.SatirSayisi} satır · {Veri.KayitSayisi} kayıt";
}
public record EkstreKayitSatiri(EkstreKayitDto Veri)
{
    public string Baslik => $"Satır {Veri.SatirNo} · {Veri.Tarih:dd.MM.yyyy} · {Veri.Aciklama} · {Bicim.Tl(Veri.Tutar)} ₺";
    public string Ozet => $"{Veri.IslemTuru} · {(Veri.Iptal ? "İptal edildi" : "Kaydedildi")} · {(Veri.DagilimTuru == "Genel" ? "Yalnız genel kasa" : TakipMetni.Paylar(Veri.Dagilimlar))}";
}
