using Kasa.Api.Data;

namespace Kasa.Api.Servisler;

/// <summary>Risk eşikleri (config'ten ezilebilir: <c>Kasa:Risk...</c>).</summary>
public sealed record RiskEsikleri(
    double UzakKirmiziSaat = 48,
    double YerelSariSaat = 26,
    double YerelKirmiziSaat = 48,
    double DogrulamaSariSaat = 48,
    long DiskSariMb = 1024,
    long DiskKirmiziMb = 300,
    int GirisSari = 5,
    int GirisKirmizi = 20)
{
    public static RiskEsikleri Oku(IConfiguration cfg)
    {
        var v = new RiskEsikleri();
        return v with
        {
            UzakKirmiziSaat = cfg.GetValue("Kasa:RiskUzakYedekKirmiziSaat", v.UzakKirmiziSaat),
            YerelSariSaat = cfg.GetValue("Kasa:RiskYedekSariSaat", v.YerelSariSaat),
            YerelKirmiziSaat = cfg.GetValue("Kasa:RiskYedekKirmiziSaat", v.YerelKirmiziSaat),
            DogrulamaSariSaat = cfg.GetValue("Kasa:RiskDogrulamaSariSaat", v.DogrulamaSariSaat),
            DiskSariMb = cfg.GetValue("Kasa:RiskDiskSariMb", v.DiskSariMb),
            DiskKirmiziMb = cfg.GetValue("Kasa:RiskDiskKirmiziMb", v.DiskKirmiziMb),
            GirisSari = cfg.GetValue("Kasa:RiskGirisSari", v.GirisSari),
            GirisKirmizi = cfg.GetValue("Kasa:RiskGirisKirmizi", v.GirisKirmizi),
        };
    }
}

/// <summary>Karşılıksız çıkmış alınan çek (risk kartı için).</summary>
/// <param name="TakipNotuVar">Takipte: takip notu yazılmış ya da konumu İcrada (Paket D, takibe verildi).</param>
public sealed record KarsiliksizCek(int Id, string Kisi, decimal Tutar, DateOnly Vade, bool TakipNotuVar);

/// <summary>Risk kartının girdileri: hepsi /health'in zaten hesapladığı ya da DB'den okunan değerler.</summary>
public sealed record RiskGirdisi(
    UzakYedekDurumu.Ozet UzakYedek,
    bool YerelYedekEtkin,
    DateTime? YerelSonBasariliUtc,
    string? YerelHata,
    YedekDogrulamaEntity? SonDogrulama,
    long? DiskBosMb,
    int DunBasarisizGiris,
    IReadOnlyList<KarsiliksizCek> Karsiliksizlar,
    DateTime SimdiUtc);

