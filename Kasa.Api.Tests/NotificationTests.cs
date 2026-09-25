using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Kasa.Api.Auth;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

public sealed class NotificationTests
{
    private static readonly DateOnly Day = new(2026, 9, 23);
    private static TakipOlayDto Event(string source, string type, DateOnly date, decimal amount = 125m, int id = 1)
        => new(source, 1, id, "Deneme", date, amount, type, source == "Kredi");

    [Fact]
    public void OnlyRequestedFiveTimingsAreGenerated()
    {
        var events = new[] { Event("Kart", "Kesim", Day, 0), Event("Kart", "SonOdeme", Day),
            Event("Kart", "SonOdeme", Day.AddDays(3), id: 2), Event("Kredi", "Taksit", Day),
            Event("Kredi", "Taksit", Day.AddDays(3), id: 2), Event("Kart", "SonOdeme", Day.AddDays(-1), id: 3),
            Event("Kredi", "Taksit", Day.AddDays(1), id: 3), Event("Kart", "Kesim", Day.AddDays(3), id: 4) };
        var result = BildirimTakvimi.Olustur(events, Day);
        Assert.Equal(5, result.Count);
        Assert.Equal(5, result.Select(x => x.Anahtar).Distinct().Count());
        Assert.Contains(result, x => x.Tur == "Taksit" && x.Mesaj.Contains("bankadaki ödemeyi ayrıca kontrol et"));
    }

