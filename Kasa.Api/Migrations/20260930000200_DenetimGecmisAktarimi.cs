using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kasa.Api.Migrations;

/// <summary>
/// Denetim izinden önceki sürümün sakladığı gerekçe ve önceki durumları merkezi denetim izine aktarır; yoksa bu bilgiye
/// yalnız sunucuda sqlite3 ile ulaşılabiliyordu (gap-denetim-izi-gozlemlenebilirlik-5):
/// <list type="bullet">
/// <item>İptal edilmiş aylık gider ödemeleri (AylikGiderOdemeler.IptalAciklamasi) ve ekstre satırları
/// (EkstreKayitlar.IptalAciklamasi): satırın bugünkü bütün alanları YeniJson'da, iptal açıklaması Gerekce'de.</item>
/// <item>Alış ödemesi düzeltme/iptalinin önceki durumu (FinansIstekler.OncekiJson): alışın önceki hâli OncekiJson'da,
/// açıklama Gerekce'de, isteğin türü YeniJson'da, istek kimliği IstekId'de; varlık alış (SonucId).</item>
/// </list>
/// Olay türü 'GecmisKayit', aktör 'sistem'dir; IP, iz ve kilit penceresi yoktur. Zaman aktarım anıdır: eski sürüm
/// değişikliğin zamanını ve yapanını tutmuyordu. Yalnız DenetimOlaylari'na ekler; kaynak satırların hiçbiri değişmez, bu
/// yüzden raporlar birebir aynı kalır. Göç öncesi otomatik yedek başlatıcıda alınır. Kaydı zaten denetim izinde olan iptal
/// (YeniJson'u Iptal=true olan olay) ya da istek (aynı IstekId'li olay) yeniden aktarılmaz: olay tablosu kurulduktan sonra
/// yazılanlar kancanın kendi olaylarıyla izdedir. Tutar metni geçerli bir JSON sayısıysa sayı olarak yazılır.
/// </summary>
[DbContext(typeof(KasaDbContext))]
[Migration("20260930000200_DenetimGecmisAktarimi")]
public sealed class DenetimGecmisAktarimi : Migration
{
    private const string Simdi = "CAST(ROUND((julianday('now') - 2440587.5) * 86400000.0) AS INTEGER)";

    protected override void Up(MigrationBuilder m) => m.Sql($$"""
        INSERT INTO "DenetimOlaylari" ("ZamanUtc", "AktorRol", "Tur", "Varlik", "VarlikId", "YeniJson", "Gerekce")
        SELECT {{Simdi}}, 'sistem', 'GecmisKayit', 'AylikGiderOdeme', CAST(p."Id" AS TEXT),
          json_object('Id', p."Id", 'SablonId', p."SablonId", 'RevizyonId', p."RevizyonId", 'Ay', p."Ay", 'Tarih', p."Tarih",
            'Tutar', CASE WHEN json_valid(p."Tutar") THEN json(p."Tutar") ELSE p."Tutar" END, 'IslemId', p."IslemId",
            'Iptal', json('true'), 'IptalAciklamasi', p."IptalAciklamasi"),
          p."IptalAciklamasi"
        FROM "AylikGiderOdemeler" p
        WHERE p."Iptal" = 1 AND NOT EXISTS (SELECT 1 FROM "DenetimOlaylari" d WHERE d."Varlik" = 'AylikGiderOdeme'
          AND d."VarlikId" = CAST(p."Id" AS TEXT) AND CASE WHEN json_valid(d."YeniJson") THEN json_extract(d."YeniJson", '$.Iptal') END = 1)
        ORDER BY p."Id";

        INSERT INTO "DenetimOlaylari" ("ZamanUtc", "AktorRol", "Tur", "Varlik", "VarlikId", "YeniJson", "Gerekce")
        SELECT {{Simdi}}, 'sistem', 'GecmisKayit', 'EkstreKayit', CAST(k."Id" AS TEXT),
          json_object('Id', k."Id", 'BelgeId', k."BelgeId", 'SatirNo', k."SatirNo", 'Tarih', k."Tarih", 'Aciklama', k."Aciklama",
            'Tutar', CASE WHEN json_valid(k."Tutar") THEN json(k."Tutar") ELSE k."Tutar" END, 'IslemTuru', k."IslemTuru",
            'DagilimTuru', k."DagilimTuru", 'DagilimJson', k."DagilimJson", 'KrediKartiId', k."KrediKartiId", 'IslemId', k."IslemId",
            'KartHarcamaId', k."KartHarcamaId", 'KartOdemeId', k."KartOdemeId", 'Iptal', json('true'), 'IptalAciklamasi', k."IptalAciklamasi"),
          k."IptalAciklamasi"
        FROM "EkstreKayitlar" k
        WHERE k."Iptal" = 1 AND NOT EXISTS (SELECT 1 FROM "DenetimOlaylari" d WHERE d."Varlik" = 'EkstreKayit'
          AND d."VarlikId" = CAST(k."Id" AS TEXT) AND CASE WHEN json_valid(d."YeniJson") THEN json_extract(d."YeniJson", '$.Iptal') END = 1)
        ORDER BY k."Id";

        INSERT INTO "DenetimOlaylari" ("ZamanUtc", "AktorRol", "Tur", "Varlik", "VarlikId", "OncekiJson", "YeniJson", "Gerekce", "IstekId")
        SELECT {{Simdi}}, 'sistem', 'GecmisKayit', CASE WHEN f."Tur" IN ('OdemeDuzelt', 'OdemeIptal') THEN 'Alis' ELSE 'FinansIstek' END,
          CAST(f."SonucId" AS TEXT),
          CASE WHEN json_valid(f."OncekiJson") THEN COALESCE(json_extract(f."OncekiJson", '$.alis'), f."OncekiJson") ELSE f."OncekiJson" END,
          json_object('IstekTuru', f."Tur"),
          CASE WHEN json_valid(f."OncekiJson") THEN json_extract(f."OncekiJson", '$.aciklama') END,
          f."IstekId"
        FROM "FinansIstekler" f
        WHERE f."OncekiJson" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM "DenetimOlaylari" d WHERE d."IstekId" = f."IstekId")
        ORDER BY f."Id";
        """);

    // Aktarılan olaylar silinemez (TR_DenetimOlaylari_Silinemez); geri dönüş doğrulanmış yedekle yapılır.
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Denetim olayları silinemez; otomatik geri alma yerine doğrulanmış yedek kullanın.");
    protected override void BuildTargetModel(ModelBuilder b) => DenetimOlaylariSchemaModel.Build(b);
}
