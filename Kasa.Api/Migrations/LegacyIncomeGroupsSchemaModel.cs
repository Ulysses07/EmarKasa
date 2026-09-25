using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

internal static class LegacyIncomeGroupsSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        OperationsSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.GelenEntity", e =>
        {
            e.Property<bool>("EskiYinelenenGrup").ValueGeneratedOnAdd().HasColumnType("INTEGER").HasDefaultValue(false);
            e.HasIndex("DonemStart", "KanalId").IsUnique().HasFilter("\"EskiYinelenenGrup\" = 0");
            e.HasIndex("DonemStart", "Kanal").IsUnique().HasFilter("\"EskiYinelenenGrup\" = 0");
        });
    }
}
