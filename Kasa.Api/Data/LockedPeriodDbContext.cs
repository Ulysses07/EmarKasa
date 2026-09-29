using Kasa.Api.Denetim;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Data;

public partial class KasaDbContext
{
    // Ortak kümesini değiştiren kanal değişikliğinden önce tamamlanmış ayların kanal kümesi aynı transaction'da dondurulur
    // (AyKanalKumesi): kilit kuralları ve kayıt, dondurulmuş kümeyi görür; kayıt geri alınırsa dondurma da kalmaz.
    // Kilit kuralları geçen değişiklikler kaydedilir, ardından aynı transaction'da denetim olayları yazılır: olay yazılamazsa
    // değişiklik de geri alınır, değişiklik geri alınırsa olay da kalmaz. Eklenen kaydın anahtarı kayıttan sonra okunur.
    // İzleyici değişiklikleri olaylar yazılıp transaction commit edildikten sonra kabul eder: olay yazımı ya da commit
    // başarısız olursa eklenen/değişen/silinen kayıtlar bekler (veritabanıyla tutarlı) ve aynı bağlamla yeniden kaydedilebilir.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ChangeTracker.DetectChanges();
        if (!ChangeTracker.HasChanges()) return base.SaveChanges(acceptAllChangesOnSuccess);
        int result;
        using (var transaction = Database.CurrentTransaction is null ? Database.BeginTransaction() : null)
        {
            AyKanalKumesi.KanalDegisikligindenOnce(this);
            AyKilidiKurallari.Dogrula(this);
            var yakalanan = DenetimYakalayici.Yakala(this);
            CekirdekSurumleriniArtir();
            result = base.SaveChanges(acceptAllChangesOnSuccess: false);
            DenetimYakalayici.Yaz(this, yakalanan);
            transaction?.Commit();
        }
        if (acceptAllChangesOnSuccess) ChangeTracker.AcceptAllChanges();
        return result;
    }
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        if (!ChangeTracker.HasChanges()) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        int result;
        await using (var transaction = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null)
        {
            AyKanalKumesi.KanalDegisikligindenOnce(this);
            AyKilidiKurallari.Dogrula(this);
            var yakalanan = DenetimYakalayici.Yakala(this);
            CekirdekSurumleriniArtir();
            result = await base.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken);
            DenetimYakalayici.Yaz(this, yakalanan);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        if (acceptAllChangesOnSuccess) ChangeTracker.AcceptAllChanges();
        return result;
    }

    /// <summary>
    /// contract-6: değişen gider, gelir, kanal ve ayar kaydının sürümü, kayıt anındaki (okunan) sürümün bir fazlası olur. Uç
    /// dışındaki dolaylı yazımlar da (alış ödemesi düzeltmesi ve alıştan ayırma, kanal adı değişikliğinin etiket senkronu, kart silinince
    /// bağın kopması...) sürümü artırır: o kaydı önceden okumuş istemcinin düzenlemesi 409 alır. Sürüm eşzamanlılık belirteci
    /// olduğundan UPDATE okunan sürümle koşullanır; arada başka bağlam yazdıysa kayıt DbUpdateConcurrencyException (409) ile durur.
    /// Kilit kuralları ve denetim yakalaması bundan önce çalışır, değişen alan kümesini sürümsüz görür (etiket senkronu yalnız
    /// Kanal metni değişen satırdır; sürüm sayacı olay üretmez). Değeri artırmak yerine "okunan + 1" atanır: başarısız kaydın aynı
    /// bağlamla yeniden denenmesi sürümü ikinci kez artırmaz. Yalnız sürümü değişen ya da yalnız sürümü etkilemeyen alanı (izleyici
    /// şifresi) değişen kayıt artırılmaz. Ham SQL yolu (gelir upsert'ü) sürümü kendisi artırır.
    /// </summary>
    private void CekirdekSurumleriniArtir()
    {
        foreach (var e in ChangeTracker.Entries())
        {
            if (e.State != EntityState.Modified || e.Entity is not (IslemEntity or GelenEntity or KanalEntity or AyarEntity)) continue;
            // Sürüm sütunundan önceki şemanın modeli (göç testlerinin eski sürüm bağlamı) sürümü tanımaz.
            if (e.Metadata.FindProperty(nameof(IslemEntity.Surum)) is null) continue;
            if (!e.Properties.Any(p => p.IsModified && p.Metadata.Name is not (nameof(IslemEntity.Surum) or nameof(AyarEntity.IzleyiciSifreHash)))) continue;
            var surum = e.Property(nameof(IslemEntity.Surum));
            surum.CurrentValue = (int)surum.OriginalValue! + 1;
        }
    }
}
