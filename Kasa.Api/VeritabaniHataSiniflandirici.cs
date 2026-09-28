using System.Diagnostics;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Kasa.Api.Data;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

/// <summary>Veritabanı hatasının kullanıcıya dönen türü.</summary>
public enum VeritabaniHataTuru
{
    /// <summary>SQLITE_BUSY/LOCKED: yazma kilidi bekleme süresi içinde alınamadı. İşlem yapılmadı; kısa süre sonra yeniden denenebilir (503).</summary>
    Mesgul,
    /// <summary>UNIQUE/PRIMARY KEY: kayıt, aynı anda yazılmış başka bir kayıtla çakıştı (409).</summary>
    Cakisma,
    /// <summary>EF sürüm (concurrency token) çakışması: kayıt okunduktan sonra başka bir işlemle değişti (409).</summary>
    SurumCakismasi,
    /// <summary>Kilitli dönem ya da kayıt kaynağı kuralı (<see cref="KilitliDonemException"/>, 'Kilitli ay' tetikleyicisi) (409).</summary>
    KilitliDonem,
    /// <summary>Başka bir veritabanı tetikleyicisinin iş kuralı (RAISE(ABORT, ...)); ileti tetikleyicinin kendi iletisidir (409).</summary>
    Tetikleyici,
    /// <summary>FOREIGN KEY, CHECK, NOT NULL ve diğer kısıtlar: kodun doğrulamadığı bir bütünlük hatası; kullanıcı yeniden
    /// denemekle düzeltemez (500, Error).</summary>
    Butunluk,
}

/// <summary>Sınıflandırılmış veritabanı hatası: HTTP durumu, kullanıcıya gösterilecek ileti ve (varsa) SQLite istisnası.</summary>
public sealed record VeritabaniHatasi(VeritabaniHataTuru Tur, int Durum, string Ileti, SqliteException? Sqlite);

/// <summary>
/// Veritabanı hatalarının tek sınıflandırıcısı (gap-okuma-yolu-maliyet-kilit-cekismesi-5, gap-denetim-izi-gozlemlenebilirlik-11).
/// Hem genel istisna işleyicisi (<see cref="VeritabaniHataIsleyici"/>) hem uç sarmalayıcıları (<see cref="AlisEndpoints.Mutate"/>,
/// <see cref="AlisEndpoints.Oku"/>) aynı kuralı kullanır: aynı kök neden her uçta aynı yanıtı verir.
/// Kilit beklemesi (BUSY/LOCKED) iş kuralı çakışması gibi gösterilmez: 503 + Retry-After döner. Kodun doğrulamadığı bütünlük
/// hataları (FK, CHECK, NOT NULL) "listeyi yenileyin" diye örtülmez: 500 döner ve Error olarak loglanır. 409/503'e çevrilen her
/// hata <see cref="LogKategorisi"/> kategorisinde Warning olarak uç, rol, SQLite (genişletilmiş) kodu ve iz kimliğiyle (meşgulde
/// bağlantının etkin kilit beklemesiyle) yazılır;
/// ASP.NET Core, <c>IExceptionHandler</c>'ın işlediği istisnayı kendisi loglamaz.
/// </summary>
public static partial class VeritabaniHataSiniflandirici
{
    public const string LogKategorisi = "Kasa.Veritabani";
    /// <summary>Meşgul yanıtının Retry-After değeri (saniye).</summary>
    public const int YenidenDenemeSaniye = 2;
    public const string MesgulIletisi = "Sunucu şu an başka bir kayıt işlemini tamamlıyor. Birkaç saniye sonra tekrar deneyin.";
    public const string CakismaIletisi = "Kayıt başka bir kayıtla çakışıyor. Listeyi yenileyip tekrar deneyin.";
    public const string SurumIletisi = "Kayıt başka bir işlemle değişti. Listeyi yenileyip tekrar deneyin.";
    public const string KilitliAyIletisi = "Bu tarih kilitli dönemde. Değişiklik için ilgili ayı gerekçeyle açın.";
    private const string TetikleyiciIletisi = "Kayıt bu işlemle değiştirilemez.";

    // SQLite birincil ve genişletilmiş sonuç kodları (https://sqlite.org/rescode.html).
    private const int Busy = 5, Locked = 6, Constraint = 19;
    private const int ConstraintTrigger = 1811, ConstraintUnique = 2067, ConstraintPrimaryKey = 1555;

