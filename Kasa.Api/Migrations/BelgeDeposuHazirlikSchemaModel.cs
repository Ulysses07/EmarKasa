using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class BelgeDeposuHazirlikSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        KartTakipDuzeltmeleriSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.BelgeEntity", e =>
        {
            foreach (var p in new[] { "IcerikOzeti", "YukleyenRol", "SilenRol", "SilmeGerekcesi" })
                e.Property<string>(p).HasColumnType("TEXT");
            foreach (var p in new[] { "YukleyenId", "SilenId" })
                e.Property<int?>(p).HasColumnType("INTEGER");
            e.Property<bool>("Silindi").HasColumnType("INTEGER");
            e.Property<DateTimeOffset?>("SilinmeZamani").HasColumnType("TEXT");
            e.HasIndex("AlisId", "Silindi");
        });
    }
}
