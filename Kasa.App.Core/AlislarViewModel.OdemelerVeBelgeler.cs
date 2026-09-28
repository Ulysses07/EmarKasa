using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

public partial class AlislarViewModel
{
    private readonly TekrarAnahtari _duzeltmeAnahtari = new();
    private readonly TekrarAnahtari _iptalAnahtari = new();
    public ObservableCollection<BelgeDto> Belgeler { get; } = new();
    /// <summary>Yalnız editör: kaldırılmış belgeler de (kaldıran, zaman ve gerekçesiyle) listelenir; değişince liste yenilenir.</summary>
    [ObservableProperty] private bool _silinenBelgeleriGoster;
    partial void OnSilinenBelgeleriGosterChanged(bool value) { if (_secili is not null && EditorMu) _ = BelgeleriYukleAsync(); }
    /// <summary>Web (app.js) ile aynı ileti; sunucu da aynı kuralı uygular.</summary>
    public const string BelgeSilmeGerekcesiGerekli = "Belge silme gerekçesi girin.";
    public ObservableCollection<AlisSatiri> DuzeltmeHedefleri { get; } = new();
    public ObservableCollection<OdemeKartiSecenegi> DuzeltmeKartlari { get; } = new();
    [ObservableProperty] private AlisOdemeSatiri? _duzeltilecekOdeme;
    [ObservableProperty] private DateTime _duzeltmeTarihi = DateTime.Today;
    [ObservableProperty] private decimal _duzeltmeTutari;
    [ObservableProperty] private OdemeKartiSecenegi? _duzeltmeKarti;
    [ObservableProperty] private AlisSatiri? _hedefAlis;
    [ObservableProperty] private string _duzeltmeAciklamasi = "";
    [ObservableProperty] private bool _eskiKartHarcamasi;
    public bool DuzeltmeAcik => DuzeltilecekOdeme is not null && EditorMu;
    partial void OnDuzeltilecekOdemeChanged(AlisOdemeSatiri? value) => OnPropertyChanged(nameof(DuzeltmeAcik));
    public void IdIleSec(int id) { var satir = Alislar.FirstOrDefault(a => a.Veri.Id == id); if (satir is not null) Sec(satir); }
    [RelayCommand] private void OdemeDuzelt(AlisOdemeSatiri odeme)
    {
        if (!EditorMu || Mesgul) return;
        if (KaydedilmemisDegisiklikVar) { KaydetmeUyarisi(); return; }
        DuzeltilecekOdeme = odeme; DuzeltmeTarihi = odeme.Veri.Tarih.ToDateTime(TimeOnly.MinValue); DuzeltmeTutari = odeme.Veri.Tutar;
        // Bağlı gider artık bağlanabilir listede değildir: kartsız eski kart harcaması bilgisi ödemenin kendisinden gelir.
        EskiKartHarcamasi = odeme.Veri.KrediKartiId is null && odeme.Veri.EskiKartHarcamasi;
        DuzeltmeKartlari.Clear(); DuzeltmeKartlari.Add(new(null, EskiKartHarcamasi ? "Eski kart harcamasını koru" : "Nakit / banka"));
        foreach (var kart in OdemeKartlari.Where(k => k.Id is not null)) DuzeltmeKartlari.Add(kart);
        // K3: liste yalnız takipteki açık kartlardır; ödemenin kendi (eski/kapalı) kartı ayrıca eklenir, kayıt kartıyla kalabilir.
        if (odeme.Veri.KrediKartiId is { } kendi && DuzeltmeKartlari.All(k => k.Id != kendi))
            DuzeltmeKartlari.Add(new(kendi, $"{(string.IsNullOrWhiteSpace(odeme.Veri.KrediKartiAdi) ? _kartAdlari.GetValueOrDefault(kendi, $"Kart #{kendi}") : odeme.Veri.KrediKartiAdi)} (eski kayıt)"));
        DuzeltmeKarti = DuzeltmeKartlari.FirstOrDefault(k => k.Id == odeme.Veri.KrediKartiId);
        HedefAlis = null; DuzeltmeAciklamasi = "";
        _duzeltmeAnahtari.Temizle(); _iptalAnahtari.Temizle();
    }
    [RelayCommand] private void DuzeltmedenVazgec() { DuzeltilecekOdeme = null; HedefAlis = null; }
    [RelayCommand] private void HedefiTemizle() => HedefAlis = null;
    [RelayCommand] private Task OdemeDuzeltKaydetAsync() => YurutAsync(async n =>
    {
        if (_odemelerApi is null || !EditorMu || _secili is null || DuzeltilecekOdeme is null) return;
        if (KaydedilmemisDegisiklikVar) { KaydetmeUyarisi(); return; }
        if (!ParaAyristirici.GecerliMi(DuzeltmeTutari)) { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (DuzeltmeTutari <= 0 || string.IsNullOrWhiteSpace(DuzeltmeAciklamasi)) { Hata = "Pozitif ödeme tutarı ve düzeltme açıklaması girin."; return; }
        var g = new AlisOdemeDuzeltYaz(_secili.Surum, Guid.Empty, DateOnly.FromDateTime(DuzeltmeTarihi), DuzeltmeTutari, DuzeltmeKarti?.Id,
            null, DuzeltmeAciklamasi.Trim(), HedefAlis?.Veri.Id, HedefAlis?.Veri.Surum);
        g = g with { IstekId = _duzeltmeAnahtari.Al(new { AlisId = _secili.Id, OdemeId = DuzeltilecekOdeme.Veri.Id, g }) };
        var sonuc = await _odemelerApi.AlisOdemeDuzeltAsync(_secili.Id, DuzeltilecekOdeme.Veri.Id, g);
        if (!SonucuUygula(sonuc, n)) return;
        _duzeltmeAnahtari.Temizle();
        var tumu = await _api.AlislarAsync();
        if (!Gecerli(n)) return;
        Degistir(Alislar, tumu.Select(a => new AlisSatiri(a))); SeciliyiGoster(sonuc); GiderSecenekleriniYenile();
        OnPropertyChanged(nameof(DagilimBekleyenTutar)); OnPropertyChanged(nameof(DagilimBekliyor));
        Mesaj = g.HedefAlisId is null ? "Ödeme düzeltildi; gerekçe işlem geçmişine kaydedildi." : "Ödeme seçilen alışa taşındı; ikinci gider oluşturulmadı.";
    });
    public Task OdemeIptalAsync() => YurutAsync(async n =>
    {
        if (_odemelerApi is null || !EditorMu || _secili is null || DuzeltilecekOdeme is null) return;
        if (KaydedilmemisDegisiklikVar) { KaydetmeUyarisi(); return; }
        if (string.IsNullOrWhiteSpace(DuzeltmeAciklamasi)) { Hata = "İptal nedenini açıklama alanına yazın."; return; }
        var g = new AlisOdemeIptalYaz(_secili.Surum, Guid.Empty, DuzeltmeAciklamasi.Trim());
        var id = DuzeltilecekOdeme.Veri.Id;
        g = g with { IstekId = _iptalAnahtari.Al(new { _secili.Id, OdemeId = id, g }) };
        if (!SonucuUygula(await _odemelerApi.AlisOdemeIptalAsync(_secili.Id, id, g), n)) return;
        _iptalAnahtari.Temizle();
        var arama = GiderArama.Trim();
        var giderler = await GiderSayfasiAsync(arama, null);
        if (!Gecerli(n)) return;
        GiderSayfasiniUygula(giderler, arama, ekle: false);
        Mesaj = "Ödeme ve bağlı gider iptal edildi. İptal gerekçesi geçmişte korundu.";
    });
    [RelayCommand] public Task BelgeleriYukleAsync() => YurutAsync(async n =>
    {
        if (_yonetim is null || _secili is null) return;
        var id = _secili.Id;
        var belgeler = await _yonetim.BelgelerAsync(id, EditorMu && SilinenBelgeleriGoster);
        if (Gecerli(n) && _secili?.Id == id) Degistir(Belgeler, belgeler);
    });
    /// <summary>Belge ekleme (maui-8; önceden AlislarPage.BelgeEkleTiklandi'deydi). Seçici açılırken oturum ve seçili alış
    /// yakalanır: seçim ya da okuma sürerken oturum veya alış değişirse dosya yüklenmez. İçerik türü uzantıdan, 10 MB sınırı
    /// okurken denetlenir. Dönen uyarıyı sayfa gösterir (desteklenmeyen, büyük ya da okunamayan dosya); yükleme hatası
    /// <see cref="TemelViewModel.Hata"/>'dadır.</summary>
    /// <param name="sec">Dosya seçiciyi açar; vazgeçilirse null.</param>
    public async Task<DosyaUyarisi?> BelgeEkleAsync(Func<Task<SecilenDosya?>> sec, int? odemeId)
    {
        if (Mesgul || _secili is null) return null;
        var nesil = Yurutucu.Nesil; var alisId = _secili.Id;
        bool SecimSuruyor() => Gecerli(nesil) && _secili?.Id == alisId;
        try
        {
            var dosya = await sec();
            if (dosya is null || !SecimSuruyor()) return null;
            var tur = DosyaSecimKurallari.BelgeIcerikTuru(dosya.Ad);
            if (tur is null) return DosyaSecimKurallari.DesteklenmeyenBelge;
            DosyaOkumasi okuma;
            await using (var akis = await dosya.Ac()) okuma = await DosyaSecimKurallari.SinirliOkuAsync(akis, DosyaSecimKurallari.EnFazlaBayt, SecimSuruyor);
            if (okuma.Durum == DosyaOkumaDurumu.SinirAsildi) return DosyaSecimKurallari.BuyukBelge;
            if (okuma.Durum == DosyaOkumaDurumu.Vazgecildi || !SecimSuruyor()) return null;
            await BelgeYukleAsync(dosya.Ad, tur, okuma.Icerik!, odemeId);
            return null;
        }
        catch (Exception) { return DosyaSecimKurallari.OkunamayanBelge; }
    }
    public Task BelgeYukleAsync(string ad, string tur, byte[] icerik, int? odemeId) => YurutAsync(async n =>
    {
        if (_yonetim is null || _secili is null) return;
        if (icerik.Length > DosyaSecimKurallari.EnFazlaBayt || icerik.Length == 0) { Hata = "Belge boş olamaz ve 10 MB sınırını aşamaz."; return; }
        var id = _secili.Id;
        try
        {
            var belge = await _yonetim.BelgeYukleAsync(id, ad, tur, icerik, EditorMu ? odemeId : null);
            if (Gecerli(n) && _secili?.Id == id) { Belgeler.Add(belge); Mesaj = "Belge eklendi."; }
        }
        catch (TimeoutException)
        {
            // Yükleme sunucuda tamamlanmış olabilir (belge ucu tekrar anahtarı taşımaz): liste yenilenir ki kullanıcı
            // aynı belgeyi yeniden yüklemeden önce görsün. Yenileme de başarısızsa asıl zaman aşımı iletisi gösterilir.
            try { var belgeler = await _yonetim.BelgelerAsync(id); if (Gecerli(n) && _secili?.Id == id) Degistir(Belgeler, belgeler); }
            catch (Exception) { /* zaman aşımı iletisi yeterli */ }
            throw;
        }
    });
    /// <summary>Belgeyi <paramref name="hedef"/>'e yazar; hata ya da eski oturumda null.</summary>
    public async Task<IndirmeBilgisi?> BelgeIndirAsync(BelgeDto belge, Stream hedef)
    {
        IndirmeBilgisi? dosya = null;
        await YurutAsync(async n => { if (_yonetim is null) return; var d = await _yonetim.BelgeIndirAsync(belge.Id, hedef); if (Gecerli(n)) dosya = d; });
        return dosya;
    }
    /// <summary>Belgeyi kaldırır (gap-denetim-izi-gozlemlenebilirlik-9): sunucuda yumuşak silmedir, içerik ve kaldırma kaydı saklanır.
    /// Editör için gerekçe zorunludur (boşsa istek gönderilmez), alıcı için isteğe bağlıdır. Silinenler gösteriliyorsa liste
    /// yenilenir (belge 'kaldırıldı' olarak kalır), aksi halde listeden çıkar.</summary>
    public Task BelgeSilAsync(BelgeDto belge, string? gerekce = null) => YurutAsync(async n =>
    {
        if (_yonetim is null || belge.Silindi) return;
        gerekce = string.IsNullOrWhiteSpace(gerekce) ? null : gerekce.Trim();
        if (EditorMu && gerekce is null) { Hata = BelgeSilmeGerekcesiGerekli; return; }
        var alisId = _secili?.Id;
        await _yonetim.BelgeSilAsync(belge.Id, gerekce);
        if (!Gecerli(n)) return;
        if (EditorMu && SilinenBelgeleriGoster && alisId is { } id)
        {
            var belgeler = await _yonetim.BelgelerAsync(id, true);
            if (Gecerli(n) && _secili?.Id == id) Degistir(Belgeler, belgeler);
        }
        else Belgeler.Remove(belge);
        if (Gecerli(n)) Mesaj = "Belge kaldırıldı; içeriği ve kaldırma kaydı saklanır.";
    });

    /// <summary>Belge satırının açıklaması: yükleyen (bu sürümden önceki belgelerde bilinmez) ve kaldırıldıysa kaldıran, zaman, gerekçe.</summary>
    public static string BelgeAciklamasi(BelgeDto b)
    {
        var yukleyen = b.Yukleyen is { Length: > 0 } ad ? $"Yükleyen: {ad}" : "Yükleyen: bilinmiyor (eski kayıt)";
        if (!b.Silindi) return yukleyen;
        var kaldiran = b.Silen is { Length: > 0 } s ? s : "bilinmiyor";
        var zaman = b.SilinmeZamani?.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        return $"{yukleyen} · Kaldırıldı: {kaldiran}{(zaman is null ? "" : " · " + zaman)}{(string.IsNullOrWhiteSpace(b.SilmeGerekcesi) ? "" : " · Gerekçe: " + b.SilmeGerekcesi)}";
    }
}
