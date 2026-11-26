using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kasa.Api.Data;
using Kasa.Api.Servisler;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api.YapayZeka;

/// <summary>
/// Yapay zekâ önerileri (docs/specs/2026-11-25-yapay-zeka-onerileri.md): sorulacak satırların ve maskeli isteğin hazırlanması, harcama sınırı, yanıtın
/// doğrulanması ve ölçüm. Öneri mali kayıt üretmez; hiçbir satır kendiliğinden seçilmez.
/// </summary>
public sealed partial class YapayZekaServisi(YapayZekaAyarlari ayarlar, IYapayZekaIstemcisi istemci, TimeProvider saat)
{
    private const string BelirsizTur = "Belirsiz", KanalDagilimi = "Kanal";
    private readonly SemaphoreSlim _tek = new(1, 1);

    public YapayZekaAyarlari Ayarlar => ayarlar;
    public Task<YapayZekaYaniti> SorAsync(YapayZekaIstegi istek) => istemci.SorAsync(istek, CancellationToken.None);

    /// <summary>Aynı anda tek çağrı (harcama sınırı paralel çağrılarla aşılamaz); meşgulse null.</summary>
    internal IDisposable? TekCagri() => _tek.Wait(0) ? new Birakici(_tek) : null;
    private sealed class Birakici(SemaphoreSlim kilit) : IDisposable { public void Dispose() => kilit.Release(); }

    // ---- Harcama ----

    internal string Ay() => TimeZoneInfo.ConvertTime(saat.GetUtcNow(), KasaSaati.Istanbul).ToString("yyyy-MM", CultureInfo.InvariantCulture);
    internal static decimal AyHarcamasi(KasaDbContext db, string ay) => db.YapayZekaCagrilar.AsNoTracking().Where(c => c.Ay == ay).Select(c => c.MaliyetUsd).ToList().Sum();

    /// <summary>Çağrıyı en yüksek maliyetiyle yazar; ayın sınırı aşılacaksa null.</summary>
    internal YapayZekaCagriEntity? Ayir(KasaDbContext db, Guid istekId, string tur, int? belgeId, int satir, YapayZekaIstegi istek)
    {
        var enYuksek = YapayZekaAyarlari.EnYuksekMaliyet(istek.Karakter);
        if (AyHarcamasi(db, Ay()) + enYuksek > ayarlar.AylikSinirUsd)
            return null;
        var cagri = new YapayZekaCagriEntity
        {
            IstekId = istekId,
            Tur = tur,
            BelgeId = belgeId,
            Ay = Ay(),
            Zaman = saat.GetUtcNow().ToUnixTimeMilliseconds(),
            Model = YapayZekaAyarlari.Model,
            SatirSayisi = satir,
            MaliyetUsd = enYuksek,
            Durum = YapayZekaCagriDurumlari.Basladi,
        };
        db.YapayZekaCagrilar.Add(cagri);
        db.SaveChanges();
        return cagri;
    }

    internal static void Bitir(YapayZekaCagriEntity cagri, YapayZekaYaniti yanit)
    {
        cagri.Durum = YapayZekaCagriDurumlari.Tamam;
        cagri.GirdiJetonu = yanit.GirdiJetonu + yanit.OnbellekYazma + yanit.OnbellekOkuma;
        cagri.CiktiJetonu = yanit.CiktiJetonu;
        cagri.MaliyetUsd = YapayZekaAyarlari.Maliyet(yanit.GirdiJetonu, yanit.CiktiJetonu, yanit.OnbellekYazma, yanit.OnbellekOkuma);
    }

    /// <summary>Yapılamayan çağrı: sonucu bilinmiyorsa ayrılan maliyet harcanmış sayılır.</summary>
    internal static void Bitir(YapayZekaCagriEntity cagri, YapayZekaHatasi hata)
    {
        cagri.Durum = hata.SonucBilinmiyor ? YapayZekaCagriDurumlari.Bilinmiyor : YapayZekaCagriDurumlari.Hata;
        if (!hata.SonucBilinmiyor)
            cagri.MaliyetUsd = 0;
    }

