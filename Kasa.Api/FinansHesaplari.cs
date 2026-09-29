using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

public static class FinansHesaplari
{
    public static string Ozet(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static void IstekKaydet(KasaDbContext db, Guid id, string tur, string ozet, int sonucId, string? onceki = null) =>
        db.FinansIstekler.Add(new FinansIstekEntity { IstekId = id, Tur = tur, Ozet = ozet, SonucId = sonucId, OncekiJson = onceki });

    public static IResult? Tekrar(KasaDbContext db, Guid id, string tur, string ozet, Func<int, IResult> result)
    {
        if (id == Guid.Empty) return Results.ValidationProblem(new Dictionary<string, string[]> { ["istekId"] = ["Geçerli bir istek kimliği gerekir."] });
        var old = db.FinansIstekler.AsNoTracking().SingleOrDefault(x => x.IstekId == id);
        return old is null ? null : old.Tur == tur && old.Ozet == ozet ? result(old.SonucId) : AlisEndpoints.Conflict("İstek kimliği başka bir işlem veya farklı içerik için kullanılmış.");
    }

    /// <summary>
    /// Takip başlangıcına bağlı ilk mali kaydın türü; hiç yoksa null (gap-veri-degismezleri-patlama-yaricapi-5). Başlangıç
    /// dönemleri belirler: kayıt varken değişirse başlangıçtan önceye düşen kayıt raporlardan sessizce çıkar, sonrakiler
    /// başka döneme kayar. Gider (Islem) üretmeyen yollar da sayılır: takipli kart açılışı/harcaması/ödemesi, takipli kredi,
    /// iptal edilmemiş ekstre kaydı (banka geliri dahil), aylık gider ödemesi, kasa sayımı ve alış ödemesi.
    /// </summary>
    public static string? IlkMaliKayitTuru(KasaDbContext db) =>
        db.Islemler.Any() ? "gider"
        : db.Gelenler.Any() ? "dönem geliri"
        : db.Krediler.Any() || db.TakipKrediler.Any() ? "kredi"
        : db.HesapHareketler.Any() || db.HesapTransferler.Any() ? "hesap hareketi"
        : db.KartOdemeler.Any() ? "eski kart ödemesi"
        : db.TakipKartlar.Any() || db.TakipHarcamalar.Any() || db.TakipKartOdemeler.Any() ? "takipli kart, kart harcaması veya kart ödemesi"
        : db.EkstreKayitlar.Any(k => !k.Iptal) ? "ekstre kaydı (banka geliri dahil)"
        : db.AylikGiderOdemeler.Any(p => !p.Iptal) ? "aylık gider ödemesi"
        : db.KasaKontrolleri.Any() ? "kasa sayımı"
        : db.AlisOdemeler.Any() ? "alış ödemesi"
        : null;

    /// <summary>finance-9 iletisi: takipteki karta genel gider ekranından sıfır/eksi tutar (iade, alacak) girilemez.</summary>
    public const string TakipliKartIadeYolu = "Takipteki karta genel gider ekranından iade veya alacak girilemez; tutar sıfırdan büyük olmalı. İade için Kredi Kartları ekranında kaynak harcamayı seçerek iade girin.";

    /// <summary>
    /// Yeni genel giderin (POST /api/islemler) takipli kart denetimi (finance-9): takipteki karttaki sıfır/eksi gider
    /// kaynaksız alacak olur, herhangi bir kanalın taksidini kapatırdı (iade akışının kaynak, kanal oranı ve ödenmiş kısım
    /// korumaları atlanır). Alan doğrulamasıyla aynı biçimde 400 (<c>tutarTl</c>) ya da null. Takipsiz karta ya da kartsız
    /// eksi gider değişmez; düzenlemede (PUT) takipli kart zaten 409 alır.
    /// </summary>
    public static IResult? TakipliKartIadeHatasi(IslemYazDto dto, KasaDbContext db) =>
        dto.TutarTl <= 0 && dto.KrediKartiId is { } kartId && db.TakipKartlar.Any(k => k.KrediKartiId == kartId)
            ? Results.ValidationProblem(new Dictionary<string, string[]> { ["tutarTl"] = [TakipliKartIadeYolu] })
            : null;

    /// <summary>
    /// Eski (takipsiz) kredinin geçmiş kasa etkisi var mı (gap-tarihsel-spec-ve-emekli-web-6). Çekimi ve taksitleri kayıttan
    /// bellekte türetilir (<see cref="KrediTuretici"/>); taksitler çekimden kesin sonra olduğundan çekim bugün ya da daha
    /// önceyse kredi raporlara girmiştir. Böyle kredi silinir ya da mali alanı değişirse geçmiş raporlar kilitsiz ve izsiz
    /// yeniden yazılır.
    /// </summary>
    public static bool EskiKrediGecmisEtkili(KrediEntity kredi, DateOnly bugun) => kredi.CekimTarihi <= bugun;

    /// <summary>Eski kredi düzeltmesinde geçmişi koruma kuralı: geçmiş etkili kredide yalnız ad değişebilir; geçmiş etkisi
    /// olmayan kredi geçmişe taşınamaz. İzin verilen düzeltmede null, aksi halde 409 iletisi.</summary>
    public static string? EskiKrediDuzeltmeHatasi(KasaDbContext db, KrediEntity mevcut, KrediEntity gelen, DateOnly bugun)
    {
        if (EskiKrediGecmisEtkili(mevcut, bugun))
            return mevcut.CekilenTutar != gelen.CekilenTutar || mevcut.CekimTarihi != gelen.CekimTarihi || mevcut.TaksitSayisi != gelen.TaksitSayisi
                || mevcut.AylikOdeme != gelen.AylikOdeme || mevcut.OdemeGunu != gelen.OdemeGunu || KanalAdi(db, mevcut) != KanalAdi(db, gelen)
                ? "Geçmiş kasa etkisi olan eski kredinin tutar, tarih, taksit veya kanal bilgisi değiştirilemez; yalnız ad düzeltilebilir. Geçmiş raporlar korunur."
                : null;
        return EskiKrediGecmisEtkili(gelen, bugun)
            ? "Eski kredi geçmiş tarihe taşınamaz; geçmiş raporlar değişirdi. Yeni krediyi Krediler ekranından oluşturun."
            : null;
    }

    /// <summary>Raporların kullandığı kanal adı (<c>KanalKaydi?.Ad ?? Kanal</c>); kanal adları tekildir.</summary>
    private static string KanalAdi(KasaDbContext db, KrediEntity kredi) =>
        kredi.KanalId is { } id ? db.Kanallar.AsNoTracking().Where(k => k.Id == id).Select(k => k.Ad).FirstOrDefault() ?? kredi.Kanal : kredi.Kanal;

    public static IReadOnlyList<Gelen> EkGelirler(KasaDbContext db, IReadOnlyList<Donem> donemler) => db.HesapHareketler.AsNoTracking().Include(h => h.Kanal)
        .Where(h => h.IslemId == null && h.GelenId == null && h.KartOdemeId == null && h.KrediId == null).ToList()
        .Select(h => (Hareket: h, Donem: donemler.FirstOrDefault(d => d.Icerir(h.Tarih))))
        .Where(x => x.Donem is not null).Select(x => new Gelen(x.Donem!.Start, x.Hareket.Kanal!.Ad, x.Hareket.Tutar)).ToList();
}
