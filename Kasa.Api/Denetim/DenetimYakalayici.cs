using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Kasa.Api.Denetim;

/// <summary>
/// SaveChanges kancası (<see cref="KasaDbContext"/>): kilit kuralları geçtikten sonra eklenen, değişen ve silinen her
/// kaydın önceki/yeni değerini yakalar; kayıttan sonra aynı transaction'da olay olarak yazar.
/// <list type="bullet">
/// <item>Ekleme: bütün alanlar (anahtar kayıttan sonra okunur). Değiştirme: yalnız değeri gerçekten değişen alanlar iki
/// tarafta. Silme: bütün alanların önceki değeri. Yalnız sürüm sayacı değişen kayıt olay üretmez.</item>
/// <item>Gizli alanlar (parola ve kurtarma özetleri) yazılmaz, yalnız değiştiği görünür; ikili içerik boyutuyla yazılır.</item>
/// <item>Gerekçe: ucun verdiği (<see cref="KasaDbContext.Denetle"/>) ya da aynı kayıtta yazılan iptal açıklaması.
/// İstek kimliği: ucun verdiği ya da aynı kayıtta eklenen FinansIstek'inki.</item>
/// <item>Kanal yeniden adlandırılırken geçmiş gider/gelir/kredi satırlarındaki kanal metninin eşitlenmesi ayrı olay
/// üretmez (kanal kimliği aynı; kanalın olayı eski ve yeni adı taşır).</item>
/// <item>Ay kilidi açılışının penceresine düşen değişiklik açılış olayına bağlanır (<see cref="DenetimKilitPenceresi"/>).</item>
/// <item>Veritabanının silmede kendiliğinden yaptığı değişiklikler (ON DELETE SET NULL / CASCADE; izleyiciye yüklenmemiş
/// bağımlılar, ör. iptal edilen alış ödemesinin belgesi): bağı kopan kayıt 'BagKoptu', zincirleme silinen kayıt 'Sil' olayıyla
/// üst kaydın silmesiyle aynı gerekçe ve istek kimliğiyle yazılır (<see cref="VeritabaniEtkileri"/>).</item>
/// <item>Dışarıda: olay ve istek tabloları, bildirim altyapısı, türetilmiş kilitli ay rapor görüntüsü; ay kilidi ve editör
/// güvenliği kendi uçlarında ayrı (daha anlamlı) olay yazar.</item>
/// </list>
/// </summary>
internal static class DenetimYakalayici
{
    private static readonly HashSet<Type> Haric =
    [
        typeof(DenetimOlayEntity), typeof(FinansIstekEntity), typeof(BildirimEntity), typeof(BildirimTeslimEntity),
        typeof(PushAbonelikEntity), typeof(BildirimAyarEntity), typeof(AyRaporAnlikGoruntuEntity), typeof(AyKilidiEntity),
        typeof(AyKilidiOlayEntity), typeof(EditorGuvenlikEntity), typeof(SistemDurumuEntity),
    ];
    /// <summary>Değeri asla yazılmayan alanlar: yalnız değiştiği (ya da dolu olduğu) görünür.</summary>
    private static readonly HashSet<string> Gizli = ["SifreHash", "KurtarmaHash", "IzleyiciSifreHash", "OturumDamgasi"];
    /// <summary>Sürüm sayacı farkta gösterilmez; tek başına değişmesi olay üretmez.</summary>
    private static readonly HashSet<string> Sayac = ["Surum"];
    /// <summary>Türe özgü türetilmiş alanlar (kasa alt sınırı alarm durumu bildirim işinde güncellenir).</summary>
    private static readonly Dictionary<Type, HashSet<string>> Turetilmis = new()
    {
        [typeof(KasaEsikEntity)] = ["AlarmAcik", "OlaySayisi", "UyariTarihi"],
    };
    /// <summary>Tarihsiz ama bütün dönemlerin sonucunu etkileyen varlıklar: kilit penceresi açıksa açılışa bağlanır.</summary>
    private static readonly HashSet<Type> GenelEtkili = [typeof(KanalEntity), typeof(AyarEntity), typeof(KrediKartiEntity)];

    internal sealed class Kayit
    {
        /// <summary>İzleyicideki kayıt; veritabanının kendiliğinden değiştirdiği (izlenmeyen) bağımlıda null.</summary>
        public EntityEntry? Entry { get; init; }
        public required object Nesne { get; init; }
        public required EntityState Durum { get; init; }
        public required string Varlik { get; init; }
        public string Tur { get; set; } = "";
        public string? VarlikId { get; set; }
        public Dictionary<string, object?>? Onceki { get; set; }
        public Dictionary<string, object?>? Yeni { get; set; }
        public List<DateOnly> Tarihler { get; } = [];
        public int? AlisId { get; set; }
        public int? AlisKalemId { get; set; }
    }

