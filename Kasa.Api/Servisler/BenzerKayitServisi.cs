using System.Globalization;
using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api.Servisler;

/// <summary>Benzer kayıt araması. <paramref name="Tur"/>: "Gider", "AylikGider", "AlisOdeme" (kartlı ya da kartsız),
/// "KartHarcama" (işaretli tutar: iade eksi) ya da "KartOdeme". <paramref name="Kanallar"/> null ise kanal süzgeci yoktur.
/// <paramref name="AlisId"/> verilirse aynı alışın ödemeleri kanal süzgecinden bağımsız listelenir.</summary>
public sealed record BenzerAramasi(string Tur, DateOnly Tarih, decimal Tutar, int? KrediKartiId = null, IReadOnlySet<int>? Kanallar = null, int? AlisId = null);

/// <summary>
/// Olası mükerrer girişin tek kuralı: benzerlik ucu (<c>/api/islemler/benzerlik</c>) ve ekstre önizlemesi bu servisi
/// kullanır. Eşleşme aynı tutar ve ±<see cref="GunPenceresi"/> gündür. Kaynak kümeleri simetriktir: bir yoldan girilen para,
/// diğer yoldan girilirken hangi kaynaktan sorulursa sorulsun bulunur.
/// <list type="bullet">
/// <item>Kartsız para çıkışı (gider, aylık gider, kartsız alış ödemesi, banka ekstresi gideri): kartsız bütün giderler
/// (elle, Ortak, alış ödemesi, aylık gider, ekstre), kart ödemeleri (takip ve eski), takipli kredi taksitleri ve hesap
/// motorunun türettiği eski kredi taksitleri.</item>
/// <item>Kart ödemesi: aynı kartın ödemeleri ve kartsız bütün giderler (kart borcu bankadan ödenip gider yazılmış olabilir).</item>
/// <item>Kart harcaması (kartlı gider ve alış ödemesi dahil): aynı kartın giderleri ve giderden türememiş kart harcamaları.</item>
/// </list>
/// Kanal süzgeci yalnız kanalı kesin olarak başka olan kaydı eler. Kanal kümesi hesap motorunun kasayı düşürdüğü kümedir:
/// giderde <see cref="HesapServisi"/> ile aynı öncelik (ekstre dağılımı, aylık gider revizyonu, onaylı alış payı, gider
/// kanalı); takipli kredi taksidinde taksidin dağılımı; türetilmiş eski taksitte kredinin kanalı (kimliği yoksa adı).
/// Genel kasa, Ortak, dağılım bekleyen ve kanalı belirsiz kayıt her kanal sorgusunda görünür. Kart ödemesi de süzülmez:
/// kanal payı ödeme anında kesin değildir, kart taksitlerinin harcama dağılımından önceki ödemelere ve alış onayına göre
/// hesaplanır (onaysız alışta "Dağılım bekliyor"); eski kart ödemesinin kanalı yoktur.
/// Benzerlik bir uyarıdır: servis kayıt oluşturmaz ve yazmaz; çağıranın okuma bağlamını (anlık görüntü ya da yazma
/// transaction'ı) kullanır. Bir istek boyunca değişmeyen okumalar (kanal adları, eski kredi taksitleri) bir kez yapılır.
/// Kullanıcılar: benzerlik ucu, ekstre önizlemesi ve aylık gider ödemesinin benzer kayıt protokolü (<c>BenzerOnay</c>).
/// </summary>
public sealed class BenzerKayitServisi(KasaDbContext db)
{
    public const int GunPenceresi = 3;
    public const int EnFazla = 10;
    /// <summary>Uyarı metninde adlandırılan kayıt sayısı; kalanlar sayılır (satır başına tek uyarı).</summary>
    public const int UyaridaEnFazla = 3;
    private const string Belirsiz = "Kanal belirsiz";
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private Dictionary<int, (string Ad, int Sira)>? _kanallar;
    private List<(int KrediId, int No, DateOnly Tarih, decimal Tutar, string Ad, string Kanal, int? KanalId)>? _eskiTaksitler;

