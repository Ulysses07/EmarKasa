using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Gelen (kanal geliri) formu — web app.js incomeDialog aynası. PUT /api/gelenler dönem+kanal toplamını
/// yerine koyar, üstüne eklemez; bu yüzden form tarih değil kasa dönemi seçtirir, dönemin kayıtlı toplamını yükleyip
/// alana doldurur ve kayıt yeni dönem toplamı olarak gider.</summary>
public partial class IslemlerViewModel
{
    private readonly TimeProvider _zaman;
    private IReadOnlyList<KanalDto> _kanallar = Array.Empty<KanalDto>();
    private IReadOnlyList<GelenDto> _gelenler = Array.Empty<GelenDto>();
    private readonly SonIstekHatti _gelenHatti;   // son dönem yüklemesi kazanır
    private bool _gelenDonemAtaniyor;    // VM'nin kendi dönem ataması yükleme tetiklemez
    private bool _gelenSifirOnayi;       // kayıtlı toplamı 0'a indirmek ikinci basışta gider

    /// <summary>Gelir formu dönem seçici kaynağı (yeniden eskiye). Filtre seçicisinden ayrıdır: filtre listesi
    /// yenilenirken Picker seçimi boşaltır, gelir formunun dönemi bundan etkilenmemeli.</summary>
    public ObservableCollection<DonemDto> GelenDonemler { get; } = new();

    [ObservableProperty] private DonemDto? _gelenDonem;
    [ObservableProperty] private string _gelenKanal = "";
    [ObservableProperty] private decimal _gelenTutar;
    [ObservableProperty] private bool _gelenYukleniyor;
    [ObservableProperty] private bool _gelenYuklemeHatasi;
    [ObservableProperty] private string? _gelenBilgi;

    /// <summary>Son başlatılan dönem geliri yüklemesi (eski dönemin geç yanıtı uygulanmaz).</summary>
    public Task GelenYuklemesi { get; private set; } = Task.CompletedTask;

    private DateOnly Bugun => DateOnly.FromDateTime(_zaman.GetLocalNow().DateTime);
    private GelirSecimSonucu GelenSecim => GelirSecimi.Hesapla(_gelenler, _kanallar.FirstOrDefault(k => k.Ad == GelenKanal));

    /// <summary>Seçili dönem ve kanalın kayıtlı toplamı; kayıt yoksa null.</summary>
    public decimal? GelenMevcutToplam => GelenSecim is { Sayi: > 0 } s ? s.Toplam : null;
    public bool GelenSaltOkunur => GelenSecim.SaltOkunur;
    public bool GelenKaydedilebilir => GelenDonem is not null && GelenKanal.Length > 0 && !GelenYukleniyor && !GelenYuklemeHatasi && !GelenSaltOkunur;

    /// <summary>Dönem listesini eşitler, seçili dönemi korur (yoksa bugünün dönemi) ve gelirlerini yükler.</summary>
    private Task GelenFormunuHazirlaAsync()
    {
        var donemler = _donemler.OrderByDescending(d => d.Start).ToList();
        var hedef = GelenDonem is { } secili && donemler.Contains(secili) ? secili : GelirSecimi.VarsayilanDonem(donemler, Bugun);
        _gelenDonemAtaniyor = true;
        try
        {
            if (!GelenDonemler.SequenceEqual(donemler)) TakipMetni.Doldur(GelenDonemler, donemler);
            GelenDonem = hedef;
        }
        finally { _gelenDonemAtaniyor = false; }
        return GelenleriYukle();
    }

    private Task GelenleriYukle() => GelenYuklemesi = GelenleriYukleAsync(_gelenHatti.Baslat(), GelenDonem);

    private async Task GelenleriYukleAsync(IstekBileti istek, DonemDto? donem)
    {
        // Web gibi: yanıt gelene kadar hem "yükleniyor" hem "hata" açık; kayıt kapalı kalır.
        _gelenler = Array.Empty<GelenDto>();
        GelenYukleniyor = GelenYuklemeHatasi = donem is not null;
        GelenFormunuDoldur();
        if (donem is null) return;
        try
        {
            var liste = await _api.GelenlerAsync(donem.Start);
            if (_gelenHatti.Guncel(istek)) { _gelenler = liste; GelenYuklemeHatasi = false; }
        }
        catch (Exception) { /* hata bayrağı açık kalır: mevcut toplam bilinmeden kayıt yapılamaz */ }
        finally { if (_gelenHatti.Guncel(istek)) { GelenYukleniyor = false; GelenFormunuDoldur(); } }
    }

    /// <summary>Seçili kanalın kayıtlı dönem toplamı forma dolar (web fill()).</summary>
    private void GelenFormunuDoldur()
    {
        GelenTutar = GelenSecim.Toplam;
        GelenBilgiYenile();
    }

