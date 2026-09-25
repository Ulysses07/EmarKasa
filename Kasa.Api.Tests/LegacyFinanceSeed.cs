using Kasa.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

// Legacy cards and loans can no longer be created through the public API. These
// fixtures represent records already present before the new tracking contract.
internal static class LegacyFinanceSeed
{
    internal static KrediKartiEntity Kart(KasaWebFactory factory, KrediKartiYazDto dto) => Kaydet(factory,
        new KrediKartiEntity { Ad = dto.Ad, KesimTarihi = dto.KesimTarihi,
            SonOdemeTarihi = dto.SonOdemeTarihi, Limit = dto.Limit, Borc = dto.Borc });

    internal static KrediEntity Kredi(KasaWebFactory factory, KrediYazDto dto) => Kaydet(factory,
        new KrediEntity { Ad = dto.Ad, CekilenTutar = dto.CekilenTutar, CekimTarihi = dto.CekimTarihi,
            TaksitSayisi = dto.TaksitSayisi, AylikOdeme = dto.AylikOdeme, OdemeGunu = dto.OdemeGunu,
            Kanal = dto.Kanal, GerceklesmeTakibi = false });

    internal static T Kaydet<T>(KasaWebFactory factory, T entity) where T : class
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        if (entity is KrediEntity loan && loan.Kanal != Kasa.Core.Kanallar.Ortak)
            loan.KanalId = db.Kanallar.Single(k => k.Ad == loan.Kanal).Id;
        db.Add(entity); db.SaveChanges();
        return entity;
    }
}
