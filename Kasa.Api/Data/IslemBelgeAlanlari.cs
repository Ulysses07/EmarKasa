using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Kasa.Api.Data;

/// <summary>
/// İşlemin belge (fatura/fiş) bilgisi. Üç alan da isteğe bağlıdır: boş bırakılan işlem bugünkü
/// gibi çalışır. Para hesabına (kasa, devir, kârlılık) hiçbir etkisi yoktur; bu yüzden kilitli
/// aydaki işlemde de değiştirilebilir (<see cref="AyKilidiDisiAttribute"/>).
/// </summary>
/// <remarks>
/// Eski istemciler bu alanları göndermez. PUT'ta yalnız gövdede GELEN alanlar kopyalanır
/// (<see cref="GelenBelgeAlanlari"/>): eski sürüm uygulamayla yapılan bir düzeltme belge bilgisini silmez.
/// </remarks>
public partial class IslemEntity
{
    private BelgeTuru? _belgeTuru;
    private string? _belgeNo;
    private bool _faturaBekleniyor;

    /// <summary>Belge türü; null = belirtilmemiş.</summary>
    [AyKilidiDisi]
    public BelgeTuru? BelgeTuru
    {
        get => _belgeTuru;
        set { _belgeTuru = value; GelenBelgeAlanlari |= BelgeAlanlari.Tur; }
    }

    /// <summary>Fatura / fiş / makbuz numarası (kırpılır, boşsa null; en fazla 50 karakter).</summary>
    [AyKilidiDisi]
    public string? BelgeNo
    {
        get => _belgeNo;
        set { _belgeNo = value; GelenBelgeAlanlari |= BelgeAlanlari.No; }
    }

    /// <summary>Ödeme yapıldı, faturası henüz gelmedi ("fatura bekleniyor").</summary>
    [AyKilidiDisi]
    public bool FaturaBekleniyor
    {
        get => _faturaBekleniyor;
        set { _faturaBekleniyor = value; GelenBelgeAlanlari |= BelgeAlanlari.Bekleniyor; }
    }

    /// <summary>
    /// İşlemin ek (fiş/fatura fotoğrafı, PDF) sayısı. Veritabanında yoktur; yalnız işlem listesinde
    /// (GET /islemler) doldurulur ki liste satırı "Ekler (n)" gösterebilsin. 0 ise JSON'a yazılmaz;
    /// setter internal olduğundan istek gövdesinden okunmaz (istemci ek sayısı uyduramaz).
    /// </summary>
    [NotMapped, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int EkSayisi { get; internal set; }

    /// <summary>JSON gövdesinde hangi belge alanlarının geldiği (EF ve JSON'a yazılmaz).</summary>
    [NotMapped, JsonIgnore]
    public BelgeAlanlari GelenBelgeAlanlari { get; private set; }
}

[Flags]
public enum BelgeAlanlari
{
    Yok = 0,
    Tur = 1,
    No = 2,
    Bekleniyor = 4,
}
