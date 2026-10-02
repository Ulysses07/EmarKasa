namespace Kasa.Api.Data;

/// <summary>Alınan ya da verilen çek / senet (docs/specs/2026-10-01-cekler.md). Durum saklanmaz, hareketlerden hesaplanır
/// (<see cref="Kasa.Core.CekKurallari.Durum"/>). Kasayı yalnız hareketler etkiler (türetilmiş satır; HesapServisi).</summary>
public class CekEntity
{
    public int Id { get; set; }
    /// <summary><see cref="Kasa.Core.Kodlar.CekTurleri"/>.</summary>
    public string Tur { get; set; } = "";
    /// <summary><see cref="Kasa.Core.Kodlar.CekYonleri"/>.</summary>
    public string Yon { get; set; } = "";
    public string No { get; set; } = "";
    /// <summary>Çekte zorunlu, senette boş olabilir.</summary>
    public string? Banka { get; set; }
    /// <summary>Alınanda kimden (keşideci ya da ciro eden), verilende kime (lehtar).</summary>
    public string Kisi { get; set; } = "";
    public decimal Tutar { get; set; }
    public DateOnly VadeTarihi { get; set; }
    /// <summary>Verilen çekin ödeneceği kasa; verilende null Ortak'tır. Alınan çekte her zaman null (kasa harekette seçilir).</summary>
    public int? KanalId { get; set; }
    /// <summary>Teminat çeki: bildirim çıkmaz, panel toplamlarına girmez.</summary>
    public bool Teminat { get; set; }
    /// <summary>Portföydeki alınan çekin yeri (<see cref="Kasa.Core.Kodlar.CekKonumlari"/>); verilende null.</summary>
    public string? Konum { get; set; }
    public string? Not { get; set; }
    /// <summary>İyimser eşzamanlılık: çek ya da hareketleri her değiştiğinde bir artar.</summary>
    public int Surum { get; set; }
}

/// <summary>Çekin hareketi (tahsilat, ödeme, ciro, kırdırma, dönüş, karşılıksız, iade). <see cref="Sira"/> çekin kaçıncı
/// hareketi olduğudur; yalnız son hareket silinebilir (geri alma).</summary>
public class CekHareketEntity
{
    public int Id { get; set; }
    public int CekId { get; set; }
    public int Sira { get; set; }
    /// <summary><see cref="Kasa.Core.Kodlar.CekHareketTurleri"/>.</summary>
    public string Tur { get; set; } = "";
    /// <summary>Hareketin günü; ay kilidi bu tarihe bakar.</summary>
    public DateOnly Tarih { get; set; }
    /// <summary>Tahsilat, ödeme ve dönüşte tutar; ciroda kalanın tamamı; kırdırmada çek tutarı; karşılıksız ve iadede 0.</summary>
    public decimal Tutar { get; set; }
    /// <summary>Yalnız kırdırmada hesaba geçen tutar; masraf = Tutar − NetTutar.</summary>
    public decimal? NetTutar { get; set; }
    /// <summary>Kasayı etkileyen hareketin kasası. Alınanda seçilir (dönüşte ters çevrilen hareketinki); verilen çekin ödemesinde
    /// çekin kasasının kopyasıdır ve null Ortak'tır. Karşılıksız ve iadede null.</summary>
    public int? KanalId { get; set; }
    /// <summary>Ciroda ciro edilen kişi, kırdırmada banka ya da faktoring adı; dönüşte ters çevrilen hareketinki.</summary>
    public string? Karsi { get; set; }
}
