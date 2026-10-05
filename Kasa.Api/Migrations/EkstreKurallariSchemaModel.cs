using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

internal static class EkstreKurallariSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        IslemTarihIndeksiSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.EkstreKuralEntity", e =>
        {
            e.ToTable("EkstreKurallar");
            e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            e.HasKey("Id");
            e.Property<int>("Surum").IsConcurrencyToken().HasColumnType("INTEGER");
            foreach (var name in new[] { "Ad", "Kaynak", "AciklamaIcerir", "IslemTuru", "DagilimTuru", "KanalIdsJson" })
                e.Property<string>(name).IsRequired().HasColumnType("TEXT");
            e.Property<string>("Banka").HasColumnType("TEXT");
            e.Property<string>("Yon").HasColumnType("TEXT");
            e.Property<bool>("Aktif").HasColumnType("INTEGER");
        });
    }
}
