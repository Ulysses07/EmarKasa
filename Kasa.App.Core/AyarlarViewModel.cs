using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Editör ayarları: kanal CRUD + izleyici şifre + takip başlangıç/açılış devri (spec §6). Yükleme ve bütün kayıtlar
/// yürütücünün tekil işlemidir (appcore-10): biri sürerken ötekisi başlamaz (yükleme sürerken form gönderilmez), sonuç başladığı
/// neslin hâlâ geçerli olduğu denetlenerek uygulanır.</summary>
public partial class AyarlarViewModel : OturumluViewModel
{
    private readonly IKasaApi _api;
    private readonly IAylikGiderApi? _kilit;
    /// <param name="auth">Model oturum değişimini kendisi alır (<see cref="OturumluViewModel"/>): bekleyen yükleme ve kayıtlar eskir
    /// (sonuçları, hataları ve bitişleri yansımaz, yeni oturum onları beklemez), önceki oturumun ayarları, kanalları, formları,
    /// onayları ve yazılmış izleyici şifresi kalkar.</param>
    /// <param name="kilit">Ay kilidi durumu: yalnız kanal formundaki kilit notu için (kuralı sunucu uygular).</param>
    public AyarlarViewModel(IKasaApi api, AuthViewModel auth, IAylikGiderApi? kilit = null) : base(auth)
    {
        _api = api;
        _kilit = kilit;
    }

    public ObservableCollection<KanalDto> Kanallar { get; } = new();

