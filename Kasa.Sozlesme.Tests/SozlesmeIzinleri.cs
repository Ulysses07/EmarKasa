using Kasa.ApiClient;

namespace Kasa.Sozlesme.Tests;

/// <summary>
/// Sözleşme farklarının gerekçeli izin listesi. Buradaki her satır bilinen ve bilinçli bir farktır; listede olmayan her
/// fark sözleşme testini kırar. Satır eklemek yerine önce DTO'yu eşitlemeyi düşünün: sunucu fazlası masaüstünün
/// göstermediği bilgi, istemci fazlası masaüstünde varsayılan değerle (null/0/false) görünen alan demektir.
/// </summary>
public static class SozlesmeIzinleri
{
    public enum Yon
    {
        /// <summary>Sunucu yanıtta gönderiyor, istemci DTO'su tanımıyor.</summary>
        SunucuFazlasi,
        /// <summary>İstemci DTO'su bekliyor, sunucu yanıtta göndermiyor.</summary>
        IstemciFazlasi,
        /// <summary>İstemci istekte gönderiyor, ucun bağladığı sunucu türü tanımıyor.</summary>
        IstekFazlasi,
    }

    /// <param name="Uc">"YÖNTEM yol" ({id} ile); null ise türün bütün uçlarında geçerli. İstek farklarında tür, sunucunun
    /// bağladığı türdür.</param>
    public sealed record Izin(Yon Yon, Type Tur, string Alan, string? Uc, string Gerekce);

