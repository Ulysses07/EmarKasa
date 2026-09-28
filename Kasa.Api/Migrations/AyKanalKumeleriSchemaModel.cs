using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class AyKanalKumeleriSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        DenetimOlaylariSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.AyKanalKumesiEntity", e =>
        {
            e.ToTable("AyKanalKumeleri"); e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER"); e.HasKey("Id");
            foreach (var p in new[] { "Yil", "Ay" }) e.Property<int>(p).HasColumnType("INTEGER");
            e.Property<string>("Kaynak").IsRequired().HasColumnType("TEXT");
            e.Property<DateTimeOffset>("Zaman").HasColumnType("TEXT");
            e.HasIndex("Yil", "Ay").IsUnique();
        });
        b.Entity("Kasa.Api.Data.AyKanalKumesiKanalEntity", e =>
        {
            e.ToTable("AyKanalKumesiKanallari"); e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER"); e.HasKey("Id");
            foreach (var p in new[] { "KumeId", "KanalId", "Sira" }) e.Property<int>(p).HasColumnType("INTEGER");
            e.Property<bool>("Aktif").HasColumnType("INTEGER");
            e.HasIndex("KumeId", "KanalId").IsUnique(); e.HasIndex("KanalId");
            e.HasOne("Kasa.Api.Data.AyKanalKumesiEntity", null).WithMany().HasForeignKey("KumeId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            e.HasOne("Kasa.Api.Data.KanalEntity", null).WithMany().HasForeignKey("KanalId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
    }
}
