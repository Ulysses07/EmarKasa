using System.Globalization;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

public static class KasaEsikServisi
{
    public static IReadOnlyList<BildirimTaslagi> Oku(KasaDbContext db, DateOnly today, bool yeniUyariEtkin)
    {
        if (!db.KasaEsikleri.Any(x => x.Etkin || x.AlarmAcik)) return [];
        using var snapshot = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
        foreach (var entry in db.ChangeTracker.Entries<KasaEsikEntity>().ToList()) entry.State = EntityState.Detached;
        var limits = db.KasaEsikleri.Where(x => x.Etkin || x.AlarmAcik).ToList();
        var balances = new HesapServisi(db).Panel().Kanallar.ToDictionary(k => k.KanalId ?? 0);
        var result = new List<BildirimTaslagi>();
        foreach (var limit in limits)
        {
            balances.TryGetValue(limit.KanalId, out var channel);
            if (!limit.Etkin || channel is null || channel.Bakiye >= limit.Tutar)
            {
                limit.AlarmAcik = false; limit.UyariTarihi = null;
                continue;
            }
            if (!limit.AlarmAcik && yeniUyariEtkin)
            {
                limit.AlarmAcik = true; limit.OlaySayisi++; limit.UyariTarihi = today;
            }
            // Aynı düşük bakiye olayı ertesi gün veya sunucu yeniden başlarken tekrarlanmaz.
            if (!yeniUyariEtkin || limit.UyariTarihi != today) continue;
            var culture = CultureInfo.GetCultureInfo("tr-TR");
            result.Add(new($"KasaEsik:{limit.Id}:{limit.OlaySayisi}", "Kanal kasası alt sınırın altında",
                $"{channel.Kanal}: kasa {channel.Bakiye.ToString("N2", culture)} TL, belirlediğin alt sınır {limit.Tutar.ToString("N2", culture)} TL.",
                today, "/#home", "KasaEsik", limit.KanalId));
        }
        db.SaveChanges(); snapshot?.Commit();
        return result;
    }
}
