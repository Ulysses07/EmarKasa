// Alan kodları: API sözleşmesinde ve veritabanında taşınan sabit dizeler; sunucu (Kasa.Api), hesap motoru ve masaüstü
// görünüm modelleri (Kasa.App.Core) aynı sabiti kullanır, dize elle kopyalanmaz. Değerler sözleşme ve veritabanı değeridir:
// değiştirmek veri dönüşümü ve istemci uyumu gerektirir.
//
// Ayrı isim alanındadır: Kasa.App.Core hem Kasa.Core'a hem Kasa.ApiClient'a başvurur ve iki derlemede de GiderTipi vardır
// (Kasa.Core.GiderTipi, Kasa.ApiClient.GiderTipi). "using Kasa.Core;" ile "using Kasa.ApiClient;" birlikteyken GiderTipi'yi
// kullanan dosya CS0104 (belirsiz başvuru) verir; "using Kasa.Core.Kodlar;" yalnız bu sabitleri getirir. Buradaki tür adları
// Kasa.ApiClient ve Kasa.App.Core türleriyle çakışmaz (Kasa.Sozlesme.Tests/MimariTests).
namespace Kasa.Core.Kodlar;

/// <summary>Gerçek kanal olmayan kanal etiketleri (<see cref="Islem.Kanal"/>, rapor ve döküm satırlarının kanal alanı). Gerçek
/// kanallar kullanıcı tanımlıdır (KanalEntity); bu etiketler kanal adı olarak kullanılamaz. Kredi çekiminin iç etiketi
/// <see cref="KrediTuretici.KrediKanal"/>'dır.</summary>
public static class KanalEtiketleri
{
    /// <summary>Belirli bir kanala ait olmayan ortak gider: aylık raporda o ayın Ortak kümesine dağıtılır.</summary>
    public const string Ortak = "Ortak";
    /// <summary>Gerçek giderin kanalı henüz kesinleşmedi; ortak paya dağıtılmaz, kasadan düşer.</summary>
    public const string DagilimBekliyor = "Dağılım bekliyor";
    /// <summary>Yalnız genel kasayı etkileyen kaydın etiketi (kanal payı olmayan gider ve gelir, genel kasa dökümü).</summary>
    public const string GenelKasa = "Genel kasa";
}

/// <summary>Alış kaydının iş akışı durumu (AlisEntity.Durum, AlisDto.Durum).</summary>
public static class AlisDurumlari
{
    public const string Taslak = "Taslak";
    public const string Incelemede = "Incelemede";
    public const string Onaylandi = "Onaylandi";
}
