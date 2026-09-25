using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

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
    public static string Paylar(IEnumerable<TakipKanalPayi> paylar) => string.Join(" · ", paylar.Select(p => $"{(p.KanalId is null ? "Dağılım bekliyor" : p.Kanal)}: {Bicim.Tl(p.Tutar)} ₺"));
    public static string Gecis(TakipGecisDto g) => $"Geçiş: {g.Baslangic:dd.MM.yyyy}\nGenel kasa farkı: {Bicim.Tl(g.GenelKasaAnlikFarki)} ₺ · kanal farkı: {Bicim.Tl(g.KanalAnlikFarki)} ₺\nKasada önceden sayılan: {Bicim.Tl(g.EskiKasadaSayilanTutar)} ₺\n" + string.Join("\n", g.Aciklamalar);
    public static bool Ayni<T>(T a, T b) => System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(b);
    public static void Doldur<T>(ObservableCollection<T> liste, IEnumerable<T> veri) { liste.Clear(); foreach (var item in veri) liste.Add(item); }
    public static IReadOnlyList<KanalPayYaz> Paylar(IEnumerable<TakipPayEditor> paylar)
    {
        var satirlar = paylar.ToList();
        if (satirlar.Any(p => p.Kanal is null || p.Tutar <= 0 || decimal.Round(p.Tutar, 2) != p.Tutar)) throw new KasaApiException(System.Net.HttpStatusCode.BadRequest, "Her dağılım satırında kanal ve pozitif, kuruş hassasiyetinde tutar girin.");
        if (satirlar.Select(p => p.Kanal!.Id).Distinct().Count() != satirlar.Count) throw new KasaApiException(System.Net.HttpStatusCode.BadRequest, "Aynı kanalı iki kez seçmeyin.");
        return satirlar.Select(p => new KanalPayYaz(p.Kanal!.Id, p.Tutar)).ToList();
    }
}
public record KartTakipSatiri(KartTakipDto Veri)
{
    public string Baslik => Veri.Ad + (!Veri.Aktif ? " · pasif" : "") + (!Veri.YeniTakip ? " · eski takip" : "");
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
    public string Ozet => (Veri.Iptal ? "İptal edildi" : $"{Veri.TaksitSayisi} taksit") + " · " + TakipMetni.Paylar(Veri.Dagilimlar) + (Veri.IslemId is { } id ? $" · gider #{id}" : "");
}
public record KartOdemeSatiri(KartTakipOdemeDto Veri)
{
    public string Baslik => $"{Veri.Tarih:dd.MM.yyyy} · ödeme {Bicim.Tl(Veri.Tutar)} ₺" + (Veri.Iptal ? " · iptal" : "");
    public string Ozet => $"Kasa çıkışı {Bicim.Tl(Veri.KasaEtkisi)} ₺ · {TakipMetni.Paylar(Veri.Dagilimlar)}\n{Veri.Not}";
}
public record KrediTakipSatiri(KrediTakipDto Veri)
{
    public string Baslik => Veri.Ad + (!Veri.Aktif ? " · arşiv" : "") + (!Veri.YeniTakip ? " · eski takip" : "");
    public string Ozet => $"Çekim {Veri.CekimTarihi:dd.MM.yyyy} · çekilen {Bicim.Tl(Veri.CekilenTutar)} ₺ · kalan planlı ödeme {Bicim.Tl(Veri.KalanPlanliOdeme)} ₺ · {Veri.Taksitler.Count(t => t.Durum == "Bekliyor")} taksit" +
        (Veri.Taksitler.Where(t => t.Durum == "Bekliyor").OrderBy(t => t.Tarih).FirstOrDefault() is { } t ? $"\nSıradaki taksit: {t.Tarih:dd.MM.yyyy} · {Bicim.Tl(t.Tutar)} ₺" : "");
}
public record TaksitSatiri(KrediPlanTaksitDto Veri)
{
    public string Baslik => $"{Veri.No}. taksit · {Veri.Tarih:dd.MM.yyyy} · {Bicim.Tl(Veri.Tutar)} ₺";
    public string Ozet => (Veri.Durum switch { "KasayaIslendi" => "Kasaya işlendi; banka ödeme doğrulaması değildir", "Iptal" => "Plan değişikliğiyle iptal", _ => "Bekliyor; tarihinde otomatik düşer" }) + "\n" + TakipMetni.Paylar(Veri.Dagilimlar) + "\n" + Veri.Not;
}
public record TakipOlaySatiri(TakipOlayDto Veri, DateOnly? RaporTarihi = null)
{
    public string Baslik => $"{Veri.Tarih:dd.MM.yyyy} · {Veri.Ad} · {Bicim.Tl(Veri.Tutar)} ₺";
    public string Ozet => Veri.OtomatikKasa ? "Kredi taksidi: tarihinde otomatik kasaya işlenir." :
        Veri.Tur == "Kesim" ? "Kart hesap kesimi; banka ekstresi doğrulaması değildir." :
        Veri.Tarih < (RaporTarihi ?? DateOnly.FromDateTime(DateTime.Today)) ? "Son ödeme tarihi geçti; kayıtlı kalan borç var. Yalnız ödeme kaydıyla kasadan düşer." : "Kart: yalnız ödeme kaydıyla kasadan düşer.";
}