    // ---- İstek ----

    /// <summary>Sorulan satır grubu: maskeli metni, yönü ve tutar aralığı aynı olan satırlar bir kez sorulur.</summary>
    internal sealed record SoruSatiri(int No, string Yon, string Aralik, YapayZekaMaskesi.Maskeli Metin, IReadOnlyList<EkstreOkunanSatir> Satirlar)
    {
        internal string Yazi => $"{No} | {YonYazisi(Yon)} | {Aralik} | {Metin.Metin}";
    }

    /// <param name="Satirlar">Bu çağrıda sorulacaklar.</param>
    /// <param name="Kalan">Sonraki çağrıya kalan, hiç sorulmamış satır sayısı.</param>
    internal sealed record Hazirlik(string Kaynak, IReadOnlyList<SoruSatiri> Satirlar, int Kalan, int OrnekSayisi, YapayZekaIstegi? Istek);

    private static string YonYazisi(string yon) => yon switch { "Giris" => "GIRIS", "Cikis" => "CIKIS", _ => "BELIRSIZ" };

    /// <summary>Belgenin kural ve hafızayla tanınmayan, önerisi olmayan bekleyen TL satırlarından istek. Para birimi okunamayan satır da sorulur
    /// (uygulama onu TL sayıp seçtirir; kart ekstrelerinde çoğu satır böyledir); dövizli satır sorulmaz. Tür için tarih ve kesin tutar gerekmez:
    /// tutarı okunamayan satırın aralığı "?" gider.</summary>
    /// <param name="yalnizSorulmamis">Daha önce sorulmuş (yanıt alamamış) satırlar dışarıda kalır.</param>
    internal static Hazirlik Hazirla(KasaDbContext db, EkstreBelgeEntity belge, bool yalnizSorulmamis = false)
    {
        var satirlar = Read<EkstreOkunanSatir>(belge.SatirlarJson).ToDictionary(r => r.No);
        // Öneri gelmemiş satır yeniden sorulabilir ama hiç sorulmamışlardan sonra; yoksa kalan satırlara hiç sıra gelmezdi (Codex incelemesi, EmarKasa#65).
        var sorulmus = db.YapayZekaOneriler.AsNoTracking().Where(o => o.BelgeId == belge.Id).ToDictionary(o => o.SatirNo, o => o.CagriId);
        var acik = EkstreKuralServisi.Oneriler(db, belge).Where(o => o.Durum == "Yok" && !(yalnizSorulmamis && sorulmus.ContainsKey(o.SatirNo))).Select(o => satirlar[o.SatirNo])
            .Where(r => r.ParaBirimi is "TRY" or "TL" or "Belirsiz" && r.Tutar is null or > 0).ToList();
        var maske = new YapayZekaMaskesi();
        var kanallar = Kanallar(db, maske);
        var ornekler = Ornekler(Kararlar(db, belge.Kaynak).Take(YapayZekaAyarlari.EnCokOrnek), maske);
        var ornekYerTutuculari = ornekler.SelectMany(o => YerTutucu().Matches(o).Select(m => m.Value)).ToHashSet(StringComparer.Ordinal);
        var gruplar = acik.Select(r => (Satir: r, Metin: maske.Maskele(r.Aciklama), Aralik: YapayZekaMaskesi.TutarAraligi(r.Tutar)))
            // Hiç açık sözcüğü olmayan ve örneklerde geçmeyen satır modele bir şey söylemez.
            .Where(x => x.Metin.Metin.Split(' ').Any(s => s.Any(char.IsLetter) || ornekYerTutuculari.Contains(s)))
            .GroupBy(x => (x.Satir.Yon, x.Aralik, x.Metin.Metin))
            .Select(g => new SoruSatiri(g.First().Satir.No, g.Key.Yon, g.Key.Aralik, g.First().Metin, g.Select(x => x.Satir).ToList()))
            .OrderBy(g => g.Satirlar.Min(r => sorulmus.GetValueOrDefault(r.No))).ThenBy(g => g.No).ToList();
        var soru = gruplar.Take(YapayZekaAyarlari.EnCokSatir).ToList();
        var kalan = gruplar.Skip(soru.Count).SelectMany(g => g.Satirlar).Count(r => !sorulmus.ContainsKey(r.No));
        return new(belge.Kaynak, soru, kalan, ornekler.Count, soru.Count == 0 ? null : Istek(belge.Kaynak, kanallar, ornekler, soru.Select(s => s.Yazi)));
    }

