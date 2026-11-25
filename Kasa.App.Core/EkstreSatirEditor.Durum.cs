using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.Core.Kodlar;

namespace Kasa.App.Core;

// Satırın durumu (bekleyen/kapanan), giriş-çıkış gösterimi ve toplu uygulama (docs/specs/2026-11-25-pdf-ekrani.md).
public partial class EkstreSatirEditor
{
    private readonly bool _kartBelgesi;
    /// <summary>Açık düzenleyicideki işlemin (eşleşme adayları, hatırla) sonucu; düğmenin altında görünür.</summary>
    [ObservableProperty] private string? _formMesaji;
    /// <summary>Satır kaydedilmeden kapatıldı (yok sayıldı); geri alınabilir.</summary>
    public bool YokSayildi { get; }
    /// <summary>Satır düzenleyicisinde "Yok say" görünür: sunucu destekliyor ve satır bekliyor (TL olmayan satır da kapatılabilir).</summary>
    public bool YokSayilabilir { get; }
    /// <summary>Kapanan sekmesindeki satır: iptal edilmemiş kaydı var ya da yok sayıldı.</summary>
    public bool Kapali => Kayitli || YokSayildi;
    /// <summary>Satırın iptal edilmemiş kaydı (Kapanan sekmesinde sonucu ve iptali).</summary>
    public EkstreKayitSatiri? Kayit { get; init; }
    /// <summary>Kapanan satırın başlığı: kaydı varsa kaydedilen (düzeltilmiş olabilir) tarih, açıklama ve tutar.</summary>
    public string KapananBaslik => Kayit?.Baslik ?? Ozet;
    private string TarihYazisi => DateOnly.TryParseExact(TarihMetni.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
        ? t.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : TarihMetni.Trim() is { Length: > 0 } yazilan ? yazilan : "tarih okunamadı";
    private string TutarYazisi
    {
        get
        {
            var tutar = ParaAyristirici.Coz(TutarMetni, out var t, out _) ? Bicim.Tl(t) : TutarMetni.Trim();
            return tutar.Length == 0 || TutarMetni.Trim().Length == 0 ? "tutar okunamadı"
                : Kaynak.ParaBirimi switch { "TRY" or "TL" => $"{tutar} ₺", "Belirsiz" => $"{tutar} (para birimi belirsiz)", var birim => $"{tutar} {birim}" };
        }
    }
    public string SonucMetni => YokSayildi ? "Yok sayıldı · kaydedilmedi, kasa etkisi yok" : Kayit?.Ozet ?? "";

    private static string? TurYonu(string? tur, bool kartBelgesi) => tur switch
    {
        EkstreIslemTurleri.Gelir or EkstreIslemTurleri.KartIade => "Giris",
        EkstreIslemTurleri.Gider or EkstreIslemTurleri.KartHarcama => "Cikis",
        // Aynı ödeme banka hesabından çıkar, kart hesabına girer.
        EkstreIslemTurleri.KartOdemesi => kartBelgesi ? "Giris" : "Cikis",
        _ => null
    };

    /// <summary>Satırın yönü: kaydın ya da seçilen işlem türünün yönü; tür seçilmediyse PDF'den okunan yön.</summary>
    public string EtkinYon => TurYonu(Kayit?.Veri.IslemTuru ?? IslemTuru?.Kod, _kartBelgesi) ?? Kaynak.Yon;
    public static string YonAdi(string yon) => yon switch { "Giris" => "Giriş +", "Cikis" => "Çıkış −", _ => "Belirsiz" };
    /// <summary>"Giriş +", "Çıkış −" ya da "Belirsiz"; yanında dayanağı ya da seçilen türle çelişen PDF yönü.</summary>
    public string YonMetni => YonAdi(EtkinYon) + (Kaynak.Yon is "Giris" or "Cikis" && Kaynak.Yon != EtkinYon ? $" · belgede: {YonAdi(Kaynak.Yon)}"
        : EtkinYon != Kaynak.Yon ? "" : Kaynak.YonKaynagi switch
        {
            YonKaynaklari.Sutun => " · belgenin kolonundan",
            YonKaynaklari.Isaret => " · tutarın işaretinden",
            YonKaynaklari.Bakiye => " · bakiyeden",
            YonKaynaklari.Sozcuk => " · açıklamadan",
            _ => ""
        });

    /// <summary>Toplamlarda kullanılan tutar: düzenlenen tutar geçerliyse o, değilse (ya da satır kapalıysa) kaydın/PDF'in tutarı.</summary>
    public decimal? EtkinTutar => Kayit?.Veri.Tutar ?? (ParaAyristirici.Coz(TutarMetni, out var tutar, out _) && tutar > 0 ? tutar : Kaynak.Tutar);

    /// <summary>Belge toplamına girer: TL satırı ya da kaydı olan satır (dövizli satır yok sayılınca TL toplamına girmez).</summary>
    public bool ToplamaGirer => Kayitli || TlMi;

    /// <summary>Aynı satırın önceki modelindeki taslağı (alanlar ve işaret) alır; seçimler bu modelin güncel kanal ve kart listesinden
    /// yapılır. Artık olmayan kanalın payı kanalsız kalır (önizlemede seçilmesi istenir), eşleşme adayı yeniden getirilir.</summary>
    public void TaslagiAl(EkstreSatirEditor eski, bool isaret = true)
    {
        if (!Secilebilir && !YokSayilabilir || Kapali)
            return;
        _oneriUygulaniyor = true;
        try
        {
            TarihMetni = eski.TarihMetni;
            Aciklama = eski.Aciklama;
            TutarMetni = eski.TutarMetni;
            IslemTuru = IslemTurleri.FirstOrDefault(t => t.Kod == eski.IslemTuru?.Kod);
            DagilimTuru = DagilimTurleri.FirstOrDefault(t => t.Kod == eski.DagilimTuru?.Kod);
            Kart = Kartlar.FirstOrDefault(k => k.Id == eski.Kart?.Id);
            KaynakHarcama = KaynakHarcamalar.FirstOrDefault(h => h.Veri.Id == eski.KaynakHarcama?.Veri.Id);
            Paylar.Clear();
            foreach (var pay in eski.Paylar)
                Paylar.Add(new(Kanallar) { Kanal = Kanallar.FirstOrDefault(k => k.Id == pay.Kanal?.Id), Tutar = pay.Tutar });
            Secili = isaret && Secilebilir && eski.Secili;
        }
        finally { _oneriUygulaniyor = false; }
        ElleDegisti = eski.ElleDegisti;
        HafizaDayanaginiAl(eski);
    }

    /// <summary>İşaretli satırlara toplu uygulama: tür (null: satırdaki tür kalır) ve dağılım. Dağılımı seçilmeyen tür (karta ödeme,
    /// iade, eşleştirme) ya da türü belli olmayan satır değişmez.</summary>
    /// <returns>Uygulandıysa true.</returns>
    public bool TopluUygula(string? turKodu, string dagilimKodu, IReadOnlyCollection<int> kanalIds)
    {
        var tur = turKodu is null ? IslemTuru : IslemTurleri.FirstOrDefault(t => t.Kod == turKodu);
        if (!Secilebilir || tur?.Kod is not (EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider or EkstreIslemTurleri.KartHarcama)
            || IslemTuru?.Kod is EkstreIslemTurleri.KartOdemesi or EkstreIslemTurleri.KartIade or EkstreIslemTurleri.Eslestir
            // Seçilen kanallardan biri artık yoksa pay sessizce kalanlara kaymaz.
            || kanalIds.Any(id => !Kanallar.Any(k => k.Id == id)))
            return false;
        IslemTuru = tur;
        DagilimTuru = DagilimTurleri.Single(d => d.Kod == dagilimKodu);
        Paylar.Clear();
        foreach (var kanal in Kanallar.Where(k => kanalIds.Contains(k.Id)))
            Paylar.Add(new(Kanallar) { Kanal = kanal });
        return true;
    }
}
