namespace Kasa.Api;

// Sistem ve risk kartı (paket E; yalnız editör, salt okunur).

public enum RiskSeviyesi { Sari, Kirmizi }

/// <param name="Konu">Kısa anahtar: UzakYedek, YerelYedek, YedekDogrulama, Disk, Giris, Cek.</param>
public record RiskMaddesiDto(RiskSeviyesi Seviye, string Konu, string Baslik, string Aciklama);

public record YedekDogrulamaDto(DateTime ZamanUtc, string Dosya, DateTime DosyaZamaniUtc, bool Basarili, string Mesaj);

/// <param name="Durum">"ok", "sari" ya da "kirmizi" (en ağır madde).</param>
public record SistemRiskDto(string Durum, IReadOnlyList<RiskMaddesiDto> Maddeler, DateTime HesaplanmaUtc,
    YedekDogrulamaDto? SonDogrulama, long? DiskBosMb, int DunBasarisizGiris);
