namespace Kasa.App.Core.Tests;

/// <summary>Elle ilerletilen saat (yerel saat dilimi = UTC).</summary>
public sealed class SabitSaat : TimeProvider
{
    public DateTimeOffset Simdi;
    public SabitSaat(DateTime yerel) => Simdi = new DateTimeOffset(yerel, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Simdi;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public void Ilerle(TimeSpan t) => Simdi += t;
}