    internal sealed record Yakalanan(List<Kayit> Kayitlar, string? Gerekce, Guid? IstekId);

    /// <summary>Kayıttan önce: durumlar ve önceki değerler (kayıt onları sıfırlar). Olay tablosu yoksa null.</summary>
    internal static Yakalanan? Yakala(KasaDbContext db)
    {
        var entries = db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        if (entries.Any(e => e.Entity is DenetimOlayEntity && e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Denetim kaydı değiştirilemez ve silinemez.");
        if (!DenetimYazici.TabloVar(db)) return null;

        // Kanal adı değişince eşitlenen metinler: kanal kimliği → yeni ad.
        var yenidenAdlandirilan = entries.Where(e => e.Entity is KanalEntity && e.State == EntityState.Modified && e.Property(nameof(KanalEntity.Ad)).IsModified
                && !Equals(e.Property(nameof(KanalEntity.Ad)).OriginalValue, e.Property(nameof(KanalEntity.Ad)).CurrentValue))
            .ToDictionary(e => ((KanalEntity)e.Entity).Id, e => ((KanalEntity)e.Entity).Ad);

        string? otomatikGerekce = null;
        Guid? istekId = null;
        var kayitlar = new List<Kayit>();
        foreach (var e in entries)
        {
            if (e.Entity is FinansIstekEntity istek && e.State == EntityState.Added) istekId ??= istek.IstekId;
            if (Haric.Contains(e.Entity.GetType())) continue;
            var yoksay = Turetilmis.GetValueOrDefault(e.Entity.GetType());
            var ozellikler = e.Properties.Where(p => !Sayac.Contains(p.Metadata.Name) && yoksay?.Contains(p.Metadata.Name) != true).ToList();
            var kayit = new Kayit { Entry = e, Nesne = e.Entity, Durum = e.State, Varlik = VarlikAdi(e.Entity.GetType()) };
            switch (e.State)
            {
                case EntityState.Modified:
                    var degisen = ozellikler.Where(p => p.IsModified && !Esit(p.OriginalValue, p.CurrentValue)).ToList();
                    if (degisen.Count == 0) continue;
                    if (yenidenAdlandirilan.Count > 0 && KanalMetniEsitlemesi(e, degisen, yenidenAdlandirilan)) continue;
                    kayit.Onceki = degisen.ToDictionary(p => p.Metadata.Name, p => Deger(p.Metadata, p.OriginalValue));
                    kayit.Yeni = degisen.ToDictionary(p => p.Metadata.Name, p => Deger(p.Metadata, p.CurrentValue));
                    kayit.VarlikId = Anahtar(e, orijinal: true);
                    kayit.Tur = OzelTur(e, degisen) ?? "Degistir";
                    foreach (var p in degisen) Tarih(kayit, p.OriginalValue);
                    foreach (var p in degisen) Tarih(kayit, p.CurrentValue);
                    // Değişen alan tarih değilse (ör. tutar) kaydın kendi tarihi etkilenen dönemi gösterir.
                    foreach (var p in ozellikler) Tarih(kayit, p.CurrentValue);
                    if (otomatikGerekce is null && degisen.FirstOrDefault(p => p.Metadata.Name == "IptalAciklamasi")?.CurrentValue is string iptal && iptal.Length > 0)
                        otomatikGerekce = iptal;
                    break;
                case EntityState.Deleted:
                    kayit.Onceki = ozellikler.ToDictionary(p => p.Metadata.Name, p => Deger(p.Metadata, p.OriginalValue));
                    kayit.VarlikId = Anahtar(e, orijinal: true);
                    kayit.Tur = "Sil";
                    foreach (var p in ozellikler) Tarih(kayit, p.OriginalValue);
                    break;
                default:
                    kayit.Tur = "Ekle";
                    foreach (var p in ozellikler) Tarih(kayit, p.CurrentValue);
                    if (otomatikGerekce is null && ozellikler.FirstOrDefault(p => p.Metadata.Name == "IptalAciklamasi")?.CurrentValue is string eklenenIptal && eklenenIptal.Length > 0)
                        otomatikGerekce = eklenenIptal;
                    break;
            }
            (kayit.AlisId, kayit.AlisKalemId) = AlisBagi(e.Entity);
            kayitlar.Add(kayit);
        }
        if (entries.Any(e => e.State == EntityState.Deleted)) kayitlar.AddRange(VeritabaniEtkileri(db, entries));
        return new(kayitlar, db.DenetimGerekcesi ?? otomatikGerekce, db.DenetimIstekId ?? istekId);
    }

    /// <summary>
    /// Silmenin veritabanında kendiliğinden yaptığı değişiklikler: izleyiciye yüklenmemiş bağımlı kayıtlar EF'e görünmez,
    /// ON DELETE SET NULL bağlarını, CASCADE kayıtları veritabanı değiştirir (ör. iptal edilen alış ödemesine iliştirilmiş belge,
    /// yüklenmeden silinen alışın kalemleri). Silinen her kaydın bu ilişkilerdeki bağımlıları kayıttan önce okunur: bağı kopan
    /// kayıt 'BagKoptu' (önceki/yeni yabancı anahtar), zincirleme silinen kayıt bütün alanlarıyla 'Sil' olur ve onun
    /// bağımlılarına da bakılır. İzleyicideki kayda EF'in kendi kuralı uygulanır (olayı zaten yakalanır), iki kez yazılmaz.
    /// Davranış değişmez: bağı yine veritabanı koparır.
    /// </summary>
    private static List<Kayit> VeritabaniEtkileri(KasaDbContext db, List<EntityEntry> entries)
    {
        var sonuc = new List<Kayit>();
        var gorulen = db.ChangeTracker.Entries().Select(e => $"{e.Metadata.Name}|{Anahtar(e, orijinal: e.State != EntityState.Added)}").ToHashSet(StringComparer.Ordinal);
        List<(IEntityType Tur, Func<IProperty, object?> Oku)> silinen = entries.Where(e => e.State == EntityState.Deleted)
            .Select(e => (e.Metadata, (Func<IProperty, object?>)(p => e.Property(p.Name).OriginalValue))).ToList();
        while (silinen.Count > 0)
        {
            var sonraki = new List<(IEntityType Tur, Func<IProperty, object?> Oku)>();
            foreach (var grup in silinen.Where(s => !Haric.Contains(s.Tur.ClrType)).GroupBy(s => s.Tur))
                foreach (var fk in grup.Key.GetReferencingForeignKeys())
                {
                    if (fk.DeleteBehavior is not (DeleteBehavior.SetNull or DeleteBehavior.Cascade) || fk.Properties.Count != 1
                        || Haric.Contains(fk.DeclaringEntityType.ClrType)) continue;
                    var bagimli = fk.DeclaringEntityType;
                    var yabanci = fk.Properties[0];
                    var anahtarlar = grup.Select(s => s.Oku(fk.PrincipalKey.Properties[0])).Where(v => v is not null).Distinct().ToList();
                    foreach (var nesne in Bagimlilar(db, bagimli, yabanci, anahtarlar))
                    {
                        object? Oku(IProperty p) => p.GetGetter().GetClrValueUsingContainingEntity(nesne);
                        var anahtar = string.Join("|", bagimli.FindPrimaryKey()!.Properties.Select(p => Convert.ToString(Oku(p), System.Globalization.CultureInfo.InvariantCulture)));
                        if (!gorulen.Add($"{bagimli.Name}|{anahtar}")) continue;
                        var ozellikler = bagimli.GetProperties().Where(p => !Sayac.Contains(p.Name) && Turetilmis.GetValueOrDefault(bagimli.ClrType)?.Contains(p.Name) != true).ToList();
                        var kayit = new Kayit { Nesne = nesne, Varlik = VarlikAdi(bagimli.ClrType), VarlikId = anahtar,
                            Durum = fk.DeleteBehavior == DeleteBehavior.Cascade ? EntityState.Deleted : EntityState.Modified };
                        if (kayit.Durum == EntityState.Deleted)
                        {
                            kayit.Tur = "Sil";
                            kayit.Onceki = ozellikler.ToDictionary(p => p.Name, p => Deger(p, Oku(p)));
                            sonraki.Add((bagimli, Oku));
                        }
                        else
                        {
                            kayit.Tur = "BagKoptu";
                            kayit.Onceki = new() { [yabanci.Name] = Oku(yabanci) };
                            kayit.Yeni = new() { [yabanci.Name] = null };
                        }
                        foreach (var p in ozellikler) Tarih(kayit, Oku(p));
                        (kayit.AlisId, kayit.AlisKalemId) = AlisBagi(nesne);
                        sonuc.Add(kayit);
                    }
                }
            silinen = sonraki;
        }
        return sonuc;
    }

    /// <summary>Bağımlı türün, yabancı anahtarı verilen anahtarlardan biri olan kayıtları (izlenmeden, kayıttan önceki hâliyle).</summary>
    private static IEnumerable<object> Bagimlilar(KasaDbContext db, IEntityType bagimli, IProperty yabanci, List<object?> anahtarlar)
    {
        foreach (var parca in anahtarlar.Chunk(500))
        {
            var liste = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(yabanci.ClrType))!;
            foreach (var anahtar in parca) liste.Add(anahtar);
            var sonuc = (List<object>)BagimliSorgusu.MakeGenericMethod(bagimli.ClrType, yabanci.ClrType).Invoke(null, [db, yabanci.Name, liste])!;
            foreach (var nesne in sonuc) yield return nesne;
        }
    }

    private static readonly System.Reflection.MethodInfo BagimliSorgusu =
        typeof(DenetimYakalayici).GetMethod(nameof(BagimliSorgula), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    private static List<object> BagimliSorgula<TVarlik, TAnahtar>(KasaDbContext db, string yabanci, List<TAnahtar> anahtarlar) where TVarlik : class =>
        db.Set<TVarlik>().AsNoTracking().Where(v => anahtarlar.Contains(EF.Property<TAnahtar>(v, yabanci))).Cast<object>().ToList();

    /// <summary>Kayıttan sonra, aynı transaction'da: eklenenlerin anahtarı ve değerleri okunur, olaylar yazılır.</summary>
    internal static void Yaz(KasaDbContext db, Yakalanan? yakalanan)
    {
        if (yakalanan is null || yakalanan.Kayitlar.Count == 0) return;
        foreach (var k in yakalanan.Kayitlar.Where(k => k.Durum == EntityState.Added))
        {
            var entry = k.Entry!;
            var ozellikler = entry.Properties.Where(p => !Sayac.Contains(p.Metadata.Name)
                && Turetilmis.GetValueOrDefault(k.Nesne.GetType())?.Contains(p.Metadata.Name) != true);
            k.Yeni = ozellikler.ToDictionary(p => p.Metadata.Name, p => Deger(p.Metadata, p.CurrentValue));
            k.VarlikId = Anahtar(entry, orijinal: false);
            // Yeni kaydın ve yeni üst kaydın anahtarı kayıttan sonra kesinleşir.
            (k.AlisId, k.AlisKalemId) = AlisBagi(k.Nesne);
        }
        var pencere = PencereGerekli(yakalanan.Kayitlar) ? DenetimKilitPenceresi.Oku(db) : DenetimKilitPenceresi.Bos;
        if (pencere != DenetimKilitPenceresi.Bos) AlisTarihleri(db, yakalanan.Kayitlar);
        DenetimYazici.Yaz(db, yakalanan.Kayitlar.Select(k => new DenetimOlayi(k.Tur, k.Varlik, k.VarlikId,
            DenetimYazici.Json(k.Onceki), DenetimYazici.Json(k.Yeni), yakalanan.Gerekce, Pencere(pencere, k), IstekId: yakalanan.IstekId)).ToList());
    }

    private static bool PencereGerekli(List<Kayit> kayitlar) =>
        kayitlar.Any(k => k.Tarihler.Count > 0 || k.AlisId is not null || k.AlisKalemId is not null || GenelEtkili.Contains(k.Nesne.GetType()));

    private static int? Pencere(DenetimKilitPenceresi pencere, Kayit k)
    {
        if (GenelEtkili.Contains(k.Nesne.GetType())) return pencere.EnSon;
        foreach (var t in k.Tarihler.Distinct().OrderDescending())
            if (pencere.Bul(t) is { } olay) return olay;
        return null;
    }

    // Alış kalemi/dağılımı/durumu, alışın ödemelerinin tarihlerindeki kanal sonuçlarını değiştirir (kilit kuralıyla aynı).
    private static void AlisTarihleri(KasaDbContext db, List<Kayit> kayitlar)
    {
        // Dağılımın kalemi aynı kayıtta silinmiş olabilir: önce izlenen kalemden, yoksa veritabanından okunur.
        var kalemAlisi = kayitlar.Where(k => k.Nesne is AlisKalemEntity).Select(k => (AlisKalemEntity)k.Nesne)
            .GroupBy(k => k.Id).ToDictionary(g => g.Key, g => g.First().AlisId);
        var kalemler = kayitlar.Where(k => k.AlisId is null && k.AlisKalemId is { } id && !kalemAlisi.ContainsKey(id)).Select(k => k.AlisKalemId!.Value).Distinct().ToList();
        if (kalemler.Count > 0)
            foreach (var (id, alis) in db.AlisKalemler.AsNoTracking().Where(k => kalemler.Contains(k.Id)).Select(k => new { k.Id, k.AlisId }).AsEnumerable().Select(k => (k.Id, k.AlisId)))
                kalemAlisi[id] = alis;
        foreach (var k in kayitlar.Where(k => k.AlisId is null && k.AlisKalemId is not null))
            if (kalemAlisi.TryGetValue(k.AlisKalemId!.Value, out var alis)) k.AlisId = alis;
        var alislar = kayitlar.Where(k => k.AlisId is not null).Select(k => k.AlisId!.Value).Distinct().ToList();
        if (alislar.Count == 0) return;
        var tarihler = db.AlisOdemeler.AsNoTracking().Where(o => alislar.Contains(o.AlisId)).Select(o => new { o.AlisId, o.Islem.Tarih }).ToList()
            .GroupBy(o => o.AlisId).ToDictionary(g => g.Key, g => g.Select(o => o.Tarih).ToList());
        foreach (var k in kayitlar.Where(k => k.AlisId is not null))
            if (tarihler.TryGetValue(k.AlisId!.Value, out var t)) k.Tarihler.AddRange(t);
    }

    private static (int? AlisId, int? AlisKalemId) AlisBagi(object varlik) => varlik switch
    {
        AlisEntity a => (a.Id, null),
        AlisKalemEntity k => (k.AlisId, null),
        AlisOdemeEntity o => (o.AlisId, null),
        AlisDagilimEntity d => (null, d.AlisKalemId),
        _ => (null, null),
    };

    private static bool KanalMetniEsitlemesi(EntityEntry e, List<PropertyEntry> degisen, Dictionary<int, string> yenidenAdlandirilan) =>
        e.Entity is IslemEntity or GelenEntity or KrediEntity
        && degisen.All(p => p.Metadata.Name is "Kanal" or "KanalId")
        && e.Property("KanalId").CurrentValue is int kanal && yenidenAdlandirilan.TryGetValue(kanal, out var ad)
        && string.Equals(e.Property("Kanal").CurrentValue as string, ad, StringComparison.Ordinal);

    // Güvenlik açısından anlamlı değişiklikler kendi türüyle yazılır (değerleri yine gizlidir).
    private static string? OzelTur(EntityEntry e, List<PropertyEntry> degisen) => e.Entity switch
    {
        AyarEntity when degisen.Any(p => p.Metadata.Name == nameof(AyarEntity.IzleyiciSifreHash)) => "IzleyiciSifresiDegisti",
        AliciEntity when degisen.Any(p => p.Metadata.Name == nameof(AliciEntity.SifreHash)) => "AliciSifresiDegisti",
        AliciEntity when degisen.Any(p => p.Metadata.Name == nameof(AliciEntity.OturumSurumu)) => "AliciOturumlariKapatildi",
        _ => null,
    };

    internal static string VarlikAdi(Type tur) => tur.Name.EndsWith("Entity", StringComparison.Ordinal) ? tur.Name[..^"Entity".Length] : tur.Name;

    private static string? Anahtar(EntityEntry e, bool orijinal)
    {
        var anahtar = e.Metadata.FindPrimaryKey();
        if (anahtar is null) return null;
        return string.Join("|", anahtar.Properties.Select(p =>
        {
            var deger = orijinal ? e.Property(p.Name).OriginalValue : e.Property(p.Name).CurrentValue;
            return Convert.ToString(deger, System.Globalization.CultureInfo.InvariantCulture);
        }));
    }

    private static object? Deger(IProperty p, object? deger)
    {
        if (Gizli.Contains(p.Name)) return deger is null ? null : "***";
        return deger switch
        {
            byte[] b => $"{b.Length} bayt",
            // Ekstre belgesinin ayrıştırılmış satırları büyük olabilir; kaynak dosya zaten saklanır.
            string s when p.Name == nameof(EkstreBelgeEntity.SatirlarJson) && s.Length > 4000 => $"{s.Length} karakter",
            _ => deger,
        };
    }

    private static bool Esit(object? a, object? b) => a is byte[] x && b is byte[] y ? x.AsSpan().SequenceEqual(y) : Equals(a, b);

    private static void Tarih(Kayit k, object? deger)
    {
        if (deger is DateOnly t) k.Tarihler.Add(t);
    }
}
