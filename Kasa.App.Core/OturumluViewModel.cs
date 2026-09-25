using CommunityToolkit.Mvvm.ComponentModel;

namespace Kasa.App.Core;

public abstract partial class OturumluViewModel : TemelViewModel
{
    private int _nesil;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    protected readonly AuthViewModel Auth;
    protected OturumluViewModel(AuthViewModel auth)
    {
        Auth = auth;
        auth.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AuthViewModel.OturumSurumu)) return;
            Interlocked.Increment(ref _nesil);
            void Sifirla() { VeriHazir = false; Mesgul = false; Hata = null; Mesaj = null; SonGuncelleme = null; OturumTemizle(); OnPropertyChanged(nameof(EditorMu)); }
            if (_ui is not null && SynchronizationContext.Current != _ui) _ui.Post(_ => Sifirla(), null); else Sifirla();
        };
    }
    public bool EditorMu => Auth.AktifRol == Rol.Editor;
    public int OturumNesli => Volatile.Read(ref _nesil);
    [ObservableProperty] private bool _veriHazir;
    [ObservableProperty] private string? _mesaj;
    [ObservableProperty] private DateTime? _sonGuncelleme;
    protected bool Gecerli(int nesil) => nesil == Volatile.Read(ref _nesil);
    protected void BekleyenleriIptalEt() { Interlocked.Increment(ref _nesil); Mesgul = false; }
    protected abstract void OturumTemizle();
    protected async Task YurutAsync(Func<int, Task> islem)
    {
        if (Mesgul) return;
        var nesil = Volatile.Read(ref _nesil);
        Mesgul = true; Hata = null; Mesaj = null;
        try { await islem(nesil); }
        catch (Exception e) { if (Gecerli(nesil)) Hata = HataMesaji(e); }
        finally { if (Gecerli(nesil)) Mesgul = false; }
    }
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
