using System.Globalization;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api;

internal static class AlisOdemeIslemleri
{
    private const int VarsayilanSayfa = 50;
    private const int EnBuyukSayfa = 200;

    /// <summary>
    /// GET /api/alis/baglanabilir-giderler (webui-6, gap-okuma-yolu-maliyet-kilit-cekismesi-12): alış ödemesine bağlanabilecek
    /// mevcut giderler. Ödeme diyaloğu bütün gider geçmişini (GET /api/islemler) çekip istemcide süzmez: sunucu yalnız
    /// <see cref="BaglanabilirSorgu"/> kurallarına uyan giderleri tarih/tutar/metin süzgeciyle, tarih ve kimlik azalan
    /// imleçli sayfalarla ({ogeler, sonrakiImlec, devamVar}) döndürür. Okuma anlık görüntüsünde çalışır (yazma kilidi almaz);
    /// sorgu sayısı kayıt sayısından bağımsızdır.
    /// </summary>
    internal static WebApplication MapBaglanabilirGiderler(this WebApplication app)
    {
        app.MapGet("/api/alis/baglanabilir-giderler", (DateOnly? baslangic, DateOnly? bitis, string? arama, string? tutar, string? imlec, int? limit, KasaDbContext db)
            => AlisEndpoints.Oku(db, () => BaglanabilirGiderler(db, baslangic, bitis, arama, tutar, imlec, limit ?? VarsayilanSayfa)))
            .RequireAuthorization("Editor");
        return app;
    }

