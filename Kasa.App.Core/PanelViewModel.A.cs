using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Paket A — Panel'in ek bölümleri: hızlı durum kartları, "son bakışınızdan beri" satırı, bugün
/// yapılacaklar (editör) ve nakit tahmini. Hepsi yalnız okur ve gösterir; hiçbir rakamı değiştirmez.
/// Her bölüm kendi okumasının hatasını yutar (bölüm gizlenir ya da kendi hata satırını gösterir):
/// ana panel (kasa, kanallar, bekleyen giderler) bu bölümlerden etkilenmez.
/// </summary>
public partial class PanelViewModel
{
    private readonly IYerelDepo _depo;

    /// <summary>Tahmin ufku seçenekleri (gün).</summary>
    public static IReadOnlyList<int> TahminUfuklari { get; } = [30, 60, 90];

    /// <summary>
    /// Bir satır/kart bir sayfaya gitmek istedi: Shell rotası ("//cekler") ya da Panel içi hedef
    /// (<see cref="YapilacakListesi.BekleyenKarti"/>: bekleyen giderler kartına kaydır). Sayfa uygular.
    /// </summary>
    public event EventHandler<string>? GitIstendi;

    private void PaketAKur()
    {
        var gun = _depo.OkuInt(YerelAnahtarlar.TahminGun);
        TahminGun = gun is { } g && TahminUfuklari.Contains(g) ? g : TahminUfuklari[0];
        foreach (var u in TahminUfuklari) TahminCipleri.Add(new SecimCipi($"{u} gün") { Secili = u == TahminGun });
        Yapilacaklar.CollectionChanged += (_, _) => OnPropertyChanged(nameof(YapilacakVar));
    }

    partial void OnEditorMuChanged(bool value) => OnPropertyChanged(nameof(YapilacakVar));

    // ───────────── Hızlı durum kartları ─────────────

    /// <summary>Kart kutusu: en az bir kart varsa.</summary>
    [ObservableProperty] private bool _kartDurumuVar;
    /// <summary>Ödenmemiş ekstre borçlarının toplamı.</summary>
    [ObservableProperty] private decimal _kartEkstreToplam;
    /// <summary>"En yakın son ödeme: Bonus · 30 Eylül" / "Ödenmemiş ekstre yok".</summary>
    [ObservableProperty] private string _kartVadeMetni = "";
    /// <summary>En yakın son ödeme bugün ya da geçmişte mi (vurgu)?</summary>
    [ObservableProperty] private bool _kartVadeAcil;
    /// <summary>"Limit uyarısı: Bonus %85 · Axess limit aşıldı"; uyarı yoksa null.</summary>
    [ObservableProperty] private string? _limitUyariMetni;

    [ObservableProperty] private bool _cekDurumuVar;
    [ObservableProperty] private decimal _cekTahsilToplam;
    [ObservableProperty] private string _cekTahsilMetni = "";   // "3 alınan çek"
    [ObservableProperty] private decimal _cekOdemeToplam;
    [ObservableProperty] private string _cekOdemeMetni = "";    // "2 verilen çek"
    /// <summary>"2 çekin vadesi geçti · 5.000,00 ₺"; yoksa null.</summary>
    [ObservableProperty] private string? _cekGecikmeMetni;

    [ObservableProperty] private bool _sayimDurumuVar;
    /// <summary>"20 Eylül (4 gün önce)" / "Henüz sayım yok".</summary>
    [ObservableProperty] private string _sayimMetni = "";
    /// <summary>Son sayımın farkı (sayılan − defter); sayım yoksa null.</summary>
    [ObservableProperty] private decimal? _sayimFarki;
    [ObservableProperty] private string? _sayimFarkMetni;

    /// <summary>"Defter en son 2 saat önce güncellendi"; bilinmiyorsa null.</summary>
    [ObservableProperty] private string? _defterGuncellemeMetni;

    // ───────────── Son bakıştan beri (geçmiş) ─────────────

    /// <summary>"Son bakışınızdan beri 12 değişiklik, 2'si geçmiş aylara dokunuyor"; yeni yoksa null.</summary>
    [ObservableProperty] private string? _yeniDegisiklikMetni;
    [ObservableProperty] private bool _gecmiseDonukVar;
    /// <summary>Geçmiş ayları etkileyen en yeni (en fazla 5) değişiklik.</summary>
    public ObservableCollection<GecmisOzetSatiriDto> GecmiseDonukSatirlar { get; } = new();
    private int? _sonGecmisId;