    [ObservableProperty] private DateTime _takipBaslangic = DateTime.Today;
    [ObservableProperty] private decimal _kasaAcilisDevri;
    [ObservableProperty] private string? _ayarUyarisi;
    /// <summary>Ayarlar bu oturumda sunucudan başarıyla okundu mu? Okunmadan formda varsayılanlar durur (takip başlangıcı bugün,
    /// açılış devri 0); yükleme başarısızken 'Ayarı kaydet' bunları sunucuya göndermesin diye kayıt yalnız başarılı yüklemeden
    /// sonra çalışır (düğme devre dışı, çağrılırsa <see cref="AyarlarYuklenmediMesaji"/>). Oturum değişince yeniden okunmalıdır.</summary>
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AyarKaydetCommand))] private bool _ayarlarYuklendi;
    public const string AyarlarYuklenmediMesaji = "Ayarlar sunucudan yüklenemedi; kayıtlı değerlerin üzerine varsayılanlar yazılmasın diye kaydedilmedi. Ekranı yenileyip yeniden deneyin.";

    /// <summary>Kanal formunun hataları (tasarım 2026-10-02 §1): ad ve açılış devri alanın altında, eşlenemeyen sunucu iletisi
    /// (ör. "Bu kanal adı zaten kullanılıyor.") formun genel hatasında.</summary>
    public AlanHatalari KanalHatalari { get; } = new();
    protected override IEnumerable<AlanHatalari> Formlar => [KanalHatalari];

    /// <summary>Sunucunun kanal doğrulama alanları (KanalEndpoints; küçük harf) → formun alanları.</summary>
    private static readonly Dictionary<string, string> KanalSunucuAlanlari = new()
    {
        ["ad"] = nameof(DuzenKanalAd),
        ["acilisdevri"] = nameof(DuzenKanalAcilisDevri),
    };

    // Kanal düzenleme
    [ObservableProperty] private int _duzenKanalId;      // 0 = yeni
    [ObservableProperty] private string _duzenKanalAd = "";
    [ObservableProperty] private bool _duzenKanalAktif = true;
    [ObservableProperty] private int _duzenKanalSira;
    [ObservableProperty] private decimal _duzenKanalAcilisDevri;
    [ObservableProperty] private string? _kanalUyarisi;
    // contract-6: düzenlenen kanalın ve ayarların okunduğu andaki sürümü; kayıtla gönderilir, kayıt arada başka oturumda değiştiyse
    // sunucu 409 verir. 409'da liste ve ayarlar yeniden yüklenir (ileti gösterilir).
    private int _duzenKanalSurum, _ayarSurum;

    // Tamamlanmış ayların kanal kümesi sunucuda dondurulur: kanal eklemek, pasife almak ve sırasını değiştirmek ay kilidi varken de
    // serbesttir, yalnız açılış devri kilitte değişmez. Not bunu söyler; kilit durumu okunamazsa not gösterilmez.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(KanalKilitNotu))] private DateOnly? _kilitliSonTarih;
    public string? KanalKilitNotu => KilitliSonTarih is { } son
        ? $"{son:dd.MM.yyyy} dahil aylar kilitli: kanal açılış devri değiştirilemez (yeni kanal açılış devri 0 ile eklenir). Kanal eklemek, "
          + "pasife almak ve sırasını değiştirmek serbesttir; tamamlanmış ayların Ortak gider dağılımı değişmez."
        : null;

    // İzleyici şifre: sunucu ve web ile aynı kural ve ileti (yalnız belirlerken/değiştirirken).
    public const string IzleyiciSifreKuralMesaji = "İzleyici şifresi 12–1024 karakter olmalıdır.";
    // Sunucu, kayıtlı şifrenin kurala uymadığını ancak bir izleyici girişinde görür (hash uzunluk saklamaz).
    public const string IzleyiciSifreKisaMesaji = "Mevcut izleyici şifresi 12 karakterden kısa (son izleyici girişinde görüldü). Kurala uygun yeni bir şifre belirleyin.";
    [ObservableProperty] private string _yeniIzleyiciSifre = "";
    [ObservableProperty] private string? _izleyiciSifreHatasi;
    [ObservableProperty] private string? _izleyiciSifreMesaji;
    [ObservableProperty] private string? _izleyiciSifreUyarisi;
    // Sunucunun yanlış vekil ayarı uyarısı (hız sınırları tüm istemcileri tek IP sayabilir); yoksa null.
    [ObservableProperty] private string? _vekilUyarisi;

    public static bool IzleyiciSifresiGecerli(string? sifre) => !string.IsNullOrWhiteSpace(sifre) && sifre.Length is >= 12 and <= 1024;

    // Boşaltılan para alanı 0 olur: kayıtlı sıfır olmayan açılış devrini 0'a indirmek, gelir formundaki gibi
    // ikinci basışta kaydedilir. Değer ya da düzenlenen alan/kanal değişince onay sıfırlanır.
    private decimal _kayitliKasaAcilisDevri, _kayitliKanalAcilisDevri;
    private bool _kasaSifirOnayi, _kanalSifirOnayi;

    partial void OnTakipBaslangicChanged(DateTime value) => AyarOnayiniSifirla();
    partial void OnKasaAcilisDevriChanged(decimal value) => AyarOnayiniSifirla();
    partial void OnDuzenKanalIdChanged(int value) => KanalOnayiniSifirla();
    partial void OnDuzenKanalAcilisDevriChanged(decimal value) => KanalOnayiniSifirla();
    private void AyarOnayiniSifirla() { _kasaSifirOnayi = false; AyarUyarisi = null; }
    private void KanalOnayiniSifirla() { _kanalSifirOnayi = false; KanalUyarisi = null; }

    /// <summary>Kayıtlı sıfır olmayan tutar 0'a iniyorsa ve henüz onaylanmadıysa onay metni; aksi halde null.</summary>
    private static string? SifirOnayMetni(string ad, decimal kayitli, decimal yeni, bool onaylandi) =>
        kayitli != 0 && yeni == 0 && !onaylandi
            ? $"{ad} {Bicim.Tl(kayitli)} ₺ yerine 0,00 ₺ yapılacak. Alan boş bırakılmış olabilir. Onaylamak için yeniden kaydedin."
            : null;

    /// <summary>Ayarları, kanalları ve kilit notunu okur; her okumadan sonra nesil denetlenir: oturum değiştiyse sonuç yeni
    /// oturumun ekranına yazılmaz.</summary>
    private async Task DoldurAsync(int n)
    {
        var ayar = await _api.AyarlarAsync();
        if (!Gecerli(n))
            return;
        AyarlariUygula(ayar);
        var kanallar = await _api.KanallarAsync();
        if (!Gecerli(n))
            return;
        Kanallar.Clear();
        foreach (var k in kanallar)
            Kanallar.Add(k);
        var kilitSonu = await KilitSonuAsync();
        if (Gecerli(n))
            KilitliSonTarih = kilitSonu;
    }

    private void AyarlariUygula(AyarlarDto ayar)
    {
        TakipBaslangic = ayar.TakipBaslangic.ToDateTime(TimeOnly.MinValue);
        KasaAcilisDevri = _kayitliKasaAcilisDevri = ayar.KasaAcilisDevri;
        _ayarSurum = ayar.Surum;
        AyarlarYuklendi = true;
        IzleyiciSifreUyarisi = ayar.IzleyiciSifreKisa ? IzleyiciSifreKisaMesaji : null;
        VekilUyarisi = ayar.VekilUyarisi;
    }

    private async Task<DateOnly?> KilitSonuAsync()
    {
        if (_kilit is null)
            return null;
        try
        { return (await _kilit.AyKilidiAsync()).KilitliSonTarih; }
        catch (KasaApiException e) when (e.DurumKodu != HttpStatusCode.Unauthorized) { return null; }
    }

    public Task YukleAsync() => YurutAsync(DoldurAsync);

    /// <summary>Oturum değişince (nesil artmış, bekleyen işler eskimiştir; gösterge ve hata tabanda kalkar) ekran yeni kurulmuş
    /// modelin durumuna döner: önceki oturumun ayarları, kanalları, formları, sıfır onayları, iletileri ve yazılmış izleyici şifresi
    /// kalkar.</summary>
    protected override void OturumTemizle()
    {
        Kanallar.Clear();
        TakipBaslangic = DateTime.Today;
        KasaAcilisDevri = _kayitliKasaAcilisDevri = 0;
        _ayarSurum = 0;
        AyarlarYuklendi = false;
        YeniKanal();
        AyarOnayiniSifirla();
        KanalOnayiniSifirla();
        KilitliSonTarih = null;
        VekilUyarisi = null;
        YeniIzleyiciSifre = "";
        IzleyiciSifreHatasi = null;
        IzleyiciSifreMesaji = null;
        IzleyiciSifreUyarisi = null;
    }

    [RelayCommand]
    private void YeniKanal()
    {
        DuzenKanalId = 0;
        DuzenKanalAd = "";
        DuzenKanalAktif = true;
        DuzenKanalSira = 0;
        DuzenKanalAcilisDevri = _kayitliKanalAcilisDevri = 0;
        _duzenKanalSurum = 0;
        KanalHatalari.Temizle();
    }

    [RelayCommand]
    public void KanalDuzenle(KanalDto k)
    {
        DuzenKanalId = k.Id;
        DuzenKanalAd = k.Ad;
        DuzenKanalAktif = k.Aktif;
        DuzenKanalSira = k.Sira;
        DuzenKanalAcilisDevri = _kayitliKanalAcilisDevri = k.AcilisDevri;
        _duzenKanalSurum = k.Surum;
        KanalHatalari.Temizle();
    }

    [RelayCommand]
    private Task KanalKaydetAsync() => FormIsleAsync(KanalHatalari, async n =>
    {
        KanalHatalari.Denetle(!string.IsNullOrWhiteSpace(DuzenKanalAd), nameof(DuzenKanalAd), "Kanal adı boş olamaz.");
        KanalHatalari.Denetle(ParaAyristirici.GecerliMi(DuzenKanalAcilisDevri), nameof(DuzenKanalAcilisDevri), ParaAyristirici.GecersizMesaji);
        if (KanalHatalari.Var)
            return;
        if (SifirOnayMetni($"{DuzenKanalAd} açılış devri", _kayitliKanalAcilisDevri, DuzenKanalAcilisDevri, _kanalSifirOnayi) is { } onay)
        {
            _kanalSifirOnayi = true;
            KanalUyarisi = onay;
            return;
        }
        var g = new KanalYaz(DuzenKanalAd, DuzenKanalAktif, DuzenKanalSira, DuzenKanalAcilisDevri, _duzenKanalSurum);
        var id = DuzenKanalId;
        try
        {
            if (id == 0)
                await _api.KanalOlusturAsync(g);
            else
                await _api.KanalGuncelleAsync(id, g);
        }
        // Kanal arada başka oturumda değiştiyse liste güncel kayıtlarla yenilenir; form korunur, kanal listeden yeniden seçilince
        // güncel sürümüyle kaydedilir.
        catch (KasaApiException e) when (e.DurumKodu == HttpStatusCode.Conflict && id != 0)
        {
            if (Gecerli(n))
                await EnIyiCaba(() => DoldurAsync(n));
            throw;
        }
        if (!Gecerli(n))
            return;
        YeniKanal();
        KanalOnayiniSifirla();
        // Kanal kaydedildi: listenin yeniden okunamaması kaydı geri almaz, formun hatası değildir (sayfanın okuma hatasıdır).
        try
        { await DoldurAsync(n); }
        catch (Exception hata) when (Gecerli(n))
        { Yurutucu.OkumaHatasiniYaz(hata); }
    }, KanalSunucuAlanlari);

    /// <summary>Onay diyaloğundan sonra gelir: başka işlem (yükleme, kayıt) sürerken silme yapılmaz ve bu söylenir (sessizce yok
    /// sayılmaz).</summary>
    [RelayCommand]
    private Task KanalSilAsync(KanalDto k) => YurutAsync(async n =>
    {
        await _api.KanalSilAsync(k.Id);
        if (!Gecerli(n))
            return;
        await DoldurAsync(n);
    }, mesgulkenBildir: true);

    [RelayCommand(CanExecute = nameof(AyarlarYuklendi))]
    private Task AyarKaydetAsync() => YurutAsync(async n =>
    {
        if (!ParaAyristirici.GecerliMi(KasaAcilisDevri))
        { Hata = ParaAyristirici.GecersizMesaji; return; }
        if (!AyarlarYuklendi)
        { Hata = AyarlarYuklenmediMesaji; return; }
        if (SifirOnayMetni("Kasa açılış devri", _kayitliKasaAcilisDevri, KasaAcilisDevri, _kasaSifirOnayi) is { } onay)
        {
            _kasaSifirOnayi = true;
            AyarUyarisi = onay;
            return;
        }
        var devir = KasaAcilisDevri;
        try
        { await _api.AyarGuncelleAsync(new AyarYaz(DateOnly.FromDateTime(TakipBaslangic), devir, _ayarSurum)); }
        // Ayarlar arada başka oturumda değiştiyse güncel değerler forma yüklenir (ileti gösterilir); kullanıcı onları görerek yeniden kaydeder.
        catch (KasaApiException e) when (e.DurumKodu == HttpStatusCode.Conflict)
        {
            if (Gecerli(n))
                await EnIyiCaba(async () => { if (await AyarlariOkuAsync(n)) AyarOnayiniSifirla(); });
            throw;
        }
        if (!Gecerli(n))
            return;
        _kayitliKasaAcilisDevri = devir;
        AyarOnayiniSifirla();
        // Kayıt sürümü artırır (yanıt gövdesizdir): sonraki kayıt için güncel sürüm ve değerler yeniden okunur. Okuma en iyi çabadır:
        // okunamazsa kayıt yine alınmıştır (hata gösterilmez); sonraki kayıt 409 alır ve güncel değerler o zaman yüklenir.
        await EnIyiCaba(async () => { if (await AyarlariOkuAsync(n)) AyarOnayiniSifirla(); });
    });

    /// <summary>Kayıttan sonraki yeniden okuma en iyi çabadır: okuma hatası kaydın sonucunu ya da çakışma iletisini örtmez (oturum
    /// sonu hariç: 401 yine yüzeye çıkar).</summary>
    private static async Task EnIyiCaba(Func<Task> oku)
    {
        try
        { await oku(); }
        catch (Exception e) when (e is not KasaApiException { DurumKodu: HttpStatusCode.Unauthorized }) { }
    }

    /// <summary>Ayarları yeniden okuyup forma uygular; oturum arada değiştiyse uygulamaz (false).</summary>
    private async Task<bool> AyarlariOkuAsync(int n)
    {
        var ayar = await _api.AyarlarAsync();
        if (!Gecerli(n))
            return false;
        AyarlariUygula(ayar);
        return true;
    }

    // Hata ve onay izleyici kartında gösterilir (kanal kartındaki genel Hata kutusuna düşmez).
    [RelayCommand]
    private Task IzleyiciSifreKaydetAsync() => YurutAsync(async n =>
    {
        IzleyiciSifreHatasi = null;
        IzleyiciSifreMesaji = null;
        if (!IzleyiciSifresiGecerli(YeniIzleyiciSifre))
        { IzleyiciSifreHatasi = IzleyiciSifreKuralMesaji; return; }
        try
        { await _api.IzleyiciSifreAsync(YeniIzleyiciSifre); }
        catch (Exception hata) { if (Gecerli(n)) IzleyiciSifreHatasi = HataMesaji(hata); return; }
        if (!Gecerli(n))
            return;
        YeniIzleyiciSifre = "";
        IzleyiciSifreUyarisi = null;
        IzleyiciSifreMesaji = "İzleyici şifresi güncellendi. Eski izleyici oturumları kapandı.";
    });
}
