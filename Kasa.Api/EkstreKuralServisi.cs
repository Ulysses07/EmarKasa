using System.Globalization;
using System.Text;
using Kasa.Api.Data;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api;

public static class EkstreKuralServisi
{
    // Noktalama sözcük ayırır; Türkçe/Latin harf varyantları eşleşir, sözcüklerin içi başka ada uymaz.
    internal static string Normalize(string text)
    {
        var result = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c))
                result.Append(c is 'ı' or 'İ' ? 'I' : char.ToUpperInvariant(c));
            else if (result.Length > 0 && result[^1] != ' ')
                result.Append(' ');
        }
        return result.ToString().Trim();
    }

    internal static EkstreKuralDto Dto(EkstreKuralEntity k) => new(k.Id, k.Surum, k.Ad, k.Kaynak, k.Banka,
        k.AciklamaIcerir, k.Yon, k.IslemTuru, k.DagilimTuru, Read<int>(k.KanalIdsJson), k.Aktif);

    public static IReadOnlyList<EkstreKuralOnerisi> Oneriler(KasaDbContext db, EkstreBelgeEntity belge)
    {
        var kurallar = db.EkstreKurallar.AsNoTracking().Where(k => k.Aktif && k.Kaynak == belge.Kaynak && (k.Banka == null || k.Banka == belge.Banka))
            .OrderBy(k => k.Id).ToList().Select(k => (Kural: Dto(k), Kosul: " " + Normalize(k.AciklamaIcerir) + " ")).ToArray();
        var aktifKanallar = db.Kanallar.AsNoTracking().Where(k => k.Aktif).Select(k => k.Id).ToHashSet();
        var kayitli = db.EkstreKayitlar.AsNoTracking().Where(k => k.BelgeId == belge.Id && !k.Iptal).Select(k => k.SatirNo).ToHashSet();
        return Read<EkstreOkunanSatir>(belge.SatirlarJson).Select(row =>
        {
            EkstreKuralOnerisi Durum(string durum, string ileti, string[]? adlar = null) => new(row.No, durum, adlar ?? [], null, null, [], ileti);
            if (kayitli.Contains(row.No))
                return Durum("Kayitli", "Bu satır zaten kaydedildi.");
            var text = " " + Normalize(row.Aciklama) + " ";
            var matched = kurallar.Where(k => (k.Kural.Yon == null || k.Kural.Yon == row.Yon) && text.Contains(k.Kosul, StringComparison.Ordinal)).Select(k => k.Kural).ToArray();
            if (matched.Length == 0)
                return Durum("Yok", "Eşleşen kişisel kural yok; PDF önerisini kontrol edin.");
            var names = matched.Select(k => k.Ad).ToArray();
            var targets = matched.Select(k => $"{k.IslemTuru}|{k.DagilimTuru}|{string.Join(',', k.KanalIds.Order())}").Distinct().ToArray();
            if (targets.Length != 1)
                return Durum("Celiski", "Birden fazla kural farklı sonuç öneriyor. Kuralları veya bu satırı elle düzenleyin.", names);
            var k = matched[0];
            if (k.KanalIds.Any(id => !aktifKanallar.Contains(id)))
                return Durum("Kontrol", "Kuralın kanalı silinmiş veya pasif; kuralı güncelleyin.", names);
            if (k.IslemTuru != EkstreIslemTurleri.Atla)
            {
                if (row.ParaBirimi is not ("TRY" or "TL") || row.Tarih is not { } tarih || !GirdiDogrulama.GecerliKayitTarihi(tarih, db.Bugunu())
                    || row.Tutar is not > 0 or > 999_999_999_999.99m || decimal.Round(row.Tutar.Value, 2) != row.Tutar)
                    return Durum("Kontrol", "Tarih, tutar veya TL bilgisi kesin/geçerli değil; PDF ile karşılaştırın.", names);
                var yon = k.IslemTuru == EkstreIslemTurleri.Gelir ? "Giris" : "Cikis";
                if (row.Yon != yon || row.OnerilenIslem == EkstreIslemTurleri.Atla || row.Uyarilar.Count > 0
                    || row.OnerilenIslem is EkstreIslemTurleri.KartIade or EkstreIslemTurleri.KartOdemesi)
                    return Durum("Kontrol", "PDF yönü veya uyarısı otomatik öneriye uygun değil; mevcut uyarıları kontrol edin.", names);
            }
            return new EkstreKuralOnerisi(row.No, "Oneri", names, k.IslemTuru, k.DagilimTuru, k.KanalIds,
                k.IslemTuru == EkstreIslemTurleri.Atla ? "Bu hareketi seçmeden bırakın; yeni gelir/gider oluşturmayın." : "Kişisel kural önerisi; uyguladıktan sonra tür ve kanal paylarını onaylayın.");
        }).ToList();
    }
}
