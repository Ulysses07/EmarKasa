using System.Data;
using System.Globalization;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Denetim;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kasa.Api.Auth;

/// <summary>
/// Yedekten geri yüklenmiş veritabanının ilk açılışta işlenmesi (gap-geri-yukleme-durum-geri-sarma-1, -2, -6).
/// <para>Geri yükleme veritabanındaki bütün durumu yedek anına sarar: editör şifresi ve kurtarma kodu, izleyici şifresi, alıcı
/// hesaplarının etkinliği ve şifreleri, oturum iptalleri, bildirim abonelikleri, AUTOINCREMENT sayaçları ve kayıt sürümleri.
/// Yedekten sonra yapılmış bir güvenlik kararı (ör. çalınan cihaz yüzünden editör şifresinin değiştirilmesi) veritabanında
/// atılan soydadır; bu yüzden işlem koşulsuz sıkılaştırır ve yedekten sonraki kararları veritabanı dışındaki güvenlik
/// günlüğünden (<see cref="GuvenlikGunlugu"/>) okuyup yalnız sıkılaştırıcı olanları yeniden uygular.</para>
/// <para>Tanıma: uygulamanın aldığı her yedek kopyası (<see cref="Servisler.YedekServisi"/>) ve restore_backup.py'nin geri
/// açtığı her dosya SQLite başlığında <see cref="Isaret"/> taşır (<c>PRAGMA user_version</c>; canlı dosyada 0). Yedek anı
/// rapor için uygulamanın kopyasında <see cref="SistemDurumuEntity.YedekZamani"/>'dadır; günlüğün kesin kesim noktası
/// <see cref="GuvenlikKesimiTablosu"/> tablosundadır. restore_backup.py eski yedeklerde manifestteki anı
/// <see cref="IsaretTablosu"/> tablosuna yazar. Kesim noktası olmayan eski yedeklerde kimlik erişimi güvenli yönde kilitlenir. Program.cs
/// başlatıcıdan (migration) sonra, HTTP sunucusu açılmadan çağırır; işaretli dosyada tek transaction'da:</para>
/// <list type="bullet">
/// <item>Bütün oturumlar kapanır, Kasa:JwtKey'e dokunulmaz: yeni rastgele oturum dönemi açılır
/// (<see cref="SistemDurumuEntity.OturumDonemi"/>; damgaya girer, <see cref="OturumDamgasi"/>); ayrıca editör güvenlik kaydının
/// sürümü (kayıt yoksa sürüm 1 ile açılır) ve her alıcının oturum sürümü artar, izleyici şifresi silinir. Yedekten önce ya da
/// sonra, hatta aynı yedeğin önceki bir geri yüklemesinden sonra alınmış bütün oturum belirteçleri, tanıdık cihaz belirteçleri
/// ve bildirim aboneliği damgaları geçersiz olur. Açık kalan masaüstü/web ekranı eski soydaki (Id, Surum) çiftiyle ya da bekleyen
/// tekrar anahtarıyla yazamaz: 401 alır, istemci oturumu kapatıp ekran durumunu atar.</item>
/// <item>Kurtarma kodu iptal edilir (yedekteki kod yeniden geçerli olmaz; editör yenisini üretir). Bütün bildirim abonelikleri
/// kapatılır (kaldırılmış kayıp cihaz dirilmez; cihazlar bildirimleri yeniden açar).</item>
/// <item>İzleyici girişi kapanır: editör yeni izleyici şifresi belirleyene kadar izleyici giremez; yedekteki eski şifre de
/// yedekten sonra belirlenen de geçersizdir (izleyici bütün finans verisini okur).</item>
/// <item>Güvenlik günlüğünden, yedek kesiminden SONRAKİ olaylar yeniden uygulanır (yalnız sıkılaştırma; yeniden etkinleştirme gibi
/// gevşetici olay uygulanmaz): editör şifresi değiştirildiyse, kurtarma kullanıldıysa ya da operatör şifreyi sıfırladıysa yedekteki
/// şifre geçersiz kılınır ve editör girişi kilitlenir (<see cref="EditorGuvenligi.GirisKilidi"/>): ne yedekteki ya da sonradan
/// kaybolan şifre ne de ortamdaki Kasa:EditorSifre geçer (ilk kurulumun ya da unutulmuş eski bir ortam şifresinin kendiliğinden
/// geçerli olmaması için). Kilidi yalnız operatörün bilinçli sıfırlaması açar: yeni Kasa:EditorSifre ve Kasa:EditorSifreSifirla
/// (<see cref="EditorSifreSifirlama"/>, bu işlemden hemen sonra aynı açılışta çalışır). Şifresi ya da kullanıcı adı değiştirilen veya son olayı pasife alma olan alıcı
/// (kimliği ve adı eşleşirse) pasif bırakılır ve eski şifresi geçersiz kılınır; yedekten sonra açılıp kaybolan alıcılar rapora yazılır. Günlük yoksa veya kesin
/// kesim doğrulanamıyorsa editör girişi kilitlenir, yedekteki bütün alıcılar pasifleştirilir ve şifreleri geçersiz kılınır.</item>
/// <item>Kimlikler ileri alınır: AUTOINCREMENT'li her tablonun sayacı MAX(sayaç, en yüksek kimlik) + <see cref="KimlikAraligi"/>
/// olur (sayaç satırı olmayan tabloya satır eklenir). Atılan soyda açılmış kayıtların kimlikleri yeni kayda verilmez; eski
/// ekranın ya da tekrarın taşıdığı kimlik başka kayda ulaşamaz (404). Yedek anında var olan kayıtların sürümü de geri sarıldığı
/// için eski sürümle gelen yazma 409 alır. Kimlikler 32 bittir; her geri yükleme 1.000.000 tüketir (~2.000 geri yükleme).</item>
/// <item>İşaretler silinir, son geri yüklemenin anı ve Türkçe raporu <see cref="SistemDurumuEntity"/>'ye yazılır (/api/yedek/durum,
/// web Araçlar ve masaüstü Güvenlik ekranı); 'Oturum ve güvenlik' izine <see cref="OlayTuru"/> olayı, güvenlik günlüğüne de
/// aynı olay yazılır, her rapor maddesi Kasa.Guvenlik loguna uyarı olarak düşer. Sonraki açılışlarda yeniden çalışmaz; hata
/// olursa hiçbir şey değişmez ve açılış durur. Kasa kayıtlarına ve raporlara dokunulmaz.</item>
/// </list>
/// <para>İşaretsiz yedek (bu sürümden önce alınmış) restore_backup.py dışında, ZIP'ten elle çıkarılarak canlıya alınırsa
/// tanınmaz; runbook ("Geri yüklemeden sonra") aracı zorunlu tutar.</para>
/// </summary>
public static class GeriYuklemeIsleyici
{
    /// <summary>Yedek kopyasının başlığındaki işaret (<c>PRAGMA user_version</c>, ASCII "KSGY"). restore_backup.py'deki
    /// GERI_YUKLEME_ISARETI ile aynıdır.</summary>
    public const int Isaret = 0x4B534759;
    /// <summary>Geri yükleme aracının yedek anını yazdığı tablo (restore_backup.py'deki GERI_YUKLEME_TABLOSU ile aynı); işlenince düşürülür.</summary>
    public const string IsaretTablosu = "__KasaGeriYukleme";
    /// <summary>Yalnız yedek kopyasında bulunan güvenlik günlüğü bayt/özet kesimi; işlemden sonra kaldırılır.</summary>
    public const string GuvenlikKesimiTablosu = "__KasaYedekGuvenlikKesimi";
    /// <summary>Geri yüklemede sayaçların ileri alındığı pay (restore_backup.py'deki KIMLIK_ARALIGI ile aynı).</summary>
    public const int KimlikAraligi = 1_000_000;
    public const string OlayTuru = GuvenlikOlaylari.GeriYuklemeIslendi;
    /// <summary>Geri yüklemede geçersiz kılınan alıcı parolası; yeni parola verilmeden hesap etkinleştirilemez.</summary>
    public const string AliciSifreKilidi = "kilitli:geri-yukleme-alici";

