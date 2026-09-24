using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Ayarlar › Kullanıcılar (editör): her ortağa ayrı giriş. Ekle, adı/rolü/aktifliği düzenle, şifre
/// sıfırla, oturumlarını kapat, iki adımlı girişini kapat (telefon kaybolduysa), sil. .env editörü
/// (yerleşik hesap) silinemez ve rolü değişmez. Kendi satırınızda (Ben) rol/aktiflik, şifre, oturum
/// kapatma, iki adım kapatma ve silme yoktur: bunlar Hesabım ve Oturumlar bölümlerinden yapılır
/// (sunucu da reddeder). Ortak izleyici şifresi buradan kaldırılabilir.
/// </summary>
public partial class KullanicilarViewModel : TemelViewModel
{
    public const string RolIzleyici = "İzleyici";
    public const string RolEditor = "Editör";

    public const string KendiOturumlariMesaji = "Kendi oturumlarınızı buradan kapatamazsınız; diğer cihazları Oturumlar listesinden kapatın, bu cihaz için Çıkış yapın.";
    public const string KendiIkiAdimMesaji = "Kendi iki adımlı girişinizi Hesabım bölümünden kapatın.";
    public const string KendiSifreMesaji = "Kendi şifrenizi Hesabım bölümünden değiştirin.";
    public const string KendiSilMesaji = "Kendi hesabınızı silemezsiniz.";

    private static readonly Regex KullaniciAdiKurali = new(@"^[\p{L}\p{N}._-]{3,50}$", RegexOptions.CultureInvariant);

    private readonly IKasaApi _api;

    public KullanicilarViewModel(IKasaApi api, TimeProvider? zaman = null) : base(zaman)
    {
        _api = api;
        YeniRoller = [new SecimCipi(RolIzleyici) { Secili = true }, new SecimCipi(RolEditor)];
        DuzenRoller = [new SecimCipi(RolIzleyici), new SecimCipi(RolEditor)];
    }

    public ObservableCollection<KullaniciSatiri> Kullanicilar { get; } = new();

    [ObservableProperty] private string? _bilgi;

    // ── Yeni kullanıcı ──
    [ObservableProperty] private string? _yeniAdSoyad;
    [ObservableProperty] private string? _yeniKullaniciAdi;
    [ObservableProperty] private string _yeniSifre = "";
    public IReadOnlyList<SecimCipi> YeniRoller { get; }

    // ── Düzenleme ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DuzenAcik), nameof(DuzenBaslik), nameof(DuzenRolDegisebilir), nameof(DuzenSifreDegisebilir))]
    private KullaniciSatiri? _duzenlenen;
    [ObservableProperty] private string? _duzenAdSoyad;
    [ObservableProperty] private bool _duzenAktif;
    [ObservableProperty] private string _duzenSifre = "";
    public IReadOnlyList<SecimCipi> DuzenRoller { get; }

    public bool DuzenAcik => Duzenlenen is not null;
    public string DuzenBaslik => Duzenlenen is { } d ? $"{d.KullaniciAdi} düzenleniyor" : "";
    /// <summary>.env editörünün ve kendi hesabınızın rolü ve aktifliği buradan değişmez.</summary>
    public bool DuzenRolDegisebilir => Duzenlenen is { Yerlesik: false, Ben: false };
    /// <summary>.env editörünün ve kendi hesabınızın şifresi Hesabım bölümünden değişir.</summary>
    public bool DuzenSifreDegisebilir => Duzenlenen is { Yerlesik: false, Ben: false };

    // ── İzleyici şifresini kaldır (iki basışlı onay) ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IzleyiciKaldirMetni))]
    private bool _izleyiciKaldirOnayBekliyor;

    public string IzleyiciKaldirMetni => IzleyiciKaldirOnayBekliyor
        ? "Emin misiniz? Ortak izleyici girişi kapanacak"
        : "Ortak izleyici şifresini kaldır";

    public Task YukleAsync() => CalistirAsync(DoldurAsync);

    private async Task DoldurAsync()
    {
        var liste = await _api.KullanicilarAsync();
        Kullanicilar.Clear();
        foreach (var k in liste.OrderByDescending(k => k.Aktif).ThenByDescending(k => k.Rol == "editor").ThenBy(k => k.AdSoyad, StringComparer.Create(Kultur.Turkce, true)))
            Kullanicilar.Add(new KullaniciSatiri(k, Zaman.LocalTimeZone));
        if (Duzenlenen is { } d && Kullanicilar.FirstOrDefault(k => k.Id == d.Id) is { } yeni) Duzenlenen = yeni;
        else if (Duzenlenen is not null) Duzenlenen = null;
    }

    [RelayCommand]
    private void YeniRolSec(SecimCipi? cip) => Sec(YeniRoller, cip);

    [RelayCommand]
    private void DuzenRolSec(SecimCipi? cip)
    {
        if (!DuzenRolDegisebilir) return;
        Sec(DuzenRoller, cip);
    }