    /// <summary>En yakın tarihli en çok <paramref name="enFazla"/> (varsayılan <see cref="EnFazla"/>) benzer kayıt. <paramref name="dahil"/>
    /// verilirse süzgeç sınırdan önce uygulanır (elenen kayıt yakındaki başka kaydın yerini tutmaz). Ekstre eşleşme adayları da
    /// bu aramayla bulunur (<see cref="EkstreAktarmaEndpoints"/>).</summary>
    public IReadOnlyList<BenzerKayitDto> Bul(BenzerAramasi a, Func<BenzerKayitDto, bool>? dahil = null, int enFazla = EnFazla)
    {
        var bas = a.Tarih.AddDays(-GunPenceresi);
        var son = a.Tarih.AddDays(GunPenceresi);
        var adaylar = new List<(BenzerKayitDto Kayit, int Sira)>();
        if (a.Tur == "KartHarcama" || a.Tur is "Gider" or "AlisOdeme" && a.KrediKartiId is not null)
        {
            var kart = a.KrediKartiId!.Value;
            adaylar.AddRange(Giderler(a, bas, son, kart).Select(k => (k, 0)));
            adaylar.AddRange(KartHarcamalari(a, bas, son, kart).Select(k => (k, 1)));
        }
        else
        {
            var kartOdemesi = a.Tur == "KartOdeme";
            adaylar.AddRange(Giderler(a, bas, son, null).Select(k => (k, 0)));
            adaylar.AddRange(KartOdemeleri(a, bas, son, kartOdemesi ? a.KrediKartiId : null));
            if (!kartOdemesi)
                adaylar.AddRange(KrediTaksitleri(a, bas, son));
        }
        return adaylar.Where(x => dahil is null || dahil(x.Kayit))
            .OrderBy(x => Math.Abs(x.Kayit.Tarih.DayNumber - a.Tarih.DayNumber)).ThenBy(x => x.Sira).ThenByDescending(x => x.Kayit.Id)
            .Take(enFazla).Select(x => x.Kayit).ToList();
    }

    /// <summary>Uyarı metninde tek kayıt: kaynak (<paramref name="kaynak"/> verilmezse <see cref="KaynakEtiketi"/>), tarih, tutar
    /// ve varsa kasadan düştüğü kanal ("Banka gideri #12 · 01.11.2026 · 40.000,00 TL · Genel kasa").</summary>
    public static string Satir(BenzerKayitDto k, string? kaynak = null) =>
        $"{kaynak ?? KaynakEtiketi(k)} · {k.Tarih.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} · {k.Tutar.ToString("N2", Tr)} TL"
        + (k.KanalEtiketi is { } kanal ? $" · {kanal}" : "");

    /// <summary>Kayıtların tek uyarıdaki metni: ilk <see cref="UyaridaEnFazla"/> kayıt adlandırılır, kalanı sayılır. Liste
    /// <see cref="EnFazla"/> sınırına ulaşmışsa kalan sayısı alt sınırdır.</summary>
    public static string Liste(IReadOnlyList<string> kayitlar)
    {
        var metin = string.Join("; ", kayitlar.Take(UyaridaEnFazla));
        var kalan = kayitlar.Count - UyaridaEnFazla;
        return kalan <= 0 ? metin : $"{metin} ve {(kayitlar.Count >= EnFazla ? "en az " : "")}{kalan} kayıt daha";
    }

    /// <summary>Uyarı metninde kaynağın adı ve numarası ("Gider #12", "Kredi #3 taksidi").</summary>
    public static string KaynakEtiketi(BenzerKayitDto k) => k.Kaynak switch
    {
        "Islem" when k.EkstreKayitId is not null => $"Banka gideri #{k.Id}",
        "Islem" when k.AylikGiderOdemeId is not null => $"Aylık gider ödemesi #{k.Id}",
        "Islem" when k.AlisId is not null => $"Alış #{k.AlisId} ödemesi #{k.Id}",
        "Islem" => $"Gider #{k.Id}",
        "KartHarcama" => $"Kart harcaması #{k.Id}",
        "KartOdeme" => $"Kart ödemesi #{k.Id}",
        "EskiKartOdeme" => $"Eski kart ödemesi #{k.Id}",
        "KrediTaksidi" => $"Kredi taksidi #{k.Id}",
        "EskiKrediTaksidi" => $"Kredi #{k.Id} taksidi",
        _ => $"Kayıt #{k.Id}"
    };

