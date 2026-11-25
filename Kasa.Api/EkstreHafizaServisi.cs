using System.Globalization;
using Kasa.Api.Data;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api;

/// <summary>
/// İşlem hafızası (docs/specs/2026-11-25-islem-hafizasi.md): iptal edilmemiş ekstre kayıtları ve yok sayılan satırlar karardır; açıklamasının
/// değişmeyen kısmı ve yönü aynı olan satıra son karar önerilir. Ayrı tabloda tutulmaz, istek anında hesaplanır; yalnız unutulan anahtarlar
/// saklanır (<see cref="EkstreHafizaUnutulanEntity"/>). Öneri mali kayıt üretmez.
/// </summary>
public static class EkstreHafizaServisi
{
    /// <summary>Verilmiş bir karar: kaynak satırın anahtarı ve yönü ile kullanıcının yaptığı işlem. <paramref name="Aciklama"/> yalnız yeniden
    /// kullanılabilir düzeltilmiş açıklamadır. <see cref="EkstreIslemTurleri.Atla"/> yok sayma kararıdır.</summary>
    internal sealed record Karar(string Kaynak, string Banka, string HesapAdi, int? KartId, string Anahtar, string Yon, DateOnly Tarih, int BelgeId, int SatirNo,
        string IslemTuru, string DagilimTuru, IReadOnlyList<int> KanalIds, int? KrediKartiId, string? Aciklama)
    {
        /// <summary>İki kararın aynı sayıldığı alanlar.</summary>
        public string Imza { get; } = $"{IslemTuru}|{DagilimTuru}|{string.Join(',', KanalIds)}|{KrediKartiId}";
    }

    // Normalize edilmiş (büyük harf, Türkçe harfsiz) sözcükler. Yalnız bunlardan oluşan açıklama karşı tarafı söylemez.
    private static readonly HashSet<string> GenelSozcukler = new(StringComparer.Ordinal)
    {
        "ALACAK", "ALAN", "ATM", "BANKA", "BANKACILIGI", "BANKACILIK", "BORC", "BSMV", "CEK", "CEKME", "CEP", "DEKONT", "EFT", "FAST", "GELEN",
        "GIDEN", "GIRIS", "GONDERIM", "GONDERILEN", "HAREKET", "HAVALE", "HESABA", "HESAP", "HESAPTAN", "ILE", "INTERNET", "ISLEM", "ISLEMI",
        "KARSILIGI", "MOBIL", "NAKIT", "NOLU", "ODEME", "ODEMESI", "OTOMATIK", "PARA", "REF", "REFERANS", "SUBE", "SUBESI", "SUBEDEN", "TAHSILAT",
        "TALIMAT", "TARIHLI", "TRANSFER", "TRANSFERI", "TUTAR", "TUTARI", "VALOR", "VE", "YATAN", "YATIRMA", "CIKIS", "ACIKLAMA", "GONDEREN", "ALICI",
    };

    private static readonly HashSet<string> AyAdlari = new(StringComparer.Ordinal)
    {
        "OCAK", "SUBAT", "MART", "NISAN", "MAYIS", "HAZIRAN", "TEMMUZ", "AGUSTOS", "EYLUL", "EKIM", "KASIM", "ARALIK",
    };

