using System.Globalization;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Kasa.Api.Data;

/// <summary>Geçmişteki eski/yeni JSON biçimi: API ile aynı (camelCase, enum metin), Türkçe karakterler kaçışsız.</summary>
public static class GecmisJson
{
    public static readonly JsonSerializerOptions Secenekler = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static string Yaz(object deger) => JsonSerializer.Serialize(deger, deger.GetType(), Secenekler);

    public static T Coz<T>(string json) => JsonSerializer.Deserialize<T>(json, Secenekler)
                                          ?? throw new JsonException("Kayıt boş.");
}

/// <summary>
/// SaveChanges'ten önce değişiklik izleyicideki eklenen / güncellenen / silinen kayıtları yakalar
/// ve geçmiş satırına (tür, kayıt Id, Türkçe özet, eski/yeni JSON) çevirir. Genel çalışır: tür
/// adı ve özet alanları <see cref="GecmisAttribute"/>'ten, gizli alanlar <see cref="GizliAttribute"/>'ten
/// (ya da adından) okunur; özniteliği olmayan yeni bir entity de kod değişikliği olmadan kaydedilir.
/// <see cref="GecmisDisiAttribute"/> taşıyan türler (geçmişin kendisi, iptal edilen token'lar) atlanır.
/// </summary>
internal static class DegisiklikKaydedici
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    private const string Bos = "—";

    /// <summary>Özniteliği unutulsa bile değeri asla yazılmayacak alan adı parçaları.</summary>
    private static readonly string[] GizliAdParcalari = ["Sifre", "Şifre", "Parola", "Hash", "Token", "Secret", "Password"];

    private static readonly Dictionary<string, string> Etiketler = new()
    {
        ["Ad"] = "Ad", ["Aktif"] = "Aktif", ["Sira"] = "Sıra", ["AcilisDevri"] = "Açılış devri",
        ["Tarih"] = "Tarih", ["Cari"] = "Cari", ["TutarTl"] = "Tutar", ["Tutar"] = "Tutar",
        ["Kanal"] = "Kanal", ["Tip"] = "Tip", ["Not"] = "Not", ["KrediKartiId"] = "Kredi kartı",
        ["DonemStart"] = "Dönem", ["TakipBaslangic"] = "Takip başlangıcı", ["KasaAcilisDevri"] = "Kasa açılış devri",
        ["KesimTarihi"] = "Kesim tarihi", ["SonOdemeTarihi"] = "Son ödeme tarihi", ["Limit"] = "Limit",
        ["Borc"] = "Açılış borcu",
        ["Yon"] = "Yön", ["CekNo"] = "Çek no", ["Banka"] = "Banka", ["Kisi"] = "Kişi/firma",
        ["DuzenlemeTarihi"] = "Düzenleme tarihi", ["VadeTarihi"] = "Vade", ["Durum"] = "Durum",
        ["IslemTarihi"] = "İşlem tarihi", ["SayilanTutar"] = "Sayılan", ["HesaplananTutar"] = "Defterdeki",
        ["KayitZamaniUtc"] = "Kayıt zamanı",
        ["Kalem"] = "Kalem", ["AyinGunu"] = "Ayın günü", ["BaslangicAyi"] = "Başlangıç ayı",
        ["TekrarlayanGiderId"] = "Tekrarlayan gider", ["Ay"] = "Ay", ["IslemId"] = "İşlem", ["Zaman"] = "Zaman",
        // Paket D
        ["Tur"] = "Tür", ["Konum"] = "Konum", ["CiroEdilenCari"] = "Ciro edilen cari",
        ["SatirlarJson"] = "Sayım satırları", ["FarkDurumu"] = "Fark durumu", ["FarkAciklamasi"] = "Fark açıklaması",
        ["Siklik"] = "Sıklık", ["TutarDegisken"] = "Tutar her seferinde girilir",
        ["DonemBaslangic"] = "Dönem başı", ["DonemBitis"] = "Kesim", ["EkstreTutari"] = "Ekstre tutarı",
        ["HesaplananBorc"] = "Uygulamadaki borç", ["TikliIslemIdleri"] = "İşaretli işlemler",
    };

    private static readonly Dictionary<string, string> EnumAdlari = new()
    {
        ["SabitGider"] = "Sabit gider", ["KrediKarti"] = "Kredi kartı",
        ["Alinan"] = "Alınan", ["Verilen"] = "Verilen", ["Portfoyde"] = "Portföyde",
        ["TahsilEdildi"] = "Tahsil edildi", ["Odendi"] = "Ödendi", ["CiroEdildi"] = "Ciro edildi",
        ["Karsiliksiz"] = "Karşılıksız", ["IadeEdildi"] = "İade edildi",
        ["Girildi"] = "Girildi", ["Atlandi"] = "Atlandı",
        // Paket D
        ["Cek"] = "Çek", ["Senet"] = "Senet",
        ["Elde"] = "Elde", ["BankadaTahsilde"] = "Bankada tahsilde", ["Teminatta"] = "Teminatta", ["Icrada"] = "İcrada",
        ["Aylik"] = "Aylık", ["UcAylik"] = "3 ayda bir", ["AltiAylik"] = "6 ayda bir", ["Yillik"] = "Yıllık",
        ["Acik"] = "Açık", ["Aciklandi"] = "Açıklandı", ["KabulEdildi"] = "Kabul edildi",
        ["Mutabik"] = "Mutabık", ["FarkKabul"] = "Fark kabul edildi",
    };

    internal sealed record Alan(IProperty Ozellik, string Etiket, bool Gizli, string? GizliMesaj);

    internal sealed record Tanim(string Tur, bool Disarida, IReadOnlyList<Alan> Alanlar,
        IReadOnlyList<IProperty> OzetAlanlari, IProperty? TekAnahtar);

    /// <summary>Kayıttan önce yakalanan tek değişiklik; satırı kayıttan sonra <see cref="Satir"/> üretir.</summary>
    internal sealed class Bekleyen
    {
        public required EntityEntry Kayit { get; init; }
        public required EntityState Durum { get; init; }
        public required Tanim Tanim { get; init; }
        public int? KayitId { get; init; }
        public string? EskiJson { get; init; }
        /// <summary>Silindi / Güncellendi özeti (kayıttan önce: sonra silinen kayda ulaşılamaz).</summary>
        public string? Ozet { get; init; }
    }

    /// <summary>
    /// Kaydedilecek değişiklikleri yakalar. SaveChanges'ten ÖNCE çağrılmalı: silinen kaydın ve
    /// güncellenen alanların eski değerlerine ancak bu noktada ulaşılır. Gerçekte değeri
    /// değişmeyen güncellemeler atlanır.
    /// </summary>
    internal static List<Bekleyen> Yakala(DbContext db)
    {
        var sonuc = new List<Bekleyen>();
        var kayitlar = db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();   // özet için ilişkili kayıt (Find) izleyiciye eklenebilir: önce kopyala
        foreach (var e in kayitlar)
        {
            var t = TanimOku(e.Metadata);
            if (t.Disarida) continue;
            switch (e.State)
            {
                case EntityState.Added:
                    sonuc.Add(new Bekleyen { Kayit = e, Durum = EntityState.Added, Tanim = t });
                    break;

                case EntityState.Deleted:
                    sonuc.Add(new Bekleyen
                    {
                        Kayit = e, Durum = EntityState.Deleted, Tanim = t,
                        KayitId = Anahtar(e, t, orijinal: true),
                        EskiJson = GecmisJson.Yaz(Degerler(e, t, orijinal: true)),
                        Ozet = Cumle(t.Tur, "silindi", Kimlik(db, e, t, orijinal: true)),
                    });
                    break;

                case EntityState.Modified:
                    var degisenler = new List<string>();
                    var mesajlar = new List<string>();
                    var gizliDegisti = false;
                    foreach (var a in t.Alanlar)
                    {
                        var p = e.Property(a.Ozellik.Name);
                        if (!p.IsModified || Equals(p.OriginalValue, p.CurrentValue)) continue;
                        if (a.Gizli)
                        {
                            gizliDegisti = true;
                            if (a.GizliMesaj is { } m && !mesajlar.Contains(m)) mesajlar.Add(m);
                            continue;
                        }
                        degisenler.Add($"{a.Etiket}: {Bicimle(db, a.Ozellik, p.OriginalValue)} → {Bicimle(db, a.Ozellik, p.CurrentValue)}");
                    }
                    if (degisenler.Count == 0 && !gizliDegisti) continue;

                    string ozet;
                    if (degisenler.Count == 0)
                        ozet = mesajlar.Count > 0 ? string.Join(" · ", mesajlar) : Cumle(t.Tur, "güncellendi", "");
                    else
                    {
                        ozet = $"{Cumle(t.Tur, "güncellendi", Kimlik(db, e, t, orijinal: false))} ({string.Join("; ", degisenler)})";
                        if (mesajlar.Count > 0) ozet += " · " + string.Join(" · ", mesajlar);
                    }
                    sonuc.Add(new Bekleyen
                    {
                        Kayit = e, Durum = EntityState.Modified, Tanim = t,
                        KayitId = Anahtar(e, t, orijinal: true),
                        EskiJson = GecmisJson.Yaz(Degerler(e, t, orijinal: true)),
                        Ozet = ozet,
                    });
                    break;
            }
        }
        return sonuc;
    }

    /// <summary>Kayıttan SONRA çağrılır: eklenen kaydın Id'si artık bellidir.</summary>
    internal static DegisiklikEntity Satir(DbContext db, Bekleyen b, string rol, DateTime zamanUtc, bool geriAlma)
    {
        var t = b.Tanim;
        var satir = new DegisiklikEntity { ZamanUtc = zamanUtc, Rol = rol, Tur = t.Tur };
        switch (b.Durum)
        {
            case EntityState.Added:
                satir.Eylem = geriAlma ? Eylemler.GeriAlindi : Eylemler.Eklendi;
                satir.KayitId = Anahtar(b.Kayit, t, orijinal: false);
                satir.Ozet = Cumle(t.Tur, geriAlma ? "eklendi (geri alındı)" : "eklendi", Kimlik(db, b.Kayit, t, orijinal: false));
                satir.YeniJson = GecmisJson.Yaz(Degerler(b.Kayit, t, orijinal: false));
                break;
            case EntityState.Modified:
                // Geri almada güncellenen kayıt "önceki haline döndürüldü" olarak yazılır.
                satir.Eylem = geriAlma ? Eylemler.GuncellemeGeriAlindi : Eylemler.Guncellendi;
                satir.KayitId = b.KayitId;
                satir.Ozet = geriAlma && b.Ozet!.StartsWith($"{t.Tur} güncellendi", StringComparison.Ordinal)
                    ? $"{t.Tur} önceki haline döndürüldü" + b.Ozet[$"{t.Tur} güncellendi".Length..]
                    : b.Ozet!;
                satir.EskiJson = b.EskiJson;
                satir.YeniJson = GecmisJson.Yaz(Degerler(b.Kayit, t, orijinal: false));
                break;
            default:
                satir.Eylem = Eylemler.Silindi;
                satir.KayitId = b.KayitId;
                satir.Ozet = b.Ozet!;
                satir.EskiJson = b.EskiJson;
                break;
        }
        return satir;
    }

    // ------------------------------------------------------------------ tanım

    internal static Tanim TanimOku(IEntityType tip)
    {
        var clr = tip.ClrType;
        var gecmis = clr.GetCustomAttribute<GecmisAttribute>();
        // JSON ve özet sınıftaki bildirim sırasını izlesin (EF özellikleri ada göre sıralar).
        var sira = clr.GetProperties().Select((p, i) => (p.Name, i)).ToDictionary(x => x.Name, x => x.i);
        var alanlar = tip.GetProperties()
            .OrderBy(p => sira.GetValueOrDefault(p.Name, int.MaxValue))
            .Select(p =>
            {
                var gizli = p.PropertyInfo?.GetCustomAttribute<GizliAttribute>();
                var addanGizli = GizliAdParcalari.Any(g => p.Name.Contains(g, StringComparison.OrdinalIgnoreCase));
                return new Alan(p, Etiket(p.Name), gizli is not null || addanGizli, gizli?.Mesaj);
            })
            .ToList();
        var anahtar = tip.FindPrimaryKey()?.Properties is [var k] && k.ClrType == typeof(int) ? k : null;
        IReadOnlyList<IProperty> ozet = gecmis is not null
            ? gecmis.OzetAlanlari.Select(a => tip.FindProperty(a)).OfType<IProperty>()
                .Where(p => !alanlar.Single(a => a.Ozellik == p).Gizli).ToList()
            : alanlar.Where(a => !a.Gizli && !a.Ozellik.IsPrimaryKey()).Take(3).Select(a => a.Ozellik).ToList();
        return new Tanim(gecmis?.Tur ?? Adlandir(clr.Name.EndsWith("Entity") ? clr.Name[..^"Entity".Length] : clr.Name),
            clr.IsDefined(typeof(GecmisDisiAttribute), inherit: true), alanlar, ozet, anahtar);
    }

    // ------------------------------------------------------------------ değerler

    private static Dictionary<string, object?> Degerler(EntityEntry e, Tanim t, bool orijinal)
    {
        var d = new Dictionary<string, object?>();
        foreach (var a in t.Alanlar.Where(a => !a.Gizli))
        {
            var p = e.Property(a.Ozellik.Name);
            d[JsonNamingPolicy.CamelCase.ConvertName(a.Ozellik.Name)] = orijinal ? p.OriginalValue : p.CurrentValue;
        }
        return d;
    }

    private static int? Anahtar(EntityEntry e, Tanim t, bool orijinal)
    {
        if (t.TekAnahtar is not { } k) return null;
        var p = e.Property(k.Name);
        return (orijinal ? p.OriginalValue : p.CurrentValue) as int?;
    }

    private static string Kimlik(DbContext db, EntityEntry e, Tanim t, bool orijinal)
        => string.Join(" · ", t.OzetAlanlari
            .Select(p => Bicimle(db, p, orijinal ? e.Property(p.Name).OriginalValue : e.Property(p.Name).CurrentValue))
            .Where(s => s != Bos));

    private static string Cumle(string tur, string eylem, string kimlik)
        => kimlik.Length > 0 ? $"{tur} {eylem}: {kimlik}" : $"{tur} {eylem}";

    // ------------------------------------------------------------------ biçim

    /// <summary>Değerin Türkçe okunur hali; başka kayda bağlı alan (ör. KrediKartiId) o kaydın adıyla gösterilir.</summary>
    private static string Bicimle(DbContext db, IProperty p, object? v)
    {
        if (v is null) return Bos;
        if (p.GetContainingForeignKeys().FirstOrDefault(f => f.Properties.Count == 1) is { } fk)
        {
            var asil = db.Find(fk.PrincipalEntityType.ClrType, v);
            // Bağlı kaydın adı: "Ad" alanı, yoksa geçmiş özetindeki ilk metin alanı (ör. tekrarlayan giderin kalemi).
            var adOzelligi = fk.PrincipalEntityType.FindProperty("Ad")
                             ?? TanimOku(fk.PrincipalEntityType).OzetAlanlari.FirstOrDefault(o => o.ClrType == typeof(string));
            if (asil is not null && adOzelligi is { } ad
                && db.Entry(asil).Property(ad.Name).CurrentValue is string s && s.Length > 0)
                return Metin.Kisalt(s);
            return $"#{v}";
        }
        return v switch
        {
            string s => s.Length == 0 ? Bos : Metin.Kisalt(s),
            decimal d => Para(d),
            DateOnly t => t.ToString("dd.MM.yyyy", Tr),
            DateTime t => t.ToString("dd.MM.yyyy HH:mm", Tr),
            bool b => b ? "evet" : "hayır",
            Enum en => EnumAdlari.GetValueOrDefault(en.ToString()) ?? Adlandir(en.ToString()),
            IFormattable f => f.ToString(null, Tr),
            _ => v.ToString() ?? Bos,
        };
    }

    internal static string Para(decimal d) => d.ToString("#,##0.00", Tr) + " ₺";

    private static string Etiket(string ozellik) => Etiketler.GetValueOrDefault(ozellik) ?? Adlandir(ozellik);

    /// <summary>"KasaSayimi" → "Kasa sayimi": bilinmeyen tür/alan adları için okunur yedek.</summary>
    private static string Adlandir(string ad)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < ad.Length; i++)
        {
            var c = ad[i];
            // Sınıf adları ASCII'dir: I → i (Türkçe kültürde ı olurdu).
            if (i > 0 && char.IsUpper(c)) sb.Append(' ').Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
