namespace Kasa.App.Core;

public enum Rol { Izleyici, Editor, Alici }

public enum Bolum { Panel, Haftalik, Aylik, Islemler, Ayarlar, Alislar, DisariAktar, Kartlar, Krediler, Bildirimler, AylikGiderler, EkstreAktar, Cekler, Cariler, KasaKontrolu }

/// <summary>Rol → görünür bölümler (spec §6). Ayarlar yalnız editörde. Kasa kontrolü izleyiciye de açıktır: geçmiş, alt limit
/// uyarıları ve hareket dökümü sunucuda okuma yetkisiyle (Finans) gelir; karşılaştırma ve açıklama yalnız editörde görünür.</summary>
public static class SekmeModeli
{
    public static Rol RolCoz(string? rol)
        => rol?.ToLowerInvariant() switch { "editor" => Rol.Editor, "alici" => Rol.Alici, _ => Rol.Izleyici };

    public static IReadOnlyList<Bolum> Bolumler(Rol rol)
    {
        if (rol == Rol.Alici)
            return new[] { Bolum.Alislar };
        var liste = new List<Bolum>
        {
            Bolum.Panel, Bolum.KasaKontrolu, Bolum.Haftalik, Bolum.Aylik,
            Bolum.Islemler, Bolum.AylikGiderler, Bolum.Kartlar, Bolum.Krediler, Bolum.Cekler, Bolum.Cariler, Bolum.DisariAktar,
        };
        if (rol == Rol.Editor)
        { liste.Add(Bolum.Alislar); liste.Add(Bolum.Bildirimler); liste.Add(Bolum.EkstreAktar); liste.Add(Bolum.Ayarlar); }
        return liste;
    }
}
