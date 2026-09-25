using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration(StableSchemaDefinition.MigrationId)]
public sealed class InitialStableSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in StableSchemaDefinition.Tables) migrationBuilder.Sql(table.Create());
        foreach (var (_, sql) in StableSchemaDefinition.Indexes) migrationBuilder.Sql(sql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var name in new[] { "KartOdemeler", "Islemler", "Gelenler", "Krediler", "KrediKartlari", "Ayarlar", "Cariler", "Kanallar" })
            migrationBuilder.DropTable(name);
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => InitialStableSchemaModel.Build(modelBuilder);
}