    private static YapayZekaIstegi Istek(string kaynak, IReadOnlyList<(int Id, string Ad)> kanallar, IReadOnlyList<string> ornekler, IEnumerable<string> satirlar)
    {
        var baglam = new StringBuilder("Kanallar (numara = ad):\n");
        foreach (var (id, ad) in kanallar)
            baglam.Append(id).Append(" = ").Append(ad).Append('\n');
        baglam.Append("\nGeçmiş kararlar (yön | açıklama => karar):\n");
        foreach (var o in ornekler)
            baglam.Append(o).Append('\n');
        if (ornekler.Count == 0)
            baglam.Append("(henüz yok)\n");
        var soru = $"Belge: {(kaynak == EkstreKaynaklari.Kart ? "kredi kartı ekstresi" : "banka hesap hareketleri")}\nSatırlar (numara | yön | tutar aralığı TL | açıklama):\n{string.Join('\n', satirlar)}";
        return new(Yonerge, baglam.ToString(), soru, Sema(kaynak, kanallar));
    }

    private const string Yonerge = """
        Bir Türk perakende şirketinin banka ve kart hareketlerini sınıflandırıyorsun. Açıklamalar gizlilik için maskelidir: [n] gizlenmiş bir sözcüktür (kişi, firma ya da
        yer adı olabilir; aynı numara aynı sözcüktür), # gizlenmiş bir sayıdır. Yalnız verilen bilgiyle karar ver, tahmin ettiğin adı yazma.

        Türler:
        Gelir: hesaba gelen satış, tahsilat, POS, pazaryeri ödemesi, kira ya da faiz geliri.
        Gider: hesaptan çıkan masraf, fatura, vergi, maaş, kira, kargo, tedarikçi ödemesi.
        KartHarcama: kart ekstresindeki alışveriş, faiz ya da ücret. KartIade: karta iade. KartOdemesi: kredi kartı borcunun ödenmesi.
        Atla: kasayı değiştirmeyen hareket (kendi hesapları arası virman, vadeli hesap açma ya da kapama, bloke ve çözümü, bilgi satırı).
        Belirsiz: verilen bilgi yetmiyorsa. Emin değilsen Belirsiz yaz.
        Yön GIRIS ise Gelir, KartIade ya da kart ekstresinde KartOdemesi; yön CIKIS ise Gider, KartHarcama ya da banka belgesinde KartOdemesi olabilir.

        Dağılım: geçmiş kararlarda aynı tür hareket hangi kanala yazıldıysa Kanal ve numaraları; şirketin geneline aitse Genel; bilinmiyorsa Belirsiz.
        Güven: geçmiş karar ya da açıklama açıkça gösteriyorsa Yuksek, güçlü ipucu varsa Orta, yoksa Dusuk.
        Kategori: en çok üç sözcük (örnek: kargo, akaryakıt, banka masrafı). Açıklama: muhasebe için kısa ve düzgün Türkçe, en çok 60 karakter; gizli sözcükleri [n]
        biçiminde aynen kullan, sayı yazma; düzeltmeye gerek yoksa boş bırak. Her satır numarası için tam bir yanıt ver.
        """;