    // ───────────── Bugün yapılacaklar (editör) ─────────────

    public ObservableCollection<YapilacakSatiri> Yapilacaklar { get; } = new();
    public bool YapilacakVar => EditorMu && Yapilacaklar.Count > 0;

    // ───────────── Nakit tahmini ─────────────

    public ObservableCollection<SecimCipi> TahminCipleri { get; } = new();
    [ObservableProperty] private int _tahminGun;
    /// <summary>Tahmin bölümü: sunucu tahmini döndürdüyse (eski sunucuda gizli).</summary>
    [ObservableProperty] private bool _tahminVar;
    [ObservableProperty] private string? _tahminHata;
    [ObservableProperty] private bool _tahminYukleniyor;
    /// <summary>"En düşük: 14 Kasım, −12.500,00 ₺".</summary>
    [ObservableProperty] private string _enDusukMetni = "";
    [ObservableProperty] private bool _enDusukNegatif;
    /// <summary>"90 gün sonra 45.000,00 ₺ · giriş +…, çıkış −…".</summary>
    [ObservableProperty] private string _tahminOzetMetni = "";
    /// <summary>Hareket olan günler (ve en düşük gün), tarihe göre.</summary>
    public ObservableCollection<TahminGunuGorunum> TahminGunleri { get; } = new();
    /// <summary>Tahmindeki çekler: kullanıcı riskli olanı hesaptan çıkarabilir (cihazda saklanır).</summary>
    public ObservableCollection<TahminCekSecenegi> TahminCekleri { get; } = new();
    [ObservableProperty] private bool _tahminCekVar;
    /// <summary>Son yüklenen tahmin (testler ve bildirimler için).</summary>
    public NakitTahminDto? SonTahmin { get; private set; }

    private int _tahminSurum;

    private async Task EkBolumleriYukleAsync(IReadOnlyList<BekleyenGiderDto> bekleyenler)
    {
        var bugun = BugunTarih;
        var kartGorevi = Guvenli(_api.KrediKartlariAsync);
        var cekGorevi = Guvenli(_api.CekOzetAsync);
        var sayimGorevi = Guvenli(_api.KasaSayimlariAsync);
        var gecmisGorevi = Guvenli(() => _api.GecmisOzetAsync(_depo.OkuInt(YerelAnahtarlar.GecmisSonGorulenId)));
        var eksikGorevi = EditorMu ? Guvenli(_api.EksikGelenlerAsync) : Task.FromResult<IReadOnlyList<EksikGelenDto>?>(null);
        var tahminGorevi = TahminYukleAsync();
        await Task.WhenAll(kartGorevi, cekGorevi, sayimGorevi, gecmisGorevi, eksikGorevi, tahminGorevi);

        KartDurumunuKur(kartGorevi.Result, bugun);
        CekDurumunuKur(cekGorevi.Result);
        SayimDurumunuKur(sayimGorevi.Result, bugun);
        GecmisOzetiniKur(gecmisGorevi.Result);

        Yapilacaklar.Clear();
        if (EditorMu)
            foreach (var y in YapilacakListesi.Olustur(bekleyenler, cekGorevi.Result, kartGorevi.Result, eksikGorevi.Result, sayimGorevi.Result, bugun))
                Yapilacaklar.Add(y);
    }

    /// <summary>Okuma hatası (404 = eski sunucu dahil) bölümü gizler; ana paneli bozmaz.</summary>
    private static async Task<T?> Guvenli<T>(Func<Task<T>> okuma) where T : class
    {
        try { return await okuma(); }
        catch (Exception) { return null; }
    }

