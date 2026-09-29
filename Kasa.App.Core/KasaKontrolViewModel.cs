using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class KasaKontrolViewModel(IKasaKontrolApi api, AuthViewModel auth) : OturumluViewModel(auth)
{
    public const string NotZorunluMesaji = "Fark varsa açıklama girin.";
    private readonly TekrarAnahtari _anahtar = new();
    private readonly TekrarAnahtari _aciklamaAnahtari = new();
    private KasaKontrolOnizle? _girdi;
    private KasaKontrolOnizlemeDto? _onizleme;
    public ObservableCollection<KasaKontrolSatiri> Gecmis { get; } = new();
    [ObservableProperty] private decimal _gercekBakiye;
    [ObservableProperty] private string _not = "";
    [ObservableProperty] private string? _karsilastirma;
    [ObservableProperty] private string? _esikUyarilari;
    /// <summary>Kanal eşikleri ayrıca istenip yüklenemediyse hatası; bakiye karşılaştırma geçmişi yine gösterilir.</summary>
    [ObservableProperty] private string? _esikHatasi;
    [ObservableProperty] private string? _kayitUyarisi;
    // Boşaltılan alan 0 olur: 0 bakiyeyle kayıt ikinci basışta gider; bakiye ya da açıklama değişince onay sıfırlanır.
    private bool _sifirOnayi;
    partial void OnGercekBakiyeChanged(decimal value) => SifirOnayiniKaldir();
    partial void OnNotChanged(string value) => SifirOnayiniKaldir();
    private void SifirOnayiniKaldir() { _sifirOnayi = false; KayitUyarisi = null; }

    // "Bu kontrolden beri değişenler" ve fark açıklaması (gap-denetim-izi-gozlemlenebilirlik-3): geçmişten seçilen kontrol.
    [ObservableProperty] private KasaKontrolSatiri? _secili;
    [ObservableProperty] private string? _sonrasiOzeti;
    [ObservableProperty] private string _farkAciklamasi = "";
    public ObservableCollection<KasaKontrolDegisiklikSatiri> Degisiklikler { get; } = new();
    public ObservableCollection<KasaHareketiSatiri> SonrasiHareketler { get; } = new();

    // Kasa hareket dökümü: genel kasa ya da seçilen kanalın kasası (kanallar eşik listesinden).
    public ObservableCollection<DokumKasasi> DokumKasalari { get; } = new() { DokumKasasi.Genel };
    [ObservableProperty] private DokumKasasi? _dokumKasa = DokumKasasi.Genel;
    [ObservableProperty] private DateTime _dokumBaslangic = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _dokumBitis = DateTime.Today;
    [ObservableProperty] private string? _dokumOzeti;
    public ObservableCollection<KasaHareketiSatiri> DokumSatirlari { get; } = new();

    public Task YukleAsync() => YukleAsync(null);
    /// <param name="panelEsikleri">Panelle aynı anlık görüntüden gelen kanal eşikleri (ana sayfa yanıtı): uyarı ile bakiye
    /// çelişmez. Null ise (eski sunucu, birleşik ucun sunucu hatası ya da eşiksiz yanıtı, yeniden deneme) ayrıca istenir; o
    /// istek de başarısızsa hata <see cref="EsikHatasi"/>'nda kalır, geçmiş yine gösterilir.</param>
    public Task YukleAsync(IReadOnlyList<KasaEsikDto>? panelEsikleri) => YurutAsync(async n =>
    {
        VeriHazir = false;
        var gecmis = await api.KasaKontrolleriAsync();
        IReadOnlyList<KasaEsikDto>? esikler = panelEsikleri;
        string? esikHatasi = null;
        if (esikler is null)
        {
            try
            { esikler = await api.KasaEsikleriAsync(); }
            catch (Exception e) when (e is not OperationCanceledException) { esikHatasi = "Kanal uyarıları yüklenemedi: " + OkumaHataMesaji(e); }
        }
        if (!Gecerli(n))
            return;
        TakipMetni.Doldur(Gecmis, gecmis.Select(x => new KasaKontrolSatiri(x)));
        EsikHatasi = esikHatasi;
        EsikUyarilari = esikler is null ? null : string.Join("\n", esikler.Where(x => x.Etkin && x.EsikAltinda).Select(x => $"{x.Kanal}: bakiye {Bicim.Tl(x.Bakiye)} ₺ — alt limit {Bicim.Tl(x.Tutar)} ₺"));
        if (esikler is not null && string.IsNullOrEmpty(EsikUyarilari))
            EsikUyarilari = "Açık uyarılarda alt limitin altında kanal yok.";
        if (esikler is not null)
        {
            var secili = DokumKasa?.KanalId;
            TakipMetni.Doldur(DokumKasalari, esikler.Select(x => new DokumKasasi(x.KanalId, x.Kanal)).Prepend(DokumKasasi.Genel));
            DokumKasa = DokumKasalari.FirstOrDefault(k => k.KanalId == secili) ?? DokumKasasi.Genel;
        }
        _girdi = null;
        _onizleme = null;
        Karsilastirma = null;
        SeciliyiBirak();
        Tamamlandi();
    });
    private KasaKontrolOnizle Girdi() => new(GercekBakiye, Not.Trim());
    [RelayCommand]
    private Task OnizleAsync() => YurutAsync(async n =>
    {
        if (!EditorMu)
            return;
        _girdi = null;
        _onizleme = null;
        Karsilastirma = null;
        if (!ParaAyristirici.GecerliMi(GercekBakiye))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        var g = Girdi();
        var s = await api.KasaKontrolOnizleAsync(g);
        // Açıklama özete girmez (fark görüldükten sonra yazılabilir); önizleme yalnız bakiye değişince geçersizdir.
        if (!Gecerli(n) || g.GercekBakiye != GercekBakiye)
            return;
        _girdi = g;
        _onizleme = s;
        var satirlar = new List<string> { $"Sistemdeki genel kasa: {Bicim.Tl(s.SistemBakiye)} ₺", $"Gerçek bakiye: {Bicim.Tl(s.GercekBakiye)} ₺", $"Fark (gerçek − sistem): {Bicim.Tl(s.Fark)} ₺" };
        if (s.KanalBakiyeleri is { Count: > 0 } kanallar)
            satirlar.Add("Kanal kasaları: " + string.Join(" · ", kanallar.Select(k => $"{k.Kanal} {Bicim.Tl(k.Bakiye)} ₺")));
        if (s.Fark != 0)
            satirlar.Add("Fark var: kaydetmek için açıklama zorunludur.");
        satirlar.Add("Yalnız karşılaştırma kaydı tutulur; kasa değişmez.");
        Karsilastirma = string.Join("\n", satirlar);
    });
    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu)
            return;
        if (!ParaAyristirici.GecerliMi(GercekBakiye))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (_onizleme is null || _girdi is null || _girdi.GercekBakiye != GercekBakiye)
        { Hata = "Önce güncel bakiye karşılaştırmasını alın."; return; }
        if (_girdi.GercekBakiye == 0 && !_sifirOnayi)
        {
            _sifirOnayi = true;
            KayitUyarisi = "Gerçek toplam bakiye 0,00 ₺ olarak kaydedilecek. Alan boş bırakılmış olabilir. Onaylamak için yeniden kaydedin.";
            return;
        }
        if (_onizleme.Fark != 0 && string.IsNullOrWhiteSpace(Not))
        { Hata = NotZorunluMesaji; return; }
        var g = new KasaKontrolYaz(Guid.Empty, _girdi.GercekBakiye, _onizleme.KontrolOzeti, Not.Trim());
        g = g with { IstekId = _anahtar.Al(g) };
        try
        {
            var sonuc = await api.KasaKontrolKaydetAsync(g);
            if (!Gecerli(n))
                return;
            Gecmis.Insert(0, new(sonuc));
            _anahtar.Temizle();
            _girdi = null;
            _onizleme = null;
            Karsilastirma = null;
            SifirOnayiniKaldir();
            Mesaj = "Karşılaştırma kaydedildi. Genel kasa ve kanal bakiyeleri değiştirilmedi.";
            Tamamlandi();
        }
        catch (KasaApiException e) when ((int)e.DurumKodu == 409) { if (Gecerli(n)) { _girdi = null; _onizleme = null; Karsilastirma = null; } throw; }
    });

    /// <summary>"Bu kontrolden beri değişenler" (yalnız editör): kontrolden sonraki denetim olayları ve mali istekler, kontrol gününe
    /// sonradan girilen giderler ve kontrol gününden bugüne kasaya işleyen (kendiliğinden işleyenler dahil) hareketler.</summary>
    public Task IncelemeAsync(KasaKontrolSatiri satir) => YurutAsync(async n =>
    {
        if (!EditorMu)
            return;
        var s = await api.KasaKontrolSonrasiAsync(satir.Veri.Id);
        if (!Gecerli(n))
            return;
        Secili = Gecmis.FirstOrDefault(x => x.Veri.Id == satir.Veri.Id) ?? satir;
        FarkAciklamasi = Secili.Veri.FarkAciklamasi ?? "";
        _aciklamaAnahtari.Temizle();
        SonrasiOzeti = KasaKontrolMetni.Sonrasi(s);
        TakipMetni.Doldur(Degisiklikler, s.Degisiklikler.Select(d => new KasaKontrolDegisiklikSatiri(d)));
        TakipMetni.Doldur(SonrasiHareketler, s.Hareketler.Select(h => new KasaHareketiSatiri(h, s.EsasTarih)));
    });

    /// <summary>Seçili kontrolün farkını açıklar: kayıtlı bakiyeler ve fark değişmez; kayıt arada değiştiyse sunucu 409 verir.</summary>
    [RelayCommand]
    private Task AciklaAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is not { } satir)
            return;
        var metin = FarkAciklamasi.Trim();
        if (metin.Length == 0)
        { Hata = "Fark açıklamasını yazın."; return; }
        var g = new KasaKontrolAciklamaYaz(Guid.Empty, satir.Veri.Surum, metin);
        g = g with { IstekId = _aciklamaAnahtari.Al(new { satir.Veri.Id, g }) };
        var s = await api.KasaKontrolAciklaAsync(satir.Veri.Id, g);
        if (!Gecerli(n))
            return;
        _aciklamaAnahtari.Temizle();
        // Yanıtta güncel karşılaştırma alanları yoktur: listedeki güncel durum korunur.
        var yeni = new KasaKontrolSatiri(satir.Veri with { Surum = s.Surum, FarkAciklamasi = s.FarkAciklamasi, FarkAciklamaZamani = s.FarkAciklamaZamani });
        var i = Gecmis.IndexOf(satir);
        if (i >= 0)
            Gecmis[i] = yeni;
        Secili = yeni;
        Mesaj = "Fark açıklaması kaydedildi. Kayıtlı bakiyeler ve fark değişmedi.";
    });

    /// <summary>Kasa hareket dökümü (en çok 366 gün, bitiş en geç bugün; sunucu sınırlar).</summary>
    [RelayCommand]
    private Task DokumGetirAsync() => YurutAsync(async n =>
    {
        if (DokumBaslangic.Date > DokumBitis.Date)
        { Hata = "Bitiş tarihi başlangıçtan önce olamaz."; return; }
        var d = await api.KasaHareketleriAsync(DateOnly.FromDateTime(DokumBaslangic), DateOnly.FromDateTime(DokumBitis), DokumKasa?.KanalId);
        if (!Gecerli(n))
            return;
        DokumOzeti = $"{d.Kanal ?? "Genel kasa"} · {d.Baslangic:dd.MM.yyyy} – {d.Bitis:dd.MM.yyyy}\nAçılış {Bicim.Tl(d.AcilisBakiyesi)} ₺ · kapanış {Bicim.Tl(d.KapanisBakiyesi)} ₺ · {d.Hareketler.Count} hareket";
        TakipMetni.Doldur(DokumSatirlari, d.Hareketler.Select(h => new KasaHareketiSatiri(h, KanalKasasi: d.KanalId is not null)));
    });

    private void SeciliyiBirak() { Secili = null; SonrasiOzeti = null; FarkAciklamasi = ""; Degisiklikler.Clear(); SonrasiHareketler.Clear(); _aciklamaAnahtari.Temizle(); }
    protected override void OturumTemizle()
    {
        Gecmis.Clear();
        GercekBakiye = 0;
        Not = "";
        Karsilastirma = EsikUyarilari = EsikHatasi = null;
        _girdi = null;
        _onizleme = null;
        _anahtar.Temizle();
        SifirOnayiniKaldir();
        SeciliyiBirak();
        DokumOzeti = null;
        DokumSatirlari.Clear();
    }
}
public record KasaKontrolSatiri(KasaKontrolDto Veri)
{
    public string Baslik => $"{Veri.Kaydedildi.LocalDateTime:dd.MM.yyyy HH:mm} · fark {Bicim.Tl(Veri.Fark)} ₺" + (Veri.SonradanDegisti ? " · sonradan değişti" : "");
    public string Ozet => string.Join("\n", new[]
    {
        $"Sistem {Bicim.Tl(Veri.SistemBakiye)} ₺ · gerçek {Bicim.Tl(Veri.GercekBakiye)} ₺",
        Veri.Not,
        Veri.FarkAciklamasi is { Length: > 0 } a ? "Fark açıklaması: " + a : null,
        KasaKontrolMetni.Durum(Veri),
        Veri.KanalBakiyeleri is { Count: > 0 } k ? "Kanallar: " + string.Join(" · ", k.Select(x => $"{x.Kanal} {Bicim.Tl(x.Bakiye)} ₺" + (x.GuncelBakiye is { } g && g != x.Bakiye ? $" (güncel {Bicim.Tl(g)} ₺)" : ""))) : null,
    }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
/// <summary>Döküm kasası: genel kasa (<see cref="KanalId"/> null) ya da bir kanalın kasası.</summary>
public record DokumKasasi(int? KanalId, string Ad)
{
    public static readonly DokumKasasi Genel = new(null, "Genel kasa");
}
/// <summary>Kontrolden sonraki denetim olayı.</summary>
public record KasaKontrolDegisiklikSatiri(DenetimOlayDto Olay)
{
    public string Baslik => $"{Olay.Zaman.LocalDateTime:dd.MM.yyyy HH:mm} · {KasaKontrolMetni.OlayTuru(Olay.Tur)} · {KasaKontrolMetni.Kayit(Olay)}";
    public string Ozet => KasaKontrolMetni.OlayAyrintisi(Olay);
}
/// <summary>Kasa hareketi satırı. <paramref name="EsasTarih"/> verilirse ("kontrolden beri değişenler") o güne ya da öncesine düşen
/// satır "kontrol gününe sonradan girildi" diye işaretlenir; <paramref name="KanalKasasi"/> dökümde kanal kasası etkisini gösterir.</summary>
public record KasaHareketiSatiri(KasaHareketiDto Veri, DateOnly? EsasTarih = null, bool KanalKasasi = false)
{
    public decimal Etki => KanalKasasi ? Veri.KanalEtkisi : Veri.GenelKasaEtkisi;
    public string Baslik => $"{Veri.EtkiTarihi:dd.MM.yyyy} · {KasaKontrolMetni.HareketTuru(Veri.Tur)}{(Veri.Otomatik ? " (kendiliğinden)" : "")} · {Bicim.ImzaliTl(Etki)} ₺";
    public string Ozet => string.Join(" · ", new[] { Veri.Aciklama, Veri.Kanal, Veri.EtkiTarihi != Veri.KayitTarihi ? $"kayıt {Veri.KayitTarihi:dd.MM.yyyy}" : null }.Where(s => s is not null))
        + (EsasTarih is { } e && Veri.EtkiTarihi <= e ? "\nKontrol gününe ya da öncesine sonradan girildi." : "");
}
/// <summary>Kasa kontrolü metinleri (web cash-controls-ui.js ile aynı adlar).</summary>
public static class KasaKontrolMetni
{
    private static readonly Dictionary<string, string> Hareketler = new()
    {
        ["Gelir"] = "Dönem geliri",
        ["EkstreGeliri"] = "Ekstre geliri",
        ["EkGelir"] = "Ek gelir",
        ["KrediCekimi"] = "Kredi çekimi",
        ["Gider"] = "Gider",
        ["SabitGider"] = "Sabit gider",
        ["AylikGider"] = "Aylık gider",
        ["KartOdemesi"] = "Kart ödemesi",
        ["KartIadesi"] = "Kart borcu iadesi",
        ["KrediTaksidi"] = "Kredi taksidi",
        ["KartAySonu"] = "Eski kart ay sonu düşümü",
    };
    private static readonly Dictionary<string, string> Varliklar = new()
    {
        ["Islem"] = "Gider",
        ["Gelen"] = "Gelir",
        ["Kanal"] = "Kanal",
        ["Ayar"] = "Kasa ayarı",
        ["Alis"] = "Alış",
        ["AlisKalem"] = "Alış kalemi",
        ["AlisDagilim"] = "Alış kanal payı",
        ["AlisOdeme"] = "Alış ödemesi",
        ["TakipKart"] = "Kredi kartı",
        ["TakipHarcama"] = "Kart harcaması",
        ["TakipKartTaksit"] = "Kart taksidi",
        ["TakipKartOdeme"] = "Kart ödemesi",
        ["TakipKredi"] = "Kredi",
        ["TakipKrediTaksit"] = "Kredi taksidi",
        ["Kredi"] = "Kredi kaydı",
        ["KrediKarti"] = "Kart kaydı",
        ["KartOdeme"] = "Eski kart ödemesi",
        ["AylikGiderOdeme"] = "Aylık gider ödemesi",
        ["EkstreKayit"] = "Ekstre satırı",
        ["HesapHareket"] = "Hesap hareketi",
    };
    private static readonly Dictionary<string, string> OlayTurleri = new()
    {
        ["Ekle"] = "Eklendi",
        ["Degistir"] = "Değiştirildi",
        ["Sil"] = "Silindi",
        ["BagKoptu"] = "Bağ koptu",
        ["GecmisAyEtkisi"] = "Geçmiş ay payı değişti",
        ["KilitAc"] = "Ay kilidi açıldı",
        ["KilitKapat"] = "Ay kapatıldı",
    };
    private static readonly string[] Alanlar = ["Tarih", "TutarTl", "Tutar", "Iptal", "Kanal", "Cari", "Aciklama", "Durum"];

    public static string HareketTuru(string tur) => Hareketler.GetValueOrDefault(tur, tur);
    public static string OlayTuru(string tur) => OlayTurleri.GetValueOrDefault(tur, tur);
    public static string Kayit(DenetimOlayDto o) => Varliklar.GetValueOrDefault(o.Varlik, o.Varlik) + (o.VarlikId is { } id ? " #" + id : "");

    /// <summary>Listedeki güncel durum: sonradan değiştiyse güncel sistem ve güncel fark; filigransız eski kayıt belirtilir.</summary>
    public static string? Durum(KasaKontrolDto k)
    {
        var parcalar = new List<string>();
        if (k.SonradanDegisti && k.GuncelSistemBakiye is { } g)
            parcalar.Add($"Sonradan değişti: güncel sistem {Bicim.Tl(g)} ₺, güncel fark {Bicim.Tl(k.GuncelFark ?? 0)} ₺");
        else if (k.GuncelSistemBakiye is not null)
            parcalar.Add("Kayıttan sonra değişmedi");
        if (k.HesapTarihi is null)
            parcalar.Add("Eski kayıt, filigran yok");
        return parcalar.Count == 0 ? null : string.Join(" · ", parcalar);
    }

    public static string Sonrasi(KasaKontrolSonrasiDto s)
    {
        var satirlar = new List<string>
        {
            $"Kontroldeki sistem bakiyesi ({s.EsasTarih:dd.MM.yyyy}): {Bicim.Tl(s.SistemBakiye)} ₺",
            $"Aynı gün, bugünkü veriyle: {Bicim.Tl(s.GuncelSistemBakiye)} ₺ (geriye dönük değişim {Bicim.ImzaliTl(s.GuncelSistemBakiye - s.SistemBakiye)} ₺)",
            $"Bugünkü genel kasa: {Bicim.Tl(s.BugunkuSistemBakiye)} ₺ (kontrol gününden sonra {Bicim.ImzaliTl(s.BugunkuSistemBakiye - s.GuncelSistemBakiye)} ₺)",
        };
        if (!s.FiligranVar)
            satirlar.Add("Eski kayıt, filigran yok: değişiklikler kayıt anından sonraki zamana göre listelenir.");
        if (s.Istekler.Count > 0)
            satirlar.Add("Mali istekler: " + string.Join(", ", s.Istekler.Select(i => $"{i.Tur} #{i.SonucId}")));
        if (s.Degisiklikler.Count == 0)
            satirlar.Add("Kontrolden sonra kasayı etkileyen değişiklik yok.");
        if (s.Kirpildi)
            satirlar.Add("Listeler en eski 500 öğeyle kırpıldı; ayrıntı için değişiklik geçmişini kullanın.");
        return string.Join("\n", satirlar);
    }

    /// <summary>Olayın gerekçesi ve önemli mali alanları (değişiklikte önceki → yeni).</summary>
    public static string OlayAyrintisi(DenetimOlayDto o)
    {
        var once = Oku(o.OncekiJson);
        var sonra = Oku(o.YeniJson);
        var satirlar = new List<string>();
        if (!string.IsNullOrWhiteSpace(o.Gerekce))
            satirlar.Add(o.Gerekce);
        foreach (var alan in Alanlar)
        {
            var onceVar = once is not null && once.ContainsKey(alan);
            var sonraVar = sonra is not null && sonra.ContainsKey(alan);
            if (onceVar && sonraVar)
                satirlar.Add($"{alan}: {Metin(once![alan])} → {Metin(sonra![alan])}");
            else if (sonraVar)
                satirlar.Add($"{alan}: {Metin(sonra![alan])}");
            else if (onceVar)
                satirlar.Add($"{alan}: {Metin(once![alan])}");
        }
        return satirlar.Count == 0 ? "—" : string.Join(" · ", satirlar);
    }
    private static Dictionary<string, System.Text.Json.JsonElement>? Oku(string? json)
    {
        if (json is null)
            return null;
        try
        { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json); }
        catch (System.Text.Json.JsonException) { return null; }
    }
    private static string Metin(System.Text.Json.JsonElement e) => e.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Null => "—",
        System.Text.Json.JsonValueKind.True => "Evet",
        System.Text.Json.JsonValueKind.False => "Hayır",
        System.Text.Json.JsonValueKind.String => e.GetString() ?? "—",
        _ => e.GetRawText(),
    };
}

