using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

internal static class AlisOdemeIslemleri
{
    internal static IResult Duzelt(KasaDbContext db, int id, int odemeId, AlisOdemeDuzelt dto)
    {
        var v = new GirdiDogrulama(); v.Metin(dto.Aciklama, "aciklama", 2000); v.Tarih(dto.Tarih, "tarih"); v.Para(dto.Tutar, "tutar");
        v.Kontrol(dto.Tutar > 0, "tutar", "Ödeme pozitif olmalı.");
        v.Kontrol(dto.HesapId is null, "hesapId", "Ödeme doğrudan kanal ve genel kasaya kaydedilir; ayrı hesap seçilmez.");
        if (v.Sonuc() is { } invalid) return invalid;
        var digest = FinansHesaplari.Ozet(new { id, odemeId, dto.Tarih, tutar = dto.Tutar.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), dto.KrediKartiId, dto.HesapId, dto.HedefAlisId, aciklama = dto.Aciklama.Trim() });
        if (FinansHesaplari.Tekrar(db, dto.IstekId, "OdemeDuzelt", digest, key => Results.Ok(AlisEndpoints.ReadDto(db, key))) is { } replay) return replay;
        var source = AlisEndpoints.Query(db).SingleOrDefault(a => a.Id == id);
        var payment = source?.Odemeler.SingleOrDefault(o => o.Id == odemeId);
        if (source is null) return Results.NotFound();
        if (source.Surum != dto.Surum) return AlisEndpoints.Conflict("Alış değişmiş. Listeyi yenileyin.");
        if (payment is null) return Results.NotFound();
        if (FinansTakipServisi.IslemYonetiliyor(db, payment.Islem) || (dto.KrediKartiId is { } targetCard && db.TakipKartlar.Any(t => t.KrediKartiId == targetCard)))
            return AlisEndpoints.Conflict("Yeni kart takibine bağlı ödeme için Kredi Kartları ekranında açıklamalı iade girin; alışın kanal dağılımı ayrıca düzenlenebilir.");
        var target = source;
        if (dto.HedefAlisId is { } targetId && targetId != id)
        {
            target = AlisEndpoints.Query(db).SingleOrDefault(a => a.Id == targetId);
            if (target is null) return Results.NotFound();
            if (dto.HedefSurum != target.Surum) return AlisEndpoints.Conflict("Hedef alış değişmiş. Listeyi yenileyin.");
        }
        if (target.Odemeler.Where(o => o.Id != odemeId).Sum(o => o.Islem.TutarTl) + dto.Tutar > target.Kalemler.Sum(k => k.Tutar))
            return AlisEndpoints.Conflict("Düzeltilmiş ödeme hedef alış toplamını aşamaz.");
        v.Kart(db, dto.KrediKartiId);
        v.Kontrol(dto.Tarih >= db.Ayarlar.Select(a => a.TakipBaslangic).First(), "tarih", "Ödeme takip başlangıcından önce olamaz.");
        if (v.Sonuc() is { } invalidReference) return invalidReference;
        var before = JsonSerializer.Serialize(new { aciklama = dto.Aciklama.Trim(), alis = AlisHesaplari.ToDto(source) });
        var expense = payment.Islem;
        var eskiKartTipiniKoru = expense.Tip == GiderTipi.KrediKarti && expense.KrediKartiId is null && dto.KrediKartiId is null;
        expense.Tarih = dto.Tarih; expense.TutarTl = dto.Tutar; expense.KrediKartiId = dto.KrediKartiId;
        expense.Tip = dto.KrediKartiId is not null || eskiKartTipiniKoru ? GiderTipi.KrediKarti : GiderTipi.Cari;
        expense.Cari = target.Tedarikci; expense.Not = dto.Aciklama.Trim();
        // Önizleme sürümünden kalmış bir bağlantı varsa koru; yeni hesap bağlantısı oluşturma.
        if (expense.HesapHareketi is { } legacyAccount)
        { legacyAccount.Tarih = expense.Tarih; legacyAccount.Tutar = -expense.TutarTl; legacyAccount.Aciklama = expense.Not; }
        if (target.Id != source.Id)
        {
            source.Odemeler.Remove(payment); target.Odemeler.Add(payment); payment.AlisId = target.Id; target.Surum++;
            foreach (var attachment in db.Belgeler.Where(b => b.OdemeId == odemeId)) attachment.AlisId = target.Id;
        }
        source.Surum++;
        FinansHesaplari.IstekKaydet(db, dto.IstekId, "OdemeDuzelt", digest, id, before);
        db.SaveChanges();
        return Results.Ok(AlisEndpoints.ReadDto(db, id));
    }

    internal static IResult Iptal(KasaDbContext db, int id, int odemeId, AlisOdemeIptal dto)
    {
        var v = new GirdiDogrulama(); v.Metin(dto.Aciklama, "aciklama", 2000);
        if (v.Sonuc() is { } invalid) return invalid;
        var digest = FinansHesaplari.Ozet(new { id, odemeId, aciklama = dto.Aciklama.Trim() });
        if (FinansHesaplari.Tekrar(db, dto.IstekId, "OdemeIptal", digest, key => Results.Ok(AlisEndpoints.ReadDto(db, key))) is { } replay) return replay;
        var alis = AlisEndpoints.Query(db).SingleOrDefault(a => a.Id == id);
        var payment = alis?.Odemeler.SingleOrDefault(o => o.Id == odemeId);
        if (alis is null) return Results.NotFound();
        if (alis.Surum != dto.Surum) return AlisEndpoints.Conflict("Alış değişmiş. Listeyi yenileyin.");
        if (payment is null) return Results.NotFound();
        if (FinansTakipServisi.IslemYonetiliyor(db, payment.Islem)) return AlisEndpoints.Conflict("Yeni kart takibine bağlı ödeme silinemez; Kredi Kartları ekranında açıklamalı iade girin.");
        var before = JsonSerializer.Serialize(new { aciklama = dto.Aciklama.Trim(), alis = AlisHesaplari.ToDto(alis) });
        if (payment.Islem.HesapHareketi is { } account) db.HesapHareketler.Remove(account);
        db.AlisOdemeler.Remove(payment); db.Islemler.Remove(payment.Islem);
        alis.Surum++;
        FinansHesaplari.IstekKaydet(db, dto.IstekId, "OdemeIptal", digest, id, before);
        db.SaveChanges();
        return Results.Ok(AlisEndpoints.ReadDto(db, id));
    }
}
