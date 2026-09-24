using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>
/// Paket D (özellik 33) — Panel'deki bekleyen gider kartının ikinci adımı: onaylarken tarih, kanal
/// ve not da değiştirilebilir; yanlışlıkla "Bu ay atla" denen ay "Geri al" ile yeniden bekleyene döner.
/// </summary>
public partial class PanelViewModel
{
    /// <summary>Onayda seçilebilecek kanallar: Panel'deki kanallar + "Ortak".</summary>
    public ObservableCollection<string> BekleyenKanallar { get; } = new();

    /// <summary>Bu ay ve önceki 2 ayın atlanan giderleri (yalnız editör).</summary>
    public ObservableCollection<AtlananGiderGorunum> AtlananGiderler { get; } = new();
    public bool AtlananVar => EditorMu && AtlananGiderler.Count > 0;

    /// <summary>DatePicker üst sınırı: ileri tarihli gider girilmez.</summary>
    public DateTime BugunTarihi => Bugun;

    /// <summary>Onay tarihi: satırda seçilen gün; bugünden sonrası bugüne çekilir (ileri tarih asla).</summary>
    private DateOnly OnayTarihi(BekleyenGiderGorunum b)
    {
        var t = DateOnly.FromDateTime(b.TarihGiris);
        return t > BugunTarih ? BugunTarih : t;
    }

    /// <summary>
    /// Kanal seçeneklerini kurar ve atlananları yükler (<see cref="DoldurAsync"/> içinde, bekleyen satırlar
    /// kurulmadan önce: satırdaki kanal seçicisi açıldığında seçenekler hazır olsun).
    /// </summary>
    private async Task AtlananlariYukleAsync(IReadOnlyList<BekleyenGiderDto> bekleyenler)
    {
        BekleyenKanallariKur(bekleyenler);
        var liste = EditorMu
            ? await TekrarlayanYukleme.Oku(_api.AtlananGiderlerAsync)
            : Array.Empty<TekrarlayanAtlananDto>();
        AtlananGiderler.Clear();
        foreach (var a in liste) AtlananGiderler.Add(new AtlananGiderGorunum(a));
        OnPropertyChanged(nameof(AtlananVar));
    }

    private void BekleyenKanallariKur(IReadOnlyList<BekleyenGiderDto> bekleyenler)
    {
        var adlar = Kanallar.Select(k => k.Kanal).ToList();
        foreach (var b in bekleyenler)
            if (!adlar.Contains(b.Kanal) && b.Kanal != AyarlarViewModel.OrtakKanal) adlar.Add(b.Kanal);
        adlar.Add(AyarlarViewModel.OrtakKanal);
        if (adlar.SequenceEqual(BekleyenKanallar)) return;
        BekleyenKanallar.Clear();
        foreach (var a in adlar) BekleyenKanallar.Add(a);
    }

    /// <summary>"Bu ay atla" kararını geri alır; ay yeniden bekleyenler listesine düşer.</summary>
    [RelayCommand]
    private Task AtlamayiGeriAlAsync(AtlananGiderGorunum a) => CalistirAsync(async () =>
    {
        Dogrula(EditorMu, HataMesaji.Yetkisiz);
        await KararVerAsync(() => _api.TekrarlayanAtlamayiGeriAlAsync(a.TekrarlayanGiderId, a.Ay));
        AtlananGiderler.Remove(a);
        await DoldurAsync();
    });
}

/// <summary>Atlanan bir tekrarlayan gider ayı ("Geri al" ile yeniden bekleyene döner).</summary>
public sealed class AtlananGiderGorunum
{
    public AtlananGiderGorunum(TekrarlayanAtlananDto d)
    {
        TekrarlayanGiderId = d.TekrarlayanGiderId; Kalem = d.Kalem; Kanal = d.Kanal; Ay = d.Ay; Vade = d.Vade;
    }

    public int TekrarlayanGiderId { get; }
    public string Kalem { get; }
    public string Kanal { get; }
    public DateOnly Ay { get; }
    public DateOnly Vade { get; }
    /// <summary>"Eylül 2026 atlandı · MEZAT · Vade 28 Eylül".</summary>
    public string Aciklama => $"{Ay.ToString("MMMM yyyy", Kultur.Turkce)} atlandı · {Kanal} · Vade {Vade.ToString("d MMMM", Kultur.Turkce)}";
}

/// <summary>Bekleyen gider satırının Paket D eki: onay tarihi, kanal ve not; sıklık/kart/değişken tutar bilgisi.</summary>
public sealed partial class BekleyenGiderGorunum
{
    private DateTime? _tarihGiris;
    private string? _kanalGiris;
    private string? _notGiris;

    /// <summary>Onay tarihi (varsayılan vade; ileri tarih gönderilirken bugüne çekilir).</summary>
    public DateTime TarihGiris
    {
        get => _tarihGiris ?? Vade.ToDateTime(TimeOnly.MinValue);
        set => SetProperty(ref _tarihGiris, value);
    }

    /// <summary>Onay kanalı (varsayılan şablonun kanalı).</summary>
    public string KanalGiris
    {
        get => _kanalGiris ?? Kanal;
        set => SetProperty(ref _kanalGiris, value);
    }

    /// <summary>İşlem notu (boşsa sunucu "Tekrarlayan gider" yazar).</summary>
    public string? NotGiris
    {
        get => _notGiris;
        set => SetProperty(ref _notGiris, value);
    }

    /// <summary>Kanal değişmediyse null gönderilir (sunucu şablonun kanalını kullanır).</summary>
    public string? GonderilecekKanal => string.IsNullOrWhiteSpace(KanalGiris) || KanalGiris == Kanal ? null : KanalGiris;
    public string? GonderilecekNot => string.IsNullOrWhiteSpace(NotGiris) ? null : NotGiris.Trim();

    public bool TutarDegisken { get; private set; }
    public int? KrediKartiId { get; private set; }
    public TekrarSikligi Siklik { get; private set; }

    /// <summary>"3 ayda bir · karta bağlı · tutar her seferinde girilir" (aylık, kartsız, sabit tutarda boş).</summary>
    public string EkBilgi
    {
        get
        {
            var p = new List<string>();
            if (Siklik != TekrarSikligi.Aylik) p.Add(TekrarlayanMetin.SiklikAdi(Siklik));
            if (KrediKartiId is not null) p.Add("karta bağlı: kart harcaması olarak girilir");
            if (TutarDegisken) p.Add("tutar her seferinde girilir");
            return string.Join(" · ", p);
        }
    }
    public bool EkBilgiVar => EkBilgi.Length > 0;

    /// <summary>DTO'daki Paket D alanlarını alır (yapıcı çağırır).</summary>
    private void EkAlanlariKur(BekleyenGiderDto d)
    {
        TutarDegisken = d.TutarDegisken;
        KrediKartiId = d.KrediKartiId;
        Siklik = d.Siklik;
    }

    /// <summary>Yeniden yüklemede değiştirilmiş tarih/kanal/notu eski satırdan taşır.</summary>
    private void EkGirisiDevral(BekleyenGiderGorunum eski)
    {
        if (eski._tarihGiris is { } t) TarihGiris = t;
        if (eski._kanalGiris is { } k) KanalGiris = k;
        if (eski._notGiris is { } n) NotGiris = n;
    }
}
