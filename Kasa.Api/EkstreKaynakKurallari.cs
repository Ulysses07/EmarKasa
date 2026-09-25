using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Kasa.Api;

internal static class EkstreKaynakKurallari
{
    internal static void Dogrula(KasaDbContext db, IReadOnlyList<EntityEntry> entries)
    {
        foreach (var e in entries)
        {
            if (e.Entity is KanalEntity channel && e.State == EntityState.Deleted && db.EkstreKayitlar.AsNoTracking().AsEnumerable()
                .Any(k => FinansTakipServisi.Read<TakipKanalPayi>(k.DagilimJson).Any(p => p.KanalId == channel.Id)))
                throw new KilitliDonemException("Ekstre geçmişinde kullanılan kanal silinemez; pasife alınabilir.");
            if (e.Entity is AlisOdemeEntity purchase && db.EkstreKayitlar.Any(k => k.IslemId == purchase.IslemId))
                throw new KilitliDonemException("Ekstreden alınan gider başka bir alışa bağlanamaz. PDF İçe Aktarma bölümünden düzeltin.");
            if (db.EkstreDegisikligi) continue;
            bool blocked = e.Entity switch
            {
                IslemEntity expense when e.State != EntityState.Added => db.EkstreKayitlar.Any(k => k.IslemId == expense.Id),
                TakipHarcamaEntity charge when e.State != EntityState.Added => db.EkstreKayitlar.Any(k => k.KartHarcamaId == charge.Id),
                TakipKartOdemeEntity payment when e.State == EntityState.Deleted || e.State == EntityState.Modified && e.Property(nameof(payment.Iptal)).IsModified
                    => db.EkstreKayitlar.Any(k => k.KartOdemeId == payment.Id),
                EkstreKayitEntity => true,
                _ => false
            };
            if (blocked) throw new KilitliDonemException("Ekstreden alınan kaydı PDF İçe Aktarma bölümünden gerekçeyle iptal edip yeniden işleyin.");
        }
    }
}
