using System.ComponentModel;

namespace Kasa.App.Core;

/// <summary>
/// Bir formun hataları (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §1): alan → ileti ve formun genel hatası
/// (<see cref="Genel"/>, "FormHatasi"). Alan adı formun görünüm modelindeki özelliğin adıdır ("DuzenCari"); görünüm
/// <c>Hatalar[DuzenCari]</c> diye bağlar. Değişiklik dizinleyici için "Item[alan]" (hepsi silinince ayrıca "Item"), genel hata
/// için <see cref="Genel"/> ve <see cref="Var"/> adıyla bildirilir. Hatalar konuldukları sırayı korur: kaydırma ilk hatalı alana
/// gider (<see cref="IlkAlan"/>).
/// </summary>
public sealed class AlanHatalari : INotifyPropertyChanged
{
    private readonly Dictionary<string, string> _hatalar = new(StringComparer.Ordinal);
    private readonly List<string> _sira = [];
    private string? _genel;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Kaydetme bittiğinde hata varsa görünüm ilk hatalı alana (yoksa genel hata kutusuna) kaydırır ve odaklanır.</summary>
    public event EventHandler? GosterIstendi;

    /// <summary>Alanın iletisi; hata yoksa null.</summary>
    public string? this[string alan] => _hatalar.GetValueOrDefault(alan);

    /// <summary>Formun genel hatası: alana eşlenemeyen sunucu iletisi, bağlantı ve sunucu hatası. Formun en üstünde gösterilir.</summary>
    public string? Genel
    {
        get => _genel;
        set
        {
            var yeni = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_genel == yeni)
                return;
            var onceVar = Var;
            _genel = yeni;
            Bildir(nameof(Genel));
            if (onceVar != Var)
                Bildir(nameof(Var));
        }
    }

    /// <summary>Alan ya da genel hata var mı.</summary>
    public bool Var => _genel is not null || _hatalar.Count > 0;

    /// <summary>Hatalı alanlar, konuldukları sırayla.</summary>
    public IReadOnlyList<string> Alanlar => _sira;

    /// <summary>İlk hatalı alan; yoksa null.</summary>
    public string? IlkAlan => _sira.Count > 0 ? _sira[0] : null;

    public void Ayarla(string alan, string ileti)
    {
        var onceVar = Var;
        if (!_hatalar.ContainsKey(alan))
            _sira.Add(alan);
        _hatalar[alan] = ileti;
        Bildir($"Item[{alan}]");
        if (onceVar != Var)
            Bildir(nameof(Var));
    }

    /// <summary>Ön doğrulama: koşul tutmuyorsa alana iletiyi yazar (alanda önceki hata varsa korunur: ilk kural kazanır).</summary>
    /// <returns><paramref name="gecerli"/>.</returns>
    public bool Denetle(bool gecerli, string alan, string ileti)
    {
        if (!gecerli && !_hatalar.ContainsKey(alan))
            Ayarla(alan, ileti);
        return gecerli;
    }

    /// <summary>Alanın hatasını kaldırır (kullanıcı alanı değiştirdi); hata yoksa bir şey yapmaz.</summary>
    public void Temizle(string? alan)
    {
        if (alan is null || !_hatalar.Remove(alan))
            return;
        _sira.Remove(alan);
        Bildir($"Item[{alan}]");
        if (!Var)
            Bildir(nameof(Var));
    }

    /// <summary>Bütün alan hatalarını ve genel hatayı kaldırır (kayıt değişti, Yeni, Vazgeç, başarılı kayıt).</summary>
    public void Temizle()
    {
        if (!Var)
            return;
        var alanlar = _sira.ToList();
        _hatalar.Clear();
        _sira.Clear();
        _genel = null;
        foreach (var alan in alanlar)
            Bildir($"Item[{alan}]");
        Bildir("Item");
        Bildir(nameof(Genel));
        Bildir(nameof(Var));
    }

    /// <summary>Sunucunun alan hatalarını (küçük harf sunucu alan adı → ileti) <paramref name="eslem"/> ile formun alanlarına yazar.</summary>
    /// <returns>Formun alanına eşlenemeyen iletiler (genel hataya gider), sırayla ve her biri bir kez.</returns>
    public IReadOnlyList<string> SunucuHatalariniYaz(IReadOnlyDictionary<string, string> sunucu, IReadOnlyDictionary<string, string> eslem)
    {
        var eslenmeyen = new List<string>();
        foreach (var (sunucuAlani, ileti) in sunucu)
        {
            if (eslem.TryGetValue(sunucuAlani, out var alan))
                Ayarla(alan, ileti);
            else if (!eslenmeyen.Contains(ileti))
                eslenmeyen.Add(ileti);
        }
        return eslenmeyen;
    }

    /// <summary>Görünümden hataya kaydırmasını ister (<see cref="GosterIstendi"/>); hata yoksa bir şey yapmaz.</summary>
    public void GosterIste()
    {
        if (Var)
            GosterIstendi?.Invoke(this, EventArgs.Empty);
    }

    private void Bildir(string ad) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(ad));
}
