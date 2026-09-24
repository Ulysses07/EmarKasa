using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>
/// Paket B raporları: kasa dökümü, ay kapanışı (anlık görüntü ve farklar), hedef/bütçe, grafik
/// verisi ve cari özeti. Hiçbir para kuralı burada yeniden yazılmaz: rakamlar
/// <see cref="HesapServisi"/>'nin (motorun) çıktılarından okunur.
/// </summary>
public sealed class RaporServisi(KasaDbContext db, HesapServisi hesap, TimeProvider saat)
{
    public static readonly JsonSerializerOptions AnlikJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public DateOnly Bugun => Saat.Bugun(saat);

    public DateOnly TakipBaslangic()
        => db.Ayarlar.AsNoTracking().OrderBy(a => a.Id).Select(a => a.TakipBaslangic).First();

    public IReadOnlyList<Kanal> Kanallar()
        => db.Kanallar.AsNoTracking().OrderBy(k => k.Sira).ThenBy(k => k.Id)
            .Select(e => new Kanal(e.Ad, e.AcilisDevri, e.Aktif, e.Sira)).ToList();

    // ------------------------------------------------------------ 21 · kasa dökümü

    public static string TurAdi(KasaKalemTuru t) => t switch
    {
        KasaKalemTuru.Gelen => "Gelen",
        KasaKalemTuru.CekTahsilat => "Çek tahsilatı",
        KasaKalemTuru.CariGider => "Cari gider",
        KasaKalemTuru.SabitGider => "Sabit gider",
        KasaKalemTuru.OrtakGider => "Ortak gider",
        KasaKalemTuru.CekOdemesi => "Çek ödemesi",
        KasaKalemTuru.KartOdemesi => "Kart ödemesi",
        KasaKalemTuru.ErtelenenKk => "K.K (geçen ay, kartsız)",
        _ => t.ToString(),
    };

    /// <summary>[baslangic, bitis] ile çakışan dönemlerin dökümü; takvimde böyle dönem yoksa null.</summary>
    public KasaDokumu? KasaDokumu(DateOnly baslangic, DateOnly bitis)
    {
        var parca = hesap.Haftalik().Where(o => o.Donem.End >= baslangic && o.Donem.Start <= bitis).ToList();
        return KasaDokumuHesap.Olustur(parca, Kanallar());
    }

    public static KasaDokumuDto Dto(KasaDokumu d) => new(d.Baslangic, d.Bitis, d.Acilis, d.Kapanis, d.ToplamGiren, d.ToplamCikan,
        d.Adimlar.Select(a => new KasaDokumAdimiDto(a.Tur, TurAdi(a.Tur), a.Kanal, a.Tutar, a.Bakiye)).ToList());

    public KasaDokumu? AyinKasaDokumu(int yil, int ay)
    {
        var bas = new DateOnly(yil, ay, 1);
        return KasaDokumu(bas, AyBicimi.AySonu(bas));
    }

    // ------------------------------------------------------------ 11 · ay kapanışı

    /// <summary>
    /// Ayın rakamları: kanal satırları (aylık rapor), kasa açılışı/kapanışı ve kanal adı → Id eşlemi
    /// (kanal yeniden adlandırılsa da satırlar Id ile eşleşsin diye).
    /// </summary>
    public AyAnlikGoruntusu Anlik(int yil, int ay)
    {
        var d = AyinKasaDokumu(yil, ay);
        var idler = db.Kanallar.AsNoTracking().Select(k => new { k.Ad, k.Id }).ToList().ToDictionary(k => k.Ad, k => k.Id);
        return new AyAnlikGoruntusu(hesap.Aylik(yil, ay).Kanallar, d?.Acilis, d?.Kapanis, idler);
    }

    public const string KasaAcilisiKalemi = "Kasa açılışı";
    public const string KasaKapanisiKalemi = "Kasa kapanışı";
    public const string OrtakPayAlani = "Ortak pay";

