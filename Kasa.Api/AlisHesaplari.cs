using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api;

public static class AlisHesaplari
{
    public static IReadOnlyList<AlisKanalPayi> KanalPaylari(AlisEntity alis) => alis.Kalemler
        .SelectMany(k => k.Dagilimlar).GroupBy(d => d.KanalId)
        .Select(g => new AlisKanalPayi(g.Key, g.Sum(d => d.Tutar)))
        .Where(p => p.Tutar > 0).OrderBy(p => p.KanalId).ToList();

    public static IReadOnlyDictionary<int, IReadOnlyList<AlisKanalPayi>> OdemeDagilimlari(AlisEntity alis)
    {
        var result = new Dictionary<int, IReadOnlyList<AlisKanalPayi>>();
        if (alis.Durum != AlisDurumlari.Onaylandi) return result;
        var paylar = KanalPaylari(alis);
        decimal onceki = 0;
        foreach (var odeme in alis.Odemeler.OrderBy(o => o.Id))
        {
            result[odeme.IslemId] = AlisDagitici.Dagit(paylar, onceki, odeme.Islem.TutarTl);
            onceki += odeme.Islem.TutarTl;
        }
        return result;
    }

    public static AlisDto ToDto(AlisEntity alis, IReadOnlyDictionary<int, string>? kartAdlari = null)
    {
        var toplam = alis.Kalemler.Sum(k => k.Tutar);
        var odenen = alis.Odemeler.Sum(o => o.Islem.TutarTl);
        var dagilimlar = OdemeDagilimlari(alis);
        var kanalAdlari = alis.Kalemler.SelectMany(k => k.Dagilimlar)
            .GroupBy(d => d.KanalId).ToDictionary(g => g.Key, g => g.First().KanalKaydi.Ad);
        return new AlisDto(alis.Id, alis.Surum, alis.AliciId, alis.AliciKaydi?.Ad ?? "Editör", alis.Tarih,
            alis.Tedarikci, alis.Not, alis.Durum, alis.EditorNotu, toplam, odenen, toplam - odenen,
            alis.Kalemler.OrderBy(k => k.Id).Select(k => new AlisKalemDto(k.Id, k.Aciklama, k.Tutar,
                k.Dagilimlar.OrderBy(d => d.KanalId).Select(d => new AlisDagilimDto(d.KanalId, d.KanalKaydi.Ad, d.Tutar)).ToList(), k.Miktar, k.BirimFiyat)).ToList(),
            alis.Odemeler.OrderBy(o => o.Id).Select(o => new AlisOdemeDto(o.Id, o.IslemId, o.Islem.Tarih,
                o.Islem.TutarTl, o.Islem.KrediKartiId, alis.Durum != AlisDurumlari.Onaylandi,
                dagilimlar.TryGetValue(o.IslemId, out var paylar)
                    ? paylar.Select(p => new AlisDagilimDto(p.KanalId, kanalAdlari[p.KanalId], p.Tutar)).ToList()
                    : [], o.Islem.HesapHareketi?.HesapId,
                o.Islem.KrediKartiId is { } kartId ? kartAdlari?.GetValueOrDefault(kartId) : null)).ToList(), alis.TedarikciId, alis.Vade);
    }
}
