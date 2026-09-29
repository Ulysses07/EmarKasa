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
            VeriHazir = false;
            Mesgul = false;
            Hata = null;
            Mesaj = null;
            SonGuncelleme = null;
            OturumTemizle();
            OnPropertyChanged(nameof(EditorMu));
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