    private void KartDurumunuKur(IReadOnlyList<KrediKartiDto>? kartlar, DateOnly bugun)
    {
        KartDurumuVar = kartlar is { Count: > 0 };
        if (kartlar is not { Count: > 0 }) { LimitUyariMetni = null; return; }
        var gorunumler = kartlar.Select(d => new KrediKartiGorunum(d, bugun.ToDateTime(TimeOnly.MinValue))).ToList();
        KartEkstreToplam = gorunumler.Sum(k => Math.Max(0m, k.EkstreBorc));
        var enYakin = gorunumler.Where(k => k.EkstreBorc > 0)
            .Select(k => (Kart: k, Vade: KartHatirlatici.AcikEkstre(k, bugun).SonOdeme))
            .OrderBy(x => x.Vade).ThenBy(x => x.Kart.Ad, StringComparer.Create(Kultur.Turkce, false))
            .FirstOrDefault();
        if (enYakin.Kart is null)
        {
            KartVadeMetni = "Ödenmemiş ekstre yok";
            KartVadeAcil = false;
        }
        else
        {
            var ne = enYakin.Vade < bugun ? $"{PanelMetin.Gun(enYakin.Vade, bugun)} (geçti)" : PanelMetin.GoreliGun(enYakin.Vade, bugun);
            KartVadeMetni = $"En yakın son ödeme: {enYakin.Kart.Ad} · {ne}";
            KartVadeAcil = enYakin.Vade <= bugun;
        }
        var uyarilar = gorunumler.Where(k => k.LimitUyarisi)
            .Select(k => k.GuncelBorc > k.Limit ? $"{k.Ad} limit aşıldı" : $"{k.Ad} %{KartLimit.Yuzde(k.GuncelBorc, k.Limit)}")
            .ToList();
        LimitUyariMetni = uyarilar.Count > 0 ? "Limit uyarısı: " + string.Join(" · ", uyarilar) : null;
    }

    private void CekDurumunuKur(CekOzetDto? o)
    {
        CekDurumuVar = o is not null;
        if (o is null) return;
        CekTahsilToplam = o.PortfoydekiAlinanToplam;
        CekTahsilMetni = $"{o.PortfoydekiAlinanAdet} alınan çek";
        CekOdemeToplam = o.OdenecekVerilenToplam;
        CekOdemeMetni = $"{o.OdenecekVerilenAdet} verilen çek";
        CekGecikmeMetni = o.VadesiGecenler.Count > 0
            ? $"{o.VadesiGecenler.Count} çekin vadesi geçti · {PanelMetin.Tutar(o.VadesiGecenler.Sum(c => c.Tutar))}"
            : null;
    }

    private void SayimDurumunuKur(IReadOnlyList<KasaSayimDto>? sayimlar, DateOnly bugun)
    {
        SayimDurumuVar = sayimlar is not null;
        if (sayimlar is null) return;
        var son = sayimlar.OrderByDescending(s => s.Tarih).ThenByDescending(s => s.Id).FirstOrDefault();
        if (son is null)
        {
            SayimMetni = "Henüz sayım yok";
            SayimFarki = null;
            SayimFarkMetni = null;
            return;
        }
        var gun = bugun.DayNumber - son.Tarih.DayNumber;
        var once = gun switch { 0 => "bugün", 1 => "dün", < 0 => "ileri tarih", _ => $"{gun} gün önce" };
        SayimMetni = $"{PanelMetin.Gun(son.Tarih, bugun)} ({once})";
        SayimFarki = son.Fark;
        SayimFarkMetni = son.Fark == 0 ? "Fark yok" : $"Fark {PanelMetin.IsaretliTutar(son.Fark)}";
    }

    private void GecmisOzetiniKur(GecmisOzetDto? o)
    {
        GecmiseDonukSatirlar.Clear();
        YeniDegisiklikMetni = null;
        GecmiseDonukVar = false;
        DefterGuncellemeMetni = null;
        if (o is null) return;
        _sonGecmisId = o.SonId;
        if (o.SonZamanUtc is { } z)
            DefterGuncellemeMetni = $"Defter en son {GoreceZaman.Metin(z, Zaman.GetLocalNow(), Zaman.LocalTimeZone)} güncellendi";

        var gorulen = _depo.OkuInt(YerelAnahtarlar.GecmisSonGorulenId);
        // İlk açılış (ya da sunucu geçmişi sıfırlandı): birikmiş eski satırlar "yeni" sayılmaz.
        if (gorulen is null || gorulen > o.SonId)
        {
            _depo.YazInt(YerelAnahtarlar.GecmisSonGorulenId, o.SonId);
            return;
        }
        if (o.Toplam <= 0) return;
        YeniDegisiklikMetni = DegisiklikMetni(o.Toplam, o.GecmiseDonuk);
        GecmiseDonukVar = o.GecmiseDonuk > 0;
        foreach (var s in o.GecmiseDonukSatirlar) GecmiseDonukSatirlar.Add(s);
    }

