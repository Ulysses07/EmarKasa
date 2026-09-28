using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kasa.Api.Migrations;

[DbContext(typeof(KasaDbContext))]
public sealed class KasaDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => KasaKontrolFiligraniSchemaModel.Build(modelBuilder);
}
