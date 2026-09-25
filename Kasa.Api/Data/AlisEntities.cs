using System.Text.Json.Serialization;

namespace Kasa.Api.Data;

public static class AlisDurumlari
{
    public const string Taslak = "Taslak";
    public const string Incelemede = "Incelemede";
    public const string Onaylandi = "Onaylandi";
}

public class AliciEntity
{
    public int Id { get; set; }
    public string Kullanici { get; set; } = "";
    public string Ad { get; set; } = "";
    [JsonIgnore]
    public string SifreHash { get; set; } = "";
    public bool Aktif { get; set; } = true;
    [JsonIgnore]
    public int OturumSurumu { get; set; }
}

public class AlisEntity
{
    public int Id { get; set; }
    public int Surum { get; set; } = 1;
    public int? AliciId { get; set; }
    public AliciEntity? AliciKaydi { get; set; }
    public DateOnly Tarih { get; set; }
    public string Tedarikci { get; set; } = "";
    public int? TedarikciId { get; set; }
    public CariEntity? TedarikciKaydi { get; set; }
    public DateOnly? Vade { get; set; }
    public string? Not { get; set; }
    public string Durum { get; set; } = AlisDurumlari.Taslak;
    public string? EditorNotu { get; set; }
    public List<AlisKalemEntity> Kalemler { get; set; } = [];
    public List<AlisOdemeEntity> Odemeler { get; set; } = [];
}

public class AlisKalemEntity
{
    public int Id { get; set; }
    public int AlisId { get; set; }
    public string Aciklama { get; set; } = "";
    public decimal Tutar { get; set; }
    public decimal? Miktar { get; set; }
    public decimal? BirimFiyat { get; set; }
    public List<AlisDagilimEntity> Dagilimlar { get; set; } = [];
}

public class AlisDagilimEntity
{
    public int Id { get; set; }
    public int AlisKalemId { get; set; }
    public int KanalId { get; set; }
    public KanalEntity KanalKaydi { get; set; } = null!;
    public decimal Tutar { get; set; }
}

public class AlisOdemeEntity
{
    public int Id { get; set; }
    public int AlisId { get; set; }
    public int IslemId { get; set; }
    public IslemEntity Islem { get; set; } = null!;
    public Guid IstekId { get; set; }
    public string IstekOzeti { get; set; } = "";
}
