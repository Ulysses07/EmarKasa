using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Kart takibi düzeltmeleri (finance-2, finance-8, gap-coklu-giris-cift-sayim-mutabakat-3). Yalnız iki yeni tablo kurar;
/// mevcut takip tablolarının hiçbir satırı ve sütunu değişmez, raporlar birebir aynı kalır:
/// <list type="bullet">
/// <item>TakipIadeHesaplari: yeni kart iadesinin iade anındaki ödenmiş tutarı ve devir iadesinin kasada önceden sayılan
/// tutardan düşen kısmı. Kaydı olmayan (bu sürümden önceki) iade dondurulmuş kanal payıyla hesaplanmaya devam eder.</item>
/// <item>TakipAvansTahsisleri: kilitli döneme düşen kart avansını kilit sonrası tarihte dağıtan ödeme kaydının kaynağı.</item>
/// </list>
/// Eski iadelerin hesap kaydı veri adımıyla yazılır (<see cref="Kasa.Api.FinansTakipServisi.IadeHesabiTohumu"/>): kaynak
/// payından aynı kuralla türetilen pay dondurulmuş payla birebir aynıysa; tutmayan iade eski kuralda kalır. Göç öncesi
/// otomatik yedek başlatıcıda alınır.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration(Kimlik)]
public sealed class KartTakipDuzeltmeleri : Migration
{
    public const string Kimlik = "20261001000200_KartTakipDuzeltmeleri";

    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE "TakipIadeHesaplari" (
            "HarcamaId" INTEGER NOT NULL CONSTRAINT "PK_TakipIadeHesaplari" PRIMARY KEY,
            "IadeAnindaOdenen" TEXT NOT NULL, "KasadaSayilanDuzeltme" TEXT NOT NULL,
            CONSTRAINT "FK_TakipIadeHesaplari_TakipHarcamalar_HarcamaId" FOREIGN KEY ("HarcamaId") REFERENCES "TakipHarcamalar" ("Id") ON DELETE RESTRICT);
        CREATE TABLE "TakipAvansTahsisleri" (
            "OdemeId" INTEGER NOT NULL CONSTRAINT "PK_TakipAvansTahsisleri" PRIMARY KEY,
            "KaynakOdemeId" INTEGER NOT NULL,
            CONSTRAINT "FK_TakipAvansTahsisleri_TakipKartOdemeler_OdemeId" FOREIGN KEY ("OdemeId") REFERENCES "TakipKartOdemeler" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_TakipAvansTahsisleri_TakipKartOdemeler_KaynakOdemeId" FOREIGN KEY ("KaynakOdemeId") REFERENCES "TakipKartOdemeler" ("Id") ON DELETE RESTRICT);
        CREATE INDEX "IX_TakipAvansTahsisleri_KaynakOdemeId" ON "TakipAvansTahsisleri" ("KaynakOdemeId");
        """);
    // Tabloları düşürmek iade paylarının ve kilitli avans dağıtımının kaynağını siler; geri dönüş doğrulanmış yedekle yapılır.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Kart iadesi ve avans dağıtımı kayıtları silinemez; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => KartTakipDuzeltmeleriSchemaModel.Build(b);
}
