using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;
using Kasa.Core.Kodlar;
using CekKurallari = Kasa.Core.CekKurallari;

namespace Kasa.App.Core;

/// <summary>Kodlu seçim çipi (CipGrubu: "Ad" ve "Secili"): yön, durum süzgeci ve hareket türü çipleri. <see cref="Kod"/> sunucuya
/// giden değerdir (Kasa.Core.Kodlar), <see cref="Ad"/> görünen metin.</summary>
public partial class KodCipi(string kod, string ad) : ObservableObject
{
    public string Kod { get; } = kod;
    public string Ad { get; } = ad;
    [ObservableProperty] private bool _secili;
}

/// <summary>Çekler ekranının üst şeridindeki ve panel kutusundaki hazır süzgeçler (tasarım "Masaüstü ekranı", "Panel"). Panel
/// kutusu sayfayı //cekler?Suzgec={ad} ile açar.</summary>
public enum CekHazirSuzgec { Portfoy, Alinan30, Verilen30, VadesiGecmis }

/// <summary>Çek metinleri: durum, konum, vade rozeti ve tutar biçimi (görünen ad kuralları Kasa.Core.CekKurallari'ndadır).</summary>
public static class CekMetni
{
    public static string Tur(string tur) => tur switch { CekTurleri.Senet => "Senet", _ => "Çek" };
    public static string Yon(string yon) => yon switch { CekYonleri.Verilen => "Verilen", _ => "Alınan" };
    public static string Durum(string durum) => CekKurallari.DurumAdi(durum);

    /// <summary>Durum süzgeci çipinin adı.</summary>
    public static string Suzgec(string suzgec) => suzgec switch
    {
        CekSuzgecleri.Portfoyde => "Portföyde",
        CekSuzgecleri.Karsiliksiz => "Karşılıksız",
        CekSuzgecleri.Kapanan => "Kapananlar",
        _ => "Hepsi",
    };
    public static string Hareket(string tur) => CekKurallari.HareketAdi(tur);

    public static string Konum(string? konum) => konum switch
    {
        CekKonumlari.Elde => "Elde",
        CekKonumlari.BankadaTahsilde => "Bankada tahsilde",
        CekKonumlari.Teminatta => "Teminatta",
        CekKonumlari.Icrada => "İcrada",
        _ => "",
    };

    /// <summary>Özet satırı (üst şerit ve panel kutusu): "Ad: 10.000,00 ₺ (2 çek)".</summary>
    public static string OzetSatiri(string ad, CekOzetKalemi kalem) => $"{ad}: {Bicim.Tl(kalem.Toplam)} ₺ ({kalem.Adet} çek)";

    // Panel kutusu (CekOzetViewModel) ve Çekler ekranının üst şeridi (CekTakipViewModel) aynı üç/dört satırı gösterir; metin
    // tek yerden üretilir ki ikisi arasında söz dizimi kaymasın.
    public static string PortfoyMetni(CekOzetDto? ozet) => ozet is { } o ? OzetSatiri("Portföydeki alınan", o.PortfoydekiAlinan) : "";
    public static string Alinan30Metni(CekOzetDto? ozet) => ozet is { } o ? OzetSatiri("30 gün içinde tahsil edilecek", o.Alinan30) : "";
    public static string Verilen30Metni(CekOzetDto? ozet) => ozet is { } o ? OzetSatiri("30 gün içinde ödenecek", o.Verilen30) : "";
    public static string GecmisMetni(CekOzetDto? ozet) => ozet is { } o ? OzetSatiri("Vadesi geçmiş, tahsil edilmemiş", o.VadesiGecmis) : "";

    /// <summary>Vade rozeti: "5 gün kaldı", "Bugün", "3 gün geçti".</summary>
    public static string Rozet(DateOnly vade, DateOnly bugun) => (vade.DayNumber - bugun.DayNumber) switch
    {
        0 => "Bugün",
        > 0 and var kalan => $"{kalan} gün kaldı",
        var gecen => $"{-gecen} gün geçti",
    };
}

/// <summary>Çek listesinin satırı (TakipUi.Liste: Baslik ve Ozet). Başlık vade, rozet ve kişi; özet banka · no, tutar, kalan, durum,
/// konum ve teminat işareti.</summary>
public sealed class CekSatiri(CekDto veri, DateOnly bugun)
{
    public CekDto Veri { get; } = veri;
    public string Baslik => $"{Veri.VadeTarihi:dd.MM.yyyy} · {CekMetni.Rozet(Veri.VadeTarihi, bugun)} · {Veri.Kisi}";

    public string Ozet
    {
        get
        {
            var parcalar = new List<string> { $"{CekMetni.Tur(Veri.Tur)} {Veri.Banka ?? "—"} · {Veri.No}", $"{Bicim.Tl(Veri.Tutar)} ₺" };
            if (Veri.Kalan > 0 && Veri.Kalan != Veri.Tutar)
                parcalar.Add($"Kalan: {Bicim.Tl(Veri.Kalan)} ₺");
            parcalar.Add(CekMetni.Durum(Veri.Durum));
            if (CekMetni.Konum(Veri.Konum) is { Length: > 0 } konum && Veri.Durum is CekDurumlari.Portfoyde or CekDurumlari.KismenTahsilEdildi or CekDurumlari.Karsiliksiz)
                parcalar.Add(konum);
            if (Veri.Teminat)
                parcalar.Add("Teminat");
            return string.Join(" · ", parcalar);
        }
    }
}

/// <summary>Açık çekin hareket satırı.</summary>
public sealed class CekHareketSatiri(CekHareketDto veri)
{
    public CekHareketDto Veri { get; } = veri;
    public string Baslik => $"{Veri.Sira}. {CekMetni.Hareket(Veri.Tur)} · {Veri.Tarih:dd.MM.yyyy}";

    public string Ozet => string.Join(" · ", new[]
    {
        Veri.Tutar != 0 ? $"{Bicim.Tl(Veri.Tutar)} ₺" : null,
        Veri.NetTutar is { } net ? $"hesaba geçen {Bicim.Tl(net)} ₺, masraf {Bicim.Tl(Veri.Tutar - net)} ₺" : null,
        Veri.Kanal,
        Veri.Karsi,
    }.OfType<string>());
}
