namespace Kasa.ApiClient;

public record EkstreKuralYaz(Guid IstekId, int Surum, string Ad, string Kaynak, string? Banka, string AciklamaIcerir,
    string? Yon, string IslemTuru, string DagilimTuru, IReadOnlyList<int> KanalIds, bool Aktif = true);
public record EkstreKuralDto(int Id, int Surum, string Ad, string Kaynak, string? Banka, string AciklamaIcerir,
    string? Yon, string IslemTuru, string DagilimTuru, IReadOnlyList<int> KanalIds, bool Aktif);
/// <summary>Yalnız Oneri uygulanabilir; Yok/Celiski/Kontrol/Kayitli finans kaydı üretmez.</summary>
public record EkstreKuralOnerisi(int SatirNo, string Durum, IReadOnlyList<string> KuralAdlari, string? IslemTuru,
    string? DagilimTuru, IReadOnlyList<int> KanalIds, string Aciklama);
