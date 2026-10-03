using System.Text.Json;

namespace Kasa.App.Core;

/// <summary>
/// Formda kaydedilmemiş değişiklik var mı (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §2): form açıldığı andaki
/// değerlerden farklıysa vardır. <paramref name="durum"/> formun karşılaştırılan değerlerini verir (anonim nesne ya da kayıt; JSON
/// ile karşılaştırılır). Form açılınca (yeni, düzenleme, kaydedildi) <see cref="Ac"/>, kapanınca <see cref="Kapat"/> çağrılır;
/// kapalı formda değişiklik sayılmaz.
/// </summary>
public sealed class KaydedilmemisDegisiklik(Func<object?> durum)
{
    public const string Baslik = "Kaydedilmemiş değişiklik";
    public const string Ileti = "Kaydedilmemiş değişiklik var. Bırakılsın mı?";
    public const string Birak = "Bırak";
    public const string FormaDon = "Forma dön";

    private string? _acilis;

    /// <summary>Form bu değerlerle açıldı: şimdiki değerler karşılaştırmanın tabanıdır.</summary>
    public void Ac() => _acilis = Anlik();

    /// <summary>Form kapandı: değişiklik sayılmaz.</summary>
    public void Kapat() => _acilis = null;

    public bool Acik => _acilis is not null;

    /// <summary>Form açık ve değerleri açıldığı andakinden farklı.</summary>
    public bool Var => _acilis is not null && Anlik() != _acilis;

    private string Anlik() => JsonSerializer.Serialize(durum());
}

/// <summary>Kaydedilmemiş değişikliği olabilen ekran modeli: kabuk sayfadan çıkışta onay sorar ("Bırak" seçilirse
/// <see cref="DegisiklikleriBirak"/>).</summary>
public interface IKaydedilmemisForm
{
    bool KaydedilmemisDegisiklikVar { get; }

    /// <summary>Yazılmış değişiklikleri bırakır: form açıldığı hale döner ya da kapanır.</summary>
    void DegisiklikleriBirak();
}
