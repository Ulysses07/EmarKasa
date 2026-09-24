namespace Kasa.App.Core;

/// <summary>
/// Süreç boyunca yalnız bir kez yapılabilen iş. Örnek: Windows bildirim kaydı
/// (<c>AppNotificationManager.Register()</c>) aynı süreçte ikinci kez çağrılırsa "Already Registered" hatası
/// atar; arka plan hatırlatıcısında kart hatırlatması ve Paket A bildirimleri ayrı servis nesneleriyle
/// kaydolduğundan kayıt bunun üzerinden yapılır. Eylem hata atarsa yapılmış sayılmaz (sonraki çağrı yeniden
/// dener). İş parçacığı güvenlidir: aynı anda gelen çağrılardan yalnız biri çalıştırır.
/// </summary>
public sealed class TekSeferlik
{
    private readonly object _kilit = new();
    private bool _yapildi;

    public bool Yapildi
    {
        get { lock (_kilit) return _yapildi; }
    }

    /// <returns>Eylem bu çağrıda çalıştıysa true; daha önce yapılmışsa false.</returns>
    public bool Calistir(Action eylem)
    {
        lock (_kilit)
        {
            if (_yapildi) return false;
            eylem();
            _yapildi = true;
            return true;
        }
    }
}
