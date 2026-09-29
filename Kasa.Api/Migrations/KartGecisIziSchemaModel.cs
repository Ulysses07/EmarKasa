using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class KartGecisIziSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        KartGecisKuraliSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.TakipKartEntity", e =>
        {
            e.Property<string>("GecisAciklamasi").HasColumnType("TEXT");
            e.Property<string>("GecisOzetiJson").HasColumnType("TEXT");
        });
    }
}
