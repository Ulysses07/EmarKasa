namespace Kasa.ApiClient;

public enum GiderTipi { Cari, SabitGider, KrediKarti }

public record LoginYanit(string Rol, string Token);

public record KanalDto(int Id, string Ad, bool Aktif, int Sira, decimal AcilisDevri);
public record CariDto(int Id, string Ad, bool Aktif);
public record GiderKalemiDto(int Id, string Ad, bool Aktif);
public record IslemDto(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not, int? KrediKartiId = null);
/// <summary>İşlem listesinin bir sayfası: kayıtlar (tarih, id artan) + filtreye uyan toplam kayıt sayısı.</summary>
public record IslemSayfasi(IReadOnlyList<IslemDto> Kayitlar, int Toplam);
public record GelenDto(int Id, DateOnly DonemStart, string Kanal, decimal TutarTl);
public record KrediKartiDto(int Id, string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc, decimal GuncelBorc = 0m, decimal AcilisBorc = 0m, decimal HarcamaToplam = 0m, decimal OdemeToplam = 0m, decimal EkstreBorc = 0m);
public record KartOdemeDto(int Id, int KrediKartiId, DateOnly Tarih, decimal Tutar, string? Not);
public record AyarlarDto(DateOnly TakipBaslangic, decimal KasaAcilisDevri, bool IzleyiciSifreVarMi);
/// <summary>Her ay tekrarlayan sabit gider (kira, SGK, maaş…). Ayın günü ay daha kısaysa ayın son gününe düşer.</summary>
public record TekrarlayanGiderDto(int Id, string Kalem, string Kanal, decimal Tutar, int AyinGunu, bool Aktif, DateOnly BaslangicAyi);
/// <summary>Vadesi gelmiş, henüz girilmemiş/atlanmamış tekrarlayan gider ayı (<see cref="Ay"/> ayın 1'i).</summary>
public record BekleyenGiderDto(int TekrarlayanGiderId, string Kalem, string Kanal, decimal Tutar, DateOnly Ay, DateOnly Vade);

public record DonemDto(DateOnly Start, DateOnly End, int Yil, int Ay);
public record KanalHaftalikDto(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir);
public record HaftalikOzetDto(
    DonemDto Donem,
    IReadOnlyList<KanalHaftalikDto> Kanallar,
    decimal ToplamGelen,
    decimal ToplamGiden,
    decimal KasaSonucu,
    decimal KasaDevir);
public record KanalAylikDto(string Kanal, decimal Gelen, decimal CariGiden, decimal SabitGider, decimal KrediKarti, decimal OrtakPay, decimal AySonucu);
public record AylikRaporDto(int Yil, int Ay, IReadOnlyList<KanalAylikDto> Kanallar);
public record KanalBakiyeDto(string Kanal, decimal Bakiye);
public record PanelDto(decimal GuncelKasa, IReadOnlyList<KanalBakiyeDto> Kanallar, decimal BuHaftaSonucu, decimal BuAySonucu);

// Mutasyon gövdeleri (Id sunucuda atanır; create'te gönderilmez)
public record KanalYaz(string Ad, bool Aktif, int Sira, decimal AcilisDevri);
public record CariYaz(string Ad, bool Aktif);
public record GiderKalemiYaz(string Ad, bool Aktif);
public record IslemYaz(DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not, int? KrediKartiId = null);
public record GelenYaz(DateOnly DonemStart, string Kanal, decimal TutarTl);
public record KrediKartiYaz(string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc);
public record KartOdemeYaz(int KrediKartiId, DateOnly Tarih, decimal Tutar, string? Not);
public record AyarYaz(DateOnly TakipBaslangic, decimal KasaAcilisDevri);
/// <param name="BaslangicAyi">
/// Gönderilmezse (null) sunucu karar verir: yeni kayıtta bu ay (Türkiye saati), güncellemede eski değer kalır.
/// </param>
public record TekrarlayanGiderYaz(string Kalem, string Kanal, decimal Tutar, int AyinGunu, bool Aktif,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? BaslangicAyi = null);
/// <summary>Bekleyen bir ayı sabit gider işlemi olarak girer (<paramref name="Ay"/> o ayın herhangi bir günü olabilir).</summary>
public record TekrarlayanOnayYaz(DateOnly Ay, DateOnly Tarih, decimal Tutar);
