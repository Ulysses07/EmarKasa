using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// İşlem formunun "Belge" bölümü (Paket F): belge türü, belge no, fatura bekleniyor ve
/// fiş/fatura ekleri. Hepsi isteğe bağlıdır; boş bırakılan işlem bugünkü gibi kaydedilir.
/// Para hesabına etkisi yoktur.
/// <list type="bullet">
/// <item>Seçilen dosyalar "bekleyen ek" olarak tutulur ve işlem kaydedildikten SONRA yüklenir
///       (yeni işlemin Id'si ancak o zaman bellidir).</item>
/// <item>Bir ek yüklenemezse işlem yine kayıtlıdır: form o işlemin düzenlemesine geçer, yüklenemeyen
///       ekler bekler ve Kaydet'e yeniden basınca tekrar denenir.</item>
/// <item>Var olan ekin silinmesi iki adımlıdır (Sil → Evet, sil) ve onaylanınca hemen uygulanır.</item>
/// </list>
/// </summary>
public partial class IslemlerViewModel
{
    /// <summary>Belge türü çipleri: Belirtilmedi · e-Fatura · e-Arşiv · Fiş · Makbuz · Belgesiz.</summary>
    public ObservableCollection<SecimCipi> BelgeTuruCipleri { get; } = BelgeCipleriKur();

    [ObservableProperty] private BelgeTuru? _duzenBelgeTuru;
    [ObservableProperty] private string? _duzenBelgeNo;
    [ObservableProperty] private bool _duzenFaturaBekleniyor;

    /// <summary>Düzenlenen işlemin sunucudaki ekleri.</summary>
    public ObservableCollection<EkDto> MevcutEkler { get; } = new();

    /// <summary>Seçilmiş, kaydetmede yüklenecek dosyalar.</summary>
    public ObservableCollection<BekleyenEk> BekleyenEkler { get; } = new();

    /// <summary>Silme onayı bekleyen ek (dolu = "Evet, sil / Vazgeç" görünür).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EkSilmeOnayiBekliyor), nameof(SilinecekEkMetni))]
    private EkDto? _silinecekEk;

    public bool EkSilmeOnayiBekliyor => SilinecekEk is not null;
    public string SilinecekEkMetni => SilinecekEk is { } e ? $"'{e.Ad}' silinsin mi? Bu hemen uygulanır." : "";

    private IDosyaSecici? _dosyaSecici;

    /// <summary>Platform dosya/fotoğraf seçicisi (MAUI DI verir; yoksa "Fotoğraf/PDF ekle" hata gösterir).</summary>
    public IDosyaSecici? DosyaSecici
    {
        get => _dosyaSecici;
        set { _dosyaSecici = value; OnPropertyChanged(nameof(KameraVar)); }
    }

    /// <summary>Eki cihazda açan servis (MAUI DI verir).</summary>
    public IEkAcici? EkAcici { get; set; }

    public bool KameraVar => DosyaSecici?.KameraVar == true;
    public bool MevcutEkVar => MevcutEkler.Count > 0;
    public bool BekleyenEkVar => BekleyenEkler.Count > 0;

    /// <summary>"2 ek · 1 kaydetmede yüklenecek" (hiç yoksa "Ek yok").</summary>
    public string EkOzeti => (MevcutEkler.Count, BekleyenEkler.Count) switch
    {
        (0, 0) => "Ek yok",
        (var m, 0) => $"{m} ek",
        (0, var b) => $"{b} dosya kaydetmede yüklenecek",
        var (m, b) => $"{m} ek · {b} dosya kaydetmede yüklenecek",
    };

    public const string SeciciYokMesaji = "Bu cihazda dosya seçilemiyor.";
    public const string KameraYokMesaji = "Bu cihazda kamera yok.";
    public const string AciciYokMesaji = "Bu cihazda dosya açılamıyor.";

    /// <summary>Ek listesi istek sürümü: başka işleme geçilince eski yanıt listeyi ezmesin.</summary>
    private int _ekSurumu;

    private static ObservableCollection<SecimCipi> BelgeCipleriKur()
    {
        var l = new ObservableCollection<SecimCipi>(BelgeMetin.Turler.Select(t => new SecimCipi(BelgeMetin.TurAdi(t))));
        l[0].Secili = true;
        return l;
    }

    /// <summary>Formdaki belge bilgisi (kaydetmede <see cref="IslemYaz.Belge"/> olarak gider).</summary>
    public BelgeBilgisi FormBelgesi => new(DuzenBelgeTuru,
        string.IsNullOrWhiteSpace(DuzenBelgeNo) ? null : DuzenBelgeNo.Trim(), DuzenFaturaBekleniyor);

