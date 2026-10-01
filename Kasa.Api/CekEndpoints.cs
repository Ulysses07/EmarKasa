using System.Globalization;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core;
using Kasa.Core.Kodlar;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Kasa.Api.FinansTakipServisi;

namespace Kasa.Api;

/// <summary>
/// Çek ve senet uçları (/api/takip/cekler; docs/specs/2026-10-01-cekler.md "Uçlar"). Okuma Finans politikasıyla (İzleyici ve Editör),
/// yazma Editor politikasıyla açılır; Alıcı erişemez. Yazmalarda istekId tekrar koruması ve sürüm (409) diğer takip uçlarındaki
/// gibidir; geçiş ve tutar kuralları <see cref="CekKurallari"/>'ndadır, ay kilidi kaydetme kancasındadır (AyKilidiKurallari).
/// Hata iletileri Türkçe ve { hata } biçimindedir.
/// </summary>
public static partial class FinansTakipEndpoints
{
    private static readonly string[] HareketTurleri =
    [
        CekHareketTurleri.Tahsilat, CekHareketTurleri.Odeme, CekHareketTurleri.Ciro, CekHareketTurleri.Kirdirma, CekHareketTurleri.Donus,
        CekHareketTurleri.Karsiliksiz, CekHareketTurleri.Iade,
    ];