public partial class KasaEsikViewModel(IKasaKontrolApi api, AuthViewModel auth) : OturumluViewModel(auth)
{
    public ObservableCollection<KasaEsikSatiri> Kanallar { get; } = new();
    [ObservableProperty] private KasaEsikSatiri? _secili;
    [ObservableProperty] private decimal _tutar;
    [ObservableProperty] private bool _etkin;
    partial void OnSeciliChanged(KasaEsikSatiri? value) { Tutar = value?.Veri.Tutar ?? 0; Etkin = value?.Veri.Etkin ?? false; }
    public Task YukleAsync() => YurutAsync(async n => { VeriHazir = false; var s = await api.KasaEsikleriAsync(); if (!Gecerli(n)) return; TakipMetni.Doldur(Kanallar, s.Select(x => new KasaEsikSatiri(x))); Secili = null; Tamamlandi(); });
    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(async n =>
    {
        if (!EditorMu || Secili is null)
            return;
        if (!ParaAyristirici.GecerliMi(Tutar))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (Tutar < 0 || decimal.Round(Tutar, 2) != Tutar)
        { Hata = "Alt limiti sıfır veya pozitif, en fazla iki ondalıkla girin."; return; }
        var g = new KasaEsikYaz(Secili.Veri.Surum, Tutar, Etkin);
        var s = await api.KasaEsigiKaydetAsync(Secili.Veri.KanalId, g);
        if (!Gecerli(n))
            return;
        var eski = Kanallar.First(k => k.Veri.KanalId == s.KanalId);
        Kanallar[Kanallar.IndexOf(eski)] = new(s);
        Secili = Kanallar.First(k => k.Veri.KanalId == s.KanalId);
        Mesaj = "Kanal alt limit uyarısı kaydedildi. Bakiye değiştirilmedi.";
        Tamamlandi();
    });
    protected override void OturumTemizle() { Kanallar.Clear(); Secili = null; Tutar = 0; Etkin = false; }
}
public record KasaEsikSatiri(KasaEsikDto Veri)
{
    public string Ad => Veri.Kanal;
    public string Baslik => Veri.Kanal;
    public string Ozet => $"Bakiye {Bicim.Tl(Veri.Bakiye)} ₺ · " + (Veri.Etkin ? $"Alt limit {Bicim.Tl(Veri.Tutar)} ₺" + (Veri.EsikAltinda ? " — limit altında" : "") : "Uyarı kapalı");
}
