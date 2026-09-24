namespace Kasa.ApiClient;

public enum GiderTipi { Cari, SabitGider, KrediKarti }

/// <summary>Çekin yönü: alınan (müşteriden) ya da verilen (kesilen).</summary>
public enum CekYonu { Alinan, Verilen }

/// <summary>
/// Çek durumu. Alınan: Portfoyde/TahsilEdildi/CiroEdildi/Karsiliksiz/IadeEdildi;
/// verilen: Portfoyde (= ödenecek)/Odendi/IadeEdildi. Kasayı yalnız TahsilEdildi/Odendi etkiler.
/// </summary>
public enum CekDurumu { Portfoyde, TahsilEdildi, Odendi, CiroEdildi, Karsiliksiz, IadeEdildi }

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
public record CekDto(int Id, CekYonu Yon, string? CekNo, string? Banka, string Kisi, decimal Tutar,
    DateOnly DuzenlemeTarihi, DateOnly VadeTarihi, string Kanal, CekDurumu Durum, DateOnly? IslemTarihi, string? Not);
/// <summary>
/// Çek özeti: portföydeki alınan / ödenecek verilen toplam ve adet; vadesi <see cref="YaklasanGun"/> gün
/// içinde gelen ve vadesi geçtiği hâlde portföyde bekleyen çekler (iki yön, vadeye göre artan).
/// </summary>
public record CekOzetDto(decimal PortfoydekiAlinanToplam, int PortfoydekiAlinanAdet,
    decimal OdenecekVerilenToplam, int OdenecekVerilenAdet, int YaklasanGun,
    IReadOnlyList<CekDto> Yaklasanlar, IReadOnlyList<CekDto> VadesiGecenler);

/// <summary>
/// Değişiklik geçmişi satırı. <see cref="Eylem"/>: Eklendi / Güncellendi / Silindi / Eklendi (geri alındı).
/// <see cref="GeriAlinabilir"/> sunucu kurallarıyla hesaplanır (silinmiş, desteklenen tür, 30 gün içinde, geri alınmamış).
/// </summary>
public record DegisiklikDto(
    int Id, DateTime ZamanUtc, string Rol, string Tur, int? KayitId, string Eylem, string Ozet,
    string? EskiJson, string? YeniJson, bool GeriAlindi, DateTime? GeriAlmaZamaniUtc, bool GeriAlinabilir);
/// <summary>Geçmişin bir sayfası (en yeni önce) + filtreye uyan toplam satır sayısı.</summary>
public record DegisiklikSayfasi(IReadOnlyList<DegisiklikDto> Kayitlar, int Toplam);

/// <summary>Her ay tekrarlayan sabit gider (kira, SGK, maaş…). Ayın günü ay daha kısaysa ayın son gününe düşer.</summary>
public record TekrarlayanGiderDto(int Id, string Kalem, string Kanal, decimal Tutar, int AyinGunu, bool Aktif, DateOnly BaslangicAyi);
/// <summary>Vadesi gelmiş, henüz girilmemiş/atlanmamış tekrarlayan gider ayı (<see cref="Ay"/> ayın 1'i).</summary>
public record BekleyenGiderDto(int TekrarlayanGiderId, string Kalem, string Kanal, decimal Tutar, DateOnly Ay, DateOnly Vade);