    private List<BenzerKayitDto> Giderler(BenzerAramasi a, DateOnly bas, DateOnly son, int? kart)
    {
        var giderler = db.Islemler.AsNoTracking()
            .Where(i => i.KrediKartiId == kart && i.TutarTl == a.Tutar && i.Tarih >= bas && i.Tarih <= son).ToList();
        if (giderler.Count == 0)
            return [];
        var ids = giderler.Select(i => i.Id).ToArray();
        var ekstre = db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal && k.IslemId != null && ids.Contains(k.IslemId.Value)).ToList()
            .GroupBy(k => k.IslemId!.Value).ToDictionary(g => g.Key, g => g.First());
        var aylik = db.AylikGiderOdemeler.AsNoTracking().Where(p => !p.Iptal && p.IslemId != null && ids.Contains(p.IslemId.Value)).ToList()
            .GroupBy(p => p.IslemId!.Value).ToDictionary(g => g.Key, g => g.First());
        var revizyonIds = aylik.Values.Select(p => p.RevizyonId).ToArray();
        var revizyonlar = db.AylikGiderRevizyonlar.AsNoTracking().Where(r => revizyonIds.Contains(r.Id)).ToDictionary(r => r.Id);
        var alislar = AlisEndpoints.Query(db).AsNoTracking().Where(x => x.Odemeler.Any(o => ids.Contains(o.IslemId))).ToList();
        var bagliAlis = alislar.SelectMany(x => x.Odemeler.Select(o => (o.IslemId, Alis: x))).ToDictionary(x => x.IslemId, x => x.Alis);
        var alisPaylari = alislar.ToDictionary(x => x.Id, AlisHesaplari.OdemeDagilimlari);

        // Hesap motoruyla aynı öncelik (HesapServisi.Yukle): ekstre dağılımı, aylık gider revizyonu, alış, gider kanalı.
        // Küme null ise gider tek bir kanala ait değildir (genel kasa, Ortak, dağılım bekliyor, belirsiz).
        (IReadOnlySet<int>? Kume, string Etiket) Kanal(IslemEntity i)
        {
            if (ekstre.TryGetValue(i.Id, out var satir))
                return satir.DagilimTuru == DagilimBicimleri.Genel ? (null, KanalEtiketleri.GenelKasa) : Belirli(Read<TakipKanalPayi>(satir.DagilimJson).Where(p => p.KanalId is not null).Select(p => p.KanalId!.Value));
            if (aylik.TryGetValue(i.Id, out var odeme) && revizyonlar.TryGetValue(odeme.RevizyonId, out var revizyon))
                return revizyon.DagilimTuru == DagilimBicimleri.Genel ? (null, KanalEtiketleri.GenelKasa) : Belirli(Read<KanalPayYaz>(revizyon.DagilimJson).Select(p => p.KanalId));
            if (bagliAlis.TryGetValue(i.Id, out var alis))
                return alis.Durum != AlisDurumlari.Onaylandi ? (null, KanalEtiketleri.DagilimBekliyor)
                    : Belirli(alisPaylari[alis.Id].TryGetValue(i.Id, out var paylar) ? paylar.Where(p => p.Tutar > 0).Select(p => p.KanalId) : []);
            if (i.KanalId is { } kanal)
                return Belirli([kanal]);
            return (null, string.IsNullOrWhiteSpace(i.Kanal) ? Belirsiz : i.Kanal);
        }
        (IReadOnlySet<int>? Kume, string Etiket) Belirli(IEnumerable<int> kanallar)
        {
            var (kume, etiket) = Kume(kanallar);
            return (kume, etiket ?? Belirsiz);
        }

