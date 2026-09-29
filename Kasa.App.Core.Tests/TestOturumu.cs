namespace Kasa.App.Core.Tests;

/// <summary>Testlerin oturumu: oturumlu ekranlar (<see cref="OturumluViewModel"/>) rolü ve oturumu <see cref="AuthViewModel"/>'den
/// okur; sayfa ya da test rolü ekrana ayrıca atamaz.</summary>
internal static class TestOturumu
{
    /// <summary>Verilen rolle açılmış oturum (varsayılan editör).</summary>
    public static AuthViewModel Ac(Rol rol = Rol.Editor) => new(new SahteApi()) { AktifRol = rol };

    /// <summary>Verilen rolle yeni oturum: <see cref="AuthViewModel"/> girişteki gibi önce rol, sonra oturum sürümü değişir.</summary>
    public static void YeniOturum(AuthViewModel auth, Rol rol)
    {
        auth.AktifRol = rol;
        auth.OturumSurumu++;
    }
}
