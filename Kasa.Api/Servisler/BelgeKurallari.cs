using Kasa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>
/// İşlemin belge alanlarının (belge türü, belge no, fatura bekleniyor) doğrulaması ve
/// Program.cs'teki işlem uç noktalarına tek satırla bağlanan kancalar. Para hesabına dokunmaz.
/// </summary>
public static class BelgeKurallari
{
    public const int BelgeNoEnFazla = 50;
    public const string BelgesizBekleniyorMesaji =
        "Belgesiz bir ödeme için fatura beklenemez. Fatura bekleniyorsa belge türünü boş bırakın; fatura gelince türünü seçin.";

    /// <summary>
    /// Belge alanlarını doğrular ve belge no'yu kırpar (boşsa null). Hata yoksa null.
    /// İşlem doğrulamasının (IslemHatasi) parçası olarak POST, PUT ve geri almada çalışır.
    /// </summary>
    public static string? Hata(IslemEntity e)
    {
        if (e.BelgeTuru is { } t && !Enum.IsDefined(t)) return "Geçersiz belge türü.";
        if (e.BelgeNo is { } no)
        {
            var k = no.Trim();
            if (k.Length > BelgeNoEnFazla) return $"Belge no en fazla {BelgeNoEnFazla} karakter olabilir.";
            if (k.Any(char.IsControl)) return "Belge no geçersiz karakter içeriyor.";
            e.BelgeNo = k.Length == 0 ? null : k;
        }
        // Çelişkili durum: ay sonu belgesiz toplamında ve fatura bekleyenlerde aynı anda görünürdü.
        if (e.BelgeTuru == BelgeTuru.Belgesiz && e.FaturaBekleniyor) return BelgesizBekleniyorMesaji;
        return null;
    }

    /// <summary>
    /// PUT: yalnız gövdede GELEN belge alanlarını kaydedilen işleme kopyalar. Belge alanlarını
    /// bilmeyen eski istemcinin düzeltmesi var olan belge bilgisini silmez.
    /// </summary>
    public static void Kopyala(IslemEntity gelen, IslemEntity hedef)
    {
        var f = gelen.GelenBelgeAlanlari;
        if (f.HasFlag(BelgeAlanlari.Tur)) hedef.BelgeTuru = gelen.BelgeTuru;
        if (f.HasFlag(BelgeAlanlari.No)) hedef.BelgeNo = gelen.BelgeNo;
        if (f.HasFlag(BelgeAlanlari.Bekleniyor)) hedef.FaturaBekleniyor = gelen.FaturaBekleniyor;
    }

    /// <summary>İşlem listesindeki kayıtlara ek sayılarını yazar (GET /islemler; liste satırında "Ekler (n)").</summary>
    public static void EkSayilariniYaz(KasaDbContext db, IReadOnlyList<IslemEntity> islemler)
    {
        if (islemler.Count == 0) return;
        var sayilar = Endpoints.FaturaTakibi.EkSayilari(db, islemler.Select(i => i.Id));
        foreach (var i in islemler) i.EkSayisi = sayilar.GetValueOrDefault(i.Id);
    }

    /// <summary>
    /// İşlem silinirken (aynı transaction'da, SaveChanges'ten önce) eklerini işlemden ayırır: ekler 30 gün
    /// (geri alma süresi) <see cref="IslemEkiEntity.SilinenIslemId"/> ile bekler, <see cref="IslemEkiEntity.IslemId"/>
    /// 0 olur. Böylece aynı Id bir gün yeniden verilse bile yeni işlem eski işlemin eklerini devralmaz.
    /// Tek bir geçmiş satırı yazılır (sonraki SaveChanges ile). Çağıran transaction açmış olmalıdır.
    /// </summary>
    public static void IslemSiliniyor(KasaDbContext db, int islemId)
    {
        var simdi = db.SimdiUtc;
        var adet = db.IslemEkleri.Where(x => x.IslemId == islemId)
            .ExecuteUpdate(s => s
                .SetProperty(x => x.IslemId, IslemEkiEntity.IslemsizId)
                .SetProperty(x => x.SilinenIslemId, (int?)islemId)
                .SetProperty(x => x.SilinmeZamaniUtc, (DateTime?)simdi));
        if (adet == 0) return;
        db.TopluDegisiklikEkle(GecmisTurleri.IslemEki, islemId,
            $"{adet} ek, silinen işlemle birlikte {GecmisKurallari.GeriAlmaSuresi.TotalDays:0} gün saklanacak (işlem geri alınırsa ona bağlanır)",
            eski: new { islemId }, yeni: new { silinenIslemId = islemId, adet });
    }

    /// <summary>
    /// Silinen işlem geçmişten geri alındıktan sonra (yeni Id'yle) eski işlemin eklerini yeni işleme
    /// bağlar. Geri alma transaction'ı içinde, geri alınan kayıt kaydedildikten sonra çağrılır.
    /// Ekler <see cref="IslemEkiEntity.SilinenIslemId"/> ile bulunur; başka bir yoldan silinmiş işlemin
    /// ekleri (IslemId hâlâ eski Id) yalnız o Id'de bugün bir işlem YOKSA bağlanır.
    /// </summary>
    public static void GeriAlinanIslemeBagla(KasaDbContext db, DegisiklikEntity d, object? yeni)
    {
        if (d.Tur != GecmisTurleri.Islem || d.KayitId is not int eskiId || yeni is not IslemEntity islem || islem.Id == 0) return;
        var adet = db.IslemEkleri.Where(x => x.IslemId == IslemEkiEntity.IslemsizId && x.SilinenIslemId == eskiId)
            .ExecuteUpdate(s => s
                .SetProperty(x => x.IslemId, islem.Id)
                .SetProperty(x => x.SilinenIslemId, (int?)null)
                .SetProperty(x => x.SilinmeZamaniUtc, (DateTime?)null));
        if (eskiId != IslemEkiEntity.IslemsizId && !db.Islemler.Any(i => i.Id == eskiId))
            adet += db.IslemEkleri.Where(x => x.IslemId == eskiId)
                .ExecuteUpdate(s => s.SetProperty(x => x.IslemId, islem.Id));
        if (adet == 0) return;
        db.TopluDegisiklikEkle(GecmisTurleri.IslemEki, islem.Id, $"{adet} ek geri alınan işleme bağlandı",
            eski: new { islemId = eskiId }, yeni: new { islemId = islem.Id, adet });
        db.SaveChanges();
    }
}
