// Alan kodları: API sözleşmesinde ve veritabanında taşınan sabit dizeler; sunucu (Kasa.Api), hesap motoru ve masaüstü
// görünüm modelleri (Kasa.App.Core) aynı sabiti kullanır, dize elle kopyalanmaz. Değerler sözleşme ve veritabanı değeridir:
// değiştirmek veri dönüşümü ve istemci uyumu gerektirir.
//
// Ayrı isim alanındadır: Kasa.App.Core hem Kasa.Core'a hem Kasa.ApiClient'a başvurur ve iki derlemede de GiderTipi vardır
// (Kasa.Core.GiderTipi, Kasa.ApiClient.GiderTipi). "using Kasa.Core;" ile "using Kasa.ApiClient;" birlikteyken GiderTipi'yi
// kullanan dosya CS0104 (belirsiz başvuru) verir; "using Kasa.Core.Kodlar;" yalnız bu sabitleri getirir. Buradaki tür adları
// Kasa.ApiClient ve Kasa.App.Core türleriyle çakışmaz (Kasa.Sozlesme.Tests/MimariTests).
namespace Kasa.Core.Kodlar;

/// <summary>Gerçek kanal olmayan kanal etiketleri (<see cref="Islem.Kanal"/>, rapor ve döküm satırlarının kanal alanı). Gerçek
/// kanallar kullanıcı tanımlıdır (KanalEntity); bu etiketler kanal adı olarak kullanılamaz. Kredi çekiminin iç etiketi
/// <see cref="KrediTuretici.KrediKanal"/>'dır.</summary>
public static class KanalEtiketleri
{
    /// <summary>Belirli bir kanala ait olmayan ortak gider: aylık raporda o ayın Ortak kümesine dağıtılır.</summary>
    public const string Ortak = "Ortak";
    /// <summary>Gerçek giderin kanalı henüz kesinleşmedi; ortak paya dağıtılmaz, kasadan düşer.</summary>
    public const string DagilimBekliyor = "Dağılım bekliyor";
    /// <summary>Yalnız genel kasayı etkileyen kaydın etiketi (kanal payı olmayan gider ve gelir, genel kasa dökümü).</summary>
    public const string GenelKasa = "Genel kasa";
}

/// <summary>Alış kaydının iş akışı durumu (AlisEntity.Durum, AlisDto.Durum).</summary>
public static class AlisDurumlari
{
    public const string Taslak = "Taslak";
    public const string Incelemede = "Incelemede";
    public const string Onaylandi = "Onaylandi";
}

/// <summary>Kanal dağılımının biçimi (AylikGiderRevizyonEntity.DagilimTuru, EkstreKayitEntity.DagilimTuru; AylikGiderSablonYaz,
/// EkstreSatirYaz ve karşılık gelen DTO'ların DagilimTuru alanı). Aylık gider yalnız <see cref="Genel"/>, <see cref="Esit"/> ve
/// <see cref="Ozel"/> alır; <see cref="Otomatik"/> ve <see cref="Eslesme"/> yalnız ekstre satırındadır.</summary>
public static class DagilimBicimleri
{
    /// <summary>Kanal payı yok: tutar yalnız genel kasayı etkiler.</summary>
    public const string Genel = "Genel";
    /// <summary>Tutar seçilen kanallara eşit bölünür.</summary>
    public const string Esit = "Esit";
    /// <summary>Kanal tutarları elle girilir; toplamı kaydın tutarına eşittir.</summary>
    public const string Ozel = "Ozel";
    /// <summary>Kart ödemesi ya da iadesi: dağılım kaynak borçtan (kartın kayıtlı dağılımı) okunur, elle seçilmez.</summary>
    public const string Otomatik = "Otomatik";
    /// <summary>Mevcut kayıtla eşleştirilen satır (<see cref="EkstreIslemTurleri.Eslestir"/>): dağılım eşleşen kayıttadır.</summary>
    public const string Eslesme = "Eslesme";
}

/// <summary>Ekstre satırının işlem türü (EkstreKayitEntity.IslemTuru, EkstreSatirYaz.IslemTuru, EkstreOkunanSatir.OnerilenIslem).
/// Banka belgesinde <see cref="Gelir"/>, <see cref="Gider"/>, <see cref="KartOdemesi"/>; kart belgesinde <see cref="KartHarcama"/>,
/// <see cref="KartIade"/>, <see cref="KartOdemesi"/>; ikisinde de <see cref="Eslestir"/>.</summary>
public static class EkstreIslemTurleri
{
    public const string Gelir = "Gelir";
    public const string Gider = "Gider";
    public const string KartHarcama = "KartHarcama";
    public const string KartIade = "KartIade";
    public const string KartOdemesi = "KartOdemesi";
    /// <summary>Satır yeni kayıt üretmez, mevcut bir kayda bağlanır (<see cref="EslesmeTurleri"/>); kasa ve kart borcu değişmez.</summary>
    public const string Eslestir = "Eslestir";
    /// <summary>Yalnız okuyucunun önerisi (OnerilenIslem): satır kaydedilmez, seçilebilir işlem türü değildir.</summary>
    public const string Atla = "Atla";
}

/// <summary>Ekstre belgesinin kaynağı (EkstreBelgeEntity.Kaynak, ekstre yükleme formunun 'kaynak' alanı, EkstreBelgeDto.Kaynak).</summary>
public static class EkstreKaynaklari
{
    /// <summary>Kredi kartı ekstresi: belge bir karta bağlıdır.</summary>
    public const string Kart = "Kart";
    /// <summary>Banka hesap hareketleri.</summary>
    public const string Banka = "Banka";
}

/// <summary>Ekstre satırının bağlandığı mevcut kaydın türü (EkstreKayitEntity.EslesmeTuru, EkstreSatirYaz.EslesenKayitTuru,
/// EkstreEslesmeAdayiDto.Tur).</summary>
public static class EslesmeTurleri
{
    /// <summary>Kartsız gider (Islem).</summary>
    public const string Gider = "Gider";
    /// <summary>Kart harcaması (TakipHarcama).</summary>
    public const string KartHarcama = "KartHarcama";
    /// <summary>Kart harcamasının taksidi (TakipKartTaksit).</summary>
    public const string KartTaksidi = "KartTaksidi";
    /// <summary>Karta ödeme (TakipKartOdeme).</summary>
    public const string KartOdeme = "KartOdeme";
}

/// <summary>Ekstre satırının eşleşmesinin bugünkü durumu (EkstreKayitDto.EslesmeDurumu; saklanmaz, okurken hesaplanır).</summary>
public static class EslesmeDurumlari
{
    /// <summary>Eşleşen kayıt duruyor ve iptal edilmemiş.</summary>
    public const string Eslesti = "Eslesti";
    /// <summary>Eşleşen kayıt silinmiş ya da iptal edilmiş.</summary>
    public const string KayitYok = "KayitYok";
}
