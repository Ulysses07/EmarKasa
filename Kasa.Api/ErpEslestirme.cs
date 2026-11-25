using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api;

/// <summary>
/// ERP12 cari hareketleri (docs/specs/2026-11-25-erp12-aktarimi.md): aktarım özeti, kasadaki karşılığın bulunması ve satır önerileri. Eşleşme
/// yalnız bağdır; kasa, kart borcu ve raporlar değişmez.
/// </summary>
public static class ErpEslestirme
{
    /// <summary>Nakit satırın sınıfı (EkstreOkunanSatir.Sinif); diğerleri <see cref="BankaSinifi"/> ya da <see cref="CekSinifi"/>.</summary>
    public const string NakitSinifi = "ErpNakitSatir", BankaSinifi = "ErpBankaSatir", CekSinifi = "ErpCekSatir";
    /// <summary>ERP12 belgesinin banka alanı (belge bir bankaya ait değildir).</summary>
    public const string Banka = "ERP12";
    private const int EnCokAday = 20;

    // Ünvanlarda ayırt edici olmayan sözcükler (normalize edilmiş).
    private static readonly HashSet<string> UnvanSozcukleri = new(StringComparer.Ordinal)
    {
        "TICARET", "SANAYI", "LIMITED", "ANONIM", "SIRKETI", "SIRKET", "PAZARLAMA", "ITHALAT", "IHRACAT", "HIZMETLERI", "TURIZM", "INSAAT", "GIDA", "TEKSTIL",
        "FAST", "HAVALE", "GELEN", "GIDEN", "ODEME", "ODEMESI", "TAHSILAT", "GONDERIM", "GONDERILEN", "PARA", "TRANSFERI", "INTERNET", "MOBIL", "BANKA",
    };

    /// <summary>Carinin kimliği: kodu varsa kodu, yoksa sadeleştirilmiş adı.</summary>
    public static string CariAnahtari(string? kod, string ad) => EkstreKuralServisi.Normalize(kod ?? "") is { Length: > 0 } k ? "K:" + k : "A:" + EkstreKuralServisi.Normalize(ad);

