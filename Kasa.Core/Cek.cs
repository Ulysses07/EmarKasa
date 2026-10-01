using System.Globalization;
using Kasa.Core.Kodlar;

namespace Kasa.Core;

/// <summary>Çek ya da senedin türetme için gereken alanları (docs/specs/2026-10-01-cekler.md).</summary>
public sealed record CekBilgisi(int Id, string Tur, string Yon, string No, string Kisi, decimal Tutar);

/// <summary>Çek hareketi. <paramref name="Kanal"/>: kasayı etkileyen hareketin kasası — gerçek kanal adı ya da (yalnız verilen
/// çekin ödemesinde) <see cref="KanalEtiketleri.Ortak"/>; kasası çözülemeyen harekette null. <paramref name="NetTutar"/> yalnız
/// kırdırmada hesaba geçen tutardır. <paramref name="Karsi"/>: ciroda ciro edilen kişi, kırdırmada banka ya da faktoring adı.</summary>
public sealed record CekHareketi(int Id, int Sira, string Tur, DateOnly Tarih, decimal Tutar, decimal? NetTutar = null, string? Kanal = null, string? Karsi = null);

/// <summary>Hareketlerden hesaplanan durum. <paramref name="Kalan"/>: tahsil ya da ödeme bekleyen tutar (ciro edilmiş, kırdırılmış
/// ve iade edilmiş çekte 0). <paramref name="Odenen"/>: tahsilat ya da ödeme toplamı. <paramref name="AcikDevir"/>: dönüşü
/// girilmemiş son ciro ya da kırdırma; yoksa null.</summary>
public sealed record CekDurumu(string Durum, decimal Kalan, decimal Odenen, CekHareketi? AcikDevir)
{
    /// <summary>Portföyde ya da kısmen tahsil edilmiş / ödenmiş: bildirim, panel ve "Portföyde" süzgecindeki çek.</summary>
    public bool Acik => Durum is CekDurumlari.Portfoyde or CekDurumlari.KismenTahsilEdildi or CekDurumlari.KismenOdendi;

    /// <summary>Tahsil edildi, ödendi, ciro edildi, kırdırıldı ya da iade edildi ("Kapananlar" süzgeci).</summary>
    public bool Kapali => Durum is CekDurumlari.TahsilEdildi or CekDurumlari.Odendi or CekDurumlari.CiroEdildi
        or CekDurumlari.Kirdirildi or CekDurumlari.IadeEdildi;
}

