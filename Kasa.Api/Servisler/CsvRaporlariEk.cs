using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api.Servisler;

/// <summary>
/// Paket B'nin yeni "Excel'e aktar" çıktıları (çekler, kasa sayımları, geçmiş, gelenler, kart
/// ödemeleri, kasa dökümü). <see cref="CsvRaporlari"/> ile aynı biçim ve formül enjeksiyonu koruması
/// (<see cref="CsvYazici"/>): hesap yapılmaz, yalnız biçimlenir.
/// </summary>
public static class CsvRaporlariEk
{
    public static string YonAdi(CekYonu y) => y == CekYonu.Alinan ? "Alınan" : "Verilen";

    /// <summary>Çek durumunun ekrandaki adı (verilen çekte portföy "Ödenecek"tir).</summary>
    public static string CekDurumAdi(CekYonu yon, CekDurumu d) => d switch
    {
        CekDurumu.Portfoyde => yon == CekYonu.Verilen ? "Ödenecek" : "Portföyde",
        CekDurumu.TahsilEdildi => "Tahsil edildi",
        CekDurumu.Odendi => "Ödendi",
        CekDurumu.CiroEdildi => "Ciro edildi",
        CekDurumu.Karsiliksiz => "Karşılıksız",
        CekDurumu.IadeEdildi => "İade edildi",
        _ => d.ToString(),
    };

    public static string RolAdi(string? rol) => rol switch
    {
        "editor" => "Editör",
        "viewer" => "İzleyici",
        _ => rol ?? "",
    };

    public static string EvrakTurAdi(CekTuru t) => t == CekTuru.Senet ? "Senet" : "Çek";

    public static string KonumAdi(CekKonumu k) => k switch
    {
        CekKonumu.Elde => "Elde",
        CekKonumu.BankadaTahsilde => "Bankada tahsilde",
        CekKonumu.Teminatta => "Teminatta",
        CekKonumu.Icrada => "İcrada",
        _ => k.ToString(),
    };

    /// <summary>Toplam satırındaki adet: yalnız çek varsa "3 çek" (eski biçim), senet de varsa "2 çek, 1 senet".</summary>
    public static string EvrakAdedi(IReadOnlyCollection<CekEntity> l)
    {
        var senet = l.Count(c => c.Tur == CekTuru.Senet);
        var cek = l.Count - senet;
        if (senet == 0) return $"{cek} çek";
        return cek == 0 ? $"{senet} senet" : $"{cek} çek, {senet} senet";
    }

    /// <summary>
    /// Çek/senet listesi (GET /api/cekler ile aynı sıra) + yön başına toplam satırları. Paket D'nin
    /// tür, konum (yalnız alınan evrakta) ve ciro edilen cari sütunları sonda: eski sütunların yeri değişmez.
    /// </summary>
    public static byte[] Cekler(IReadOnlyList<CekEntity> cekler)
    {
        var csv = new CsvYazici().Baslik("Yön", "Çek no", "Banka", "Kişi/firma", "Tutar", "Düzenleme", "Vade", "Kanal", "Durum", "İşlem tarihi", "Not",
            "Tür", "Konum", "Ciro edilen cari");
        foreach (var c in cekler)
            csv.Satir(
                CsvYazici.Metin(YonAdi(c.Yon)),
                CsvYazici.Metin(c.CekNo),
                CsvYazici.Metin(c.Banka),
                CsvYazici.Metin(c.Kisi),
                CsvYazici.Sayi(c.Tutar),
                CsvYazici.Tarih(c.DuzenlemeTarihi),
                CsvYazici.Tarih(c.VadeTarihi),
                CsvYazici.Metin(c.Kanal),
                CsvYazici.Metin(CekDurumAdi(c.Yon, c.Durum)),
                c.IslemTarihi is { } t ? CsvYazici.Tarih(t) : "",
                CsvYazici.Metin(c.Not),
                CsvYazici.Metin(EvrakTurAdi(c.Tur)),
                c.Yon == CekYonu.Alinan ? CsvYazici.Metin(KonumAdi(c.Konum)) : "",
                CsvYazici.Metin(c.CiroEdilenCari));
        foreach (var yon in new[] { CekYonu.Alinan, CekYonu.Verilen })
        {
            var l = cekler.Where(c => c.Yon == yon).ToList();
            csv.Satir(CsvYazici.Metin($"Toplam {YonAdi(yon).ToLower(Metin.Tr)} ({EvrakAdedi(l)})"), "", "", "",
                CsvYazici.Sayi(l.Sum(c => c.Tutar)), "", "", "", "", "", "", "", "", "");
        }
        return csv.Baytlar();
    }

