using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

// Yapay zekâ önerileri (docs/specs/2026-11-25-yapay-zeka-onerileri.md, 2026-11-26-ekstre-hareket-penceresi.md §3): belge açılınca hiç sorulmamış satırlar
// kendiliğinden sorulur; hazır öneri satırı işaretler, kaydetmez. Yanıt alamamış satırlar yalnız kullanıcı isteyince (onayla) yeniden sorulur.
public partial class EkstreAktarmaViewModel
{
    private readonly TekrarAnahtari _yapayZekaKey = new(), _olcumKey = new();
    /// <summary>Sunucudaki durum; sunucu desteklemiyorsa ya da alınamadıysa null (bölüm görünmez).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(YapayZekaGorunur), nameof(YapayZekaAcik), nameof(YapayZekaMetni))]
    private YapayZekaDurumDto? _yapayZeka;
    /// <summary>Sor düğmesinin altındaki sonuç ya da hata.</summary>
    [ObservableProperty] private string? _yapayZekaMesaji;
    [ObservableProperty] private string? _yapayZekaHatasi;
    [ObservableProperty] private string? _olcumHatasi;
    public ObservableCollection<string> GonderilecekSatirlar { get; } = new();
    [ObservableProperty][NotifyPropertyChangedFor(nameof(GonderilecekDugmesi))] private bool _gonderilecekAcik;
    public string GonderilecekDugmesi => GonderilecekAcik ? "Gönderilecek metni gizle" : "Gönderilecek metni göster";