    /// <summary>"Son bakışınızdan beri 12 değişiklik, 2'si geçmiş aylara dokunuyor".</summary>
    public static string DegisiklikMetni(int toplam, int gecmiseDonuk)
    {
        var metin = $"Son bakışınızdan beri {toplam} değişiklik";
        if (gecmiseDonuk <= 0) return metin;
        return gecmiseDonuk == toplam && toplam > 1
            ? $"{metin}, hepsi geçmiş aylara dokunuyor"
            : $"{metin}, {gecmiseDonuk}'{PanelMetin.SayiEki(gecmiseDonuk)} geçmiş aylara dokunuyor";
    }

    /// <summary>"Tamam": yeni değişiklikler görüldü sayılır (bu cihazda).</summary>
    [RelayCommand]
    private void DegisiklikleriGorulduSay()
    {
        if (_sonGecmisId is { } id) _depo.YazInt(YerelAnahtarlar.GecmisSonGorulenId, id);
        YeniDegisiklikMetni = null;
        GecmiseDonukVar = false;
        GecmiseDonukSatirlar.Clear();
    }

    /// <summary>Kart/satır dokunuşu: hedefe git (sayfa uygular).</summary>
    [RelayCommand]
    private void Git(string? hedef)
    {
        if (!string.IsNullOrWhiteSpace(hedef)) GitIstendi?.Invoke(this, hedef);
    }

    [RelayCommand]
    private void YapilacakAc(YapilacakSatiri? s)
    {
        if (s is not null) GitIstendi?.Invoke(this, s.Hedef);
    }

    // ───────────── Nakit tahmini ─────────────

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SecTahminGunAsync(SecimCipi c)
    {
        var gun = TahminUfuklari.FirstOrDefault(u => c.Ad == $"{u} gün");
        if (gun == 0) return Task.CompletedTask;
        TahminGun = gun;
        _depo.YazInt(YerelAnahtarlar.TahminGun, gun);
        foreach (var x in TahminCipleri) x.Secili = x == c;
        return TahminYukleAsync();
    }

    /// <summary>Çeki hesaba kat / hesaptan çıkar (riskli çek); tahmin yeniden hesaplanır.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task TahminCekDegistirAsync(TahminCekSecenegi c)
    {
        var haric = _depo.OkuIdler(YerelAnahtarlar.TahminHaricCekler).ToHashSet();
        if (c.Dahil) haric.Add(c.CekId); else haric.Remove(c.CekId);
        _depo.YazIdler(YerelAnahtarlar.TahminHaricCekler, haric);
        c.Dahil = !c.Dahil;
        return TahminYukleAsync();
    }

    /// <summary>Tahmini seçili ufuk ve hariç çeklerle yükler; hata yalnız bu bölümde gösterilir.</summary>
    private async Task TahminYukleAsync()
    {
        var surum = ++_tahminSurum;
        var gun = TahminGun;
        var haric = _depo.OkuIdler(YerelAnahtarlar.TahminHaricCekler);
        TahminYukleniyor = true;
        try
        {
            var t = await _api.NakitTahminAsync(gun, haric.Count > 0 ? haric.ToList() : null);
            if (surum != _tahminSurum) return;   // bu arada başka ufuk seçildi
            TahminHata = null;
            TahminiKur(t, haric);
            TahminVar = true;
        }
        catch (KasaApiException ex) when (ex.DurumKodu == HttpStatusCode.NotFound)
        {
            if (surum == _tahminSurum) TahminVar = false;   // sunucu tahmini bilmiyor: bölüm gizli
        }
        catch (Exception ex)
        {
            if (surum != _tahminSurum) return;
            TahminHata = HataMesaji.Coz(ex);
            TahminVar = true;
        }
        finally
        {
            if (surum == _tahminSurum) TahminYukleniyor = false;
        }
    }