    /// <summary>
    /// Anlık görüntüdeki rakamlarla bugünküler arasındaki farklar (kanal ve alan başına). Kanal satırları
    /// iki görüntüde de Id varsa Id ile eşlenir (ad değişimi fark sayılmaz; bugünkü ad gösterilir), yoksa
    /// adla. Satırı olmayan kanalın alanı "—"dir; yalnız bir yanda satır olup rakam 0 ise fark sayılmaz.
    /// </summary>
    public static IReadOnlyList<AyFarkiDto> Karsilastir(AyAnlikGoruntusu eski, AyAnlikGoruntusu yeni)
    {
        var farklar = new List<AyFarkiDto>();
        void Ekle(string kalem, decimal? e, decimal? y)
        {
            if (e == y || (e ?? 0m) == (y ?? 0m) && (e is null || y is null)) return;
            farklar.Add(new AyFarkiDto(kalem, e, y));
        }
        Ekle(KasaAcilisiKalemi, eski.KasaAcilis, yeni.KasaAcilis);
        Ekle(KasaKapanisiKalemi, eski.KasaKapanis, yeni.KasaKapanis);

        bool idIle = eski.KanalIdleri is not null && yeni.KanalIdleri is not null;
        string Anahtar(AyAnlikGoruntusu g, string ad)
            => idIle && g.KanalIdleri!.TryGetValue(ad, out var id) ? "#" + id : "ad:" + ad;
        var eskiler = eski.Kanallar.GroupBy(k => Anahtar(eski, k.Kanal)).ToDictionary(g => g.Key, g => g.First());
        var yeniler = yeni.Kanallar.GroupBy(k => Anahtar(yeni, k.Kanal)).ToDictionary(g => g.Key, g => g.First());
        foreach (var anahtar in eskiler.Keys.Concat(yeniler.Keys).Distinct())
        {
            var e = eskiler.GetValueOrDefault(anahtar);
            var y = yeniler.GetValueOrDefault(anahtar);
            var ad = (y ?? e)!.Kanal;
            Ekle($"{ad} · Gelen", e?.Gelen, y?.Gelen);
            Ekle($"{ad} · Çek tahsilatı", e?.CekGelen, y?.CekGelen);
            Ekle($"{ad} · Cari gider", e?.CariGiden, y?.CariGiden);
            Ekle($"{ad} · Sabit gider", e?.SabitGider, y?.SabitGider);
            Ekle($"{ad} · Kredi kartı", e?.KrediKarti, y?.KrediKarti);
            Ekle($"{ad} · {OrtakPayAlani}", e?.OrtakPay, y?.OrtakPay);
            Ekle($"{ad} · Çek ödemesi", e?.CekGiden, y?.CekGiden);
            Ekle($"{ad} · Ay sonucu", e?.AySonucu, y?.AySonucu);
        }
        return farklar;
    }

    private static readonly HashSet<string> KapanisTurleri = [GecmisTurleri.AyKilidi, GecmisTurleri.AyYayini];

