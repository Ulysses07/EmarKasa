using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core;

/// <summary>Oturuma bağlı ekran: oturum değişince yürütücünün nesli artar (bekleyen işler eskir) ve ekran sıfırlanır.
/// Yürütme deseni tabandaki <see cref="Yurutucu"/>'dur.</summary>
public abstract partial class OturumluViewModel : TemelViewModel
{
    protected readonly AuthViewModel Auth;
    protected OturumluViewModel(AuthViewModel auth)
    {
        Auth = auth;
        OturumDegisiminiDinle(auth, () =>
        {
            VeriHazir = false; Mesgul = false; Hata = null; Mesaj = null; SonGuncelleme = null; OturumTemizle(); OnPropertyChanged(nameof(EditorMu));
        });
    }
    public bool EditorMu => Auth.AktifRol == Rol.Editor;
    public int OturumNesli => Yurutucu.Nesil;
    [ObservableProperty] private bool _veriHazir;
    [ObservableProperty] private string? _mesaj;
    [ObservableProperty] private DateTime? _sonGuncelleme;
    protected override void IletiyiTemizle() => Mesaj = null;
    protected void BekleyenleriIptalEt() { Yurutucu.GecersizKil(); Mesgul = false; }
    protected abstract void OturumTemizle();
    protected void Tamamlandi() { VeriHazir = true; SonGuncelleme = DateTime.Now; }
}

public sealed class TekrarAnahtari
{
    private string? _govde;
    private Guid _id;
    public Guid Al(object govde)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(govde);
        if (_govde != json) { _govde = json; _id = Guid.NewGuid(); }
        return _id;
    }
    public void Temizle() { _govde = null; _id = Guid.Empty; }
}

/// <summary>Kayıt (ör. kart) başına tekrar anahtarı: bir kaydın yanıtı belirsiz kalan isteğinin anahtarı başka kayıtta yapılan
/// işlemlerle ezilmez; aynı kayda dönülüp aynı gövde yeniden gönderilince aynı anahtar kullanılır (sunucu ikinci kez işlemez).
/// Gövde kaydın kimliğini de taşıdığından başka kaydın isteği hiçbir zaman bu anahtarı almaz.</summary>
public sealed class KayitBasinaTekrarAnahtari
{
    private readonly Dictionary<int, TekrarAnahtari> _kayitlar = new();
    public Guid Al(int kayitId, object govde)
    {
        if (!_kayitlar.TryGetValue(kayitId, out var anahtar)) _kayitlar[kayitId] = anahtar = new();
        return anahtar.Al(govde);
    }
    public void Temizle(int kayitId) => _kayitlar.Remove(kayitId);
    public void Temizle() => _kayitlar.Clear();
}
