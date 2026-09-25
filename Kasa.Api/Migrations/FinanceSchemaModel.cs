using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kasa.Api.Migrations;

internal static class FinanceSchemaModel
{
    internal static void Build(ModelBuilder b)
    {
        PurchaseSchemaModel.Build(b);
        b.Entity("Kasa.Api.Data.AlisEntity", e =>
        {
            e.Property<int?>("TedarikciId").HasColumnType("INTEGER"); e.Property<DateOnly?>("Vade").HasColumnType("TEXT"); e.HasIndex("TedarikciId");
            e.HasOne("Kasa.Api.Data.CariEntity", "TedarikciKaydi").WithMany().HasForeignKey("TedarikciId").OnDelete(DeleteBehavior.Restrict); e.Navigation("TedarikciKaydi");
        });
        b.Entity("Kasa.Api.Data.AlisKalemEntity", e => { e.Property<decimal?>("Miktar").HasColumnType("TEXT"); e.Property<decimal?>("BirimFiyat").HasColumnType("TEXT"); });
        b.Entity("Kasa.Api.Data.KrediEntity", e => e.Property<bool>("GerceklesmeTakibi").HasColumnType("INTEGER"));
        Entity(b,"HesapEntity","Hesaplar",e =>
        {
            e.Property<int>("Surum").IsConcurrencyToken().HasColumnType("INTEGER"); Text(e,"Ad").UseCollation("NOCASE"); Text(e,"Tur");
            e.Property<DateOnly>("AcilisTarihi").HasColumnType("TEXT"); e.Property<decimal>("AcilisBakiyesi").HasColumnType("TEXT"); e.Property<bool>("Aktif").HasColumnType("INTEGER"); e.HasIndex("Ad").IsUnique();
        });
        Entity(b,"HesapHareketEntity","HesapHareketler",e =>
        {
            e.Property<int>("HesapId").HasColumnType("INTEGER"); e.HasIndex("HesapId");
            foreach(var field in new[]{"IslemId","GelenId","KartOdemeId","KrediId","KanalId"}) { e.Property<int?>(field).HasColumnType("INTEGER"); var index=e.HasIndex(field); if(field!="KanalId") index.IsUnique(); }
            e.Property<DateOnly>("Tarih").HasColumnType("TEXT"); e.Property<decimal>("Tutar").HasColumnType("TEXT"); Text(e,"Aciklama");
        });
        Entity(b,"HesapTransferEntity","HesapTransferler",e =>
        {
            foreach(var field in new[]{"KaynakHesapId","HedefHesapId"}) { e.Property<int>(field).HasColumnType("INTEGER"); e.HasIndex(field); }
            e.Property<DateOnly>("Tarih").HasColumnType("TEXT"); e.Property<decimal>("Tutar").HasColumnType("TEXT"); Text(e,"Aciklama");
        });
        Entity(b,"FinansIstekEntity","FinansIstekler",e => { e.Property<Guid>("IstekId").HasColumnType("TEXT"); Text(e,"Ozet"); Text(e,"Tur"); e.Property<int>("SonucId").HasColumnType("INTEGER"); e.Property<string>("OncekiJson").HasColumnType("TEXT"); e.HasIndex("IstekId").IsUnique(); });
        Entity(b,"KrediTaksitOdemeEntity","KrediTaksitOdemeler",e =>
        {
            foreach(var field in new[]{"KrediId","TaksitNo","IslemId"}) e.Property<int>(field).HasColumnType("INTEGER");
            e.HasIndex("KrediId","TaksitNo").IsUnique(); e.HasIndex("IslemId").IsUnique();
        });
        b.Entity("Kasa.Api.Data.HesapHareketEntity",e =>
        {
            e.HasOne("Kasa.Api.Data.HesapEntity",null).WithMany().HasForeignKey("HesapId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            e.HasOne("Kasa.Api.Data.IslemEntity","Islem").WithOne("HesapHareketi").HasForeignKey("Kasa.Api.Data.HesapHareketEntity","IslemId").OnDelete(DeleteBehavior.Restrict);
            foreach(var item in new[]{("GelenEntity","Gelen","GelenId"),("KartOdemeEntity","KartOdeme","KartOdemeId"),("KrediEntity","Kredi","KrediId"),("KanalEntity","Kanal","KanalId")})
                e.HasOne("Kasa.Api.Data."+item.Item1,item.Item2).WithMany().HasForeignKey(item.Item3).OnDelete(DeleteBehavior.Restrict);
            foreach(var nav in new[]{"Islem","Gelen","KartOdeme","Kredi","Kanal"}) e.Navigation(nav);
        });
        b.Entity("Kasa.Api.Data.IslemEntity",e => e.Navigation("HesapHareketi"));
        b.Entity("Kasa.Api.Data.HesapTransferEntity",e => { foreach(var field in new[]{"KaynakHesapId","HedefHesapId"}) e.HasOne("Kasa.Api.Data.HesapEntity",null).WithMany().HasForeignKey(field).OnDelete(DeleteBehavior.Restrict).IsRequired(); });
        b.Entity("Kasa.Api.Data.KrediTaksitOdemeEntity",e =>
        {
            e.HasOne("Kasa.Api.Data.KrediEntity",null).WithMany().HasForeignKey("KrediId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            e.HasOne("Kasa.Api.Data.IslemEntity","Islem").WithMany().HasForeignKey("IslemId").OnDelete(DeleteBehavior.Restrict).IsRequired(); e.Navigation("Islem");
        });
    }
    private static void Entity(ModelBuilder b,string entity,string table,Action<EntityTypeBuilder> configure) => b.Entity("Kasa.Api.Data."+entity,e => { e.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER"); e.HasKey("Id"); e.ToTable(table); configure(e); });
    private static PropertyBuilder<string> Text(EntityTypeBuilder e,string name)=>e.Property<string>(name).IsRequired().HasColumnType("TEXT");
}
