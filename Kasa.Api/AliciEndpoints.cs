using System.Security.Claims;
using System.Text.RegularExpressions;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kasa.Api;

public static partial class AliciEndpoints
{
    public static void MapAliciEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/alicilar").RequireAuthorization("Editor");
        api.MapGet("", (KasaDbContext db) => db.Alicilar.AsNoTracking().OrderBy(a => a.Ad)
            .Select(a => new AliciDto(a.Id, a.Kullanici, a.Ad, a.Aktif)).ToList());
        api.MapPost("", (AliciYaz dto, KasaDbContext db, IConfiguration cfg) =>
        {
            if (Dogrula(dto, db, cfg, null) is { } hata) return hata;
            var e = new AliciEntity
            {
                Kullanici = dto.Kullanici.Trim().ToLowerInvariant(), Ad = dto.Ad.Trim(),
                SifreHash = SifreHasher.Hashle(dto.Sifre!), Aktif = dto.Aktif
            };
            db.Alicilar.Add(e);
            db.SaveChanges();
            return Results.Created($"/api/alicilar/{e.Id}", Oku(e));
        });
        api.MapPut("/{id:int}", (int id, AliciYaz dto, KasaDbContext db, IConfiguration cfg) =>
        {
            var e = db.Alicilar.Find(id);
            if (e is null) return Results.NotFound();
            if (Dogrula(dto, db, cfg, id) is { } hata) return hata;
            var oturumlariKapat = e.Aktif != dto.Aktif || e.Kullanici != dto.Kullanici.Trim().ToLowerInvariant();
            e.Kullanici = dto.Kullanici.Trim().ToLowerInvariant();
            e.Ad = dto.Ad.Trim();
            // Pasife alıp tekrar açmak eski oturumu diriltmesin.
            if (!string.IsNullOrEmpty(dto.Sifre)) e.SifreHash = SifreHasher.Hashle(dto.Sifre);
            if (oturumlariKapat) e.OturumSurumu++;
            e.Aktif = dto.Aktif;
            db.SaveChanges();
            return Results.Ok(Oku(e));
        });
    }

    private static AliciDto Oku(AliciEntity e) => new(e.Id, e.Kullanici, e.Ad, e.Aktif);

    private static IResult? Dogrula(AliciYaz dto, KasaDbContext db, IConfiguration cfg, int? id)
    {
        var v = new GirdiDogrulama();
        v.Metin(dto.Kullanici, "kullanici", 64);
        v.Metin(dto.Ad, "ad");
        var kullanici = dto.Kullanici?.Trim().ToLowerInvariant() ?? "";
        v.Kontrol(KullaniciDeseni().IsMatch(kullanici), "kullanici", "Kullanıcı adı 3–64 karakter olmalı; a–z, 0–9, nokta, tire ve alt çizgi kullanılabilir.");
        v.Kontrol(!string.Equals(kullanici, cfg["Kasa:EditorKullanici"], StringComparison.OrdinalIgnoreCase),
            "kullanici", "Editörün kullanıcı adı kullanılamaz.");
        if (id is null || !string.IsNullOrEmpty(dto.Sifre))
            v.Kontrol(!string.IsNullOrWhiteSpace(dto.Sifre) && dto.Sifre.Length is >= 8 and <= 1024,
                "sifre", "Şifre 8–1024 karakter olmalı.");
        if (v.Sonuc() is { } hata) return hata;
        return db.Alicilar.Any(a => a.Id != id && a.Kullanici == kullanici)
            ? Results.Conflict(new { hata = "Bu kullanıcı adı zaten kullanılıyor." }) : null;
    }

    [GeneratedRegex("^[a-z0-9._-]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex KullaniciDeseni();
}

/// <summary>
/// 'Kasa:AliciKota' bölümü (host-auth-5, purchase-3): en düşük yetkili rol olan alıcının kalıcı olarak yazabileceği veri.
/// Belgeler ana veritabanında BLOB olarak durur ve her otomatik yedekle kopyalanır; kotasız bir alıcı hesabı diski doldurup
/// bütün yazmaları ve yedeklemeyi durdurabilirdi. Editör bu kotalara tabi değildir (genel sınır: alış başına 30 belge, dosya
/// başına 10 MB). Değerler istek anında okunur; başlangıçta <see cref="Hatalar"/> ile doğrulanır.
/// </summary>
public sealed class AliciKotaAyarlari
{
    /// <summary>Alıcının aynı anda taslak durumundaki alış sayısı (iade edilenler dahil).</summary>
    public int AcikTaslak { get; set; } = 20;
    /// <summary>Alıcının bir taslağa ekleyebileceği belge sayısı.</summary>
    public int TaslakBelgeSayisi { get; set; } = 20;
    /// <summary>Bir taslaktaki belgelerin toplam boyutu (MB); alıcı yüklerken denetlenir.</summary>
    public int TaslakBelgeMb { get; set; } = 50;
    /// <summary>Alıcının alışlarına son 24 saatte yüklenmiş ve hâlâ duran belgelerin toplam boyutu (MB).</summary>
    public int GunlukYuklemeMb { get; set; } = 100;

    public IEnumerable<string> Hatalar()
    {
        if (this is not { AcikTaslak: > 0, TaslakBelgeSayisi: > 0, TaslakBelgeMb: > 0, GunlukYuklemeMb: > 0 })
            yield return "Kasa:AliciKota değerleri (AcikTaslak, TaslakBelgeSayisi, TaslakBelgeMb, GunlukYuklemeMb) sıfırdan büyük olmalıdır.";
    }
}

/// <summary>Başlangıç doğrulaması: <see cref="AliciKotaAyarlari.Hatalar"/> boş değilse uygulama başlamaz.</summary>
internal sealed class AliciKotaDogrulayici : IValidateOptions<AliciKotaAyarlari>
{
    public ValidateOptionsResult Validate(string? name, AliciKotaAyarlari ayarlar)
        => ayarlar.Hatalar().ToList() is { Count: > 0 } hatalar ? ValidateOptionsResult.Fail(hatalar) : ValidateOptionsResult.Success;
}

/// <summary>
/// Alıcı kotalarının denetimi. Çağıran yazma transaction'ının (BEGIN IMMEDIATE) içindedir: sayım ile kayıt arasına başka
/// bir istek giremez. Aşım 409 ve Türkçe 'hata' döner; editör ve alıcı olmayan oturum denetlenmez.
/// </summary>
internal static class AliciKotalari
{
    private const long Mb = 1024 * 1024;

    internal static int? AliciId(ClaimsPrincipal user) => int.TryParse(user.FindFirstValue("alici_id"), out var id) && id > 0 ? id : null;

    /// <summary>Yeni taslak alış: alıcının açık taslak sayısı.</summary>
    internal static IResult? Taslak(KasaDbContext db, ClaimsPrincipal user, AliciKotaAyarlari kota)
    {
        if (user.IsInRole("editor") || AliciId(user) is not { } aliciId) return null;
        var acik = db.Alislar.Count(a => a.AliciId == aliciId && a.Durum == AlisDurumlari.Taslak);
        return acik >= kota.AcikTaslak
            ? AlisEndpoints.Conflict($"En fazla {kota.AcikTaslak} açık taslak alışınız olabilir. Önce mevcut taslakları incelemeye gönderin.")
            : null;
    }

    /// <summary>Alıcının belge yüklemesi: taslaktaki belge sayısı ve toplam boyutu, son 24 saatteki yükleme hacmi. Belge içeriği
    /// okunmaz (yalnız boyut ve an); 24 saat süzmesi bellekte yapılır (SQLite DateTimeOffset karşılaştırmasını çeviremez),
    /// alıcının kotalı belge sayısı zaten küçüktür. Editörün alıcının alışına eklediği belge de hacme sayılır: kota alıcının
    /// alışlarındaki veriyi sınırlar. Silinen belge disk tutmadığından sayılmaz; yükle-sil döngüsünü 'alis-yukleme' hız
    /// politikası yavaşlatır.</summary>
    internal static IResult? Belge(KasaDbContext db, ClaimsPrincipal user, int alisId, long boyut, AliciKotaAyarlari kota, DateTimeOffset simdi)
    {
        if (user.IsInRole("editor") || AliciId(user) is not { } aliciId) return null;
        var taslaktakiler = db.Belgeler.Where(b => b.AlisId == alisId).Select(b => b.Boyut).ToList();
        if (taslaktakiler.Count >= kota.TaslakBelgeSayisi)
            return AlisEndpoints.Conflict($"Bir taslağa en fazla {kota.TaslakBelgeSayisi} belge ekleyebilirsiniz. Gereksiz belgeleri silin veya editöre başvurun.");
        if (taslaktakiler.Sum() + boyut > kota.TaslakBelgeMb * Mb)
            return AlisEndpoints.Conflict($"Bir taslaktaki belgelerin toplam boyutu en fazla {kota.TaslakBelgeMb} MB olabilir. Daha küçük dosya seçin veya gereksiz belgeleri silin.");
        var sinir = simdi.AddDays(-1);
        var gunluk = db.Belgeler.Where(b => db.Alislar.Any(a => a.Id == b.AlisId && a.AliciId == aliciId))
            .Select(b => new { b.Boyut, b.Yuklendi }).AsEnumerable().Where(b => b.Yuklendi > sinir).Sum(b => b.Boyut);
        return gunluk + boyut > kota.GunlukYuklemeMb * Mb
            ? AlisEndpoints.Conflict($"Son 24 saatte en fazla {kota.GunlukYuklemeMb} MB belge yükleyebilirsiniz. Daha sonra yeniden deneyin veya editöre başvurun.")
            : null;
    }
}

/// <summary>
/// POST /api/alis'in yazma transaction'ı içindeki kuralları (appcore-5, host-auth-5). İstemci istek kimliği (IstekId)
/// gönderirse oluşturma tekrar edilebilir: aynı kimlik ve aynı içerik (aynı kullanıcıdan) ilk alışı 200 ile döndürür, ikinci
/// taslak oluşmaz; farklı içerik ya da başka kullanıcı 409 alır. Kimlik göndermeyen eski istemci eskisi gibi her istekte yeni
/// taslak açar. Tekrar denetimi kotadan önce yapılır: kotayı dolduran ilk isteğin yeniden gönderimi yine ilk sonucu alır.
/// </summary>
internal static class AlisOlusturmaKurallari
{
    private const string Tur = "AlisOlustur";

    /// <summary>Kayıttan önce: tekrar yanıtı ya da kota aşımı; null ise oluşturma sürer.</summary>
    internal static IResult? Once(KasaDbContext db, AlisYaz dto, ClaimsPrincipal user, AliciKotaAyarlari kota)
    {
        if (dto.IstekId is { } istekId && FinansHesaplari.Tekrar(db, istekId, Tur, Ozet(dto, user), id => db.Alislar.Any(a => a.Id == id)
                ? Results.Ok(AlisEndpoints.ReadDto(db, id))
                : AlisEndpoints.Conflict("Bu istekle oluşturulan alış artık yok. Alışı yeniden kaydedin.")) is { } tekrar)
            return tekrar;
        return AliciKotalari.Taslak(db, user, kota);
    }

    /// <summary>Kayıttan sonra, aynı transaction'da: istek kimliğini sonucuyla saklar.</summary>
    internal static void Kaydet(KasaDbContext db, AlisYaz dto, ClaimsPrincipal user, int alisId)
    {
        if (dto.IstekId is not { } istekId) return;
        FinansHesaplari.IstekKaydet(db, istekId, Tur, Ozet(dto, user), alisId);
        db.SaveChanges();
    }

    // Özet gövdeyle birlikte oluşturanı da taşır: başka bir alıcı aynı kimlikle ilk alıcının alışını okuyamaz.
    private static string Ozet(AlisYaz dto, ClaimsPrincipal user)
        => FinansHesaplari.Ozet(new { dto = dto with { IstekId = null }, editor = user.IsInRole("editor"), aliciId = AliciKotalari.AliciId(user) });
}