        var sonuc = new List<BenzerKayitDto>();
        foreach (var i in giderler)
        {
            var (kume, etiket) = Kanal(i);
            var alis = bagliAlis.GetValueOrDefault(i.Id);
            if (Gorunur(a, kume) || a.AlisId is { } alisId && alis?.Id == alisId)
                sonuc.Add(new("Islem", i.Id, i.Tarih, i.TutarTl, i.Cari, i.KrediKartiId, alis?.Id, etiket, ekstre.GetValueOrDefault(i.Id)?.Id, aylik.GetValueOrDefault(i.Id)?.Id));
        }
        return sonuc;
    }

    private List<BenzerKayitDto> KartHarcamalari(BenzerAramasi a, DateOnly bas, DateOnly son, int kart)
    {
        // Giderden türeyen takip harcaması (IslemId) gider olarak zaten listelenir; aynı para iki kez gösterilmez.
        var harcamalar = db.TakipHarcamalar.AsNoTracking()
            .Where(h => h.KrediKartiId == kart && !h.Iptal && h.IslemId == null && h.Tutar == a.Tutar && h.Tarih >= bas && h.Tarih <= son).ToList();
        if (harcamalar.Count == 0)
            return [];
        var ids = harcamalar.Select(h => h.Id).ToArray();
        var ekstre = db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal && k.KartHarcamaId != null && ids.Contains(k.KartHarcamaId.Value))
            .Select(k => new { k.Id, Harcama = k.KartHarcamaId!.Value }).ToList().GroupBy(k => k.Harcama).ToDictionary(g => g.Key, g => g.First().Id);
        return harcamalar.Select(h => new BenzerKayitDto("KartHarcama", h.Id, h.Tarih, h.Tutar, h.Aciklama, h.KrediKartiId,
            KanalEtiketi: Kume(Read<KanalPayYaz>(h.DagilimJson).Select(p => p.KanalId)).Etiket, EkstreKayitId: ekstre.TryGetValue(h.Id, out var kayit) ? kayit : null)).ToList();
    }

    private List<(BenzerKayitDto, int)> KartOdemeleri(BenzerAramasi a, DateOnly bas, DateOnly son, int? kart)
    {
        var takip = db.TakipKartOdemeler.AsNoTracking().Where(p => !p.Iptal && p.Tutar == a.Tutar && p.Tarih >= bas && p.Tarih <= son);
        var eski = db.KartOdemeler.AsNoTracking().Where(p => p.Tutar == a.Tutar && p.Tarih >= bas && p.Tarih <= son);
        if (kart is { } id)
        { takip = takip.Where(p => p.KrediKartiId == id); eski = eski.Where(p => p.KrediKartiId == id); }
        var odemeler = takip.ToList();
        var eskiOdemeler = eski.ToList();
        if (odemeler.Count == 0 && eskiOdemeler.Count == 0)
            return [];
        var kartIds = odemeler.Select(p => p.KrediKartiId).Concat(eskiOdemeler.Select(p => p.KrediKartiId)).Distinct().ToArray();
        var kartAdlari = db.KrediKartlari.AsNoTracking().Where(k => kartIds.Contains(k.Id)).ToDictionary(k => k.Id, k => k.Ad);
        var odemeIds = odemeler.Select(p => p.Id).ToArray();
        var ekstre = db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal && k.KartOdemeId != null && odemeIds.Contains(k.KartOdemeId.Value))
            .Select(k => new { k.Id, Odeme = k.KartOdemeId!.Value }).ToList().GroupBy(k => k.Odeme).ToDictionary(g => g.Key, g => g.First().Id);
        string Aciklama(string? not, string varsayilan, int kartId) => $"{not ?? varsayilan} · {kartAdlari.GetValueOrDefault(kartId, $"Kart #{kartId}")}";
        return odemeler.Select(p => (new BenzerKayitDto("KartOdeme", p.Id, p.Tarih, p.Tutar, Aciklama(p.Not, "Kart ödemesi", p.KrediKartiId), p.KrediKartiId,
                EkstreKayitId: ekstre.TryGetValue(p.Id, out var kayit) ? kayit : null), 2))
            .Concat(eskiOdemeler.Select(p => (new BenzerKayitDto("EskiKartOdeme", p.Id, p.Tarih, p.Tutar, Aciklama(p.Not, "Eski kart ödemesi", p.KrediKartiId), p.KrediKartiId), 3)))
            .ToList();
    }

    private List<(BenzerKayitDto, int)> KrediTaksitleri(BenzerAramasi a, DateOnly bas, DateOnly son)
    {
        // Takipli kredinin yalnız takip taksidi kasadan düşer (HesapServisi): iptal edilmemiş ve kredisi takipte olanlar.
        var takipli = db.TakipKrediTaksitler.AsNoTracking()
            .Where(t => !t.Iptal && t.Tutar == a.Tutar && t.Tarih >= bas && t.Tarih <= son && db.TakipKrediler.Any(k => k.KrediId == t.KrediId)).ToList();
        var krediIds = takipli.Select(t => t.KrediId).Distinct().ToArray();
        var krediAdlari = db.Krediler.AsNoTracking().Where(k => krediIds.Contains(k.Id)).ToDictionary(k => k.Id, k => k.Ad);
        // Açıklama kaynağı kendisi adlandırır: bu kaynak türlerini tanımayan istemci kaydı "Gider #id" ya da "Kayıt #id" diye
        // gösterir; eski taksidin kimliği kredinindir ve açıklamada öyle yazılır.
        var sonuc = new List<(BenzerKayitDto, int)>();
        foreach (var t in takipli)
        {
            var (kume, etiket) = Kume(Read<KanalPayYaz>(t.DagilimJson).Select(p => p.KanalId));
            if (Gorunur(a, kume))
                sonuc.Add((new BenzerKayitDto("KrediTaksidi", t.Id, t.Tarih, t.Tutar, $"Kredi taksidi · {krediAdlari.GetValueOrDefault(t.KrediId, "Kredi")} · {t.No}. taksit", null, KanalEtiketi: etiket), 4));
        }
        sonuc.AddRange(EskiTaksitler().Where(t => t.Tutar == a.Tutar && t.Tarih >= bas && t.Tarih <= son && Gorunur(a, t.KanalId is { } kanal ? new HashSet<int> { kanal } : null))
            .Select(t => (new BenzerKayitDto("EskiKrediTaksidi", t.KrediId, t.Tarih, t.Tutar, $"Otomatik kredi taksidi (kredi #{t.KrediId}) · {t.Ad} · {t.No}. taksit", null, KanalEtiketi: t.Kanal), 5)));
        return sonuc;
    }

    /// <summary>Kanal süzgeci: süzgeçsiz sorguda, kanalı kesin olmayan (<paramref name="kume"/> null) ya da kanalı sorguyla
    /// kesişen kayıt görünür.</summary>
    private static bool Gorunur(BenzerAramasi a, IReadOnlySet<int>? kume) => a.Kanallar is null || kume is null || kume.Overlaps(a.Kanallar);

    /// <summary>Hesap motorunun takipsiz (ve takibe geçmeden önceki eski kayıt) kredilerden türettiği taksit giderleri;
    /// gerçekleşme takipli kredinin ödemeleri gider kaydıdır ve giderlerle bulunur. Kanal kimliği kredinin kanalıdır; kimliği
    /// olmayan eski kayıtta hesap motoru kanalı adıyla eşlediğinden ad kayıtlı kanala çözülür. Ortak ya da bilinmeyen ad null.</summary>
    private List<(int KrediId, int No, DateOnly Tarih, decimal Tutar, string Ad, string Kanal, int? KanalId)> EskiTaksitler()
    {
        if (_eskiTaksitler is not null)
            return _eskiTaksitler;
        var takip = db.TakipKrediler.AsNoTracking().ToDictionary(t => t.KrediId);
        _eskiTaksitler = [];
        foreach (var kredi in db.Krediler.AsNoTracking().Include(k => k.KanalKaydi).Where(k => !k.GerceklesmeTakibi).ToList())
        {
            takip.TryGetValue(kredi.Id, out var t);
            var taksitler = KrediTuretici.TaksitGiderleri(kredi.ToCore());
            var kanalId = kredi.KanalKaydi?.Id ?? KanalAdiyla(kredi.Kanal);
            for (var n = 0; n < taksitler.Count; n++)
                if (t is null || t.EskiKayit && taksitler[n].Tarih < t.Baslangic)
                    _eskiTaksitler.Add((kredi.Id, n + 1, taksitler[n].Tarih, taksitler[n].TutarTl, kredi.Ad, taksitler[n].Kanal, kanalId));
        }
        return _eskiTaksitler;
    }

    private int? KanalAdiyla(string ad) => Kanallari().Where(k => k.Value.Ad == ad).Select(k => (int?)k.Key).FirstOrDefault();
    private Dictionary<int, (string Ad, int Sira)> Kanallari() => _kanallar ??= db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => (k.Ad, k.Sira));

    /// <summary>Kanal kümesi ve sıralı adları; boş küme bir kanala ait değildir (null).</summary>
    private (IReadOnlySet<int>? Kume, string? Etiket) Kume(IEnumerable<int> ids)
    {
        var kume = ids.ToHashSet();
        if (kume.Count == 0)
            return (null, null);
        var kanallar = Kanallari();
        var adlar = kume.Select(id => kanallar.TryGetValue(id, out var k) ? k : ($"Kanal #{id}", int.MaxValue))
            .OrderBy(k => k.Item2).ThenBy(k => k.Item1, StringComparer.Ordinal).Select(k => k.Item1);
        return (kume, string.Join(" / ", adlar));
    }
}
