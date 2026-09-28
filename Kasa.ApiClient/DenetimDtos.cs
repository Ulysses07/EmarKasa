namespace Kasa.ApiClient;

/// <summary>Değişiklik geçmişi satırı (GET api/denetim; yalnız editör). <paramref name="Tur"/>: 'Ekle' | 'Degistir' | 'Sil'
/// ya da özel olay ('KilitAc', 'KilitKapat', 'GecmisAyEtkisi', 'GirisBasarisiz'...); 'GecmisKayit': denetim izinden önceki
/// sürümün sakladığı iptal gerekçesi ya da alış ödemesinin önceki durumu, 'sistem' aktörlü ve aktarım anı zamanlı. <paramref name="OncekiJson"/> ve
/// <paramref name="YeniJson"/>: değişiklikte yalnız değişen alanlar, eklemede/silmede bütün alanlar; gizli alanlar '***'.
/// <paramref name="KilitAcmaOlayiId"/>: değişiklik bir ay kilidi açılışının penceresine düştüyse o açılışın kilit olayı
/// (<see cref="AyKilidiOlayDto.Id"/>).</summary>
public record DenetimOlayDto(int Id, DateTimeOffset Zaman, string AktorRol, int? AktorId, string? IstemciIp, string Tur, string Varlik,
    string? VarlikId, string? OncekiJson, string? YeniJson, string? Gerekce, Guid? IstekId, string? TraceId, int? KilitAcmaOlayiId);

/// <summary>Değişiklik geçmişi süzgeci: boş alan süzmez. <see cref="OncekiId"/> sayfalama içindir (bir önceki sayfanın son
/// satırının Id'si); <see cref="Adet"/> 1–200 (varsayılan 100). Sonuç yeniden eskiye sıralıdır.</summary>
public sealed record DenetimSorgusu(string? Varlik = null, string? VarlikId = null, string? Tur = null, int? KilitAcmaOlayiId = null,
    int? OncekiId = null, int? Adet = null);