    /// <summary>Editör girişinin kilitlendiğini bildiren rapor maddesi (web ve masaüstü aynen gösterir; açılışta loglanır).</summary>
    public const string EditorGirisiKilitlendi = "Editör şifresi yedekten sonra değiştirilmişti: yedekteki eski şifre geçersiz kılındı ve editör girişi "
        + "kilitlendi (sunucudaki KASA_EDITOR_SIFRE de geçmez). Sunucu operatörü sıfırlamalı: deploy/.env'de KASA_EDITOR_SIFRE'yi yeni, en az 12 "
        + "karakterlik bir değere çevirip KASA_EDITOR_SIFRE_SIFIRLA=true ile uygulamayı yeniden başlatır (Kasa:EditorSifreSifirla). Editör bu "
        + "şifreyle girip şifresini hemen değiştirmeli; ardından bayrak kaldırılıp uygulama yeniden başlatılmalı.";
    public const string EditorGirisiKilidiEksikGunluk = "Güvenlik günlüğünün yedek anından beri tam olduğu doğrulanamadığı için yedekteki editör şifresinin güvenliği doğrulanamadı: "
        + "editör girişi kilitlendi (sunucudaki KASA_EDITOR_SIFRE de geçmez). Sunucu operatörü yeni, en az 12 karakterlik KASA_EDITOR_SIFRE "
        + "ve KASA_EDITOR_SIFRE_SIFIRLA=true ile uygulamayı yeniden başlatmalı; editör bu şifreyle girip hemen kendi şifresini değiştirmeli.";