    /// <summary>
    /// <paramref name="sonId"/>'den sonraki geçmiş satırlarından <paramref name="ay"/>'ın rakamlarını
    /// açıklayanlar (en yeni önce):
    /// <list type="bullet">
    /// <item>Kaydın eski ya da yeni halinin kilit tarihi o aya düşen satırlar (K.K işlemi bir sonraki
    /// aya da dokunur), o ayı etkileyen takip başlangıcı / kasa açılış devri değişimi ve kanal açılış
    /// devri değişimi.</item>
    /// <item><paramref name="oncekiAylar"/> (kasa açılışı değişti): kasa aydan aya devrettiği için
    /// önceki aylara ait işlem, gelen, kart ödemesi ve çek satırları da (kasa sayımı kasayı değiştirmez).</item>
    /// <item><paramref name="kanalDagilimi"/> (bir kanalın Ortak payı değişti): kanal ekleme/silme ve
    /// sıra ya da aktiflik değişimi (Ortak gider bunlara göre bölünür).</item>
    /// </list>
    /// Kilit/yayın satırları ve yalnız ad değişimleri sayılmaz.
    /// </summary>
    public List<DegisiklikEntity> AyaDokunanlar(DateOnly ay, int sonId, bool oncekiAylar = false, bool kanalDagilimi = false)
    {
        var turTipleri = db.Model.GetEntityTypes().Select(t => t.ClrType)
            .Where(t => AyKilidiKurali.KilitAlanlari(t).Count > 0)
            .Select(t => (Tip: t, Tur: DegisiklikKaydedici.TanimOku(db.Model.FindEntityType(t)!).Tur))
            .ToDictionary(x => x.Tur, x => x.Tip);
        var aySonu = AyBicimi.AySonu(ay);
        var sonuc = new List<DegisiklikEntity>();
        foreach (var d in db.Degisiklikler.AsNoTracking().Where(d => d.Id > sonId).OrderByDescending(d => d.Id).ToList())
        {
            if (KapanisTurleri.Contains(d.Tur)) continue;
            var eski = Coz(d.EskiJson);
            var yeni = Coz(d.YeniJson);
            bool dokunur = false;
            if (turTipleri.TryGetValue(d.Tur, out var tip))
            {
                var aylar = new HashSet<DateOnly>();
                if (eski is { } e) aylar.UnionWith(AyKilidiKurali.EtkiledigiAylar(tip, AyKilidiKurali.JsonOkuyucu(e)));
                if (yeni is { } y) aylar.UnionWith(AyKilidiKurali.EtkiledigiAylar(tip, AyKilidiKurali.JsonOkuyucu(y)));
                dokunur = aylar.Contains(ay)
                          || (oncekiAylar && tip != typeof(KasaSayimEntity) && aylar.Any(a => a < ay));
            }
            else if (d.Tur == GecmisTurleri.Ayar && eski is { } ea && yeni is { } ya)
            {
                var eo = AyKilidiKurali.JsonOkuyucu(ea);
                var yo = AyKilidiKurali.JsonOkuyucu(ya);
                if (!Equals(eo(nameof(AyarEntity.KasaAcilisDevri)), yo(nameof(AyarEntity.KasaAcilisDevri)))) dokunur = true;
                if (eo(nameof(AyarEntity.TakipBaslangic)) is DateOnly et && yo(nameof(AyarEntity.TakipBaslangic)) is DateOnly yt
                    && et != yt && (et < yt ? et : yt) <= aySonu) dokunur = true;
            }
            else if (d.Tur == GecmisTurleri.Kanal)
            {
                object? Alan(JsonElement? j, string alan) => j is { } x ? AyKilidiKurali.JsonOkuyucu(x)(alan) : null;
                var ed = Alan(eski, nameof(KanalEntity.AcilisDevri)) as decimal?;
                var yd = Alan(yeni, nameof(KanalEntity.AcilisDevri)) as decimal?;
                if (d.Eylem == Eylemler.Guncellendi)
                {
                    dokunur = ed is not null && yd is not null && ed != yd;
                    // Toplu ad değişimi satırında sıra/aktiflik yoktur (ikisi de null): sayılmaz.
                    if (kanalDagilimi
                        && (!Equals(Alan(eski, nameof(KanalEntity.Sira)), Alan(yeni, nameof(KanalEntity.Sira)))
                            || !Equals(Alan(eski, nameof(KanalEntity.Aktif)), Alan(yeni, nameof(KanalEntity.Aktif)))))
                        dokunur = true;
                }
                else
                    dokunur = kanalDagilimi || (ed ?? 0m) != 0m || (yd ?? 0m) != 0m;
            }
            if (dokunur) sonuc.Add(d);
        }
        return sonuc;
    }

