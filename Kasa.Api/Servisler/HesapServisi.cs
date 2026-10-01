using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kasa.Api.Servisler;

/// <summary>DB'den veriyi yükler, dönem takvimini üretir ve HesapMotoru'nu çağırır. "Bugün" bağlamın
/// saatinden (<c>db.Bugunu()</c>) çağrı başına bir kez okunur: istek dışında da (eşik bildirimi) aynı gün.
/// Okuma salt okunur bir anlık görüntüde yapılır (<see cref="OkumaAnlikGoruntusu"/>): yazma kilidi alınmaz ve
/// Sync çalışmaz. Raporlar Sync'in yazdığı hiçbir şeye bağlı değildir: kart harcamaları ve avans payları yazma
/// yollarının sonunda türetilir; gün dönümünde bakım adımının yazdığı boş kesim ekstreleri kasa hesabına girmez.
/// Tek bir bozuk kayıt raporu düşürmez: kayıt karantinaya alınır ve raporda veri sağlığı uyarısıyla görünür (bkz. Yukle,
/// <see cref="VeriKarantinasi"/>).</summary>
public class HesapServisi
{
    private readonly KasaDbContext _db;
    public HesapServisi(KasaDbContext db) => _db = db;

    /// <summary>Haftalık rapor ve dönem listesinin ileri ufku: bugünden bir yıl sonrasının ay sonu. Daha ileri tarihli
    /// tek bir kayıt (ör. yıl yazım hatası) dönem listesini binlerce döneme uzatamaz; ufkun ötesindeki kayıtlar
    /// silinmez, tarihleri ufka girdikçe rapora girer ve o zamana dek haftalık rapor veri sağlığı uyarısı verir.</summary>
    public static DateOnly IleriUfuk(DateOnly bugun)
    {
        var sinir = bugun.AddYears(1);
        return new(sinir.Year, sinir.Month, DateTime.DaysInMonth(sinir.Year, sinir.Month));
    }

    private record Yuk(
        IReadOnlyList<Kanal> Kanallar,
        IReadOnlyList<Islem> Islemler,
        IReadOnlyList<Gelen> Gelenler,
        IReadOnlyList<Donem> Donemler,
        decimal KasaAcilis,
        IReadOnlyDictionary<string, int?> KanalIdleri,
        string? UfukUyarisi,
        IReadOnlyList<KarantinaKaydi> Karantina,
        IReadOnlyDictionary<(int Yil, int Ay), IReadOnlyList<(string Ad, bool Aktif)>> AyKumeleri);

    /// <summary>Rapora olduğu gibi giremeyen (karantinaya alınan) kayıt: kayıt anahtarı (tür ve kimlik), kullanıcıya gösterilen
    /// açıklama ve kaydın rapordaki etki aralığı (aylık rapor yalnız dokunduğu ayda uyarır; <see cref="Son"/> null ise açık uçlu).</summary>
    internal sealed record KarantinaKaydi(string Anahtar, string Aciklama, DateOnly Ilk, DateOnly? Son, Exception? Hata = null)
    {
        public bool AyaDokunur(int yil, int ay) =>
            Ilk <= AyRaporAnlikGoruntusu.AySonu(yil, ay) && (Son is not { } son || son >= new DateOnly(yil, ay, 1));
    }

    /// <summary>Uyarı metninde adıyla sayılan en çok karantina kaydı; fazlası sayıyla belirtilir.</summary>
    private const int UyaridaEnCokKayit = 5;

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    private static string Tl(decimal tutar) => tutar.ToString("N2", Tr) + " TL";

    /// <summary>Karantina kayıtlarının listesi: ilk beşi açıklamasıyla, fazlası sayıyla (uyarıda ve ay kapatma iletisinde).</summary>
    internal static string KarantinaListesi(IReadOnlyCollection<KarantinaKaydi> kayitlar)
    {
        var liste = string.Join("; ", kayitlar.Take(UyaridaEnCokKayit).Select(k => k.Aciklama));
        return kayitlar.Count > UyaridaEnCokKayit ? $"{liste}; … ve {kayitlar.Count - UyaridaEnCokKayit} kayıt daha" : liste;
    }

    /// <summary>Karantina kayıtlarının veri sağlığı uyarısı; kayıt yoksa null. Yalnız canlı hesaplanan raporda görünür: bozuk kayıtla
    /// hesaplanan rapor dondurulmaz (<see cref="AyRaporAnlikGoruntusu"/>), kayıt düzeltilince rapor da düzelir.</summary>
    internal static string? KarantinaUyarisi(IReadOnlyCollection<KarantinaKaydi> kayitlar) => kayitlar.Count == 0 ? null
        : $"Okunamayan {kayitlar.Count} kayıt karantinaya alındı: {KarantinaListesi(kayitlar)}. Kayıt düzeltilene dek rapor onu bu biçimde sayar; kaydı düzeltin.";

