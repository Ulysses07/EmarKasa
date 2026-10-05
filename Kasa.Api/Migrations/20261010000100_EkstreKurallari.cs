using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
[Migration("20261010000100_EkstreKurallari")]
public sealed class EkstreKurallari : Migration
{
    protected override void Up(MigrationBuilder m) => m.CreateTable("EkstreKurallar", columns: t => new
    {
        Id = t.Column<int>("INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
        Surum = t.Column<int>("INTEGER", nullable: false),
        Ad = t.Column<string>("TEXT", nullable: false),
        Kaynak = t.Column<string>("TEXT", nullable: false),
        Banka = t.Column<string>("TEXT", nullable: true),
        AciklamaIcerir = t.Column<string>("TEXT", nullable: false),
        Yon = t.Column<string>("TEXT", nullable: true),
        IslemTuru = t.Column<string>("TEXT", nullable: false),
        DagilimTuru = t.Column<string>("TEXT", nullable: false),
        KanalIdsJson = t.Column<string>("TEXT", nullable: false),
        Aktif = t.Column<bool>("INTEGER", nullable: false)
    }, constraints: t => t.PrimaryKey("PK_EkstreKurallar", x => x.Id));
    protected override void Down(MigrationBuilder m) => m.DropTable("EkstreKurallar");
    protected override void BuildTargetModel(ModelBuilder b) => EkstreKurallariSchemaModel.Build(b);
}
