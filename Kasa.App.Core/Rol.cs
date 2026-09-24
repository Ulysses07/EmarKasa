namespace Kasa.App.Core;

public enum Rol { Izleyici, Editor }

public enum Bolum { Panel, KasaSayimi, Haftalik, Aylik, Cariler, Islemler, KrediKartlari, Cekler, Gecmis, Ayarlar }

/// <summary>Rol → görünür bölümler (spec §6). Ayarlar yalnız editörde; Kasa Sayımı, Çekler ve Geçmiş her iki rolde (izleyici yalnız görür, geri alma yalnız editörde).</summary>
public static class SekmeModeli
{
    public static Rol RolCoz(string? rol)
        => string.Equals(rol, "editor", StringComparison.OrdinalIgnoreCase) ? Rol.Editor : Rol.Izleyici;

    public static IReadOnlyList<Bolum> Bolumler(Rol rol)
    {
        var liste = new List<Bolum>
        {
            Bolum.Panel, Bolum.KasaSayimi, Bolum.Haftalik, Bolum.Aylik,
            Bolum.Cariler, Bolum.Islemler, Bolum.KrediKartlari, Bolum.Cekler,
            Bolum.Gecmis,
        };
        if (rol == Rol.Editor) liste.Add(Bolum.Ayarlar);
        return liste;
    }
}
