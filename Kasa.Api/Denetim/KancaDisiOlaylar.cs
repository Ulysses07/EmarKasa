using Kasa.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kasa.Api.Denetim;

/// <summary>
/// SaveChanges kancasından geçmeyen ya da kancanın yakaladığından daha anlamlı yazılması gereken olaylar. Hepsi çağıranın
/// açık transaction'ında yazılır (ana işlemle birlikte kalıcı olur ya da geri alınır).
/// </summary>
internal static class KancaDisiOlaylar
{
    /// <summary>Ham SQL gelir upsert'i (PUT /api/gelenler): önceki satır yoksa ekleme, varsa yalnız değişen alanlarla
    /// değişiklik; hiçbir alan değişmediyse olay yazılmaz.</summary>
    internal static void GelenUpsert(KasaDbContext db, GelenEntity? onceki, GelenEntity yeni)
    {
        var yeniAlanlar = Alanlar(yeni);
        Dictionary<string, object?>? eski = null;
        if (onceki is not null)
        {
            var oncekiAlanlar = Alanlar(onceki);
            var degisen = yeniAlanlar.Keys.Where(k => !Equals(oncekiAlanlar[k], yeniAlanlar[k])).ToList();
            if (degisen.Count == 0)
                return;
            eski = degisen.ToDictionary(k => k, k => oncekiAlanlar[k]);
            yeniAlanlar = degisen.ToDictionary(k => k, k => yeniAlanlar[k]);
        }
        var pencere = DenetimKilitPenceresi.Oku(db);
        DenetimYazici.Yaz(db, new DenetimOlayi(onceki is null ? "Ekle" : "Degistir", "Gelen", yeni.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DenetimYazici.Json(eski), DenetimYazici.Json(yeniAlanlar), KilitAcmaOlayiId: pencere.Bul(yeni.DonemStart) ?? (onceki is null ? null : pencere.Bul(onceki.DonemStart))));
    }

    /// <summary>
    /// Ham SQL yazma yolu için ertelenmiş (DEFERRED) transaction: yazma kilidini BEGIN değil ilk yazma ifadesi alır, yani
    /// tek ifadelik upsert autocommit'teki eşzamanlılığını korur (eşzamanlı istekler aynı upsert noktasına kadar ilerler,
    /// SQLite ifadeyi sıraya koyar). Olay aynı transaction'da yazılır; commit edilmeden bırakılırsa ikisi de geri alınır.
    /// Önceki değer upsert'ten hemen önce, transaction dışında okunur: aynı dönem/kanal gelirine milisaniyeler içinde
    /// eşzamanlı iki giriş gelirse ikincinin olayındaki önceki değer, araya giren girişten önceki değer olabilir (iki
    /// değişiklik de ayrı olay olarak kalır).
    /// </summary>
    internal static ErteliIslem ErteliTransaction(KasaDbContext db) => new(db);

    internal sealed class ErteliIslem : IDisposable
    {
        private readonly KasaDbContext _db;
        private readonly SqliteTransaction _transaction;
        private readonly IDbContextTransaction _ef;

        internal ErteliIslem(KasaDbContext db)
        {
            _db = db;
            db.Database.OpenConnection();
            try
            {
                _transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
                // EF'in sarmalayıcısı dış transaction'ın sahibi değildir: ham SQL ve olay yazımı bu transaction'a katılır.
                _ef = db.Database.UseTransaction(_transaction)!;
            }
            catch
            {
                db.Database.CloseConnection();
                throw;
            }
        }

        public void Commit() => _transaction.Commit();

        public void Dispose()
        {
            try
            { _ef.Dispose(); _transaction.Dispose(); }
            finally { _db.Database.CloseConnection(); }
        }
    }

    private static Dictionary<string, object?> Alanlar(GelenEntity g) => new()
    {
        [nameof(GelenEntity.Id)] = g.Id,
        [nameof(GelenEntity.DonemStart)] = g.DonemStart,
        [nameof(GelenEntity.Kanal)] = g.Kanal,
        [nameof(GelenEntity.KanalId)] = g.KanalId,
        [nameof(GelenEntity.TutarTl)] = g.TutarTl,
        [nameof(GelenEntity.EskiYinelenenGrup)] = g.EskiYinelenenGrup,
    };

    /// <summary>
    /// Ay kilidi açma/kapatma olayı (kilit olayı kaydedildikten sonra). Açma, açtığı pencerenin kimliğini (kilit olayının
    /// Id'si) taşır; o pencereye düşen sonraki değişikliklerin olayları aynı kimliği taşır. Kapatma, yeniden kilitlediği
    /// (kapattığı) pencereleri yazar ve en sonuncusunun kimliğini taşır.
    /// </summary>
    internal static void AyKilidi(KasaDbContext db, AyKilidiOlayEntity olay, bool acma, IReadOnlyList<int> kapatilanPencereler, Guid istekId)
    {
        // Aralığın başı yoksa (kilit tamamen kalktı ya da ilk kez kuruldu) takip başlangıcından itibaren demektir.
        var yeni = new Dictionary<string, object?> { ["KilitliSonTarih"] = olay.YeniSonTarih, ["KilitOlayiId"] = olay.Id };
        if (acma)
            yeni["AcilanAralik"] = new { Baslangic = olay.YeniSonTarih?.AddDays(1), Bitis = olay.OncekiSonTarih };
        else
        {
            yeni["KilitlenenAralik"] = new { Baslangic = olay.OncekiSonTarih?.AddDays(1), Bitis = olay.YeniSonTarih };
            yeni["KapatilanPencereler"] = kapatilanPencereler;
        }
        DenetimYazici.Yaz(db, new DenetimOlayi(acma ? "KilitAc" : "KilitKapat", "AyKilidi", "1",
            DenetimYazici.Json(new { KilitliSonTarih = olay.OncekiSonTarih }), DenetimYazici.Json(yeni),
            olay.Aciklama, acma ? olay.Id : kapatilanPencereler.Count == 0 ? null : kapatilanPencereler[^1], IstekId: istekId));
    }
}
