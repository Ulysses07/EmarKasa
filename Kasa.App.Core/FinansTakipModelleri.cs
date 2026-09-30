using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

public partial class TakipPayEditor : ObservableObject
{
    public IReadOnlyList<KanalDto> Kanallar { get; }
    [ObservableProperty] private KanalDto? _kanal;
    [ObservableProperty] private decimal _tutar;
    public TakipPayEditor(IReadOnlyList<KanalDto> kanallar) => Kanallar = kanallar;
}
public partial class TakipKanalSecimi(KanalDto veri) : ObservableObject
{
    public KanalDto Veri { get; } = veri;
    public string Ad => Veri.Ad + (Veri.Aktif ? "" : " (pasif)");
    [ObservableProperty] private bool _secili;
}
public static class TakipMetni
{
    public static string Paylar(IEnumerable<TakipKanalPayi> paylar) => string.Join(" · ", paylar.Select(p => $"{(p.KanalId is null ? KanalEtiketleri.DagilimBekliyor : p.Kanal)}: {Bicim.Tl(p.Tutar)} ₺"));
    public static string Gecis(TakipGecisDto g)
    {
        var satirlar = new List<string> { $"Geçiş: {g.Baslangic:dd.MM.yyyy}", $"Genel kasa farkı: {Bicim.Tl(g.GenelKasaAnlikFarki)} ₺ · kanal farkı: {Bicim.Tl(g.KanalAnlikFarki)} ₺" };
        // Kart geçişinde sunucu eski kuralla işlenen/bekleyen düşümleri ve önerilen tutarı hesaplar (web finance-ui.js
        // transitionPreview aynası). Kredi geçişinde ve eski sunucuda bu alanlar null'dır.
        if (g.SistemKartBorcu is { } sistem)
        {
            satirlar.Add(g.GenelKasaAnlikFarki switch
            {
                > 0 => "Artı fark: bu tutar kasadan hiçbir zaman düşmez; kasa bu kadar fazla görünür.",
                < 0 => "Eksi fark: bu tutar ödendiğinde kasadan düşer; açılış borcu kasadan ayrıca ödenmeyecekse ikinci kez düşer.",
                _ => "Fark yok: kasada önceden sayılan tutar önerilenle aynı; toplam kasa etkisi tutarlı.",
            });
            satirlar.Add($"Kasada önceden sayılan: {Bicim.Tl(g.EskiKasadaSayilanTutar)} ₺");
            satirlar.Add($"Sistem kart borcu: {Bicim.Tl(sistem)} ₺ (açılış borcu + eski kart giderleri − eski kart ödemeleri)");
            satirlar.Add($"Başlangıçtan önce eski kuralla kasadan düşen/düşecek: {Bicim.Tl(g.EskiKuraldaIslenenTutar ?? 0)} ₺");
            satirlar.Add(g.SonBekleyenDusumTarihi is { } son ? $"Bekleyen eski düşüm: {Bicim.Tl(g.BekleyenEskiDusumTutari ?? 0)} ₺ · son düşüm {son:dd.MM.yyyy}" : "Bekleyen eski düşüm yok.");
            if (g.OnerilenKasadaSayilanTutar is { } oneri)
                satirlar.Add($"Önerilen kasada önceden sayılan: {Bicim.Tl(oneri)} ₺" + (g.EnAzKasadaSayilanTutar is { } enAz && enAz < oneri ? $" · en az {Bicim.Tl(enAz)} ₺ (yalnız açılış borcu kasadan ayrıca ödenecekse)" : ""));
        }
        else
            satirlar.Add($"Kasada önceden sayılan: {Bicim.Tl(g.EskiKasadaSayilanTutar)} ₺");
        return string.Join("\n", satirlar.Concat(g.Aciklamalar));
    }
    /// <summary>KabulEdilebilir=false önizlemenin nedeni (sunucu /gecis bu girdiyi 409 ile reddeder).</summary>
    public static string GecisEngeli(TakipGecisDto g) => "Geçiş bu tutarlarla onaylanamaz: " +
        (g.OnerilenKasadaSayilanTutar is { } oneri && g.EskiKasadaSayilanTutar > oneri
            ? $"kasada önceden sayılan tutar önerilen {Bicim.Tl(oneri)} ₺ tutarını aşamaz; aşan kısım kasadan hiçbir zaman düşmez."
            : g.EnAzKasadaSayilanTutar is { } enAz && g.EskiKasadaSayilanTutar < enAz
                ? $"kasada önceden sayılan tutar en az {Bicim.Tl(enAz)} ₺ olmalı; altındaki kısım eski kuralla düşmüş/düşecek borçtur ve ödendiğinde kasadan ikinci kez düşer."
                : "önizleme kabul edilebilir değil; açıklamaları inceleyin.")
        + " Tutarı ve girdileri düzeltip yeniden önizleyin.";
    /// <summary>Onaylanmış kart geçişinin denetim izi (web transitionRecord aynası) ve uygulanan kural.</summary>
    public static string GecisKaydi(KartGecisDto g)
    {
        var satirlar = new List<string>
        {
            "Geçiş kuralı: " + g.Kural switch
            {
                "IslemTarihi" => "başlangıçtan önceki her eski kart gideri eski ay sonu kuralıyla bir kez düşer; sonraki harcamalar yalnız kaydedilen ödemeyle düşer.",
                "EtkiTarihi" => "ilk sürüm (etki tarihi): ay sonu düşümü başlangıçta veya sonrasında olan eski giderler raporlara girmez.",
                var kural => kural,
            },
        };
        if (g.Onizleme is { } k)
        {
            satirlar.Add($"Girilen kalan borç {Bicim.Tl(k.KalanBorc)} ₺ · sistem kart borcu {Bicim.Tl(k.SistemKartBorcu)} ₺");
            satirlar.Add($"Kasada önceden sayılan {Bicim.Tl(k.KasadaOncedenSayilanTutar)} ₺ · önerilen {Bicim.Tl(k.OnerilenKasadaSayilanTutar)} ₺");
            satirlar.Add(k.SonBekleyenDusumTarihi is { } son ? $"Bekleyen eski düşüm {Bicim.Tl(k.BekleyenEskiDusumTutari)} ₺ · son düşüm {son:dd.MM.yyyy}" : "Bekleyen eski düşüm yok.");
            satirlar.Add($"{k.OnayTarihi:dd.MM.yyyy} tarihinde onaylandı. Başlangıçtan önce eski kuralla düşen/düşecek kart gideri {Bicim.Tl(k.EskiKuraldaIslenenTutar)} ₺.");
        }
        else
            satirlar.Add("Bu geçiş ilk sürümde yapıldı; önizleme özeti saklanmadı.");
        if (!string.IsNullOrWhiteSpace(g.Aciklama))
            satirlar.Add($"Geçiş açıklaması: {g.Aciklama}");
        return string.Join("\n", satirlar);
    }
    /// <summary>İlk sürüm geçiş kalıntısı uyarısı; sunucu metni yoksa (eski sunucu) tutarlardan kurulur, kalıntı yoksa null.</summary>
    public static string? GecisUyarisi(KartGecisDto? g) => g switch
    {
        null => null,
        { Uyari: { Length: > 0 } uyari } => uyari,
        { RaporDisiEskiDusumTutari: 0, TahminiKasaFarki: 0 } => null,
        _ => $"İlk sürüm geçiş kalıntısı: başlangıçtan önceki kart giderlerinin {Bicim.Tl(g.RaporDisiEskiDusumTutari)} ₺ tutarındaki eski ay sonu düşümü"
            + (g.RaporDisiIlkDusumTarihi is { } ilk && g.RaporDisiSonDusumTarihi is { } son ? $" ({ilk:dd.MM.yyyy}–{son:dd.MM.yyyy})" : "")
            + $" raporlara girmiyor; tahmini kasa farkı {Bicim.Tl(g.TahminiKasaFarki)} ₺. Tutarları banka/kasa kayıtlarıyla doğrulayın.",
    };
    /// <summary>Geçişli kartın eski borç devri (web transferRecord aynası): devir, kasada önceden sayılan ve iadeyle kasaya
    /// dönen tutar, düzeltme sınırları ve varsa engel.</summary>
    public static string Devir(KartDevirDto d)
    {
        var satirlar = new List<string>
        {
            d.HarcamaId is null ? "Etkin eski borç devri yok; düzeltmeyle yeniden yazılabilir." : $"Devir {d.Tarih:dd.MM.yyyy} · kalan borç {Bicim.Tl(d.KalanBorc)} ₺ · kasada önceden sayılan {Bicim.Tl(d.KasadaOncedenSayilanTutar)} ₺",
            $"Devrin iadeleriyle kasaya dönen önceden sayılmış tutar: {Bicim.Tl(d.IadeDuzeltmesi)} ₺",
            $"Sistem kart borcu {Bicim.Tl(d.SistemKartBorcu)} ₺" + (d.RaporDisiTutar != 0 ? $" · raporlara girmeyen eski düşüm {Bicim.Tl(d.RaporDisiTutar)} ₺" : "")
                + $" · önerilen {Bicim.Tl(d.OnerilenKasadaSayilanTutar)} ₺" + (d.EnAzKasadaSayilanTutar < d.OnerilenKasadaSayilanTutar ? $" · en az {Bicim.Tl(d.EnAzKasadaSayilanTutar)} ₺ (açılış borcu kasadan ayrıca ödenecekse)" : ""),
            "Devrin ödemesi kasada önceden sayılan kısım kadar kasadan ikinci kez düşmez. Düzeltme etkin devri iptal edip aynı tarihle yeni tutarı yazar; geçmiş kasa sonuçları değişmez.",
        };
        if (d.Engel is { Length: > 0 } engel)
            satirlar.Add("Düzeltilemez: " + engel);
        return string.Join("\n", satirlar);
    }
    public static bool Ayni<T>(T a, T b) => System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(b);
    public static void Doldur<T>(ObservableCollection<T> liste, IEnumerable<T> veri) { liste.Clear(); foreach (var item in veri) liste.Add(item); }
    public static IReadOnlyList<KanalPayYaz> Paylar(IEnumerable<TakipPayEditor> paylar)
    {
        var satirlar = paylar.ToList();
        ParaAyristirici.Dogrula(satirlar.Select(p => p.Tutar).ToArray());
        if (satirlar.Any(p => p.Kanal is null || p.Tutar <= 0 || decimal.Round(p.Tutar, 2) != p.Tutar))
            throw new DogrulamaHatasi("Her dağılım satırında kanal ve pozitif, kuruş hassasiyetinde tutar girin.");
        if (satirlar.Select(p => p.Kanal!.Id).Distinct().Count() != satirlar.Count)
            throw new DogrulamaHatasi("Aynı kanalı iki kez seçmeyin.");
        return satirlar.Select(p => new KanalPayYaz(p.Kanal!.Id, p.Tutar)).ToList();
    }
}
public record KartTakipSatiri(KartTakipDto Veri)
{
    // Web kart listesindeki "Geçiş farkını doğrulayın" rozetinin karşılığı.
    public string Baslik => Veri.Ad + (!Veri.Aktif ? " · pasif" : "") + (!Veri.YeniTakip ? " · eski takip" : "") + (Veri.Gecis is { TahminiKasaFarki: not 0 } ? " · geçiş farkını doğrulayın" : "");
    public string Ozet => $"Kart borcu {Bicim.Tl(Veri.Borc)} ₺ · açık ekstre {Bicim.Tl(Veri.EkstreBorc)} ₺ · limit {Bicim.Tl(Veri.Limit)} ₺" +
        (Veri.Ekstreler.Where(e => e.Kalan > 0).OrderBy(e => e.SonOdemeTarihi).FirstOrDefault() is { } e ? $"\nİlk açık ekstrenin son ödemesi: {e.SonOdemeTarihi:dd.MM.yyyy}" : "");
}
public record EkstreSatiri(KartEkstreDto Veri)
{
    public string Baslik => $"Kesim {Veri.KesimTarihi:dd.MM.yyyy} · son ödeme {Veri.SonOdemeTarihi:dd.MM.yyyy}";
    public string AsgariDurumu => Veri.AsgariOdeme is null ? "Asgari ödeme girilmedi." : Veri.AsgariKalan is null ? "Asgari ödeme durumu alınamadı." : Veri.AsgariKalan <= 0 ? "Kayıtlı ödemelere göre asgari tamamlandı." : $"Kayıtlı ödemelere göre asgariye kalan: {Bicim.Tl(Veri.AsgariKalan.Value)} ₺";
    public string Ozet => $"Borç {Bicim.Tl(Veri.Borc)} ₺ · ödenen {Bicim.Tl(Veri.Odenen)} ₺ · kalan {Bicim.Tl(Veri.Kalan)} ₺ · asgari {(Veri.AsgariOdeme is { } a ? Bicim.Tl(a) + " ₺" : "girilmedi")}\n{AsgariDurumu}" + (Veri.Kalan > 0 && Veri.SonOdemeTarihi < DateOnly.FromDateTime(DateTime.Today) ? "\nSon ödeme tarihi geçti; kayıtlı kalan borç var." : "");
    public string Ad => $"{Veri.KesimTarihi:dd.MM.yyyy} · kalan {Bicim.Tl(Veri.Kalan)} ₺";
}
public record HarcamaSatiri(KartHarcamaDto Veri)
{
    public string Baslik => $"{Veri.Tarih:dd.MM.yyyy} · {Veri.Aciklama} · {Bicim.Tl(Veri.Tutar)} ₺";
    public string Ozet => (Veri.Iptal ? "İptal edildi" : $"{Veri.TaksitSayisi} taksit") + " · " + TakipMetni.Paylar(Veri.Dagilimlar) + (Veri.IslemId is { } id ? $" · gider #{id}" : "")
        + (Veri.KasadaSayilanDuzeltme > 0 ? $"\nÖnceden sayılan {Bicim.Tl(Veri.KasadaSayilanDuzeltme)} ₺ iade tarihinde kasaya döndü." : "");
}
public record KartOdemeSatiri(KartTakipOdemeDto Veri)
{
    /// <summary>Kilitli döneme düşen avansın dağıtım kaydı (tutarı 0): kasa değişmez, ayrıca iptal edilemez.</summary>
    public bool AvansDagitimi => Veri.AvansKaynakOdemeId is not null;
    public string Baslik => $"{Veri.Tarih:dd.MM.yyyy} · " + (AvansDagitimi ? "kilitli avans dağıtımı" : $"ödeme {Bicim.Tl(Veri.Tutar)} ₺") + (Veri.Iptal ? " · iptal" : "");
    public string Ozet => (AvansDagitimi ? "Kasa değişmez; kilitli dönemdeki avans bu tarihte harcamanın kanalına geçer" : $"Kasa çıkışı {Bicim.Tl(Veri.KasaEtkisi)} ₺")
        + $" · {TakipMetni.Paylar(Veri.Dagilimlar)}\n{Veri.Not}";
}
public record KrediTakipSatiri(KrediTakipDto Veri)
{
    public string Baslik => Veri.Ad + (!Veri.Aktif ? " · arşiv" : "") + (!Veri.YeniTakip ? " · eski takip" : "");
    public string Ozet => $"Çekim {Veri.CekimTarihi:dd.MM.yyyy} · çekilen {Bicim.Tl(Veri.CekilenTutar)} ₺ · kalan planlı ödeme {Bicim.Tl(Veri.KalanPlanliOdeme)} ₺ · {Veri.Taksitler.Count(t => t.Durum == TaksitDurumlari.Bekliyor)} taksit" +
        (Veri.Taksitler.Where(t => t.Durum == TaksitDurumlari.Bekliyor).OrderBy(t => t.Tarih).FirstOrDefault() is { } t ? $"\nSıradaki taksit: {t.Tarih:dd.MM.yyyy} · {Bicim.Tl(t.Tutar)} ₺" : "");
}
public record TaksitSatiri(KrediPlanTaksitDto Veri)
{
    public string Baslik => $"{Veri.No}. taksit · {Veri.Tarih:dd.MM.yyyy} · {Bicim.Tl(Veri.Tutar)} ₺";
    public string Ozet => (Veri.Durum switch { TaksitDurumlari.KasayaIslendi => "Kasaya işlendi; banka ödeme doğrulaması değildir", TaksitDurumlari.Iptal => "Plan değişikliğiyle iptal", _ => "Bekliyor; tarihinde otomatik düşer" }) + "\n" + TakipMetni.Paylar(Veri.Dagilimlar) + "\n" + Veri.Not;
}
public record TakipOlaySatiri(TakipOlayDto Veri, DateOnly? RaporTarihi = null)
{
    public string Baslik => $"{Veri.Tarih:dd.MM.yyyy} · {Veri.Ad} · {Bicim.Tl(Veri.Tutar)} ₺";
    public string Ozet => Veri.OtomatikKasa ? "Kredi taksidi: tarihinde otomatik kasaya işlenir." :
        Veri.Tur == "Kesim" ? "Kart hesap kesimi; banka ekstresi doğrulaması değildir." :
        Veri.Tarih < (RaporTarihi ?? DateOnly.FromDateTime(DateTime.Today)) ? "Son ödeme tarihi geçti; kayıtlı kalan borç var. Yalnız ödeme kaydıyla kasadan düşer." : "Kart: yalnız ödeme kaydıyla kasadan düşer.";
}
