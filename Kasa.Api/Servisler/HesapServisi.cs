using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kasa.Api;
using Kasa.Api.Data;
using Kasa.Core;
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
        IReadOnlyList<KarantinaKaydi> Karantina);

    /// <summary>Rapora olduğu gibi giremeyen (karantinaya alınan) kayıt: kayıt anahtarı (tür ve kimlik), kullanıcıya gösterilen
    /// açıklama ve kaydın rapordaki etki aralığı (aylık rapor yalnız dokunduğu ayda uyarır; <see cref="Son"/> null ise açık uçlu).</summary>
    internal sealed record KarantinaKaydi(string Anahtar, string Aciklama, DateOnly Ilk, DateOnly? Son, Exception? Hata = null)
    {
        public bool AyaDokunur(int yil, int ay) =>
            Ilk <= AyRaporAnlikGoruntusu.AySonu(yil, ay) && (Son is not { } son || son >= new DateOnly(yil, ay, 1));
    }

    /// <summary>Uyarı metninde adıyla sayılan en çok karantina kaydı; fazlası sayıyla belirtilir.</summary>
    private const int UyaridaEnCokKayit = 5;

    /// <summary>Karantina kayıtlarının veri sağlığı uyarısı; kayıt yoksa null.</summary>
    internal static string? KarantinaUyarisi(IReadOnlyCollection<KarantinaKaydi> kayitlar)
    {
        if (kayitlar.Count == 0) return null;
        var liste = string.Join("; ", kayitlar.Take(UyaridaEnCokKayit).Select(k => k.Aciklama));
        var fazlasi = kayitlar.Count > UyaridaEnCokKayit ? $"; … ve {kayitlar.Count - UyaridaEnCokKayit} kayıt daha" : "";
        return $"Okunamayan {kayitlar.Count} kayıt karantinaya alındı: {liste}{fazlasi}. Kayıt düzeltilene dek rapor onu bu biçimde sayar; kaydı düzeltin.";
    }

    /// <summary>
    /// Raporun bütün verisini tek anlık görüntüden okur ve motor satırlarına çevirir. Karantina (gap-veri-degismezleri-patlama-yaricapi-3):
    /// tek bir kaydın kendi verisindeki sorun raporu düşürmez; kayıt, türüne göre aşağıdaki biçimde rapora girer ve
    /// <see cref="KarantinaKaydi"/> olarak döner (raporda veri sağlığı uyarısı, kayıt anahtarıyla bir kez Warning).
    /// - Gider (ekstre gideri, aylık gider ödemesi, alış ödemesi, takipli kredi taksiti, kart ödemesi): para kasadan çıkmıştır, yalnız
    ///   kanalı bilinmez. Çözülemeyen pay (okunamayan dağılımda kaydın tamamı) "Dağılım bekliyor" olur: tutar kasadan düşer, hiçbir
    ///   kanala yazılmaz, dağılım bekleyen tutarda görünür. Hesabı yapılamayan takipli kartın iptal edilmemiş ödemeleri tam
    ///   tutarlarıyla (hangi harcamayı kapattıkları bilinmediğinden) "Dağılım bekliyor" sayılır.
    /// - Gelir (ekstre geliri, eski hesap ek geliri): para kasaya girmiştir, kanalı bilinmez: tutar genel kasaya gelir yazılır
    ///   (dağılım bekleyen giderin gelir karşılığı). Takipli kredi çekiminin çözülemeyen payı eski modelin kanalsız kredi girişi
    ///   olur: kasaya girer, hiçbir kanala ve ay sonucuna yazılmaz, aylık raporun kredi girişinde görünür (K2 korunur).
    /// - Tutarı türetilemeyen kayıt (eski kredinin geçersiz planı: taksit sayısı/günü ya da tutarı anlamsız) rapora alınmaz;
    ///   uydurma satır kasaya yazılmaz.
    /// Sağlam veride satırlar, sıraları ve tutarlar karantinasız hesapla birebir aynıdır (altın rapor testi).
    /// </summary>
    private Yuk Yukle(TakipHesapBaglami takip, DateOnly? raporBitis = null)
    {
        var bugun = takip.Bugun; var ct = takip.Iptal;
        // Tutarlı okuma: alış onayı/ödeme eşleştirmesi rapor okunurken yarım görünmez; yazanlar beklemez.
        using var snapshot = _db.OkumaBaslat();
        ct.ThrowIfCancellationRequested();
        var karantina = new List<KarantinaKaydi>();
        void Karantinaya(string anahtar, string aciklama, DateOnly ilk, DateOnly? son, Exception? hata = null)
        {
            if (karantina.All(k => k.Anahtar != anahtar)) karantina.Add(new(anahtar, aciklama, ilk, son, hata));
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
        var eslemeler = alislar.SelectMany(a => a.Odemeler.Select(o => (o.IslemId, Alis: a)))
            .ToDictionary(o => o.IslemId, o => o.Alis);
        // Ödeme dağılımı hesaplanamayan (ör. ödemeleri kalem dağılımını aşan) alışın ödemeleri karantinaya alınır.
        var dagilimlar = new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<AlisKanalPayi>>>();
        var alisHatalari = new Dictionary<int, Exception>();
        foreach (var a in alislar)
            try { dagilimlar[a.Id] = AlisHesaplari.OdemeDagilimlari(a); }
            catch (Exception e) when (VeriKarantinasi.VeriHatasiMi(e)) { alisHatalari[a.Id] = e; }
        var kanalAdlari = _db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
        bool KanalAdi(int? kanalId, [NotNullWhen(true)] out string? ad)
        {
            ad = null;
            return kanalId is { } id && kanalAdlari.TryGetValue(id, out ad);
        }
        var aylikOdemeler = _db.AylikGiderOdemeler.AsNoTracking().Where(p => !p.Iptal && p.IslemId != null).ToDictionary(p => p.IslemId!.Value);
        var aylikRevizyonlar = _db.AylikGiderRevizyonlar.AsNoTracking().ToDictionary(r => r.Id);
        var imported = _db.EkstreKayitlar.AsNoTracking().Where(k => !k.Iptal).ToList();
        var importedExpenses = imported.Where(k => k.IslemId != null).ToDictionary(k => k.IslemId!.Value);
        var dbIslemler = new List<Islem>();
        // Rapora satır veren her kaydın tarihi (birden çok kanala bölünen kayıt bir kez): dönem ufkunu belirler.
        var kayitTarihleri = new List<DateOnly>();
        var sayac = 0;
        foreach (var kayit in kayitlar)
        {
            if (++sayac % 256 == 0) ct.ThrowIfCancellationRequested();
            var onceki = dbIslemler.Count;
            KayitSatirlari(kayit);
            if (dbIslemler.Count > onceki) kayitTarihleri.Add(kayit.Tarih);
        }
        void KayitSatirlari(IslemEntity kayit)
        {
            // Kaydın bütün satırları (kanal payları) aynı kaynak anahtarını taşır: K1 adedi kayıt düzeyinde sayılır.
            var kaynak = "Islem:" + kayit.Id;
            // Kanalı çözülemeyen tutar: kasadan düşer, hiçbir kanala yazılmaz.
            void Bekliyor(Islem satir, decimal tutar, string anahtar, string aciklama, Exception? hata = null)
            {
                dbIslemler.Add(satir with { Kanal = Kanallar.DagilimBekliyor, TutarTl = tutar, DagilimBekliyor = true });
                Karantinaya(anahtar, aciklama, satir.Tarih, EtkiSonu(satir), hata);
            }
            if (importedExpenses.TryGetValue(kayit.Id, out var importedExpense))
            {
                var source = kayit.ToCore() with { Kaynak = kaynak };
                var anahtar = "EkstreKaydi:" + importedExpense.Id;
                string Aciklama(string sorun) => $"Ekstre kaydı #{importedExpense.Id} (gider, {Gun(kayit.Tarih)}): {sorun}; tutar 'Dağılım bekliyor' sayıldı";
                if (importedExpense.DagilimTuru == "Genel") dbIslemler.Add(source with { YalnizGenelKasa = true });
                else if (VeriKarantinasi.Oku<TakipKanalPayi>(importedExpense.DagilimJson) is not { } paylar)
                    Bekliyor(source, source.TutarTl, anahtar, Aciklama("dağılımı okunamadı"));
                else foreach (var share in paylar)
                    if (KanalAdi(share.KanalId, out var ad)) dbIslemler.Add(source with { Kanal = ad, TutarTl = share.Tutar });
                    else Bekliyor(source, share.Tutar, anahtar, Aciklama(PaySorunu(share.KanalId)));
                return;
            }
            if (aylikOdemeler.TryGetValue(kayit.Id, out var aylikOdeme))
            {
                var source = kayit.ToCore() with { AylikGider = true, Kaynak = kaynak };
                var anahtar = "AylikGiderOdemesi:" + aylikOdeme.Id;
                string Aciklama(string sorun) => $"Aylık gider ödemesi #{aylikOdeme.Id} ({Gun(kayit.Tarih)}): {sorun}; tutar 'Dağılım bekliyor' sayıldı";
                if (!aylikRevizyonlar.TryGetValue(aylikOdeme.RevizyonId, out var revision))
                    Bekliyor(source, source.TutarTl, anahtar, Aciklama($"gider tanımı (revizyon #{aylikOdeme.RevizyonId}) bulunamadı"));
                else if (revision.DagilimTuru == "Genel") dbIslemler.Add(source with { YalnizGenelKasa = true });
                else if (VeriKarantinasi.Oku<KanalPayYaz>(revision.DagilimJson) is not { } paylar)
                    Bekliyor(source, source.TutarTl, anahtar, Aciklama($"dağılımı (revizyon #{revision.Id}) okunamadı"));
                else foreach (var share in paylar)
                    if (KanalAdi(share.KanalId, out var ad)) dbIslemler.Add(source with { Kanal = ad, TutarTl = share.Tutar });
                    else Bekliyor(source, share.Tutar, anahtar, Aciklama(PaySorunu(share.KanalId)));
                return;
            }
            if (kayit.KrediKartiId is { } cardId && kartTakip.TryGetValue(cardId, out var tracking))
            {
                // Başlangıçtan sonraki kart giderleri takip harcaması olarak izlenir (Sync).
                if (!tracking.EskiKayit || kayit.Tarih >= tracking.Baslangic) return;
                // İşlem tarihi kuralı: başlangıçtan önceki her eski gider eski ay sonu kuralıyla bir
                // kez düşer. Etki tarihi kuralı (ilk sürüm geçişleri) raporları korumak için aynen
                // sürer: eski etkisi başlangıçta/sonrasında olan gider atlanır. Atlanan tutar kart
                // ekranında ve açılış logunda görünür (KartGecisHesabi.IlkSurumKalintisi, aynı koşul).
                if (KartGecisHesabi.IlkSurumdeAtlanir(tracking, kayit.Tarih)) return;
            }
            var islem = kayit.ToCore() with { Kaynak = kaynak };
            if (!eslemeler.TryGetValue(kayit.Id, out var alis))
                dbIslemler.Add(islem);
            else if (alis.Durum != "Onaylandi")
                dbIslemler.Add(islem with { Kanal = Kanallar.DagilimBekliyor, DagilimBekliyor = true });
            else
            {
                var anahtar = "Alis:" + alis.Id;
                string Aciklama(string sorun) => $"Alış #{alis.Id} ödemesi (gider #{kayit.Id}, {Gun(kayit.Tarih)}): {sorun}; tutar 'Dağılım bekliyor' sayıldı";
                if (!dagilimlar.TryGetValue(alis.Id, out var odemeler) || !odemeler.TryGetValue(kayit.Id, out var paylar))
                    Bekliyor(islem, islem.TutarTl, anahtar, Aciklama("kanal dağılımı hesaplanamadı (ödemeler ile kalem dağılımı tutarsız)"), alisHatalari.GetValueOrDefault(alis.Id));
                else foreach (var pay in paylar.Where(p => p.Tutar > 0))
                    if (KanalAdi(pay.KanalId, out var ad)) dbIslemler.Add(islem with { Kanal = ad, TutarTl = pay.Tutar });
                    else Bekliyor(islem, pay.Tutar, anahtar, Aciklama(PaySorunu(pay.KanalId)));
            }
        }
        ct.ThrowIfCancellationRequested();
        var dbGelenler = _db.Gelenler.AsNoTracking().Include(g => g.KanalKaydi).ToList().Select(e => e.ToCore()).ToList();
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
        var ufukTarihleri = kayitTarihleri.Concat(ekGelirTarihleri).Concat(imported.Where(k => k.IslemTuru == "Gelir").Select(k => k.Tarih)).ToList();
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
            try { KrediTuretici.Dogrula(k.ToCore()); }
            catch (ArgumentException e)
            {
                gecersizKrediler.Add(k.Id);
                Karantinaya("KrediPlani:" + k.Id, $"Kredi #{k.Id} ('{k.Ad}', çekim {Gun(k.CekimTarihi)}): taksit planı geçersiz (ödeme günü, taksit sayısı ya da tutar); türetilen çekim ve taksitler rapora alınmadı",
                    k.CekimTarihi, null, e);
            }
        var taksitler = krediKayitlari.Where(k => !k.GerceklesmeTakibi && Turetilir(k) && !gecersizKrediler.Contains(k.Id)).SelectMany(k =>
            KrediTuretici.TaksitGiderleri(k.ToCore()).Where(t => !krediTakip.TryGetValue(k.Id, out var tracking) || (tracking.EskiKayit && t.Tarih < tracking.Baslangic))
                .Select(t => t with { Kaynak = "Kredi:" + k.Id }));
        var islemler = dbIslemler.Concat(taksitler).ToList();
        var cekimGelenleri = krediKayitlari.Where(k => Turetilir(k) && !gecersizKrediler.Contains(k.Id))
            .Select(k => KrediTuretici.CekimGeleni(k.ToCore(), donemler))
            .Where(g => g is not null)
            .Select(g => g!);
        var ekGelirler = new List<Gelen>();
        foreach (var hareket in ekGelirHareketleri)
            if (donemler.FirstOrDefault(d => d.Icerir(hareket.Tarih)) is { } period)
            {
                if (hareket.Kanal is { } kanal) ekGelirler.Add(new(period.Start, kanal.Ad, hareket.Tutar));
                else
                {
                    // Kanalı olmayan gelir kasaya girer, hiçbir kanala yazılmaz.
                    ekGelirler.Add(new(period.Start, "Genel kasa", hareket.Tutar, GenelGelir: true));
                    Karantinaya("EkGelir:" + hareket.Id, $"Ek gelir #{hareket.Id} ({Gun(hareket.Tarih)}): kanalı yok; tutar genel kasaya gelir yazıldı", period.Start, hareket.Tarih);
                }
            }
        var gelenler = dbGelenler.Concat(cekimGelenleri).Concat(ekGelirler).ToList();
        foreach (var income in imported.Where(k => k.IslemTuru == "Gelir"))
            if (donemler.FirstOrDefault(d => d.Icerir(income.Tarih)) is { } period)
            {
                // Kanalı çözülemeyen gelir kasaya girer, hiçbir kanala yazılmaz.
                void Genel(decimal tutar, string sorun)
                {
                    gelenler.Add(new(period.Start, "Genel kasa", tutar, GenelGelir: true));
                    Karantinaya("EkstreKaydi:" + income.Id, $"Ekstre kaydı #{income.Id} (gelir, {Gun(income.Tarih)}): {sorun}; tutar genel kasaya gelir yazıldı", period.Start, income.Tarih);
                }
                if (income.DagilimTuru == "Genel") gelenler.Add(new(period.Start, "Genel kasa", income.Tutar, GenelGelir: true));
                else if (VeriKarantinasi.Oku<TakipKanalPayi>(income.DagilimJson) is not { } paylar) Genel(income.Tutar, "dağılımı okunamadı");
                else foreach (var share in paylar)
                    if (KanalAdi(share.KanalId, out var ad)) gelenler.Add(new(period.Start, ad, share.Tutar));
                    else Genel(share.Tutar, PaySorunu(share.KanalId));
            }
        // Takipli kredilerin taksitleri tek sorguda okunur (kredi başına sorgu yok).
        var takipliKrediler = krediKayitlari.Where(k => krediTakip.ContainsKey(k.Id)).Select(k => k.Id).ToArray();
        var takipliTaksitler = _db.TakipKrediTaksitler.AsNoTracking().Where(t => !t.Iptal && takipliKrediler.Contains(t.KrediId)).ToList().ToLookup(t => t.KrediId);
        foreach (var loan in krediKayitlari.Where(k => krediTakip.ContainsKey(k.Id)))
        {
            var tracking = krediTakip[loan.Id];
            if (!tracking.MevcutKredi && donemler.FirstOrDefault(d => d.Icerir(loan.CekimTarihi)) is { } period)
            {
                // Kanalı çözülemeyen çekim payı eski modelin kanalsız kredi girişidir: kasaya girer, kanala ve ay sonucuna girmez.
                void Kanalsiz(decimal tutar, string sorun)
                {
                    gelenler.Add(new(period.Start, KrediTuretici.KrediKanal, tutar));
                    Karantinaya("KrediCekimi:" + loan.Id, $"Kredi #{loan.Id} ('{loan.Ad}') çekimi ({Gun(loan.CekimTarihi)}): {sorun}; tutar kanalsız kredi girişi olarak genel kasaya yazıldı",
                        period.Start, loan.CekimTarihi);
                }
                if (VeriKarantinasi.Oku<KanalPayYaz>(tracking.CekimPaylariJson) is not { } paylar) Kanalsiz(loan.CekilenTutar, "kanal payları okunamadı");
                else foreach (var share in paylar)
                    if (KanalAdi(share.KanalId, out var ad)) gelenler.Add(new(period.Start, ad, share.Tutar, KrediGirisi: true));
                    else Kanalsiz(share.Tutar, PaySorunu(share.KanalId));
            }
            foreach (var installment in takipliTaksitler[loan.Id])
            {
                var satir = new Islem(installment.Tarih, loan.Ad + " / " + installment.No + ". taksit", installment.Tutar, Kanallar.DagilimBekliyor, GiderTipi.Cari) { Kaynak = "KrediTaksiti:" + installment.Id };
                // Kanalı çözülemeyen taksit tutarı kasadan düşer, hiçbir kanala yazılmaz.
                void Bekliyor(decimal tutar, string sorun)
                {
                    islemler.Add(satir with { TutarTl = tutar, DagilimBekliyor = true });
                    Karantinaya("KrediTaksiti:" + installment.Id, $"Kredi taksiti #{installment.Id} ('{loan.Ad}' {installment.No}. taksit, {Gun(installment.Tarih)}): {sorun}; tutar 'Dağılım bekliyor' sayıldı",
                        installment.Tarih, installment.Tarih);
                }
                if (VeriKarantinasi.Oku<KanalPayYaz>(installment.DagilimJson) is not { } paylar) Bekliyor(installment.Tutar, "dağılımı okunamadı");
                else foreach (var share in paylar)
                    if (KanalAdi(share.KanalId, out var ad)) islemler.Add(satir with { TutarTl = share.Tutar, Kanal = ad });
                    else Bekliyor(share.Tutar, PaySorunu(share.KanalId));
            }
        }
        // Takipli kartın yalnız ödeme kanal payları gerekir: tam kart DTO'su (ekstre, harcama, kalan borç) hesaplanmaz.
        foreach (var cardId in kartTakip.Keys)
        {
            IReadOnlyList<(DateOnly Tarih, string? Not, IReadOnlyList<TakipKanalPayi> Dagilimlar)> odemeler;
            try { odemeler = FinansTakipServisi.KartOdemeDagilimlari(takip, cardId).ToList(); }
            catch (Exception e) when (VeriKarantinasi.VeriHatasiMi(e))
            {
                KartOdemeleriBekliyor(cardId, e);
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
                            $"Kredi kartı #{cardId} ({KartAdi(cardId)}) {Gun(payment.Tarih)} tarihli ödemesi: {PaySorunu(kanalId)}; pay 'Dağılım bekliyor' sayıldı", payment.Tarih, payment.Tarih);
                    }
                    islemler.Add(new(payment.Tarih, "Kart ödemesi", share.Tutar, bekliyor ? Kanallar.DagilimBekliyor : share.Kanal, GiderTipi.KrediKarti, payment.Not,
                        DagilimBekliyor: bekliyor, NakitKartOdemesi: true) { Kaynak = kaynak });
                }
            }
        }
        string KartAdi(int cardId) => _db.KrediKartlari.AsNoTracking().Where(k => k.Id == cardId).Select(k => k.Ad).FirstOrDefault() is { } ad ? $"'{ad}'" : "adı yok";
        // Kart hesabı (ödeme payları, harcama/iade/alış bağları) okunamadı: ödemenin hangi harcamayı ne kadar kapattığı ve
        // önceden kasada sayılan tutar bilinmez. İptal edilmemiş her ödeme tam tutarıyla nakit çıkışıdır ("Dağılım bekliyor").
        void KartOdemeleriBekliyor(int cardId, Exception hata)
        {
            var aktif = _db.TakipKartOdemeler.AsNoTracking().Where(p => p.KrediKartiId == cardId && !p.Iptal).OrderBy(p => p.Id).ToList();
            foreach (var p in aktif)
                islemler.Add(new(p.Tarih, "Kart ödemesi", p.Tutar, Kanallar.DagilimBekliyor, GiderTipi.KrediKarti, p.Not,
                    DagilimBekliyor: true, NakitKartOdemesi: true) { Kaynak = "KartOdemesi:" + cardId + ":" + p.Id });
            var bozuk = aktif.Where(p => VeriKarantinasi.Oku<KartTaksitPayi>(p.PaylarJson) is null).Select(p => "#" + p.Id).ToList();
            Karantinaya("KrediKarti:" + cardId, $"Kredi kartı #{cardId} ({KartAdi(cardId)}): ödemeleri hesaplanamadı"
                + (bozuk.Count > 0 ? $" (payları okunamayan ödeme: {string.Join(", ", bozuk)})" : " (kart kayıtları okunamadı)")
                + $"; {aktif.Count} ödeme tam tutarıyla 'Dağılım bekliyor' sayıldı",
                aktif.Count > 0 ? aktif.Min(p => p.Tarih) : bugun, aktif.Count > 0 ? aktif.Max(p => p.Tarih) : bugun, hata);
        }

        foreach (var k in karantina) VeriKarantinasi.Logla(_db, k.Anahtar, k.Aciklama, k.Hata);
        return new Yuk(kanallar, islemler, gelenler, donemler, ayar.KasaAcilisDevri,
            kanalAdlari.ToDictionary(k => k.Value, k => (int?)k.Key), ufukUyarisi, karantina);
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
        if (uyarilar.Count == 0 || sonuc.Count == 0) return sonuc;
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
    public AylikRapor Aylik(int yil, int ay, CancellationToken ct = default, int? kuralSurumu = null)
    {
        var y = Yukle(new TakipHesapBaglami(_db, ct), new DateOnly(yil, ay, DateTime.DaysInMonth(yil, ay)));
        var rapor = HesapMotoru.AylikHesapla(yil, ay, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler, kuralSurumu ?? AcikAyKurali);
        var uyarilar = new[] { HesapMotoru.BaslangicOncesiUyarisi(HesapMotoru.BaslangicOncesi(y.Islemler, y.Donemler, (yil, ay)), aylik: true),
                KarantinaUyarisi(y.Karantina.Where(k => k.AyaDokunur(yil, ay)).ToList()) }
            .OfType<string>().ToList();
        return uyarilar.Count > 0 ? rapor with { VeriSagligiUyarisi = string.Join(" ", uyarilar) } : rapor;
    }

    /// <summary>API'nin aylık raporu: kilitli ay dondurulmuş görüntüsünden (<c>"dondurulmus": true</c>), açık ay canlı
    /// hesaplanır.</summary>
    public object AylikYanit(int yil, int ay, CancellationToken ct = default)
    {
        using (_db.OkumaBaslat())
            if (AyRaporAnlikGoruntusu.Oku(_db, yil, ay) is { } dondurulmus) return dondurulmus;
        return Aylik(yil, ay, ct);
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

        var buAyRapor = HesapMotoru.AylikHesapla(bugun.Year, bugun.Month, y.Kanallar, y.Islemler, y.Gelenler, y.Donemler, AcikAyKurali);
        var buAy = buAyRapor.Kanallar.Sum(k => k.AySonucu) - buAyRapor.DagilimBekleyenTutar - buAyRapor.GenelGider + buAyRapor.GenelGelir;

        return (new PanelDto(guncelKasa, kanalBakiyeleri, buHafta, buAy,
            haftalik.Sum(h => h.DagilimBekleyenTutar)), KarantinaUyarisi(y.Karantina));
    }
}