    partial void OnDuzenBelgeTuruChanged(BelgeTuru? value)
    {
        var ad = BelgeMetin.TurAdi(value);
        foreach (var c in BelgeTuruCipleri) c.Secili = c.Ad == ad;
        // "Belgesiz" ile "fatura bekleniyor" birlikte olamaz (sunucu da reddeder): biri seçilince öteki kalkar.
        if (value == BelgeTuru.Belgesiz) DuzenFaturaBekleniyor = false;
    }

    partial void OnDuzenFaturaBekleniyorChanged(bool value)
    {
        // Faturası beklenen ödemenin türü fatura gelince seçilir (Fatura Takibi → "Fatura geldi").
        if (value && DuzenBelgeTuru == BelgeTuru.Belgesiz) DuzenBelgeTuru = null;
    }

    [RelayCommand] private void SecBelgeTuru(SecimCipi s) => DuzenBelgeTuru = BelgeMetin.TurDegeri(s.Ad);

    /// <summary>Yeni(): belge bölümünü boşaltır (bekleyen dosyalar atılır).</summary>
    private void BelgeFormunuSifirla()
    {
        DuzenBelgeTuru = null;
        DuzenBelgeNo = null;
        DuzenFaturaBekleniyor = false;
        _ekSurumu++;
        MevcutEkler.Clear();
        BekleyenEkler.Clear();
        SilinecekEk = null;
        EkDurumunuBildir();
    }

    /// <summary>Duzenle(): işlemin belge bilgisini forma alır ve eklerini yükler.</summary>
    private void BelgeFormunuDoldur(IslemDto i)
    {
        BelgeFormunuSifirla();
        DuzenBelgeTuru = i.BelgeTuru;
        DuzenBelgeNo = i.BelgeNo;
        DuzenFaturaBekleniyor = i.FaturaBekleniyor;
        _ = MevcutEkleriYukleAsync(i.Id);
    }

    private async Task MevcutEkleriYukleAsync(int islemId)
    {
        var surum = ++_ekSurumu;
        try
        {
            var l = await _api.EklerAsync(islemId);
            if (surum != _ekSurumu) return;
            MevcutEkler.Clear();
            foreach (var e in l) MevcutEkler.Add(e);
            EkDurumunuBildir();
        }
        catch (Exception ex)
        {
            if (surum == _ekSurumu) Hata = HataMesaji.Coz(ex);
        }
    }

    private void EkDurumunuBildir()
    {
        OnPropertyChanged(nameof(MevcutEkVar));
        OnPropertyChanged(nameof(BekleyenEkVar));
        OnPropertyChanged(nameof(EkOzeti));
    }

    [RelayCommand]
    private Task EkEkleAsync() => CalistirAsync(async () =>
    {
        Dogrula(DosyaSecici is not null, SeciciYokMesaji);
        BekleyeneEkle(await DosyaSecici!.BelgeSecAsync());
    });

    [RelayCommand]
    private Task FotografCekAsync() => CalistirAsync(async () =>
    {
        Dogrula(DosyaSecici is { KameraVar: true }, KameraYokMesaji);
        if (await DosyaSecici!.FotografCekAsync() is { } f) BekleyeneEkle([f]);
    });

    /// <summary>Uygun dosyaları bekleyenlere ekler; uygun olmayanlar tek bir hata mesajında toplanır.</summary>
    private void BekleyeneEkle(IReadOnlyList<SecilenDosya> dosyalar)
    {
        var hatalar = new List<string>();
        foreach (var d in dosyalar)
        {
            if (EkKurallari.Hata(d) is string h) { hatalar.Add(h); continue; }
            if (MevcutEkler.Count + BekleyenEkler.Count >= EkKurallari.IslemBasinaEnFazla) { hatalar.Add(EkKurallari.SayiMesaji); break; }
            BekleyenEkler.Add(new BekleyenEk(d.Ad, d.Icerik));
        }
        EkDurumunuBildir();
        if (hatalar.Count > 0) throw new DogrulamaHatasi(string.Join(" ", hatalar.Distinct()));
    }

    [RelayCommand]
    private void BekleyenEkiKaldir(BekleyenEk e)
    {
        BekleyenEkler.Remove(e);
        EkDurumunuBildir();
    }

    [RelayCommand]
    private Task EkAcAsync(EkDto e) => CalistirAsync(async () =>
    {
        Dogrula(EkAcici is not null, AciciYokMesaji);
        var d = await _api.EkIndirAsync(e.Id);
        await EkAcici!.AcAsync(d.DosyaAdi, d.Icerik);
    });