    private static void Sec(IReadOnlyList<SecimCipi> cipler, SecimCipi? cip)
    {
        if (cip is null) return;
        foreach (var c in cipler) c.Secili = ReferenceEquals(c, cip);
    }

    private static string SeciliRol(IReadOnlyList<SecimCipi> cipler)
        => cipler.FirstOrDefault(c => c.Secili)?.Ad == RolEditor ? "editor" : "viewer";

    [RelayCommand]
    private Task Ekle() => CalistirAsync(async () =>
    {
        Bilgi = null;
        var ad = YeniAdSoyad?.Trim() ?? "";
        var kullaniciAdi = YeniKullaniciAdi?.Trim() ?? "";
        Dogrula(ad.Length > 0, "Ad soyad yazın.");
        Dogrula(ad.Length <= 100, "Ad soyad en fazla 100 karakter olabilir.");
        Dogrula(KullaniciAdiKurali.IsMatch(kullaniciAdi), "Kullanıcı adı 3–50 karakter olmalı; harf, rakam, nokta, tire ve alt çizgi kullanılabilir.");
        Dogrula(YeniSifre.Length >= HesapGuvenligiViewModel.SifreEnAz, $"Şifre en az {HesapGuvenligiViewModel.SifreEnAz} karakter olmalı.");
        Dogrula(YeniSifre.Length <= HesapGuvenligiViewModel.SifreEnCok, $"Şifre en fazla {HesapGuvenligiViewModel.SifreEnCok} karakter olabilir.");
        var rol = SeciliRol(YeniRoller);
        await _api.KullaniciEkleAsync(new KullaniciEkle(ad, kullaniciAdi, rol, YeniSifre));
        YeniAdSoyad = YeniKullaniciAdi = null;
        YeniSifre = "";
        Sec(YeniRoller, YeniRoller[0]);
        Bilgi = $"{ad} eklendi. Kullanıcı adını ve şifreyi kendisine iletin.";
        await DoldurAsync();
    });

    [RelayCommand]
    private void Duzenle(KullaniciSatiri? k)
    {
        if (k is null) return;
        Bilgi = null;
        Duzenlenen = k;
        DuzenAdSoyad = k.AdSoyad;
        DuzenAktif = k.Aktif;
        DuzenSifre = "";
        Sec(DuzenRoller, DuzenRoller[k.EditorMu ? 1 : 0]);
    }

    [RelayCommand]
    private void DuzenIptal()
    {
        Duzenlenen = null;
        DuzenSifre = "";
    }

    /// <summary>Ad, rol, aktiflik; yeni şifre yazıldıysa şifre de değişir.</summary>
    [RelayCommand]
    private Task Kaydet() => Duzenlenen is not { } k ? Task.CompletedTask : CalistirAsync(async () =>
    {
        Bilgi = null;
        var ad = DuzenAdSoyad?.Trim() ?? "";
        Dogrula(ad.Length > 0, "Ad soyad yazın.");
        Dogrula(ad.Length <= 100, "Ad soyad en fazla 100 karakter olabilir.");
        var sifreDegisecek = DuzenSifre.Length > 0;
        if (sifreDegisecek)
        {
            Dogrula(!k.Ben, KendiSifreMesaji);
            Dogrula(!k.Yerlesik, "Bu hesabın şifresi yalnız kendi Hesabım bölümünden değişir.");
            Dogrula(DuzenSifre.Length >= HesapGuvenligiViewModel.SifreEnAz, $"Şifre en az {HesapGuvenligiViewModel.SifreEnAz} karakter olmalı.");
            Dogrula(DuzenSifre.Length <= HesapGuvenligiViewModel.SifreEnCok, $"Şifre en fazla {HesapGuvenligiViewModel.SifreEnCok} karakter olabilir.");
        }
        // Yerleşik hesapta ve kendi hesabınızda rol/aktiflik olduğu gibi gider (sunucu değişikliği reddeder).
        var sabit = k.Yerlesik || k.Ben;
        var rol = sabit ? k.Dto.Rol : SeciliRol(DuzenRoller);
        var aktif = sabit ? k.Yerlesik || k.Aktif : DuzenAktif;
        await _api.KullaniciGuncelleAsync(k.Id, new KullaniciGuncelle(ad, rol, aktif));
        if (sifreDegisecek) await _api.KullaniciSifreAsync(k.Id, DuzenSifre);
        Duzenlenen = null;
        DuzenSifre = "";
        Bilgi = sifreDegisecek ? $"{ad} kaydedildi; şifresi değişti ve açık oturumları kapandı." : $"{ad} kaydedildi.";
        await DoldurAsync();
    });

