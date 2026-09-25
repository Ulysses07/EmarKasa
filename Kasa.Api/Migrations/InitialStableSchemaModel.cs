using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kasa.Api.Migrations;

/// <summary>İlk migration'ın sabit hedef modeli. Yeni şema değişiklikleri bu dosyayı
/// değiştirmek yerine yeni migration ve snapshot oluşturmalıdır.</summary>
internal static class InitialStableSchemaModel
{
    internal static void Build(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", StableSchemaDefinition.ProductVersion);
        Entity(modelBuilder, "KanalEntity", "Kanallar", b =>
        {
            Text(b, "Ad").UseCollation("NOCASE");
            b.Property<bool>("Aktif").HasColumnType("INTEGER");
            b.Property<int>("Sira").HasColumnType("INTEGER");
            Money(b, "AcilisDevri");
            b.HasIndex("Ad").IsUnique();
        });
        Entity(modelBuilder, "CariEntity", "Cariler", b =>
        {
            Text(b, "Ad");
            b.Property<bool>("Aktif").HasColumnType("INTEGER");
        });
        Entity(modelBuilder, "AyarEntity", "Ayarlar", b =>
        {
            Date(b, "TakipBaslangic");
            Money(b, "KasaAcilisDevri");
            b.Property<string>("IzleyiciSifreHash").HasColumnType("TEXT");
        });
        Entity(modelBuilder, "KrediKartiEntity", "KrediKartlari", b =>
        {
            Text(b, "Ad"); Date(b, "KesimTarihi"); Date(b, "SonOdemeTarihi");
            Money(b, "Limit"); Money(b, "Borc");
        });
        Entity(modelBuilder, "IslemEntity", "Islemler", b =>
        {
            Date(b, "Tarih"); Text(b, "Cari"); Money(b, "TutarTl"); Text(b, "Kanal");
            b.Property<int>("Tip").HasColumnType("INTEGER");
            b.Property<string>("Not").HasColumnType("TEXT");
            b.Property<int?>("KrediKartiId").HasColumnType("INTEGER");
            b.Property<int?>("KanalId").HasColumnType("INTEGER");
            b.HasIndex("KrediKartiId"); b.HasIndex("KanalId");
        });
        Entity(modelBuilder, "GelenEntity", "Gelenler", b =>
        {
            Date(b, "DonemStart"); Text(b, "Kanal").UseCollation("NOCASE"); Money(b, "TutarTl");
            b.Property<int?>("KanalId").HasColumnType("INTEGER");
            b.HasIndex("KanalId");
            b.HasIndex("DonemStart", "KanalId").IsUnique();
            b.HasIndex("DonemStart", "Kanal").IsUnique();
        });
        Entity(modelBuilder, "KartOdemeEntity", "KartOdemeler", b =>
        {
            b.Property<int>("KrediKartiId").HasColumnType("INTEGER");
            Date(b, "Tarih"); Money(b, "Tutar");
            b.Property<string>("Not").HasColumnType("TEXT");
            b.HasIndex("KrediKartiId");
        });
        Entity(modelBuilder, "KrediEntity", "Krediler", b =>
        {
            Text(b, "Ad"); Money(b, "CekilenTutar"); Date(b, "CekimTarihi");
            b.Property<int>("TaksitSayisi").HasColumnType("INTEGER");
            Money(b, "AylikOdeme");
            b.Property<int>("OdemeGunu").HasColumnType("INTEGER");
            Text(b, "Kanal");
            b.Property<int?>("KanalId").HasColumnType("INTEGER");
            b.HasIndex("KanalId");
        });

        foreach (var entity in new[] { "IslemEntity", "GelenEntity", "KrediEntity" })
            modelBuilder.Entity("Kasa.Api.Data." + entity, b =>
            {
                b.HasOne("Kasa.Api.Data.KanalEntity", "KanalKaydi").WithMany().HasForeignKey("KanalId").OnDelete(DeleteBehavior.Restrict);
                b.Navigation("KanalKaydi");
            });
        modelBuilder.Entity("Kasa.Api.Data.IslemEntity", b =>
            b.HasOne("Kasa.Api.Data.KrediKartiEntity", null).WithMany().HasForeignKey("KrediKartiId").OnDelete(DeleteBehavior.SetNull));
        modelBuilder.Entity("Kasa.Api.Data.KartOdemeEntity", b =>
            b.HasOne("Kasa.Api.Data.KrediKartiEntity", null).WithMany().HasForeignKey("KrediKartiId").OnDelete(DeleteBehavior.Cascade).IsRequired());
    }

    private static void Entity(ModelBuilder modelBuilder, string entity, string table, Action<EntityTypeBuilder> configure)
    {
        modelBuilder.Entity("Kasa.Api.Data." + entity, b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.HasKey("Id");
            b.ToTable(table);
            configure(b);
        });
    }

    private static PropertyBuilder<string> Text(EntityTypeBuilder b, string name) => b.Property<string>(name).IsRequired().HasColumnType("TEXT");
    private static void Money(EntityTypeBuilder b, string name) => b.Property<decimal>(name).HasColumnType("TEXT");
    private static void Date(EntityTypeBuilder b, string name) => b.Property<DateOnly>(name).HasColumnType("TEXT");
}