    public bool YapayZekaGorunur => YapayZeka is not null;
    public bool YapayZekaAcik => YapayZeka?.Acik == true;
    /// <summary>Kural ve hafızanın tanımadığı, önerisi olmayan bekleyen satırlar.</summary>
    private int TaninmayanSayisi => Satirlar.Count(s => s.Secilebilir && s.Oneri?.Durum == "Yok");
    /// <summary>Yanıt alamamış (ya da hiç sorulmamış) tanınmayan satır var: "Yanıtsız satırları yeniden sor" kullanılabilir.</summary>
    public bool YanitsizSorulabilir => YapayZekaAcik && Belge is { Kaynak: not EkstreKaynaklari.Erp } && TaninmayanSayisi > 0;
    /// <summary>Kendiliğinden doldurmanın durumu: "Yapay zekâ N satırı dolduruyor… tur", bitince "Yapay zekâ N satırı doldurdu".</summary>
    [ObservableProperty] private string? _yapayZekaIlerleme;
    /// <summary>Bu oturumda kendiliğinden doldurmanın gönderdiği maskeli satırlar.</summary>
    public ObservableCollection<string> SonGonderilenler { get; } = new();
    private const int EnCokYapayZekaTuru = 10;
    private bool _yapayZekaDurdur;
    /// <summary>Pencere kapanınca: kendiliğinden doldurma başlamaz ya da sonraki tura geçmez (sürmekte olan soru biter, sonucu yazılır). Bayrak
    /// pencere açılmadan önce kalkar (<see cref="PencereyiAc"/>); açılışın kural isteği sürerken kapanan pencere yapay zekâyı başlatmaz.</summary>
    public void YapayZekaDurdur() => _yapayZekaDurdur = true;
    private void YapayZekaDurdurmayiKaldir() => _yapayZekaDurdur = false;
    /// <summary>Belge açılışı pencereyi açar: önceki pencerenin durdurması bu açılışa geçmez.</summary>
    private void PencereyiAc()
    {
        YapayZekaDurdurmayiKaldir();
        PencereAcIstendi?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>Başka belge açıldı: önceki belgenin ilerlemesi, hatası ve gönderilen metni görünmez.</summary>
    private void YapayZekaBelgeDurumunuTemizle()
    {
        YapayZekaIlerleme = null;
        YapayZekaHatasi = null;
        YapayZekaMesaji = null;
        SonGonderilenler.Clear();
        GonderilecekAcik = false;
    }
    /// <summary>Gönder onayının metni: gönderilecek ve gönderilmeyecek satırlar, en yüksek maliyet.</summary>
    public string YapayZekaOnayi(YapayZekaOnizlemeDto o)
    {
        var gonderilmeyen = TaninmayanSayisi - o.SatirSayisi - o.KalanSatir;
        return $"{o.SatirSayisi} satırın maskeli açıklaması{(o.OrnekSayisi > 0 ? $" ve {o.OrnekSayisi} geçmiş kararın maskeli örneği" : "")} Anthropic'e gönderilecek."
            + $" En çok {YapayZekaMetinleri.Dolar(o.EnYuksekMaliyetUsd)} tutar."
            + (gonderilmeyen > 0 ? $" Yalnız ad ve sayıdan oluşan {gonderilmeyen} satır gönderilmez." : "")
            + (o.KalanSatir > 0 ? $" {o.KalanSatir} satır sonraki soruya kalır." : "");
    }
    public string YapayZekaMetni => YapayZeka is not { } y ? ""
        : !y.Acik ? y.KapaliNedeni ?? "Kapalı."
        : string.Join("\n", new[]
        {
            $"Bu ay {YapayZekaMetinleri.Dolar(y.AyHarcamasiUsd)} / {YapayZekaMetinleri.Dolar(y.AylikSinirUsd)} · {y.AyCagri} çağrı · {y.Model}",
            y.Sonuclanan == 0 ? "Henüz sonuçlanan öneri yok." : $"Önerisi sonuçlanan {y.Sonuclanan} satırın {y.AyniTur} tanesi önerilen türde kapandı ({YapayZekaMetinleri.Yuzde(y.AyniTur, y.Sonuclanan)}).",
        }.Concat(y.Olcumler.Select(YapayZekaMetinleri.Olcum)));

    private void YapayZekayiBildir()
    {
        OnPropertyChanged(nameof(YanitsizSorulabilir));
    }
    partial void OnYapayZekaChanged(YapayZekaDurumDto? value) => YapayZekayiBildir();

    /// <summary>Durum alınamazsa (eski sunucu, bağlantı) bölüm gizlenir; ekstre işlemeye devam edilir.</summary>
    private async Task YapayZekaDurumunuAlAsync(int n)
    {
        if (!EditorMu || !Gecerli(n))
            return;
        YapayZekaDurumDto? durum = null;
        try
        { durum = await api.YapayZekaDurumuAsync(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or TimeoutException || e is KasaApiException { DurumKodu: not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) }) { }
        if (Gecerli(n))
            YapayZeka = durum;
    }

    /// <summary>Gönderilecek maskeli metin (hiçbir şey gönderilmez).</summary>
    public async Task<YapayZekaOnizlemeDto?> YapayZekaOnizleAsync()
    {
        YapayZekaOnizlemeDto? sonuc = null;
        await YurutAsync(async n =>
        {
            YapayZekaHatasi = null;
            if (!EditorMu || !VeriHazir || !YanitsizSorulabilir || Belge is not { } belge)
                return;
            var onizleme = await api.YapayZekaOnizlemeAsync(belge.Id);
            if (!Gecerli(n) || Belge?.Id != belge.Id)
                return;
            TakipMetni.Doldur(GonderilecekSatirlar, onizleme.Satirlar);
            if (onizleme.SatirSayisi == 0)
            {
                YapayZekaMesaji = "Tanınmayan satırlar yalnız ad ve sayıdan oluşuyor; gönderilecek bilgi yok.";
                return;
            }
            sonuc = onizleme;
        }, hataIsle: e => YapayZekaHatasi = HataMesaji(e));
        return sonuc;
    }

    /// <summary>Gönderilen metin: bu oturumda kendiliğinden doldurmanın gönderdikleri; gönderim olmadıysa sonraki gönderimde gidecekler.</summary>
    [RelayCommand]
    private async Task GonderilecekAcKapatAsync()
    {
        if (GonderilecekAcik)
        { GonderilecekAcik = false; return; }
        if (SonGonderilenler.Count > 0)
        {
            TakipMetni.Doldur(GonderilecekSatirlar, SonGonderilenler);
            GonderilecekAcik = true;
            return;
        }
        GonderilecekAcik = await YapayZekaOnizleAsync() is not null;
    }

    /// <summary>Belge açılınca hiç sorulmamış tanınmayan satırları onaysız sorar: "kalan" bitene, sunucu "sorulacak satır yok" diyene, bir turda öneri
    /// gelmeyene ya da hataya kadar, en çok <see cref="EnCokYapayZekaTuru"/> tur. Aynı yürütmenin içindedir (pencere bu sürede meşguldür).</summary>
    private async Task YapayZekaDoldurAsync(int n)
    {
        if (!EditorMu || !Gecerli(n) || _yapayZekaDurdur || !YapayZekaAcik || Belge is not { Kaynak: not EkstreKaynaklari.Erp } belge || TaninmayanSayisi == 0)
            return;
        YapayZekaHatasi = null;
        SonGonderilenler.Clear();
        var doldurulan = 0;
        try
        {
            for (var tur = 1; tur <= EnCokYapayZekaTuru; tur++)
            {
                var onizleme = await api.YapayZekaOnizlemeAsync(belge.Id, yalnizSorulmamis: true);
                if (!Gecerli(n) || _yapayZekaDurdur || Belge?.Id != belge.Id || onizleme.SatirSayisi == 0)
                    break;
                YapayZekaIlerleme = $"Yapay zekâ {onizleme.SatirSayisi + onizleme.KalanSatir} satırı dolduruyor… {tur}";
                YapayZekaSonucDto sonuc;
                try
                { sonuc = await api.YapayZekaSorAsync(belge.Id, new(_yapayZekaKey.Al(new { belge.Id, belge.Surum, tur }), YalnizSorulmamis: true)); }
                catch (KasaApiException e) when (e.DurumKodu == HttpStatusCode.Conflict && e.Message.Contains("sorulacak satır yok", StringComparison.Ordinal))
                { _yapayZekaKey.Temizle(); break; }
                catch (KasaApiException) { _yapayZekaKey.Temizle(); throw; }
                _yapayZekaKey.Temizle();
                if (!Gecerli(n) || Belge?.Id != belge.Id)
                    break;
                // Gönderilen metin yalnız gerçekten gönderilen (yanıtı alınan) sorudandır.
                foreach (var satir in onizleme.Satirlar)
                    SonGonderilenler.Add(satir);
                doldurulan += sonuc.Onerilen;
                await KurallariAlAsync(n);
                if (!Gecerli(n) || _yapayZekaDurdur || sonuc.KalanSatir == 0 || sonuc.Onerilen == 0)
                    break;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or TimeoutException
            || e is KasaApiException { DurumKodu: not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) })
        {
            if (Gecerli(n))
                YapayZekaHatasi = HataMesaji(e);
        }
        if (!Gecerli(n))
            return;
        YapayZekaIlerleme = doldurulan > 0 ? $"Yapay zekâ {doldurulan} satırı doldurdu" : null;
        YapayZekayiBildir();
        await YapayZekaDurumunuAlAsync(n);
    }

    /// <summary>Yanıt alamamış satırları da sorar (sayfa önce <see cref="YapayZekaOnayi"/> ile onay ister). Aynı istek yeniden gönderilirse ikinci kez
    /// ücret doğmaz.</summary>
    public Task YanitsizlariSorAsync() => YurutAsync(async n =>
    {
        YapayZekaMesaji = null;
        YapayZekaHatasi = null;
        if (!EditorMu || !VeriHazir || !YanitsizSorulabilir || Belge is not { } belge)
            return;
        YapayZekaSonucDto sonuc;
        try
        { sonuc = await api.YapayZekaSorAsync(belge.Id, new(_yapayZekaKey.Al(new { belge.Id, belge.Surum }))); }
        catch (KasaApiException) { _yapayZekaKey.Temizle(); throw; }
        _yapayZekaKey.Temizle();
        if (!Gecerli(n))
            return;
        GonderilecekAcik = false;
        GonderilecekSatirlar.Clear();
        if (Belge?.Id == belge.Id)
            await KurallariAlAsync(n);
        if (!Gecerli(n))
            return;
        YapayZekayiBildir();
        YapayZekaMesaji = sonuc.Not is { } not ? $"{not} ({YapayZekaMetinleri.Dolar(sonuc.MaliyetUsd)})"
            : $"{sonuc.Sorulan} satır soruldu, {sonuc.Onerilen} satıra öneri geldi ({YapayZekaMetinleri.Dolar(sonuc.MaliyetUsd)})."
                + (sonuc.KalanSatir > 0 ? $" {sonuc.KalanSatir} satır kaldı; yeniden sorabilirsiniz." : "");
        await YapayZekaDurumunuAlAsync(n);
    }, mesgulkenBildir: true, hataIsle: e => YapayZekaHatasi = HataMesaji(e));

    /// <summary>Geçmiş kararların yarısıyla doğruluk ölçümü (sayfa önce onay ister).</summary>
    public Task YapayZekaOlcAsync(string kaynak) => YurutAsync(async n =>
    {
        OlcumHatasi = null;
        if (!EditorMu || !VeriHazir || !YapayZekaAcik)
            return;
        try
        { await api.YapayZekaOlcAsync(new(_olcumKey.Al(kaynak), kaynak)); }
        catch (KasaApiException) { _olcumKey.Temizle(); throw; }
        _olcumKey.Temizle();
        await YapayZekaDurumunuAlAsync(n);
    }, mesgulkenBildir: true, hataIsle: e => OlcumHatasi = HataMesaji(e));

    private void YapayZekayiTemizle()
    {
        YapayZeka = null;
        YapayZekaMesaji = null;
        YapayZekaHatasi = null;
        OlcumHatasi = null;
        GonderilecekAcik = false;
        GonderilecekSatirlar.Clear();
        _yapayZekaKey.Temizle();
        _olcumKey.Temizle();
        YapayZekaIlerleme = null;
        SonGonderilenler.Clear();
    }
}

public static class YapayZekaMetinleri
{
    private static readonly System.Globalization.CultureInfo Tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
    /// <summary>Küçük tutarlar da görünür: "0,0026 $", "2,00 $".</summary>
    public static string Dolar(decimal usd) => usd.ToString(usd is > 0 and < 0.1m ? "0.0000" : "0.00", Tr) + " $";
    public static string Yuzde(int pay, int payda) => payda == 0 ? "—" : "%" + decimal.Round(100m * pay / payda, 0, MidpointRounding.AwayFromZero).ToString("0", Tr);
    public static string Olcum(YapayZekaOlcumDto o) =>
        $"{(o.Kaynak == EkstreKaynaklari.Kart ? "Kart" : "Banka")} ölçümü ({o.Zaman.LocalDateTime:dd.MM.yyyy}): {o.Satir} karar · tür {Yuzde(o.TurDogru, o.Satir)} · dağılım {Yuzde(o.DagilimDogru, o.DagilimSatiri)}"
        + $" · yüksek güvenlide tür {Yuzde(o.YuksekDogru, o.YuksekGuven)} · {o.Onerisiz} karara öneri yok · {Dolar(o.MaliyetUsd)}";
}
