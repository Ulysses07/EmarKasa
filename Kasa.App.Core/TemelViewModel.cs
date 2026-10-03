using System.ComponentModel;
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
    protected Task YurutAsync(Func<int, Task> islem, bool mesgulkenBildir = false, Action<Exception>? hataIsle = null)
        => Yurutucu.YurutAsync(islem, mesgulkenBildir, hataIsle);
    protected bool Gecerli(int nesil) => Yurutucu.Gecerli(nesil);

    /// <summary>Yeni işlem başlarken önceki başarı iletisi kalkar; Mesaj taşıyan model geçersiz kılar.</summary>
    protected virtual void IletiyiTemizle() { }
    void IYurutmeYuzeyi.IletiyiTemizle() => IletiyiTemizle();

    /// <summary>Yazma ve tekil işlem hatasının iletisi; bkz. <see cref="Yurutucu.HataMesaji"/>.</summary>
    protected static string HataMesaji(Exception hata) => Yurutucu.HataMesaji(hata);

    /// <summary>Sunucu hatasının (5xx) iz kimliği varsa iletiye kısa "Hata kodu" eklenir (web errorMessage ile aynı biçim):
    /// kullanıcı yöneticiye bildirir, yönetici sunucu logundaki tam iz kimliğini bu parçayla bulur.</summary>
    public static string HataKoduEkle(string mesaj, KasaApiException hata)
        => hata.HataKodu is { } kod && (int)hata.DurumKodu >= 500 ? $"{mesaj} Hata kodu: {kod}" : mesaj;

    /// <summary>Salt okuma çağrısının (liste, rapor) hata iletisi; bkz. <see cref="Yurutucu.OkumaHataMesaji"/>.</summary>
    protected static string OkumaHataMesaji(Exception hata) => Yurutucu.OkumaHataMesaji(hata);

    /// <summary>Uygulamanın bağlantısı kopuk mu; oturumlu ve rapor modelleri BaglantiDurumu'ndan okur.</summary>
    protected virtual bool BaglantiKopuk => false;
    bool IYurutmeYuzeyi.BaglantiKopuk => BaglantiKopuk;

    /// <summary>Modelin formlarının hataları: bir özellik değişince her formda o adlı alanın hatası kalkar (kullanıcı alanı düzeltti).</summary>
    protected virtual IEnumerable<AlanHatalari> Formlar => [];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        foreach (var form in Formlar)
            form.Temizle(e.PropertyName);
    }

    /// <summary>Formun kaydı (tasarım §1): tekil işlemdir; başlarken formun hataları kalkar. İşlem ön doğrulamada formun alanlarına
    /// yazıp dönebilir (istek gönderilmez). Hata Hata'ya değil forma yazılır (<see cref="FormHatasiniYaz"/>). Bittiğinde formda hata
    /// varsa görünümden ilk hatalı alana kaydırması istenir.</summary>
    /// <param name="eslem">Sunucu alan adı (küçük harf) → formun alanı; verilmezse sunucunun alan hataları genel hataya gider.</param>
    protected Task FormIsleAsync(AlanHatalari form, Func<int, Task> islem, IReadOnlyDictionary<string, string>? eslem = null)
        => YurutAsync(async n =>
        {
            form.Temizle();
            await islem(n);
            if (Gecerli(n))
                form.GosterIste();
        }, hataIsle: hata =>
        {
            FormHatasiniYaz(form, hata, eslem);
            form.GosterIste();
        });

    /// <summary>Kayıt hatasını forma yazar: sunucunun alan hataları eşlenen alanlara, eşlenemeyenler genel hataya; istek sunucuya
    /// ulaşamadıysa <see cref="Yurutucu.KayitBaglantiIletisi"/>; diğerleri <see cref="Yurutucu.HataMesaji"/> ile genel hataya.
    /// Zaman aşımı bağlantı iletisi almaz: istek sunucuya ulaşmış ve kayıt tamamlanmış olabilir.</summary>
    public static void FormHatasiniYaz(AlanHatalari form, Exception hata, IReadOnlyDictionary<string, string>? eslem = null)
    {
        if (hata is KasaApiException { AlanHatalari.Count: > 0 } api && eslem is not null)
        {
            var kalan = form.SunucuHatalariniYaz(api.AlanHatalari, eslem);
            form.Genel = kalan.Count > 0 ? string.Join("\n", kalan) : null;
            return;
        }
        form.Genel = hata is HttpRequestException ? Yurutucu.KayitBaglantiIletisi : HataMesaji(hata);
    }
}
