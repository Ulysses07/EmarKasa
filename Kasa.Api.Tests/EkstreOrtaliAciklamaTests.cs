namespace Kasa.Api.Tests;

/// <summary>Açıklama hücresi tarih satırına göre dikey ortalanan PDF tabloları (QNB hesap hareketi; docs/specs/2026-11-25-pdf-ekrani.md §2):
/// iki satırlık açıklamanın biri tarih satırının üstüne, biri altına düşer.</summary>
public class EkstreOrtaliAciklamaTests
{
    // Tarih 4'te, kanal 18'de, açıklama 26'da başlar; tutar 60'ta, bakiye 75'te biter. Başlıkta İşlem Tutarı 50'de, Bakiye 64'te başlar.
    private static readonly string Baslik = ("    Tarih".PadRight(18) + "Kanal".PadRight(8) + "Açıklama").PadRight(50) + "İşlem Tutarı".PadRight(14) + "Bakiye";
    private static string Hareket(string tarih, string kanal, string aciklama, string tutar, string bakiye) =>
        (("    " + tarih).PadRight(18) + kanal.PadRight(8) + aciklama).PadRight(60 - tutar.Length) + tutar + bakiye.PadLeft(15);
    private static string Metin(string metin) => new string(' ', 26) + metin;
    private static EkstreOkumaSonucu Oku(params string[][] sayfalar) =>
        EkstreMetinOkuyucu.Oku("Para Birimi: TL\nMüşteri Adı : ORNEK KISI\n" + Baslik + "\n" + string.Join("\f", sayfalar.Select(s => string.Join("\n", s))), "Banka", "QNB");

    private static readonly string[] Sayfa1 =
    [
        Hareket("01.09.2026", "ŞB", "POS SATIS ISLEMI", "1,000.00", "11,000.00"),
        Metin("POS SATIS UYE ISYERI"),
        Hareket("01.09.2026", "ŞB", "", "-30.00", "10,970.00"),
        Metin("UCRETI PESIN"),
        Hareket("01.09.2026", "ŞB", "BSMV", "-1.50", "10,968.50"),
        "Sayfa: 1/2",
    ];
    private static readonly string[] Sayfa2 =
    [
        Metin("HAVALE GONDEREN ORNEK"),
        Hareket("02.09.2026", "MB", "", "500.00", "11,468.50"),
        Metin("GELEN HAVALE"),
        "Sayfa: 2/2",
    ];

    [Fact]
    public void Ustte_ve_altta_kalan_aciklama_satirlari_bos_aciklamali_harekete_eklenir()
    {
        var sonuc = Oku(Sayfa1, Sayfa2);
        var rows = sonuc.Satirlar;
        Assert.Equal(["ŞB POS SATIS ISLEMI", "ŞB POS SATIS UYE ISYERI UCRETI PESIN", "ŞB BSMV", "MB HAVALE GONDEREN ORNEK GELEN HAVALE"], rows.Select(r => r.Aciklama));
        Assert.Equal([1000m, 30m, 1.50m, 500m], rows.Select(r => r.Tutar!.Value));
        Assert.Equal(["Belirsiz", "Cikis", "Cikis", "Giris"], rows.Select(r => r.Yon));
        // Kaynak satırı PDF'teki sırayla üst satırı, tarih satırını ve alt satırı gösterir.
        Assert.StartsWith("POS SATIS UYE ISYERI\n    01.09.2026", rows[1].KaynakSatir);
        Assert.EndsWith("\nUCRETI PESIN", rows[1].KaynakSatir);
        // Açıklama sınıflandırmaya da girer: "ücreti" banka masrafıdır.
        Assert.Equal("Ucret", rows[1].Sinif);
        Assert.Contains(sonuc.Uyarilar, w => w.StartsWith("2 sayfada açıklaması tarih satırının üstüne ve altına bölünmüş"));
    }

    [Fact]
    public void Paylasim_tutmayan_sayfaya_dokunulmaz()
    {
        // Son hareketten sonra not satırı var: metin satırları hareketlere tam paylaşılamaz.
        var sonuc = Oku([.. Sayfa1[..^1], Metin("BU BELGE BILGI AMACLIDIR"), "Sayfa: 1/1"]);
        Assert.Equal(["ŞB POS SATIS ISLEMI", "ŞB", "ŞB BSMV"], sonuc.Satirlar.Select(r => r.Aciklama));
        Assert.DoesNotContain(sonuc.Uyarilar, w => w.Contains("bölünmüş"));
    }

    [Fact]
    public void Asagi_tasan_aciklama_ortali_sayilmaz()
    {
        // Açıklama tarih satırında başlayıp alt satıra taşıyor: alt satırı alacak hareketin açıklaması boş değil.
        var sonuc = Oku(
        [
            Hareket("01.09.2026", "ŞB", "HAVALE GELEN", "1,000.00", "11,000.00"),
            Metin("ORNEK FIRMA"),
            Hareket("02.09.2026", "ŞB", "EFT GIDEN", "-30.00", "10,970.00"),
            Metin("DIGER FIRMA"),
        ]);
        Assert.Equal(["ŞB HAVALE GELEN", "ŞB EFT GIDEN"], sonuc.Satirlar.Select(r => r.Aciklama));
        Assert.DoesNotContain(sonuc.Uyarilar, w => w.Contains("bölünmüş"));
    }
}