    [RelayCommand] private void EkSilIste(EkDto e) => SilinecekEk = e;
    [RelayCommand] private void EkSilVazgec() => SilinecekEk = null;

    [RelayCommand]
    private Task EkSilOnaylaAsync() => CalistirAsync(async () =>
    {
        if (SilinecekEk is not { } e) return;
        await _api.EkSilAsync(e.Id);
        SilinecekEk = null;
        MevcutEkler.Remove(e);
        EkDurumunuBildir();
        // Listedeki "Ekler (n)" ve açıksa salt okunur panel de silineni göstermesin.
        ListedeEkSayisiniYaz(e.IslemId, MevcutEkler.Count);
        if (ListeEkleri.FirstOrDefault(x => x.Id == e.Id) is { } l) ListeEkleri.Remove(l);
    });

    // ---- Listede belge/ek görüntüleme (her iki rol; ortaklar da fişi/faturayı açabilir) ----

    /// <summary>Listede "Ekler (n)" ile ekleri açılan işlem (dolu = salt okunur ek paneli görünür).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListeEkPaneliGorunur), nameof(ListeEkBasligi))]
    private IslemDto? _listeEkIslemi;

    /// <summary>Salt okunur ek panelindeki dosyalar (yalnız "Aç").</summary>
    public ObservableCollection<EkDto> ListeEkleri { get; } = new();

    public bool ListeEkPaneliGorunur => ListeEkIslemi is not null;

    public string ListeEkBasligi => ListeEkIslemi is { } i
        ? $"Ekler · {i.Cari} · {Bicim.Tl(i.TutarTl)} ₺ · {i.Tarih.ToString("dd.MM.yyyy", Kultur.Turkce)}"
        : "";

    /// <summary>Liste ek paneli istek sürümü: başka satıra geçilince eski yanıt paneli ezmesin.</summary>
    private int _listeEkSurumu;

    [RelayCommand]
    private Task ListeEkleriGosterAsync(IslemDto i) => CalistirAsync(async () =>
    {
        var surum = ++_listeEkSurumu;
        ListeEkIslemi = i;
        ListeEkleri.Clear();
        var l = await _api.EklerAsync(i.Id);
        if (surum != _listeEkSurumu) return;
        foreach (var e in l) ListeEkleri.Add(e);
    });

    [RelayCommand]
    private void ListeEkleriKapat()
    {
        _listeEkSurumu++;
        ListeEkIslemi = null;
        ListeEkleri.Clear();
    }

    /// <summary>Listede o işlemin satırını yeni ek sayısıyla değiştirir (liste yeniden yüklenmeden).</summary>
    private void ListedeEkSayisiniYaz(int islemId, int adet)
    {
        for (var k = 0; k < Islemler.Count; k++)
            if (Islemler[k].Id == islemId && Islemler[k].EkSayisi != adet)
                Islemler[k] = Islemler[k] with { EkSayisi = adet };
    }

    /// <summary>
    /// İşlem kaydedildikten sonra bekleyen dosyaları yükler. Hepsi yüklenirse hiçbir şey olmaz; biri bile
    /// yüklenemezse form kaydedilen işlemin düzenlemesine geçer, yüklenemeyenler bekler ve hata gösterilir.
    /// </summary>
    private async Task BekleyenEkleriYukleAsync(IslemDto kaydedilen, DateOnly tarih)
    {
        if (BekleyenEkler.Count == 0) return;
        var hatalar = new List<string>();
        foreach (var e in BekleyenEkler.ToList())
        {
            try
            {
                await _api.EkYukleAsync(kaydedilen.Id, e.Ad, e.Icerik);
                BekleyenEkler.Remove(e);
            }
            catch (Exception ex) { hatalar.Add($"{e.Ad}: {HataMesaji.Coz(ex)}"); }
        }
        EkDurumunuBildir();
        if (hatalar.Count == 0) return;

        var kalan = BekleyenEkler.ToList();
        Duzenle(kaydedilen);
        foreach (var e in kalan) BekleyenEkler.Add(e);
        EkDurumunuBildir();
        await DonemleriGerekirseYenileAsync(tarih);
        await IslemleriYukleAsync();
        throw new DogrulamaHatasi(
            $"İşlem kaydedildi ama {hatalar.Count} dosya yüklenemedi ({string.Join("; ", hatalar)}). Tekrar denemek için Kaydet'e basın.");
    }
}

/// <summary>Seçilmiş, henüz yüklenmemiş ek.</summary>
public sealed class BekleyenEk(string ad, byte[] icerik)
{
    public string Ad { get; } = ad;
    public byte[] Icerik { get; } = icerik;
    public string BoyutMetni => EkKurallari.BoyutMetni(Icerik.LongLength);
}
