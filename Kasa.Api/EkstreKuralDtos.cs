namespace Kasa.Api;

public record EkstreKuralYaz(Guid IstekId, int Surum, string Ad, string Kaynak, string? Banka, string AciklamaIcerir,
    string? Yon, string IslemTuru, string DagilimTuru, IReadOnlyList<int> KanalIds, bool Aktif = true);
public record EkstreKuralDto(int Id, int Surum, string Ad, string Kaynak, string? Banka, string AciklamaIcerir,
    string? Yon, string IslemTuru, string DagilimTuru, IReadOnlyList<int> KanalIds, bool Aktif);
/// <summary>Durum: Oneri, Yok, Celiski, Kontrol veya Kayitli. Yalnız Oneri editöre uygulanabilir; hiçbir durum mali kayıt üretmez.</summary>
public record EkstreKuralOnerisi(int SatirNo, string Durum, IReadOnlyList<string> KuralAdlari, string? IslemTuru,
    string? DagilimTuru, IReadOnlyList<int> KanalIds, string Aciklama);
