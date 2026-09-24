namespace Kasa.Api.Data;

/// <summary>
/// Değişiklik geçmişinde varlığın Türkçe adı (<see cref="Tur"/>) ve özet satırında gösterilecek
/// alanlar. Özniteliği olmayan yeni bir entity de geçmişe yazılır: tür adı sınıf adından türetilir
/// ve ilk birkaç alanı özetlenir (bkz. <see cref="DegisiklikKaydedici"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GecmisAttribute(string tur, params string[] ozetAlanlari) : Attribute
{
    public string Tur { get; } = tur;
    public string[] OzetAlanlari { get; } = ozetAlanlari;
}

/// <summary>Bu varlığın değişiklikleri geçmişe yazılmaz (geçmişin kendisi, çıkışta iptal edilen token'lar).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GecmisDisiAttribute : Attribute;

/// <summary>
/// Gizli alan: değeri geçmişe (özet ve JSON dahil) hiç yazılmaz. Değişince yalnız
/// <see cref="Mesaj"/> (varsa) özete girer. Adında Sifre/Hash/Token geçen alanlar
/// öznitelik olmasa da gizli sayılır.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GizliAttribute(string? mesaj = null) : Attribute
{
    public string? Mesaj { get; } = mesaj;
}

/// <summary>Geçmişteki tür adları (geri almada türe göre dallanmak için sabit).</summary>
public static class GecmisTurleri
{
    public const string Islem = "İşlem";
    public const string Gelen = "Gelen";
    public const string Cari = "Cari";
    public const string Kanal = "Kanal";
    public const string KrediKarti = "Kredi kartı";
    public const string KartOdemesi = "Kart ödemesi";
    public const string GiderKalemi = "Gider kalemi";
    public const string Cek = "Çek";
    public const string KasaSayimi = "Kasa sayımı";
    public const string Ayar = "Ayar";
}

/// <summary>Geçmiş satırının eylemi.</summary>
public static class Eylemler
{
    public const string Eklendi = "Eklendi";
    public const string Guncellendi = "Güncellendi";
    public const string Silindi = "Silindi";
    /// <summary>Silinen bir kaydın geçmişten geri getirilmesi.</summary>
    public const string GeriAlindi = "Eklendi (geri alındı)";
}

public static class GecmisKurallari
{
    /// <summary>Bu süreden eski geçmiş satırları açılışta silinir.</summary>
    public const int SaklamaYili = 2;

    /// <summary>Silme en fazla bu kadar süre sonra geri alınabilir.</summary>
    public static readonly TimeSpan GeriAlmaSuresi = TimeSpan.FromDays(30);

    /// <summary>Silindiğinde geri alınabilen türler.</summary>
    public static readonly IReadOnlySet<string> GeriAlinabilirTurler = new HashSet<string>
    {
        GecmisTurleri.Islem, GecmisTurleri.Gelen, GecmisTurleri.KartOdemesi, GecmisTurleri.Cari, GecmisTurleri.GiderKalemi,
        GecmisTurleri.Cek, GecmisTurleri.KasaSayimi,
    };

    /// <summary>Satır geri alınamıyorsa nedeni (Türkçe), alınabiliyorsa null.</summary>
    public static string? GeriAlmaEngeli(DegisiklikEntity d, DateTime simdiUtc)
    {
        if (d.GeriAlindi) return "Bu silme zaten geri alındı.";
        if (d.Eylem != Eylemler.Silindi) return "Yalnız silinen kayıtlar geri alınabilir.";
        if (!GeriAlinabilirTurler.Contains(d.Tur)) return $"{d.Tur} kayıtları geri alınamaz.";
        if (string.IsNullOrEmpty(d.EskiJson)) return "Kaydın eski hali yok; geri alınamaz.";
        if (simdiUtc - d.ZamanUtc > GeriAlmaSuresi) return $"{GeriAlmaSuresi.TotalDays:0} günden eski silmeler geri alınamaz.";
        return null;
    }
}
