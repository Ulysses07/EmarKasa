namespace Kasa.App.Core;

public enum Rol { Izleyici, Editor }

public enum Bolum { Panel, Haftalik, Aylik, Cariler, Islemler, KrediKartlari, Ayarlar }

/// <summary>Rol → görünür bölümler (spec §6). Ayarlar yalnız editörde.</summary>
public static class SekmeModeli
{
    public static Rol RolCoz(string? rol)
        => string.Equals(rol, "editor", StringComparison.OrdinalIgnoreCase) ? Rol.Editor : Rol.Izleyici;

    public static IReadOnlyList<Bolum> Bolumler(Rol rol)
    {
        var liste = new List<Bolum>
        {
            Bolum.Panel, Bolum.Haftalik, Bolum.Aylik,
            Bolum.Cariler, Bolum.Islemler, Bolum.KrediKartlari,
        };
        if (rol == Rol.Editor) liste.Add(Bolum.Ayarlar);
        return liste;
    }
}
