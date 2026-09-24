using Kasa.ApiClient;

namespace Kasa.App.Core;

// Paket C: işlem formunun alan değişim kancaları (tek yerde; özellikler buradan dağıtılır).
// Ana dosyadaki tek-parametreli OnXChanged kancalarına dokunulmaz; burada eski/yeni değerli
// aşırı yüklemeler kullanılır.
public partial class IslemlerViewModel
{
    /// <summary>Form alanları programla (Yeni, Düzenle, Kopyala, öneri, seri giriş) değiştiriliyor.</summary>
    private bool _programatik;

    /// <summary>Kullanıcı kanalı / tipi bu girişte kendisi seçti: cari önerisi bunları ezmez.</summary>
    private bool _kanalElle, _tipElle;

    partial void OnDuzenCariChanged(string? oldValue, string newValue)
    {
        UyarilariTemizle();
        BenzerCariKapat();
        OnPropertyChanged(nameof(CariEklenebilir));
        // Yalnız harf büyüklüğü değiştiyse (kaydederken kayıtlı yazıma çevirme) öneri yeniden sorulmaz.
        if (string.Compare(oldValue?.Trim(), newValue?.Trim(), Kultur.Turkce, System.Globalization.CompareOptions.IgnoreCase) == 0) return;
        CariOnerisiMetni = null;
        if (!_programatik) _ = CariOnerisiUygulaAsync(newValue ?? "");
        else _oneriSurumu++;   // programla değişti: bekleyen öneri artık geçersiz
    }

    partial void OnDuzenTarihChanged(DateTime oldValue, DateTime newValue) => UyarilariTemizle();

    partial void OnDuzenTutarChanged(decimal oldValue, decimal newValue) => UyarilariTemizle();

    partial void OnDuzenKanalChanged(string? oldValue, string newValue)
    {
        UyarilariTemizle();
        if (!_programatik) _kanalElle = newValue.Length > 0;
    }

    partial void OnDuzenTipChanged(GiderTipi oldValue, GiderTipi newValue)
    {
        UyarilariTemizle();
        OnPropertyChanged(nameof(CariEklenebilir));
        if (!_programatik) _tipElle = true;
    }

    partial void OnDuzenKrediKartiIdChanged(int? oldValue, int? newValue) => UyarilariTemizle();

    partial void OnEditorMuChanged(bool oldValue, bool newValue) => OnPropertyChanged(nameof(CariEklenebilir));

    /// <summary>Form sıfırlandı ya da bir kayıt düzenlemeye alındı (ana dosyadaki Yeni/Düzenle sonunda çağrılır).</summary>
    private void FormSifirlandi()
    {
        _kanalElle = false;
        _tipElle = false;
        UyarilariTemizle();
        BenzerCariKapat();
        CariOnerisiMetni = null;
    }

    /// <summary>Alanları öneri/seri akışında kullanıcı seçimi saymadan değiştirir.</summary>
    private void Programla(Action degisiklik)
    {
        var onceki = _programatik;
        _programatik = true;
        try { degisiklik(); }
        finally { _programatik = onceki; }
    }
}
