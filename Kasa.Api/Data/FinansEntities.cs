namespace Kasa.Api.Data;

public class HesapEntity
{
    public int Id { get; set; }
    public int Surum { get; set; } = 1;
    public string Ad { get; set; } = "";
    public string Tur { get; set; } = "Kasa";
    public DateOnly AcilisTarihi { get; set; }
    public decimal AcilisBakiyesi { get; set; }
    public bool Aktif { get; set; } = true;
}

// Kaynaklı satırların tarih/tutarı kaynağından okunur; ikinci bir mali hareket değildir.
public class HesapHareketEntity
{
    public int Id { get; set; }
    public int HesapId { get; set; }
    public int? IslemId { get; set; }
    public IslemEntity? Islem { get; set; }
    public int? GelenId { get; set; }
    public GelenEntity? Gelen { get; set; }
    public int? KartOdemeId { get; set; }
    public KartOdemeEntity? KartOdeme { get; set; }
    public int? KrediId { get; set; }
    public KrediEntity? Kredi { get; set; }
    public int? KanalId { get; set; }
    public KanalEntity? Kanal { get; set; }
    public DateOnly Tarih { get; set; }
    public decimal Tutar { get; set; }
    public string Aciklama { get; set; } = "";
}

public class HesapTransferEntity
{
    public int Id { get; set; }
    public int KaynakHesapId { get; set; }
    public int HedefHesapId { get; set; }
    public DateOnly Tarih { get; set; }
    public decimal Tutar { get; set; }
    public string Aciklama { get; set; } = "";
}

public class FinansIstekEntity
{
    public int Id { get; set; }
    public Guid IstekId { get; set; }
    public string Ozet { get; set; } = "";
    public string Tur { get; set; } = "";
    public int SonucId { get; set; }
    public string? OncekiJson { get; set; }
}

public class KrediTaksitOdemeEntity
{
    public int Id { get; set; }
    public int KrediId { get; set; }
    public int TaksitNo { get; set; }
    public int IslemId { get; set; }
    public IslemEntity Islem { get; set; } = null!;
}