    public static readonly IReadOnlyList<Izin> Liste =
    [
        // Sunucu fazlası: masaüstünün kullanmadığı bilgi.
        new(Yon.SunucuFazlasi, typeof(IslemDto), "kanalId", null,
            "Masaüstü gideri kanal adıyla gösterir ve süzer; kanal kimliği okunmaz."),
        new(Yon.SunucuFazlasi, typeof(KrediDto), "kanalId", null,
            "Eski kredi listesi; masaüstünde çağıranı yok (eski uç), kanal adı yeterli."),
        new(Yon.SunucuFazlasi, typeof(KanalHaftalikDto), "krediGirisi", null,
            "Gelen'in içindeki kredi çekimi payı; Gelen toplamı onu zaten içerir, masaüstü ayrıca göstermiyor (izleme notu)."),
        new(Yon.SunucuFazlasi, typeof(KanalAylikDto), "krediGirisi", null,
            "Gelen'in içindeki kredi çekimi payı; Gelen toplamı onu zaten içerir, masaüstü ayrıca göstermiyor (izleme notu)."),
        new(Yon.SunucuFazlasi, typeof(BildirimAyarDto), "sonHata", null,
            "Bildirim hattının son sunucu hatası web ayarlarında görünür; masaüstü göstermiyor (izleme notu)."),
        new(Yon.SunucuFazlasi, typeof(BildirimAyarDto), "sonHataZamani", null,
            "Bildirim hattının son sunucu hatasının anı; masaüstü göstermiyor (izleme notu)."),
        new(Yon.SunucuFazlasi, typeof(YedekDurumuDto), "sonOtomatikYedek", null,
            "Yedek türü ayrıntısı; masaüstü yalnız son yedeği, doğrulamayı, hatayı ve rotasyon uyarısını gösterir."),
        new(Yon.SunucuFazlasi, typeof(YedekDurumuDto), "otomatikYedekSayisi", null, "Yedek türü ayrıntısı; masaüstünde gösterilmez."),
        new(Yon.SunucuFazlasi, typeof(YedekDurumuDto), "sonElleYedek", null, "Yedek türü ayrıntısı; masaüstünde gösterilmez."),
        new(Yon.SunucuFazlasi, typeof(YedekDurumuDto), "elleYedekSayisi", null, "Yedek türü ayrıntısı; masaüstünde gösterilmez."),

        // İstemci fazlası: genel gider yazma yanıtı kayıt varlığıdır; liste okumasındaki bağ alanları yoktur. Bu uçtan
        // yazılan gider alış/aylık gider/ekstre bağı taşıyamaz (bağlı gider 409 alır), varsayılan değerler doğrudur.
        new(Yon.IstemciFazlasi, typeof(IslemDto), "alisId", "POST api/islemler", "Yeni genel gider alışa bağlı değildir."),
        new(Yon.IstemciFazlasi, typeof(IslemDto), "dagilimBekliyor", "POST api/islemler", "Genel gider kanalı yazılırken seçilir."),
        new(Yon.IstemciFazlasi, typeof(IslemDto), "aylikGiderOdemeId", "POST api/islemler", "Yeni genel gider aylık gider ödemesi değildir."),
        new(Yon.IstemciFazlasi, typeof(IslemDto), "ekstreKayitId", "POST api/islemler", "Yeni genel gider ekstre kaydı değildir."),
        new(Yon.IstemciFazlasi, typeof(IslemDto), "alisId", "PUT api/islemler/{id}", "Alışa bağlı gider bu uçta 409 alır."),
        new(Yon.IstemciFazlasi, typeof(IslemDto), "dagilimBekliyor", "PUT api/islemler/{id}", "Genel gider kanalı yazılırken seçilir."),
        new(Yon.IstemciFazlasi, typeof(IslemDto), "aylikGiderOdemeId", "PUT api/islemler/{id}", "Aylık gider ödemesi bu uçta değiştirilemez (409)."),
        new(Yon.IstemciFazlasi, typeof(IslemDto), "ekstreKayitId", "PUT api/islemler/{id}", "Ekstre kaydı bu uçta değiştirilemez (409)."),

        // Eski kart/kredi uçları: masaüstünde çağıranı olmayan metotlar (KrediKartiGuncelleAsync, KrediGuncelleAsync);
        // düzeltme yanıtı kart varlığıdır, türetilmiş borç alanları yalnız listede (GET api/kredikartlari) doludur.
        new(Yon.IstemciFazlasi, typeof(KrediKartiDto), "guncelBorc", "PUT api/kredikartlari/{id}", "Eski uç; türetilmiş alan yalnız listede."),
        new(Yon.IstemciFazlasi, typeof(KrediKartiDto), "acilisBorc", "PUT api/kredikartlari/{id}", "Eski uç; türetilmiş alan yalnız listede."),
        new(Yon.IstemciFazlasi, typeof(KrediKartiDto), "harcamaToplam", "PUT api/kredikartlari/{id}", "Eski uç; türetilmiş alan yalnız listede."),
        new(Yon.IstemciFazlasi, typeof(KrediKartiDto), "odemeToplam", "PUT api/kredikartlari/{id}", "Eski uç; türetilmiş alan yalnız listede."),
        new(Yon.IstemciFazlasi, typeof(KrediKartiDto), "ekstreBorc", "PUT api/kredikartlari/{id}", "Eski uç; türetilmiş alan yalnız listede."),
        new(Yon.IstekFazlasi, typeof(Kasa.Api.KrediYazDto), "id", "PUT api/krediler/{id}",
            "Eski uç: istemci gövde olarak KrediDto gönderir; kimlik yoldadır, sunucu gövdedekini yok sayar."),
        new(Yon.IstekFazlasi, typeof(Kasa.Api.KrediYazDto), "gerceklesmeTakibi", "PUT api/krediler/{id}",
            "Eski uç: gerçekleşme takibi düzeltmede korunur (sunucu mevcut değeri yazar); istemcinin gönderdiği yok sayılır."),
        new(Yon.IstekFazlasi, typeof(Kasa.Api.KrediYazDto), "id", "POST api/krediler", "Emekli uç: sunucu her isteği 409 ile reddeder (KrediEkleAsync)."),
        new(Yon.IstekFazlasi, typeof(Kasa.Api.KrediYazDto), "gerceklesmeTakibi", "POST api/krediler", "Emekli uç: sunucu her isteği 409 ile reddeder (KrediEkleAsync)."),
    ];

    public static bool Izinli(Yon yon, Type tur, string alan, string uc) => Liste.Any(i => i.Yon == yon && i.Tur == tur
        && string.Equals(i.Alan, alan, StringComparison.OrdinalIgnoreCase) && (i.Uc is null || i.Uc == uc));
}
