namespace Kasa.ApiClient;

/// <summary>Değişiklik geçmişi satırı (sunucuda GET api/denetim; masaüstü onu "bu kontrolden beri değişenler" içinde,
/// <see cref="KasaKontrolSonrasiDto.Degisiklikler"/>'de okur). <paramref name="Tur"/>: 'Ekle' | 'Degistir' | 'Sil' ya da özel olay
/// ('KilitAc', 'KilitKapat', 'GecmisAyEtkisi', 'GirisBasarisiz'...); 'GecmisKayit': denetim izinden önceki sürümün sakladığı iptal gerekçesi ya da alış ödemesinin önceki durumu, 'sistem' aktörlü ve aktarım anı zamanlı; 'BagKoptu':
/// üst kayıt silinince veritabanının kopardığı bağ (önceki/yeni yabancı anahtar, silmeyle aynı gerekçe ve iz). <paramref name="OncekiJson"/> ve
/// <paramref name="YeniJson"/>: değişiklikte yalnız değişen alanlar, eklemede/silmede bütün alanlar; gizli alanlar '***'.
/// <paramref name="KilitAcmaOlayiId"/>: değişiklik bir ay kilidi açılışının penceresine düştüyse o açılışın kilit olayı
/// (<see cref="AyKilidiOlayDto.Id"/>).</summary>
public record DenetimOlayDto(int Id, DateTimeOffset Zaman, string AktorRol, int? AktorId, string? IstemciIp, string Tur, string Varlik,
    string? VarlikId, string? OncekiJson, string? YeniJson, string? Gerekce, Guid? IstekId, string? TraceId, int? KilitAcmaOlayiId);