    /// <summary>Kasa sayımları (GET /api/kasasayimlari ile aynı: en yeni önce).</summary>
    public static byte[] KasaSayimlari(IReadOnlyList<KasaSayimDto> sayimlar)
    {
        var csv = new CsvYazici().Baslik("Tarih", "Sayılan", "Defterdeki (kayıt anında)", "Fark", "Bugünkü defter", "Not", "Kayıt zamanı");
        foreach (var s in sayimlar)
            csv.Satir(
                CsvYazici.Tarih(s.Tarih),
                CsvYazici.Sayi(s.SayilanTutar),
                CsvYazici.Sayi(s.HesaplananTutar),
                CsvYazici.Sayi(s.Fark),
                s.GuncelHesaplanan is { } g ? CsvYazici.Sayi(g) : "",
                CsvYazici.Metin(s.Not),
                CsvYazici.Metin(Saat.Simdi(s.KayitZamaniUtc).ToString("dd.MM.yyyy HH:mm", Metin.Tr)));
        return csv.Baytlar();
    }

    /// <summary>
    /// Değişiklik geçmişi (en yeni önce); zaman Türkiye saatiyle. Kişi ve cihaz (paket E: kim, hangi
    /// cihazdan) sonda: eski sütunların yeri değişmez; eski satırlarda boştur.
    /// </summary>
    public static byte[] Gecmis(IReadOnlyList<DegisiklikEntity> satirlar)
    {
        var csv = new CsvYazici().Baslik("Zaman", "Rol", "Tür", "Eylem", "Özet", "Geri alındı", "Kişi", "Cihaz");
        foreach (var d in satirlar)
            csv.Satir(
                CsvYazici.Metin(Saat.Simdi(d.ZamanUtc).ToString("dd.MM.yyyy HH:mm:ss", Metin.Tr)),
                CsvYazici.Metin(RolAdi(d.Rol)),
                CsvYazici.Metin(d.Tur),
                CsvYazici.Metin(d.Eylem),
                CsvYazici.Metin(d.Ozet),
                CsvYazici.Metin(d.GeriAlindi ? "Evet" : ""),
                CsvYazici.Metin(d.Kullanici),
                CsvYazici.Metin(d.Cihaz));
        return csv.Baytlar();
    }

    /// <summary>Gelenler (dönem başı, kanal artan) + toplam.</summary>
    public static byte[] Gelenler(IReadOnlyList<GelenEntity> gelenler)
    {
        var csv = new CsvYazici().Baslik("Dönem başı", "Kanal", "Tutar");
        foreach (var g in gelenler)
            csv.Satir(CsvYazici.Tarih(g.DonemStart), CsvYazici.Metin(g.Kanal), CsvYazici.Sayi(g.TutarTl));
        csv.Satir(CsvYazici.Metin($"Toplam ({gelenler.Count} satır)"), "", CsvYazici.Sayi(gelenler.Sum(g => g.TutarTl)));
        return csv.Baytlar();
    }

    /// <summary>Kart ödemeleri (tarih artan) + toplam.</summary>
    public static byte[] KartOdemeleri(IReadOnlyList<KartOdemeEntity> odemeler, IReadOnlyDictionary<int, string> kartAdlari)
    {
        var csv = new CsvYazici().Baslik("Tarih", "Kart", "Tutar", "Not");
        foreach (var o in odemeler)
            csv.Satir(CsvYazici.Tarih(o.Tarih), CsvYazici.Metin(kartAdlari.GetValueOrDefault(o.KrediKartiId)),
                CsvYazici.Sayi(o.Tutar), CsvYazici.Metin(o.Not));
        csv.Satir(CsvYazici.Metin($"Toplam ({odemeler.Count} ödeme)"), "", CsvYazici.Sayi(odemeler.Sum(o => o.Tutar)), "");
        return csv.Baytlar();
    }

    /// <summary>"Kasa neden değişti?": açılış, adımlar (işaretli tutar ve bakiye), kapanış.</summary>
    public static byte[] KasaDokumu(KasaDokumu? d)
    {
        var csv = new CsvYazici().Baslik("Adım", "Kanal", "Tutar", "Kasa");
        if (d is null)
        {
            csv.Satir(CsvYazici.Metin("Bu aralıkta takip dönemi yok"), "", "", "");
            return csv.Baytlar();
        }
        csv.Satir(CsvYazici.Metin($"Açılış ({CsvYazici.Tarih(d.Baslangic)})"), "", "", CsvYazici.Sayi(d.Acilis));
        foreach (var a in d.Adimlar)
            csv.Satir(CsvYazici.Metin(RaporServisi.TurAdi(a.Tur)), CsvYazici.Metin(a.Kanal), CsvYazici.Sayi(a.Tutar), CsvYazici.Sayi(a.Bakiye));
        csv.Satir(CsvYazici.Metin($"Kapanış ({CsvYazici.Tarih(d.Bitis)})"), "", CsvYazici.Sayi(d.Kapanis - d.Acilis), CsvYazici.Sayi(d.Kapanis));
        return csv.Baytlar();
    }
}
