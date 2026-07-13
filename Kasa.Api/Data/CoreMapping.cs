using Kasa.Core;

namespace Kasa.Api.Data;

/// <summary>EF entity'lerini hesap motorunun beklediği Core record'larına eşler.</summary>
public static class CoreMapping
{
    public static Kanal ToCore(this KanalEntity e) => new(e.Ad, e.AcilisDevri, e.Aktif, e.Sira);
    public static Cari ToCore(this CariEntity e) => new(e.Ad, e.Aktif);
    public static Islem ToCore(this IslemEntity e) => new(e.Tarih, e.Cari, e.TutarTl, e.Kanal, e.Tip, e.Not);
    public static Gelen ToCore(this GelenEntity e) => new(e.DonemStart, e.Kanal, e.TutarTl);
}
