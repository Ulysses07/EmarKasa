namespace Kasa.ApiClient;

/// <summary>Geçersiz oturumun istemciye bildirilmesi; UI bağımlılığı içermez.</summary>
public interface IOturumBildirimleri
{
    event EventHandler? OturumSonlandi;
}
