using Kasa.ApiClient;

namespace Kasa.App.Core;

public enum YapilacakTuru { TekrarlayanGider, VadesiGecenCek, BugunVadeliCek, YaklasanCek, KartOdemesi, EksikGelen, KasaSayimi }

/// <summary>
/// "Bugün yapılacaklar" satırı. Satır bir şey KAYDETMEZ; düğmesi işi yapılacak yere götürür ve formu hazırlar
/// (<see cref="Hedef"/>: <see cref="DerinBaglanti"/> bağlantısı — gelen formu eksik dönem ve kanalla, çek
/// düzenleme formunda, kartın ödeme girişi ekstre borcuyla — ya da Panel'deki bekleyen kartı için
/// <see cref="YapilacakListesi.BekleyenKarti"/>). Son adım (Kaydet / Ekle) kullanıcınındır.
/// </summary>
/// <param name="Adet">Satırın kapsadığı kayıt sayısı (çek satırında düğme ilk çeki açar).</param>
public sealed record YapilacakSatiri(YapilacakTuru Tur, string Baslik, string Aciklama, string Hedef, bool Acil, int Adet = 1)
{
    /// <summary>Satır düğmesinin metni ("Gelen gir", "Çeki aç" vb.).</summary>
    public string DugmeMetni => Tur switch
    {
        YapilacakTuru.TekrarlayanGider => "Göster",
        YapilacakTuru.VadesiGecenCek or YapilacakTuru.BugunVadeliCek or YapilacakTuru.YaklasanCek
            => Hedef.Contains('?') ? (Adet > 1 ? "İlk çeki aç" : "Çeki aç") : "Çeklere git",
        YapilacakTuru.KartOdemesi => Hedef.Contains('?') ? "Ödeme gir" : "Kartlara git",
        YapilacakTuru.EksikGelen => "Gelen gir",
        YapilacakTuru.KasaSayimi => "Sayım yap",
        _ => "Git",
    };
}

/// <summary>
/// Editörün "Bugün yapılacaklar" listesi (saf). Kaynaklar okunabildiği kadarıyla kullanılır: bir kaynak
/// null ise (okunamadı / sunucu desteklemiyor) o tür satır üretilmez.
/// </summary>
public static class YapilacakListesi
{
    public const string BekleyenKarti = "bekleyen";
    public const string RotaCekler = "//cekler";
    public const string RotaKartlar = "//kartlar";
    public const string RotaIslemler = "//islemler";
    public const string RotaKasaSayimi = "//kasasayimi";

    /// <summary>Vadesine bu kadar gün (dahil) kalan portföy çekleri listelenir.</summary>
    public const int CekGunu = 3;

    /// <summary>Son kasa sayımı bu kadar günden eskiyse (ya da hiç yoksa) hatırlatılır.</summary>
    public const int SayimGunu = 7;

