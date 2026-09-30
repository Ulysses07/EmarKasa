using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public record BenzerKayitSorgu(string Tur, DateOnly Tarih, decimal Tutar, int? KrediKartiId = null, string? Kanal = null, int? AlisId = null);
/// <summary>Benzer kayıt. Yeni alanlar isteğe bağlıdır (eski istemciler yok sayar): <paramref name="KanalEtiketi"/> kaydın
/// kasadan düştüğü kanal(lar) ya da "Genel kasa"/"Ortak"/"Dağılım bekliyor"; <paramref name="EkstreKayitId"/> ve
/// <paramref name="AylikGiderOdemeId"/> kaydın kaynağını gösterir. <paramref name="Kaynak"/>: <see cref="BenzerKayitKaynaklari"/>
/// (KrediTaksidi'nde Id takip taksidinin, EskiKrediTaksidi'nde kredinindir).</summary>
public record BenzerKayitDto(string Kaynak, int Id, DateOnly Tarih, decimal Tutar, string Aciklama, int? KrediKartiId, int? AlisId = null,
    string? KanalEtiketi = null, int? EkstreKayitId = null, int? AylikGiderOdemeId = null);

/// <summary>Benzerlik bir uyarıdır; kayıt oluşturmaz ve meşru ikinci işlemi yasaklamaz. Kural ve kaynaklar
/// <see cref="BenzerKayitServisi"/>'ndedir (ekstre önizlemesi de aynı servisi kullanır).</summary>
public static class BenzerKayitEndpoints
{
    public static WebApplication MapBenzerKayitEndpoints(this WebApplication app)
    {
        // Tutar ve açıklamalar URL/erişim günlüğüne girmesin. POST yalnız okur.
        app.MapPost("/api/islemler/benzerlik", (BenzerKayitSorgu dto, KasaDbContext db) =>
        {
            var v = new GirdiDogrulama();
            v.Kontrol(dto.Tur is BenzerAramaTurleri.Gider or BenzerAramaTurleri.AylikGider or BenzerAramaTurleri.AlisOdeme or BenzerAramaTurleri.KartHarcama or BenzerAramaTurleri.KartOdeme, "tur", "Geçerli bir işlem türü seçin.");
            v.Tarih(dto.Tarih, "tarih");
            v.Para(dto.Tutar, "tutar", negatifOlabilir: true);
            v.Kontrol(dto.KrediKartiId is null || db.KrediKartlari.Any(k => k.Id == dto.KrediKartiId), "krediKartiId", "Kart bulunamadı.");
            v.Kontrol(dto.Tur is not (BenzerAramaTurleri.KartHarcama or BenzerAramaTurleri.KartOdeme) || dto.KrediKartiId is > 0, "krediKartiId", "Kart seçin.");
            v.Kontrol(dto.Tur != BenzerAramaTurleri.AylikGider || dto.KrediKartiId is null, "krediKartiId", "Aylık gider ödemesi kartla kaydedilmez.");
            v.Metin(dto.Kanal, "kanal", zorunlu: dto.Tur == BenzerAramaTurleri.Gider && dto.KrediKartiId is null);
            v.Kontrol(dto.Tur != BenzerAramaTurleri.AlisOdeme || dto.AlisId is > 0, "alisId", "Alış seçin.");
            if (v.Sonuc() is { } error)
                return error;

            // Salt okunur: tutarlı anlık görüntüde çalışır, yazma kilidi almaz ve Sync yapmaz.
            return AlisEndpoints.Oku(db, () =>
            {
                var purchase = dto.Tur == BenzerAramaTurleri.AlisOdeme ? AlisEndpoints.Query(db).AsNoTracking().SingleOrDefault(a => a.Id == dto.AlisId) : null;
                if (dto.Tur == BenzerAramaTurleri.AlisOdeme && purchase is null)
                    return Results.NotFound();
                int? channelId = null;
                if (dto.Tur is BenzerAramaTurleri.Gider or BenzerAramaTurleri.AylikGider && dto.KrediKartiId is null && dto.Kanal is not null && dto.Kanal != KanalEtiketleri.Ortak)
                {
                    channelId = db.Kanallar.Where(k => k.Ad == dto.Kanal).Select(k => (int?)k.Id).SingleOrDefault();
                    if (channelId is null)
                        return Results.ValidationProblem(new Dictionary<string, string[]> { ["kanal"] = ["Kayıtlı bir kanal seçin."] });
                }
                var search = new BenzerAramasi(dto.Tur, dto.Tarih, dto.Tutar, dto.KrediKartiId, Channels(dto, purchase, channelId), purchase?.Id);
                return Results.Ok(new BenzerKayitServisi(db).Bul(search));
            });
        }).RequireAuthorization("Editor");
        return app;
    }

    /// <summary>Sorgunun kanal kümesi; null süzgeç yok demektir. Ortak gider bütün kanallara dağıldığından süzmez; taslak
    /// alışın payı henüz belli olmadığından tahmin yapılmaz. Kartlı sorgu ve kart ödemesi kanala bakmaz.</summary>
    private static HashSet<int>? Channels(BenzerKayitSorgu dto, AlisEntity? purchase, int? channelId)
    {
        if (dto.KrediKartiId is not null)
            return null;
        if (channelId is { } channel)
            return [channel];
        if (purchase?.Durum != AlisDurumlari.Onaylandi)
            return null;
        var shares = AlisHesaplari.KanalPaylari(purchase).Select(p => p.KanalId).ToHashSet();
        return shares.Count == 0 ? null : shares;
    }
}
