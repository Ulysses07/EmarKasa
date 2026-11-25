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

/// <summary>İşlem hafızasında unutulan anahtar (docs/specs/2026-11-25-islem-hafizasi.md): kimliği <see cref="SonKayitId"/> ve
/// <see cref="SonYokSayId"/>'den büyük olmayan kararlar hafızaya girmez; sonrakiler yeniden öğrenilir. Kayıtları değiştirmez.</summary>
public sealed class EkstreHafizaUnutulanEntity
{
    public int Id { get; set; }
    public string Kaynak { get; set; } = "";
    public string Anahtar { get; set; } = "";
    public string Yon { get; set; } = "";
    public int SonKayitId { get; set; }
    public int SonYokSayId { get; set; }
}

public partial class KasaDbContext
{
    public DbSet<EkstreKuralEntity> EkstreKurallar => Set<EkstreKuralEntity>();
    public DbSet<EkstreHafizaUnutulanEntity> EkstreHafizaUnutulanlar => Set<EkstreHafizaUnutulanEntity>();
}