/// <summary>
/// Rapor yolunun veri karantinası (gap-veri-degismezleri-patlama-yaricapi-3). Rapora satır veren tek bir kaydın kendi verisindeki
/// sorun (bilinmeyen kanal kimliği, okunamayan dağılım/pay JSON'u, eksik kaynak, türetilemeyen plan) bütün raporu düşürmez: kayıt
/// karantinaya alınır, raporda veri sağlığı uyarısıyla görünür ve kayıt anahtarıyla uygulama başına bir kez Warning loglanır.
/// Hata politikası listeleme yolununkiyle (<c>FinansTakipServisi.Adlandir</c>: silinmiş kanal adı) aynı yöndedir: kayıt
/// gösterilir, sorun gizlenmez. Veritabanı, iptal ve kod hataları karantinaya alınmaz; olduğu gibi yükselir.
/// </summary>
internal static class VeriKarantinasi
{
    public const string LogKategorisi = "Kasa.Rapor";

    /// <summary>Kaydın kendi verisinden doğan hata mı: JSON, eksik sözlük anahtarı, boş zorunlu değer, geçersiz tutar/plan.
    /// Veritabanı hatası (SqliteException), iptal ve atılmış bağlam bu sınıfa girmez.</summary>
    public static bool VeriHatasiMi(Exception e) => e is JsonException or KeyNotFoundException or ArgumentException or FormatException
        or OverflowException or NullReferenceException or IndexOutOfRangeException
        || (e is InvalidOperationException && e is not ObjectDisposedException);

    /// <summary>Kaydın JSON listesi (dağılım, pay); okunamazsa null: çağıran kaydı karantinaya alır.</summary>
    public static List<T>? Oku<T>(string? json)
    {
        if (json is null) return null;
        try { return FinansTakipServisi.Read<T>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Karantina kaydını <paramref name="anahtar"/> (kayıt türü ve kimliği) ile uygulama başına bir kez Warning olarak yazar:
    /// aynı bozuk kayıt her rapor isteğinde ve dakikalık bildirim işçisinde yeniden görülür, log taşmaz.</summary>
    public static void Logla(KasaDbContext db, string anahtar, string aciklama, Exception? hata = null)
    {
        if (!IlkKezMi(db, anahtar)) return;
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
        if (gorulen.Count >= GorulenSiniri) gorulen.Clear();
        return gorulen.TryAdd(anahtar, 0);
    }
}
