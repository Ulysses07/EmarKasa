using Kasa.Api.Data;

namespace Kasa.Api;

// Kayda soru sorma (paket E). Enum'lar JSON'da metin olarak gider ("Islem", "Acik").

public record SoruDto(int Id, SoruHedefTuru HedefTur, int? HedefId, DateOnly? Hafta, string? HedefOzet, string Metin,
    int? SoranId, string SoranAd, string SoranRol, DateTime SorulmaUtc, string? Cevap, string? CevaplayanAd,
    DateTime? CevaplanmaUtc, SoruDurumu Durum, DateTime? KapanmaUtc)
{
    public static SoruDto Olustur(SoruEntity s) => new(s.Id, s.HedefTur, s.HedefId, s.Hafta, s.HedefOzet, s.Metin,
        s.SoranId, s.SoranAd, s.SoranRol, DateTime.SpecifyKind(s.SorulmaUtc, DateTimeKind.Utc), s.Cevap, s.CevaplayanAd,
        s.CevaplanmaUtc is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : null, s.Durum,
        s.KapanmaUtc is { } k ? DateTime.SpecifyKind(k, DateTimeKind.Utc) : null);
}

public record SoruEkleDto(SoruHedefTuru HedefTur, int? HedefId, DateOnly? Hafta, string? Metin);

/// <param name="Kapat">Cevapla birlikte soruyu kapat (varsayılan).</param>
public record SoruCevapDto(string? Cevap, bool Kapat = true);

/// <param name="CevapBekleyen">Açık ve henüz cevaplanmamış.</param>
public record SoruOzetDto(int AcikSayisi, int CevapBekleyen, IReadOnlyList<SoruDto> SonAciklar);