    private static JsonElement? Coz(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    public AyKapanisDto AyKapanisi(int yil, int ay)
    {
        var ayBasi = new DateOnly(yil, ay, 1);
        // Kilit geriye doğru kapsar: satırı olmasa da en son kilitli aydan önceki (takipteki) ay kilitlidir.
        var kilitSatirlari = db.AyKilitleri.AsNoTracking().ToList();
        var kilitli = AyKilidiKurali.EtkinKilitliAylar(db, kilitSatirlari.Select(k => k.Ay).ToList()).Contains(ayBasi);
        var kilitZamani = kilitSatirlari.Where(k => k.Ay >= ayBasi).OrderBy(k => k.Ay).FirstOrDefault()?.KilitZamaniUtc;
        var yayin = db.AyYayinlari.AsNoTracking().FirstOrDefault(y => y.Ay == ayBasi);
        IReadOnlyList<AyFarkiDto> farklar = [];
        IReadOnlyList<DegisiklikDto> degisiklikler = [];
        if (yayin is not null)
        {
            AyAnlikGoruntusu? eski = null;
            try { eski = JsonSerializer.Deserialize<AyAnlikGoruntusu>(yayin.AnlikJson, AnlikJson); }
            catch (JsonException) { }
            if (eski is not null) farklar = Karsilastir(eski, Anlik(yil, ay));
            var simdi = saat.GetUtcNow().UtcDateTime;
            degisiklikler = AyaDokunanlar(ayBasi, yayin.SonDegisiklikId,
                    oncekiAylar: farklar.Any(f => f.Kalem == KasaAcilisiKalemi),
                    kanalDagilimi: farklar.Any(f => f.Kalem.EndsWith(" · " + OrtakPayAlani, StringComparison.Ordinal)))
                .Select(d => DegisiklikDtosu(d, simdi)).ToList();
        }
        return new AyKapanisDto(yil, ay, AyBicimi.Etiket(ayBasi), kilitli, kilitli ? Utc(kilitZamani) : null,
            Kilitlenebilir(ayBasi), yayin is not null, Utc(yayin?.YayinZamaniUtc), farklar, degisiklikler);
    }

    /// <summary>Yalnız bitmiş ay kilitlenebilir (içinde bulunulan ay hâlâ yazılıyor).</summary>
    public bool Kilitlenebilir(DateOnly ayBasi) => AyBicimi.AySonu(ayBasi) < Bugun;

    public static DegisiklikDto DegisiklikDtosu(DegisiklikEntity d, DateTime simdiUtc) => new(
        d.Id, DateTime.SpecifyKind(d.ZamanUtc, DateTimeKind.Utc), d.Rol, d.Tur, d.KayitId, d.Eylem, d.Ozet,
        d.EskiJson, d.YeniJson, d.GeriAlindi,
        d.GeriAlmaZamaniUtc is { } g ? DateTime.SpecifyKind(g, DateTimeKind.Utc) : null,
        GeriAlinabilir: GecmisKurallari.GeriAlmaEngeli(d, simdiUtc) is null);

    private static DateTime? Utc(DateTime? d) => d is { } x ? DateTime.SpecifyKind(x, DateTimeKind.Utc) : null;

    // ------------------------------------------------------------ 05 · hedef ve bütçe

    /// <summary>Ayın kartsız sabit gider işlemleri (takip başlangıcından önce değil), kalem adına göre toplam.</summary>
    private Dictionary<string, decimal> KalemHarcamalari(DateOnly ayBasi)
    {
        var takip = TakipBaslangic();
        var aySonu = AyBicimi.AySonu(ayBasi);
        return db.Islemler.AsNoTracking()
            .Where(i => i.Tarih >= ayBasi && i.Tarih <= aySonu && i.Tarih >= takip
                        && i.Tip == GiderTipi.SabitGider && i.KrediKartiId == null)
            .Select(i => new { i.Cari, i.TutarTl }).ToList()
            .GroupBy(i => i.Cari, Metin.EsitBuyukKucukDuyarsiz)
            .ToDictionary(g => g.Key, g => g.Sum(i => Para.Yuvarla(i.TutarTl)), Metin.EsitBuyukKucukDuyarsiz);
    }

    /// <summary>Ay için aktif tekrarlayan giderlerin (başlangıç ayı ≤ ay) kalem başına şablon tutarı.</summary>
    private Dictionary<string, decimal> Sablonlar(DateOnly ayBasi)
        => db.TekrarlayanGiderler.AsNoTracking().Where(t => t.Aktif && t.BaslangicAyi <= ayBasi)
            .Select(t => new { t.Kalem, t.Tutar }).ToList()
            .GroupBy(t => t.Kalem, Metin.EsitBuyukKucukDuyarsiz)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Tutar), Metin.EsitBuyukKucukDuyarsiz);

    public static decimal? Yuzde(decimal gerceklesen, decimal? hedef)
        => hedef is > 0m ? decimal.Round(gerceklesen / hedef.Value * 100m, 1, MidpointRounding.AwayFromZero) : null;

    public HedefButceDto HedefButce(int yil, int ay)
    {
        var ayBasi = new DateOnly(yil, ay, 1);
        var rapor = hesap.Aylik(yil, ay);
        var hedefler = db.KanalHedefleri.AsNoTracking().Where(h => h.Ay == ayBasi).ToList().ToDictionary(h => h.KanalId, h => h.GelirHedefi);
        var butceler = db.GiderButceleri.AsNoTracking().Where(b => b.Ay == ayBasi).ToList().ToDictionary(b => b.GiderKalemiId, b => b.Tutar);
        var harcama = KalemHarcamalari(ayBasi);
        var sablon = Sablonlar(ayBasi);

        var kanallar = db.Kanallar.AsNoTracking().OrderBy(k => k.Sira).ThenBy(k => k.Id).ToList()
            .Select(k =>
            {
                var satir = rapor.Kanallar.FirstOrDefault(r => r.Kanal == k.Ad);
                var gercek = satir is null ? 0m : satir.Gelen + satir.CekGelen;
                decimal? hedef = hedefler.TryGetValue(k.Id, out var h) ? h : null;
                return new KanalHedefDto(k.Id, k.Ad, k.Aktif, hedef, gercek, Yuzde(gercek, hedef));
            })
            .Where(k => k.Aktif || k.Hedef is not null || k.Gerceklesen != 0m)
            .ToList();
        var kalemler = db.GiderKalemleri.AsNoTracking().ToList().OrderBy(k => k.Ad, Metin.Sirala)
            .Select(k =>
            {
                var gercek = harcama.GetValueOrDefault(k.Ad);
                decimal? butce = butceler.TryGetValue(k.Id, out var b) ? b : null;
                decimal? s = sablon.TryGetValue(k.Ad, out var x) ? x : null;
                return new GiderButceDto(k.Id, k.Ad, k.Aktif, butce, gercek, Yuzde(gercek, butce), s);
            })
            .Where(k => k.Aktif || k.Butce is not null || k.Gerceklesen != 0m || k.Sablon is not null)
            .ToList();
        return new HedefButceDto(yil, ay, kanallar, kalemler);
    }

    // ------------------------------------------------------------ 04 · grafik verisi

    public const int GrafikAySayisi = 24;

    public GrafikDto Grafik(int yil, int ay)
    {
        var son = new DateOnly(yil, ay, 1);
        var ilk = son.AddMonths(-(GrafikAySayisi - 1));
        var takip = TakipBaslangic();
        var kurlar = db.Kurlar.AsNoTracking().Where(k => k.Ay >= ilk && k.Ay <= son).ToList().ToDictionary(k => k.Ay);
        var kanalAdlari = Kanallar().Select(k => k.Ad).ToList();
        var aylar = new List<GrafikAyDto>(GrafikAySayisi);
        for (var a = ilk; a <= son; a = a.AddMonths(1))
        {
            bool takipOncesi = AyBicimi.AySonu(a) < takip;
            var rapor = takipOncesi ? null : hesap.Aylik(a.Year, a.Month);
            var satirlar = kanalAdlari.Select(k =>
            {
                var r = rapor?.Kanallar.FirstOrDefault(x => x.Kanal == k);
                return new GrafikKanalDto(k, r is null ? 0m : r.Gelen + r.CekGelen, r?.AySonucu ?? 0m);
            }).ToList();
            kurlar.TryGetValue(a, out var kur);
            aylar.Add(new GrafikAyDto(a.Year, a.Month, takipOncesi, satirlar, kur?.TufeEndeksi, kur?.UsdTry, kur?.EurTry, kur?.AltinGramTry));
        }
        return new GrafikDto(yil, ay, kanalAdlari, aylar);
    }

    // ------------------------------------------------------------ 07 · cari özeti

    public const string CariTuru = "cari";
    public const string KalemTuru = "kalem";

    public CariOzetiDto CariOzeti(string ad, int yil, string tur)
    {
        var bas = new DateOnly(yil, 1, 1);
        var son = new DateOnly(yil, 12, 31);
        var islemler = db.Islemler.AsNoTracking().Where(i => i.Tarih >= bas && i.Tarih <= son)
            .Select(i => new { i.Tarih, i.Cari, i.TutarTl, i.Tip, i.KrediKartiId }).ToList()
            .Where(i => Metin.EsitBuyukKucukDuyarsiz.Equals(i.Cari.Trim(), ad))
            .ToList();
        var aylar = new List<CariOzetiAyDto>(12);
        if (tur == KalemTuru)
        {
            var sablonlar = db.TekrarlayanGiderler.AsNoTracking().ToList()
                .Where(t => Metin.EsitBuyukKucukDuyarsiz.Equals(t.Kalem, ad)).ToList();
            var ids = sablonlar.Select(t => t.Id).ToList();
            var kararlar = db.TekrarlayanGirisler.AsNoTracking()
                .Where(g => ids.Contains(g.TekrarlayanGiderId) && g.Ay >= bas && g.Ay <= son).ToList();
            var kalemIslemleri = islemler.Where(i => i.Tip == GiderTipi.SabitGider && i.KrediKartiId == null).ToList();
            for (int m = 1; m <= 12; m++)
            {
                var ayBasi = new DateOnly(yil, m, 1);
                var bu = kalemIslemleri.Where(i => i.Tarih.Month == m).ToList();
                var nakit = bu.Sum(i => Para.Yuvarla(i.TutarTl));
                var aktif = sablonlar.Where(t => t.Aktif && t.BaslangicAyi <= ayBasi).ToList();
                decimal? sablon = aktif.Count > 0 ? aktif.Sum(t => t.Tutar) : null;
                var ayKararlari = kararlar.Where(g => g.Ay == ayBasi).Select(g => g.Durum).Distinct().ToList();
                string? karar = ayKararlari.Count == 0 ? null
                    : string.Join(", ", ayKararlari.Order().Select(d => d == TekrarlayanDurum.Girildi ? "Girildi" : "Atlandı"));
                aylar.Add(new CariOzetiAyDto(m, nakit, 0m, 0m, nakit, bu.Count, sablon, karar));
            }
            return new CariOzetiDto(ad, yil, KalemTuru, aylar, aylar.Sum(a => a.Toplam),
                aylar.Any(a => a.Sablon is not null) ? aylar.Sum(a => a.Sablon ?? 0m) : null);
        }

        // Cari: kartsız sabit gider işlemlerinin adı kaleme aittir, cariye sayılmaz.
        var cariIslemleri = islemler.Where(i => !(i.Tip == GiderTipi.SabitGider && i.KrediKartiId == null)).ToList();
        var cekler = db.Cekler.AsNoTracking()
            .Where(c => c.Yon == CekYonu.Verilen && c.Durum == CekDurumu.Odendi && c.IslemTarihi >= bas && c.IslemTarihi <= son)
            .Select(c => new { c.Kisi, c.Tutar, c.IslemTarihi }).ToList()
            .Where(c => Metin.EsitBuyukKucukDuyarsiz.Equals(c.Kisi.Trim(), ad))
            .ToList();
        for (int m = 1; m <= 12; m++)
        {
            var bu = cariIslemleri.Where(i => i.Tarih.Month == m).ToList();
            var nakit = bu.Where(i => HesapMotoru.EtkinTip(new Islem(i.Tarih, i.Cari, i.TutarTl, "", i.Tip, null, i.KrediKartiId)) != GiderTipi.KrediKarti)
                .Sum(i => Para.Yuvarla(i.TutarTl));
            var kk = bu.Sum(i => Para.Yuvarla(i.TutarTl)) - nakit;
            var buCek = cekler.Where(c => c.IslemTarihi!.Value.Month == m).ToList();
            var cek = buCek.Sum(c => Para.Yuvarla(c.Tutar));
            aylar.Add(new CariOzetiAyDto(m, nakit, kk, cek, nakit + kk + cek, bu.Count + buCek.Count, null, null));
        }
        return new CariOzetiDto(ad, yil, CariTuru, aylar, aylar.Sum(a => a.Toplam), null);
    }
}