    [Fact]
    public void PaidCardIsSuppressedButCutoffAndLoanDueRemain()
    {
        var result = BildirimTakvimi.Olustur([Event("Kart", "SonOdeme", Day, 0), Event("Kart", "Kesim", Day, 0),
            Event("Kredi", "Taksit", Day)], Day);
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, x => x.Tur == "SonOdeme");
    }

    [Fact]
    public void IstanbulCalendarAndYearRolloverDoNotUseUtcDate()
    {
        var local = BildirimTakvimi.Yerel(new DateTimeOffset(2026, 12, 31, 22, 30, 0, TimeSpan.Zero));
        Assert.Equal(new DateTime(2027, 1, 1, 1, 30, 0), local);
        Assert.Single(BildirimTakvimi.Olustur([Event("Kredi", "Taksit", new(2027, 1, 3))], new(2026, 12, 31)));
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc", true)]
    [InlineData("https://web.push.apple.com/Qabc", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc", true)]
    [InlineData("https://wns2-db5p.notify.windows.com/w/?token=abc", true)]
    [InlineData("https://127.0.0.1/admin", false)]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc", false)]
    [InlineData("https://fcm.googleapis.com.attacker.com/x", false)]
    [InlineData("https://fcm.googleapis.com:444/x", false)]
    [InlineData("https://attacker@fcm.googleapis.com/x", false)]
    [InlineData("https://example.com/push", false)]
    public void PushEndpointsCannotBeUsedAsArbitraryServerRequests(string endpoint, bool expected)
        => Assert.Equal(expected, PushDogrulama.Endpoint(endpoint));

    [Fact]
    public void SubscriptionKeysMustBeValidCurvePoints()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = key.ExportParameters(false).Q;
        var publicKey = PushDogrulama.Encode([4, .. q.X!, .. q.Y!]);
        var auth = PushDogrulama.Encode(RandomNumberGenerator.GetBytes(16));
        Assert.True(PushDogrulama.Anahtarlar(publicKey, auth));
        Assert.False(PushDogrulama.Anahtarlar(PushDogrulama.Encode([4, .. new byte[64]]), auth));
        Assert.False(PushDogrulama.Anahtarlar(publicKey, "not-a-valid-auth-key"));
    }

    [Fact]
    public async Task PersistentDedupeSurvivesServiceRestartAndKeepsSeparateDevices()
    {
        using var fixture = new Fixture();
        fixture.Sources.Events = [Event("Kart", "SonOdeme", Day), Event("Kredi", "Taksit", Day)];
        fixture.Device("a"); fixture.Device("b");
        await fixture.Service().Gonder();
        await fixture.Service().Gonder();
        Assert.Equal(4, fixture.Sender.Calls.Count);
        Assert.Equal(2, fixture.Db.Set<BildirimEntity>().Count());
        Assert.Equal(4, fixture.Db.Set<BildirimTeslimEntity>().Count(x => x.Gonderildi != null));
    }

    [Fact]
    public async Task PaidBetweenQueueingAndDeliveryCancelsPendingReminder()
    {
        using var fixture = new Fixture();
        fixture.Sources.Events = [Event("Kart", "SonOdeme", Day)]; fixture.Device("a");
        await fixture.Service().Yenile();
        fixture.Sources.Events = [];
        await fixture.Service().Gonder();
        Assert.Empty(fixture.Sender.Calls);
        Assert.True(fixture.Db.Set<BildirimEntity>().Single().Iptal);
    }

    [Fact]
    public async Task PartialPaymentUpdatesMessageBeforeRetry()
    {
        using var fixture = new Fixture();
        fixture.Sources.Events = [Event("Kart", "SonOdeme", Day, 250)]; fixture.Device("a");
        fixture.Sender.Result = PushSonuc.GeciciHata;
        await fixture.Service().Gonder();
        fixture.Sources.Events = [Event("Kart", "SonOdeme", Day, 100)];
        fixture.Clock.Utc = fixture.Clock.Utc.AddMinutes(2); fixture.Sender.Result = PushSonuc.Basarili;
        await fixture.Service().Gonder();
        Assert.Equal(2, fixture.Sender.Calls.Count);
        Assert.Contains("100,00 TL", fixture.Sender.Calls.Last().Mesaj);
        await fixture.Service().Gonder(); Assert.Equal(2, fixture.Sender.Calls.Count);
    }

    [Fact]
    public async Task ExpiredSubscriptionAndPasswordChangeStopPush()
    {
        using var fixture = new Fixture();
        fixture.Sources.Events = [Event("Kredi", "Taksit", Day)]; fixture.Device("a");
        fixture.Sender.Result = PushSonuc.AbonelikBitti;
        await fixture.Service().Gonder();
        Assert.False(fixture.Db.Set<PushAbonelikEntity>().AsNoTracking().Single().Etkin);
        fixture.Device("b", "old-password-stamp");
        await fixture.Service().Gonder();
        Assert.Single(fixture.Sender.Calls);
        Assert.All(fixture.Db.Set<PushAbonelikEntity>().AsNoTracking(), x => Assert.False(x.Etkin));
    }

    [Fact]
    public async Task BeforeSendHourCreatesNothingAndMissedYesterdayIsNotSent()
    {
        using var fixture = new Fixture(); fixture.Device("a");
        fixture.Sources.Events = [Event("Kredi", "Taksit", Day), Event("Kart", "SonOdeme", Day.AddDays(-1))];
        fixture.Clock.Utc = new DateTimeOffset(2026, 9, 23, 5, 59, 0, TimeSpan.Zero);
        await fixture.Service().Gonder(); Assert.Empty(fixture.Sender.Calls);
        fixture.Clock.Utc = fixture.Clock.Utc.AddMinutes(1);
        await fixture.Service().Gonder(); Assert.Single(fixture.Sender.Calls);
    }

    [Fact]
    public async Task ChangingDueDatePreservesSentHistoryAndCreatesTheNewReminder()
    {
        using var fixture = new Fixture(); fixture.Device("a");
        fixture.Sources.Events = [Event("Kart", "SonOdeme", Day.AddDays(3))];
        await fixture.Service().Gonder();
        fixture.Sources.Events = [Event("Kart", "SonOdeme", Day.AddDays(4))];
        fixture.Clock.Utc = fixture.Clock.Utc.AddDays(1);
        await fixture.Service().Gonder();
        Assert.Equal(2, fixture.Sender.Calls.Count);
        var history = fixture.Db.Set<BildirimEntity>().AsNoTracking().OrderBy(x => x.Id).ToArray();
        Assert.Equal(Day, history[0].Tarih); Assert.Equal(Day.AddDays(1), history[1].Tarih);
        Assert.Contains("26.09.2026", history[0].Mesaj); Assert.Contains("27.09.2026", history[1].Mesaj);
    }

    [Fact]
    public async Task ChangingHourDefersAlreadyQueuedReminder()
    {
        using var fixture = new Fixture(); fixture.Device("a");
        fixture.Sources.Events = [Event("Kredi", "Taksit", Day)];
        await fixture.Service().Yenile();
        fixture.Db.Set<BildirimAyarEntity>().ExecuteUpdate(x => x.SetProperty(p => p.Saat, 10));
        await fixture.Service().Gonder(); Assert.Empty(fixture.Sender.Calls);
        fixture.Clock.Utc = fixture.Clock.Utc.AddHours(1);
        await fixture.Service().Gonder(); Assert.Single(fixture.Sender.Calls);
    }

    [Fact]
    public async Task EachLeaseStartsAtFreshTimeAndBatchStopsAtMidnight()
    {
        using var fixture = new Fixture(); fixture.Device("a"); fixture.Device("b"); fixture.Device("c");
        fixture.Sources.Events = [Event("Kredi", "Taksit", Day)];
        var checkedLeases = 0;
        fixture.Sender.OnSend = () =>
        {
            var lease = fixture.Db.Set<BildirimTeslimEntity>().AsNoTracking().Single(x => x.Kilit != null && x.Gonderildi == null);
            Assert.Equal(fixture.Clock.Utc.ToUnixTimeSeconds() + 120, lease.KilitBitis);
            checkedLeases++;
            fixture.Clock.Utc = checkedLeases == 1 ? fixture.Clock.Utc.AddMinutes(3)
                : new DateTimeOffset(2026, 9, 23, 21, 0, 1, TimeSpan.Zero);
            return Task.CompletedTask;
        };
        await fixture.Service().Gonder();
        Assert.Equal(2, checkedLeases); Assert.Equal(2, fixture.Sender.Calls.Count);
    }

    [Fact]
    public async Task SecondWorkerCannotSendAnAlreadyLeasedDelivery()
    {
        using var fixture = new Fixture(); fixture.Device("a");
        fixture.Sources.Events = [Event("Kredi", "Taksit", Day)];
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Sender.OnSend = () => { reached.SetResult(); return finish.Task; };
        var first = fixture.Service().Gonder();
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Service().Gonder();
        Assert.Single(fixture.Sender.Calls);
        finish.SetResult(); await first;
        Assert.Single(fixture.Db.Set<BildirimTeslimEntity>().Where(x => x.Gonderildi != null));
    }

    [Fact]
    public async Task ApiRequiresEditorAndSettingsUseVersionCheck()
    {
        using var factory = new KasaWebFactory();
        var guest = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/bildirimler")).StatusCode);
        var editor = await factory.EditorClientAsync();
        var before = await editor.GetFromJsonAsync<BildirimAyarYaz>("/api/bildirimler/ayarlar");
        Assert.NotNull(before); Assert.Equal(9, before.Saat);
        var changed = await editor.PutAsJsonAsync("/api/bildirimler/ayarlar", new BildirimAyarYaz(true, 10, 30, before.Surum));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var stale = await editor.PutAsJsonAsync("/api/bildirimler/ayarlar", new BildirimAyarYaz(false, 10, 30, before.Surum));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var invalid = await editor.PutAsJsonAsync("/api/bildirimler/ayarlar", new BildirimAyarYaz(true, 24, 0, before.Surum + 1));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Empty(await editor.GetFromJsonAsync<object[]>("/api/bildirimler/push/abonelikler") ?? []);
    }

    [Fact]
    public async Task PaidSameDayHidesPendingReminderButKeepsDeliveredHistory()
    {
        using var factory = new KasaWebFactory(); var client = await factory.EditorClientAsync();
        int deliveredId, pendingId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
            var today = DateOnly.FromDateTime(BildirimTakvimi.Yerel(DateTimeOffset.UtcNow));
            var sent = new BildirimEntity { OlayAnahtari = "paid-source-delivered", Baslik = "Ödeme günü", Mesaj = "Geçmiş uyarı", Tarih = today };
            var pending = new BildirimEntity { OlayAnahtari = "paid-source-pending", Baslik = "Ödeme günü", Mesaj = "Bekleyen uyarı", Tarih = today };
            var device = new PushAbonelikEntity { Endpoint = "https://fcm.googleapis.com/fcm/send/history-test" };
            db.AddRange(sent, pending, device); db.SaveChanges();
            db.Add(new BildirimTeslimEntity { BildirimId = sent.Id, AbonelikId = device.Id, Gonderildi = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
            db.SaveChanges(); deliveredId = sent.Id; pendingId = pending.Id;
        }
        // Neither paid source is returned by the finance event source during refresh.
        var rows = await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/bildirimler");
        Assert.NotNull(rows);
        Assert.Contains(rows, x => x.GetProperty("id").GetInt32() == deliveredId);
        Assert.DoesNotContain(rows, x => x.GetProperty("id").GetInt32() == pendingId);
    }

    [Fact]
    public async Task CashThresholdUsesExistingDeliveryDedupeAndDoesNotAlertEveryDay()
    {
        using var fixture = new Fixture();
        fixture.Db.Ayarlar.Add(new() { TakipBaslangic = Day.AddDays(-10) });
        var channel = new KanalEntity { Ad = "Kanal" }; fixture.Db.Kanallar.Add(channel); fixture.Db.SaveChanges();
        fixture.Db.KasaEsikleri.Add(new() { KanalId = channel.Id, Etkin = true, Tutar = 100m }); fixture.Db.SaveChanges();
        fixture.Device("a"); fixture.Device("b");
        await fixture.Service().Gonder(); await fixture.Service().Gonder();
        Assert.Equal(2, fixture.Sender.Calls.Count);
        Assert.Single(fixture.Db.Set<BildirimEntity>());
        fixture.Clock.Utc = fixture.Clock.Utc.AddDays(1);
        await fixture.Service().Gonder();
        Assert.Equal(2, fixture.Sender.Calls.Count);
        Assert.Single(fixture.Db.Set<BildirimEntity>());
    }

    [Fact]
    public async Task RecoveredCashCancelsQueuedThresholdNotificationBeforeSending()
    {
        using var fixture = new Fixture();
        fixture.Db.Ayarlar.Add(new() { TakipBaslangic = Day.AddDays(-10) });
        var channel = new KanalEntity { Ad = "Kanal" }; fixture.Db.Kanallar.Add(channel); fixture.Db.SaveChanges();
        fixture.Db.KasaEsikleri.Add(new() { KanalId = channel.Id, Etkin = true, Tutar = 100m }); fixture.Db.SaveChanges();
        fixture.Device("a");
        await fixture.Service().Yenile();
        fixture.Db.Kanallar.ExecuteUpdate(p => p.SetProperty(k => k.AcilisDevri, 100m));
        await fixture.Service().Gonder();
        Assert.Empty(fixture.Sender.Calls);
        Assert.True(fixture.Db.Set<BildirimEntity>().Single().Iptal);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Utc = new(2026, 9, 23, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Utc;
    }
    private sealed class Source : IBildirimKaynaklari
    {
        public IReadOnlyList<TakipOlayDto> Events = [];
        public IReadOnlyList<TakipOlayDto> Oku(KasaDbContext db, DateOnly today) => Events;
    }
    private sealed class Sender : IPushGonderici
    {
        public readonly List<PushIleti> Calls = [];
        public PushSonuc Result = PushSonuc.Basarili;
        public Func<Task>? OnSend;
        public async Task<PushSonuc> Gonder(PushAbonelikEntity s, PushIleti m, int ttl, CancellationToken ct)
        { Calls.Add(m); if (OnSend is not null) await OnSend(); return Result; }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public readonly KasaDbContext Db;
        public readonly Source Sources = new(); public readonly Sender Sender = new(); public readonly TestClock Clock = new();
        public readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Kasa:EditorKullanici"] = "editor", ["Kasa:EditorSifre"] = "test", ["Kasa:JwtKey"] = "notification-tests-only-long-enough-key" }).Build();
        public Fixture()
        {
            connection.Open(); Db = new(new DbContextOptionsBuilder<KasaDbContext>().UseSqlite(connection).Options);
            Db.Database.Migrate();
        }
        public void Device(string suffix, string? stamp = null)
        {
            Db.Add(new PushAbonelikEntity { Endpoint = "https://fcm.googleapis.com/fcm/send/" + suffix,
                CihazId = Guid.NewGuid().ToString(), CihazAdi = suffix, OturumDamgasi = stamp ?? OturumDamgasi.Uret("editor", Config, Db)!,
                Olusturuldu = Clock.GetUtcNow().ToUnixTimeSeconds() }); Db.SaveChanges(); Db.ChangeTracker.Clear();
        }
        public BildirimServisi Service() => new(Db, Sources, Sender, Config, Clock);
        public void Dispose() { Db.Dispose(); connection.Dispose(); }
    }
}