public record DonemDto(DateOnly Start, DateOnly End, int Yil, int Ay);
/// <summary>Gelen/Giden çek hariçtir; çek tahsilatı/ödemesi CekGelen/CekGiden'dedir (Sonuc ve Devir çek dahil).</summary>
public record KanalHaftalikDto(string Kanal, decimal Gelen, decimal Giden, decimal Sonuc, decimal Devir,
    decimal CekGelen = 0m, decimal CekGiden = 0m)
{
    /// <summary>Görünüm kolaylığı: dönemde bu kanalın çek hareketi var mı.</summary>
    public bool CekVar => CekGelen != 0m || CekGiden != 0m;
}
/// <summary>ToplamGelen/ToplamGiden çek hariçtir; çekler ToplamCekGelen/ToplamCekGiden'dedir (KasaSonucu/KasaDevir çek dahil).</summary>
public record HaftalikOzetDto(
    DonemDto Donem,
    IReadOnlyList<KanalHaftalikDto> Kanallar,
    decimal ToplamGelen,
    decimal ToplamGiden,
    decimal KasaSonucu,
    decimal KasaDevir,
    decimal ToplamCekGelen = 0m,
    decimal ToplamCekGiden = 0m)
{
    /// <summary>Görünüm kolaylığı: dönemde çek tahsilatı ya da ödemesi var mı.</summary>
    public bool CekVar => ToplamCekGelen != 0m || ToplamCekGiden != 0m;
}
/// <summary>Gelen/CariGiden/OrtakPay çek hariçtir; CekGiden kanalın çek ödemeleri + Ortak çek ödemesi payıdır (AySonucu çek dahil).</summary>
public record KanalAylikDto(string Kanal, decimal Gelen, decimal CariGiden, decimal SabitGider, decimal KrediKarti, decimal OrtakPay, decimal AySonucu,
    decimal CekGelen = 0m, decimal CekGiden = 0m)
{
    /// <summary>Görünüm kolaylığı: ayda bu kanalın çek hareketi var mı.</summary>
    public bool CekVar => CekGelen != 0m || CekGiden != 0m;
}
public record AylikRaporDto(int Yil, int Ay, IReadOnlyList<KanalAylikDto> Kanallar);
public record KanalBakiyeDto(string Kanal, decimal Bakiye);
public record PanelDto(decimal GuncelKasa, IReadOnlyList<KanalBakiyeDto> Kanallar, decimal BuHaftaSonucu, decimal BuAySonucu);

/// <summary>Sunucudan indirilen dosya (Excel'e aktar): sunucunun önerdiği ad + içerik.</summary>
public record IndirilenDosya(string DosyaAdi, byte[] Icerik);

/// <summary>
/// Kasa sayımı. HesaplananTutar kayıt anındaki defter kasasıdır (değişmez), Fark = Sayılan − Hesaplanan.
/// GuncelHesaplanan aynı günün bugünkü defter değeridir (geçmiş düzeltildiyse farklıdır; takvim dışıysa null).
/// </summary>
public record KasaSayimDto(int Id, DateOnly Tarih, decimal SayilanTutar, decimal HesaplananTutar, decimal Fark,
    decimal? GuncelHesaplanan, string? Not, DateTime KayitZamaniUtc);
/// <summary>Bir günün sonundaki defter kasası (sayım formu önizlemesi).</summary>
public record KasaHesapDto(DateOnly Tarih, decimal HesaplananTutar);

// Mutasyon gövdeleri (Id sunucuda atanır; create'te gönderilmez)
public record KanalYaz(string Ad, bool Aktif, int Sira, decimal AcilisDevri);
public record CariYaz(string Ad, bool Aktif);
public record GiderKalemiYaz(string Ad, bool Aktif);
public record IslemYaz(DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip, string? Not, int? KrediKartiId = null);
public record GelenYaz(DateOnly DonemStart, string Kanal, decimal TutarTl);
public record KrediKartiYaz(string Ad, DateOnly KesimTarihi, DateOnly SonOdemeTarihi, decimal Limit, decimal Borc);
public record KartOdemeYaz(int KrediKartiId, DateOnly Tarih, decimal Tutar, string? Not);
public record AyarYaz(DateOnly TakipBaslangic, decimal KasaAcilisDevri);
public record CekYaz(CekYonu Yon, string? CekNo, string? Banka, string Kisi, decimal Tutar,
    DateOnly DuzenlemeTarihi, DateOnly VadeTarihi, string Kanal, CekDurumu Durum, DateOnly? IslemTarihi, string? Not);

public record KasaSayimYaz(DateOnly Tarih, decimal SayilanTutar, string? Not);

/// <param name="BaslangicAyi">
/// Gönderilmezse (null) sunucu karar verir: yeni kayıtta bu ay (Türkiye saati), güncellemede eski değer kalır.
/// </param>
public record TekrarlayanGiderYaz(string Kalem, string Kanal, decimal Tutar, int AyinGunu, bool Aktif,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? BaslangicAyi = null);
/// <summary>Bekleyen bir ayı sabit gider işlemi olarak girer (<paramref name="Ay"/> o ayın herhangi bir günü olabilir).</summary>
public record TekrarlayanOnayYaz(DateOnly Ay, DateOnly Tarih, decimal Tutar);