    /// <param name="Soy">Geri yüklemeyle başlayan veri soyunun kimliği (olayın VarlikId'si; oturum dönemi onun 32 haneli biçimi).</param>
    /// <param name="Sayaclar">Tablo → ileri alınmış sayaç.</param>
    /// <param name="Rapor">Operatör ve editör için Türkçe maddeler (SistemDurumu.GeriYuklemeRaporu).</param>
    public sealed record Sonuc(Guid Soy, IReadOnlyDictionary<string, long> Sayaclar, bool IzleyiciErisimiKapatildi, int AliciOturumlariKapatildi,
        bool EditorGirisiKilitlendi, IReadOnlyList<int> PasifAlicilar, int BildirimKayitlariKapatildi, IReadOnlyList<string> Rapor);

    /// <summary>Veritabanı geri yükleme işaretliyse işler ve sonucu döner; değilse hiçbir şeye dokunmaz (null).</summary>
    /// <param name="gunluk">Yedekten sonraki kararların okunduğu güvenlik günlüğü; verilmezse günlük yok sayılır.</param>
    public static Sonuc? Isle(KasaDbContext db, GuvenlikGunlugu? gunluk = null)
    {
        var baglanti = (SqliteConnection)db.Database.GetDbConnection();
        var acildi = baglanti.State != ConnectionState.Open;
        if (acildi)
            db.Database.OpenConnection();
        try
        {
            // Olağan açılışta yazma kilidi alınmaz; aynı dosyayla aynı anda açılan iki süreçte işareti transaction içinde yeniden okur.
            if (!Isaretli(baglanti, null))
                return null;
            // Günlük veritabanından bağımsız bir dosyadır; seri kilit, kesim okuma ve işlemi birlikte korur.
            using var seriKilit = gunluk?.IslemKilidiAl();
            using var tx = db.Database.BeginTransaction();
            var sqliteTx = (SqliteTransaction)tx.GetDbTransaction();
            if (!Isaretli(baglanti, sqliteTx))
            { tx.Commit(); return null; }

            var simdi = db.Saati().GetUtcNow();
            var sonOlay = Deger(baglanti, sqliteTx, "SELECT MAX(\"ZamanUtc\") FROM \"DenetimOlaylari\";");
            var durum = db.SistemDurumu.SingleOrDefault(s => s.Id == 1);
            if (durum is null)
                db.SistemDurumu.Add(durum = new SistemDurumuEntity());
            var (yedekAni, anKaynagi) = YedekAni(baglanti, sqliteTx, durum);
            var gunlukKesimi = GuvenlikKesimiOku(baglanti, sqliteTx);
            // Bozuk canlı günlükte açılış durur. Kesim uyuşmazlığı ise geri yüklemeyi güvenli yönde kilitleyerek tamamlar.
            var icerik = gunluk?.Oku();
            var sayaclar = KimlikleriIlerlet(baglanti, sqliteTx);
            var rapor = new List<string>
            {
                yedekAni is { } an
                    ? $"Veritabanı {Yerel(an)} tarihli yedekten geri yüklendi. Bu andan sonra girilen kayıtlar yedekte yok; yeniden girilmeli."
                    : "Veritabanı bir yedekten geri yüklendi (yedek anı bilinmiyor). Yedekten sonra girilen kayıtlar yeniden girilmeli.",
                "Bütün oturumlar kapatıldı; herkes yeniden giriş yapmalı.",
            };

            var ayar = db.Ayarlar.FirstOrDefault();
            var izleyiciKapatildi = ayar?.IzleyiciSifreHash is not null;
            if (ayar is not null)
                ayar.IzleyiciSifreHash = null;
            var alicilar = db.Alicilar.ToList();
            foreach (var a in alicilar)
                a.OturumSurumu++;
            var editor = db.EditorGuvenlik.SingleOrDefault(e => e.Id == 1);
            if (editor is null)
                db.EditorGuvenlik.Add(editor = new EditorGuvenlikEntity());
            editor.Surum++;
            var kurtarmaKoduVardi = editor.KurtarmaHash is not null;
            editor.KurtarmaHash = null;

            // Duvar saati geriye alınabilir; olay sırasını yalnız yedek kopyasındaki bayt/özet kesimi belirler.
            // Eski yedeklerde bu bilgi yoktur: tarih süzmesi şifre veya pasife alma kararını atlayabilir.
            var sonrakiler = new List<GuvenlikGunlugu.Olay>();
            string gunlukDurumu;
            string? eksikNedeni = null;
            if (icerik is null)
                gunlukDurumu = "bulunamadi";
            else if (gunlukKesimi is null)
            {
                gunlukDurumu = "eksik";
                eksikNedeni = "Bu yedekte güvenlik günlüğünün kesin sıra bilgisi yok. Saat geri alınmış olabileceğinden yedekten sonraki kimlik kararları doğrulanamadı.";
            }
            else if (gunlukKesimi.Bayt < icerik.DogrulamaBaslangiciBayt)
            {
                gunlukDurumu = "eksik";
                eksikNedeni = "Yedek, güvenlik günlüğünün doğrulanabilen bölümünden önce alınmış. Aradaki kimlik kararları doğrulanamadı.";
            }
            else
            {
                try
                {
                    sonrakiler = gunluk!.KesimdenSonraOku(gunlukKesimi).Olaylar.ToList();
                    gunlukDurumu = "tam";
                }
                catch (InvalidDataException)
                {
                    gunlukDurumu = "eksik";
                    eksikNedeni = "Yedekteki güvenlik günlüğü kesimi mevcut günlükte doğrulanamadı. Aradaki kimlik kararları doğrulanamadı.";
                }
            }
            var kararlarEksik = gunlukDurumu != "tam";
            // Operatörün yedekten sonraki sıfırlaması da bir şifre kararıdır: yedekteki şifre (belki sıfırlamanın nedeni) geri gelmez.
            var editorKilitlendi = kararlarEksik || sonrakiler.Any(o => o.Tur is GuvenlikGunlugu.EditorSifresiDegisti or GuvenlikGunlugu.KurtarmaKullanildi
                or GuvenlikGunlugu.EditorSifresiSifirlandi);
            if (editorKilitlendi)
            { editor.SifreHash = EditorGuvenligi.GirisKilidi; rapor.Add(kararlarEksik ? EditorGirisiKilidiEksikGunluk : EditorGirisiKilitlendi); }
            if (izleyiciKapatildi)
                rapor.Add("İzleyici girişi kapatıldı: Ayarlar'dan yeni bir izleyici şifresi belirleyin; yedekteki eski şifreyi yeniden kullanmayın.");
            rapor.Add(kurtarmaKoduVardi
                ? "Kurtarma kodu iptal edildi (yedekteki kod geçersiz): Güvenlik bölümünden yeni kurtarma kodu üretin."
                : "Kurtarma kodu yok: Güvenlik bölümünden yeni kurtarma kodu üretin.");
            var pasif = kararlarEksik ? AlicilariToptanPasiflestir(alicilar, rapor) : AlicilariSikilastir(alicilar, sonrakiler, rapor);
            db.SaveChanges();

            var bildirim = Calistir(baglanti, sqliteTx, "UPDATE \"PushAbonelikler\" SET \"Etkin\" = 0 WHERE \"Etkin\" = 1;");
            if (bildirim > 0)
                rapor.Add($"{bildirim} cihazın bildirim kaydı kapatıldı: bildirim kullanan cihazlarda bildirimleri yeniden açın.");
            if (gunlukDurumu == "bulunamadi")
                rapor.Add("Güvenlik günlüğü bulunamadı: yedekten sonraki kimlik kararları bilinmiyor. Bu nedenle editör girişi kilitlendi "
                    + "ve yedekteki bütün alıcı hesapları pasifleştirildi. Sunucu operatörü editör şifresini yeni KASA_EDITOR_SIFRE ile "
                    + "sıfırlamalı; editör alıcıları ancak yeni şifre belirleyerek tekrar etkinleştirmeli.");
            else if (gunlukDurumu == "eksik")
                rapor.Add((eksikNedeni ?? "Güvenlik günlüğünün yedekten beri tam olduğu doğrulanamadı.") + " "
                    + "Bu nedenle editör girişi kilitlendi ve yedekteki bütün alıcı hesapları pasifleştirildi. Sunucu operatörü "
                    + "editör şifresini sıfırlamalı; editör alıcıları ancak yeni şifre belirleyerek tekrar etkinleştirmeli.");

            var soy = Guid.NewGuid();
            durum.OturumDonemi = soy.ToString("N");
            durum.YedekZamani = null;
            durum.SonGeriYukleme = simdi;
            durum.GeriYuklemeRaporu = JsonSerializer.Serialize(rapor, DenetimYazici.JsonAyarlari);
            db.SaveChanges();
            Calistir(baglanti, sqliteTx, $"DROP TABLE IF EXISTS \"{IsaretTablosu}\";");
            Calistir(baglanti, sqliteTx, $"DROP TABLE IF EXISTS \"{GuvenlikKesimiTablosu}\";");

            DenetimYazici.Yaz(db, new DenetimOlayi(OlayTuru, GuvenlikOlaylari.Varlik, soy.ToString("D"), null, DenetimYazici.Json(new
            {
                soy,
                kimlikAraligi = KimlikAraligi,
                sayaclar,
                oturumlarKapatildi = true,
                izleyiciErisimiKapatildi = izleyiciKapatildi,
                aliciOturumlariKapatildi = alicilar.Count,
                yedektekiSonOlay = sonOlay is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : (DateTimeOffset?)null,
                yedekAni,
                yedekAniKaynagi = anKaynagi,
                kurtarmaKoduIptal = kurtarmaKoduVardi,
                editorGirisiKilitlendi = editorKilitlendi,
                pasifAlicilar = pasif,
                bildirimKayitlariKapatildi = bildirim,
                guvenlikGunlugu = gunlukDurumu,
            }), Aktor: DenetimAktoru.Sistem, BaslikGerekcesi: false));
            Calistir(baglanti, sqliteTx, "PRAGMA user_version = 0;");
            // Günlük yazılamazsa geri yükleme işareti, oturum iptalleri ve bütün sıkılaştırmalar birlikte geri alınır.
            // Bir sonraki açılış işlemi yeniden deneyebilir; işaret ancak günlük kalıcı olduktan sonra temizlenir.
            gunluk?.Yaz(GuvenlikGunlugu.GeriYuklemeIslendi, ayrinti: new
            {
                yedekAni,
                yedekAniKaynagi = anKaynagi,
                guvenlikGunlugu = gunlukDurumu,
                editorGirisiKilitlendi = editorKilitlendi,
                kurtarmaKoduIptal = kurtarmaKoduVardi,
                pasifAlicilar = pasif.Count,
                bildirimKayitlariKapatildi = bildirim,
            }, zorunlu: true);
            tx.Commit();
            var log = db.GetService<ILoggerFactory>().CreateLogger("Kasa.Guvenlik");
            log.LogWarning(
                "Geri yüklenmiş veritabanı tanındı ve işlendi (veri soyu {Soy}): bütün oturumlar kapatıldı, kurtarma kodu iptal edildi, izleyici girişi {Izleyici}, "
                + "{Sayi} tablonun kayıt numarası {Aralik} ileri alındı, güvenlik günlüğü {Gunluk}. Rapor maddeleri aşağıdadır (runbook: Geri yüklemeden sonra).",
                soy, izleyiciKapatildi ? "kapatıldı" : "zaten kapalıydı", sayaclar.Count, KimlikAraligi, gunlukDurumu);
            foreach (var madde in rapor)
                log.LogWarning("Geri yükleme: {Madde}", madde);
            return new Sonuc(soy, sayaclar, izleyiciKapatildi, alicilar.Count, editorKilitlendi, pasif, bildirim, rapor);
        }
        finally
        {
            if (acildi)
                db.Database.CloseConnection();
        }
    }