    /// <summary>
    /// Raporun bütün verisini tek anlık görüntüden okur ve motor satırlarına çevirir. Karantina (gap-veri-degismezleri-patlama-yaricapi-3):
    /// tek bir kaydın kendi verisindeki sorun raporu düşürmez; kayıt, türüne göre aşağıdaki biçimde rapora girer ve
    /// <see cref="KarantinaKaydi"/> olarak döner (raporda veri sağlığı uyarısı, kayıt anahtarıyla bir kez Warning).
    /// - Gider (ekstre gideri, aylık gider ödemesi, alış ödemesi, takipli kredi taksiti, kart ödemesi): para kasadan çıkmıştır, yalnız
    ///   kanalı bilinmez. Çözülemeyen pay (okunamayan dağılımda kaydın tamamı) "Dağılım bekliyor" olur: tutar kasadan düşer, hiçbir
    ///   kanala yazılmaz, dağılım bekleyen tutarda görünür. Hesabı yapılamayan takipli kartın ödemelerinin yalnız nakit etkisi
    ///   (kasada önceden sayılan tutar düşülmüş hâli) ve devir iadesinin kasaya döndürdüğü tutar "Dağılım bekliyor" sayılır (bkz.
    ///   KartOdemeleriBekliyor).
    /// - Gelir (ekstre geliri, eski hesap ek geliri): para kasaya girmiştir, kanalı bilinmez: tutar genel kasaya gelir yazılır
    ///   (dağılım bekleyen giderin gelir karşılığı). Takipli kredi çekiminin çözülemeyen payı eski modelin kanalsız kredi girişi
    ///   olur: kasaya girer, hiçbir kanala ve ay sonucuna yazılmaz, aylık raporun kredi girişinde görünür (K2 korunur).
    /// - Tutarı türetilemeyen kayıt (eski kredinin geçersiz planı: taksit sayısı/günü ya da tutarı anlamsız) rapora alınmaz;
    ///   uydurma satır kasaya yazılmaz.
    /// Okunabilen ama tutarsız dağılım da çözülemez sayılır (bkz. Coz): boş öğe, boş liste, kayıt tutarını tutmayan pay toplamı. Aynı
    /// gidere bağlı ikinci kayıt (benzersizlik dizini olmayan eski/geri yüklenmiş veritabanı) gideri ikinci kez saydırmaz. Kasa her
    /// durumda kaydın tutarı kadar değişir. Karantina yalnız kaydın verisinden doğan hatayı kapsar (<see cref="VeriKarantinasi"/>): temiz
    /// veride kod hatası yükselir.
    /// Sağlam veride satırlar, sıraları ve tutarlar karantinasız hesapla birebir aynıdır (altın rapor testi).
    /// </summary>
    private Yuk Yukle(TakipHesapBaglami takip, DateOnly? raporBitis = null)
    {
        var bugun = takip.Bugun;
        var ct = takip.Iptal;
        // Tutarlı okuma: alış onayı/ödeme eşleştirmesi rapor okunurken yarım görünmez; yazanlar beklemez.
        using var snapshot = _db.OkumaBaslat();
        ct.ThrowIfCancellationRequested();
        var karantina = new List<KarantinaKaydi>();
        var karantinaAnahtarlari = new HashSet<string>(StringComparer.Ordinal);
        // Kayıt (anahtar) bir kez alınır; açıklama yalnız ilk kez kurulur: aynı kaydın her payında metin (ve kart adı) üretilmez.
        void Karantinaya(string anahtar, Func<string> aciklama, DateOnly ilk, DateOnly? son, Exception? hata = null)
        {
            if (karantinaAnahtarlari.Add(anahtar))
                karantina.Add(new(anahtar, aciklama(), ilk, son, hata));
        }
        static string Gun(DateOnly tarih) => tarih.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        // Ertelemeli K.K satırı bir sonraki ayın sonucuna da girer.
        static DateOnly EtkiSonu(Islem i) => i.Tip == GiderTipi.KrediKarti && !i.NakitKartOdemesi ? i.Tarih.AddMonths(1) : i.Tarih;
        static string PaySorunu(int? kanalId) => kanalId is { } id ? $"bir payı olmayan kanala (#{id}) bağlı" : "bir payının kanalı boş";
        var kartTakip = _db.TakipKartlar.AsNoTracking().ToDictionary(t => t.KrediKartiId);
        var kanallar = _db.Kanallar.AsNoTracking().OrderBy(k => k.Sira).ToList().Select(e => e.ToCore()).ToList();
        var kayitlar = _db.Islemler.AsNoTracking().Include(i => i.KanalKaydi).ToList();
        ct.ThrowIfCancellationRequested();
        var alislar = _db.Alislar.AsNoTracking()
            .Include(a => a.Kalemler).ThenInclude(k => k.Dagilimlar).ThenInclude(d => d.KanalKaydi)
            .Include(a => a.Odemeler).ThenInclude(o => o.Islem).ToList();
        ct.ThrowIfCancellationRequested();
        // Aynı gidere bağlı ikinci kayıt (benzersizlik dizini olmayan eski/geri yüklenmiş veritabanı) raporu düşürmez: gider bir kez,
        // ilk bağın (Id sırası) dağılımıyla sayılır; ikinci bağ rapora alınmaz ve karantinada görünür. Sağlam veride bağlar tekildir.
        var eslemeler = new Dictionary<int, AlisEntity>();
        foreach (var (odeme, alis) in alislar.SelectMany(a => a.Odemeler.Select(o => (o, a))).OrderBy(x => x.o.Id))
            if (!eslemeler.TryAdd(odeme.IslemId, alis))
                Karantinaya("AlisOdemesi:" + odeme.Id, () => $"Alış #{alis.Id} ödemesi #{odeme.Id} ({Gun(alis.Tarih)}): gider #{odeme.IslemId} alış #{eslemeler[odeme.IslemId].Id} ödemesi olarak zaten sayıldı; bu ikinci bağ rapora alınmadı",
                    alis.Tarih, alis.Tarih);
        // Ödeme dağılımı hesaplanamayan (ör. ödemeleri kalem dağılımını aşan) alışın ödemeleri karantinaya alınır. Kod hatası türünden
        // istisna yalnız alışın verisi gerçekten bozuksa (VeriKarantinasi.AlisVerisiSorunu) karantinaya alınır; değilse yükselir.
        var dagilimlar = new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<AlisKanalPayi>>>();
        var alisHatalari = new Dictionary<int, (Exception Hata, string Sorun)>();
        foreach (var a in alislar)
            try
            { dagilimlar[a.Id] = AlisHesaplari.OdemeDagilimlari(a); }
            catch (Exception e) when (VeriKarantinasi.VeriHatasiMi(e) || VeriKarantinasi.VeriKaynakliOlabilir(e) && VeriKarantinasi.AlisVerisiSorunu(a) is not null)
            {
                alisHatalari[a.Id] = (e, VeriKarantinasi.AlisVerisiSorunu(a) ?? "ödemeler ile kalem dağılımı tutarsız");
            }
        var kanalAdlari = _db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
        bool KanalAdi(int? kanalId, [NotNullWhen(true)] out string? ad)
        {
            ad = null;
            return kanalId is { } id && kanalAdlari.TryGetValue(id, out ad);
        }
        // Tamamlanmış ayların dondurulmuş kanal kümeleri (AyKanalKumesi), güncel adlarla. Kümede olup artık bulunmayan kanal (kısıtlar
        // silinmesini engeller; ör. kısıtsız geri yüklenmiş veritabanı) o ayın kümesine alınmaz ve ayın raporunda karantinada görünür.
        var ayKumeleri = new Dictionary<(int Yil, int Ay), IReadOnlyList<(string Ad, bool Aktif)>>();
        foreach (var (kumeAyi, uyeler) in AyKanalKumesi.Oku(_db))
        {
            var ayBasi = new DateOnly(kumeAyi.Yil, kumeAyi.Ay, 1);
            foreach (var (kanalId, _) in uyeler.Where(u => !kanalAdlari.ContainsKey(u.KanalId)))
                Karantinaya($"AyKanalKumesi:{AyKanalKumesi.AyMetni(kumeAyi)}:{kanalId}", () => $"{AyKanalKumesi.AyMetni(kumeAyi)} ayının kanal kümesindeki kanal #{kanalId} bulunamadı; Ortak gider kümedeki öteki kanallara bölündü",
                    ayBasi, AyRaporAnlikGoruntusu.AySonu(kumeAyi.Yil, kumeAyi.Ay));
            ayKumeleri[kumeAyi] = uyeler.Where(u => kanalAdlari.ContainsKey(u.KanalId)).Select(u => (kanalAdlari[u.KanalId], u.Aktif)).ToList();
        }
        // Kaydın payları (okunamayan dağılımda null) ve tutarı: kanalı çözülen pay kanalına yazılır (Kanal dolu). Çözülemeyen tutar
        // (Kanal null, Sorun dolu): kanalı bilinmeyen ya da boş pay ve pay toplamının kayıt tutarından eksik kalan kısmı (boş liste
        // dahil). Toplam tutarı aşıyorsa paylara güvenilmez, kaydın tamamı çözülemez. Böylece kayıt kasayı her durumda tutarı kadar
        // değiştirir; sağlam veride pay toplamı tutara eşittir (dağılımlar yazılırken doğrulanır) ve satırlar aynen kalır.
        List<(string? Kanal, decimal Tutar, string? Sorun)> Coz(IEnumerable<(int? KanalId, decimal Tutar)>? paylar, decimal tutar, string okunamadi)
        {
            if (paylar is null)
                return [(null, tutar, okunamadi)];
            var liste = paylar.ToList();
            var toplam = liste.Sum(p => p.Tutar);
            if (toplam > tutar)
                return [(null, tutar, $"pay toplamı ({Tl(toplam)}) kayıt tutarını ({Tl(tutar)}) aşıyor")];
            var sonuc = new List<(string? Kanal, decimal Tutar, string? Sorun)>(liste.Count + 1);
            foreach (var (kanalId, payTutari) in liste)
                sonuc.Add(KanalAdi(kanalId, out var ad) ? (ad, payTutari, null) : (null, payTutari, PaySorunu(kanalId)));
            if (toplam < tutar)
                sonuc.Add((null, tutar - toplam, liste.Count == 0 ? "dağılımı boş" : $"pay toplamı ({Tl(toplam)}) kayıt tutarından ({Tl(tutar)}) az"));
            return sonuc;
        }
        static IEnumerable<(int? KanalId, decimal Tutar)>? Paylar(List<KanalPayYaz>? paylar) => paylar?.Select(p => ((int?)p.KanalId, p.Tutar));
        var aylikOdemeler = new Dictionary<int, AylikGiderOdemeEntity>();
        foreach (var p in _db.AylikGiderOdemeler.AsNoTracking().Where(p => !p.Iptal && p.IslemId != null).OrderBy(p => p.Id).ToList())
            if (!aylikOdemeler.TryAdd(p.IslemId!.Value, p))
                Karantinaya("AylikGiderOdemesi:" + p.Id, () => $"Aylık gider ödemesi #{p.Id} ({Gun(p.Tarih)}): gider #{p.IslemId} aylık gider ödemesi #{aylikOdemeler[p.IslemId!.Value].Id} ile zaten sayıldı; bu ikinci bağ rapora alınmadı",
                    p.Tarih, p.Tarih);
        var aylikRevizyonlar = _db.AylikGiderRevizyonlar.AsNoTracking().ToDictionary(r => r.Id);
        // Rapor ekstre satırının yalnız sahiplik ve dağılım sütunlarını okur: eşleşme sütunları (EkstreEslesmesi) raporu etkilemez ve
        // göç öncesi şemada da okunabilir kalır.
        var imported = _db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal).Select(k => new EkstreKayitEntity
        {
            Id = k.Id,
            BelgeId = k.BelgeId,
            SatirNo = k.SatirNo,
            Tarih = k.Tarih,
            Aciklama = k.Aciklama,
            Tutar = k.Tutar,
            IslemTuru = k.IslemTuru,
            DagilimTuru = k.DagilimTuru,
            DagilimJson = k.DagilimJson,
            KrediKartiId = k.KrediKartiId,
            IslemId = k.IslemId,
            KartHarcamaId = k.KartHarcamaId,
            KartOdemeId = k.KartOdemeId,
            Iptal = k.Iptal,
            IptalAciklamasi = k.IptalAciklamasi
        }).ToList();
        var importedExpenses = new Dictionary<int, EkstreKayitEntity>();
        foreach (var k in imported.Where(k => k.IslemId != null).OrderBy(k => k.Id))
            if (!importedExpenses.TryAdd(k.IslemId!.Value, k))
                Karantinaya("EkstreKaydi:" + k.Id, () => $"Ekstre kaydı #{k.Id} (gider, {Gun(k.Tarih)}): gider #{k.IslemId} ekstre kaydı #{importedExpenses[k.IslemId!.Value].Id} ile zaten sayıldı; bu ikinci bağ rapora alınmadı",
                    k.Tarih, k.Tarih);
        var dbIslemler = new List<Islem>();
        // Rapora satır veren her kaydın tarihi (birden çok kanala bölünen kayıt bir kez): dönem ufkunu belirler.
        var kayitTarihleri = new List<DateOnly>();
        var sayac = 0;
        foreach (var kayit in kayitlar)
        {
            if (++sayac % 256 == 0)
                ct.ThrowIfCancellationRequested();
            var onceki = dbIslemler.Count;
            KayitSatirlari(kayit);
            if (dbIslemler.Count > onceki)
                kayitTarihleri.Add(kayit.Tarih);
        }
        void KayitSatirlari(IslemEntity kayit)
        {
            // Kaydın bütün satırları (kanal payları) aynı kaynak anahtarını taşır: K1 adedi kayıt düzeyinde sayılır. Döküm anahtarı
            // (KaynakAnahtari) aynıdır; ikisi de hesaba girmez.
            var kaynak = "Islem:" + kayit.Id;
            // Kanalı çözülemeyen tutar: kasadan düşer, hiçbir kanala yazılmaz.
            void Bekliyor(Islem satir, decimal tutar, string anahtar, Func<string> aciklama, Exception? hata = null)
            {
                dbIslemler.Add(satir with { Kanal = KanalEtiketleri.DagilimBekliyor, TutarTl = tutar, DagilimBekliyor = true });
                Karantinaya(anahtar, aciklama, satir.Tarih, EtkiSonu(satir), hata);
            }
            // Kaydın paylarını satırlara çevirir (bkz. Coz): çözülen pay kanalına, çözülemeyen tutar "Dağılım bekliyor"a.
            void PaySatirlari(Islem source, IEnumerable<(int? KanalId, decimal Tutar)>? paylar, string okunamadi, string anahtar, Func<string, string> aciklama)
            {
                foreach (var (ad, tutar, sorun) in Coz(paylar, source.TutarTl, okunamadi))
                    if (ad is not null)
                        dbIslemler.Add(source with { Kanal = ad, TutarTl = tutar });
                    else
                        Bekliyor(source, tutar, anahtar, () => aciklama(sorun!));
            }
            if (importedExpenses.TryGetValue(kayit.Id, out var importedExpense))
            {
                var source = kayit.ToCore() with { Kaynak = kaynak, KaynakAnahtari = kaynak };
                if (importedExpense.DagilimTuru == DagilimBicimleri.Genel)
                    dbIslemler.Add(source with { YalnizGenelKasa = true });
                else
                    PaySatirlari(source, VeriKarantinasi.Oku<TakipKanalPayi>(importedExpense.DagilimJson)?.Select(p => (p.KanalId, p.Tutar)), "dağılımı okunamadı",
                    "EkstreKaydi:" + importedExpense.Id, sorun => $"Ekstre kaydı #{importedExpense.Id} (gider, {Gun(kayit.Tarih)}): {sorun}; tutar 'Dağılım bekliyor' sayıldı");
                return;
            }
            if (aylikOdemeler.TryGetValue(kayit.Id, out var aylikOdeme))
            {
                var source = kayit.ToCore() with { AylikGider = true, Kaynak = kaynak, KaynakAnahtari = kaynak };
                var anahtar = "AylikGiderOdemesi:" + aylikOdeme.Id;
                string Aciklama(string sorun) => $"Aylık gider ödemesi #{aylikOdeme.Id} ({Gun(kayit.Tarih)}): {sorun}; tutar 'Dağılım bekliyor' sayıldı";
                if (!aylikRevizyonlar.TryGetValue(aylikOdeme.RevizyonId, out var revision))
                    Bekliyor(source, source.TutarTl, anahtar, () => Aciklama($"gider tanımı (revizyon #{aylikOdeme.RevizyonId}) bulunamadı"));
                else if (revision.DagilimTuru == DagilimBicimleri.Genel)
                    dbIslemler.Add(source with { YalnizGenelKasa = true });
                else
                    PaySatirlari(source, Paylar(VeriKarantinasi.Oku<KanalPayYaz>(revision.DagilimJson)), $"dağılımı (revizyon #{revision.Id}) okunamadı", anahtar, Aciklama);
                return;
            }
            if (kayit.KrediKartiId is { } cardId && kartTakip.TryGetValue(cardId, out var tracking))
            {
                // Başlangıçtan sonraki kart giderleri takip harcaması olarak izlenir (Sync).
                if (!tracking.EskiKayit || kayit.Tarih >= tracking.Baslangic)
                    return;
                // İşlem tarihi kuralı: başlangıçtan önceki her eski gider eski ay sonu kuralıyla bir
                // kez düşer. Etki tarihi kuralı (ilk sürüm geçişleri) raporları korumak için aynen
                // sürer: eski etkisi başlangıçta/sonrasında olan gider atlanır. Atlanan tutar kart
                // ekranında ve açılış logunda görünür (KartGecisHesabi.IlkSurumKalintisi, aynı koşul).
                if (KartGecisHesabi.IlkSurumdeAtlanir(tracking, kayit.Tarih))
                    return;
            }
            var islem = kayit.ToCore() with { Kaynak = kaynak, KaynakAnahtari = kaynak };
            if (!eslemeler.TryGetValue(kayit.Id, out var alis))
                dbIslemler.Add(islem);
            else if (alis.Durum != AlisDurumlari.Onaylandi)
                dbIslemler.Add(islem with { Kanal = KanalEtiketleri.DagilimBekliyor, DagilimBekliyor = true });
            else
            {
                var anahtar = "Alis:" + alis.Id;
                string Aciklama(string sorun) => $"Alış #{alis.Id} ödemesi (gider #{kayit.Id}, {Gun(kayit.Tarih)}): {sorun}; tutar 'Dağılım bekliyor' sayıldı";
                // Ödeme payları alış verisinden hesaplanır (saklanmaz); toplamları ödemeye eşittir, pay toplamı denetimi gerekmez.
                if (!dagilimlar.TryGetValue(alis.Id, out var odemeler) || !odemeler.TryGetValue(kayit.Id, out var paylar))
                {
                    alisHatalari.TryGetValue(alis.Id, out var hata);
                    Bekliyor(islem, islem.TutarTl, anahtar, () => Aciklama($"kanal dağılımı hesaplanamadı ({hata.Sorun ?? "ödemeler ile kalem dağılımı tutarsız"})"), hata.Hata);
                }
                else
                    foreach (var pay in paylar.Where(p => p.Tutar > 0))
                        if (KanalAdi(pay.KanalId, out var ad))
                            dbIslemler.Add(islem with { Kanal = ad, TutarTl = pay.Tutar });
                        else
                            Bekliyor(islem, pay.Tutar, anahtar, () => Aciklama(PaySorunu(pay.KanalId)));
            }
        }
        ct.ThrowIfCancellationRequested();
        var dbGelenler = _db.Gelenler.AsNoTracking().Include(g => g.KanalKaydi).ToList().Select(e => e.ToCore() with { KaynakAnahtari = "Gelen:" + e.Id }).ToList();
        var krediKayitlari = _db.Krediler.AsNoTracking().Include(k => k.KanalKaydi).ToList();
        var krediTakip = _db.TakipKrediler.AsNoTracking().ToDictionary(t => t.KrediId);
        var ayar = _db.Ayarlar.AsNoTracking().First();
        // Eski hesap ek gelirleri: hiçbir kayda bağlı olmayan hesap hareketi (ufuk ve gelir için tek sorgu).
        var ekGelirHareketleri = _db.HesapHareketler.AsNoTracking().Include(h => h.Kanal)
            .Where(h => h.IslemId == null && h.GelenId == null && h.KartOdemeId == null && h.KrediId == null).ToList();

