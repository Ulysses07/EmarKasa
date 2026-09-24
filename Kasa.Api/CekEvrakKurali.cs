using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

/// <summary>
/// Çek/senet evrak alanlarının (tür, konum, ciro edilen cari) doğrulaması. Kasa kuralına dokunmaz:
/// senet çekle aynı vade ve kasa kurallarına tabidir (<see cref="CekKurali"/>).
/// </summary>
public static class CekEvrakKurali
{
    /// <summary>
    /// Doğrular ve normalleştirir: verilen evrakın konumu her zaman Elde; ciro edilen cari yalnız
    /// CiroEdildi durumunda tutulur, kayıtlı bir cariyle eşleşirse onun yazımı kullanılır.
    /// </summary>
    public static string? Hata(KasaDbContext db, CekEntity e)
    {
        if (!Enum.IsDefined(e.Tur)) return "Geçersiz evrak türü (Cek ya da Senet).";
        if (!Enum.IsDefined(e.Konum)) return "Geçersiz evrak konumu.";
        if (e.Yon == CekYonu.Verilen) e.Konum = CekKonumu.Elde;
        var cari = e.CiroEdilenCari?.Trim();
        if (e.Durum != CekDurumu.CiroEdildi || string.IsNullOrEmpty(cari))
        {
            e.CiroEdilenCari = null;
            return null;
        }
        if (cari.Length > 200) return "Ciro edilen cari en fazla 200 karakter olabilir.";
        e.CiroEdilenCari = db.Cariler.AsNoTracking().Select(c => c.Ad).AsEnumerable()
            .FirstOrDefault(a => Metin.EsitBuyukKucukDuyarsiz.Equals(a, cari)) ?? cari;
        return null;
    }

    /// <summary>Tek dokunuşla geçilebilen durumlar: alınanda tahsil/ciro/karşılıksız, verilende ödendi.</summary>
    public static bool TekDokunusGecerli(CekYonu yon, CekDurumu hedef) => yon switch
    {
        CekYonu.Alinan => hedef is CekDurumu.TahsilEdildi or CekDurumu.CiroEdildi or CekDurumu.Karsiliksiz,
        CekYonu.Verilen => hedef is CekDurumu.Odendi,
        _ => false,
    };

    /// <summary>Kaydın bağımsız kopyası (doğrulama kopya üzerinde yapılır; geçerse asıl kayda aktarılır).</summary>
    public static CekEntity Kopya(CekEntity c) => new()
    {
        Id = c.Id, Yon = c.Yon, CekNo = c.CekNo, Banka = c.Banka, Kisi = c.Kisi, Tutar = c.Tutar,
        DuzenlemeTarihi = c.DuzenlemeTarihi, VadeTarihi = c.VadeTarihi, Kanal = c.Kanal, Durum = c.Durum,
        IslemTarihi = c.IslemTarihi, Not = c.Not, Tur = c.Tur, Konum = c.Konum, CiroEdilenCari = c.CiroEdilenCari,
    };

    /// <summary>PUT /api/cekler/{id} ile aynı alan aktarımı.</summary>
    public static void Aktar(CekEntity kaynak, CekEntity hedef)
    {
        hedef.Yon = kaynak.Yon; hedef.CekNo = kaynak.CekNo; hedef.Banka = kaynak.Banka; hedef.Kisi = kaynak.Kisi;
        hedef.Tutar = kaynak.Tutar; hedef.DuzenlemeTarihi = kaynak.DuzenlemeTarihi; hedef.VadeTarihi = kaynak.VadeTarihi;
        hedef.Kanal = kaynak.Kanal; hedef.Durum = kaynak.Durum; hedef.IslemTarihi = kaynak.IslemTarihi; hedef.Not = kaynak.Not;
        hedef.Tur = kaynak.Tur; hedef.Konum = kaynak.Konum; hedef.CiroEdilenCari = kaynak.CiroEdilenCari;
    }
}