    /// <summary>Açıklamanın karşılaştırılan kısmı: sayılar, referanslar, ay adları ve para birimi atılır. Ayırt edici sözcük kalmıyorsa null.</summary>
    public static string? Anahtar(string aciklama)
    {
        var sozcukler = EkstreKuralServisi.Normalize(aciklama).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s.Any(char.IsLetter) && s.Count(char.IsDigit) < 4 && !AyAdlari.Contains(s) && s is not ("TL" or "TRY")).ToList();
        return sozcukler.Any(s => s.Count(char.IsLetter) >= 3 || s.Any(char.IsDigit)) ? string.Join(' ', sozcukler) : null;
    }

    /// <summary>Anahtar yalnız genel bankacılık sözcüklerinden mi oluşuyor (karşı taraf belli değil).</summary>
    public static bool Genel(string anahtar) => anahtar.Split(' ').All(s => s.Length < 3 || GenelSozcukler.Contains(s));

    /// <summary>Düzeltilmiş açıklama başka hareketlere taşınabilir mi: rakam ve ay adı içermez.</summary>
    private static bool Tasinabilir(string aciklama) => !aciklama.Any(char.IsDigit)
        && !EkstreKuralServisi.Normalize(aciklama).Split(' ').Any(AyAdlari.Contains);

    /// <summary>Unutulmamış bütün kararlar.</summary>
    internal static List<Karar> Kararlar(KasaDbContext db)
    {
        var unutulan = db.EkstreHafizaUnutulanlar.AsNoTracking().ToDictionary(u => (u.Kaynak, u.Anahtar, u.Yon));
        var kayitlar = db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal)
            .Select(k => new { k.Id, k.BelgeId, k.SatirNo, k.Aciklama, k.IslemTuru, k.DagilimTuru, k.DagilimJson, k.KrediKartiId }).ToList().ToLookup(k => k.BelgeId);
        var yokSayilan = db.EkstreYokSayilanlar.AsNoTracking().ToList().ToLookup(y => y.BelgeId);
        var sonuc = new List<Karar>();
        foreach (var b in db.EkstreBelgeler.AsNoTracking().Where(b => db.EkstreKayitlar.Any(k => k.BelgeId == b.Id && !k.Iptal) || db.EkstreYokSayilanlar.Any(y => y.BelgeId == b.Id))
            .Select(b => new { b.Id, b.Kaynak, b.Banka, b.HesapAdi, b.KartId, b.SatirlarJson }).ToList())
        {
            var satirlar = Read<EkstreOkunanSatir>(b.SatirlarJson).ToDictionary(r => r.No);
            foreach (var k in kayitlar[b.Id])
            {
                if (!satirlar.TryGetValue(k.SatirNo, out var r) || Anahtar(r.Aciklama) is not { } anahtar
                    || unutulan.TryGetValue((b.Kaynak, anahtar, r.Yon), out var u) && k.Id <= u.SonKayitId)
                    continue;
                var (dagilim, kanallar) = Dagilim(k.IslemTuru, k.DagilimTuru, k.DagilimJson);
                var aciklama = k.Aciklama != GirdiDogrulama.Temizle(r.Aciklama).Trim() && Tasinabilir(k.Aciklama) ? k.Aciklama : null;
                sonuc.Add(new(b.Kaynak, b.Banka, b.HesapAdi, b.KartId, anahtar, r.Yon, r.Tarih ?? default, b.Id, k.SatirNo, k.IslemTuru, dagilim, kanallar,
                    b.Kaynak == EkstreKaynaklari.Banka && k.IslemTuru == EkstreIslemTurleri.KartOdemesi ? k.KrediKartiId : null, aciklama));
            }
            foreach (var y in yokSayilan[b.Id])
            {
                if (!satirlar.TryGetValue(y.SatirNo, out var r) || Anahtar(r.Aciklama) is not { } anahtar
                    || unutulan.TryGetValue((b.Kaynak, anahtar, r.Yon), out var u) && y.Id <= u.SonYokSayId)
                    continue;
                sonuc.Add(new(b.Kaynak, b.Banka, b.HesapAdi, b.KartId, anahtar, r.Yon, r.Tarih ?? default, b.Id, y.SatirNo, EkstreIslemTurleri.Atla, DagilimBicimleri.Genel, [], null, null));
            }
        }
        return sonuc;
    }

    /// <summary>Kararın dağılımı: tek kanallı özel dağılım eşit tek kanaldır; çok kanallı özel dağılımın tutarları taşınmaz.</summary>
    private static (string Tur, IReadOnlyList<int> Kanallar) Dagilim(string islemTuru, string dagilimTuru, string json)
    {
        if (islemTuru is not (EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider or EkstreIslemTurleri.KartHarcama) || dagilimTuru == DagilimBicimleri.Genel)
            return (dagilimTuru, []);
        var kanallar = Read<TakipKanalPayi>(json).Select(p => p.KanalId).OfType<int>().Distinct().Order().ToList();
        return (dagilimTuru == DagilimBicimleri.Ozel && kanallar.Count == 1 ? DagilimBicimleri.Esit : dagilimTuru, kanallar);
    }

    /// <summary>En yeni karar önce: hareket tarihi, sonra belge ve satır.</summary>
    internal static List<Karar> Sirala(IEnumerable<Karar> kararlar) =>
        [.. kararlar.OrderByDescending(k => k.Tarih).ThenByDescending(k => k.BelgeId).ThenByDescending(k => k.SatirNo)];

    /// <summary>Bir belgenin satırlarına öneri üreten okuma (kararlar bir kez okunur).</summary>
    internal sealed class Okuma(KasaDbContext db, EkstreBelgeEntity belge)
    {
        private readonly ILookup<(string Anahtar, string Yon), Karar> _kararlar = Kararlar(db).Where(k => k.Kaynak == belge.Kaynak).ToLookup(k => (k.Anahtar, k.Yon));
        private readonly HashSet<int> _kanallar = [.. db.Kanallar.AsNoTracking().Where(k => k.Aktif).Select(k => k.Id)];
        private readonly HashSet<int> _kartlar = [.. db.TakipKartlar.AsNoTracking().Select(k => k.KrediKartiId)];
        private readonly DateOnly _bugun = db.Bugunu();

        /// <summary>Satırın hafıza önerisi; hafızada yoksa null.</summary>
        internal EkstreKuralOnerisi? Oneri(EkstreOkunanSatir row)
        {
            if (Anahtar(row.Aciklama) is not { } anahtar)
                return null;
            var hepsi = _kararlar[(anahtar, row.Yon)].ToList();
            // En yakın kapsam: aynı hesap/kart, aynı banka, bütün bankalar.
            var kapsam = new[]
            {
                hepsi.Where(k => k.Banka == belge.Banka && k.HesapAdi == belge.HesapAdi && k.KartId == belge.KartId).ToList(),
                hepsi.Where(k => k.Banka == belge.Banka).ToList(), hepsi,
            }.FirstOrDefault(k => k.Count > 0);
            if (kapsam is null)
                return null;
            var sirali = Sirala(kapsam);
            var son = sirali[0];
            var ayni = sirali.TakeWhile(k => k.Imza == son.Imza).Count();
            if (ayni == 1 && sirali.Count > 1)
                return Sonuc(row, EkstreHafizaDurumlari.Kontrol, son, ayni, null, "Daha önce farklı işlendi (son iki karar aynı değil); kontrol edip kendiniz seçin.", doldur: false);
            if (ayni < 2 && Genel(anahtar))
                return null;
            var yeniAciklama = son.Aciklama is not null && (ayni < 2 || sirali[1].Aciklama == son.Aciklama) ? son.Aciklama : null;
            var ileti = $"Hafızadan: {ayni} kez {(son.IslemTuru == EkstreIslemTurleri.Atla ? "yok sayıldı" : "aynı işlendi")}, son {son.Tarih.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}.";
            var nedenler = Nedenler(row, son);
            return nedenler.Count == 0 ? Sonuc(row, EkstreHafizaDurumlari.Hazir, son, ayni, yeniAciklama, ileti)
                : Sonuc(row, EkstreHafizaDurumlari.Kontrol, son, ayni, yeniAciklama, $"{ileti} Kontrol edin: {string.Join("; ", nedenler)}.");
        }

        private EkstreKuralOnerisi Sonuc(EkstreOkunanSatir row, string durum, Karar k, int adet, string? aciklama, string ileti, bool doldur = true)
        {
            var kanalVar = k.KanalIds.All(_kanallar.Contains);
            return new(row.No, durum, [], doldur ? k.IslemTuru : null, doldur && kanalVar ? k.DagilimTuru : null, doldur && kanalVar ? k.KanalIds : [], ileti,
                new EkstreHafizaOnerisi(k.Anahtar, adet, k.Tarih, doldur && k.KrediKartiId is { } kart && _kartlar.Contains(kart) ? kart : null, doldur ? aciklama : null));
        }

        /// <summary>Toplu onaya neden girmez (boşsa hazır). Yok sayma kararı mali etkisiz ve geri alınabilir olduğundan hep hazırdır.</summary>
        private List<string> Nedenler(EkstreOkunanSatir row, Karar k)
        {
            var nedenler = new List<string>();
            if (k.IslemTuru == EkstreIslemTurleri.Atla)
                return nedenler;
            if (row.ParaBirimi is not ("TRY" or "TL") || row.Tarih is not { } tarih || !GirdiDogrulama.GecerliKayitTarihi(tarih, _bugun)
                || row.Tutar is not > 0 or > 999_999_999_999.99m || decimal.Round(row.Tutar.Value, 2) != row.Tutar)
                nedenler.Add("tarih, tutar ya da TL bilgisi kesin değil");
            if (row.Uyarilar.Count > 0)
                nedenler.Add("satırda okuma uyarısı var");
            if (row.Yon is not ("Giris" or "Cikis"))
                nedenler.Add("giriş/çıkış yönü okunamadı");
            else if (Yon(k.IslemTuru, belge.Kaynak) is { } yon && yon != row.Yon)
                nedenler.Add("okunan yön hafızadaki işlemle çelişiyor");
            if (row.Sinif == "Transfer" && k.IslemTuru is EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider)
                nedenler.Add("hesaplar arası transfer olabilir");
            if (!k.KanalIds.All(_kanallar.Contains))
                nedenler.Add("kanalı pasif ya da silinmiş");
            if (k.DagilimTuru == DagilimBicimleri.Ozel)
                nedenler.Add("kanal tutarları o satıra özeldi, yeniden girin");
            if (k.IslemTuru == EkstreIslemTurleri.KartOdemesi && belge.Kaynak == EkstreKaynaklari.Banka && (k.KrediKartiId is not { } kart || !_kartlar.Contains(kart)))
                nedenler.Add("kartı takipte değil, kart seçin");
            if (k.IslemTuru == EkstreIslemTurleri.KartIade)
                nedenler.Add("iadenin kaynak harcamasını seçin");
            if (k.IslemTuru == EkstreIslemTurleri.Eslestir)
                nedenler.Add("eşleşecek kaydı seçin");
            return nedenler;
        }
    }

    /// <summary>İşlem türünün beklenen yönü; eşleştirmede yok.</summary>
    internal static string? Yon(string islemTuru, string kaynak) => islemTuru switch
    {
        EkstreIslemTurleri.Gelir or EkstreIslemTurleri.KartIade => "Giris",
        EkstreIslemTurleri.Gider or EkstreIslemTurleri.KartHarcama => "Cikis",
        EkstreIslemTurleri.KartOdemesi => kaynak == EkstreKaynaklari.Kart ? "Giris" : "Cikis",
        _ => null
    };

    /// <summary>Hafıza listesi: anahtar ve yön başına son karar, en yeni önce.</summary>
    internal static List<EkstreHafizaDto> Liste(KasaDbContext db) => Kararlar(db).GroupBy(k => (k.Kaynak, k.Anahtar, k.Yon)).Select(g =>
    {
        var sirali = Sirala(g);
        var son = sirali[0];
        var ayni = sirali.TakeWhile(k => k.Imza == son.Imza).Count();
        return new EkstreHafizaDto(son.Kaynak, son.Anahtar, son.Yon, son.IslemTuru, son.DagilimTuru, son.KanalIds, son.KrediKartiId, son.Aciklama, sirali.Count, son.Tarih, ayni == 1 && sirali.Count > 1);
    }).OrderByDescending(h => h.SonTarih).ThenBy(h => h.Anahtar, StringComparer.Ordinal).Take(500).ToList();

    /// <summary>Anahtarın bugüne kadarki kararlarını hafızadan çıkarır; kayıtlar değişmez.</summary>
    internal static void Unut(KasaDbContext db, EkstreHafizaUnutYaz dto)
    {
        var u = db.EkstreHafizaUnutulanlar.SingleOrDefault(u => u.Kaynak == dto.Kaynak && u.Anahtar == dto.Anahtar && u.Yon == dto.Yon);
        if (u is null)
            db.EkstreHafizaUnutulanlar.Add(u = new() { Kaynak = dto.Kaynak, Anahtar = dto.Anahtar, Yon = dto.Yon });
        u.SonKayitId = db.EkstreKayitlar.Select(k => (int?)k.Id).Max() ?? 0;
        u.SonYokSayId = db.EkstreYokSayilanlar.Select(y => (int?)y.Id).Max() ?? 0;
    }
}
