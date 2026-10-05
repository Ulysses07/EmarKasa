using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public sealed class EkstreKuralEntity
{
    public int Id { get; set; }
    public int Surum { get; set; } = 1;
    public string Ad { get; set; } = "";
    public string Kaynak { get; set; } = "";
    public string? Banka { get; set; }
    public string AciklamaIcerir { get; set; } = "";
    public string? Yon { get; set; }
    public string IslemTuru { get; set; } = "";
    public string DagilimTuru { get; set; } = "";
    public string KanalIdsJson { get; set; } = "[]";
    public bool Aktif { get; set; } = true;
}

public partial class KasaDbContext
{
    public DbSet<EkstreKuralEntity> EkstreKurallar => Set<EkstreKuralEntity>();
}
