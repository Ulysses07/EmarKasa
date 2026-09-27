namespace Kasa.ApiClient;

/// <summary>İstek başına süre sınırları. Normal çağrılar kısa tutulur; dosya yükleme, dosya indirme ve yedek
/// (sunucuda hazırlık + indirme) uzun sürebilir. Sınır yanıt gövdesinin okunmasını da kapsar; HttpClient.Timeout
/// bu sınırları kesmesin diye istemcide sonsuz bırakılır (MauiProgram).</summary>
public sealed record KasaZamanAsimlari(TimeSpan Varsayilan, TimeSpan Yukleme, TimeSpan Indirme, TimeSpan Yedek)
{
    /// <summary>Normal 15 sn, yükleme 2 dk, indirme 5 dk, yedek 15 dk.</summary>
    public static KasaZamanAsimlari Varsayilanlar { get; } = new(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15));

    /// <summary>Süre dolduğunda fırlatılan <see cref="TimeoutException"/> iletisi; çağıranın iptali bu iletiyi almaz.</summary>
    public const string Ileti = "Sunucu zamanında yanıt vermedi.";
}
