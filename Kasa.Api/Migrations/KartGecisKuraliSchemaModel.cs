using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

// Migration models are frozen and never call runtime entity configuration.
internal static class KartGecisKuraliSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        StatementImportsSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.TakipKartEntity", e => e.Property<int>("EskiDusumKurali").HasColumnType("INTEGER"));
    }
}