    /// <summary>
    /// İstisnanın veritabanı hatası karşılığı; veritabanıyla ilgisizse (ya da bilinmeyen bir SQLite hatasıysa) null: çağıran
    /// istisnayı olduğu gibi fırlatır, genel işleyici 500 döner. <paramref name="cakismaIletisi"/> UNIQUE çakışmasında bağlama
    /// özgü iletidir (ör. aynı ödeme isteği); verilmezse genel ileti kullanılır.
    /// </summary>
    public static VeritabaniHatasi? Siniflandir(Exception e, string? cakismaIletisi = null)
    {
        if (e is KilitliDonemException) return new(VeritabaniHataTuru.KilitliDonem, StatusCodes.Status409Conflict, e.Message, null);
        if (e is DbUpdateConcurrencyException) return new(VeritabaniHataTuru.SurumCakismasi, StatusCodes.Status409Conflict, SurumIletisi, null);
        if (Sqlite(e) is not { } s) return null;
        if (Mesgul(s)) return new(VeritabaniHataTuru.Mesgul, StatusCodes.Status503ServiceUnavailable, MesgulIletisi, s);
        if ((s.SqliteErrorCode & 0xFF) != Constraint) return null;
        if (s.Message.Contains("Kilitli ay", StringComparison.Ordinal))
            return new(VeritabaniHataTuru.KilitliDonem, StatusCodes.Status409Conflict, KilitliAyIletisi, s);
        if (s.SqliteExtendedErrorCode == ConstraintTrigger)
            return new(VeritabaniHataTuru.Tetikleyici, StatusCodes.Status409Conflict, TetikleyiciMetni(s) ?? TetikleyiciIletisi, s);
        if (s.SqliteExtendedErrorCode is ConstraintUnique or ConstraintPrimaryKey)
            return new(VeritabaniHataTuru.Cakisma, StatusCodes.Status409Conflict, cakismaIletisi ?? CakismaIletisi, s);
        return new(VeritabaniHataTuru.Butunluk, StatusCodes.Status500InternalServerError,
            "Kayıt bir veri bütünlüğü kuralına takıldığı için kaydedilmedi. Sorun sürerse yöneticiye iz kimliğini bildirin.", s);
    }

    /// <summary>İstisnanın kendisi ya da iç istisnalarından ilk SQLite istisnası (EF onu DbUpdateException içine sarar).</summary>
    public static SqliteException? Sqlite(Exception? e)
    {
        for (var i = 0; e is not null && i < 8; e = e.InnerException, i++)
            if (e is SqliteException s) return s;
        return null;
    }

    /// <summary>Kilit beklemesi süresinde alınamadı (SQLITE_BUSY/LOCKED ve genişletilmiş biçimleri): kayda özgü değildir.</summary>
    public static bool Mesgul(SqliteException e) => (e.SqliteErrorCode & 0xFF) is Busy or Locked;

    /// <summary>Veritabanının kendisinin (disk, dosya, bellek, bozulma) hatası: kayda özgü değildir, sonraki denemede
    /// düzelebilir ya da operatör müdahalesi ister. IOERR, NOMEM, INTERRUPT, CORRUPT, FULL, CANTOPEN, PROTOCOL, NOTADB.</summary>
    public static bool Altyapi(SqliteException e) => (e.SqliteErrorCode & 0xFF) is 7 or 9 or 10 or 11 or 13 or 14 or 15 or 26;