    private static void MapCekEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/cekler", (string? yon, string? durum, string? ara, DateOnly? vadeBas, DateOnly? vadeSon, KasaDbContext db, CancellationToken ct) =>
            View(db, b =>
            {
                Require(yon is null or CekYonleri.Alinan or CekYonleri.Verilen, "Yön alınan ya da verilen olmalı.");
                Require(durum is null or CekSuzgecleri.Portfoyde or CekSuzgecleri.Karsiliksiz or CekSuzgecleri.Kapanan or CekSuzgecleri.Hepsi,
                    "Durum portföyde, karşılıksız, kapanan ya da hepsi olmalı.");
                Require((ara?.Length ?? 0) <= 200, "Arama en çok 200 karakter olabilir.");
                Require(vadeBas is null || vadeSon is null || vadeBas <= vadeSon, "Vade başlangıcı bitişten sonra olamaz.");
                return CekServisi.Liste(b, yon, durum, ara, vadeBas, vadeSon);
            }, ct));
        api.MapGet("/cekler/{id:int}", (int id, KasaDbContext db, CancellationToken ct) => View(db, b =>
        {
            var cek = CekServisi.Tek(b, id);
            Require(cek is not null, "Çek bulunamadı.", 404);
            return cek!;
        }, ct));

        api.MapPost("/cekler", (CekYaz dto, KasaDbContext db) => CekDegisikligi(db, 0, null, dto.IstekId, "CekYeni", dto, () =>
        {
            var cek = new CekEntity();
            CekAlanlari(db, cek, dto);
            db.Cekler.Add(cek);
            db.SaveChanges();
            return cek.Id;
        })).RequireAuthorization("Editor");
        api.MapPut("/cekler/{id:int}", (int id, CekYaz dto, KasaDbContext db) => CekDegisikligi(db, id, dto.Surum, dto.IstekId, "CekDuzelt", dto, () =>
        {
            var cek = db.Cekler.Single(c => c.Id == id);
            var hareketler = db.CekHareketler.AsNoTracking().Where(h => h.CekId == id).ToList();
            if (hareketler.Count > 0)
            {
                Require(dto.Yon == cek.Yon && dto.Tur == cek.Tur, "Hareketi olan çekin yönü ve türü değiştirilemez; önce hareketleri geri alın.", 409);
                if (dto.Tutar != cek.Tutar)
                {
                    Require(!hareketler.Any(h => h.Tur is CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma or CekHareketTurleri.Donus),
                        "Ciro, kırdırma ya da dönüş hareketi olan çekin tutarı değiştirilemez.", 409);
                    var odenen = CekKurallari.Durum(cek.Yon, cek.Tutar, hareketler.Select(CekServisi.Cekirdek).ToList()).Odenen;
                    Require(dto.Tutar >= odenen, $"Tutar tahsil edilen ya da ödenen tutardan ({Tl(odenen)}) az olamaz.", 409);
                }
            }
            CekAlanlari(db, cek, dto);
            return id;
        })).RequireAuthorization("Editor");
        api.MapDelete("/cekler/{id:int}", (int id, [FromBody] CekSilYaz dto, KasaDbContext db) => CekDegisikligi(db, id, dto.Surum, dto.IstekId, "CekSil", dto, () =>
        {
            db.CekHareketler.RemoveRange(db.CekHareketler.Where(h => h.CekId == id).ToList());
            db.Cekler.Remove(db.Cekler.Single(c => c.Id == id));
            return id;
        }, silme: true)).RequireAuthorization("Editor");
        api.MapPost("/cekler/{id:int}/hareketler", (int id, CekHareketYaz dto, KasaDbContext db) => CekDegisikligi(db, id, dto.Surum, dto.IstekId, "CekHareket", dto, () =>
        {
            HareketEkle(db, db.Cekler.Single(c => c.Id == id), dto);
            return id;
        })).RequireAuthorization("Editor");
        api.MapDelete("/cekler/{id:int}/hareketler/son", (int id, [FromBody] CekSilYaz dto, KasaDbContext db) => CekDegisikligi(db, id, dto.Surum, dto.IstekId, "CekGeriAl", dto, () =>
        {
            var son = db.CekHareketler.Where(h => h.CekId == id).OrderByDescending(h => h.Sira).FirstOrDefault();
            Require(son is not null, "Geri alınacak hareket yok.", 409);
            db.CekHareketler.Remove(son!);
            return id;
        })).RequireAuthorization("Editor");
    }

    /// <summary>Çek yazma uçlarının ortak akışı (kart ve kredideki Change ile aynı desen): tek yazma transaction'ı, denetim bağlamı,
    /// istekId tekrar koruması (aynı istek aynı yanıtı alır, aynı kimlik başka içerikle 409), sürüm denetimi, değişiklik, sürüm artışı.</summary>
    private static IResult CekDegisikligi(KasaDbContext db, int id, int? surum, Guid istekId, string tur, object govde, Func<int> degistir, bool silme = false) =>
        Safe(() => AlisEndpoints.Mutate(db, () =>
        {
            using var denetim = db.Denetle(null, istekId);
            var dugum = JsonSerializer.SerializeToNode(govde)!.AsObject();
            dugum.Remove("Surum");
            var ozet = FinansHesaplari.Ozet(new { id, govde = dugum.ToJsonString() });
            IResult Yanit(int kimlik) => silme ? Results.NoContent() : Results.Ok(CekServisi.Tek(new TakipHesapBaglami(db), kimlik));
            if (FinansHesaplari.Tekrar(db, istekId, tur, ozet, Yanit) is { } tekrar)
                return tekrar;
            if (id != 0)
            {
                var mevcut = db.Cekler.Where(c => c.Id == id).Select(c => (int?)c.Surum).SingleOrDefault();
                Require(mevcut is not null, "Çek bulunamadı.", 404);
                Require(surum == mevcut, "Çek başka bir işlemle değişti. Listeyi yenileyip tekrar deneyin.", 409);
            }
            var sonuc = degistir();
            if (!silme)
                db.Cekler.Single(c => c.Id == sonuc).Surum++;
            FinansHesaplari.IstekKaydet(db, istekId, tur, ozet, sonuc);
            db.SaveChanges();
            return Yanit(sonuc);
        }));

    /// <summary>Çekin alanlarını doğrulayıp yazar (ekleme ve düzeltme).</summary>
    private static void CekAlanlari(KasaDbContext db, CekEntity cek, CekYaz d)
    {
        Require(d.Tur is CekTurleri.Cek or CekTurleri.Senet, "Tür çek ya da senet olmalı.");
        Require(d.Yon is CekYonleri.Alinan or CekYonleri.Verilen, "Yön alınan ya da verilen olmalı.");
        var no = CekMetni(d.No, "Çek / senet numarası", 100)!;
        var banka = CekMetni(d.Banka, "Banka", 200, zorunlu: d.Tur == CekTurleri.Cek);
        var kisi = CekMetni(d.Kisi, "Kişi", 200)!;
        var not = CekMetni(d.Not, "Not", 2000, zorunlu: false);
        Money(d.Tutar);
        Require(d.Tutar > 0, "Tutar sıfırdan büyük olmalı.");
        Date(d.VadeTarihi);
        Require(d.VadeTarihi >= GirdiDogrulama.EnErkenTarih, $"Vade tarihi {GirdiDogrulama.EnErkenTarih:dd.MM.yyyy} tarihinden önce olamaz.");
        int? kanalId = null;
        string? konum = null;
        if (d.Yon == CekYonleri.Verilen)
        {
            kanalId = KasaCoz(db, d.Kanal, ortakOlabilir: true, "Verilen çekin ödeneceği kasayı (kanal ya da Ortak) seçin.");
            Require(string.IsNullOrWhiteSpace(d.Konum), "Konum yalnız alınan çekte seçilir.");
        }
        else
        {
            Require(string.IsNullOrWhiteSpace(d.Kanal), "Alınan çekte kasa tahsilat, ciro ya da kırdırma hareketinde seçilir.");
            konum = string.IsNullOrWhiteSpace(d.Konum) ? CekKonumlari.Elde : d.Konum.Trim();
            Require(konum is CekKonumlari.Elde or CekKonumlari.BankadaTahsilde or CekKonumlari.Teminatta or CekKonumlari.Icrada,
                "Konum elde, bankada tahsilde, teminatta ya da icrada olmalı.");
        }
        cek.Tur = d.Tur;
        cek.Yon = d.Yon;
        cek.No = no;
        cek.Banka = banka;
        cek.Kisi = kisi;
        cek.Tutar = d.Tutar;
        cek.VadeTarihi = d.VadeTarihi;
        cek.KanalId = kanalId;
        cek.Teminat = d.Teminat;
        cek.Konum = konum;
        cek.Not = not;
    }

    /// <summary>Hareketi doğrulayıp ekler: tarih takip başlangıcı ile bugün arasında; geçiş, tarih sırası ve tutar kuralları
    /// <see cref="CekKurallari.HareketHatasi"/>; kasa alınan çekte seçilir, dönüşte ters çevrilen hareketinki, verilen çekte çekin kasasıdır.</summary>
    private static void HareketEkle(KasaDbContext db, CekEntity cek, CekHareketYaz d)
    {
        Require(HareketTurleri.Contains(d.Tur), "Geçerli bir hareket türü seçin.");
        Date(d.Tarih);
        var baslangic = db.Ayarlar.Select(a => a.TakipBaslangic).First();
        Require(d.Tarih >= baslangic && d.Tarih <= Bugun,
            $"Hareket tarihi takip başlangıcı ({baslangic.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}) ile bugün arasında olmalı.");
        Money(d.Tutar);
        if (d.NetTutar is { } net)
            Money(net);
        var karsi = CekMetni(d.Karsi, "Karşı taraf", 200, zorunlu: false);
        var mevcut = db.CekHareketler.Where(h => h.CekId == cek.Id).OrderBy(h => h.Sira).ToList();
        var cekirdek = mevcut.Select(CekServisi.Cekirdek).ToList();
        var yeni = new CekHareketi(0, mevcut.Count + 1, d.Tur, d.Tarih, d.Tutar, d.NetTutar, null, karsi);
        if (CekKurallari.HareketHatasi(cek.Yon, cek.Tutar, cekirdek, yeni) is { } hata)
            Require(false, hata, CekKurallari.IzinliHareketler(cek.Yon, cek.Tutar, cekirdek).Contains(d.Tur) ? 400 : 409);
        int? kanalId = null;
        if (d.Tur == CekHareketTurleri.Donus)
        {
            var devir = mevcut.Single(h => h.Id == CekKurallari.Durum(cek.Yon, cek.Tutar, cekirdek).AcikDevir!.Id);
            Require(string.IsNullOrWhiteSpace(d.Kanal), "Dönüş, ters çevrilen ciro ya da kırdırmanın kasasına yazılır; kasa seçilmez.");
            kanalId = devir.KanalId;
            karsi = devir.Karsi;
        }
        else if (cek.Yon == CekYonleri.Verilen)
        {
            Require(string.IsNullOrWhiteSpace(d.Kanal), "Verilen çek çekin kasasından ödenir; harekette kasa seçilmez.");
            kanalId = d.Tur == CekHareketTurleri.Odeme ? cek.KanalId : null;
        }
        else if (CekKurallari.KasaEtkili(d.Tur))
            kanalId = KasaCoz(db, d.Kanal, ortakOlabilir: false, "Tahsilatın, cironun ya da kırdırmanın kasasını (kanal) seçin.");
        else
            Require(string.IsNullOrWhiteSpace(d.Kanal), "Karşılıksız ve iade hareketinde kasa seçilmez.");
        db.CekHareketler.Add(new CekHareketEntity
        {
            CekId = cek.Id,
            Sira = mevcut.Count == 0 ? 1 : mevcut.Max(h => h.Sira) + 1,
            Tur = d.Tur,
            Tarih = d.Tarih,
            Tutar = d.Tutar,
            NetTutar = d.Tur == CekHareketTurleri.Kirdirma ? d.NetTutar : null,
            KanalId = kanalId,
            Karsi = d.Tur is CekHareketTurleri.Ciro or CekHareketTurleri.Kirdirma or CekHareketTurleri.Donus ? karsi : null,
        });
    }

    /// <summary>Kasa adını kimliğe çevirir; <paramref name="ortakOlabilir"/> ise "Ortak" null döner. Boş ya da kayıtsız ad 400.</summary>
    private static int? KasaCoz(KasaDbContext db, string? ad, bool ortakOlabilir, string bosIletisi)
    {
        var temiz = ad?.Trim();
        Require(!string.IsNullOrEmpty(temiz), bosIletisi);
        if (ortakOlabilir && temiz == KanalEtiketleri.Ortak)
            return null;
        var kanal = db.Kanallar.AsNoTracking().FirstOrDefault(k => k.Ad == temiz);
        Require(kanal is not null, "Kayıtlı bir kasa (kanal) seçin.");
        return kanal!.Id;
    }

    /// <summary>Kırpılmış metin; boşsa null. Zorunlu alan boş, sınırı aşan ya da görünmeyen karakter taşıyan metin 400.</summary>
    private static string? CekMetni(string? deger, string alan, int sinir, bool zorunlu = true)
    {
        var temiz = string.IsNullOrWhiteSpace(deger) ? null : deger.Trim();
        Require(!zorunlu || temiz is not null, $"{alan} boş olamaz.");
        Require(temiz is null || temiz.Length <= sinir, $"{alan} en çok {sinir} karakter olabilir.");
        if (GirdiDogrulama.GecersizKarakterIletisi(temiz) is { } ileti)
            Require(false, $"{alan}: {ileti}");
        return temiz;
    }
}
