using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kasa.App.Core;

/// <summary>
/// Formda kaydedilmemiş değişiklik var mı (docs/specs/2026-10-02-masaustu-form-hatalari-ve-baglanti.md §2): form açıldığı andaki
/// değerlerden farklıysa vardır. <paramref name="durum"/> formun karşılaştırılan değerlerini verir (anonim nesne ya da kayıt; JSON
/// ile karşılaştırılır; ondalık (<see cref="decimal"/>/<see cref="decimal"/>?) alanlar ölçekten (kuyruk sıfırları) bağımsız
/// karşılaştırılır (K-7): 150.00m ile 150m aynı sayılır). Form açılınca (yeni, düzenleme, kaydedildi) <see cref="Ac"/>, kapanınca
/// <see cref="Kapat"/> çağrılır; kapalı formda değişiklik sayılmaz.
/// </summary>
public sealed class KaydedilmemisDegisiklik(Func<object?> durum)
{
    public const string Baslik = "Kaydedilmemiş değişiklik";
    public const string Ileti = "Kaydedilmemiş değişiklik var. Bırakılsın mı?";
    public const string Birak = "Bırak";
    public const string FormaDon = "Forma dön";

    /// <summary>Ondalık değerleri ölçekten (kuyruk sıfırları) bağımsız yazar: "150.00" ve "150" aynı JSON metnini üretir
    /// (K-7). <c>value / 1.000000000000000000000000000m</c> deseni decimal bölmenin sonucu en küçük ölçeğe indirmesinden
    /// yararlanır (bilinen .NET deseni).</summary>
    private sealed class OndalikOlcekBagimsizConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value / 1.000000000000000000000000000m);
    }

    /// <summary>Boş ya da yalnız boşluktan oluşan metni <c>null</c> gibi yazar: alana yazılıp silinen metin ("") formu kirli
    /// bırakmaz, çünkü açılıştaki değer de genelde <c>null</c>'dur ve ikisi aynı JSON metnini üretir.</summary>
    private sealed class BosMetinNullConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (string.IsNullOrWhiteSpace(value))
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value);
        }
    }

    private static readonly JsonSerializerOptions Secenekler = new() { Converters = { new OndalikOlcekBagimsizConverter(), new BosMetinNullConverter() } };

    private string? _acilis;

    /// <summary>Form bu değerlerle açıldı: şimdiki değerler karşılaştırmanın tabanıdır.</summary>
    public void Ac() => _acilis = Anlik();

    /// <summary>Form kapandı: değişiklik sayılmaz.</summary>
    public void Kapat() => _acilis = null;

    public bool Acik => _acilis is not null;

    /// <summary>Form açık ve değerleri açıldığı andakinden farklı.</summary>
    public bool Var => _acilis is not null && Anlik() != _acilis;

    private string Anlik() => JsonSerializer.Serialize(durum(), Secenekler);
}

/// <summary>Kaydedilmemiş değişikliği olabilen ekran modeli: kabuk sayfadan çıkışta onay sorar ("Bırak" seçilirse
/// <see cref="DegisiklikleriBirak"/>).</summary>
public interface IKaydedilmemisForm
{
    bool KaydedilmemisDegisiklikVar { get; }

    /// <summary>Sayfanın yenilemesi (Yenile, kabuğun "Yeniden dene"si ve otomatik yenileme) açık formu koruyor mu (Ö-4): Kartlar,
    /// Krediler, Çekler ve Aylık giderler korur, kabuk sormadan yeniler; İşlemler ve Alışlar korumaz, kabuk kirli formda sorar ve
    /// otomatik yenileme yapmaz.</summary>
    bool YenilemeFormuKorur { get; }

    /// <summary>Yazılmış değişiklikleri bırakır: form açıldığı hale döner ya da kapanır.</summary>
    void DegisiklikleriBirak();
}
