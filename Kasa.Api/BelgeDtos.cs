using Kasa.Api.Data;
using Kasa.Core;

namespace Kasa.Api;

/// <summary>İşlem ekinin bilgisi (dosyanın kendisi GET /api/ekler/{id} ile iner).</summary>
public record EkDto(int Id, int IslemId, string Ad, string IcerikTipi, long Boyut, DateTime YuklemeZamaniUtc);

/// <summary>PUT /api/islemler/{id}/belge: yalnız belge alanlarını günceller (tutar/tarih/cari dokunulmaz).</summary>
public record BelgeYazDto(BelgeTuru? BelgeTuru, string? BelgeNo, bool FaturaBekleniyor);

/// <summary>Fatura takibinde bir işlem satırı.</summary>
public record FaturaIslemDto(int Id, DateOnly Tarih, string Cari, decimal TutarTl, string Kanal, GiderTipi Tip,
    int? KrediKartiId, BelgeTuru? BelgeTuru, string? BelgeNo, bool FaturaBekleniyor, int EkSayisi, string? Not);

/// <summary>Faturası beklenen işlemler, cari cari (en eski bekleyen önce).</summary>
public record FaturaBekleyenCariDto(string Cari, decimal Toplam, int Adet, DateOnly EnEskiTarih, IReadOnlyList<FaturaIslemDto> Islemler);

/// <summary>Ayın belge türü toplamı; <see cref="Tur"/> null = belirtilmemiş.</summary>
public record BelgeTuruToplamDto(BelgeTuru? Tur, string Ad, decimal Toplam, int Adet);

/// <summary>
/// Fatura takibi: tüm tarihlerdeki "fatura bekleniyor" işlemleri (cari cari) ve seçilen ayın belge
/// türü dökümü. Tutarlar işlemlerin kendi tutarlarıdır; kasa/kârlılık hesabı yapılmaz.
/// </summary>
public record FaturaTakibiDto(int Yil, int Ay,
    IReadOnlyList<FaturaBekleyenCariDto> Bekleyenler, decimal BekleyenToplam, int BekleyenAdet,
    IReadOnlyList<BelgeTuruToplamDto> AyOzeti, decimal AyToplam, int AyAdet,
    decimal AyBelgesizToplam, int AyBelgesizAdet);