    public static IReadOnlyList<YapilacakSatiri> Olustur(
        IReadOnlyList<BekleyenGiderDto>? bekleyenler,
        CekOzetDto? cekOzet,
        IReadOnlyList<KrediKartiDto>? kartlar,
        IReadOnlyList<EksikGelenDto>? eksikGelenler,
        IReadOnlyList<KasaSayimDto>? sayimlar,
        DateOnly bugun)
    {
        var l = new List<YapilacakSatiri>();

        if (bekleyenler is { Count: > 0 })
        {
            var toplam = bekleyenler.Sum(b => b.Tutar);
            var adlar = Kisalt(bekleyenler.Select(b => b.Kalem).Distinct(), 3);
            l.Add(new(YapilacakTuru.TekrarlayanGider,
                $"{bekleyenler.Count} tekrarlayan gider onay bekliyor",
                $"{adlar} · toplam {PanelMetin.Tutar(toplam)}", BekleyenKarti,
                Acil: bekleyenler.Any(b => b.Vade < bugun)));
        }

        if (cekOzet is not null)
        {
            var gecen = cekOzet.VadesiGecenler.Where(c => c.VadeTarihi < bugun).ToList();
            var bugunku = cekOzet.Yaklasanlar.Where(c => c.VadeTarihi == bugun).ToList();
            var yakin = cekOzet.Yaklasanlar.Where(c => c.VadeTarihi > bugun && c.VadeTarihi <= bugun.AddDays(CekGunu)).ToList();
            if (gecen.Count > 0)
                l.Add(new(YapilacakTuru.VadesiGecenCek, $"Vadesi geçen {gecen.Count} çek", CekAciklama(gecen, bugun), IlkCek(gecen), Acil: true, gecen.Count));
            if (bugunku.Count > 0)
                l.Add(new(YapilacakTuru.BugunVadeliCek, $"Bugün vadesi gelen {bugunku.Count} çek", CekAciklama(bugunku, bugun), IlkCek(bugunku), Acil: true, bugunku.Count));
            if (yakin.Count > 0)
                l.Add(new(YapilacakTuru.YaklasanCek, $"{CekGunu} gün içinde vadesi gelecek {yakin.Count} çek", CekAciklama(yakin, bugun), IlkCek(yakin), Acil: false, yakin.Count));
        }

        if (kartlar is not null)
        {
            foreach (var d in kartlar)
            {
                var k = new KrediKartiGorunum(d, bugun.ToDateTime(TimeOnly.MinValue));
                if (!KartHatirlatici.OdemeBekliyor(k, bugun)) continue;
                var (_, vade) = KartHatirlatici.AcikEkstre(k, bugun);
                var ne = vade == bugun ? "son ödeme bugün"
                    : vade < bugun ? $"son ödeme {PanelMetin.Gun(vade, bugun)} idi"
                    : $"son ödeme {PanelMetin.GoreliGun(vade, bugun)}";
                l.Add(new(YapilacakTuru.KartOdemesi, $"{d.Ad} · {ne}", $"Ekstre borcu {PanelMetin.Tutar(k.EkstreBorc)}",
                    DerinBaglanti.Kart(d.Id), Acil: vade <= bugun));
            }
        }

        if (eksikGelenler is not null)
            foreach (var e in eksikGelenler.Where(e => e.Kanallar.Count > 0))
                l.Add(new(YapilacakTuru.EksikGelen, $"Gelen girilmedi: {PanelMetin.Aralik(e.DonemStart, e.DonemEnd)}",
                    string.Join(", ", e.Kanallar), DerinBaglanti.GelenGir(e.DonemStart, e.Kanallar[0]), Acil: false, e.Kanallar.Count));

        if (sayimlar is not null)
        {
            var son = sayimlar.Count > 0 ? sayimlar.Max(s => s.Tarih) : (DateOnly?)null;
            if (son is null)
                l.Add(new(YapilacakTuru.KasaSayimi, "Henüz kasa sayımı yapılmadı", "Kasadaki nakdi sayıp defterle karşılaştırın.", RotaKasaSayimi, Acil: false));
            else if (bugun.DayNumber - son.Value.DayNumber > SayimGunu)
                l.Add(new(YapilacakTuru.KasaSayimi, $"Kasa sayımı {bugun.DayNumber - son.Value.DayNumber} gündür yapılmadı",
                    $"Son sayım {PanelMetin.Gun(son.Value, bugun)}", RotaKasaSayimi, Acil: false));
        }

        return l;
    }

    // Açıklamadaki sırayla ilk çek (en eski vade): satırın düğmesi onu açar.
    private static string IlkCek(IReadOnlyList<CekDto> cekler)
        => DerinBaglanti.Cek(cekler.OrderBy(c => c.VadeTarihi).ThenBy(c => c.Id).First().Id);

    // "Ahmet · +5.000,00 ₺ (12 Eylül), Veli · −2.500,00 ₺ ve 2 çek daha"
    private static string CekAciklama(IReadOnlyList<CekDto> cekler, DateOnly bugun)
    {
        var parcalar = cekler.OrderBy(c => c.VadeTarihi).ThenBy(c => c.Id).Take(2)
            .Select(c => $"{c.Kisi} · {PanelMetin.IsaretliTutar(c.Yon == CekYonu.Alinan ? c.Tutar : -c.Tutar)} ({PanelMetin.GoreliGun(c.VadeTarihi, bugun)})");
        var metin = string.Join(", ", parcalar);
        return cekler.Count > 2 ? $"{metin} ve {cekler.Count - 2} çek daha" : metin;
    }

    private static string Kisalt(IEnumerable<string> adlar, int en)
    {
        var l = adlar.ToList();
        return l.Count <= en ? string.Join(", ", l) : string.Join(", ", l.Take(en)) + $" ve {l.Count - en} diğer";
    }

    /// <summary>Bildirim metni: "3 iş: 2 tekrarlayan gider onay bekliyor · Vadesi geçen 1 çek …".</summary>
    public static string BildirimMetni(IReadOnlyList<YapilacakSatiri> satirlar)
        => string.Join(" · ", satirlar.Take(3).Select(s => s.Baslik)) + (satirlar.Count > 3 ? $" · +{satirlar.Count - 3} iş daha" : "");
}
