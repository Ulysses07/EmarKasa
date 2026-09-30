using System.Globalization;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Core.Kodlar;
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
    /// sorgu sayısı kayıt sayısından bağımsızdır. Süzgeçler: <c>arama</c> açıklama (cari) ya da notta geçen metin;
    /// <c>aramaTutari</c> aynı arama kutusunun tutar okumasıdır ve metne VEYA ile eklenir ('2024' hem sipariş numarası hem tutar
    /// olabilir; tutar okuması yerel biçimi bilen istemcide yapılır); <c>tutar</c> ise kesin tutar süzgecidir (VE).
    /// </summary>
    internal static WebApplication MapBaglanabilirGiderler(this WebApplication app)
    {
        app.MapGet("/api/alis/baglanabilir-giderler", (DateOnly? baslangic, DateOnly? bitis, string? arama, string? aramaTutari, string? tutar, string? imlec, int? limit, KasaDbContext db)
            => AlisEndpoints.Oku(db, () => BaglanabilirGiderler(db, baslangic, bitis, arama, aramaTutari, tutar, imlec, limit ?? VarsayilanSayfa)))
            .RequireAuthorization("Editor");
        app.MapGet("/api/alis/baglanabilir-kart-harcamalari", (int? krediKartiId, string? tutar, KasaDbContext db)
            => AlisEndpoints.Oku(db, () => BaglanabilirKartHarcamalari(db, krediKartiId, tutar)))
            .RequireAuthorization("Editor");
        return app;
    }

    /// <summary>
    /// GET /api/alis/baglanabilir-kart-harcamalari (gap-coklu-giris-cift-sayim-mutabakat-1, ters sıra): takipli kartla ödemede
    /// <c>MevcutKartHarcamaId</c> ile bağlanabilecek harcamalar. Pay'in kabul ettiği kural: kartın iptal edilmemiş, gidere bağlı
    /// olmayan, pozitif, iade ya da eski borç devri olmayan ve iadesi bulunmayan harcaması; kilitli dönemden sonra. Tarih ve kimlik
    /// azalan en çok <see cref="VarsayilanSayfa"/> kayıt; <c>tutar</c> verilirse yalnız o tutar. Okuma anlık görüntüsünde çalışır.
    /// </summary>
    private static IResult BaglanabilirKartHarcamalari(KasaDbContext db, int? krediKartiId, string? tutarMetni)
    {
        if (krediKartiId is not > 0 || !db.TakipKartlar.Any(t => t.KrediKartiId == krediKartiId))
            return Results.BadRequest(new { hata = "Takipteki bir kart seçin." });
        if (!TutarOku(tutarMetni, out var tutar))
            return Results.BadRequest(new { hata = "Tutarı sıfırdan büyük ve en çok iki ondalıkla yazın (ör. 1250,50)." });
        var q = db.TakipHarcamalar.AsNoTracking().Where(h => h.KrediKartiId == krediKartiId && !h.Iptal && h.IslemId == null && h.KaynakHarcamaId == null
            && h.Tutar > 0 && h.KasadaOncedenSayilanTutar <= 0 && h.Aciklama != KartGecisHesabi.DevirAciklamasi
            && !db.TakipHarcamalar.Any(i => i.KaynakHarcamaId == h.Id && !i.Iptal));
        if (db.AyKilidi.Select(k => k.KilitliSonTarih).Single() is { } kilitSonu)
            q = q.Where(h => h.Tarih > kilitSonu);
        if (tutar is { } aranan)
            q = q.Where(h => h.Tutar == aranan);
        var harcamalar = q.OrderByDescending(h => h.Tarih).ThenByDescending(h => h.Id).Take(VarsayilanSayfa).ToList();
        var ids = harcamalar.Select(h => h.Id).ToArray();
        var ekstre = db.EkstreKayitlar.AsNoTracking().Where(k => k.KartHarcamaId != null && ids.Contains(k.KartHarcamaId.Value) && !k.Iptal)
            .Select(k => new { k.Id, Harcama = k.KartHarcamaId!.Value }).ToList().ToDictionary(k => k.Harcama, k => k.Id);
        return Results.Ok(harcamalar.Select(h => new BaglanabilirKartHarcamasiDto(h.Id, h.KrediKartiId, h.Tarih, h.Aciklama, h.Tutar,
            ekstre.TryGetValue(h.Id, out var kayit) ? kayit : null)).ToList());
    }

    private static IResult BaglanabilirGiderler(KasaDbContext db, DateOnly? baslangic, DateOnly? bitis, string? arama, string? aramaTutariMetni, string? tutarMetni, string? imlec, int limit)
    {
        static IResult Hata(string ileti) => Results.BadRequest(new { hata = ileti });
        if (limit is < 1 or > EnBuyukSayfa)
            return Hata($"Sayfa boyutu 1 ile {EnBuyukSayfa} arasında olmalı.");
        if (baslangic > bitis)
            return Hata("Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        arama = arama?.Trim();
        if (arama?.Length > 200)
            return Hata("Arama metni en fazla 200 karakter olabilir.");
        if (!TutarOku(tutarMetni, out var tutar) || !TutarOku(aramaTutariMetni, out var aramaTutari))
            return Hata("Tutarı sıfırdan büyük ve en çok iki ondalıkla yazın (ör. 1250,50).");
        (DateOnly Tarih, int Id)? sonraki = null;
        if (!string.IsNullOrEmpty(imlec))
        {
            if (ImleciOku(imlec) is not { } okunan)
                return Hata("Sayfa imleci geçersiz; listeyi yeniden açın.");
            sonraki = okunan;
        }

        var q = BaglanabilirSorgu(db, db.Ayarlar.Select(a => a.TakipBaslangic).First());
        if (db.AyKilidi.Select(k => k.KilitliSonTarih).Single() is { } kilitSonu)
            q = q.Where(i => i.Tarih > kilitSonu);
        if (baslangic is { } ilk)
            q = q.Where(i => i.Tarih >= ilk);
        if (bitis is { } son)
            q = q.Where(i => i.Tarih <= son);
        if (tutar is { } aranan)
            q = q.Where(i => i.TutarTl == aranan);
        if (!string.IsNullOrEmpty(arama))
        {
            var desen = "%" + arama.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            q = aramaTutari is { } metinTutari
                ? q.Where(i => EF.Functions.Like(i.Cari, desen, "\\") || i.Not != null && EF.Functions.Like(i.Not, desen, "\\") || i.TutarTl == metinTutari)
                : q.Where(i => EF.Functions.Like(i.Cari, desen, "\\") || i.Not != null && EF.Functions.Like(i.Not, desen, "\\"));
        }
        else if (aramaTutari is { } yalnizTutar)
            q = q.Where(i => i.TutarTl == yalnizTutar);
        if (sonraki is { } s)
            q = q.Where(i => i.Tarih < s.Tarih || i.Tarih == s.Tarih && i.Id < s.Id);
        var ogeler = q.OrderByDescending(i => i.Tarih).ThenByDescending(i => i.Id).Take(limit + 1)
            .Select(i => new BaglanabilirGiderDto(i.Id, i.Tarih, i.Cari, i.TutarTl, i.Kanal, i.KanalId, i.Tip, i.Not, i.KrediKartiId,
                db.EkstreKayitlar.Where(k => k.IslemId == i.Id && !k.Iptal).Select(k => (int?)k.Id).FirstOrDefault()))
            .ToList();
        var devamVar = ogeler.Count > limit;
        if (devamVar)
            ogeler.RemoveAt(limit);
        return Results.Ok(new BaglanabilirGiderSayfasi(ogeler, devamVar ? $"{ogeler[^1].Tarih:yyyyMMdd}-{ogeler[^1].Id}" : null, devamVar));
    }

    /// <summary>Boş metin süzgeç yok demektir (true, null); dolu metin sıfırdan büyük, en çok iki ondalıklı tutar olmalıdır.</summary>
    private static bool TutarOku(string? metin, out decimal? tutar)
    {
        tutar = null;
        if (string.IsNullOrWhiteSpace(metin))
            return true;
        if (!decimal.TryParse(metin.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var t) || t <= 0 || decimal.Round(t, 2) != t)
            return false;
        tutar = t;
        return true;
    }

    private static (DateOnly, int)? ImleciOku(string imlec)
    {
        var parcalar = imlec.Split('-');
        return parcalar.Length == 2 && DateOnly.TryParseExact(parcalar[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tarih)
            && int.TryParse(parcalar[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? (tarih, id) : null;
    }

    /// <summary>
    /// Ödeme ucunun (AlisEndpoints Pay, mevcut gider bağlama) ve yazma kurallarının kabul edeceği giderler: cari ya da kredi
    /// kartı tipi, pozitif tutar, takip başlangıcından sonra; başka alışa, krediye (taksit ödemesi), aylık gidere ve eski hesap
    /// hareketine bağlı değil (iptal edilmiş aylık gider bağı da bağlamayı engeller); kartlıysa kart kayıtlı, takipteki kart yeni
    /// kullanıma açık ve gider kart takip başlangıcından önce değil. Banka ekstresinden gelen gider bağlanabilir
    /// (gap-coklu-giris-cift-sayim-mutabakat-1): bağlanınca ekstre satırının sahipliği eşleşmeye döner. Kilitli dönem ve kullanıcı
    /// süzgeçleri çağıranda eklenir.
    /// </summary>
    internal static IQueryable<IslemEntity> BaglanabilirSorgu(KasaDbContext db, DateOnly takipBaslangic) => db.Islemler.AsNoTracking().Where(i =>
        (i.Tip == GiderTipi.Cari || i.Tip == GiderTipi.KrediKarti) && i.TutarTl > 0 && i.Tarih >= takipBaslangic
        && !db.AlisOdemeler.Any(o => o.IslemId == i.Id) && !db.KrediTaksitOdemeler.Any(o => o.IslemId == i.Id)
        && !db.AylikGiderOdemeler.Any(p => p.IslemId == i.Id) && !db.EkstreKayitlar.Any(k => k.IslemId == i.Id && (k.Iptal || k.IslemTuru != "Gider"))
        && !db.HesapHareketler.Any(h => h.IslemId == i.Id)
        && (i.KrediKartiId == null || db.KrediKartlari.Any(k => k.Id == i.KrediKartiId)
            && !db.TakipKartlar.Any(t => t.KrediKartiId == i.KrediKartiId && (!t.Aktif || i.Tarih < t.Baslangic))));

    /// <summary>
    /// Ödeme taşıma kilidi (purchase-2): onaylı alışta ödeme payları Id sırasıyla kümülatif dağıtılır. Ödeme başka alışa
    /// taşınınca KAYNAKTA ondan sonra girilmiş ödemelerin payı yeniden hesaplanır. Bunlardan biri kilitli dönemdeyse (tarihi kilit
    /// sınırında ya da kart harcamasının taksidi kilitli dönemde bir kart ödemesiyle ödenmiş) taşıma kilitli ayın kanal
    /// sonuçlarını sessizce değiştirirdi: kilit sınırı döner. Taşınan ödemeden ÖNCE girilmiş kilitli ödemenin payı değişmez;
    /// o durumda taşıma serbesttir. Saf taşımada (tarih, tutar, kart aynı) genel kilit kuralı (<see cref="AyKilidiKurallari"/>)
    /// yalnız hedef alışı görür: kilitli dönem ödemesi olan hedefe taşıma orada reddedilir, burada yalnız kaynak denetlenir.
    /// </summary>
    private static DateOnly? TasimaKilidi(KasaDbContext db, AlisEntity kaynak, int odemeId)
    {
        if (kaynak.Durum != AlisDurumlari.Onaylandi || db.AyKilidi.AsNoTracking().Select(k => k.KilitliSonTarih).Single() is not { } son)
            return null;
        return kaynak.Odemeler.Any(o => o.Id > odemeId && (o.Islem.Tarih <= son || KartOdemesiKilitli(db, o.IslemId, son))) ? son : null;
    }

    /// <summary>Gider takipteki bir kartın harcamasıysa, taksitlerinden biri kilitli dönemde (iptal edilmemiş) bir kart ödemesiyle ödendi mi.</summary>
    private static bool KartOdemesiKilitli(KasaDbContext db, int islemId, DateOnly son)
    {
        var harcamalar = db.TakipHarcamalar.Where(h => h.IslemId == islemId).Select(h => h.Id).ToList();
        if (harcamalar.Count == 0)
            return false;
        var taksitler = db.TakipKartTaksitler.Where(t => harcamalar.Contains(t.HarcamaId)).Select(t => t.Id).ToHashSet();
        return db.TakipKartOdemeler.AsNoTracking().Where(p => !p.Iptal && p.Tarih <= son).AsEnumerable()
            .Any(p => FinansTakipServisi.Read<KartTaksitPayi>(p.PaylarJson).Any(x => taksitler.Contains(x.TaksitId)));
    }

    /// <summary>Ödemenin giderine sahipliğini devretmiş (eşleşmeye dönmüş) iptal edilmemiş ekstre satırı: banka gideri ('Gider',
    /// gider kimliği) ya da gidersiz kart harcaması ('KartHarcama', ödemenin giderine bağlanan harcama). Yoksa null.</summary>
    internal static EkstreKayitEntity? DevredilenEkstreSatiri(KasaDbContext db, int islemId)
    {
        var harcama = db.TakipHarcamalar.Where(h => h.IslemId == islemId).Select(h => (int?)h.Id).FirstOrDefault();
        return db.EkstreKayitlar.FirstOrDefault(k => !k.Iptal && k.IslemTuru != EkstreAktarmaEndpoints.Eslestir
            && (k.IslemTuru == "Gider" && k.EslesmeTuru == "Gider" && k.EslesmeId == islemId
                || harcama != null && k.IslemTuru == "KartHarcama" && k.EslesmeTuru == "KartHarcama" && k.EslesmeId == harcama));
    }

    internal static IResult Duzelt(KasaDbContext db, int id, int odemeId, AlisOdemeDuzelt dto)
    {
        var v = new GirdiDogrulama();
        v.Metin(dto.Aciklama, "aciklama", 2000);
        v.Tarih(dto.Tarih, "tarih");
        v.Para(dto.Tutar, "tutar");
        v.Kontrol(dto.Tutar > 0, "tutar", "Ödeme pozitif olmalı.");
        v.Kontrol(dto.HesapId is null, "hesapId", "Ödeme doğrudan kanal ve genel kasaya kaydedilir; ayrı hesap seçilmez.");
        if (v.Sonuc() is { } invalid)
            return invalid;
        var digest = FinansHesaplari.Ozet(new { id, odemeId, dto.Tarih, tutar = dto.Tutar.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), dto.KrediKartiId, dto.HesapId, dto.HedefAlisId, aciklama = dto.Aciklama.Trim() });
        if (FinansHesaplari.Tekrar(db, dto.IstekId, "OdemeDuzelt", digest, key => Results.Ok(AlisEndpoints.ReadDto(db, key))) is { } replay)
            return replay;
        var source = AlisEndpoints.Query(db).SingleOrDefault(a => a.Id == id);
        var payment = source?.Odemeler.SingleOrDefault(o => o.Id == odemeId);
        if (source is null)
            return Results.NotFound();
        if (source.Surum != dto.Surum)
            return AlisEndpoints.Conflict("Alış değişmiş. Listeyi yenileyin.");
        if (payment is null)
            return Results.NotFound();
        // Kart takibindeki ödeme (gap-coklu-giris-cift-sayim-mutabakat-5; ekstreden gelen kart harcamasına bağlı olan dahil): tarihi,
        // tutarı ve kartı kart harcamasının kendisidir; yalnız başka alışa TAŞINIR (ya da iptal satırıyla alıştan ayrılır). Taşıma kart
        // harcamasını, taksitlerini ve ödeme paylarını değiştirmez; harcamanın kanal kaynağı hedef alışın dağılımı olur.
        var harcama = FinansTakipServisi.KaynakHarcama(db, payment.IslemId);
        if (harcama is not null)
        {
            if (dto.Tarih != payment.Islem.Tarih || dto.Tutar != payment.Islem.TutarTl || dto.KrediKartiId != payment.Islem.KrediKartiId || dto.HedefAlisId is not { } hedefId || hedefId == id)
                return AlisEndpoints.Conflict("Kart takibindeki ödemenin tarihi, tutarı ve kartı değiştirilemez; ödeme yalnız başka alışa taşınabilir ya da alıştan ayrılabilir (Ödemeyi iptal et). Kart tarafındaki yanlışlık için ödemeyi alıştan ayırıp Kredi Kartları ekranından düzeltin.");
        }
        // Ekstreden gelmiş banka giderine bağlanan ödemenin tarihi, tutarı ve ödeme yöntemi banka satırıdır: yalnız başka alışa taşınabilir
        // ya da alıştan ayrılabilir (iptal satırı kendi kaydına döndürür).
        else if (DevredilenEkstreSatiri(db, payment.IslemId) is not null)
        {
            if (dto.Tarih != payment.Islem.Tarih || dto.Tutar != payment.Islem.TutarTl || dto.KrediKartiId is not null)
                return AlisEndpoints.Conflict("Ekstreden gelen ödemenin tarihi, tutarı ve ödeme yöntemi değiştirilemez; yalnız başka alışa taşınabilir ya da alıştan ayrılabilir.");
        }
        else if (FinansTakipServisi.IslemYonetiliyor(db, payment.Islem) || (dto.KrediKartiId is { } targetCard && db.TakipKartlar.Any(t => t.KrediKartiId == targetCard)))
            return AlisEndpoints.Conflict("Yeni kart takibine bağlı ödeme için Kredi Kartları ekranında açıklamalı iade girin; alışın kanal dağılımı ayrıca düzenlenebilir.");
        var target = source;
        if (dto.HedefAlisId is { } targetId && targetId != id)
        {
            target = AlisEndpoints.Query(db).SingleOrDefault(a => a.Id == targetId);
            if (target is null)
                return Results.NotFound();
            if (dto.HedefSurum != target.Surum)
                return AlisEndpoints.Conflict("Hedef alış değişmiş. Listeyi yenileyin.");
        }
        if (target.Odemeler.Where(o => o.Id != odemeId).Sum(o => o.Islem.TutarTl) + dto.Tutar > target.Kalemler.Sum(k => k.Tutar))
            return AlisEndpoints.Conflict("Düzeltilmiş ödeme hedef alış toplamını aşamaz.");
        // Kart takibindeki ödemenin kartı değişmez: yeni kullanıma kapatılmış kartın harcaması da taşınabilir.
        if (harcama is null)
        {
            v.Kart(db, dto.KrediKartiId);
            // K3: ödemeyi yeni bir karta bağlamak yeni kredi kartı gideridir; aynı kartla tutar/tarih düzeltmesi serbesttir.
            KayitGirdileri.TakipliKartKurali(v, db, dto.KrediKartiId is null ? GiderTipi.Cari : GiderTipi.KrediKarti, dto.KrediKartiId, payment.Islem);
        }
        v.Kontrol(dto.Tarih >= db.Ayarlar.Select(a => a.TakipBaslangic).First(), "tarih", "Ödeme takip başlangıcından önce olamaz.");
        if (v.Sonuc() is { } invalidReference)
            return invalidReference;
        if (target.Id != source.Id && TasimaKilidi(db, source, odemeId) is { } kilitSonu)
            return AlisEndpoints.Conflict($"{kilitSonu:yyyy-MM-dd} tarihine kadar dönem kilitli. Bu ödemeyi taşımak, alışta ondan sonra girilmiş kilitli dönem ödemelerinin kanal paylarını değiştirir; ilgili ayı gerekçeyle açın.");
        var before = JsonSerializer.Serialize(new { aciklama = dto.Aciklama.Trim(), alis = AlisHesaplari.ToDto(source) });
        var expense = payment.Islem;
        if (harcama is not null)
        {
            // Aynalanmış harcamanın açıklaması giderin açıklamasıdır (Sync): yeni alışın adını taşır; ekstreden gelen harcama banka
            // metnini korur. Harcamanın kanal kaynağı değiştiği için kart sürümü artar.
            if (harcama.Aciklama == expense.Cari)
                harcama.Aciklama = target.Tedarikci;
            db.TakipKartlar.Single(t => t.KrediKartiId == harcama.KrediKartiId).Surum++;
        }
        var eskiKartTipiniKoru = expense.Tip == GiderTipi.KrediKarti && expense.KrediKartiId is null && dto.KrediKartiId is null;
        expense.Tarih = dto.Tarih;
        expense.TutarTl = dto.Tutar;
        expense.KrediKartiId = dto.KrediKartiId;
        expense.Tip = dto.KrediKartiId is not null || eskiKartTipiniKoru ? GiderTipi.KrediKarti : GiderTipi.Cari;
        expense.Cari = target.Tedarikci;
        expense.Not = dto.Aciklama.Trim();
        // Önizleme sürümünden kalmış bir bağlantı varsa koru; yeni hesap bağlantısı oluşturma.
        if (expense.HesapHareketi is { } legacyAccount)
        { legacyAccount.Tarih = expense.Tarih; legacyAccount.Tutar = -expense.TutarTl; legacyAccount.Aciklama = expense.Not; }
        if (target.Id != source.Id)
        {
            source.Odemeler.Remove(payment);
            target.Odemeler.Add(payment);
            payment.AlisId = target.Id;
            target.Surum++;
            foreach (var attachment in db.Belgeler.Where(b => b.OdemeId == odemeId))
                attachment.AlisId = target.Id;
        }
        source.Surum++;
        FinansHesaplari.IstekKaydet(db, dto.IstekId, "OdemeDuzelt", digest, id, before);
        db.SaveChanges();
        return Results.Ok(AlisEndpoints.ReadDto(db, id));
    }

    internal static IResult Iptal(KasaDbContext db, int id, int odemeId, AlisOdemeIptal dto)
    {
        var v = new GirdiDogrulama();
        v.Metin(dto.Aciklama, "aciklama", 2000);
        var kanallar = dto.KanalDagilimlari?.OrderBy(p => p?.KanalId).ToList();
        if (kanallar is not null)
        {
            v.Kontrol(kanallar.Count is >= 1 and <= 100 && kanallar.All(p => p is not null), "kanalDagilimlari", "Alıştan ayrılan kart harcaması için 1–100 kanal payı girin.");
            if (v.Sonuc() is null)
            {
                var kayitli = db.Kanallar.Select(k => k.Id).ToHashSet();
                foreach (var pay in kanallar)
                { v.Para(pay.Tutar, "kanalDagilimlari"); v.Kontrol(pay.Tutar > 0 && kayitli.Contains(pay.KanalId), "kanalDagilimlari", "Kayıtlı kanalları pozitif tutarla seçin."); }
                v.Kontrol(kanallar.Select(p => p.KanalId).Distinct().Count() == kanallar.Count, "kanalDagilimlari", "Aynı kanal birden fazla seçilemez.");
            }
        }
        if (v.Sonuc() is { } invalid)
            return invalid;
        // Sonradan eklenen kanal payları yalnız doluyken özete girer: eski isteklerin özeti değişmez.
        var digest = kanallar is null ? FinansHesaplari.Ozet(new { id, odemeId, aciklama = dto.Aciklama.Trim() })
            : FinansHesaplari.Ozet(new { id, odemeId, aciklama = dto.Aciklama.Trim(), kanallar = kanallar.Select(p => $"{p.KanalId}:{p.Tutar.ToString("0.00", CultureInfo.InvariantCulture)}") });
        if (FinansHesaplari.Tekrar(db, dto.IstekId, "OdemeIptal", digest, key => Results.Ok(AlisEndpoints.ReadDto(db, key))) is { } replay)
            return replay;
        var alis = AlisEndpoints.Query(db).SingleOrDefault(a => a.Id == id);
        var payment = alis?.Odemeler.SingleOrDefault(o => o.Id == odemeId);
        if (alis is null)
            return Results.NotFound();
        if (alis.Surum != dto.Surum)
            return AlisEndpoints.Conflict("Alış değişmiş. Listeyi yenileyin.");
        if (payment is null)
            return Results.NotFound();
        var ekstreSatiri = DevredilenEkstreSatiri(db, payment.IslemId);
        var kartHarcamasi = FinansTakipServisi.KaynakHarcama(db, payment.IslemId);
        if (kanallar is not null)
        {
            if (kartHarcamasi is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["kanalDagilimlari"] = ["Kanal dağılımı yalnız kart takibindeki ödeme alıştan ayrılırken girilir."] });
            if (kanallar.Sum(p => p.Tutar) != payment.Islem.TutarTl)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["kanalDagilimlari"] = ["Kanal payları toplamı ödeme tutarına eşit olmalı."] });
        }
        if (ekstreSatiri is null && kartHarcamasi is null && FinansTakipServisi.IslemYonetiliyor(db, payment.Islem))
            return AlisEndpoints.Conflict("Kart takip başlangıcından önceki (eski kuralda kalan) kart ödemesi silinemez; geçmiş raporlar korunur. Kredi Kartları ekranında açıklamalı iade girin.");
        var before = JsonSerializer.Serialize(new { aciklama = dto.Aciklama.Trim(), alis = AlisHesaplari.ToDto(alis) });
        if (ekstreSatiri is null && kartHarcamasi is not null)
            return KartOdemesiniAyir(db, alis, payment, kartHarcamasi, kanallar, dto, digest, before);
        if (ekstreSatiri is not null)
        {
            // Ekstreden gelmiş kayda bağlanan ödeme (gap-coklu-giris-cift-sayim-mutabakat-1): alıştan ayrılınca kayıt yeniden ekstre
            // satırınındır. Kart harcamasında bağlama için oluşturulan gider silinir, harcama gidersiz kalır; banka giderinde gider
            // korunur. Satırın sahiplik sütunu geri yazılır, eşleşme kalkar; kasa ve kart borcu bağlamadan önceki haline döner.
            // Harcamanın kanal payı ekstre satırındaki dağılımdır: girilen kanal payları kullanılmaz.
            alis.Odemeler.Remove(payment);
            db.AlisOdemeler.Remove(payment);
            var harcama = ekstreSatiri.EslesmeTuru == "KartHarcama" ? db.TakipHarcamalar.Single(h => h.Id == ekstreSatiri.EslesmeId) : null;
            if (harcama is not null)
            { harcama.IslemId = null; db.TakipKartlar.Single(t => t.KrediKartiId == harcama.KrediKartiId).Surum++; }
            alis.Surum++;
            FinansHesaplari.IstekKaydet(db, dto.IstekId, "OdemeIptal", digest, id, before);
            db.SaveChanges();
            db.EkstreDegisikligi = true;
            try
            {
                if (harcama is not null)
                { db.Islemler.Remove(payment.Islem); ekstreSatiri.KartHarcamaId = harcama.Id; }
                else
                    ekstreSatiri.IslemId = payment.IslemId;
                ekstreSatiri.EslesmeTuru = null;
                ekstreSatiri.EslesmeId = null;
                db.SaveChanges();
            }
            finally { db.EkstreDegisikligi = false; }
            FinansTakipServisi.Sync(db);
            return Results.Ok(AlisEndpoints.ReadDto(db, id));
        }
        if (payment.Islem.HesapHareketi is { } account)
            db.HesapHareketler.Remove(account);
        db.AlisOdemeler.Remove(payment);
        db.Islemler.Remove(payment.Islem);
        alis.Surum++;
        FinansHesaplari.IstekKaydet(db, dto.IstekId, "OdemeIptal", digest, id, before);
        db.SaveChanges();
        return Results.Ok(AlisEndpoints.ReadDto(db, id));
    }

    /// <summary>
    /// Kart takibindeki ödemenin alıştan ayrılması (gap-coklu-giris-cift-sayim-mutabakat-5). Kanal payı girilmezse ödeme gerçekleşmemiş
    /// sayılır: kart harcaması ödenmemiş, iadesiz ve ekstreye bağsızsa harcama iptal edilir (taksitleri borçtan çıkar), gider ve ödeme
    /// kalkar; aksi halde 409 (ödenmiş harcamanın kaldırılması önceki kart ödemelerinin kanal payını ve kasayı değiştirirdi). Kanal
    /// payı girilirse yalnız alış bağı kalkar: gider ve harcama alıştan bağımsız kart gideri olarak kalır, harcamanın kanal kaynağı
    /// girilen gerçek paylardır (tek kanalda o kanal, çok kanalda "A / B" etiketi). Alışın kalanı artar, gider listesi harcamayı bir
    /// kez gösterir; gider başka alışa mevcut gider olarak bağlanabilir. Kilitli dönem kuralları (AyKilidiKurallari) geçerlidir.
    /// </summary>
    private static IResult KartOdemesiniAyir(KasaDbContext db, AlisEntity alis, AlisOdemeEntity payment, TakipHarcamaEntity harcama,
        IReadOnlyList<AlisDagilimYaz>? kanallar, AlisOdemeIptal dto, string digest, string before)
    {
        var expense = payment.Islem;
        if (kanallar is null && FinansTakipServisi.KaynakHarcamaEngeli(db, harcama) is { } engel)
            return AlisEndpoints.Conflict(engel switch
            {
                FinansTakipServisi.HarcamaEngeli.Odendi => "Bu kart harcaması ödendi. Alıştan ayırmak için gerçek kanal dağılımını girin; harcama hiç yapılmadıysa Kredi Kartları ekranında iade girin.",
                FinansTakipServisi.HarcamaEngeli.IadesiVar => "Bu kart harcamasının iadesi var. Alıştan ayırmak için gerçek kanal dağılımını girin; iade hatalıysa önce Kredi Kartları ekranında gerekçeyle iptal edin.",
                FinansTakipServisi.HarcamaEngeli.Ekstre => "Bu kart harcaması bir ekstre satırıyla eşleştirildi. Alıştan ayırmak için gerçek kanal dağılımını girin; harcama hiç yapılmadıysa önce PDF İçe Aktarma bölümünden eşleştirmeyi iptal edin.",
                _ => throw new ArgumentOutOfRangeException(nameof(engel), engel, null),
            });
        alis.Odemeler.Remove(payment);
        db.AlisOdemeler.Remove(payment);
        if (kanallar is not null)
        {
            var adlar = db.Kanallar.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
            expense.Kanal = string.Join(" / ", kanallar.Select(p => adlar[p.KanalId]));
            expense.KanalId = kanallar.Count == 1 ? kanallar[0].KanalId : null;
            var not = (string.IsNullOrWhiteSpace(expense.Not) ? "" : expense.Not.Trim() + " · ") + "Alıştan ayrıldı: " + dto.Aciklama.Trim();
            expense.Not = not.Length <= 2000 ? not : not[..2000];
            harcama.DagilimJson = FinansTakipServisi.Json(kanallar.Select(p => new KanalPayYaz(p.KanalId, p.Tutar)).ToList());
        }
        else
        { harcama.Iptal = true; harcama.IslemId = null; }
        db.TakipKartlar.Single(t => t.KrediKartiId == harcama.KrediKartiId).Surum++;
        alis.Surum++;
        FinansHesaplari.IstekKaydet(db, dto.IstekId, "OdemeIptal", digest, alis.Id, before);
        db.SaveChanges();
        if (kanallar is null)
        {
            if (expense.HesapHareketi is { } account)
                db.HesapHareketler.Remove(account);
            db.Islemler.Remove(expense);
            db.SaveChanges();
        }
        FinansTakipServisi.Sync(db);
        return Results.Ok(AlisEndpoints.ReadDto(db, alis.Id));
    }
}