    private void TahminiKur(NakitTahminDto t, IReadOnlySet<int> haric)
    {
        SonTahmin = t;
        var bugun = t.Bugun;
        EnDusukMetni = t.EnDusukTarih == bugun
            ? $"En düşük: bugün, {PanelMetin.Tutar(t.EnDusukKasa)}"
            : $"En düşük: {PanelMetin.Gun(t.EnDusukTarih, bugun)}, {PanelMetin.Tutar(t.EnDusukKasa)}";
        EnDusukNegatif = t.EnDusukKasa < 0;
        TahminOzetMetni = $"{t.Gun} gün sonra {PanelMetin.Tutar(t.SonKasa)} · giriş {PanelMetin.IsaretliTutar(t.ToplamGiris)} · çıkış {PanelMetin.IsaretliTutar(-t.ToplamCikis)}";

        TahminGunleri.Clear();
        foreach (var g in t.Gunler.Where(g => g.Kalemler.Count > 0 || g.Tarih == t.EnDusukTarih))
            TahminGunleri.Add(new TahminGunuGorunum(g, bugun, g.Tarih == t.EnDusukTarih));

        TahminCekleri.Clear();
        var cekler = t.Gunler.SelectMany(g => g.Kalemler).Where(k => k.CekId is not null)
            .Select(k => (Kalem: k, Dahil: true))
            .Concat(t.HaricKalemler.Where(k => k.CekId is not null).Select(k => (Kalem: k, Dahil: false)))
            .OrderBy(x => x.Kalem.Tarih).ThenBy(x => x.Kalem.CekId);
        foreach (var (k, dahil) in cekler)
            TahminCekleri.Add(new TahminCekSecenegi(k, bugun, dahil && !haric.Contains(k.CekId!.Value)));
        TahminCekVar = TahminCekleri.Count > 0;
    }
}

/// <summary>Tahmin listesinin bir günü: tarih, kalemler ve gün sonu kasa.</summary>
public sealed class TahminGunuGorunum
{
    public TahminGunuGorunum(TahminGunuDto g, DateOnly bugun, bool enDusuk)
    {
        Tarih = g.Tarih;
        TarihMetni = g.Tarih.ToString(g.Tarih.Year == bugun.Year ? "d MMMM ddd" : "d MMMM yyyy ddd", Kultur.Turkce);
        Kasa = g.Kasa;
        KasaMetni = PanelMetin.Tutar(g.Kasa);
        KasaNegatif = g.Kasa < 0;
        EnDusukMu = enDusuk;
        Kalemler = g.Kalemler.Select(k => new TahminKalemGorunum(k, bugun)).ToList();
    }

    public DateOnly Tarih { get; }
    public string TarihMetni { get; }
    public decimal Kasa { get; }
    public string KasaMetni { get; }
    public bool KasaNegatif { get; }
    public bool EnDusukMu { get; }
    public IReadOnlyList<TahminKalemGorunum> Kalemler { get; }
}

/// <summary>Tahmin kalemi: açıklama + işaretli tutar (+ gecikmişse asıl günü).</summary>
public sealed class TahminKalemGorunum
{
    public TahminKalemGorunum(TahminKalemiDto k, DateOnly bugun)
    {
        Aciklama = k.Gecikmis ? $"{k.Aciklama} · {TurNotu(k.Tur)} {PanelMetin.Gun(k.Tarih, bugun)}" : k.Aciklama;
        Tutar = k.Tutar;
        TutarMetni = PanelMetin.IsaretliTutar(k.Tutar);
        Giris = k.Tutar > 0;
        Gecikmis = k.Gecikmis;
    }

    public string Aciklama { get; }
    public decimal Tutar { get; }
    public string TutarMetni { get; }
    public bool Giris { get; }
    public bool Gecikmis { get; }

    private static string TurNotu(TahminKalemTuru t) => t switch
    {
        TahminKalemTuru.KartOdemesi => "son ödeme",
        TahminKalemTuru.TekrarlayanGider => "vade",
        _ => "vade",
    };
}

/// <summary>Tahmindeki bir çek: "hesaba kat" anahtarı (riskli çek hesaptan çıkarılabilir).</summary>
public sealed partial class TahminCekSecenegi : ObservableObject
{
    public TahminCekSecenegi(TahminKalemiDto k, DateOnly bugun, bool dahil)
    {
        CekId = k.CekId ?? 0;
        Aciklama = k.Aciklama;
        TutarMetni = PanelMetin.IsaretliTutar(k.Tutar);
        TarihMetni = k.Gecikmis ? $"vade {PanelMetin.Gun(k.Tarih, bugun)} (geçti)" : $"vade {PanelMetin.Gun(k.Tarih, bugun)}";
        _dahil = dahil;
    }

    public int CekId { get; }
    public string Aciklama { get; }
    public string TutarMetni { get; }
    public string TarihMetni { get; }

    /// <summary>Hesaba katılıyor mu?</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DugmeMetni))]
    private bool _dahil;

    public string DugmeMetni => Dahil ? "Hesaptan çıkar" : "Hesaba kat";
}