        var baslangic = ayar.TakipBaslangic;
        // Dönem ufku yalnız GERÇEKLEŞEN veriye göre (DB işlemleri + bugün). Gelecek kredi
        // taksitleri ufku ileri ÇEKMEZ — aksi halde henüz ödenmemiş taksitler güncel kasadan
        // erken düşerdi (spec: gelecek taksit güncel kasayı etkilemez; ileri aylar o ayın
        // raporu sorulunca yansır). İleri tarihli kayıt ufku en çok IleriUfuk'a kadar uzatır.
        var ekGelirTarihleri = ekGelirHareketleri.Select(h => h.Tarih).ToList();
        var ufukTarihleri = kayitTarihleri.Concat(ekGelirTarihleri).Concat(imported.Where(k => k.IslemTuru == EkstreIslemTurleri.Gelir).Select(k => k.Tarih)).ToList();
        var enGecIslem = ufukTarihleri.DefaultIfEmpty(bugun).Max();
        string? ufukUyarisi = null;
        var ufuk = IleriUfuk(bugun);
        if (raporBitis is null && enGecIslem > ufuk)
        {
            var disarida = ufukTarihleri.Where(t => t > ufuk).ToList();
            ufukUyarisi = string.Create(CultureInfo.InvariantCulture,
                $"{disarida.Count} kayıt rapor ufkunun ({ufuk:dd.MM.yyyy}) ötesinde tarihli (en geç {disarida.Max():dd.MM.yyyy}). Tarihleri ufka girene kadar haftalık rapora ve dönem listesine girmez; tarihleri doğrulayın.");
            enGecIslem = ufuk;
        }
        var bitis = raporBitis ?? new[] { bugun, enGecIslem, baslangic }.Max();
        var donemler = DonemUretici.Uret(baslangic, bitis);

