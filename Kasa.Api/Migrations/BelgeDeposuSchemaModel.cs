using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class BelgeDeposuSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        BelgeDeposuHazirlikSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.BelgeEntity", e =>
        {
            e.Ignore("Icerik");
            // Sütun ALTER ile boş olabilir eklendi; zorunluluğu tetikleyici uygular.
            e.Property<string>("IcerikOzeti").IsRequired().HasColumnType("TEXT");
            e.HasIndex("IcerikOzeti");
        });
        b.Entity("Kasa.Api.Data.EkstreBelgeEntity", e => e.Ignore("Dosya"));
    }
}