/// <summary>
/// Çek ve senet kuralları (tasarım "Durum" bölümü). Durum saklanmaz, hareketlerin sırasından hesaplanır. Geçiş tablosu:
/// portföyde/kısmen alınan çekte Tahsilat, Ciro ve Kırdırma (yalnız hiç tahsilat yokken), Karşılıksız, İade; verilende Ödeme,
/// Karşılıksız, İade. Ciro edilmiş ya da kırdırılmış çekte yalnız Dönüş (çek yeniden karşılıksız olur). Karşılıksız çekte
/// Tahsilat/Ödeme ve İade. Kapanmış çekte hareket girilmez, yalnız son hareket geri alınır.
/// </summary>
public static class CekKurallari
{
    private static readonly NumberFormatInfo TlBicimi = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NegativeSign = "-" };
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private static string Tl(decimal tutar) => tutar.ToString("#,0.00", TlBicimi) + " TL";

    /// <summary>Kasayı etkileyen hareket: türetilmiş satır üretir ve ay kilidine takılır.</summary>
    public static bool KasaEtkili(string tur) => tur is CekHareketTurleri.Tahsilat or CekHareketTurleri.Odeme or CekHareketTurleri.Ciro
        or CekHareketTurleri.Kirdirma or CekHareketTurleri.Donus;

    public static CekDurumu Durum(string yon, decimal tutar, IReadOnlyList<CekHareketi> hareketler)
    {
        decimal odenen = 0;
        CekHareketi? devir = null;
        bool karsiliksiz = false, iade = false;
        foreach (var h in hareketler.OrderBy(h => h.Sira))
        {
            switch (h.Tur)
            {
                case CekHareketTurleri.Tahsilat or CekHareketTurleri.Odeme:
                    odenen += h.Tutar;
                    break;
                case CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma:
                    devir = h;
                    break;
                case CekHareketTurleri.Donus:
                    devir = null;
                    karsiliksiz = true;
                    break;
                case CekHareketTurleri.Karsiliksiz:
                    karsiliksiz = true;
                    break;
                case CekHareketTurleri.Iade:
                    iade = true;
                    break;
            }
        }
        var alinan = yon == CekYonleri.Alinan;
        var kalan = tutar - odenen;
        var durum = iade ? CekDurumlari.IadeEdildi
            : devir?.Tur == CekHareketTurleri.Ciro ? CekDurumlari.CiroEdildi
            : devir is not null ? CekDurumlari.Kirdirildi
            : kalan <= 0 ? (alinan ? CekDurumlari.TahsilEdildi : CekDurumlari.Odendi)
            : karsiliksiz ? CekDurumlari.Karsiliksiz
            : odenen > 0 ? (alinan ? CekDurumlari.KismenTahsilEdildi : CekDurumlari.KismenOdendi)
            : CekDurumlari.Portfoyde;
        return new(durum, iade || devir is not null ? 0 : Math.Max(0, kalan), odenen, devir);
    }

    /// <summary>Çekin şu anki durumunda girilebilecek hareket türleri (geçiş tablosu); kapanmış çekte boş.</summary>
    public static IReadOnlyList<string> IzinliHareketler(string yon, decimal tutar, IReadOnlyList<CekHareketi> hareketler)
    {
        var d = Durum(yon, tutar, hareketler);
        var alinan = yon == CekYonleri.Alinan;
        var odeme = alinan ? CekHareketTurleri.Tahsilat : CekHareketTurleri.Odeme;
        if (d.Acik)
            return alinan && d.Odenen == 0
                ? [odeme, CekHareketTurleri.Ciro, CekHareketTurleri.Kirdirma, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade]
                : [odeme, CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade];
        return d.Durum switch
        {
            CekDurumlari.CiroEdildi or CekDurumlari.Kirdirildi => [CekHareketTurleri.Donus],
            CekDurumlari.Karsiliksiz => [odeme, CekHareketTurleri.Iade],
            _ => [],
        };
    }

    /// <summary>Yeni hareketin geçiş, tarih ve tutar kuralına aykırılığının iletisi; geçerliyse null. Kasa seçimi ve metin
    /// uzunlukları uçta denetlenir.</summary>
    public static string? HareketHatasi(string yon, decimal tutar, IReadOnlyList<CekHareketi> hareketler, CekHareketi yeni)
    {
        var d = Durum(yon, tutar, hareketler);
        var izinli = IzinliHareketler(yon, tutar, hareketler);
        if (!izinli.Contains(yeni.Tur))
            return izinli.Count == 0
                ? $"Bu kayıt {DurumAdi(d.Durum)}; yeni hareket girilemez. Gerekirse son hareketi geri alın."
                : $"Bu kayıt {DurumAdi(d.Durum)}; şu an yalnız şu hareketler girilebilir: {string.Join(", ", izinli.Select(HareketAdi))}.";
        if (hareketler.Count > 0 && yeni.Tarih < hareketler.Max(h => h.Tarih))
            return $"Hareket tarihi önceki hareketin tarihinden ({hareketler.Max(h => h.Tarih).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}) önce olamaz.";
        if (decimal.Round(yeni.Tutar, 2) != yeni.Tutar || yeni.NetTutar is { } n && decimal.Round(n, 2) != n)
            return "Tutar en fazla iki ondalık basamak içerebilir.";
        if (yeni.Tur != CekHareketTurleri.Kirdirma && yeni.NetTutar is not null)
            return "Hesaba geçen tutar yalnız kırdırmada girilir.";
        if (yeni.Tur is not (CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma) && !string.IsNullOrWhiteSpace(yeni.Karsi))
            return "Karşı taraf yalnız ciro ve kırdırmada girilir.";
        return yeni.Tur switch
        {
            CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma when string.IsNullOrWhiteSpace(yeni.Karsi)
                => "Ciroda ciro edilen kişiyi, kırdırmada banka ya da faktoring adını yazın.",
            CekHareketTurleri.Tahsilat or CekHareketTurleri.Odeme when yeni.Tutar <= 0 || yeni.Tutar > d.Kalan
                => $"Tutar sıfırdan büyük olmalı ve kalan tutarı ({Tl(d.Kalan)}) aşamaz.",
            CekHareketTurleri.Ciro when yeni.Tutar != d.Kalan
                => $"Ciroda tutar kalan tutarın tamamı ({Tl(d.Kalan)}) olmalı.",
            CekHareketTurleri.Kirdirma when yeni.Tutar != d.Kalan
                => $"Kırdırmada tutar çek tutarının tamamı ({Tl(d.Kalan)}) olmalı.",
            CekHareketTurleri.Kirdirma when yeni.NetTutar is not { } net || net < 0 || net > yeni.Tutar
                => "Hesaba geçen tutar 0 ile çek tutarı arasında olmalı.",
            CekHareketTurleri.Donus when yeni.Tutar != d.AcikDevir!.Tutar
                => $"Dönüş tutarı ciro ya da kırdırma tutarına ({Tl(d.AcikDevir!.Tutar)}) eşit olmalı.",
            CekHareketTurleri.Karsiliksiz or CekHareketTurleri.Iade when yeni.Tutar != 0
                => "Karşılıksız ve iade hareketinde tutar girilmez.",
            _ => null,
        };
    }

    /// <summary>Aynı çek: aynı yön, banka ve numara (baştaki ve sondaki boşluk ile büyük-küçük harf farkı yok sayılır; iki
    /// banka da boşsa eşittir). Aynı çek uyarısı kaydı engellemez.</summary>
    public static bool AyniCek(string yon1, string? banka1, string no1, string yon2, string? banka2, string no2)
        => yon1 == yon2 && Esit(banka1, banka2) && Esit(no1, no2);

    private static bool Esit(string? a, string? b) => Katla(a) == Katla(b);

    /// <summary>Karşılaştırma öncesi küçük harfe çevirir (tr-TR) ve I/ı/İ/i'yi tek harfe katlar: tr-TR'de "I".ToLower() "ı"
    /// (noktasız) verir, "ziraat" içindeki "i" (noktalı) ile harf olarak eşit sayılmaz; bu yüzden IgnoreCase tek başına
    /// "ZIRAAT" ile "ziraat"i eşitlemez.</summary>
    private static string Katla(string? s) => (s ?? "").Trim().ToLower(Tr).Replace('ı', 'i');

    /// <summary>Durumun görünen adı (küçük harfle; cümle içinde ve listede kullanılır).</summary>
    public static string DurumAdi(string durum) => durum switch
    {
        CekDurumlari.Portfoyde => "portföyde",
        CekDurumlari.KismenTahsilEdildi => "kısmen tahsil edildi",
        CekDurumlari.TahsilEdildi => "tahsil edildi",
        CekDurumlari.KismenOdendi => "kısmen ödendi",
        CekDurumlari.Odendi => "ödendi",
        CekDurumlari.CiroEdildi => "ciro edildi",
        CekDurumlari.Kirdirildi => "kırdırıldı",
        CekDurumlari.Karsiliksiz => "karşılıksız",
        CekDurumlari.IadeEdildi => "iade edildi",
        _ => durum,
    };

    /// <summary>Hareket türünün görünen adı.</summary>
    public static string HareketAdi(string tur) => tur switch
    {
        CekHareketTurleri.Tahsilat => "Tahsilat",
        CekHareketTurleri.Odeme => "Ödeme",
        CekHareketTurleri.Ciro => "Ciro",
        CekHareketTurleri.Kirdirma => "Kırdırma",
        CekHareketTurleri.Donus => "Dönüş",
        CekHareketTurleri.Karsiliksiz => "Karşılıksız",
        CekHareketTurleri.Iade => "İade",
        _ => tur,
    };
}

