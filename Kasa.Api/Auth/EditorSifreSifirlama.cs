using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Api.Denetim;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Kasa.Api.Auth;

/// <summary>
/// Operatörün editör şifresini zorla sıfırlaması (gap-geri-yukleme-durum-geri-sarma-10). Veritabanında editör şifresi varken ortamdaki
/// Kasa:EditorSifre yok sayılır; editör şifresini ve kurtarma kodunu birlikte unuttuysa ya da geri yükleme girişi kilitlediyse
/// (<see cref="EditorGuvenligi.GirisKilidi"/>) sunucu erişimi olan operatörün tek yolu budur:
/// <list type="number">
/// <item><see cref="Ayar"/> (compose'da KASA_EDITOR_SIFRE_SIFIRLA) <c>true</c> ve Kasa:EditorSifre yeni, 12–1024 karakterlik bir değerle
/// uygulama başlatılır. Program.cs bu işlemi başlatıcıdan (migration) ve geri yükleme işleminden (<see cref="GeriYuklemeIsleyici"/>)
/// sonra, HTTP sunucusu açılmadan çağırır. Bayrak açık ama ortam şifresi kurala uymuyorsa ya da editör kullanıcı adı yoksa açılış
/// durur (sıfırlama yarım uygulanmaz).</item>
/// <item>Tek transaction'da: editör şifre özeti ortam şifresinin özeti olur, kurtarma kodu iptal edilir, sürüm artar (bütün editör
/// oturumları ve tanıdık cihaz belirteçleri düşer; giriş kilidi kalkar), sıfırlama izi (<see cref="SistemDurumuEntity.EditorSifirlamaIzi"/>)
/// yazılır ve 'Oturum ve güvenlik' izine <see cref="GuvenlikOlaylari.EditorSifresiSifirlandi"/> olayı (aktör sistem) düşer. Commit'ten
/// önce aynı olay veritabanı dışındaki güvenlik günlüğüne zorunlu olarak yazılır (yedekten geri yüklemede yedekteki şifre yeniden geçerli olmaz) ve
/// Kasa.Guvenlik loguna uyarı düşer. Şifre ve iz hiçbir loga ya da olaya yazılmaz.</item>
/// <item>İz ortam şifresine bağlıdır (HMAC-SHA256, anahtar Kasa:JwtKey): bayrak açık unutulsa da aynı ortam şifresiyle sıfırlama
/// yeniden uygulanmaz, editörün arayüzden sonradan değiştirdiği şifre her açılışta ezilmez (her açılışta "bayrağı kaldırın" uyarısı
/// loglanır). Ortam şifresi değişirse sıfırlama yeniden uygulanır. Kasa:JwtKey değişirse iz eşleşmez; bayrak o sırada açıksa
/// sıfırlama yeniden uygulanır (bu yüzden bayrak işi bitince kaldırılır).</item>
/// </list>
/// Bayrak kapalıyken hiçbir şey değişmez; giriş kilitliyse her açılışta nasıl açılacağı loglanır. Runbook: deploy/README.md
/// "Editör şifresini sıfırlama".
/// </summary>
public static class EditorSifreSifirlama
{
    public const string Ayar = "Kasa:EditorSifreSifirla";

    public enum Sonuc
    {
        /// <summary>Bayrak kapalı: hiçbir şey değişmedi.</summary>
        Kapali,
        /// <summary>Şifre ortamdaki değere sıfırlandı.</summary>
        Uygulandi,
        /// <summary>Bayrak açık ama bu ortam şifresiyle sıfırlama daha önce uygulanmış: hiçbir şey değişmedi.</summary>
        DahaOnceUygulandi,
    }

    private const string KilitUyarisi = "Editör girişi kilitli: geri yükleme yedekten sonraki bir editör şifresi değişikliğini buldu ve yedekteki şifreyi "
        + "geçersiz kıldı (ortamdaki KASA_EDITOR_SIFRE de geçmez). Açmak için deploy/.env'de KASA_EDITOR_SIFRE'yi yeni, en az 12 karakterlik bir "
        + "değere çevirip KASA_EDITOR_SIFRE_SIFIRLA=true ile uygulamayı yeniden başlatın (deploy/README.md: Editör şifresini sıfırlama).";

