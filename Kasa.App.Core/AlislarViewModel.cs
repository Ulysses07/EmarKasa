using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using AlisDagitici = Kasa.Core.AlisDagitici;
using AlisKanalPayi = Kasa.Core.AlisKanalPayi;

namespace Kasa.App.Core;

public partial class AlislarViewModel : TemelViewModel
{
    private readonly IAlisApi _api;
    private readonly IKasaApi _finans;
    private readonly IAlisOdemeApi? _odemelerApi;
    private readonly IYonetimApi? _yonetim;
    private AlisDto? _secili;
    private IReadOnlyList<IslemDto> _giderler = Array.Empty<IslemDto>();
    private AlisOdemeYaz? _bekleyenOdeme;
    private int _bekleyenAlisId;
    private bool _yansitiliyor;
    private int _oturumSurumu = int.MinValue;
    private int _islemNesli;

    public AlislarViewModel(IAlisApi api, IKasaApi finans, IAlisOdemeApi? odemelerApi = null, IYonetimApi? yonetim = null, IBenzerKayitApi? benzerlikApi = null)
    {
        _api = api;
        _finans = finans;
        _odemelerApi = odemelerApi;
        _yonetim = yonetim;
        OdemeBenzerlik = new(benzerlikApi ?? finans as IBenzerKayitApi);
        Kalemler.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null) foreach (AlisKalemEditor k in e.OldItems) k.PropertyChanged -= KalemDegisti;
            if (e.NewItems is not null) foreach (AlisKalemEditor k in e.NewItems) k.PropertyChanged += KalemDegisti;
            ToplamlariYenile();
            KirliYap();
        };
    }

    public ObservableCollection<AlisSatiri> Alislar { get; } = new();
    public BenzerKayitKontrolu OdemeBenzerlik { get; }
    public ObservableCollection<AlisKanalDto> Kanallar { get; } = new();
    public ObservableCollection<AlisKalemEditor> Kalemler { get; } = new();
    public ObservableCollection<AlisOdemeSatiri> Odemeler { get; } = new();
    public ObservableCollection<AlisDagilimDto> OdemeOnizleme { get; } = new();
    public ObservableCollection<OdemeKartiSecenegi> OdemeKartlari { get; } = new();
    public ObservableCollection<GiderSecenegi> BaglanabilirGiderler { get; } = new();
    public ObservableCollection<AliciDto> Alicilar { get; } = new();

    [ObservableProperty] private bool _editorMu;
    [ObservableProperty] private bool _veriHazir;
    [ObservableProperty] private bool _kaydedilmemisDegisiklikVar;
    [ObservableProperty] private string? _mesaj;
    [ObservableProperty] private DateTime _tarih = DateTime.Today;
    [ObservableProperty] private string _tedarikci = "";
    [ObservableProperty] private string? _alisNotu;
    [ObservableProperty] private string _iadeNedeni = "";
    [ObservableProperty] private string? _onizlemeAciklamasi;
    [ObservableProperty] private bool _mevcutGiderKullan;
    [ObservableProperty] private GiderSecenegi? _seciliGider;
    [ObservableProperty] private DateTime _odemeTarihi = DateTime.Today;
    [ObservableProperty] private decimal _odemeTutari;
    [ObservableProperty] private OdemeKartiSecenegi? _odemeKarti;
    [ObservableProperty] private string? _odemeNotu;
    [ObservableProperty] private bool _hesaplarAcik;
    [ObservableProperty] private int _aliciId;
    [ObservableProperty] private string _aliciKullanici = "";
    [ObservableProperty] private string _aliciAd = "";
    [ObservableProperty] private string _aliciSifre = "";
    [ObservableProperty] private bool _aliciAktif = true;

    public AlisDto? Secili => _secili;
    public string Baslik => _secili is null ? "Yeni alış" : $"Alış #{_secili.Id}";
    public string Durum => AlisSatiri.DurumAdi(_secili?.Durum ?? "Taslak");
    public string KaydiAcan => _secili?.Alici ?? (EditorMu ? "Editör" : "Sizin alışınız");
    public string? EditorNotu => _secili?.EditorNotu;
    public bool KayitVar => _secili is not null;
    public bool Duzenlenebilir => _secili is null || _secili.Durum == "Taslak" || (EditorMu && _secili.Durum == "Incelemede");
    public bool Gonderilebilir => _secili is null || _secili.Durum == "Taslak";
    public bool Onaylanabilir => EditorMu && _secili?.Durum == "Incelemede";
    public bool IadeEdilebilir => EditorMu && _secili?.Durum is "Incelemede" or "Onaylandi";
    public bool OdemeAlaniGorunur => EditorMu && KayitVar;
    public bool YeniOdemeGirisi => !MevcutGiderKullan;
    public decimal Toplam => Kalemler.Sum(k => k.Tutar);
    public decimal Dagitilan => Kalemler.Sum(k => k.Dagilan);
    public decimal Odenen => _secili?.Odenen ?? 0m;
    public decimal Kalan => Toplam - Odenen;
    public decimal DagilimBekleyenTutar => Alislar.SelectMany(a => a.Veri.Odemeler).Where(o => o.DagilimBekliyor).Sum(o => o.Tutar);
    public bool DagilimBekliyor => DagilimBekleyenTutar > 0;
    public string DagilimOzeti => $"Kalem toplamı {Bicim.Tl(Toplam)} ₺ · kanallara ayrılan {Bicim.Tl(Dagitilan)} ₺";
    public string OdemeOzeti => $"Ödenen {Bicim.Tl(Odenen)} ₺ · kalan {Bicim.Tl(Kalan)} ₺";

    public void OturumuAyarla(int surum, bool editorMu)
    {
        if (_oturumSurumu == surum && EditorMu == editorMu) return;
        _oturumSurumu = surum;
        Interlocked.Increment(ref _islemNesli);
        Mesgul = false; VeriHazir = false; Hata = null; Mesaj = null;
        Alislar.Clear(); Alicilar.Clear(); Kanallar.Clear(); Odemeler.Clear(); BaglanabilirGiderler.Clear();
        _secili = null; _bekleyenOdeme = null; _giderler = Array.Empty<IslemDto>();
        OdemeBenzerlik.Temizle();
        HesaplarAcik = false; YeniAlici();
        Belgeler.Clear(); DuzeltmeHedefleri.Clear(); _duzeltmeAnahtari.Temizle(); _iptalAnahtari.Temizle();
        EditorMu = editorMu;
        KaydedilmemisDegisiklikVar = false;
        Yeni();
        OnPropertyChanged(nameof(DagilimBekleyenTutar)); OnPropertyChanged(nameof(DagilimBekliyor));
    }

    public Task YukleAsync() => YurutAsync(async nesil =>
    {
        if (KaydedilmemisDegisiklikVar && VeriHazir) { KaydetmeUyarisi(); return; }
        VeriHazir = false;
        var oncekiId = _secili?.Id;
        Alislar.Clear();
        OnPropertyChanged(nameof(DagilimBekleyenTutar));
        OnPropertyChanged(nameof(DagilimBekliyor));
        var kanalIsi = _api.AlisKanallariAsync();
        var alisIsi = _api.AlislarAsync();
        await Task.WhenAll(kanalIsi, alisIsi);
        if (!Gecerli(nesil)) return;
        Degistir(Kanallar, await kanalIsi);
        Degistir(Alislar, (await alisIsi).OrderByDescending(a => a.Tarih).ThenByDescending(a => a.Id).Select(a => new AlisSatiri(a)));
        OdemeKartlari.Clear();
        OdemeKartlari.Add(new(null, "Nakit / banka"));
        Alicilar.Clear();
        _giderler = Array.Empty<IslemDto>();
        if (EditorMu)
        {
            var kartIsi = _finans.KrediKartlariAsync();
            var giderIsi = _finans.IslemlerAsync();
            var hesapIsi = _api.AlicilarAsync();
            await Task.WhenAll(kartIsi, giderIsi, hesapIsi);
            if (!Gecerli(nesil)) return;
            foreach (var kart in await kartIsi) OdemeKartlari.Add(new(kart.Id, kart.Ad));
            _giderler = await giderIsi;
            Degistir(Alicilar, await hesapIsi);
        }
        var secili = Alislar.Select(a => a.Veri).FirstOrDefault(a => a.Id == oncekiId);
        if (secili is null) Yeni(); else SeciliyiGoster(secili);
        GiderSecenekleriniYenile();
        OnPropertyChanged(nameof(DagilimBekleyenTutar));
        OnPropertyChanged(nameof(DagilimBekliyor));
        VeriHazir = true;
    });

    [RelayCommand] private Task YenileAsync() => YukleAsync();
    [RelayCommand] private void Sec(AlisSatiri satir)
    {
        if (Mesgul) return;
        if (KaydedilmemisDegisiklikVar) { KaydetmeUyarisi(); return; }
        SeciliyiGoster(satir.Veri);
    }

    [RelayCommand]
    private void Yeni()
    {
        if (Mesgul && !_yansitiliyor && VeriHazir) return;
        if (KaydedilmemisDegisiklikVar && !_yansitiliyor) { KaydetmeUyarisi(); return; }
        _yansitiliyor = true;
        _secili = null;
        Tarih = DateTime.Today; Tedarikci = ""; AlisNotu = null; IadeNedeni = "";
        Belgeler.Clear(); DuzeltilecekOdeme = null;
        KalemleriTemizle();
        Kalemler.Add(new AlisKalemEditor());
        Odemeler.Clear();
        OdemeFormunuTemizle();
        _yansitiliyor = false;
        KaydedilmemisDegisiklikVar = false;
        DurumuYenile();
    }

    [RelayCommand]
    private void DegisiklikleriBirak()
    {
        if (Mesgul) return;
        KaydedilmemisDegisiklikVar = false;
        Hata = null;
        if (_secili is { } alis) SeciliyiGoster(alis); else Yeni();
    }

    private void KaydetmeUyarisi() => Hata = "Kaydedilmemiş değişiklikler var. Önce kaydedin veya ‘Değişiklikleri bırak’ seçeneğini kullanın.";

    private void SeciliyiGoster(AlisDto alis)
    {
        _yansitiliyor = true;
        _secili = alis;
        Tarih = alis.Tarih.ToDateTime(TimeOnly.MinValue); Tedarikci = alis.Tedarikci; AlisNotu = alis.Not;
        Belgeler.Clear(); DuzeltilecekOdeme = null;
        Degistir(DuzeltmeHedefleri, Alislar.Where(a => a.Veri.Id != alis.Id));
        IadeNedeni = "";
        KalemleriTemizle();
        foreach (var kalem in alis.Kalemler)
        {
            var editor = new AlisKalemEditor { Aciklama = kalem.Aciklama, Tutar = kalem.Tutar };
            foreach (var pay in kalem.Dagilimlar)
            {
                var kanal = Kanallar.FirstOrDefault(k => k.Id == pay.KanalId) ?? new AlisKanalDto(pay.KanalId, pay.Kanal, false);
                var secenekler = Kanallar.Any(k => k.Id == kanal.Id) ? Kanallar.ToList() : Kanallar.Append(kanal).ToList();
                editor.Dagilimlar.Add(new AlisDagilimEditor(secenekler) { Kanal = kanal, Tutar = pay.Tutar });
            }
            Kalemler.Add(editor);
        }
        Degistir(Odemeler, alis.Odemeler.OrderByDescending(o => o.Tarih).Select(o => new AlisOdemeSatiri(o)));
        OdemeFormunuTemizle();
        _yansitiliyor = false;
        KaydedilmemisDegisiklikVar = false;
        DurumuYenile();
    }

    private void KalemleriTemizle()
    {
        foreach (var k in Kalemler) k.PropertyChanged -= KalemDegisti;
        Kalemler.Clear();
    }

    [RelayCommand] private void KalemEkle() { if (Duzenlenebilir && !Mesgul) Kalemler.Add(new AlisKalemEditor()); }
    [RelayCommand] private void KalemSil(AlisKalemEditor kalem) { if (Duzenlenebilir && !Mesgul) Kalemler.Remove(kalem); }
    [RelayCommand]
    private void DagilimEkle(AlisKalemEditor kalem)
    {
        if (!Duzenlenebilir || Mesgul) return;
        var kanal = Kanallar.FirstOrDefault(k => k.Aktif && kalem.Dagilimlar.All(d => d.Kanal?.Id != k.Id));
        kalem.Dagilimlar.Add(new AlisDagilimEditor(Kanallar.ToList()) { Kanal = kanal, Tutar = Math.Max(0, kalem.DagilimFarki) });
    }
    [RelayCommand]
    private void DagilimSil(AlisDagilimEditor pay)
    {
        if (!Duzenlenebilir || Mesgul) return;
        Kalemler.FirstOrDefault(k => k.Dagilimlar.Contains(pay))?.Dagilimlar.Remove(pay);
    }

    [RelayCommand]
    private Task KaydetAsync() => YurutAsync(async nesil =>
    {
        if (!Duzenlenebilir || !KaydiDogrula()) return;
        if (!await KaydetCoreAsync(nesil)) return;
        Mesaj = "Alış kaydedildi. Dağılım tamamlanınca incelemeye gönderebilirsiniz.";
    });

    private async Task<bool> KaydetCoreAsync(int nesil)
    {
        var g = new AlisYaz(_secili?.Surum ?? 0, DateOnly.FromDateTime(Tarih), Tedarikci.Trim(), AlisNotu,
            Kalemler.Select(k => new AlisKalemYaz(k.Aciklama.Trim(), k.Tutar,
                k.Dagilimlar.Select(d => new AlisDagilimYaz(d.Kanal!.Id, d.Tutar)).ToList())).ToList());
        var yanit = _secili is null ? await _api.AlisOlusturAsync(g) : await _api.AlisGuncelleAsync(_secili.Id, g);
        return SonucuUygula(yanit, nesil);
    }

    [RelayCommand]
    private Task GonderAsync() => YurutAsync(async nesil =>
    {
        if (!Gonderilebilir || !KaydiDogrula()) return;
        if (!await KaydetCoreAsync(nesil)) return;
        if (!SonucuUygula(await _api.AlisGonderAsync(_secili!.Id, new(_secili.Surum)), nesil)) return;
        Mesaj = "Alış incelemeye gönderildi. Editörün kararını buradan takip edebilirsiniz.";
    });

    [RelayCommand]
    private Task OnaylaAsync() => YurutAsync(async nesil =>
    {
        if (!Onaylanabilir || !KaydiDogrula(tamDagilim: true)) return;
        if (!await KaydetCoreAsync(nesil)) return;
        if (!SonucuUygula(await _api.AlisOnaylaAsync(_secili!.Id, new(_secili.Surum)), nesil)) return;
        Mesaj = "Alış onaylandı. Ödemelerin kanal dağılımı artık raporlara yansır.";
    });

    [RelayCommand]
    private Task IadeAsync() => YurutAsync(async nesil =>
    {
        if (!IadeEdilebilir || _secili is null) return;
        if (KaydedilmemisDegisiklikVar) { KaydetmeUyarisi(); return; }
        if (string.IsNullOrWhiteSpace(IadeNedeni)) { Hata = "İade nedenini yazın."; return; }
        if (!SonucuUygula(await _api.AlisIadeAsync(_secili.Id, new(_secili.Surum, IadeNedeni.Trim())), nesil)) return;
        Mesaj = "Alış taslağa iade edildi. Ödemeler korundu; yeniden onaya kadar dağılım bekliyor.";
    });

    private bool KaydiDogrula(bool tamDagilim = false)
    {
        if (string.IsNullOrWhiteSpace(Tedarikci)) return HataYaz("Tedarikçi adını yazın.");
        if (Kalemler.Count == 0) return HataYaz("En az bir alış kalemi ekleyin.");
        foreach (var k in Kalemler)
        {
            if (string.IsNullOrWhiteSpace(k.Aciklama) || k.Tutar <= 0 || decimal.Round(k.Tutar, 2) != k.Tutar)
                return HataYaz("Her kaleme açıklama ve sıfırdan büyük, kuruş hassasiyetinde tutar girin.");
            if (k.Dagilimlar.Any(d => d.Kanal is null || d.Tutar <= 0 || decimal.Round(d.Tutar, 2) != d.Tutar))
                return HataYaz("Her dağılım satırında kanal seçin ve sıfırdan büyük tutar girin; kullanılmayan satırı kaldırın.");
            if (k.Dagilimlar.Select(d => d.Kanal!.Id).Distinct().Count() != k.Dagilimlar.Count)
                return HataYaz("Bir kalemde aynı kanalı iki kez seçmeyin.");
            if (k.Dagilan > k.Tutar || (tamDagilim && k.Dagilan != k.Tutar))
                return HataYaz(tamDagilim ? "Onay için her kalemin tamamını kanallara dağıtın." : "Kanal payları kalem tutarını aşamaz.");
        }
        if (Toplam < Odenen) return HataYaz("Alış toplamı mevcut ödemelerden küçük olamaz.");
        return true;
    }

    [RelayCommand]
    private Task OdemeKaydetAsync() => YurutAsync(async nesil =>
    {
        if (!EditorMu || _secili is null) return;
        if (KaydedilmemisDegisiklikVar) { Hata = "Ödeme eklemeden önce alıştaki değişiklikleri kaydedin."; return; }
        if (MevcutGiderKullan && SeciliGider is null) { Hata = "Bağlanacak mevcut gideri seçin."; return; }
        if (OdemeTutari <= 0 || decimal.Round(OdemeTutari, 2) != OdemeTutari || OdemeTutari > _secili.Kalan)
        { Hata = "Ödeme sıfırdan büyük, kuruş hassasiyetinde ve kalan tutarı aşmayacak şekilde olmalıdır."; return; }
        var g = new AlisOdemeYaz(_secili.Surum, Guid.NewGuid(), DateOnly.FromDateTime(OdemeTarihi), OdemeTutari,
            OdemeKarti?.Id, MevcutGiderKullan ? SeciliGider!.Veri.Id : null, OdemeNotu);
        // Ağ hatasında aynı ödeme tekrar gönderilirse aynı anahtar ve gövde kullanılır.
        if (_bekleyenAlisId == _secili.Id && _bekleyenOdeme is { } eski
            && eski.Tarih == g.Tarih && eski.Tutar == g.Tutar && eski.KrediKartiId == g.KrediKartiId
            && eski.MevcutIslemId == g.MevcutIslemId && eski.Not == g.Not && eski.HesapId == g.HesapId) g = eski;
        _bekleyenOdeme = g; _bekleyenAlisId = _secili.Id;
        var alisId = _secili.Id;
        if (g.MevcutIslemId is null && !await OdemeBenzerlik.DevamEdilebilirAsync(new("AlisOdeme", g.Tarih, g.Tutar, g.KrediKartiId, AlisId: alisId), new { alisId, g }, () => Gecerli(nesil) && _secili?.Id == alisId)) return;
        try
        {
            if (!SonucuUygula(await _api.AlisOdemeKaydetAsync(_secili.Id, g), nesil)) return;
            OdemeBenzerlik.Temizle();
            Mesaj = g.MevcutIslemId is null ? "Ödeme kaydedildi; tek bir gider oluşturuldu." : "Mevcut gider bağlandı; ikinci bir gider oluşturulmadı.";
        }
        catch (KasaApiException e) when (e.DurumKodu is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        { if (Gecerli(nesil)) _bekleyenOdeme = null; throw; }
    });

    [RelayCommand] private async Task OdemeyiAyriKaydetAsync() { if (OdemeBenzerlik.Onayla()) await OdemeKaydetAsync(); }

    partial void OnMevcutGiderKullanChanged(bool value)
    {
        OnPropertyChanged(nameof(YeniOdemeGirisi));
        if (value) GideriOdemeFormunaYansit();
        else { SeciliGider = null; OdemeTutari = 0; OdemeKarti = OdemeKartlari.FirstOrDefault(); }
    }
    partial void OnSeciliGiderChanged(GiderSecenegi? value) => GideriOdemeFormunaYansit();
    private void GideriOdemeFormunaYansit()
    {
        if (!MevcutGiderKullan || SeciliGider is null) return;
        OdemeTarihi = SeciliGider.Veri.Tarih.ToDateTime(TimeOnly.MinValue);
        OdemeTutari = SeciliGider.Veri.TutarTl;
        OdemeKarti = OdemeKartlari.FirstOrDefault(k => k.Id == SeciliGider.Veri.KrediKartiId);
    }
    partial void OnOdemeTutariChanged(decimal value) => OnizlemeyiYenile();
    partial void OnEditorMuChanged(bool value) => DurumuYenile();
    partial void OnTarihChanged(DateTime value) => KirliYap();
    partial void OnTedarikciChanged(string value) => KirliYap();
    partial void OnAlisNotuChanged(string? value) => KirliYap();
    partial void OnKaydedilmemisDegisiklikVarChanged(bool value) => OnizlemeyiYenile();
    private void KirliYap() { if (!_yansitiliyor) KaydedilmemisDegisiklikVar = true; }

    private void OnizlemeyiYenile()
    {
        if (_yansitiliyor) return;
        OdemeOnizleme.Clear();
        OnizlemeAciklamasi = "Ödeme payları alışın kanal toplamlarına orantılı hesaplanır; kuruş farkları ve önceki ödemeler korunur.";
        if (_secili is null || OdemeTutari <= 0) return;
        if (KaydedilmemisDegisiklikVar) { OnizlemeAciklamasi = "Ödeme için önce alıştaki değişiklikleri kaydedin."; return; }
        // Kaydedilmiş dağılım esastır; henüz kaydedilmemiş form değişiklikleri ödemeyi etkilemez.
        if (_secili.Kalemler.Any(k => k.Dagilimlar.Sum(d => d.Tutar) != k.Tutar))
        { OnizlemeAciklamasi = "Dağılım eksik. Ödeme kaydedilebilir; kanal dağılımı alış onaylanana kadar bekler."; return; }
        try
        {
            var paylar = _secili.Kalemler.SelectMany(k => k.Dagilimlar).GroupBy(d => d.KanalId)
                .Select(g => new AlisKanalPayi(g.Key, g.Sum(d => d.Tutar))).Where(d => d.Tutar > 0).OrderBy(d => d.KanalId).ToList();
            foreach (var pay in AlisDagitici.Dagit(paylar, _secili.Odenen, OdemeTutari))
                OdemeOnizleme.Add(new(pay.KanalId, Kanallar.FirstOrDefault(k => k.Id == pay.KanalId)?.Ad ?? $"Kanal #{pay.KanalId}", pay.Tutar));
            if (_secili.Durum != "Onaylandi") OnizlemeAciklamasi = "Kaydedilmiş dağılıma göre tahmin. Alış onaylanana kadar bu ödeme dağılım bekler.";
        }
        catch (ArgumentException) { OnizlemeAciklamasi = "Önizleme için kalan tutarı aşmayan geçerli bir ödeme girin."; }
    }

    private void OdemeFormunuTemizle()
    {
        OdemeBenzerlik.Temizle();
        _bekleyenOdeme = null;
        MevcutGiderKullan = false; SeciliGider = null; OdemeTarihi = DateTime.Today;
        OdemeTutari = 0; OdemeNotu = null; OdemeKarti = OdemeKartlari.FirstOrDefault();
       
    }

    private bool SonucuUygula(AlisDto alis, int nesil)
    {
        if (!Gecerli(nesil)) return false;
        var eski = Alislar.FirstOrDefault(a => a.Veri.Id == alis.Id);
        if (eski is not null) Alislar.Remove(eski);
        Alislar.Insert(0, new(alis));
        SeciliyiGoster(alis);
        GiderSecenekleriniYenile();
        OnPropertyChanged(nameof(DagilimBekleyenTutar)); OnPropertyChanged(nameof(DagilimBekliyor));
        return true;
    }

    private void GiderSecenekleriniYenile()
    {
        var bagli = Alislar.SelectMany(a => a.Veri.Odemeler).Select(o => o.IslemId).ToHashSet();
        Degistir(BaglanabilirGiderler, _giderler.Where(i => i.TutarTl > 0 && i.AlisId is null && i.AylikGiderOdemeId is null && i.EkstreKayitId is null && i.Tip is GiderTipi.Cari or GiderTipi.KrediKarti && !bagli.Contains(i.Id))
            .OrderByDescending(i => i.Tarih).Select(i => new GiderSecenegi(i)));
    }

    [RelayCommand] private void HesaplariAcKapat() { if (EditorMu) HesaplarAcik = !HesaplarAcik; }
    [RelayCommand] private void YeniAlici() { AliciId = 0; AliciKullanici = ""; AliciAd = ""; AliciSifre = ""; AliciAktif = true; }
    [RelayCommand] private void AliciDuzenle(AliciDto alici)
    { if (EditorMu) { AliciId = alici.Id; AliciKullanici = alici.Kullanici; AliciAd = alici.Ad; AliciAktif = alici.Aktif; AliciSifre = ""; } }
    [RelayCommand]
    private Task AliciKaydetAsync() => YurutAsync(async nesil =>
    {
        if (!EditorMu) return;
        if (string.IsNullOrWhiteSpace(AliciKullanici) || string.IsNullOrWhiteSpace(AliciAd)) { Hata = "Alıcının adını ve kullanıcı adını yazın."; return; }
        if ((AliciId == 0 || AliciSifre.Length > 0) && AliciSifre.Length < 8) { Hata = "Yeni şifre en az 8 karakter olmalıdır."; return; }
        var g = new AliciYaz(AliciKullanici.Trim(), AliciAd.Trim(), AliciSifre.Length == 0 ? null : AliciSifre, AliciAktif);
        var alici = AliciId == 0 ? await _api.AliciOlusturAsync(g) : await _api.AliciGuncelleAsync(AliciId, g);
        if (!Gecerli(nesil)) return;
        var eski = Alicilar.FirstOrDefault(a => a.Id == alici.Id);
        if (eski is not null) Alicilar.Remove(eski);
        Alicilar.Add(alici);
        YeniAlici();
        Mesaj = "Alıcı hesabı kaydedildi. Pasifleştirme veya şifre değişimi eski oturumu kapatır.";
    });

    private async Task YurutAsync(Func<int, Task> islem)
    {
        if (Mesgul) return;
        var nesil = Volatile.Read(ref _islemNesli);
        Mesaj = null; Hata = null; Mesgul = true;
        try { await islem(nesil); }
        catch (Exception hata) { if (Gecerli(nesil)) Hata = HataMesaji(hata); }
        finally { if (Gecerli(nesil)) Mesgul = false; }
    }
    public void BekleyenIslemleriGecersizKil() => Interlocked.Increment(ref _islemNesli);
    private bool Gecerli(int nesil) => nesil == Volatile.Read(ref _islemNesli);
    private bool HataYaz(string mesaj) { Hata = mesaj; return false; }
    private void KalemDegisti(object? sender, PropertyChangedEventArgs e) { ToplamlariYenile(); KirliYap(); }
    private void ToplamlariYenile()
    {
        foreach (var ad in new[] { nameof(Toplam), nameof(Dagitilan), nameof(Kalan), nameof(DagilimOzeti), nameof(OdemeOzeti) }) OnPropertyChanged(ad);
    }
    private void DurumuYenile()
    {
        foreach (var ad in new[] { nameof(Secili), nameof(Baslik), nameof(Durum), nameof(KaydiAcan), nameof(EditorNotu), nameof(KayitVar), nameof(Duzenlenebilir), nameof(Gonderilebilir), nameof(Onaylanabilir), nameof(IadeEdilebilir), nameof(OdemeAlaniGorunur), nameof(Odenen) }) OnPropertyChanged(ad);
        ToplamlariYenile();
        OnizlemeyiYenile();
    }
    private static void Degistir<T>(ObservableCollection<T> hedef, IEnumerable<T> veri)
    { hedef.Clear(); foreach (var satir in veri) hedef.Add(satir); }
}