/// <summary>
/// Çek hareketlerini sentetik <see cref="Gelen"/>/<see cref="Islem"/> satırlarına çevirir (tasarım "Rapora etkisi"; kredi
/// taksitlerindeki desen). Saf: EF/I-O yok; satırlar yalnız hesap motoruna beslenir, veritabanına yazılmaz. Gelirin döküm
/// anahtarı "Cek:{hareketId}", ciro/kırdırma/dönüşün gider ayağı "Cek:{hareketId}:gider"; iki ayak da kaynak kaydı
/// (<see cref="Islem.Kaynak"/>) "Cek:{hareketId}" taşır. Gelir, tarihini içeren dönemi bulamazsa (takip başlangıcından önce ya da
/// ufkun ötesinde) üretilmez. Kasası çözülemeyen hareketin (<see cref="CekHareketi.Kanal"/> null) geliri genel kasaya, gideri
/// "Dağılım bekliyor"a yazılır.
/// </summary>
public static class CekTuretici
{
    public static (IReadOnlyList<Gelen> Gelenler, IReadOnlyList<Islem> Islemler) Satirlar(CekBilgisi cek, IReadOnlyList<CekHareketi> hareketler, IReadOnlyList<Donem> donemler)
    {
        var gelenler = new List<Gelen>();
        var islemler = new List<Islem>();
        var ad = cek.Tur switch { CekTurleri.Senet => "Senet", _ => "Çek" };
        CekHareketi? devir = null;
        foreach (var h in hareketler.OrderBy(h => h.Sira))
        {
            var anahtar = "Cek:" + h.Id.ToString(CultureInfo.InvariantCulture);
            void Gelir(decimal tutar, string aciklama)
            {
                if (donemler.FirstOrDefault(d => d.Icerir(h.Tarih)) is not { } donem)
                    return;
                gelenler.Add(h.Kanal is null
                    ? new Gelen(donem.Start, KanalEtiketleri.GenelKasa, tutar, GenelGelir: true) { Tarih = h.Tarih, KaynakAnahtari = anahtar, Aciklama = aciklama }
                    : new Gelen(donem.Start, h.Kanal, tutar) { Tarih = h.Tarih, KaynakAnahtari = anahtar, Aciklama = aciklama });
            }
            void Gider(string cari, decimal tutar, string not, string kaynakAnahtari) =>
                islemler.Add(new Islem(h.Tarih, cari, tutar, h.Kanal ?? KanalEtiketleri.DagilimBekliyor, GiderTipi.Cari, not, DagilimBekliyor: h.Kanal is null)
                { Kaynak = anahtar, KaynakAnahtari = kaynakAnahtari });
            switch (h.Tur)
            {
                case CekHareketTurleri.Tahsilat:
                    Gelir(h.Tutar, $"{ad} tahsili: {cek.Kisi} / {cek.No}");
                    break;
                case CekHareketTurleri.Odeme:
                    Gider(cek.Kisi, h.Tutar, $"{ad} ödemesi / {cek.No}", anahtar);
                    break;
                case CekHareketTurleri.Ciro:
                    devir = h;
                    Gelir(h.Tutar, $"{ad} cirosu: {cek.Kisi} / {cek.No}");
                    Gider(h.Karsi ?? "", h.Tutar, $"{ad} cirosu / {cek.No}", anahtar + ":gider");
                    break;
                case CekHareketTurleri.Kirdirma:
                    devir = h;
                    Gelir(h.Tutar, $"{ad} kırdırma: {cek.Kisi} / {cek.No}");
                    if (h.Tutar - (h.NetTutar ?? h.Tutar) is > 0 and var masraf)
                        Gider(h.Karsi ?? "", masraf, $"{ad} kırdırma masrafı / {cek.No}", anahtar + ":gider");
                    break;
                case CekHareketTurleri.Donus:
                    Gelir(-h.Tutar, $"{ad} dönüşü: {cek.Kisi} / {cek.No}");
                    if (devir?.Tur == CekHareketTurleri.Ciro)
                        Gider(devir.Karsi ?? "", -h.Tutar, $"{ad} dönüşü / {cek.No}", anahtar + ":gider");
                    devir = null;
                    break;
            }
        }
        return (gelenler, islemler);
    }
}
