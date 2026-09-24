namespace Kasa.Api;

/// <summary>
/// Geleni girilmemiş, tamamlanmış bir dönem: dönem bitti (dünden önce ya da dün) ama listelenen
/// aktif kanallar için gelen satırı yok. Sıfır girilmiş gelen "girildi" sayılır.
/// </summary>
public record EksikGelenDto(DateOnly DonemStart, DateOnly DonemEnd, IReadOnlyList<string> Kanallar);

/// <summary>Geçmişe dönük (önceki bir ayın rakamını değiştiren) değişiklik satırının kısa hali.</summary>
public record GecmisOzetSatiriDto(int Id, DateTime ZamanUtc, string Tur, string Ozet);

/// <summary>
/// Değişiklik geçmişinin özeti. <see cref="SonId"/> en yeni satırın Id'si (geçmiş boşsa 0),
/// <see cref="SonZamanUtc"/> onun zamanı ("defter en son … güncellendi"). <c>sonId</c> verildiyse
/// <see cref="Toplam"/> ondan sonraki satır sayısı, <see cref="GecmiseDonuk"/> bunların geçmişe
/// dönük olanları ve <see cref="GecmiseDonukSatirlar"/> en yeni en fazla 5 tanesidir.
/// </summary>
public record GecmisOzetDto(
    int SonId,
    DateTime? SonZamanUtc,
    int Toplam,
    int GecmiseDonuk,
    IReadOnlyList<GecmisOzetSatiriDto> GecmiseDonukSatirlar);
