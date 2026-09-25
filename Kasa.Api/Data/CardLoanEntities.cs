namespace Kasa.Api.Data;

public class TakipKartEntity
{
    public int KrediKartiId { get; set; }
    public int Surum { get; set; } = 1;
    public DateOnly Baslangic { get; set; }
    public bool Aktif { get; set; } = true;
    public bool EskiKayit { get; set; }
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
