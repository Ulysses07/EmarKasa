using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class EkstreEslesmesiSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        BelgeDeposuSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.EkstreKayitEntity", e =>
        {
            e.Property<string>("EslesmeTuru").HasColumnType("TEXT");
            e.Property<int?>("EslesmeId").HasColumnType("INTEGER");
            e.HasIndex("EslesmeTuru", "EslesmeId");
        });
    }
}