    /// <summary>Bayrak açıksa sıfırlamayı uygular (bkz. sınıf açıklaması).</summary>
    /// <param name="gunluk">Sıfırlamanın yazıldığı veritabanı dışındaki güvenlik günlüğü; verilmezse yazılmaz.</param>
    /// <exception cref="InvalidOperationException">Bayrak geçersiz, ya da açık ama editör kullanıcı adı/ortam şifresi kurala uymuyor.</exception>
    public static Sonuc Uygula(KasaDbContext db, IConfiguration cfg, GuvenlikGunlugu? gunluk = null)
    {
        var log = db.GetService<ILoggerFactory>().CreateLogger("Kasa.Guvenlik");
        if (!Acik(cfg))
        {
            if (EditorGuvenligi.Kilitli(db.EditorGuvenlik.AsNoTracking().SingleOrDefault(e => e.Id == 1)))
                log.LogWarning(KilitUyarisi);
            return Sonuc.Kapali;
        }
        var kullanici = cfg["Kasa:EditorKullanici"];
        var sifre = cfg["Kasa:EditorSifre"];
        if (string.IsNullOrWhiteSpace(kullanici))
            throw new InvalidOperationException($"{Ayar} için Kasa:EditorKullanici tanımlı olmalıdır.");
        if (!SifreKurallari.Gecerli(sifre))
            throw new InvalidOperationException($"{Ayar} için Kasa:EditorSifre {SifreKurallari.EnAz}–{SifreKurallari.EnCok} karakter olmalıdır.");
        var iz = Iz(cfg, sifre!);

        bool kilitliydi, kurtarmaKoduVardi;
        using var seriKilit = gunluk?.IslemKilidiAl();
        using (var tx = db.Database.BeginTransaction())
        {
            var durum = db.SistemDurumu.SingleOrDefault(s => s.Id == 1);
            if (durum is null)
                db.SistemDurumu.Add(durum = new SistemDurumuEntity());
            var kayit = db.EditorGuvenlik.SingleOrDefault(e => e.Id == 1);
            if (durum.EditorSifirlamaIzi is { } onceki && OturumDamgasi.Esit(onceki, iz))
            {
                tx.Commit();
                if (EditorGuvenligi.Kilitli(kayit))
                    log.LogWarning("Kasa:EditorSifreSifirla açık ama bu ortam şifresiyle sıfırlama bu veritabanında daha önce uygulanmış ve editör girişi "
                        + "sonra yeniden kilitlenmiş (geri yükleme). Aynı ortam şifresi yeniden geçerli kılınmaz: KASA_EDITOR_SIFRE'yi yeni bir değere "
                        + "çevirip yeniden başlatın.");
                else
                    log.LogWarning("Kasa:EditorSifreSifirla hâlâ açık; bu ortam şifresi için sıfırlama daha önce uygulandı, yeniden uygulanmadı. Bayrağı kaldırın.");
                return Sonuc.DahaOnceUygulandi;
            }
            kilitliydi = EditorGuvenligi.Kilitli(kayit);
            if (kayit is null)
                db.EditorGuvenlik.Add(kayit = new EditorGuvenlikEntity());
            kurtarmaKoduVardi = kayit.KurtarmaHash is not null;
            kayit.SifreHash = SifreHasher.Hashle(sifre!);
            kayit.KurtarmaHash = null;
            kayit.Surum++;
            durum.EditorSifirlamaIzi = iz;
            // Geri yüklemenin kilidi kalktıysa son geri yükleme raporu (Araçlar/Güvenlik) bunu da söyler.
            if (kilitliydi && GeriYuklemeIsleyici.RaporuOku(durum.GeriYuklemeRaporu) is { } rapor
                && (rapor.Contains(GeriYuklemeIsleyici.EditorGirisiKilitlendi) || rapor.Contains(GeriYuklemeIsleyici.EditorGirisiKilidiEksikGunluk)))
                durum.GeriYuklemeRaporu = JsonSerializer.Serialize(rapor.Append(
                    $"Editör girişi {GeriYuklemeIsleyici.Yerel(db.Saati().GetUtcNow())} tarihinde açıldı: şifre sunucudaki KASA_EDITOR_SIFRE'ye sıfırlandı "
                    + "(Kasa:EditorSifreSifirla). Editör şifresini değiştirmediyse hemen değiştirmeli.").ToList(), DenetimYazici.JsonAyarlari);
            db.SaveChanges();
            // Değişiklikle aynı transaction'da: olay yazılamazsa sıfırlama da uygulanmaz, açılış durur.
            DenetimYazici.Yaz(db, new DenetimOlayi(GuvenlikOlaylari.EditorSifresiSifirlandi, GuvenlikOlaylari.Varlik, "1", null, DenetimYazici.Json(new
            {
                kullanici = GirisSiniri.Normalize(kullanici),
                ayar = Ayar,
                oturumlarKapatildi = true,
                kurtarmaKoduIptal = kurtarmaKoduVardi,
                girisKilidiKaldirildi = kilitliydi,
            }), Aktor: DenetimAktoru.Sistem, BaslikGerekcesi: false));
            // Günlükte editörün diğer olayları gibi yapılandırmadaki adla (EditorGuvenligi: şifre değişikliği, kurtarma).
            gunluk?.Yaz(GuvenlikGunlugu.EditorSifresiSifirlandi, kullanici: kullanici,
                ayrinti: new { oturumlarKapatildi = true, kurtarmaKoduIptal = kurtarmaKoduVardi, girisKilidiKaldirildi = kilitliydi }, zorunlu: true);
            tx.Commit();
        }
        log.LogWarning("Editör şifresi ortamdaki Kasa:EditorSifre değerine sıfırlandı (Kasa:EditorSifreSifirla); eski editör oturumları ve kurtarma kodu "
            + "iptal edildi{Kilit}. Editör bu şifreyle girip şifresini hemen değiştirmeli; ardından bayrağı kaldırıp uygulamayı yeniden başlatın.",
            kilitliydi ? ", geri yüklemenin giriş kilidi kaldırıldı" : "");
        return Sonuc.Uygulandi;
    }

    /// <summary>Bayrak: boş ya da yoksa kapalı; "true"/"false" dışındaki değer açılışı durdurur (yanlış yazılmış bayrak sessizce yok sayılmaz).</summary>
    private static bool Acik(IConfiguration cfg)
    {
        var deger = cfg[Ayar];
        if (string.IsNullOrWhiteSpace(deger))
            return false;
        return bool.TryParse(deger.Trim(), out var acik)
            ? acik
            : throw new InvalidOperationException($"{Ayar} 'true' ya da 'false' olmalıdır (şu an '{deger}').");
    }

    /// <summary>Sıfırlamanın hangi ortam şifresi için uygulandığının izi: HMAC-SHA256(Kasa:JwtKey, "editor-sifirla\n" + şifre), büyük
    /// harf onaltılık. Anahtar veritabanında ve yedekte olmadığından iz şifreyi çevrimdışı denemeye açmaz.</summary>
    internal static string Iz(IConfiguration cfg, string sifre)
        => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(cfg["Kasa:JwtKey"]!), Encoding.UTF8.GetBytes("editor-sifirla\n" + sifre)));
}
