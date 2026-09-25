using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Migrations;

internal static class StatementImportsSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        CashControlsSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.EkstreBelgeEntity", e =>
        {
            e.ToTable("EkstreBelgeler"); e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER"); e.HasKey("Id");
            e.Property<int>("Surum").HasColumnType("INTEGER").IsConcurrencyToken();
            e.Property<long>("Yuklendi").HasColumnType("INTEGER"); e.Property<int?>("KartId").HasColumnType("INTEGER");
            e.Property<byte[]>("Dosya").IsRequired().HasColumnType("BLOB");
            foreach (var p in new[] { "Kaynak", "Banka", "HesapAdi", "DosyaAdi", "DosyaOzeti", "SatirlarJson", "UyarilarJson" }) e.Property<string>(p).IsRequired().HasColumnType("TEXT");
            e.HasIndex("DosyaOzeti").IsUnique(); e.HasIndex("KartId");
            e.HasOne("Kasa.Api.Data.KrediKartiEntity", null).WithMany().HasForeignKey("KartId").OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity("Kasa.Api.Data.EkstreKayitEntity", e =>
        {
            e.ToTable("EkstreKayitlar"); e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER"); e.HasKey("Id");
            foreach (var p in new[] { "BelgeId", "SatirNo" }) e.Property<int>(p).HasColumnType("INTEGER");
            foreach (var p in new[] { "KrediKartiId", "IslemId", "KartHarcamaId", "KartOdemeId" }) e.Property<int?>(p).HasColumnType("INTEGER");
            foreach (var p in new[] { "Aciklama", "IslemTuru", "DagilimTuru", "DagilimJson" }) e.Property<string>(p).IsRequired().HasColumnType("TEXT");
            e.Property<string>("IptalAciklamasi").HasColumnType("TEXT"); e.Property<DateOnly>("Tarih").HasColumnType("TEXT");
            e.Property<decimal>("Tutar").HasColumnType("TEXT"); e.Property<bool>("Iptal").HasColumnType("INTEGER");
            e.HasIndex("BelgeId", "SatirNo").IsUnique().HasFilter("\"Iptal\" = 0"); e.HasIndex("KrediKartiId");
            foreach (var p in new[] { "IslemId", "KartHarcamaId", "KartOdemeId" }) e.HasIndex(p).IsUnique();
            e.HasOne("Kasa.Api.Data.EkstreBelgeEntity", null).WithMany().HasForeignKey("BelgeId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            e.HasOne("Kasa.Api.Data.KrediKartiEntity", null).WithMany().HasForeignKey("KrediKartiId").OnDelete(DeleteBehavior.Restrict);
            e.HasOne("Kasa.Api.Data.IslemEntity", null).WithMany().HasForeignKey("IslemId").OnDelete(DeleteBehavior.SetNull);
            e.HasOne("Kasa.Api.Data.TakipHarcamaEntity", null).WithMany().HasForeignKey("KartHarcamaId").OnDelete(DeleteBehavior.Restrict);
            e.HasOne("Kasa.Api.Data.TakipKartOdemeEntity", null).WithMany().HasForeignKey("KartOdemeId").OnDelete(DeleteBehavior.Restrict);
        });
    }
}
