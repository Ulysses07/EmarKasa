using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class AyRaporAnlikGoruntuleriSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        KartGecisIziSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.AyRaporAnlikGoruntuEntity", e =>
        {
            e.ToTable("AyRaporAnlikGoruntuleri");
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.HasKey("Id");
            foreach (var p in new[] { "Yil", "Ay", "KuralSurumu" })
                e.Property<int>(p).HasColumnType("INTEGER");
            e.Property<string>("Json").IsRequired().HasColumnType("TEXT");
            e.Property<DateTimeOffset>("Zaman").HasColumnType("TEXT");
            e.HasIndex("Yil", "Ay").IsUnique();
        });
    }
}
