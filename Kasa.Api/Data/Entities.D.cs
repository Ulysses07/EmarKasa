using Kasa.Core;

namespace Kasa.Api.Data;

// Paket D'nin var olan tablolara eklediği sütunlar. Hepsi varsayılanlı ya da NULL olabilir:
// SemaGuncelleyici eski DB'ye ALTER TABLE ADD COLUMN ile ekler, eski satırlar varsayılanı alır.

public partial class CekEntity
{
    /// <summary>Çek (varsayılan) ya da senet. Kasa kuralı ikisinde de aynıdır.</summary>
    public CekTuru Tur { get; set; }
    /// <summary>Portföydeki alınan evrakın konumu; verilen evrakta her zaman Elde.</summary>
    public CekKonumu Konum { get; set; }
    /// <summary>Ciro edilen cari (yalnız CiroEdildi durumunda dolu; kayıtlı cari ise onun yazımı).</summary>
    public string? CiroEdilenCari { get; set; }
}

public partial class KasaSayimEntity
{
    /// <summary>
    /// Sayım satırları (nakit / banka / POS / diğer, nakitte isteğe bağlı küpür adetleri) JSON olarak.
    /// null: eski tip tek tutarlı sayım. <see cref="SayilanTutar"/> her zaman satırların toplamıdır.
    /// </summary>
    public string? SatirlarJson { get; set; }
    /// <summary>Farkın durumu (fark sıfırsa anlamsızdır).</summary>
    public SayimFarkDurumu FarkDurumu { get; set; }
    public string? FarkAciklamasi { get; set; }
}

/// <summary>Kasa sayımındaki farkın durumu.</summary>
public enum SayimFarkDurumu
{
    Acik,         // varsayılan: fark açıklanmadı
    Aciklandi,    // açıklaması yazıldı
    KabulEdildi   // fark olduğu gibi kabul edildi
}

public partial class TekrarlayanGiderEntity
{
    /// <summary>Tekrar sıklığı; ilk ay <see cref="BaslangicAyi"/>'dır (evre başlangıç ayından sayılır).</summary>
    public TekrarSikligi Siklik { get; set; }
    /// <summary>
    /// Doluysa onaylanan kayıt bu karta bağlı K.K işlemi olur; bu durumda <see cref="Kalem"/>
    /// kayıtlı bir cari adıdır (kart harcaması cariye yazılır).
    /// </summary>
    public int? KrediKartiId { get; set; }
    /// <summary>Tutar her seferinde onaylarken girilir (vergi gibi): şablonda tutar 0 olabilir.</summary>
    public bool TutarDegisken { get; set; }
}

/// <summary>Tekrarlayan giderin sıklığı.</summary>
public enum TekrarSikligi
{
    Aylik,      // varsayılan: her ay
    UcAylik,    // 3 ayda bir
    AltiAylik,  // 6 ayda bir
    Yillik      // yılda bir
}