/// <summary>
/// Sistem ve risk kartının kuralları (saf fonksiyon; test edilir). Kırmızı: hemen bakılmalı;
/// sarı: dikkat. Hiçbir şeyi değiştirmez.
/// </summary>
public static class RiskHesaplayici
{
    public static IReadOnlyList<RiskMaddesiDto> Hesapla(RiskGirdisi g, RiskEsikleri e)
    {
        var m = new List<RiskMaddesiDto>();
        void Ekle(RiskSeviyesi s, string konu, string baslik, string aciklama) => m.Add(new RiskMaddesiDto(s, konu, baslik, aciklama));

        // Sunucu dışı yedek (/health'teki uzakYedek ile aynı özet).
        var u = g.UzakYedek;
        switch (u.Durum)
        {
            case UzakYedekDurumu.Yapilandirilmadi:
                Ekle(RiskSeviyesi.Sari, "UzakYedek", "Sunucu dışı yedek kurulmamış",
                    "Sunucu arızalanırsa veri yalnız bu sunucudadır. deploy/uzak-yedek kurulumuna bakın.");
                break;
            case UzakYedekDurumu.Hatali:
                Ekle(RiskSeviyesi.Kirmizi, "UzakYedek", "Sunucu dışı yedek hatalı",
                    u.Hata ?? u.DogrulamaHatasi ?? "Son gönderim ya da doğrulama başarısız.");
                break;
            case UzakYedekDurumu.Okunamadi:
                Ekle(RiskSeviyesi.Sari, "UzakYedek", "Sunucu dışı yedeğin durumu okunamadı", u.Hata ?? "Durum dosyası okunamadı.");
                break;
            case UzakYedekDurumu.Eski when u.SonBasariYasSaat is null:
                Ekle(RiskSeviyesi.Kirmizi, "UzakYedek", "Sunucu dışına hiç yedek gönderilmedi", "Yedek konteynerinin çalıştığını kontrol edin.");
                break;
            case UzakYedekDurumu.Eski:
                var uyas = u.SonBasariYasSaat!.Value;
                Ekle(uyas >= e.UzakKirmiziSaat ? RiskSeviyesi.Kirmizi : RiskSeviyesi.Sari, "UzakYedek",
                    $"Sunucu dışı yedek {Yas(uyas)} önce", "Son başarılı gönderim eski; yedek konteynerini kontrol edin.");
                break;
        }

        // Sunucudaki günlük yedek.
        if (!g.YerelYedekEtkin)
            Ekle(RiskSeviyesi.Sari, "YerelYedek", "Günlük yedek kapalı", "Kasa:YedekKlasoru ayarlı değil; sunucu içinde günlük kopya alınmıyor.");
        else if (g.YerelHata is { Length: > 0 } yh)
            Ekle(RiskSeviyesi.Kirmizi, "YerelYedek", "Günlük yedek alınamadı", Metin.Kisalt(yh, 200));
        else if (g.YerelSonBasariliUtc is not { } ys)
            Ekle(RiskSeviyesi.Sari, "YerelYedek", "Henüz günlük yedek yok", "İlk yedek uygulama açıldıktan kısa süre sonra alınır.");
        else
        {
            var yas = (g.SimdiUtc - ys).TotalHours;
            if (yas >= e.YerelKirmiziSaat)
                Ekle(RiskSeviyesi.Kirmizi, "YerelYedek", $"Son günlük yedek {Yas(yas)} önce", "Günlük yedek alınmıyor.");
            else if (yas >= e.YerelSariSaat)
                Ekle(RiskSeviyesi.Sari, "YerelYedek", $"Son günlük yedek {Yas(yas)} önce", "Bugünün yedeği gecikti.");
        }

        // Gece yedek doğrulaması.
        if (g.SonDogrulama is { Basarili: false } sd)
            Ekle(RiskSeviyesi.Kirmizi, "YedekDogrulama", $"Yedek doğrulaması başarısız ({sd.Dosya})", sd.Mesaj);
        else if (g.YerelYedekEtkin && (g.SonDogrulama is null || (g.SimdiUtc - g.SonDogrulama.ZamanUtc).TotalHours >= e.DogrulamaSariSaat))
            Ekle(RiskSeviyesi.Sari, "YedekDogrulama", "Yedek son 2 gündür doğrulanmadı", "Gece doğrulaması en yeni yedeği açıp kayıt sayılarını karşılaştırır.");

        // Disk.
        if (g.DiskBosMb is { } disk)
        {
            if (disk < e.DiskKirmiziMb)
                Ekle(RiskSeviyesi.Kirmizi, "Disk", $"Disk dolmak üzere ({disk} MB boş)", "Eski yedekleri taşıyın ya da diski büyütün; disk dolarsa kayıt yazılamaz.");
            else if (disk < e.DiskSariMb)
                Ekle(RiskSeviyesi.Sari, "Disk", $"Disk azalıyor ({disk} MB boş)", "Yer açmayı planlayın.");
        }

        // Dünkü başarısız girişler.
        if (g.DunBasarisizGiris >= e.GirisKirmizi)
            Ekle(RiskSeviyesi.Kirmizi, "Giris", $"Dün {g.DunBasarisizGiris} başarısız giriş", "Şifre deneniyor olabilir. Giriş günlüğüne bakın; gerekirse şifreleri değiştirin.");
        else if (g.DunBasarisizGiris >= e.GirisSari)
            Ekle(RiskSeviyesi.Sari, "Giris", $"Dün {g.DunBasarisizGiris} başarısız giriş", "Giriş günlüğüne bakın.");

        // Karşılıksız çekler: takipte değilse (not yok, icrada değil) kırmızı, hepsi takipteyse sarı.
        var takipsiz = g.Karsiliksizlar.Where(c => !c.TakipNotuVar).ToList();
        if (takipsiz.Count > 0)
            Ekle(RiskSeviyesi.Kirmizi, "Cek", $"{takipsiz.Count} karşılıksız çekin takibi yok",
                $"Toplam {Tutar(takipsiz.Sum(c => c.Tutar))} ₺ ({string.Join(", ", takipsiz.Take(3).Select(c => Metin.Kisalt(c.Kisi, 30)))}{(takipsiz.Count > 3 ? ", …" : "")}). Çeke takip notu yazın ya da konumunu İcrada yapın.");
        else if (g.Karsiliksizlar.Count > 0)
            Ekle(RiskSeviyesi.Sari, "Cek", $"{g.Karsiliksizlar.Count} karşılıksız çek takipte",
                $"Toplam {Tutar(g.Karsiliksizlar.Sum(c => c.Tutar))} ₺.");

        return m.OrderByDescending(x => x.Seviye).ToList();
    }

    public static string Durum(IReadOnlyList<RiskMaddesiDto> maddeler)
        => maddeler.Any(x => x.Seviye == RiskSeviyesi.Kirmizi) ? "kirmizi"
            : maddeler.Count > 0 ? "sari"
            : "ok";

    private static string Yas(double saat)
        => saat < 48 ? $"{Math.Floor(saat):0} saat" : $"{Math.Floor(saat / 24):0} gün";

    private static string Tutar(decimal t) => t.ToString("#,##0.00", Metin.Tr);
}