    private static string Sema(string kaynak, IReadOnlyList<(int Id, string Ad)> kanallar)
    {
        static JsonObject Metin(params string[] secenekler) => new() { ["type"] = "string", ["enum"] = new JsonArray(secenekler.Select(s => (JsonNode)s).ToArray()) };
        var satir = new JsonObject
        {
            ["no"] = new JsonObject { ["type"] = "integer" },
            ["tur"] = Metin([.. Turler(kaynak), BelirsizTur]),
            ["dagilim"] = Metin(DagilimBicimleri.Genel, KanalDagilimi, BelirsizTur),
            ["kanallar"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = kanallar.Count == 0 ? new JsonObject { ["type"] = "integer" } : new JsonObject { ["type"] = "integer", ["enum"] = new JsonArray(kanallar.Select(k => (JsonNode)k.Id).ToArray()) },
            },
            ["guven"] = Metin(YapayZekaGuvenleri.Yuksek, YapayZekaGuvenleri.Orta, YapayZekaGuvenleri.Dusuk),
            ["kategori"] = new JsonObject { ["type"] = "string" },
            ["aciklama"] = new JsonObject { ["type"] = "string" },
        };
        JsonObject Nesne(JsonObject alanlar) => new() { ["type"] = "object", ["properties"] = alanlar, ["required"] = new JsonArray(alanlar.Select(a => (JsonNode)a.Key).ToArray()), ["additionalProperties"] = false };
        return Nesne(new() { ["satirlar"] = new JsonObject { ["type"] = "array", ["items"] = Nesne(satir) } }).ToJsonString();
    }

    private static string[] Turler(string kaynak) => kaynak == EkstreKaynaklari.Kart
        ? [EkstreIslemTurleri.KartHarcama, EkstreIslemTurleri.KartIade, EkstreIslemTurleri.KartOdemesi, EkstreIslemTurleri.Atla]
        : [EkstreIslemTurleri.Gelir, EkstreIslemTurleri.Gider, EkstreIslemTurleri.KartOdemesi, EkstreIslemTurleri.Atla];

    private static List<(int Id, string Ad)> Kanallar(KasaDbContext db, YapayZekaMaskesi maske) =>
        db.Kanallar.AsNoTracking().Where(k => k.Aktif).OrderBy(k => k.Sira).ThenBy(k => k.Id).Select(k => new { k.Id, k.Ad }).ToList().Select(k => (k.Id, maske.Maskele(k.Ad).Metin)).ToList();

    /// <summary>Çelişmeyen son kararlar (anahtar ve yön başına bir), en yeni önce.</summary>
    private static List<EkstreHafizaServisi.Karar> Kararlar(KasaDbContext db, string kaynak) =>
        EkstreHafizaServisi.Kararlar(db).Where(k => k.Kaynak == kaynak && Turler(kaynak).Contains(k.IslemTuru)).GroupBy(k => (k.Anahtar, k.Yon))
            .Select(g => EkstreHafizaServisi.Sirala(g)).Where(g => g.Count == 1 ? !EkstreHafizaServisi.Genel(g[0].Anahtar) : g[0].Imza == g[1].Imza).Select(g => g[0])
            .OrderByDescending(k => k.Tarih).ThenByDescending(k => k.BelgeId).ThenByDescending(k => k.SatirNo).ToList();

    private static List<string> Ornekler(IEnumerable<EkstreHafizaServisi.Karar> kararlar, YapayZekaMaskesi maske) =>
        kararlar.Select(k => $"{YonYazisi(k.Yon)} | {maske.Maskele(k.Anahtar).Metin} => {k.IslemTuru}{DagilimYazisi(k.IslemTuru, k.DagilimTuru, k.KanalIds)}").ToList();

    private static string DagilimYazisi(string tur, string dagilim, IReadOnlyList<int> kanallar) =>
        tur is not (EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider or EkstreIslemTurleri.KartHarcama) ? ""
            : dagilim == DagilimBicimleri.Genel ? ", Genel" : kanallar.Count > 0 ? ", Kanal " + string.Join("+", kanallar) : "";

    // ---- Yanıt ----

    private sealed record Yanit(List<YanitSatiri>? Satirlar);
    private sealed record YanitSatiri(int No, string? Tur, string? Dagilim, List<int>? Kanallar, string? Guven, string? Kategori, string? Aciklama);
    internal sealed record Oneri(int No, string IslemTuru, string? DagilimTuru, IReadOnlyList<int> KanalIds, string Guven, string Kategori, string? Aciklama);

