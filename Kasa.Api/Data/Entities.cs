using Kasa.Core;

namespace Kasa.Api.Data;

public class KanalEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public bool Aktif { get; set; } = true;
    public int Sira { get; set; }
    public decimal AcilisDevri { get; set; }
}

public class CariEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public bool Aktif { get; set; } = true;
}

public class IslemEntity
{
    public int Id { get; set; }
    public DateOnly Tarih { get; set; }
    public string Cari { get; set; } = "";
    public decimal TutarTl { get; set; }
    public string Kanal { get; set; } = "";
    public GiderTipi Tip { get; set; }
    public string? Not { get; set; }
    public int? KrediKartiId { get; set; }
}

public class GelenEntity
{
    public int Id { get; set; }
    public DateOnly DonemStart { get; set; }
    public string Kanal { get; set; } = "";
    public decimal TutarTl { get; set; }
}

/// <summary>Tek satırlık uygulama ayarları.</summary>
public class AyarEntity
{
    public int Id { get; set; }
    public DateOnly TakipBaslangic { get; set; }
    public decimal KasaAcilisDevri { get; set; }
    public string? IzleyiciSifreHash { get; set; }
    /// <summary>Artınca eski izleyici token'ları geçersiz olur (şifre değişimi, oturum kapatma).</summary>
    public int IzleyiciOturumSurumu { get; set; }
    /// <summary>Artınca eski editör token'ları geçersiz olur.</summary>
    public int EditorOturumSurumu { get; set; }
}

public class KrediKartiEntity
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public DateOnly KesimTarihi { get; set; }
    public DateOnly SonOdemeTarihi { get; set; }
    public decimal Limit { get; set; }
    public decimal Borc { get; set; }
}

public class KartOdemeEntity
{
    public int Id { get; set; }
    public int KrediKartiId { get; set; }
    public DateOnly Tarih { get; set; }
    public decimal Tutar { get; set; }
    public string? Not { get; set; }
}