/// <summary>
/// GET /api/alis/inceleme-ozeti (webui-6, ana sayfa bölümü): editörün ana sayfası inceleme bekleyen alışların sayısını ve en
/// yenilerini göstermek için bütün alış listesini (her alış kalem, dağılım ve ödeme join'leriyle) indirmez. Sayı COUNT ile,
/// alışlar yalnız istenen kadar (<c>adet</c>, varsayılan 4, en çok 20) GET /api/alis ile aynı sıra (tarih ve kimlik azalan) ve
/// biçimde okunur. Okuma anlık görüntüsünde çalışır; sorgu sayısı alış sayısından bağımsızdır.
/// </summary>
internal static class AlisIncelemeOzeti
{
    private const int VarsayilanAdet = 4;
    private const int EnBuyukAdet = 20;

    internal static WebApplication MapAlisIncelemeOzeti(this WebApplication app)
    {
        app.MapGet("/api/alis/inceleme-ozeti", (int? adet, KasaDbContext db) => AlisEndpoints.Oku(db, () =>
        {
            if (adet is < 0 or > EnBuyukAdet)
                return Results.BadRequest(new { hata = $"Alış adedi 0 ile {EnBuyukAdet} arasında olmalı." });
            var sayi = db.Alislar.Count(a => a.Durum == AlisDurumlari.Incelemede);
            var ogeler = adet == 0 ? new List<AlisEntity>() : AlisEndpoints.Query(db).AsNoTracking().Where(a => a.Durum == AlisDurumlari.Incelemede)
                .OrderByDescending(a => a.Tarih).ThenByDescending(a => a.Id).Take(adet ?? VarsayilanAdet).ToList();
            var kartAdlari = ogeler.Count == 0 ? null : db.KrediKartlari.AsNoTracking().ToDictionary(k => k.Id, k => k.Ad);
            return Results.Ok(new AlisIncelemeOzetiDto(sayi, ogeler.Select(a => AlisHesaplari.ToDto(a, kartAdlari)).ToList()));
        })).RequireAuthorization("Editor");
        return app;
    }
}