    private void GelenBilgiYenile()
    {
        var s = GelenSecim;
        GelenBilgi = GelenDonem is null ? (GelenDonemler.Count == 0 ? "Gelir girmek için geçerli kasa dönemi gerekir." : "Kasa dönemi seçin.")
            : GelenYukleniyor ? "Dönem gelirleri yükleniyor…"
            : GelenYuklemeHatasi ? "Dönem gelirleri yüklenemedi. Başka dönem seçip yeniden deneyin; mevcut bilgilerle kayıt yapılamaz."
            : s.SaltOkunur ? $"Bu dönem ve kanal için {s.Sayi} eski gelir kaydı var. Toplam {Bicim.Tl(s.Toplam)} ₺ kasaya dahildir. Geçmiş tutarları korumak için bu grup burada değiştirilemez. Başka dönem veya kanal seçerek normal gelir kaydı yapabilirsiniz."
            : "Buraya seçilen kanalın bu dönemdeki toplam gelirini yazın. Kayıt varsa yeni tutar öncekinin yerine geçer; üzerine eklenmez."
              + (s.Sayi == 1 ? $" Kayıtlı toplam: {Bicim.Tl(s.Toplam)} ₺." : "");
        OnPropertyChanged(nameof(GelenMevcutToplam));
        OnPropertyChanged(nameof(GelenSaltOkunur));
        OnPropertyChanged(nameof(GelenKaydedilebilir));
    }

    partial void OnGelenDonemChanged(DonemDto? value)
    {
        _gelenSifirOnayi = false;
        if (!_gelenDonemAtaniyor) GelenleriYukle();
    }

    partial void OnGelenKanalChanged(string value)
    {
        foreach (var k in GelenKanallari) k.Secili = k.Ad == value;
        _gelenSifirOnayi = false;
        GelenFormunuDoldur();
    }

    partial void OnGelenTutarChanged(decimal value)
    {
        _gelenSifirOnayi = false;
        GelenBilgiYenile();
    }

    private void GelenTemizle()
    {
        _gelenHatti.Birak(); _gelenler = Array.Empty<GelenDto>(); _gelenSifirOnayi = false;
        _gelenDonemAtaniyor = true;
        try { GelenDonem = null; } finally { _gelenDonemAtaniyor = false; }
        GelenYukleniyor = GelenYuklemeHatasi = false;
        GelenKanal = "";
        GelenFormunuDoldur();
    }

    [RelayCommand]
    private Task GelenKaydetAsync() => YurutAsync(async n =>
    {
        if (_auth is not null && _auth.AktifRol != Rol.Editor) return;
        if (GelenDonem is not { } donem) { Hata = "Kasa dönemi seçin."; return; }
        if (GelenKanal.Length == 0) { Hata = "Kanal seçin."; return; }
        if (GelenYukleniyor || GelenYuklemeHatasi) { Hata = "Dönem gelirleri yüklenmeden kayıt yapılamaz. Lütfen yeniden deneyin."; return; }
        var secim = GelenSecim;
        if (secim.SaltOkunur) { Hata = "Bu eski gelir grubu geçmiş tutarları korumak için değiştirilemez."; return; }
        if (!ParaAyristirici.GecerliMi(GelenTutar)) { Hata = ParaAyristirici.GecersizMesaji; return; }
        var yeni = GelenTutar;
        if (secim.Sayi == 0 && yeni == 0) { Hata = "Dönem toplam gelirini girin."; return; }
        if (secim.Sayi > 0 && yeni == secim.Toplam) { GelenBilgi = "Tutar değişmedi."; return; }
        if (secim.Toplam != 0 && yeni == 0 && !_gelenSifirOnayi)
        {
            _gelenSifirOnayi = true;
            GelenBilgi = $"Dönem toplamı {Bicim.Tl(secim.Toplam)} ₺ yerine 0,00 ₺ yapılacak. Onaylamak için yeniden kaydedin.";
            return;
        }
        var kanal = GelenKanal;
        var sonuc = await _api.GelenKaydetAsync(new GelenYaz(donem.Start, kanal, yeni));
        if (!Gecerli(n)) return;
        _gelenSifirOnayi = false;
        await GelenleriYukle();
        // Yeniden yükleme sürerken oturum değiştiyse kayıt iletisi yeni oturumun formuna yazılmaz.
        if (!Gecerli(n)) return;
        GelenBilgi = $"Kanal geliri kaydedildi: {kanal} · {donem.Start:dd.MM.yyyy}–{donem.End:dd.MM.yyyy} dönem toplamı {Bicim.Tl(sonuc.TutarTl)} ₺ (önceki {Bicim.Tl(secim.Toplam)} ₺)."
            + (GelenYuklemeHatasi ? " Dönem gelirleri yeniden yüklenemedi; yeni kayıttan önce dönemi yeniden seçin." : "");
    });
}