    [GeneratedRegex(@"\[\d{1,4}\]", RegexOptions.CultureInvariant)]
    private static partial Regex YerTutucu();
    [GeneratedRegex(@"^[\p{L}\p{M} /&,.-]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex KategoriDeseni();

    /// <summary>Doğrulanmış öneriler: yalnız sorulan satırlar, belgeye uygun tür, okunan yönle çelişmeyen, etkin kanallı. Okunamayan yanıtta null.</summary>
    internal static List<Oneri>? Coz(string metin, string kaynak, IReadOnlyDictionary<int, (string Yon, bool Uyarili, YapayZekaMaskesi.Maskeli Metin)> sorulan, IReadOnlySet<int> kanallar)
    {
        Yanit? yanit;
        try
        { yanit = JsonSerializer.Deserialize<Yanit>(metin, JsonSerializerOptions.Web); }
        catch (JsonException) { return null; }
        if (yanit?.Satirlar is null)
            return null;
        var sonuc = new Dictionary<int, Oneri>();
        foreach (var y in yanit.Satirlar)
        {
            if (!sorulan.TryGetValue(y.No, out var s) || sonuc.ContainsKey(y.No) || y.Tur is null || !Turler(kaynak).Contains(y.Tur)
                || s.Yon is "Giris" or "Cikis" && EkstreHafizaServisi.Yon(y.Tur, kaynak) is { } yon && yon != s.Yon)
                continue;
            var kanalli = y.Tur is EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider or EkstreIslemTurleri.KartHarcama;
            var secilen = (y.Kanallar ?? []).Distinct().Order().ToList();
            var (dagilim, ids) = !kanalli ? (null, [])
                : y.Dagilim == DagilimBicimleri.Genel ? (DagilimBicimleri.Genel, [])
                : y.Dagilim == KanalDagilimi && secilen.Count > 0 && secilen.All(kanallar.Contains) ? (DagilimBicimleri.Esit, secilen) : ((string?)null, new List<int>());
            var guven = s.Yon is not ("Giris" or "Cikis") ? YapayZekaGuvenleri.Dusuk
                : y.Guven is YapayZekaGuvenleri.Yuksek ? s.Uyarili ? YapayZekaGuvenleri.Orta : YapayZekaGuvenleri.Yuksek
                : y.Guven is YapayZekaGuvenleri.Orta ? YapayZekaGuvenleri.Orta : YapayZekaGuvenleri.Dusuk;
            var kategori = GirdiDogrulama.Temizle(y.Kategori ?? "").Trim();
            sonuc[y.No] = new(y.No, y.Tur, dagilim, ids, guven, KategoriDeseni().IsMatch(kategori) ? kategori : "", YapayZekaMaskesi.GeriCevir(y.Aciklama, s.Metin));
        }
        return [.. sonuc.Values];
    }

    /// <summary>Yanıtı belgenin hâlâ açık satırlarına öneri olarak yazar; öneri gelmeyen satır da sorulmuş sayılır.</summary>
    /// <returns>Önerilen satır sayısı; yanıt yoksa ya da okunamadıysa null.</returns>
    internal static int? Yaz(KasaDbContext db, int belgeId, Hazirlik h, YapayZekaCagriEntity cagri, string? metin)
    {
        var kanallar = db.Kanallar.AsNoTracking().Where(k => k.Aktif).Select(k => k.Id).ToHashSet();
        var sorulan = h.Satirlar.ToDictionary(s => s.No, s => (s.Yon, s.Satirlar.Any(r => r.Uyarilar.Count > 0), s.Metin));
        var cozulen = metin is null ? null : Coz(metin, h.Kaynak, sorulan, kanallar);
        var oneriler = (cozulen ?? []).ToDictionary(o => o.No);
        var kapali = db.EkstreKayitlar.Where(k => k.BelgeId == belgeId && !k.Iptal).Select(k => k.SatirNo).ToHashSet();
        kapali.UnionWith(db.EkstreYokSayilanlar.Where(y => y.BelgeId == belgeId).Select(y => y.SatirNo));
        var mevcut = db.YapayZekaOneriler.Where(o => o.BelgeId == belgeId).ToDictionary(o => o.SatirNo);
        var sayi = 0;
        foreach (var (grup, satir) in h.Satirlar.SelectMany(g => g.Satirlar.Select(r => (g, r))))
        {
            if (kapali.Contains(satir.No) || mevcut.TryGetValue(satir.No, out var kayit) && kayit.IslemTuru.Length > 0)
                continue;
            if (kayit is null)
                db.YapayZekaOneriler.Add(kayit = new() { BelgeId = belgeId, SatirNo = satir.No });
            kayit.CagriId = cagri.Id;
            if (!oneriler.TryGetValue(grup.No, out var o))
                continue;
            kayit.IslemTuru = o.IslemTuru;
            kayit.DagilimTuru = o.DagilimTuru;
            kayit.KanalIdsJson = Json(o.KanalIds);
            kayit.Kategori = o.Kategori;
            kayit.Aciklama = o.Aciklama;
            kayit.Guven = o.Guven;
            sayi++;
        }
        return cozulen is null ? null : sayi;
    }

    /// <summary>Kural ve hafızanın tanımadığı satırın saklı önerisi; yoksa null.</summary>
    internal static EkstreKuralOnerisi? SatirOnerisi(EkstreOkunanSatir row, IReadOnlyDictionary<int, YapayZekaOneriEntity> oneriler, IReadOnlySet<int> kanallar)
    {
        if (!oneriler.TryGetValue(row.No, out var o) || o.IslemTuru.Length == 0)
            return null;
        var ids = Read<int>(o.KanalIdsJson);
        var kanalVar = ids.All(kanallar.Contains);
        // Kasa hareketi olmadığı ve yönü okunamayan satırın türü yalnız yazıdır.
        var doldur = o.IslemTuru != EkstreIslemTurleri.Atla && row.Yon is "Giris" or "Cikis";
        var bas = $"Yapay zekâ ({o.Guven switch { YapayZekaGuvenleri.Yuksek => "yüksek", YapayZekaGuvenleri.Orta => "orta", _ => "düşük" }} güven){(o.Kategori.Length > 0 ? ": " + o.Kategori : "")}.";
        var son = o.IslemTuru == EkstreIslemTurleri.Atla ? "Kasa hareketi olmayabilir; öyleyse yok sayın."
            : doldur ? "Kontrol edip kendiniz işaretleyin." : $"Olası tür: {TurAdi(o.IslemTuru)}; yönü belgeden kontrol edip kendiniz seçin.";
        return new(row.No, YapayZekaDurumlari.Oneri, [], doldur || o.IslemTuru == EkstreIslemTurleri.Atla ? o.IslemTuru : null, doldur && kanalVar ? o.DagilimTuru : null, doldur && kanalVar ? ids : [],
            $"{bas} {son}", YapayZeka: new(o.Guven, o.Kategori, doldur ? o.Aciklama : null));
    }

    private static string TurAdi(string tur) => tur switch
    {
        EkstreIslemTurleri.KartHarcama => "kart harcaması",
        EkstreIslemTurleri.KartIade => "kart iadesi",
        EkstreIslemTurleri.KartOdemesi => "karta ödeme",
        EkstreIslemTurleri.Gelir => "gelir",
        _ => "gider"
    };

    // ---- Doğruluk ve ölçüm ----

    /// <summary>Önerisi sonuçlanan (kaydedilen ya da yok sayılan) satırlar ve kullanıcının kararı öneriyle aynı türde olanlar.</summary>
    internal static (int Sonuclanan, int Ayni) Dogruluk(KasaDbContext db)
    {
        var oneriler = db.YapayZekaOneriler.AsNoTracking().Where(o => o.IslemTuru != "").Select(o => new { o.BelgeId, o.SatirNo, o.IslemTuru }).ToList();
        if (oneriler.Count == 0)
            return (0, 0);
        var kararlar = db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal && db.YapayZekaOneriler.Any(o => o.BelgeId == k.BelgeId && o.SatirNo == k.SatirNo))
            .Select(k => new { k.BelgeId, k.SatirNo, k.IslemTuru }).ToList().ToDictionary(k => (k.BelgeId, k.SatirNo), k => k.IslemTuru);
        foreach (var y in db.EkstreYokSayilanlar.AsNoTracking().Where(y => db.YapayZekaOneriler.Any(o => o.BelgeId == y.BelgeId && o.SatirNo == y.SatirNo)).ToList())
            kararlar.TryAdd((y.BelgeId, y.SatirNo), EkstreIslemTurleri.Atla);
        var sonuclanan = oneriler.Where(o => kararlar.ContainsKey((o.BelgeId, o.SatirNo))).ToList();
        return (sonuclanan.Count, sonuclanan.Count(o => kararlar[(o.BelgeId, o.SatirNo)] == o.IslemTuru));
    }

    public const int EnAzOlcumKarari = 10;

    /// <summary>Ölçüm: kararların her ikincisi gizlenip sorulur, diğerleri örnek verilir. Yeterli karar yoksa null.</summary>
    internal static (YapayZekaIstegi Istek, IReadOnlyDictionary<int, (string Yon, bool Uyarili, YapayZekaMaskesi.Maskeli Metin)> Sorulan, IReadOnlyDictionary<int, EkstreHafizaServisi.Karar> Beklenen)?
        OlcumHazirla(KasaDbContext db, string kaynak)
    {
        var kararlar = Kararlar(db, kaynak).Take(2 * YapayZekaAyarlari.EnCokSatir).ToList();
        if (kararlar.Count < EnAzOlcumKarari)
            return null;
        var maske = new YapayZekaMaskesi();
        var kanallar = Kanallar(db, maske);
        var ornekler = Ornekler(kararlar.Where((_, i) => i % 2 == 1), maske);
        var beklenen = kararlar.Where((_, i) => i % 2 == 0).Select((k, i) => (No: i + 1, Karar: k)).ToDictionary(x => x.No, x => x.Karar);
        var sorulan = beklenen.ToDictionary(b => b.Key, b => (b.Value.Yon, false, maske.Maskele(b.Value.Anahtar)));
        var istek = Istek(kaynak, kanallar, ornekler, sorulan.Select(s => $"{s.Key} | {YonYazisi(s.Value.Yon)} | ? | {s.Value.Item3.Metin}"));
        return (istek, sorulan, beklenen);
    }

    internal static YapayZekaOlcumDto OlcumSonucu(KasaDbContext db, string kaynak, IReadOnlyDictionary<int, (string Yon, bool Uyarili, YapayZekaMaskesi.Maskeli Metin)> sorulan,
        IReadOnlyDictionary<int, EkstreHafizaServisi.Karar> beklenen, string metin, YapayZekaCagriEntity cagri)
    {
        var oneriler = (Coz(metin, kaynak, sorulan, db.Kanallar.AsNoTracking().Where(k => k.Aktif).Select(k => k.Id).ToHashSet()) ?? []).ToDictionary(o => o.No);
        bool Tur(int no) => oneriler.TryGetValue(no, out var o) && o.IslemTuru == beklenen[no].IslemTuru;
        var dagilimli = beklenen.Where(b => b.Value.DagilimTuru is DagilimBicimleri.Genel or DagilimBicimleri.Esit
            && b.Value.IslemTuru is EkstreIslemTurleri.Gelir or EkstreIslemTurleri.Gider or EkstreIslemTurleri.KartHarcama).ToList();
        var yuksek = oneriler.Values.Where(o => o.Guven == YapayZekaGuvenleri.Yuksek).ToList();
        return new(kaynak, beklenen.Count, beklenen.Keys.Count(Tur), dagilimli.Count,
            dagilimli.Count(b => Tur(b.Key) && oneriler[b.Key].DagilimTuru == b.Value.DagilimTuru && oneriler[b.Key].KanalIds.SequenceEqual(b.Value.KanalIds)),
            yuksek.Count, yuksek.Count(o => Tur(o.No)), beklenen.Count - oneriler.Count, cagri.MaliyetUsd, DateTimeOffset.FromUnixTimeMilliseconds(cagri.Zaman));
    }
}
