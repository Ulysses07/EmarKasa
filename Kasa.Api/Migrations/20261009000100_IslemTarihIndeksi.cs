using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>İmleçli gider listesi ve aylık raporun tarih aralığı için sıralı dizin; finans verisi değişmez.</summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class IslemTarihIndeksi : Migration
{
    public const string Kimlik = "20261009000100_IslemTarihIndeksi";

    protected override void Up(MigrationBuilder m) => m.CreateIndex(
        name: "IX_Islemler_Tarih_Id", table: "Islemler", columns: ["Tarih", "Id"]);

    protected override void Down(MigrationBuilder m) => m.DropIndex(
        name: "IX_Islemler_Tarih_Id", table: "Islemler");

    protected override void BuildTargetModel(ModelBuilder b) => IslemTarihIndeksiSchemaModel.Build(b);
}
