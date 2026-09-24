namespace Kasa.App.Core;

/// <summary>"2 saat önce", "dün", "3 gün önce" gibi göreli zaman metinleri (yerel saatle).</summary>
public static class GoreceZaman
{
    /// <param name="zamanUtc">Olayın zamanı (UTC; türü belirtilmemişse UTC kabul edilir).</param>
    /// <param name="simdi">Şimdi (yerel saat dilimi bilgisini taşır).</param>
    /// <param name="saatDilimi">Yerel saat dilimi ("dün" ve tarih için).</param>
    public static string Metin(DateTime zamanUtc, DateTimeOffset simdi, TimeZoneInfo saatDilimi)
    {
        var utc = zamanUtc.Kind == DateTimeKind.Local ? zamanUtc.ToUniversalTime() : DateTime.SpecifyKind(zamanUtc, DateTimeKind.Utc);
        var fark = simdi.UtcDateTime - utc;
        if (fark < TimeSpan.FromMinutes(1)) return "az önce";               // saat farkı / ileri zaman da "az önce"
        if (fark < TimeSpan.FromHours(1)) return $"{(int)fark.TotalMinutes} dakika önce";
        var yerel = TimeZoneInfo.ConvertTimeFromUtc(utc, saatDilimi);
        var bugun = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(simdi, saatDilimi).DateTime);
        var gun = DateOnly.FromDateTime(yerel);
        if (fark < TimeSpan.FromHours(24) && gun == bugun) return $"{(int)fark.TotalHours} saat önce";
        var gunFarki = bugun.DayNumber - gun.DayNumber;
        if (gunFarki <= 1) return gunFarki == 1 ? "dün" : $"{(int)fark.TotalHours} saat önce";
        if (gunFarki < 7) return $"{gunFarki} gün önce";
        return yerel.ToString("d MMMM yyyy", Kultur.Turkce);
    }
}
