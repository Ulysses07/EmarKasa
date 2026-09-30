namespace Kasa.Api.Data;

public class AylikGiderSablonEntity
{
    public int Id { get; set; }
    public int Surum { get; set; } = 1;
}

// Revizyonlar silinmez: geçmiş ayın planı sonradan değişmez.
public class AylikGiderRevizyonEntity
{
    public int Id { get; set; }
    public int SablonId { get; set; }
    public int Surum { get; set; }
    public DateOnly GecerliAy { get; set; }
    public string Ad { get; set; } = "";
    public string Tur { get; set; } = "";
    public decimal Tutar { get; set; }
    public int OdemeGunu { get; set; }
    public string DagilimTuru { get; set; } = "Genel";
    public string DagilimJson { get; set; } = "[]";
    public bool Aktif { get; set; } = true;
}

public class AylikGiderOdemeEntity
{
    public int Id { get; set; }
    public int SablonId { get; set; }
    public int RevizyonId { get; set; }
    public DateOnly Ay { get; set; }
    public DateOnly Tarih { get; set; }
    public decimal Tutar { get; set; }
    public int? IslemId { get; set; }
    public bool Iptal { get; set; }
    public string? IptalAciklamasi { get; set; }
}

public class AyKilidiEntity
{
    public int Id { get; set; }
    public int Surum { get; set; } = 1;
    public DateOnly? KilitliSonTarih { get; set; }
}

public class AyKilidiOlayEntity
{
    public int Id { get; set; }
    public DateOnly? OncekiSonTarih { get; set; }
    public DateOnly? YeniSonTarih { get; set; }
    public string Aciklama { get; set; } = "";
    public DateTimeOffset Zaman { get; set; }
}