    /// <summary>Hareketin içerik özeti; aynı içerik aynı dosyada birden çoksa <paramref name="sira"/> ile ayrılır.</summary>
    internal static string Ozet(ErpSatir s, int sira)
    {
        string N(string? m) => EkstreKuralServisi.Normalize(m ?? "");
        string P(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);
        var icerik = string.Join('|', s.Tarih.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), CariAnahtari(s.CariKodu, s.CariAdi), N(s.IslemTuru), N(s.BelgeNo), N(s.Aciklama), P(s.Borc), P(s.Alacak));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{icerik}#{sira}")));
    }

    /// <summary>Kasadaki olası karşılık. <paramref name="Ekstre"/>: banka/kart ekstresinden gelmiş kayıt.</summary>
    internal sealed record Aday(string Tur, int Id, DateOnly? Tarih, decimal Tutar, string Aciklama, bool Ekstre, int? EkstreKayitId = null, int? KrediKartiId = null);

    /// <summary>Bir tarih aralığının karşılık adayları (başka ERP12 satırına bağlanmamış), bir kez okunur.</summary>
    internal sealed class Havuz
    {
        private readonly ILookup<(string Yon, decimal Tutar), Aday> _hareketler;
        private readonly ILookup<(string Yon, decimal Tutar), Aday> _cekler;

        internal Havuz(KasaDbContext db, DateOnly ilk, DateOnly son, IReadOnlySet<(string Tur, int Id)> bagli)
        {
            var bas = ilk.AddDays(-BenzerKayitServisi.GunPenceresi);
            var bit = son.AddDays(BenzerKayitServisi.GunPenceresi);
            var erp = db.EkstreBelgeler.AsNoTracking().Where(b => b.Kaynak == EkstreKaynaklari.Erp).Select(b => b.Id).ToHashSet();
            var kayitlar = db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal && (k.IslemId != null || k.KartHarcamaId != null || k.IslemTuru == EkstreIslemTurleri.Gelir) && k.Tarih >= bas && k.Tarih <= bit)
                .Select(k => new { k.Id, k.BelgeId, k.IslemId, k.KartHarcamaId, k.IslemTuru, k.Tarih, k.Tutar, k.Aciklama }).ToList();
            var erpGiderleri = kayitlar.Where(k => k.IslemId != null && erp.Contains(k.BelgeId)).Select(k => k.IslemId!.Value).ToHashSet();
            var ekstre = kayitlar.Where(k => k.IslemId != null).GroupBy(k => k.IslemId!.Value).ToDictionary(g => g.Key, g => g.First().Id);
            var harcamaKaydi = kayitlar.Where(k => k.KartHarcamaId != null).GroupBy(k => k.KartHarcamaId!.Value).ToDictionary(g => g.Key, g => g.First().Id);
            var adaylar = new List<(string Yon, Aday Aday)>();
            foreach (var i in db.Islemler.AsNoTracking().Where(i => i.Tarih >= bas && i.Tarih <= bit && i.TutarTl > 0).Select(i => new { i.Id, i.Tarih, i.TutarTl, i.Cari, i.KrediKartiId }).ToList())
                if (!erpGiderleri.Contains(i.Id))
                    adaylar.Add(("Cikis", new(EslesmeTurleri.Gider, i.Id, i.Tarih, i.TutarTl, i.Cari, ekstre.ContainsKey(i.Id) || i.KrediKartiId is not null, ekstre.TryGetValue(i.Id, out var kayit) ? kayit : null, i.KrediKartiId)));
            foreach (var h in db.TakipHarcamalar.AsNoTracking().Where(h => !h.Iptal && h.IslemId == null && h.Tarih >= bas && h.Tarih <= bit).Select(h => new { h.Id, h.Tarih, h.Tutar, h.Aciklama, h.KrediKartiId }).ToList())
                if (h.Tutar > 0)
                    adaylar.Add(("Cikis", new(EslesmeTurleri.KartHarcama, h.Id, h.Tarih, h.Tutar, h.Aciklama, true, harcamaKaydi.TryGetValue(h.Id, out var kayit) ? kayit : null, h.KrediKartiId)));
            foreach (var k in kayitlar.Where(k => k.IslemTuru == EkstreIslemTurleri.Gelir && !erp.Contains(k.BelgeId)))
                adaylar.Add(("Giris", new(EslesmeTurleri.EkstreGelir, k.Id, k.Tarih, k.Tutar, k.Aciklama, true, k.Id)));
            _hareketler = adaylar.Where(a => !bagli.Contains((a.Aday.Tur, a.Aday.Id))).ToLookup(a => (a.Yon, a.Aday.Tutar), a => a.Aday);
            _cekler = db.Cekler.AsNoTracking().ToList().Where(c => !bagli.Contains((EslesmeTurleri.CekKaydi, c.Id)))
                .Select(c => (Yon: c.Yon == CekYonleri.Alinan ? "Giris" : "Cikis", Aday: new Aday(EslesmeTurleri.CekKaydi, c.Id, c.VadeTarihi, c.Tutar, $"{c.Kisi} · {c.Banka ?? "—"} · {c.No} · vade {c.VadeTarihi:dd.MM.yyyy}", false)))
                .ToLookup(a => (a.Yon, a.Aday.Tutar), a => a.Aday);
        }

        /// <summary>Satırın adayları: aynı yön ve tutar. Çek satırı yalnız çek kaydıyla (tarihe bakılmaz: ERP12 günü verildiği gündür), diğerleri en çok
        /// 3 gün farkla; nakit satır ekstreden gelen kayıtla eşleşmez.</summary>
        internal List<Aday> Bul(string sinif, string yon, DateOnly tarih, decimal tutar) => (sinif == CekSinifi ? _cekler[(yon, tutar)]
                : _hareketler[(yon, tutar)].Where(a => Math.Abs(a.Tarih!.Value.DayNumber - tarih.DayNumber) <= BenzerKayitServisi.GunPenceresi && !(sinif == NakitSinifi && a.Ekstre)))
            .OrderBy(a => Math.Abs(a.Tarih!.Value.DayNumber - tarih.DayNumber)).ThenByDescending(a => a.Id).Take(EnCokAday).ToList();
    }

    /// <summary>Cari adının ayırt edici bir sözcüğü kaydın açıklamasında geçiyor mu.</summary>
    internal static bool AdUyusuyor(string cari, string aciklama)
    {
        static HashSet<string> Sozcukler(string metin) => [.. EkstreKuralServisi.Normalize(metin).Split(' ').Where(s => s.Length >= 4 && s.Any(char.IsLetter) && !UnvanSozcukleri.Contains(s))];
        return Sozcukler(cari).Overlaps(Sozcukler(aciklama));
    }

    internal static EkstreEslesmeAdayiDto Dto(Aday a) => new(a.Tur, a.Id, a.Tarih ?? default, a.Tutar, a.Aciklama, a.KrediKartiId, null, null, a.EkstreKayitId);

    /// <summary>ERP12 belgesinin bekleyen satırlarına öneri (EkstreKuralOnerisi; <see cref="ErpOneriDurumlari"/> ya da nakitte işlem hafızası).</summary>
    internal static List<EkstreKuralOnerisi> Oneriler(KasaDbContext db, EkstreBelgeEntity belge, IReadOnlySet<(string Tur, int Id)> bagli)
    {
        var satirlar = Read<EkstreOkunanSatir>(belge.SatirlarJson);
        var kayitli = db.EkstreKayitlar.AsNoTracking().Where(k => k.BelgeId == belge.Id && !k.Iptal).Select(k => k.SatirNo).ToHashSet();
        var yokSayilan = db.EkstreYokSayilanlar.AsNoTracking().Where(y => y.BelgeId == belge.Id).Select(y => y.SatirNo).ToHashSet();
        var acik = satirlar.Where(r => !kayitli.Contains(r.No) && !yokSayilan.Contains(r.No)).ToList();
        EkstreKuralOnerisi Durum(EkstreOkunanSatir r, string durum, string ileti, string? tur = null, IReadOnlyList<Aday>? adaylar = null) =>
            new(r.No, durum, [], tur, tur == EkstreIslemTurleri.Eslestir ? DagilimBicimleri.Eslesme : null, [], ileti, null, adaylar?.Select(Dto).ToList());
        if (acik.Count == 0)
            return satirlar.Select(r => Durum(r, kayitli.Contains(r.No) ? "Kayitli" : "YokSayildi", kayitli.Contains(r.No) ? "Bu satır zaten kaydedildi." : "Bu satır yok sayıldı.")).ToList();
        var havuz = new Havuz(db, acik.Min(r => r.Tarih!.Value), acik.Max(r => r.Tarih!.Value), bagli);
        var hafiza = new EkstreHafizaServisi.Okuma(db, belge);
        var bulunan = acik.ToDictionary(r => r.No, r =>
        {
            var adaylar = r.Yon is "Giris" or "Cikis" ? havuz.Bul(r.Sinif, r.Yon, r.Tarih!.Value, r.Tutar!.Value) : [];
            var uyusan = adaylar.Where(a => AdUyusuyor(r.Aciklama, a.Aciklama)).ToList();
            return (Adaylar: adaylar, Kesin: uyusan.Count == 1 ? uyusan[0] : null);
        });
        // Aynı kaydı isteyen iki satırın ikisi de kesin sayılmaz.
        var paylasilan = bulunan.Values.Where(b => b.Kesin is not null).GroupBy(b => (b.Kesin!.Tur, b.Kesin.Id)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        return satirlar.Select(r =>
        {
            if (kayitli.Contains(r.No))
                return Durum(r, "Kayitli", "Bu satır zaten kaydedildi.");
            if (yokSayilan.Contains(r.No))
                return Durum(r, "YokSayildi", "Bu satır yok sayıldı.");
            var (adaylar, kesin) = bulunan[r.No];
            if (kesin is not null && !paylasilan.Contains((kesin.Tur, kesin.Id)) && r.Uyarilar.Count == 0)
                return Durum(r, ErpOneriDurumlari.Eslesme, $"Kasadaki karşılığı: {Metin(kesin)}.", EkstreIslemTurleri.Eslestir, [kesin, .. adaylar.Where(a => a != kesin)]);
            if (adaylar.Count > 0)
                return Durum(r, ErpOneriDurumlari.Secim, $"{adaylar.Count} olası karşılık var ama kesin değil (ilki: {Metin(adaylar[0])}); kontrol edip seçin.", EkstreIslemTurleri.Eslestir, adaylar);
            if (hafiza.Oneri(r) is { } hatira && (hatira.IslemTuru == EkstreIslemTurleri.Atla || r.Sinif == NakitSinifi && hatira.IslemTuru is EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider))
                return hatira;
            if (r.Sinif == NakitSinifi)
                return Durum(r, ErpOneriDurumlari.Nakit, "Nakit: kasada karşılığı yok. Kasaya yazılacaksa kanal dağılımını seçip kaydedin; değilse yok sayın.",
                    r.Yon == "Giris" ? EkstreIslemTurleri.Gelir : r.Yon == "Cikis" ? EkstreIslemTurleri.Gider : null);
            return Durum(r, ErpOneriDurumlari.Bekle, r.Sinif == CekSinifi ? "Çekler ekranında karşılığı yok; çek girilince eşleşir."
                : "Kasada karşılığı yok; banka ya da kart ekstresi yüklenince eşleşir.");
        }).ToList();
    }

    private static string Metin(Aday a) => $"{TurAdi(a.Tur)} #{a.Id} · {a.Tarih?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} · {a.Aciklama}";
    private static string TurAdi(string tur) => tur switch
    {
        EslesmeTurleri.Gider => "Gider",
        EslesmeTurleri.KartHarcama => "Kart harcaması",
        EslesmeTurleri.EkstreGelir => "Banka girişi",
        EslesmeTurleri.CekKaydi => "Çek",
        _ => tur
    };

    /// <summary>Verilen kayıtlara (tür ve kimlik) eşleşmiş carilerin adı.</summary>
    internal static Dictionary<int, string> Cariler(KasaDbContext db, string eslesmeTuru) =>
        (from k in db.EkstreKayitlar.AsNoTracking()
         where !k.Iptal && k.IslemTuru == EkstreIslemTurleri.Eslestir && k.EslesmeTuru == eslesmeTuru && k.EslesmeId != null
         join h in db.ErpHareketler.AsNoTracking() on new { k.BelgeId, SatirNo = (int?)k.SatirNo } equals new { h.BelgeId, h.SatirNo }
         select new { Id = k.EslesmeId!.Value, h.CariAdi }).AsEnumerable().GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().CariAdi);
}
