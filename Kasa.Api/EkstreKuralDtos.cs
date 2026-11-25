namespace Kasa.Api;

public record EkstreKuralYaz(Guid IstekId, int Surum, string Ad, string Kaynak, string? Banka, string AciklamaIcerir,
    string? Yon, string IslemTuru, string DagilimTuru, IReadOnlyList<int> KanalIds, bool Aktif = true);
public record EkstreKuralDto(int Id, int Surum, string Ad, string Kaynak, string? Banka, string AciklamaIcerir,
    string? Yon, string IslemTuru, string DagilimTuru, IReadOnlyList<int> KanalIds, bool Aktif);
/// <summary>Durum: Oneri, Yok, Celiski, Kontrol, Kayitli, YokSayildi (kişisel kural) ya da EkstreHafizaDurumlari (işlem hafızası; o zaman
/// <paramref name="Hafiza"/> doludur) ya da ErpOneriDurumlari (ERP12 satırı; olası karşılıklar <paramref name="Adaylar"/>, kesinse ilki). Hiçbir durum mali
/// kayıt üretmez. Hafızada IslemTuru 'Atla' satırın daha önce yok sayıldığını söyler.</summary>
public record EkstreKuralOnerisi(int SatirNo, string Durum, IReadOnlyList<string> KuralAdlari, string? IslemTuru,
    string? DagilimTuru, IReadOnlyList<int> KanalIds, string Aciklama, EkstreHafizaOnerisi? Hafiza = null, IReadOnlyList<EkstreEslesmeAdayiDto>? Adaylar = null);
/// <summary>Hafıza önerisinin dayanağı: anahtar, son kararın art arda kaç kez aynı verildiği ve tarihi; karta ödemede kart;
/// <paramref name="Aciklama"/> yeniden kullanılabilir düzeltilmiş açıklama (yoksa null).</summary>
public record EkstreHafizaOnerisi(string Anahtar, int Adet, DateOnly SonTarih, int? KrediKartiId, string? Aciklama);
/// <summary>Hafıza listesinin öğesi (GET /api/ekstre-aktar/hafiza): anahtar ve yönün son kararı; <paramref name="Adet"/> bütün kararların sayısı,
/// <paramref name="Celiskili"/> son iki karar farklı.</summary>
public record EkstreHafizaDto(string Kaynak, string Anahtar, string Yon, string IslemTuru, string DagilimTuru, IReadOnlyList<int> KanalIds,
    int? KrediKartiId, string? Aciklama, int Adet, DateOnly SonTarih, bool Celiskili);
/// <summary>POST /api/ekstre-aktar/hafiza/unut gövdesi.</summary>
public record EkstreHafizaUnutYaz(Guid IstekId, string Kaynak, string Anahtar, string Yon);
