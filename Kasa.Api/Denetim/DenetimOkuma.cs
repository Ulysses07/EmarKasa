using System.Globalization;
using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Denetim;

/// <summary>Uç yanıtlarının denetim izinden okuduğu bilgiler (kaynak tablolarda tutulmayan zamanlar).</summary>
public static class DenetimOkuma
{
    /// <summary>
    /// İptal edilen kayıtların iptal anı: kaydın 'Iptal' alanını true yapan 'Degistir' olayının zamanı (aylık gider ödemesi,
    /// ekstre satırı). Kaynak tablo iptal anını tutmaz. Sürüm öncesi iptallerin anı bilinmez: 'GecmisKayit' aktarımının zamanı
    /// aktarım anıdır, iptal anı sayılmaz; o kayıtlar sözlükte yer almaz. Olay tablosu yoksa sözlük boştur.
    /// </summary>
    public static Dictionary<int, DateTimeOffset> IptalAnlari<TVarlik>(KasaDbContext db, IReadOnlyCollection<int> kimlikler)
    {
        if (kimlikler.Count == 0 || !DenetimYazici.TabloVar(db))
            return [];
        var varlik = DenetimYakalayici.VarlikAdi(typeof(TVarlik));
        var anahtarlar = kimlikler.Select(k => k.ToString(CultureInfo.InvariantCulture)).ToList();
        return db.DenetimOlaylari.AsNoTracking()
            .Where(o => o.Varlik == varlik && o.Tur == "Degistir" && anahtarlar.Contains(o.VarlikId!) && o.YeniJson!.Contains("\"Iptal\":true"))
            .Select(o => new { o.VarlikId, o.ZamanUtc }).AsEnumerable()
            .GroupBy(o => int.Parse(o.VarlikId!, CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => DateTimeOffset.FromUnixTimeMilliseconds(g.Min(o => o.ZamanUtc)));
    }

    /// <summary>Tek kaydın iptal anı (<see cref="IptalAnlari{TVarlik}"/>); iptal edilmemiş ya da anı bilinmeyen kayıtta null.</summary>
    public static DateTimeOffset? IptalAni<TVarlik>(KasaDbContext db, int kimlik) =>
        IptalAnlari<TVarlik>(db, [kimlik]).TryGetValue(kimlik, out var an) ? an : null;
}
