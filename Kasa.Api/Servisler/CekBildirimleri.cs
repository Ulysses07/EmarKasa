using System.Globalization;
using Kasa.Api.Data;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>
/// Çek ve senet vade hatırlatmaları (docs/specs/2026-10-01-cekler.md "Bildirimler"); <see cref="KasaEsikServisi"/> gibi
/// <see cref="BildirimServisi"/>'nin taslaklarına ayrı kaynak olarak girer. Teminat çekleri ve portföyde ya da kısmen tahsil
/// edilmiş / ödenmiş olmayan (kapanmış, ciro edilmiş, kırdırılmış, karşılıksız) çekler hariçtir. Kurallar:
/// <list type="bullet">
/// <item>Alınan, portföyde ya da kısmen: vadeye 3 gün kala ve vade günü "Çek vadesi" (kısmide kalan tutar).</item>
/// <item>Alınan çek (senet değil), portföyde: vadenin 7. günü "İbraz süresi doluyor" (kambiyo mevzuatındaki 7 günlük ibraz süresi
/// yalnız çeke özgüdür; senedin ibraz/zamanaşımı süresi farklıdır, bu yüzden senette bu uyarı çıkmaz).</item>
/// <item>Verilen, portföyde ya da kısmen: vadeye 3 gün kala ve vade günü "Ödenecek çek".</item>
/// </list>
/// Hedef "/#cheques/{id}": masaüstü bunu Çekler sayfasında o çeke çevirir (Kasa.App.Core BildirimHedefi). Anahtar çek, kural,
/// vade ve gün farkını taşır: vade değişirse eski hatırlatma iptal olur, yenisi oluşur.
/// </summary>
public static class CekBildirimleri
{
    /// <summary>Hesaplanamayan çek hatırlatmalarının kaynak adı (BildirimKaynakHatasi.Kaynak).</summary>
    public const string Kaynak = "Cekler";
    public const string VadeTuru = "CekVade";
    public const string IbrazTuru = "CekIbraz";
    public const string OdemeTuru = "CekOdeme";

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static IReadOnlyList<BildirimTaslagi> Oku(KasaDbContext db, DateOnly today)
    {
        var sonuc = new List<BildirimTaslagi>();
        // Üç kuralın hepsi vadesi {bugün, bugün+3, bugün-7} olan teminatsız çeklerle sınırlıdır: önce bu adaylar SQL'de seçilir,
        // yalnız onların hareketleri okunur (tüm çek tablosunu taramaktan kaçınılır). Aday seçimiyle hareketler aynı anlık
        // görüntüden okunur (kart/kredi kaynağındaki desen, bkz. FinansBildirimKaynaklari).
        using var okuma = db.OkumaBaslat();
        var vadeler = new[] { today, today.AddDays(3), today.AddDays(-7) };
        var adaylar = db.Cekler.AsNoTracking().Where(c => !c.Teminat && vadeler.Contains(c.VadeTarihi)).Select(c => c.Id).ToList();
        if (adaylar.Count == 0)
            return sonuc;
        foreach (var k in CekServisi.Oku(db, adaylar: adaylar).Where(k => k.Durum.Acik))
        {
            var c = k.Cek;
            var ad = c.Tur switch { CekTurleri.Senet => "Senet", _ => "Çek" };
            var fark = c.VadeTarihi.DayNumber - today.DayNumber;
            var vade = c.VadeTarihi.ToString("d MMMM", Tr);
            void Ekle(string kural, string tur, string baslik, string mesaj) => sonuc.Add(new(
                $"{Kaynak}:{c.Id}:{kural}:{c.VadeTarihi.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:{fark}", baslik, mesaj, today, $"/#cheques/{c.Id}", tur, c.Id));
            if (c.Yon == CekYonleri.Verilen)
            {
                if (fark is 3 or 0)
                    Ekle("Odenecek", OdemeTuru, $"Ödenecek {ad.ToLower(Tr)}", $"{c.Kisi} · {Tl(k.Durum.Kalan)} · hesapta bulunmalı");
                continue;
            }
            if (fark is 3 or 0)
                Ekle("Vade", VadeTuru, $"{ad} vadesi", $"{c.Kisi} · {Tl(k.Durum.Kalan)} · {vade}");
            if (fark == -7 && c.Tur == CekTurleri.Cek && k.Durum.Durum == CekDurumlari.Portfoyde)
                Ekle("Ibraz", IbrazTuru, "İbraz süresi doluyor", $"{c.Kisi} · {Tl(k.Durum.Kalan)} · vade {vade}");
        }
        return sonuc;
    }

    /// <summary>Kart ve kredi bildirimleriyle aynı biçim: her zaman kuruşlu, "50.000,00 TL" (ürün sahibi 2026-10-01).</summary>
    private static string Tl(decimal tutar) => tutar.ToString("N2", Tr) + " TL";
}