        // Krediyi sentetik kayıtlara türet (DB'ye yazılmaz, yalnız motora beslenir):
        // çekim → genel kasa geliri, taksitler → seçilen kanal/Ortak gideri. Türetilmiş taksitler kaynak kaydı (kredi) taşır.
        // Yalnız takipsiz ve eski kayıtlı takipli krediler türetilir (yeni takipli kredinin satırları taksit tablosundan gelir).
        // Planı geçersiz kredi (KrediTuretici.Dogrula) türetilemez: tutarı ve tarihleri bilinmediğinden rapora alınmaz.
        bool Turetilir(KrediEntity k) => !krediTakip.TryGetValue(k.Id, out var tracking) || tracking.EskiKayit;
        var gecersizKrediler = new HashSet<int>();
        foreach (var k in krediKayitlari.Where(Turetilir))
            try
            { KrediTuretici.Dogrula(k.ToCore()); }
            catch (ArgumentException e)
            {
                gecersizKrediler.Add(k.Id);
                Karantinaya("KrediPlani:" + k.Id, () => $"Kredi #{k.Id} ('{k.Ad}', çekim {Gun(k.CekimTarihi)}): taksit planı geçersiz (ödeme günü, taksit sayısı ya da tutar); türetilen çekim ve taksitler rapora alınmadı",
                    k.CekimTarihi, null, e);
            }
        var taksitler = krediKayitlari.Where(k => !k.GerceklesmeTakibi && Turetilir(k) && !gecersizKrediler.Contains(k.Id)).SelectMany(k =>
            KrediTuretici.TaksitGiderleri(k.ToCore()).Where(t => !krediTakip.TryGetValue(k.Id, out var tracking) || (tracking.EskiKayit && t.Tarih < tracking.Baslangic))
                .Select(t => t with { Kaynak = "Kredi:" + k.Id, KaynakAnahtari = "Kredi:" + k.Id }));
        var islemler = dbIslemler.Concat(taksitler).ToList();
        // Gelirlerin günü, döküm anahtarı ve açıklaması (Tarih, KaynakAnahtari, Aciklama) yalnız kasa hareket dökümü içindir.
        var cekimGelenleri = krediKayitlari.Where(k => Turetilir(k) && !gecersizKrediler.Contains(k.Id))
            .Select(k => KrediTuretici.CekimGeleni(k.ToCore(), donemler) is { } g
                ? g with { Tarih = k.CekimTarihi, KaynakAnahtari = "Kredi:" + k.Id, Aciklama = k.Ad } : null)
            .Where(g => g is not null)
            .Select(g => g!);
        var ekGelirler = new List<Gelen>();
        foreach (var hareket in ekGelirHareketleri)
            if (donemler.FirstOrDefault(d => d.Icerir(hareket.Tarih)) is { } period)
            {
                if (hareket.Kanal is { } kanal)
                    ekGelirler.Add(new(period.Start, kanal.Ad, hareket.Tutar) { Tarih = hareket.Tarih, KaynakAnahtari = "HesapHareket:" + hareket.Id, Aciklama = hareket.Aciklama });
                else
                {
                    // Kanalı olmayan gelir kasaya girer, hiçbir kanala yazılmaz.
                    ekGelirler.Add(new(period.Start, KanalEtiketleri.GenelKasa, hareket.Tutar, GenelGelir: true) { Tarih = hareket.Tarih, KaynakAnahtari = "HesapHareket:" + hareket.Id, Aciklama = hareket.Aciklama });
                    Karantinaya("EkGelir:" + hareket.Id, () => $"Ek gelir #{hareket.Id} ({Gun(hareket.Tarih)}): kanalı yok; tutar genel kasaya gelir yazıldı", period.Start, hareket.Tarih);
                }
            }
        var gelenler = dbGelenler.Concat(cekimGelenleri).Concat(ekGelirler).ToList();
        foreach (var income in imported.Where(k => k.IslemTuru == EkstreIslemTurleri.Gelir))
            if (donemler.FirstOrDefault(d => d.Icerir(income.Tarih)) is { } period)
            {
                Gelen Iz(Gelen g) => g with { Tarih = income.Tarih, KaynakAnahtari = "EkstreKayit:" + income.Id, Aciklama = income.Aciklama };
                if (income.DagilimTuru == DagilimBicimleri.Genel)
                { gelenler.Add(Iz(new(period.Start, KanalEtiketleri.GenelKasa, income.Tutar, GenelGelir: true))); continue; }
                foreach (var (ad, tutar, sorun) in Coz(VeriKarantinasi.Oku<TakipKanalPayi>(income.DagilimJson)?.Select(p => (p.KanalId, p.Tutar)), income.Tutar, "dağılımı okunamadı"))
                    if (ad is not null)
                        gelenler.Add(Iz(new(period.Start, ad, tutar)));
                    else
                    {
                        // Kanalı çözülemeyen gelir kasaya girer, hiçbir kanala yazılmaz.
                        gelenler.Add(Iz(new(period.Start, KanalEtiketleri.GenelKasa, tutar, GenelGelir: true)));
                        Karantinaya("EkstreKaydi:" + income.Id, () => $"Ekstre kaydı #{income.Id} (gelir, {Gun(income.Tarih)}): {sorun}; tutar genel kasaya gelir yazıldı", period.Start, income.Tarih);
                    }
            }
        // Takipli kredilerin taksitleri tek sorguda okunur (kredi başına sorgu yok).
        var takipliKrediler = krediKayitlari.Where(k => krediTakip.ContainsKey(k.Id)).Select(k => k.Id).ToArray();
        var takipliTaksitler = _db.TakipKrediTaksitler.AsNoTracking().Where(t => !t.Iptal && takipliKrediler.Contains(t.KrediId)).ToList().ToLookup(t => t.KrediId);
        foreach (var loan in krediKayitlari.Where(k => krediTakip.ContainsKey(k.Id)))
        {
            var tracking = krediTakip[loan.Id];
            if (!tracking.MevcutKredi && donemler.FirstOrDefault(d => d.Icerir(loan.CekimTarihi)) is { } period)
                foreach (var (ad, tutar, sorun) in Coz(Paylar(VeriKarantinasi.Oku<KanalPayYaz>(tracking.CekimPaylariJson)), loan.CekilenTutar, "kanal payları okunamadı"))
                    if (ad is not null)
                        gelenler.Add(new(period.Start, ad, tutar, KrediGirisi: true) { Tarih = loan.CekimTarihi, KaynakAnahtari = "Kredi:" + loan.Id, Aciklama = loan.Ad });
                    else
                    {
                        // Kanalı çözülemeyen çekim payı eski modelin kanalsız kredi girişidir: kasaya girer, kanala ve ay sonucuna girmez.
                        gelenler.Add(new(period.Start, KrediTuretici.KrediKanal, tutar) { Tarih = loan.CekimTarihi, KaynakAnahtari = "Kredi:" + loan.Id, Aciklama = loan.Ad });
                        Karantinaya("KrediCekimi:" + loan.Id, () => $"Kredi #{loan.Id} ('{loan.Ad}') çekimi ({Gun(loan.CekimTarihi)}): {sorun}; tutar kanalsız kredi girişi olarak genel kasaya yazıldı",
                            period.Start, loan.CekimTarihi);
                    }
            foreach (var installment in takipliTaksitler[loan.Id])
            {
                var satir = new Islem(installment.Tarih, loan.Ad + " / " + installment.No + ". taksit", installment.Tutar, KanalEtiketleri.DagilimBekliyor, GiderTipi.Cari) { Kaynak = "KrediTaksiti:" + installment.Id, KaynakAnahtari = "TakipKrediTaksit:" + installment.Id };
                foreach (var (ad, tutar, sorun) in Coz(Paylar(VeriKarantinasi.Oku<KanalPayYaz>(installment.DagilimJson)), installment.Tutar, "dağılımı okunamadı"))
                    if (ad is not null)
                        islemler.Add(satir with { TutarTl = tutar, Kanal = ad });
                    else
                    {
                        // Kanalı çözülemeyen taksit tutarı kasadan düşer, hiçbir kanala yazılmaz.
                        islemler.Add(satir with { TutarTl = tutar, DagilimBekliyor = true });
                        Karantinaya("KrediTaksiti:" + installment.Id, () => $"Kredi taksiti #{installment.Id} ('{loan.Ad}' {installment.No}. taksit, {Gun(installment.Tarih)}): {sorun}; tutar 'Dağılım bekliyor' sayıldı",
                            installment.Tarih, installment.Tarih);
                    }
            }
        }
        // Kart adları yalnız karantina açıklamasında gerekir: ilk gerektiğinde tek sorguda okunur (pay başına sorgu yok).
        Dictionary<int, string>? kartAdlari = null;
        string KartAdi(int cardId) =>
            (kartAdlari ??= _db.KrediKartlari.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad)).TryGetValue(cardId, out var ad) ? $"'{ad}'" : "adı yok";
        // Takipli kartın yalnız ödeme kanal payları gerekir: tam kart DTO'su (ekstre, harcama, kalan borç) hesaplanmaz.
        foreach (var cardId in kartTakip.Keys)
        {
            IReadOnlyList<(DateOnly Tarih, string? Not, IReadOnlyList<TakipKanalPayi> Dagilimlar, string Anahtar)> odemeler;
            try
            { odemeler = FinansTakipServisi.KartOdemeDagilimlari(takip, cardId).ToList(); }
            catch (Exception e) when (VeriKarantinasi.VeriKaynakliOlabilir(e))
            {
                // Kartın hesabının okuduğu kayıtlar doğrulanır. Okunamayan kayıt yoksa ve istisna veri hatası türünden değilse istisna
                // kod hatasıdır: karantinaya alınmaz, yükselir.
                var sorunlar = VeriKarantinasi.KartVerisiSorunlari(_db, cardId);
                if (sorunlar.Count == 0 && !VeriKarantinasi.VeriHatasiMi(e))
                    throw;
                KartOdemeleriBekliyor(cardId, e, sorunlar);
                continue;
            }
            var sira = 0;
            foreach (var payment in odemeler)
            {
                var kaynak = "KartOdemesi:" + cardId + ":" + sira++;
                foreach (var share in payment.Dagilimlar)
                {
                    var bekliyor = share.KanalId is null;
                    if (share.KanalId is { } kanalId && !kanalAdlari.ContainsKey(kanalId))
                    {
                        // Silinmiş kanala bağlı pay (kart hesabı adını "Silinmiş kanal" verir) hiçbir kanala yazılamaz.
                        bekliyor = true;
                        Karantinaya($"KrediKarti:{cardId}:{Gun(payment.Tarih)}:{kanalId}",
                            () => $"Kredi kartı #{cardId} ({KartAdi(cardId)}) {Gun(payment.Tarih)} tarihli ödemesi: {PaySorunu(kanalId)}; pay 'Dağılım bekliyor' sayıldı", payment.Tarih, payment.Tarih);
                    }
                    islemler.Add(new(payment.Tarih, "Kart ödemesi", share.Tutar, bekliyor ? KanalEtiketleri.DagilimBekliyor : share.Kanal, GiderTipi.KrediKarti, payment.Not,
                        DagilimBekliyor: bekliyor, NakitKartOdemesi: true)
                    { Kaynak = kaynak, KaynakAnahtari = payment.Anahtar });
                }
            }
        }
        // Kart hesabı (ödeme payları, harcama/iade/alış bağları) okunamadı: ödemelerin kanal dağılımı bilinmez, nakit etkisi ise
        // dağılımdan ayrı hesaplanır ve "Dağılım bekliyor" sayılır (kasadan düşer, hiçbir kanala yazılmaz). Kural kart hesabınınkiyle
        // (FinansTakipServisi.OdemeEtkisi) aynıdır: ödemeler Id sırasıyla işlenir; harcamaya düşen pay, harcamanın kasada önceden
        // sayılan tutarının (KasadaOncedenSayilanTutar, geçiş devri) önceki ödemelerce kullanılmayan kısmı kadar azalır; harcamaya
        // bağlanamayan pay (avans) tamamen nakittir. Devir iadesinin kasaya döndürdüğü önceden sayılan kısım (TakipIadeHesaplari.
        // KasadaSayilanDuzeltme) de aynı kuralla sayılır (FinansTakipServisi.EtkinKasadaSayilan, OncedenSayilanIadeleri): devrin
        // önceden sayılan tutarından düşülür ve iade tarihinde kasaya geri döner (eksi kart ödemesi); iadenin kanal payı bilinmediği
        // için satır "Dağılım bekliyor"dur. Payları okunamayan ödemenin hangi harcamayı kapattığı bilinmez: tam tutarıyla sayılır ve
        // uyarı kasadaki olası sapmayı (en çok kartın iadelerle düşülmüş önceden sayılan tutarı kadar düşük kasa) açıkça söyler.
        void KartOdemeleriBekliyor(int cardId, Exception hata, IReadOnlyList<string> sorunlar)
        {
            var oncedenSayilan = _db.TakipHarcamalar.AsNoTracking().Where(h => h.KrediKartiId == cardId)
                .Select(h => new { h.Id, h.KasadaOncedenSayilanTutar }).ToDictionary(h => h.Id, h => h.KasadaOncedenSayilanTutar);
            var harcamaIdleri = oncedenSayilan.Keys.ToArray();
            var iadeler = _db.TakipHarcamalar.AsNoTracking().Where(h => h.KrediKartiId == cardId && h.KaynakHarcamaId != null && !h.Iptal)
                .OrderBy(h => h.Id).Select(h => new { h.Id, KaynakId = h.KaynakHarcamaId!.Value, h.Tarih, h.Aciklama }).ToList();
            var iadeIdleri = iadeler.Select(i => i.Id).ToArray();
            var duzeltmeler = iadeIdleri.Length == 0 ? new Dictionary<int, decimal>()
                : _db.TakipIadeHesaplari.AsNoTracking().Where(x => iadeIdleri.Contains(x.HarcamaId))
                    .Select(x => new { x.HarcamaId, x.KasadaSayilanDuzeltme }).ToDictionary(x => x.HarcamaId, x => x.KasadaSayilanDuzeltme);
            foreach (var kaynak in iadeler.GroupBy(i => i.KaynakId))
                if (oncedenSayilan.TryGetValue(kaynak.Key, out var sayilan) && sayilan > 0
                    && kaynak.Sum(i => duzeltmeler.GetValueOrDefault(i.Id)) is > 0 and var duzeltme)
                    oncedenSayilan[kaynak.Key] = sayilan - duzeltme;
            var taksitHarcamasi = _db.TakipKartTaksitler.AsNoTracking().Where(t => harcamaIdleri.Contains(t.HarcamaId))
                .Select(t => new { t.Id, t.HarcamaId }).ToDictionary(t => t.Id, t => t.HarcamaId);
            var aktif = _db.TakipKartOdemeler.AsNoTracking().Where(p => p.KrediKartiId == cardId && !p.Iptal).OrderBy(p => p.Id).ToList();
            var kullanilan = new Dictionary<int, decimal>();
            var okunamayan = 0;
            decimal toplam = 0;
            foreach (var p in aktif)
            {
                var etki = p.Tutar;
                if (VeriKarantinasi.Oku<KartTaksitPayi>(p.PaylarJson) is not { } paylar)
                    okunamayan++;
                else
                {
                    etki = 0;
                    foreach (var grup in paylar.GroupBy(x => taksitHarcamasi.TryGetValue(x.TaksitId, out var harcama) ? harcama : 0))
                    {
                        var tutar = grup.Sum(x => x.Tutar);
                        if (grup.Key == 0)
                        { etki += tutar; continue; }
                        var onceki = kullanilan.GetValueOrDefault(grup.Key);
                        etki += tutar - Math.Min(tutar, Math.Max(0, oncedenSayilan[grup.Key] - onceki));
                        kullanilan[grup.Key] = onceki + tutar;
                    }
                }
                toplam += etki;
                if (etki != 0)
                    islemler.Add(new(p.Tarih, "Kart ödemesi", etki, KanalEtiketleri.DagilimBekliyor, GiderTipi.KrediKarti, p.Not,
                        DagilimBekliyor: true, NakitKartOdemesi: true)
                    { Kaynak = "KartOdemesi:" + cardId + ":" + p.Id, KaynakAnahtari = "TakipKartOdeme:" + p.Id });
            }
            var tarihler = aktif.Select(p => p.Tarih).ToList();
            var iadeSayisi = 0;
            decimal iadeToplami = 0;
            foreach (var iade in iadeler)
            {
                var duzeltme = duzeltmeler.GetValueOrDefault(iade.Id);
                if (duzeltme <= 0)
                    continue;
                iadeSayisi++;
                iadeToplami += duzeltme;
                tarihler.Add(iade.Tarih);
                islemler.Add(new(iade.Tarih, "Kart ödemesi", -duzeltme, KanalEtiketleri.DagilimBekliyor, GiderTipi.KrediKarti, "Önceden sayılan kart borcu iadesi: " + iade.Aciklama,
                    DagilimBekliyor: true, NakitKartOdemesi: true)
                { Kaynak = "KartIadesi:" + cardId + ":" + iade.Id, KaynakAnahtari = "TakipHarcama:" + iade.Id });
            }
            var iadeNotu = iadeSayisi > 0
                ? $"; {iadeSayisi} devir iadesinin kasaya döndürdüğü önceden sayılan tutar ({Tl(iadeToplami)}) kasaya geri eklendi, kanalı 'Dağılım bekliyor'" : "";
            var sapma = okunamayan > 0 && oncedenSayilan.Values.Sum() is > 0 and var sayilanToplami
                ? $"; payları okunamayan ödemenin kasada önceden sayılan kısmı ayrılamadı; kasa en çok {Tl(sayilanToplami)} düşük görünebilir" : "";
            Karantinaya("KrediKarti:" + cardId, () => $"Kredi kartı #{cardId} ({KartAdi(cardId)}): ödemelerin kanal dağılımı hesaplanamadı"
                + $" ({(sorunlar.Count > 0 ? string.Join("; ", sorunlar) : "kart kayıtları tutarsız")}); {aktif.Count} ödemenin nakit etkisi ({Tl(toplam)}) 'Dağılım bekliyor' sayıldı{iadeNotu}{sapma}",
                tarihler.Count > 0 ? tarihler.Min() : bugun, tarihler.Count > 0 ? tarihler.Max() : bugun, hata);
        }

        CekSatirlari(_db, donemler, gelenler, islemler, KanalAdi, (anahtar, aciklama, tarih) => Karantinaya(anahtar, aciklama, tarih, tarih));

        foreach (var k in karantina)
            VeriKarantinasi.Logla(_db, k.Anahtar, k.Aciklama, k.Hata);
        return new Yuk(kanallar, islemler, gelenler, donemler, ayar.KasaAcilisDevri,
            kanalAdlari.ToDictionary(k => k.Value, k => (int?)k.Key), ufukUyarisi, karantina, ayKumeleri);
    }

    /// <summary>Kanal kimliğini adına çözer (Yukle'deki KanalAdi).</summary>
    private delegate bool KanalCozucu(int? kanalId, [NotNullWhen(true)] out string? ad);

    /// <summary>
    /// Çek ve senet hareketlerinin türetilmiş satırları (docs/specs/2026-10-01-cekler.md "Rapora etkisi"): kredi taksitlerindeki gibi
    /// bellekte türetilir (<see cref="CekTuretici"/>), veritabanına Islem/Gelen yazılmaz. Satırlar listelerin sonuna eklenir: çek
    /// yokken satırlar, sıraları ve tutarlar aynıdır (altın rapor testi). Verilen çekin ödemesinde kasası boş hareket Ortak'tır;
    /// kanalı çözülemeyen kasa etkili hareket karantinaya alınır, geliri genel kasaya, gideri "Dağılım bekliyor"a yazılır.
    /// </summary>
    private static void CekSatirlari(KasaDbContext db, IReadOnlyList<Donem> donemler, List<Gelen> gelenler, List<Islem> islemler, KanalCozucu kanalAdi,
        Action<string, Func<string>, DateOnly> karantinaya)
    {
        // Çek tabloları migration 20261008000100_Cekler ile gelir: göç öncesi şemadaki rapor (göç testleri) tablolar olmadan hesaplanır.
        if (db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='Cekler'").Single() == 0)
            return;
        var cekler = db.Cekler.AsNoTracking().OrderBy(c => c.Id).ToList();
        if (cekler.Count == 0)
            return;
        var hareketler = db.CekHareketler.AsNoTracking().ToList().ToLookup(h => h.CekId);
        foreach (var cek in cekler)
        {
            var satirlar = new List<CekHareketi>();
            foreach (var h in hareketler[cek.Id].OrderBy(h => h.Sira))
            {
                string? kanal = null;
                if (kanalAdi(h.KanalId, out var ad))
                    kanal = ad;
                else if (h.KanalId is null && h.Tur == CekHareketTurleri.Odeme)
                    kanal = KanalEtiketleri.Ortak;
                else if (CekKurallari.KasaEtkili(h.Tur))
                    karantinaya("CekHareketi:" + h.Id, () => $"Çek hareketi #{h.Id} (çek #{cek.Id}, {h.Tarih.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}): "
                        + (h.KanalId is { } id ? $"kasası olan kanal (#{id}) bulunamadı" : "kasası boş") + "; geliri genel kasaya, gideri 'Dağılım bekliyor'a yazıldı", h.Tarih);
                satirlar.Add(new CekHareketi(h.Id, h.Sira, h.Tur, h.Tarih, h.Tutar, h.NetTutar, kanal, h.Karsi));
            }
            var (cekGelenleri, cekIslemleri) = CekTuretici.Satirlar(new CekBilgisi(cek.Id, cek.Tur, cek.Yon, cek.No, cek.Kisi, cek.Tutar), satirlar, donemler);
            gelenler.AddRange(cekGelenleri);
            islemler.AddRange(cekIslemleri);
        }
    }

    /// <summary>
    /// Ayın raporu, ayın kanal kümesiyle (core-1; bkz. <see cref="AyKanalKumesi"/>). Kümesi olmayan ay (içinde bulunulan ay, ileri
    /// aylar ve henüz dondurulmamış tamamlanmış ay) bugünkü davranışla birebir hesaplanır: bütün kanallar raporun sırasıyla, Ortak
    /// gider aktif kanallara. Kümesi olan ayda satırlar kümenin kanalları (kümenin sırasıyla, güncel adla) ve Ortak gider kümenin o
    /// ayda aktif olan kanallarına kümenin sırasıyla bölünür: sonradan eklenen, pasife alınan ya da yeniden sıralanan kanal o ayı
    /// değiştirmez. Kümeden sonra açılan kanal yalnız o ayda tutarı varsa (kilitli olmayan geçmiş aya girilen kayıt) sona eklenir ve
    /// Ortak payı almaz; tutarı olmayan satırı rapora girmez (sıfır satır hiçbir toplamı değiştirmez).
    /// </summary>
    private static AylikRapor AyRaporu(Yuk y, int yil, int ay, int kural)
    {
        if (!y.AyKumeleri.TryGetValue((yil, ay), out var kume))
            return HesapMotoru.AylikHesapla(yil, ay, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler, kural);
        var uyeAdlari = kume.Select(u => u.Ad).ToHashSet();
        var kanallar = y.Kanallar.ToDictionary(k => k.Ad);
        var satirlar = kume.Select(u => kanallar[u.Ad]).Concat(y.Kanallar.Where(k => !uyeAdlari.Contains(k.Ad))).ToList();
        var rapor = HesapMotoru.AylikHesapla(yil, ay, satirlar, y.Islemler, y.Gelenler, y.Donemler, kural,
            kume.Where(u => u.Aktif).Select(u => u.Ad).ToList());
        return rapor with
        {
            Kanallar = rapor.Kanallar.Where(k => uyeAdlari.Contains(k.Kanal)
                || k.Gelen != 0 || k.CariGiden != 0 || k.SabitGider != 0 || k.KrediKarti != 0 || k.KrediGirisi != 0).ToList(),
        };
    }

    /// <summary>Haftalık rapor. Ufkun ötesinde kayıt, takip başlangıcından önce tarihli gider (K1) ya da karantinaya alınan kayıt
    /// varsa son dönem <see cref="HaftalikOzet.VeriSagligiUyarisi"/> taşır; ufuk ve K1 uyarısı tutarları değiştirmez.</summary>
    public IReadOnlyList<HaftalikOzet> Haftalik(CancellationToken ct = default)
    {
        var y = Yukle(new TakipHesapBaglami(_db, ct));
        ct.ThrowIfCancellationRequested();
        var sonuc = HesapMotoru.HaftalikHesapla(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
        var uyarilar = new[] { y.UfukUyarisi, HesapMotoru.BaslangicOncesiUyarisi(HesapMotoru.BaslangicOncesi(y.Islemler, y.Donemler), aylik: false),
                KarantinaUyarisi(y.Karantina) }
            .OfType<string>().ToList();
        if (uyarilar.Count == 0 || sonuc.Count == 0)
            return sonuc;
        var liste = sonuc.ToList();
        liste[^1] = liste[^1] with { VeriSagligiUyarisi = string.Join(" ", uyarilar) };
        return liste;
    }

    /// <summary>Açık (kilitli olmayan) aylara ve panelin "bu ay"ına uygulanan aylık rapor kuralı: güncel kural (K2, kredi
    /// girişi Gelen/Ay sonucu dışında). Ay kapatılırken rapor bu kuralla dondurulur (<see cref="AyRaporAnlikGoruntusu"/>):
    /// kural sonradan değişse de kapatılmış ay değişmez. Panelin "Bu ayın sonucu" da aylık sonuçtur, kredi girişini
    /// içermez; kasa bakiyeleri (haftalık) krediyi nakit olarak içermeye devam eder.</summary>
    public const int AcikAyKurali = AylikKural.Guncel;

    /// <summary>Ayın canlı hesaplanan raporu (kilitli olsa da görüntüye bakmaz). <paramref name="kuralSurumu"/> verilmezse
    /// <see cref="AcikAyKurali"/>. Ayın sonucuna takip başlangıcından önce tarihli gider giriyorsa (K1) rapor, tutarları
    /// değiştirmeyen bir veri sağlığı uyarısı taşır (her iki kuralda; kilitlenirken görüntüye de yazılır). Karantinaya alınan
    /// kayıt yalnız dokunduğu ayın raporunda uyarılır: ilgisiz ayın raporu ne düşer ne uyarı taşır.</summary>
    public AylikRapor Aylik(int yil, int ay, CancellationToken ct = default, int? kuralSurumu = null) => AylikVeKarantina(yil, ay, ct, kuralSurumu).Rapor;

    /// <summary><see cref="Aylik"/> ve aya dokunan karantina kayıtları. Ay kapatma ve geçiş tohumu bunlara bakar: bozuk kayıtla
    /// hesaplanan (yaklaşık) rapor dondurulmaz (<see cref="AyRaporAnlikGoruntusu"/>).</summary>
    internal (AylikRapor Rapor, IReadOnlyList<KarantinaKaydi> Karantina) AylikVeKarantina(int yil, int ay, CancellationToken ct = default, int? kuralSurumu = null)
    {
        var y = Yukle(new TakipHesapBaglami(_db, ct), new DateOnly(yil, ay, DateTime.DaysInMonth(yil, ay)));
        var rapor = AyRaporu(y, yil, ay, kuralSurumu ?? AcikAyKurali);
        var karantina = y.Karantina.Where(k => k.AyaDokunur(yil, ay)).ToList();
        var uyarilar = new[] { HesapMotoru.BaslangicOncesiUyarisi(HesapMotoru.BaslangicOncesi(y.Islemler, y.Donemler, (yil, ay)), aylik: true),
                KarantinaUyarisi(karantina) }
            .OfType<string>().ToList();
        return (uyarilar.Count > 0 ? rapor with { VeriSagligiUyarisi = string.Join(" ", uyarilar) } : rapor, karantina);
    }

    /// <summary>API'nin aylık raporu: kilitli ay dondurulmuş görüntüsünden (<c>"dondurulmus": true</c>), açık ay canlı
    /// hesaplanır. Görüntüsü olmayan kilitli ay (<see cref="AyRaporAnlikGoruntusu.TohumBekliyor"/>: bu sürümden önce kilitlenmiş,
    /// geçiş tohumunun karantinaya alınmış kayıt yüzünden dondurmadığı ay) kural 1 ile canlı hesaplanır ve uyarıyı taşır.</summary>
    public object AylikYanit(int yil, int ay, CancellationToken ct = default)
    {
        int? kural = null;
        using (_db.OkumaBaslat())
        {
            if (AyRaporAnlikGoruntusu.Oku(_db, yil, ay) is { } dondurulmus)
                return dondurulmus;
            if (AyRaporAnlikGoruntusu.TohumBekliyor(_db, yil, ay))
                kural = AylikKural.V1;
        }
        return Aylik(yil, ay, ct, kural);
    }

    public IReadOnlyList<Donem> Donemler(CancellationToken ct = default) => Yukle(new TakipHesapBaglami(_db, ct)).Donemler;

    public PanelDto Panel(CancellationToken ct = default) => Panel(new TakipHesapBaglami(_db, ct));

    internal PanelDto Panel(TakipHesapBaglami takip) => PanelVeUyari(takip).Panel;

    /// <summary>Paneli verilen istek bağlamıyla hesaplar: aynı istekte takip özeti de hesaplanıyorsa kart verisi ve
    /// ödeme etkileri yeniden okunmaz/hesaplanmaz. <c>Uyari</c>: karantinaya alınan kayıtların veri sağlığı uyarısı (yoksa
    /// null); ana sayfa özeti taşır (<see cref="AnaSayfaDto.VeriSagligiUyarisi"/>).</summary>
    internal (PanelDto Panel, string? Uyari) PanelVeUyari(TakipHesapBaglami takip)
    {
        // Gelecek tarihli manuel kayıtlar paneli gelecek bir döneme taşıyamaz.
        var bugun = takip.Bugun;
        var y = Yukle(takip, bugun);
        takip.Iptal.ThrowIfCancellationRequested();
        var haftalik = HesapMotoru.HaftalikHesapla(y.KasaAcilis, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler);
        var son = haftalik.Count > 0 ? haftalik[^1] : null;

        var guncelKasa = son?.KasaDevir ?? y.KasaAcilis;
        var buHafta = son?.KasaSonucu ?? 0m;
        var kanalIdleri = y.KanalIdleri;
        var kanalBakiyeleri = son is not null
            ? son.Kanallar.Select(k => new KanalBakiye(k.Kanal, k.Devir, kanalIdleri.GetValueOrDefault(k.Kanal))).ToList()
            : y.Kanallar.Select(k => new KanalBakiye(k.Ad, k.AcilisDevri, kanalIdleri.GetValueOrDefault(k.Ad))).ToList();

        var buAyRapor = AyRaporu(y, bugun.Year, bugun.Month, AcikAyKurali);
        var buAy = buAyRapor.Kanallar.Sum(k => k.AySonucu) - buAyRapor.DagilimBekleyenTutar - buAyRapor.GenelGider + buAyRapor.GenelGelir;

        return (new PanelDto(guncelKasa, kanalBakiyeleri, buHafta, buAy,
            haftalik.Sum(h => h.DagilimBekleyenTutar)), KarantinaUyarisi(y.Karantina));
    }

    /// <summary>Kasa hareket dökümü (gap-denetim-izi-gozlemlenebilirlik-3): panelle aynı yükten (<see cref="Yukle"/>, bugüne kadarki
    /// dönemler) genel kasayı ve kanal bakiyelerini oluşturan bütün hareketler. Hesap yolu değişmez; satırlar
    /// <see cref="HesapMotoru.KasaHareketleri"/> ile aynı kurallardan türetilir. Değişmez: açılış + Σ genel kasa etkisi =
    /// <see cref="PanelDto.GuncelKasa"/>, kanal açılışı + Σ kanal etkisi = panelin kanal bakiyesi.</summary>
    public KasaDokumu Dokum(CancellationToken ct = default) => Dokum(new TakipHesapBaglami(_db, ct));

    internal KasaDokumu Dokum(TakipHesapBaglami takip)
    {
        var bugun = takip.Bugun;
        var y = Yukle(takip, bugun);
        takip.Iptal.ThrowIfCancellationRequested();
        return new(bugun, y.KasaAcilis, y.Kanallar, y.KanalIdleri, HesapMotoru.KasaHareketleri(y.Kanallar, y.Islemler, y.Gelenler, y.Donemler));
    }
}

/// <summary>
/// Rapor yolunun veri karantinası (gap-veri-degismezleri-patlama-yaricapi-3). Rapora satır veren tek bir kaydın kendi verisindeki
/// sorun (bilinmeyen kanal kimliği, okunamayan dağılım/pay JSON'u, eksik kaynak, türetilemeyen plan) bütün raporu düşürmez: kayıt
/// karantinaya alınır, raporda veri sağlığı uyarısıyla görünür ve kayıt anahtarıyla uygulama başına bir kez Warning loglanır.
/// Hata politikası listeleme yolununkiyle (<c>FinansTakipServisi.Adlandir</c>: silinmiş kanal adı) aynı yöndedir: kayıt
/// gösterilir, sorun gizlenmez. Veritabanı, iptal ve kod hataları karantinaya alınmaz; olduğu gibi yükselir. Kod hatası
/// türündeki istisna (boş başvuru, genel argüman hatası) yalnız kaydın verisi doğrulanıp sorun bulunursa karantinaya alınır.
/// </summary>
internal static class VeriKarantinasi
{
    public const string LogKategorisi = "Kasa.Rapor";

    /// <summary>Türünden kaydın kendi verisinden doğduğu belli olan hata: okunamayan JSON, eksik sözlük anahtarı, kaydın kendi
    /// doğrulamasının reddi (ArgumentOutOfRangeException: geçersiz plan, dağıtım toplamı) ve anlamsız büyüklükteki tutarın taşması.
    /// Kod hatası türleri (NullReferenceException, genel ArgumentException, InvalidOperationException, IndexOutOfRangeException),
    /// veritabanı hatası, iptal ve atılmış bağlam bu sınıfa girmez.</summary>
    public static bool VeriHatasiMi(Exception e) => e is JsonException or KeyNotFoundException or ArgumentOutOfRangeException or OverflowException;

    /// <summary>Veri kaynaklı olabilecek istisna: <see cref="VeriHatasiMi"/> ya da boş öğeli JSON listesinin (NullReferenceException),
    /// aynı kanalı iki kez ya da geçersiz kanal kimliğini taşıyan payların (ArgumentException) yol açabileceği hata. Bu son ikisi
    /// yalnız kaydın verisi doğrulanıp sorun bulunursa (<see cref="KartVerisiSorunlari"/>, <see cref="KrediVerisiSorunlari"/>,
    /// <see cref="AlisVerisiSorunu"/>) karantinaya alınır; bulunmazsa kod hatasıdır ve yükselir. InvalidOperationException hiç alınmaz.</summary>
    public static bool VeriKaynakliOlabilir(Exception e) => VeriHatasiMi(e) || e is NullReferenceException or ArgumentException;

    /// <summary>Kaydın JSON listesi (dağılım, pay); okunamazsa ya da boş (null) öğe içeriyorsa null: çağıran kaydı karantinaya alır.</summary>
    public static List<T>? Oku<T>(string? json)
    {
        if (json is null)
            return null;
        List<T> liste;
        try
        { liste = FinansTakipServisi.Read<T>(json); }
        catch (JsonException) { return null; }
        return liste.Exists(x => x is null) ? null : liste;
    }

    /// <summary>Kanal payı listesi okunabilir ve tutarlı mı: boş öğe yok, kanal kimlikleri pozitif ve her kanal bir kez.</summary>
    private static bool KanalPaylariGecerli(string? json) =>
        Oku<KanalPayYaz>(json) is { } paylar && paylar.TrueForAll(p => p.KanalId > 0) && paylar.Select(p => p.KanalId).Distinct().Count() == paylar.Count;

    /// <summary>Takipli kartın hesabının okuduğu kayıtlardaki veri sorunları: payları okunamayan ödeme (iptal edilmiş dahil: kart hesabı
    /// hepsini okur), dağılımı okunamayan ya da tutarsız harcama/iade, ödeme dağılımı hesaplanamayan bağlı alış. Boş liste: kayıtlar
    /// okunabiliyor, kart hesabındaki kod hatası türünden istisna yükselmelidir. Yalnız hata yolunda çalışır.</summary>
    public static IReadOnlyList<string> KartVerisiSorunlari(KasaDbContext db, int kartId)
    {
        var sorunlar = new List<string>();
        var odemeler = db.TakipKartOdemeler.AsNoTracking().Where(p => p.KrediKartiId == kartId).OrderBy(p => p.Id).Select(p => new { p.Id, p.PaylarJson }).ToList()
            .Where(p => Oku<KartTaksitPayi>(p.PaylarJson) is null).Select(p => "#" + p.Id).ToList();
        if (odemeler.Count > 0)
            sorunlar.Add("payları okunamayan ödeme: " + string.Join(", ", odemeler));
        var harcamalar = db.TakipHarcamalar.AsNoTracking().Where(h => h.KrediKartiId == kartId).OrderBy(h => h.Id).Select(h => new { h.Id, h.IslemId, h.DagilimJson }).ToList();
        var bozukHarcamalar = harcamalar.Where(h => !KanalPaylariGecerli(h.DagilimJson)).Select(h => "#" + h.Id).ToList();
        if (bozukHarcamalar.Count > 0)
            sorunlar.Add("dağılımı okunamayan harcama: " + string.Join(", ", bozukHarcamalar));
        var islemIdleri = harcamalar.Where(h => h.IslemId != null).Select(h => h.IslemId!.Value).ToArray();
        if (islemIdleri.Length == 0)
            return sorunlar;
        var alisIdleri = db.AlisOdemeler.AsNoTracking().Where(o => islemIdleri.Contains(o.IslemId)).Select(o => o.AlisId).Distinct().ToArray();
        var bozukAlislar = db.Alislar.AsNoTracking().Include(a => a.Kalemler).ThenInclude(k => k.Dagilimlar).Include(a => a.Odemeler).ThenInclude(o => o.Islem)
            .Where(a => alisIdleri.Contains(a.Id)).AsSplitQuery().AsEnumerable().Where(a => AlisVerisiSorunu(a) is not null).Select(a => "#" + a.Id).ToList();
        if (bozukAlislar.Count > 0)
            sorunlar.Add("ödeme dağılımı hesaplanamayan bağlı alış: " + string.Join(", ", bozukAlislar));
        return sorunlar;
    }

    /// <summary>Takipli kredinin hesabının okuduğu kayıtlardaki veri sorunları (kanal listesi, çekim payları, taksit dağılımları); boş
    /// liste: kayıtlar okunabiliyor. Yalnız hata yolunda çalışır.</summary>
    public static IReadOnlyList<string> KrediVerisiSorunlari(KasaDbContext db, int krediId)
    {
        var sorunlar = new List<string>();
        if (db.TakipKrediler.AsNoTracking().Where(t => t.KrediId == krediId).Select(t => new { t.KanalIdleriJson, t.CekimPaylariJson }).SingleOrDefault() is { } takip)
        {
            if (Oku<int>(takip.KanalIdleriJson) is null)
                sorunlar.Add("kanal listesi okunamadı");
            if (!KanalPaylariGecerli(takip.CekimPaylariJson))
                sorunlar.Add("çekim payları okunamadı");
        }
        var taksitler = db.TakipKrediTaksitler.AsNoTracking().Where(t => t.KrediId == krediId).OrderBy(t => t.No).Select(t => new { t.Id, t.DagilimJson }).ToList()
            .Where(t => !KanalPaylariGecerli(t.DagilimJson)).Select(t => "#" + t.Id).ToList();
        if (taksitler.Count > 0)
            sorunlar.Add("dağılımı okunamayan taksit: " + string.Join(", ", taksitler));
        return sorunlar;
    }

    /// <summary>Onaylı alışın ödeme dağılımını (<see cref="AlisHesaplari.OdemeDagilimlari"/>) imkânsız kılan veri sorunu; yoksa null.
    /// Bellekteki (kalemleri, dağılımları ve ödeme giderleriyle yüklenmiş) kayda bakar.</summary>
    public static string? AlisVerisiSorunu(AlisEntity a)
    {
        if (a.Durum != AlisDurumlari.Onaylandi)
            return null;
        if (a.Odemeler.Any(o => o.Islem is null))
            return "ödemesinin gider kaydı yok";
        var dagilimlar = a.Kalemler.SelectMany(k => k.Dagilimlar).ToList();
        if (dagilimlar.Exists(d => d.KanalId <= 0))
            return "kalem dağılımında geçersiz kanal var";
        return dagilimlar.GroupBy(d => d.KanalId).Any(g => g.Sum(d => d.Tutar) > 0) ? null : "kalem dağılımı yok";
    }

    /// <summary>Karantina kaydını <paramref name="anahtar"/> (kayıt türü ve kimliği) ile uygulama başına bir kez Warning olarak yazar:
    /// aynı bozuk kayıt her rapor isteğinde ve dakikalık bildirim işçisinde yeniden görülür, log taşmaz.</summary>
    public static void Logla(KasaDbContext db, string anahtar, string aciklama, Exception? hata = null)
    {
        if (!IlkKezMi(db, anahtar))
            return;
        db.GetService<ILoggerFactory>().CreateLogger(LogKategorisi).LogWarning(hata, "Veri karantinası [{Anahtar}]: {Aciklama}", anahtar, aciklama);
    }

    // Kapsam uygulamanın kök ILoggerFactory'sidir (üretimde süreçte tek; testlerde her uygulama ayrı). Önbellek sınırlıdır:
    // dolunca boşaltılır ve uyarılar yeniden birer kez yazılır (FinansTakipServisi'nin kırpma uyarısıyla aynı kural).
    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<string, byte>> Gorulenler = new();
    private static readonly object VarsayilanKapsam = new();
    private const int GorulenSiniri = 1024;
    private static bool IlkKezMi(KasaDbContext db, string anahtar)
    {
        var kapsam = (object?)db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?.GetService<ILoggerFactory>() ?? VarsayilanKapsam;
        var gorulen = Gorulenler.GetValue(kapsam, _ => new(StringComparer.Ordinal));
        if (gorulen.Count >= GorulenSiniri)
            gorulen.Clear();
        return gorulen.TryAdd(anahtar, 0);
    }
}
