using CommunityToolkit.Mvvm.ComponentModel;
using Kasa.ApiClient;

namespace Kasa.App.Core;

/// <summary>Ortak Mesgul + Hata durumu ve tek yürütme deseni (Yurutucu, appcore-10).</summary>
public partial class TemelViewModel : ObservableObject, IYurutmeYuzeyi
{
    [ObservableProperty] private bool _mesgul;
    [ObservableProperty] private string? _hata;

    public TemelViewModel() => Yurutucu = new Yurutucu(this);

    /// <summary>Ekranın yürütücüsü: tekil işlemler, son istek hatları ve oturum nesli aynı yerden yönetilir.</summary>
    protected Yurutucu Yurutucu { get; }

    /// <summary>Tekil işlem (<see cref="Yurutucu.YurutAsync"/>): sürerken ikincisi başlamaz; eskiyen işin hatası ve bitişi yansımaz.</summary>
    protected Task YurutAsync(Func<int, Task> islem, bool mesgulkenBildir = false) => Yurutucu.YurutAsync(islem, mesgulkenBildir);
    protected bool Gecerli(int nesil) => Yurutucu.Gecerli(nesil);

    /// <summary>Yeni işlem başlarken önceki başarı iletisi kalkar; Mesaj taşıyan model geçersiz kılar.</summary>
    protected virtual void IletiyiTemizle() { }
    void IYurutmeYuzeyi.IletiyiTemizle() => IletiyiTemizle();

    /// <summary>Nesil kullanmayan işlem için <see cref="YurutAsync"/> kısayolu (AyarlarViewModel): ayrı bir desen değil, aynı tekil
    /// işlemdir (yeniden giriş koruması, başlarken Hata ve ileti temizliği, eskiyen işin hatası ve bitişi yansımaz). Yeni ekranlar
    /// sonucu nesille denetleyen <see cref="YurutAsync"/> kullanır.</summary>
    protected Task CalistirAsync(Func<Task> islem) => Yurutucu.YurutAsync(_ => islem());

    /// <summary>Yazma ve tekil işlem hatasının iletisi; bkz. <see cref="Yurutucu.HataMesaji"/>.</summary>
    protected static string HataMesaji(Exception hata) => Yurutucu.HataMesaji(hata);

    /// <summary>Sunucu hatasının (5xx) iz kimliği varsa iletiye kısa "Hata kodu" eklenir (web errorMessage ile aynı biçim):
    /// kullanıcı yöneticiye bildirir, yönetici sunucu logundaki tam iz kimliğini bu parçayla bulur.</summary>
    public static string HataKoduEkle(string mesaj, KasaApiException hata)
        => hata.HataKodu is { } kod && (int)hata.DurumKodu >= 500 ? $"{mesaj} Hata kodu: {kod}" : mesaj;

    /// <summary>Salt okuma çağrısının (liste, rapor) hata iletisi; bkz. <see cref="Yurutucu.OkumaHataMesaji"/>.</summary>
    protected static string OkumaHataMesaji(Exception hata) => Yurutucu.OkumaHataMesaji(hata);
}
