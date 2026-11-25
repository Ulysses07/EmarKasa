using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class EkstreHafizasiSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        EkstreSatirDurumuSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.EkstreHafizaUnutulanEntity", e =>
        {
            e.ToTable("EkstreHafizaUnutulanlar");
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.HasKey("Id");
            foreach (var p in new[] { "Kaynak", "Anahtar", "Yon" })
                e.Property<string>(p).IsRequired().HasColumnType("TEXT");
            foreach (var p in new[] { "SonKayitId", "SonYokSayId" })
                e.Property<int>(p).HasColumnType("INTEGER");
            e.HasIndex("Kaynak", "Anahtar", "Yon").IsUnique();
        });
    }
}