    /// <summary>SistemDurumu.GeriYuklemeRaporu'nun maddeleri; yoksa ya da okunamazsa null.</summary>
    public static IReadOnlyList<string>? RaporuOku(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        { return JsonSerializer.Deserialize<List<string>>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Günlük yedek anından beri tam değilse hangi alıcının kimliği değiştiği bilinmez; bütün parolalar geçersiz kılınır.</summary>
    private static List<int> AlicilariToptanPasiflestir(List<AliciEntity> alicilar, List<string> rapor)
    {
        var pasif = new List<int>();
        foreach (var alici in alicilar)
        {
            alici.SifreHash = AliciSifreKilidi;
            if (!alici.Aktif)
                continue;
            alici.Aktif = false;
            pasif.Add(alici.Id);
        }
        if (alicilar.Count > 0)
            rapor.Add($"{pasif.Count} etkin alıcı hesabı güvenlik günlüğü tam olmadığı için pasifleştirildi; {alicilar.Count} alıcının eski şifresi geçersiz kılındı. Her hesabı yeni şifre belirleyerek yeniden etkinleştirin.");
        return pasif;
    }

    /// <summary>
    /// Yedekten sonra şifresi ya da kullanıcı adı değiştirilen veya son olayı pasife alma olan alıcı, veritabanında kimliği ve (önceki
    /// ya da yeni) adı eşleşirse pasif bırakılır: yedekteki şifre ve etkinlik geçersiz kalır, editör gerekirse yeni şifreyle etkinleştirir.
    /// Yeniden etkinleştirme (gevşetme) uygulanmaz. Yedekten sonra açılıp veritabanında olmayan alıcılar yalnız rapora yazılır.
    /// </summary>
    private static List<int> AlicilariSikilastir(List<AliciEntity> alicilar, List<GuvenlikGunlugu.Olay> sonrakiler, List<string> rapor)
    {
        var pasif = new List<int>();
        foreach (var grup in sonrakiler.Where(o => o.Tur == GuvenlikGunlugu.AliciGuncellendi && o.HedefId is not null).GroupBy(o => o.HedefId!.Value))
        {
            var olaylar = grup.ToList();
            var sifre = olaylar.Any(o => o.Mantiksal("sifreDegisti") == true);
            var ad = olaylar.Any(o => o.Metin("oncekiKullanici") is { } onceki && !string.Equals(onceki, o.Kullanici, StringComparison.OrdinalIgnoreCase));
            var pasifeAlindi = olaylar[^1].Mantiksal("aktif") == false;
            if (!(sifre || ad || pasifeAlindi))
                continue;
            var adlar = olaylar.SelectMany(o => new[] { o.Kullanici, o.Metin("oncekiKullanici") }).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (alicilar.FirstOrDefault(a => a.Id == grup.Key && adlar.Contains(a.Kullanici)) is not { } alici)
                continue;
            // Yedekte zaten pasif olsa bile eski hash sonradan yapılan şifre/ad kararından önceye aittir.
            alici.SifreHash = AliciSifreKilidi;
            if (!alici.Aktif)
            {
                rapor.Add($"'{alici.Kullanici}' alıcısı yedekte zaten pasifti; yedekten sonraki kimlik kararı nedeniyle eski şifresi geçersiz kılındı. Yeniden etkinleştirmeden önce yeni şifre belirleyin.");
                continue;
            }
            alici.Aktif = false;
            pasif.Add(alici.Id);
            var nedenler = new List<string>();
            if (pasifeAlindi)
                nedenler.Add("pasife alınmıştı");
            if (sifre)
                nedenler.Add("şifresi değiştirilmişti");
            if (ad)
                nedenler.Add("kullanıcı adı değiştirilmişti");
            rapor.Add($"'{alici.Kullanici}' alıcısı yedekten sonra {string.Join(" ve ", nedenler)}; pasif bırakıldı. Gerekirse alıcı hesaplarından yeni şifreyle etkinleştirin.");
        }
        var mevcut = alicilar.Select(a => a.Kullanici).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var kullanici in sonrakiler.Where(o => o.Tur == GuvenlikGunlugu.AliciOlusturuldu).Select(o => o.Kullanici).OfType<string>()
                     .Distinct(StringComparer.OrdinalIgnoreCase).Where(k => !mevcut.Contains(k)))
            rapor.Add($"'{kullanici}' alıcı hesabı yedekten sonra açılmıştı; geri yüklemede kayboldu. Gerekirse yeniden açın.");
        return pasif;
    }

    /// <summary>Yedek kopyasındaki kesin günlük kesimi; tablo yoksa eski yedektir, bozuksa geçersiz kesim döner.</summary>
    private static GuvenlikGunlugu.KesimNoktasi? GuvenlikKesimiOku(SqliteConnection baglanti, SqliteTransaction tx)
    {
        if (Deger(baglanti, tx, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $ad;", ("$ad", GuvenlikKesimiTablosu)) is null)
            return null;
        try
        {
            using var komut = Komut(baglanti, tx, $"SELECT \"Bayt\", \"Sha256\" FROM \"{GuvenlikKesimiTablosu}\" LIMIT 2;");
            using var okuyucu = komut.ExecuteReader();
            if (!okuyucu.Read() || okuyucu.IsDBNull(0) || okuyucu.IsDBNull(1))
                return new(-1, "");
            var kesim = new GuvenlikGunlugu.KesimNoktasi(okuyucu.GetInt64(0), okuyucu.GetString(1));
            return okuyucu.Read() ? new(-1, "") : kesim;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidCastException)
        {
            return new(-1, "");
        }
    }

    /// <summary>Yedek anı: uygulamanın kopyasındaki <see cref="SistemDurumuEntity.YedekZamani"/>, yoksa restore_backup.py'nin
    /// <see cref="IsaretTablosu"/>'na yazdığı manifest anı; ikisi de yoksa bilinmiyor.</summary>
    private static (DateTimeOffset? An, string Kaynak) YedekAni(SqliteConnection baglanti, SqliteTransaction tx, SistemDurumuEntity durum)
    {
        if (durum.YedekZamani is { } z)
            return (z, "yedek");
        if (Deger(baglanti, tx, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $ad;", ("$ad", IsaretTablosu)) is not null
            && Deger(baglanti, tx, $"SELECT \"YedekZamani\" FROM \"{IsaretTablosu}\" LIMIT 1;") is string metin
            && DateTimeOffset.TryParse(metin, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var an))
            return (an, "restore_backup.py");
        return (null, "bilinmiyor");
    }

    internal static string Yerel(DateTimeOffset an)
        => TimeZoneInfo.ConvertTime(an, KasaSaati.Istanbul).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private static bool Isaretli(SqliteConnection baglanti, SqliteTransaction? tx)
        => Convert.ToInt64(Deger(baglanti, tx, "PRAGMA user_version;"), CultureInfo.InvariantCulture) == Isaret;

    /// <summary>AUTOINCREMENT'li her tabloyu (sqlite_master'daki tanımından) MAX(sayaç, en yüksek rowid) + aralığa taşır.</summary>
    private static SortedDictionary<string, long> KimlikleriIlerlet(SqliteConnection baglanti, SqliteTransaction tx)
    {
        var tablolar = new List<string>();
        using (var komut = Komut(baglanti, tx, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND sql LIKE '%AUTOINCREMENT%' ORDER BY name;"))
        using (var okuyucu = komut.ExecuteReader())
            while (okuyucu.Read())
                tablolar.Add(okuyucu.GetString(0));
        var sonuc = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var tablo in tablolar)
        {
            var enYuksek = Convert.ToInt64(Deger(baglanti, tx,
                $"SELECT MAX(COALESCE((SELECT seq FROM sqlite_sequence WHERE name = $ad), 0), COALESCE((SELECT MAX(rowid) FROM \"{tablo.Replace("\"", "\"\"")}\"), 0));",
                ("$ad", tablo)), CultureInfo.InvariantCulture);
            var yeni = enYuksek + KimlikAraligi;
            if (Calistir(baglanti, tx, "UPDATE sqlite_sequence SET seq = $seq WHERE name = $ad;", ("$seq", yeni), ("$ad", tablo)) == 0)
                Calistir(baglanti, tx, "INSERT INTO sqlite_sequence (name, seq) VALUES ($ad, $seq);", ("$ad", tablo), ("$seq", yeni));
            sonuc[tablo] = yeni;
        }
        return sonuc;
    }

    private static SqliteCommand Komut(SqliteConnection baglanti, SqliteTransaction? tx, string sql, params (string Ad, object Deger)[] parametreler)
    {
        var komut = baglanti.CreateCommand();
        komut.Transaction = tx;
        komut.CommandText = sql;
        foreach (var (ad, deger) in parametreler)
            komut.Parameters.AddWithValue(ad, deger);
        return komut;
    }

    private static object? Deger(SqliteConnection baglanti, SqliteTransaction? tx, string sql, params (string Ad, object Deger)[] parametreler)
    {
        using var komut = Komut(baglanti, tx, sql, parametreler);
        var deger = komut.ExecuteScalar();
        return deger is DBNull ? null : deger;
    }

    private static int Calistir(SqliteConnection baglanti, SqliteTransaction tx, string sql, params (string Ad, object Deger)[] parametreler)
    {
        using var komut = Komut(baglanti, tx, sql, parametreler);
        return komut.ExecuteNonQuery();
    }
}
