using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class EkstreSatirDurumuSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        EkstreKurallariSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.EkstreBelgeEntity", e => e.Property<string>("AtlananJson").IsRequired().HasColumnType("TEXT"));
        b.Entity("Kasa.Api.Data.EkstreYokSayilanEntity", e =>
        {
            e.ToTable("EkstreYokSayilanlar");
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.HasKey("Id");
            foreach (var p in new[] { "BelgeId", "SatirNo" })
                e.Property<int>(p).HasColumnType("INTEGER");
            e.HasIndex("BelgeId", "SatirNo").IsUnique();
            e.HasOne("Kasa.Api.Data.EkstreBelgeEntity", null).WithMany().HasForeignKey("BelgeId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
    }
}
