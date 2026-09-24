namespace Kasa.Core;

/// <summary>Çekin yönü: müşteriden alınan ya da tedarikçiye kesilen (verilen).</summary>
public enum CekYonu
{
    Alinan,   // alınan çek — tahsil edilince kasaya girer
    Verilen   // kesilen çek — ödenince kasadan çıkar
}

/// <summary>
/// Çekin durumu. Geçerli durumlar yöne bağlıdır (<see cref="CekKurali.DurumGecerliMi"/>):
/// alınan çek Portföyde/TahsilEdildi/CiroEdildi/Karsiliksiz/IadeEdildi, verilen çek
/// Portföyde (= ödenecek)/Odendi/IadeEdildi olabilir.
/// </summary>
public enum CekDurumu
{
    Portfoyde,     // alınan: elde bekliyor · verilen: henüz ödenmedi ("Ödenecek")
    TahsilEdildi,  // alınan: bankadan tahsil edildi — kasaya girer
    Odendi,        // verilen: ödendi — kasadan çıkar
    CiroEdildi,    // alınan: başkasına ciro edildi — kasaya dokunmaz
    Karsiliksiz,   // alınan: karşılıksız çıktı — kasaya dokunmaz
    IadeEdildi     // iade edildi — kasaya dokunmaz
}

/// <summary>Hesap motorunun gördüğü çek (yalnız kasa kuralı için gereken alanlar).</summary>
public record Cek(CekYonu Yon, decimal Tutar, string Kanal, CekDurumu Durum, DateOnly? IslemTarihi);

/// <summary>Çekin kasaya tek etkisi: <see cref="Tarih"/>'te kanal için tahsilat (Alınan) ya da ödeme (Verilen).</summary>
public record CekHareketi(DateOnly Tarih, string Kanal, decimal Tutar, CekYonu Yon);

/// <summary>
/// Çek kuralı ("vadede kasaya"): çek kasayı yalnız gerçekten tahsil edildiği ya da ödendiği gün etkiler.
/// <list type="bullet">
/// <item>Alınan + TahsilEdildi: işlem tarihinde kanala ve kasaya +Tutar (o kanalın o tarihe düşen
/// ek geleni gibi).</item>
/// <item>Verilen + Odendi: işlem tarihinde −Tutar (o kanalın o tarihteki Cari gideri gibi; kanal
/// <see cref="Kanallar.Ortak"/> ise Ortak Cari gider gibi).</item>
/// <item>Diğer tüm durumlar (portföyde, ciro, karşılıksız, iade) kasaya dokunmaz.</item>
/// </list>
/// Saf ve yan etkisizdir; API doğrulaması da bu kuralları kullanır.
/// </summary>
public static class CekKurali
{
    /// <summary>Durum bu yöndeki bir çek için geçerli mi?</summary>
    public static bool DurumGecerliMi(CekYonu yon, CekDurumu durum) => yon switch
    {
        CekYonu.Alinan => durum is CekDurumu.Portfoyde or CekDurumu.TahsilEdildi or CekDurumu.CiroEdildi
                              or CekDurumu.Karsiliksiz or CekDurumu.IadeEdildi,
        CekYonu.Verilen => durum is CekDurumu.Portfoyde or CekDurumu.Odendi or CekDurumu.IadeEdildi,
        _ => false,
    };

    /// <summary>İşlem tarihi (tahsil / ödeme / ciro günü) bu durumda zorunlu mu?</summary>
    public static bool IslemTarihiGerekli(CekDurumu durum)
        => durum is CekDurumu.TahsilEdildi or CekDurumu.Odendi or CekDurumu.CiroEdildi;

    /// <summary>
    /// Çekin kasa hareketi; kasaya etkisi yoksa null. Yalnız Alınan+TahsilEdildi ve Verilen+Odendi
    /// (işlem tarihi dolu) hareket üretir. Tutar kuruşa yuvarlanır (<see cref="Para.Yuvarla"/>).
    /// </summary>
    public static CekHareketi? KasaHareketi(Cek c)
    {
        if (c.IslemTarihi is not { } tarih) return null;
        bool etkili = (c.Yon == CekYonu.Alinan && c.Durum == CekDurumu.TahsilEdildi)
                      || (c.Yon == CekYonu.Verilen && c.Durum == CekDurumu.Odendi);
        return etkili ? new CekHareketi(tarih, c.Kanal, Para.Yuvarla(c.Tutar), c.Yon) : null;
    }

    /// <summary>Çeklerin kasa hareketleri (etkisi olmayanlar atlanır).</summary>
    public static IReadOnlyList<CekHareketi> Hareketler(IEnumerable<Cek>? cekler)
    {
        var l = new List<CekHareketi>();
        if (cekler is null) return l;
        foreach (var c in cekler)
            if (KasaHareketi(c) is { } h) l.Add(h);
        return l;
    }
}