    [RelayCommand]
    private Task OturumlariniKapat(KullaniciSatiri? k) => k is null ? Task.CompletedTask : CalistirAsync(async () =>
    {
        Bilgi = null;
        Dogrula(!k.Ben, KendiOturumlariMesaji);
        await _api.KullaniciOturumlariniKapatAsync(k.Id);
        Bilgi = $"{k.AdSoyad}: tüm oturumları kapatıldı.";
        await DoldurAsync();
    });

    [RelayCommand]
    private Task IkiAdimKapat(KullaniciSatiri? k) => k is null ? Task.CompletedTask : CalistirAsync(async () =>
    {
        Bilgi = null;
        Dogrula(!k.Ben, KendiIkiAdimMesaji);
        await _api.KullaniciIkiAdimKapatAsync(k.Id);
        Bilgi = $"{k.AdSoyad}: iki adımlı giriş kapatıldı; yalnız şifreyle girer.";
        await DoldurAsync();
    });

    /// <summary>İlk basış onay ister, ikinci basış siler.</summary>
    [RelayCommand]
    private Task Sil(KullaniciSatiri? k)
    {
        if (k is null) return Task.CompletedTask;
        if (k.Ben || k.Yerlesik)
            return CalistirAsync(() =>
            {
                Dogrula(false, k.Ben ? KendiSilMesaji : "Sunucu hesabı silinemez.");
                return Task.CompletedTask;
            });
        if (!k.SilOnayBekliyor)
        {
            foreach (var s in Kullanicilar) s.SilOnayBekliyor = ReferenceEquals(s, k);
            return Task.CompletedTask;
        }
        return CalistirAsync(async () =>
        {
            Bilgi = null;
            await _api.KullaniciSilAsync(k.Id);
            if (Duzenlenen?.Id == k.Id) Duzenlenen = null;
            Bilgi = $"{k.AdSoyad} silindi; oturumları kapandı.";
            await DoldurAsync();
        });
    }

    [RelayCommand]
    private Task IzleyiciSifresiniKaldir()
    {
        if (!IzleyiciKaldirOnayBekliyor)
        {
            IzleyiciKaldirOnayBekliyor = true;
            return Task.CompletedTask;
        }
        IzleyiciKaldirOnayBekliyor = false;
        return CalistirAsync(async () =>
        {
            Bilgi = null;
            await _api.IzleyiciSifresiniKaldirAsync();
            Bilgi = "Ortak izleyici şifresi kaldırıldı; o şifreyle açılmış oturumlar kapandı.";
        });
    }
}

/// <summary>Kullanıcı listesindeki bir satır.</summary>
public partial class KullaniciSatiri : ObservableObject
{
    public KullaniciSatiri(KullaniciDto d, TimeZoneInfo tz)
    {
        Dto = d;
        var parcalar = new List<string> { d.KullaniciAdi, GecmisSatiri.RolMetni(d.Rol) };
        if (d.Yerlesik) parcalar.Add("sunucu hesabı");
        if (d.Ben) parcalar.Add("siz");
        if (d.IkiAdimAcik) parcalar.Add("iki adımlı giriş açık");
        Ayrinti = string.Join(" · ", parcalar);
        SonGiris = d.SonGirisUtc is { } g
            ? $"Son giriş {YerelZaman.Metin(g, tz)}{(string.IsNullOrWhiteSpace(d.SonCihaz) ? "" : " · " + d.SonCihaz)} · {d.AcikOturum} açık oturum"
            : $"Henüz giriş yok · {d.AcikOturum} açık oturum";
    }

    public KullaniciDto Dto { get; }
    public int Id => Dto.Id;
    public string AdSoyad => Dto.AdSoyad;
    public string KullaniciAdi => Dto.KullaniciAdi;
    public bool Aktif => Dto.Aktif;
    public bool Yerlesik => Dto.Yerlesik;
    public bool EditorMu => Dto.Rol == "editor";
    public bool IkiAdimAcik => Dto.IkiAdimAcik;
    /// <summary>Giriş yapmış editörün kendi satırı.</summary>
    public bool Ben => Dto.Ben;

    /// <summary>"emar · Editör · iki adımlı giriş açık".</summary>
    public string Ayrinti { get; }
    /// <summary>"Son giriş 24.09.2026 12:00 · EMAR-LAPTOP · 1 açık oturum".</summary>
    public string SonGiris { get; }

    /// <summary>Yerleşik hesap ve kendi hesabınız silinemez.</summary>
    public bool Silinebilir => !Yerlesik && !Ben;
    /// <summary>Kendi oturumlarınız Oturumlar listesinden tek tek kapatılır.</summary>
    public bool OturumlariKapatilabilir => !Ben;
    /// <summary>Başkasının iki adımlı girişi (telefonu kaybolduysa) kapatılabilir; kendinizinki Hesabım'dan.</summary>
    public bool IkiAdimKapatilabilir => IkiAdimAcik && !Ben;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SilMetni))]
    private bool _silOnayBekliyor;

    public string SilMetni => SilOnayBekliyor ? "Emin misiniz? Sil" : "Sil";
}