    // Microsoft.Data.Sqlite iletisi: "SQLite Error 19: 'Tetikleyicinin iletisi'." — kullanıcıya yalnız tırnak içi gösterilir.
    [GeneratedRegex(@"^SQLite Error \d+: '(?<ileti>.+)'\.?$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SqliteIletisi();

    private static string? TetikleyiciMetni(SqliteException s)
        => SqliteIletisi().Match(s.Message) is { Success: true } m ? m.Groups["ileti"].Value : null;

    /// <summary>Hatayı istek bağlamıyla loglar ve yanıtı yazar: 409/503 için <c>{ hata }</c> (503'te Retry-After),
    /// bütünlük hatası (500) için iz kimlikli ProblemDetails.</summary>
    public static async Task Yanitla(HttpContext http, VeritabaniHatasi hata, Exception istisna)
    {
        var iz = Activity.Current?.Id ?? http.TraceIdentifier;
        Logla(http, hata, istisna, iz);
        if (hata.Tur == VeritabaniHataTuru.Mesgul)
            http.Response.Headers.RetryAfter = YenidenDenemeSaniye.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sonuc = hata.Tur == VeritabaniHataTuru.Butunluk
            ? Results.Problem(hata.Ileti, statusCode: hata.Durum, title: "Veri bütünlüğü hatası",
                extensions: new Dictionary<string, object?> { ["traceId"] = iz })
            : Results.Json(new { hata = hata.Ileti }, statusCode: hata.Durum);
        await sonuc.ExecuteAsync(http);
    }

    private static void Logla(HttpContext http, VeritabaniHatasi hata, Exception istisna, string iz)
    {
        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogKategorisi);
        // Genel işleyicide uç bilgisi yalnız IExceptionHandlerFeature'da kalır (ara katman isteğin uç bağını temizler).
        var ozellik = http.Features.Get<IExceptionHandlerFeature>();
        var uc = http.GetEndpoint() ?? ozellik?.Endpoint;
        var yol = (uc as RouteEndpoint)?.RoutePattern.RawText ?? ozellik?.Path ?? http.Request.Path.Value;
        var rol = http.User.FindFirstValue(ClaimTypes.Role) ?? "anonim";
        // Kullanıcı girdisi ve tutar yazılmaz: SQLite iletisi yalnız kısıt/tablo adını, sürüm ve kural iletileri sabit metni taşır.
        var ayrinti = hata.Sqlite?.Message ?? istisna.Message;
        if (hata.Tur == VeritabaniHataTuru.Butunluk)
            logger.LogError(istisna, "Veritabanı hatası {Tur} → {Durum}: SQLite {Kod}/{GenisKod}, uç {Yontem} {Uc}, rol {Rol}, iz {Iz}. {Ayrinti}",
                hata.Tur, hata.Durum, hata.Sqlite?.SqliteErrorCode, hata.Sqlite?.SqliteExtendedErrorCode, http.Request.Method, yol, rol, iz, ayrinti);
        else if (hata.Tur == VeritabaniHataTuru.Mesgul)
            // Kilidin ne kadar beklendiği: isteğin bağlamının etkin kilit beklemesi (SqliteBaglantiAyarlari ile aynı kural).
            logger.LogWarning("Veritabanı hatası {Tur} → {Durum}: SQLite {Kod}/{GenisKod}, bekleme {Bekleme} sn, uç {Yontem} {Uc}, rol {Rol}, iz {Iz}. {Ayrinti}",
                hata.Tur, hata.Durum, hata.Sqlite?.SqliteErrorCode, hata.Sqlite?.SqliteExtendedErrorCode, BeklemeSaniye(http), http.Request.Method, yol, rol, iz, ayrinti);
        else
            logger.LogWarning("Veritabanı hatası {Tur} → {Durum}: SQLite {Kod}/{GenisKod}, uç {Yontem} {Uc}, rol {Rol}, iz {Iz}. {Ayrinti}",
                hata.Tur, hata.Durum, hata.Sqlite?.SqliteErrorCode, hata.Sqlite?.SqliteExtendedErrorCode, http.Request.Method, yol, rol, iz, ayrinti);
    }

    /// <summary>İsteğin veritabanı bağlamının etkin kilit beklemesi (saniye); bağlam yoksa null.</summary>
    private static int? BeklemeSaniye(HttpContext http) => http.RequestServices.GetService<KasaDbContext>() is { } db
        ? SqliteBaglantiAyarlari.BeklemeSaniye(db.Database.GetConnectionString()) : null;
}

/// <summary>Uç sarmalayıcılarının döndürdüğü sınıflandırılmış hata; loglama ve yanıt, istek bağlamı (uç, rol, iz) belli
/// olduğunda <see cref="ExecuteAsync"/>'te yapılır. Durum kodunu taşıdığından çağıran onu diğer hata yanıtları gibi görür.</summary>
public sealed class VeritabaniHataSonucu(VeritabaniHatasi hata, Exception istisna) : IResult, IStatusCodeHttpResult
{
    public VeritabaniHatasi Hata => hata;
    public int? StatusCode => hata.Durum;
    public Task ExecuteAsync(HttpContext httpContext) => VeritabaniHataSiniflandirici.Yanitla(httpContext, hata, istisna);
}