    private static IResult BaglanabilirGiderler(KasaDbContext db, DateOnly? baslangic, DateOnly? bitis, string? arama, string? tutarMetni, string? imlec, int limit)
    {
        static IResult Hata(string ileti) => Results.BadRequest(new { hata = ileti });
        if (limit is < 1 or > EnBuyukSayfa) return Hata($"Sayfa boyutu 1 ile {EnBuyukSayfa} arasında olmalı.");
        if (baslangic > bitis) return Hata("Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        arama = arama?.Trim();
        if (arama?.Length > 200) return Hata("Arama metni en fazla 200 karakter olabilir.");
        decimal? tutar = null;
        if (!string.IsNullOrWhiteSpace(tutarMetni))
        {
            if (!decimal.TryParse(tutarMetni.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var t) || t <= 0 || decimal.Round(t, 2) != t)
                return Hata("Tutarı sıfırdan büyük ve en çok iki ondalıkla yazın (ör. 1250,50).");
            tutar = t;
        }
        (DateOnly Tarih, int Id)? sonraki = null;
        if (!string.IsNullOrEmpty(imlec))
        {
            if (ImleciOku(imlec) is not { } okunan) return Hata("Sayfa imleci geçersiz; listeyi yeniden açın.");
            sonraki = okunan;
        }

        var q = BaglanabilirSorgu(db, db.Ayarlar.Select(a => a.TakipBaslangic).First());
        if (db.AyKilidi.Select(k => k.KilitliSonTarih).Single() is { } kilitSonu) q = q.Where(i => i.Tarih > kilitSonu);
        if (baslangic is { } ilk) q = q.Where(i => i.Tarih >= ilk);
        if (bitis is { } son) q = q.Where(i => i.Tarih <= son);
        if (tutar is { } aranan) q = q.Where(i => i.TutarTl == aranan);
        if (!string.IsNullOrEmpty(arama))
        {
            var desen = "%" + arama.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            q = q.Where(i => EF.Functions.Like(i.Cari, desen, "\\") || i.Not != null && EF.Functions.Like(i.Not, desen, "\\"));
        }
        if (sonraki is { } s) q = q.Where(i => i.Tarih < s.Tarih || i.Tarih == s.Tarih && i.Id < s.Id);
        var ogeler = q.OrderByDescending(i => i.Tarih).ThenByDescending(i => i.Id).Take(limit + 1)
            .Select(i => new BaglanabilirGiderDto(i.Id, i.Tarih, i.Cari, i.TutarTl, i.Kanal, i.KanalId, i.Tip, i.Not, i.KrediKartiId))
            .ToList();
        var devamVar = ogeler.Count > limit;
        if (devamVar) ogeler.RemoveAt(limit);
        return Results.Ok(new BaglanabilirGiderSayfasi(ogeler, devamVar ? $"{ogeler[^1].Tarih:yyyyMMdd}-{ogeler[^1].Id}" : null, devamVar));
    }

    private static (DateOnly, int)? ImleciOku(string imlec)
    {
        var parcalar = imlec.Split('-');
        return parcalar.Length == 2 && DateOnly.TryParseExact(parcalar[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tarih)
            && int.TryParse(parcalar[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? (tarih, id) : null;
    }

    /// <summary>
    /// Ödeme ucunun (AlisEndpoints Pay, mevcut gider bağlama) ve yazma kurallarının kabul edeceği giderler: cari ya da kredi
    /// kartı tipi, pozitif tutar, takip başlangıcından sonra; başka alışa, krediye (taksit ödemesi), aylık gidere, ekstre
    /// kaydına ve eski hesap hareketine bağlı değil (iptal edilmiş aylık gider ve ekstre bağı da bağlamayı engeller); kartlıysa
    /// kart kayıtlı, takipteki kart yeni kullanıma açık ve gider kart takip başlangıcından önce değil. Kilitli dönem ve kullanıcı
    /// süzgeçleri çağıranda eklenir.
    /// </summary>
    internal static IQueryable<IslemEntity> BaglanabilirSorgu(KasaDbContext db, DateOnly takipBaslangic) => db.Islemler.AsNoTracking().Where(i =>
        (i.Tip == GiderTipi.Cari || i.Tip == GiderTipi.KrediKarti) && i.TutarTl > 0 && i.Tarih >= takipBaslangic
        && !db.AlisOdemeler.Any(o => o.IslemId == i.Id) && !db.KrediTaksitOdemeler.Any(o => o.IslemId == i.Id)
        && !db.AylikGiderOdemeler.Any(p => p.IslemId == i.Id) && !db.EkstreKayitlar.Any(k => k.IslemId == i.Id)
        && !db.HesapHareketler.Any(h => h.IslemId == i.Id)
        && (i.KrediKartiId == null || db.KrediKartlari.Any(k => k.Id == i.KrediKartiId)
            && !db.TakipKartlar.Any(t => t.KrediKartiId == i.KrediKartiId && (!t.Aktif || i.Tarih < t.Baslangic))));

    /// <summary>
    /// Ödeme taşıma kilidi (purchase-2): onaylı alışta ödeme payları Id sırasıyla kümülatif dağıtılır. Ödeme başka alışa
    /// taşınınca KAYNAKTA ondan sonra girilmiş ödemelerin, HEDEFTE de taşınan ödemenin Id'sinden büyük ödemelerin payı yeniden
    /// hesaplanır. Bunlardan biri kilitli dönemdeyse (tarihi kilit sınırında ya da kart harcaması kilitli dönemde ödenmiş)
    /// taşıma kilitli ayın kanal sonuçlarını sessizce değiştirirdi: kilit sınırı döner. Saf taşımada (tarih, tutar, kart aynı)
    /// genel kilit kuralı yalnız hedef alışı gördüğü için kaynak burada açıkça denetlenir.
    /// </summary>
    private static DateOnly? TasimaKilidi(KasaDbContext db, AlisEntity kaynak, AlisEntity hedef, int odemeId)
    {
        if (db.AyKilidi.AsNoTracking().Select(k => k.KilitliSonTarih).Single() is not { } son) return null;
        foreach (var alis in new[] { kaynak, hedef })
            if (alis.Durum == AlisDurumlari.Onaylandi && alis.Odemeler.Any(o => o.Id > odemeId && (o.Islem.Tarih <= son || KartOdemesiKilitli(db, o.IslemId, son))))
                return son;
        return null;
    }

    /// <summary>Gider takipteki bir kartın harcamasıysa, taksitlerinden biri kilitli dönemde (iptal edilmemiş) bir kart ödemesiyle ödendi mi.</summary>
    private static bool KartOdemesiKilitli(KasaDbContext db, int islemId, DateOnly son)
    {
        var harcamalar = db.TakipHarcamalar.Where(h => h.IslemId == islemId).Select(h => h.Id).ToList();
        if (harcamalar.Count == 0) return false;
        var taksitler = db.TakipKartTaksitler.Where(t => harcamalar.Contains(t.HarcamaId)).Select(t => t.Id).ToHashSet();
        return db.TakipKartOdemeler.AsNoTracking().Where(p => !p.Iptal && p.Tarih <= son).AsEnumerable()
            .Any(p => FinansTakipServisi.Read<KartTaksitPayi>(p.PaylarJson).Any(x => taksitler.Contains(x.TaksitId)));
    }

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
        // K3: ödemeyi yeni bir karta bağlamak yeni kredi kartı gideridir; aynı kartla tutar/tarih düzeltmesi serbesttir.
        KayitGirdileri.TakipliKartKurali(v, db, dto.KrediKartiId is null ? GiderTipi.Cari : GiderTipi.KrediKarti, dto.KrediKartiId, payment.Islem);
        v.Kontrol(dto.Tarih >= db.Ayarlar.Select(a => a.TakipBaslangic).First(), "tarih", "Ödeme takip başlangıcından önce olamaz.");
        if (v.Sonuc() is { } invalidReference) return invalidReference;
        if (target.Id != source.Id && TasimaKilidi(db, source, target, odemeId) is { } kilitSonu)
            return AlisEndpoints.Conflict($"{kilitSonu:yyyy-MM-dd} tarihine kadar dönem kilitli. Bu ödemeyi taşımak, kaynak ya da hedef alışta sonradan girilmiş kilitli dönem ödemelerinin kanal paylarını değiştirir; ilgili ayı gerekçeyle açın.");
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
