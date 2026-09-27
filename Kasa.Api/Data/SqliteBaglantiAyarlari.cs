using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kasa.Api.Data;

/// <summary>
/// Bağlantı düzeyinde SQLite kilit beklemesi (gap-okuma-yolu-maliyet-kilit-cekismesi-7). Microsoft.Data.Sqlite meşgul
/// veritabanında komutu kendi döngüsünde 150 ms'lik Thread.Sleep'lerle yeniden dener ve varsayılan olarak 30 sn bekler; bu
/// süre boyunca istek bir thread-pool iş parçacığını uyutur (0,5 CPU'lu konteynerde havuz tek iş parçacığıyla başlar).
/// Burada bekleme SQLite'ın kendi bekleyicisine (<c>PRAGMA busy_timeout</c>) verilir: kilit bırakılır bırakılmaz (ms
/// duyarlılıkla) alınır, sürücünün döngüsü yalnız süre dolunca hatayı fırlatan bir yedek olarak kalır. Bağlantı dizesi süre
/// vermezse (<c>Default Timeout</c> ya da <c>Command Timeout</c>) <see cref="VarsayilanBeklemeSaniye"/> uygulanır: kilit
/// alınamayan istek iş parçacığını 30 sn tutmaz, istemcinin zaman aşımından önce 503 + Retry-After alır
/// (<see cref="VeritabaniHataSiniflandirici"/>). Bağlantı dizesindeki açık süre (testler, operatör) olduğu gibi kullanılır.
/// Bekleme yine de iş parçacığını uyuttuğundan havuz en az 16 işçiyle başlar: <c>Kasa.Api/runtimeconfig.template.json</c>
/// (<c>System.Threading.ThreadPool.MinThreads</c>); kilit bekleyen birkaç istek hafif istekleri (statik dosya, oturum) bekletmez.
/// </summary>
public static class SqliteBaglantiAyarlari
{
    /// <summary>Bağlantı dizesi süre vermediğinde kilit beklemesi (saniye). Masaüstü istemcisinin normal istek sınırının
    /// (15 sn, KasaZamanAsimlari) altındadır; WAL kipinde yalnız yazanlar birbirini beklediğinden olağan bir yazma
    /// transaction'ı bu sürenin çok altında biter.</summary>
    public const int VarsayilanBeklemeSaniye = 10;

    private static readonly ConcurrentDictionary<string, bool> AcikSureler = new(StringComparer.Ordinal);

    internal static readonly DbConnectionInterceptor Kesici = new BaglantiKesici();

    /// <summary>Bağlantı dizesi kilit beklemesini açıkça veriyor mu (<c>Default Timeout</c> / <c>Command Timeout</c>).</summary>
    public static bool SureAcikVerilmis(string? baglanti) => !string.IsNullOrEmpty(baglanti) && AcikSureler.GetOrAdd(baglanti, b =>
    {
        var anahtarlar = new DbConnectionStringBuilder { ConnectionString = b };
        return anahtarlar.ContainsKey("Default Timeout") || anahtarlar.ContainsKey("Command Timeout");
    });

    /// <summary>Açılmış bağlantıya bekleme süresini uygular: sürücünün komut süresi ve SQLite'ın <c>busy_timeout</c>'u aynı
    /// süredir. 0 (sınırsız) verilmişse SQLite bekleyicisi kurulmaz, sürücünün sınırsız beklemesi korunur.</summary>
    public static void Uygula(SqliteConnection baglanti)
    {
        if (!SureAcikVerilmis(baglanti.ConnectionString)) baglanti.DefaultTimeout = VarsayilanBeklemeSaniye;
        if (baglanti.DefaultTimeout <= 0) return;
        using var komut = baglanti.CreateCommand();
        komut.CommandText = "PRAGMA busy_timeout = " + (baglanti.DefaultTimeout * 1000L).ToString(CultureInfo.InvariantCulture);
        komut.ExecuteNonQuery();
    }

    /// <summary>EF'in açtığı her bağlantıya (havuzdan dönen dahil) ayarı uygular; EF'in komut logunda görünmez.</summary>
    private sealed class BaglantiKesici : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            if (connection is SqliteConnection sqlite) Uygula(sqlite);
        }

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (connection is SqliteConnection sqlite) Uygula(sqlite);
            return Task.CompletedTask;
        }
    }
}

public partial class KasaDbContext
{
    /// <summary>Uygulamanın, testlerin ve araçların kurduğu her bağlam aynı bağlantı ayarını alır (bkz. <see cref="SqliteBaglantiAyarlari"/>).</summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.AddInterceptors(SqliteBaglantiAyarlari.Kesici);
}
