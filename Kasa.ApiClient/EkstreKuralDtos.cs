namespace Kasa.ApiClient;

public record EkstreKuralYaz(Guid IstekId, int Surum, string Ad, string Kaynak, string? Banka, string AciklamaIcerir,
    string? Yon, string IslemTuru, string DagilimTuru, IReadOnlyList<int> KanalIds, bool Aktif = true);
public record EkstreKuralDto(int Id, int Surum, string Ad, string Kaynak, string? Banka, string AciklamaIcerir,
    string? Yon, string IslemTuru, string DagilimTuru, IReadOnlyList<int> KanalIds, bool Aktif);
/// <summary>Kişisel kuralda yalnız 'Oneri' uygulanabilir. İşlem hafızasında (<paramref name="Hafiza"/> dolu; Durum EkstreHafizaDurumlari) satır
/// kendiliğinden doldurulur; IslemTuru 'Atla' hareketin daha önce yok sayıldığını söyler. ERP12 satırında (Durum ErpOneriDurumlari) olası karşılıklar
/// <paramref name="Adaylar"/>'dadır, kesinse ilki. Hiçbiri kayıt üretmez.</summary>
public record EkstreKuralOnerisi(int SatirNo, string Durum, IReadOnlyList<string> KuralAdlari, string? IslemTuru,
    string? DagilimTuru, IReadOnlyList<int> KanalIds, string Aciklama, EkstreHafizaOnerisi? Hafiza = null, IReadOnlyList<EkstreEslesmeAdayiDto>? Adaylar = null);
/// <summary>Hafıza önerisinin dayanağı; <paramref name="Adet"/> son kararın art arda kaç kez aynı verildiği.</summary>
public record EkstreHafizaOnerisi(string Anahtar, int Adet, DateOnly SonTarih, int? KrediKartiId, string? Aciklama);
/// <summary>Hafıza listesinin öğesi: anahtar ve yönün son kararı.</summary>
public record EkstreHafizaDto(string Kaynak, string Anahtar, string Yon, string IslemTuru, string DagilimTuru, IReadOnlyList<int> KanalIds,
    int? KrediKartiId, string? Aciklama, int Adet, DateOnly SonTarih, bool Celiskili);
public record EkstreHafizaUnutYaz(Guid IstekId, string Kaynak, string Anahtar, string Yon);
