namespace Kasa.Api.Data;

public class TakipKartEntity
{
    public int KrediKartiId { get; set; }
    public int Surum { get; set; } = 1;
    public DateOnly Baslangic { get; set; }
    public bool Aktif { get; set; } = true;
    public bool EskiKayit { get; set; }
    public EskiDusumKurali EskiDusumKurali { get; set; }
    /// <summary>Onaylanan eski kart geçişinin açıklaması (denetim izi). İlk sürüm geçişlerinde saklanmadı: null.</summary>
    public string? GecisAciklamasi { get; set; }
    /// <summary>Onay anındaki önizleme özeti ve girilen tutarlar (<see cref="Kasa.Api.KartGecisKaydi"/> JSON). İlk sürüm geçişlerinde null.</summary>
    public string? GecisOzetiJson { get; set; }
}
/// <summary>Eski karttan geçişte, başlangıçtan önceki eski kart giderlerinin kasaya düşüş kuralı.</summary>
public enum EskiDusumKurali
{
    /// <summary>İlk sürüm: eski ay sonu etkisi başlangıçtan önceyse sayılır, sonrası atlanır.
    /// Bu kuralla yapılmış (canlıdaki) geçişlerin raporları aynen korunur.</summary>
    EtkiTarihi = 0,
    /// <summary>İşlem tarihi başlangıçtan önceki her eski gider, eski ay sonu kuralıyla
    /// (gerekirse geçişten sonraki ay sonunda) bir kez düşer; bekleyen düşüm kaybolmaz.</summary>
    IslemTarihi = 1,
}
public class TakipEkstreEntity
{
    public int Id { get; set; }
    public int KrediKartiId { get; set; }
    public DateOnly KesimTarihi { get; set; }
    public DateOnly SonOdemeTarihi { get; set; }
    public decimal? AsgariOdeme { get; set; }
}
public class TakipHarcamaEntity
{
    public int Id { get; set; }
    public int KrediKartiId { get; set; }
    public int? IslemId { get; set; }
    public int? KaynakHarcamaId { get; set; }
    public DateOnly Tarih { get; set; }
    public string Aciklama { get; set; } = "";
    public decimal Tutar { get; set; }
    public int TaksitSayisi { get; set; } = 1;
    public bool Iptal { get; set; }
    public string DagilimJson { get; set; } = "[]";
    public decimal KasadaOncedenSayilanTutar { get; set; }
}
public class TakipKartTaksitEntity
{
    public int Id { get; set; }
    public int HarcamaId { get; set; }
    public int EkstreId { get; set; }
    public decimal Tutar { get; set; }
}
public class TakipKartOdemeEntity
{
    public int Id { get; set; }
    public int KrediKartiId { get; set; }
    public DateOnly Tarih { get; set; }
    public decimal Tutar { get; set; }
    public string? Not { get; set; }
    public bool Iptal { get; set; }
    // Kaynak taksit payları sabittir; kanal adı/alış onayı mali toplamı değiştirmez.
    public string PaylarJson { get; set; } = "[]";
}
/// <summary>
/// Kart iadesinin hesap kaydı (iade harcamasıyla bire bir; gap-coklu-giris-cift-sayim-mutabakat-3, finance-2). Bu kaydı
/// olan iadenin kanal payı dondurulmaz: her okumada kaynak harcamanın güncel payından, iade anındaki ödenmiş kısım
/// düşülerek aynı kuralla türetilir (<see cref="Kasa.Api.FinansTakipServisi.IadePayi"/>); kaynak alış yeniden
/// dağıtılınca iade de yeni oranı izler. Kaydı olmayan iade eski kuraldır: DagilimJson'daki dondurulmuş pay kullanılır.
/// </summary>
public class TakipIadeHesabiEntity
{
    public int HarcamaId { get; set; }
    /// <summary>İade anında kaynak harcamanın taksitlerine yapılmış (iptal edilmemiş) ödemeler toplamı.</summary>
    public decimal IadeAnindaOdenen { get; set; }
    /// <summary>Kaynak harcama eski borç devriyse, kasada önceden sayılan tutarın (K) bu iadeyle düşen kısmı: iade
    /// tarihinde kasaya geri döner, devrin sonraki ödemelerinde kasada sayılmış kabul edilmez. Diğer iadelerde 0.</summary>
    public decimal KasadaSayilanDuzeltme { get; set; }
}
/// <summary>
/// Kilitli döneme düşen kart avansının dağıtımı (finance-8). Avans ödemesinin payları kilitli ayın raporunu
/// değiştirmemek için yeniden yazılmaz; avansın yeni harcamaya bağlanan kısmı, kilit sonrası tarihli ve tutarı 0 olan
/// ayrı bir kart ödemesiyle (<see cref="OdemeId"/>) dağıtılır: taksit payları ve aynı tutarda eksi avans payı taşır.
/// Kasa değişmez; "Dağılım bekliyor" payı o tarihte ilgili kanala geçer.
/// </summary>
public class TakipAvansTahsisEntity
{
    public int OdemeId { get; set; }
    /// <summary>Avansı dağıtılan (kilitli dönemdeki) kart ödemesi.</summary>
    public int KaynakOdemeId { get; set; }
}
public class TakipKrediEntity
{
    public int KrediId { get; set; }
    public int Surum { get; set; } = 1;
    public DateOnly Baslangic { get; set; }
    public bool Aktif { get; set; } = true;
    public bool EskiKayit { get; set; }
    public bool MevcutKredi { get; set; }
    public string KanalIdleriJson { get; set; } = "[]";
    public string CekimPaylariJson { get; set; } = "[]";
}
public class TakipKrediTaksitEntity
{
    public int Id { get; set; }
    public int KrediId { get; set; }
    public int No { get; set; }
    public DateOnly Tarih { get; set; }
    public decimal Tutar { get; set; }
    public bool Iptal { get; set; }
    public string? Not { get; set; }
    public string DagilimJson { get; set; } = "[]";
}
