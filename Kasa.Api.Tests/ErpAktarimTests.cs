using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kasa.Api.Data;
using Kasa.Core.Kodlar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kasa.Api.Tests;

/// <summary>ERP12 cari hareketleri (docs/specs/2026-11-25-erp12-aktarimi.md): okuma, yinelenen aktarım, eşleştirme, nakit kaydı ve cari listesi.</summary>
public class ErpAktarimTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateOnly Bugun => KasaWebFactory.VarsayilanBugun;
    private static DateOnly G(int once) => Bugun.AddDays(-once);
    private static object?[] S(params object?[] h) => h;
    private static readonly object?[] Baslik = S("Tarih", "Cari Kodu", "Cari Ünvanı", "Evrak Tipi", "Evrak No", "Açıklama", "Borç", "Alacak", "Bakiye");
    private static byte[] Dosya(params object?[][] satirlar) => TestExcel.Xlsx(("Cari Hareket", [Baslik, .. satirlar]));
    private static ErpOkumaSonucu Oku(params object?[][] satirlar) => EkstreExcelOkuyucu.OkuErp(Dosya(satirlar));
    private static string Red(byte[] dosya) => Assert.Throws<ExcelOkumaException>(() => EkstreExcelOkuyucu.OkuErp(dosya)).Message;

    private static async Task<HttpClient> Editor(KasaWebFactory f)
    {
        var c = await f.EditorClientAsync();
        (await c.PutAsJsonAsync("/api/ayarlar", new { takipBaslangic = new DateOnly(Bugun.Year, 1, 1), kasaAcilisDevri = 1000m }, Ct)).EnsureSuccessStatusCode();
        return c;
    }
    private static async Task<HttpResponseMessage> Gonder(HttpClient c, byte[] dosya, string kaynak = "Erp", string ad = "erp.xlsx")
    {
        using var form = new MultipartFormDataContent { { new StringContent(kaynak), "kaynak" }, { new StringContent(kaynak == "Erp" ? "" : "Akbank"), "banka" }, { new StringContent("Ana hesap"), "hesapAdi" }, { new ByteArrayContent(dosya), "dosya", ad } };
        return await c.PostAsync("/api/ekstre-aktar/yukle", form, Ct);
    }
    private static async Task<EkstreBelgeDto> Yukle(HttpClient c, byte[] dosya, string kaynak = "Erp")
    {
        var r = await Gonder(c, dosya, kaynak);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync(Ct));
        return (await r.Content.ReadFromJsonAsync<EkstreBelgeDto>(Ct))!;
    }
    private static Task<EkstreBelgeDto> Banka(HttpClient c, params object?[][] satirlar) => Yukle(c, TestExcel.Xlsx(("H", [S("Tarih", "Açıklama", "Tutar (TL)"), .. satirlar])), "Banka");
    private static async Task<T> Post<T>(HttpClient c, string yol, object govde)
    {
        var r = await c.PostAsJsonAsync(yol, govde, Ct);
        Assert.True(r.IsSuccessStatusCode, $"{r.StatusCode}: {await r.Content.ReadAsStringAsync(Ct)}");
        return (await r.Content.ReadFromJsonAsync<T>(Ct))!;
    }
    private static async Task<string> Reddet(HttpClient c, string yol, object govde)
    {
        var r = await c.PostAsJsonAsync(yol, govde, Ct);
        Assert.False(r.IsSuccessStatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("hata").GetString()!;
    }
    private static EkstreSatirYaz Satir(EkstreBelgeDto d, int no, string tur = "Gider", string? hedefTur = null, int? hedefId = null)
    {
        var s = d.Satirlar.Single(s => s.No == no);
        return new(no, s.Tarih!.Value, s.Aciklama, s.Tutar!.Value, tur, hedefTur is null ? "Genel" : "Eslesme", [], null, null, hedefTur, hedefId);
    }
    private static async Task<(EkstreBelgeDto Belge, EkstreOnizlemeDto Onizleme)> Kaydet(HttpClient c, EkstreBelgeDto d, params EkstreSatirYaz[] satirlar)
    {
        var istek = new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, satirlar);
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{d.Id}/onizleme", istek);
        return (await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/kaydet", istek with { OnizlemeOzeti = onizleme.OnizlemeOzeti, TekrarOnay = true }), onizleme);
    }
    private static async Task<List<EkstreKuralOnerisi>> Oneriler(HttpClient c, EkstreBelgeDto d) => (await c.GetFromJsonAsync<List<EkstreKuralOnerisi>>($"/api/ekstre-aktar/{d.Id}/oneriler", Ct))!;
    private static async Task<decimal> Kasa(HttpClient c) => (await c.GetFromJsonAsync<PanelDto>("/api/rapor/panel", Ct))!.GuncelKasa;

    // ---- Okuma ----

    [Fact]
    public void Kasa_hareketi_fatura_nakit_ve_yon_ayrilir()
    {
        var s = Oku(
            S(G(9), "120.01", "AHMET YILMAZ LTD", "Satış Faturası", "F1", null, 12_000, null, 12_000),
            S(G(8), "120.01", "AHMET YILMAZ LTD", "Tahsilat (Havale)", "T1", "Kasım", null, 5_000),
            S(G(7), "320.02", "KIRTASİYE DÜNYASI", "Nakit Ödeme", "O1", null, 350, null),
            S(G(6), "320.03", "DENİZ TİCARET", "Verilen Çek", "C1", null, 8_000, null),
            S(G(5), "320.03", "DENİZ TİCARET", "Devir", null, null, 100, null),
            S(G(4), "320.03", "DENİZ TİCARET", "Peşin Satış", "P1", null, 70, 70),
            S(G(3), "320.04", "BİLİNMEYEN", "Özel Tip", null, null, null, 15),
            S(G(2), "320.04", "BİLİNMEYEN", null, null, "kasadan ödeme", 20, null));
        Assert.Equal([(false, false, "Cikis"), (true, false, "Giris"), (true, true, "Cikis"), (true, false, "Cikis"), (false, false, "Cikis"), (false, false, "Giris"), (true, false, "Giris"), (true, true, "Cikis")],
            s.Satirlar.Select(r => (r.KasaSatiri, r.Nakit, r.Yon)));
        Assert.Equal(("120.01", "AHMET YILMAZ LTD", "Tahsilat (Havale)", "T1", "Kasım", 0m, 5_000m), (s.Satirlar[1].CariKodu, s.Satirlar[1].CariAdi, s.Satirlar[1].IslemTuru, s.Satirlar[1].BelgeNo, s.Satirlar[1].Aciklama, s.Satirlar[1].Borc, s.Satirlar[1].Alacak));
        Assert.Contains("İşlem türü tanınmadı; kasa hareketi değilse yok sayın.", s.Satirlar[6].Uyarilar);
        Assert.Equal("ERP12: 'Cari Hareket' sayfası okundu. Sütunlar: Tarih A, Cari kodu B, Cari C, İşlem türü D, Belge no E, Açıklama F, Borç G, Alacak H, Bakiye I.", s.Uyarilar[0]);
        Assert.StartsWith("5 kasa hareketi bekliyor; 3 fatura, devir", s.Uyarilar[1]);
    }

    [Fact]
    public void Birden_cok_tur_sutunu_birlestirilir_tek_tutar_turden_yonlenir()
    {
        var s = EkstreExcelOkuyucu.OkuErp(TestExcel.Xlsx(("C", [S("Fiş Tarihi", "Firma", "İşlem Türü", "Ödeme Şekli", "Tutar"), S(G(1), "A LTD", "Tahsilat", "Nakit", 10), S(G(1), "B LTD", "Ödeme", "EFT", 20), S(G(1), "C LTD", "Alış Faturası", null, 30)])));
        Assert.Equal([("Tahsilat · Nakit", true, "Giris", 10m), ("Ödeme · EFT", false, "Cikis", 0m), ("Alış Faturası", false, "Giris", 30m)], s.Satirlar.Select(r => (r.IslemTuru!, r.Nakit, r.Yon, r.Alacak)));
        Assert.Equal("ERP12 dosyasında 1 hata var; düzeltilmeden işleme alınmadı.\nSatır 2, Tutar (D2): borç mu alacak mı belli değil; Borç ve Alacak sütunlarıyla ya da tahsilat/ödeme türüyle dışa aktarın.",
            Red(TestExcel.Xlsx(("C", [S("Tarih", "Firma", "Tür", "Tutar"), S(G(1), "A", "Diğer", 5)]))));
    }

    [Fact]
    public void Hatali_dosya_butun_hatalarla_reddedilir_baslik_yoksa_gorulenler_yazar()
    {
        Assert.Equal(
        [
            "ERP12 dosyasında 6 hata var; düzeltilmeden işleme alınmadı.", "Satır 2, Tarih (A2): tarih boş.", "Satır 3, Cari (C3): cari boş.", "Satır 4: Borç ve Alacak ikisi birden dolu; yalnız biri yazılmalı.",
            "Satır 5: tutar eksi olamaz.", "Satır 6, Cari (C6): hücrede Excel hatası var.", "Satır 6, Cari (C6): cari boş.",
        ], Red(Dosya(S(null, "1", "A", "Ödeme", null, null, 5), S(G(1), "1", null, "Ödeme", null, null, 5), S(G(1), "1", "A", "Ödeme", null, null, 5, 6), S(G(1), "1", "A", "Ödeme", null, null, -5), S(G(1), "1", new TestExcel.Hata("#N/A"), "Ödeme", null, null, 5)))
            .Split('\n'));
        Assert.Contains("Görülen başlıklar: Tarih, Açıklama, Tutar.", Red(TestExcel.Xlsx(("C", [S("Tarih", "Açıklama", "Tutar"), S(G(1), "x", 1)]))));
        Assert.StartsWith("Borç/Alacak, işlem türüyle çelişiyor: tahsilat ya da ödeme yazan 3 satırın 3 tanesinde yön ters.",
            Red(Dosya(S(G(1), "1", "A", "Tahsilat", null, null, 5), S(G(1), "1", "B", "Tahsilat", null, null, 6), S(G(1), "1", "C", "Ödeme", null, null, null, 7))));
        var tek = Oku(S(G(1), "1", "A", "Tahsilat", null, null, 5), S(G(1), "1", "B", "Tahsilat", null, null, null, 6), S(G(1), "1", "C", "Ödeme", null, null, 7));
        Assert.Equal(["Belirsiz", "Giris", "Cikis"], tek.Satirlar.Select(r => r.Yon));
    }

    // ---- Yükleme ----

    [Fact]
    public async Task Yukleme_kasayi_degistirmez_bekleyen_yalniz_kasa_hareketleridir_yinelenen_hareket_alinmaz()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var once = await Kasa(c);
        var d = await Yukle(c, Dosya(S(G(9), "1", "AHMET", "Satış Faturası", "F1", null, 100), S(G(8), "1", "AHMET", "Tahsilat Nakit", "T1", null, null, 40), S(G(8), "1", "AHMET", "Tahsilat Nakit", "T1", null, null, 40)));
        Assert.Equal((EkstreKaynaklari.Erp, "ERP12", "", 2, false), (d.Kaynak, d.Banka, d.HesapAdi, d.Satirlar.Count, d.YenidenOkunabilir));
        Assert.Equal(("AHMET", "Tahsilat Nakit · Belge T1", EkstreIslemTurleri.Gelir, "TRY"), (d.Satirlar[0].Aciklama, d.Satirlar[0].EkBilgi, d.Satirlar[0].OnerilenIslem, d.Satirlar[0].ParaBirimi));
        Assert.Equal(once, await Kasa(c));
        // Örtüşen ikinci dosya: aynı iki hareket alınmaz, aynı içerikten üçüncüsü ve yenisi alınır, tutarı değişen belge uyarılır, kaybolan sayılır.
        var d2 = await Yukle(c, Dosya(S(G(8), "1", "AHMET", "Tahsilat Nakit", "T1", null, null, 40), S(G(8), "1", "AHMET", "Tahsilat Nakit", "T1", null, null, 40), S(G(8), "1", "AHMET", "Tahsilat Nakit", "T1", null, null, 40),
            S(G(9), "1", "Ahmet", "Satış Faturası", "F1", null, 120), S(G(7), "1", "AHMET", "Ödeme Nakit", "F1", null, 5)));
        Assert.Equal([40m, 5m], d2.Satirlar.Select(s => s.Tutar!.Value));
        Assert.Contains("2 hareket daha önce aktarıldığı için yeniden alınmadı; 3 yeni hareket var.", d2.Uyarilar);
        Assert.Contains(d2.Uyarilar, u => u.StartsWith("Önceki aktarımlardan 1 hareket"));
        Assert.Contains(d2.Satirlar[1].Uyarilar, u => u.StartsWith("Aynı belge no (F1) önce") && u.Contains("100,00 TL"));
        Assert.Equal(2, d2.Atlananlar.Count(a => a.Neden == "Daha önce aktarıldı"));
        var hepsi = await Gonder(c, Dosya(S(G(8), "1", "AHMET", "Tahsilat Nakit", "T1", null, null, 40), S(null, null, "TOPLAM", null, null, null, null, 40)));
        Assert.Equal(HttpStatusCode.Conflict, hepsi.StatusCode);
        Assert.Contains("tamamı daha önce aktarılmış", await hepsi.Content.ReadAsStringAsync(Ct));
        Assert.Contains("Excel", await (await Gonder(c, Encoding.ASCII.GetBytes("%PDF-1.7 x"))).Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/ekstre-aktar/{d.Id}/yeniden-oku", new EkstreYenidenOkuYaz(Guid.NewGuid(), d.Surum), Ct)).StatusCode);
    }

    // ---- Eşleştirme ----

    [Fact]
    public async Task Banka_gideri_ve_geliri_kesin_eslesir_kasa_degismez_cari_adi_gorunur_iptal_edilir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var banka = await Banka(c, S(G(5), "FAST AHMET YILMAZ", -5_000), S(G(5), "GELEN EFT DENİZ TİCARET LTD", 700), S(G(5), "KİRA", -5_000));
        banka = (await Kaydet(c, banka, Satir(banka, 1), Satir(banka, 2, "Gelir"), Satir(banka, 3))).Belge;
        var once = await Kasa(c);
        var d = await Yukle(c, Dosya(S(G(4), "320", "Ahmet Yılmaz Ltd. Şti.", "Havale Ödeme", "O1", null, 5_000), S(G(6), "120", "DENİZ TİCARET", "Gelen EFT", "T1", null, null, 700), S(G(1), "120", "UZAK TARİH", "Havale Ödeme", "O2", null, 5_000)));
        var o = await Oneriler(c, d);
        Assert.Equal([ErpOneriDurumlari.Eslesme, ErpOneriDurumlari.Eslesme, ErpOneriDurumlari.Bekle], o.Select(x => x.Durum));
        Assert.Equal((EkstreIslemTurleri.Eslestir, EslesmeTurleri.Gider, 2), (o[0].IslemTuru, o[0].Adaylar![0].Tur, o[0].Adaylar!.Count));
        Assert.Equal("FAST AHMET YILMAZ", o[0].Adaylar![0].Aciklama);
        Assert.Equal((EslesmeTurleri.EkstreGelir, banka.Kayitlar.Single(k => k.IslemTuru == "Gelir").Id), (o[1].Adaylar![0].Tur, o[1].Adaylar![0].Id));
        Assert.Contains("banka ya da kart ekstresi yüklenince eşleşir", o[2].Aciklama);

        var (kayitli, onizleme) = await Kaydet(c, d, Satir(d, 1, "Eslestir", o[0].Adaylar![0].Tur, o[0].Adaylar![0].Id), Satir(d, 2, "Eslestir", o[1].Adaylar![0].Tur, o[1].Adaylar![0].Id));
        Assert.Equal((0m, false), (onizleme.KasaEtkisi, onizleme.TekrarOnayGerekli));
        Assert.Equal(once, await Kasa(c));
        Assert.All(kayitli.Kayitlar, k => Assert.Equal(EslesmeDurumlari.Eslesti, k.EslesmeDurumu));
        // Cari adı İşlemler'de ve ekstrenin kapanan satırlarında.
        var islemler = (await c.GetFromJsonAsync<List<JsonElement>>("/api/islemler", Ct))!;
        Assert.Equal(["Ahmet Yılmaz Ltd. Şti.", null], islemler.OrderBy(i => i.GetProperty("cari").GetString()).Select(i => i.GetProperty("erpCari").GetString()));
        var guncel = (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{banka.Id}", Ct))!;
        Assert.Equal(["Ahmet Yılmaz Ltd. Şti.", "DENİZ TİCARET", null], guncel.Kayitlar.OrderBy(k => k.SatirNo).Select(k => k.ErpCari));

        // Eşleşmiş kayıt ikinci ERP12 satırına bağlanmaz.
        var d2 = await Yukle(c, Dosya(S(G(5), "999", "AHMET YILMAZ BAŞKA", "Havale Ödeme", "O9", null, 5_000)));
        Assert.Equal(ErpOneriDurumlari.Secim, (await Oneriler(c, d2)).Single().Durum);
        Assert.Contains("başka bir ERP12 satırıyla eşleşti", await Reddet(c, $"/api/ekstre-aktar/{d2.Id}/onizleme", new EkstreKaydetYaz(Guid.NewGuid(), d2.Surum, [Satir(d2, 1, "Eslestir", o[0].Adaylar![0].Tur, o[0].Adaylar![0].Id)])));
        // İptal bağı kaldırır; kasa değişmez, öneri geri gelir.
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/kayitlar/{kayitli.Kayitlar.Single(k => k.SatirNo == 1).Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış"));
        Assert.Equal(once, await Kasa(c));
        Assert.Equal(ErpOneriDurumlari.Eslesme, (await Oneriler(c, d))[0].Durum);
    }

    [Fact]
    public async Task Adi_uyusmayan_ya_da_paylasilan_aday_kesin_sayilmaz_eslesmeyen_hedef_reddedilir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var banka = await Banka(c, S(G(5), "POS HARCAMA", -100), S(G(5), "FAST DENİZ YILDIZ", -200), S(G(5), "GELEN FAST X", 100));
        banka = (await Kaydet(c, banka, Satir(banka, 1), Satir(banka, 2), Satir(banka, 3, "Gelir"))).Belge;
        var d = await Yukle(c, Dosya(S(G(5), "1", "AHMET", "Havale Ödeme", "1", null, 100), S(G(5), "2", "DENİZ A.Ş.", "Havale Ödeme", "2", null, 200), S(G(4), "3", "DENİZ LTD", "Havale Ödeme", "3", null, 200)));
        var o = await Oneriler(c, d);
        Assert.All(o, x => Assert.Equal(ErpOneriDurumlari.Secim, x.Durum));
        Assert.Single(o[0].Adaylar!);
        var yol = $"/api/ekstre-aktar/{d.Id}/onizleme";
        var gelir = banka.Kayitlar.Single(k => k.IslemTuru == "Gelir").Id;
        Assert.Contains("eşleşmiyor", await Reddet(c, yol, new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, [Satir(d, 1, "Eslestir", EslesmeTurleri.EkstreGelir, gelir)])));
        Assert.Contains("eşleşmiyor", await Reddet(c, yol, new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, [Satir(d, 2, "Eslestir", o[0].Adaylar![0].Tur, o[0].Adaylar![0].Id)])));
        Assert.Contains("geçerli işlem türü", await Reddet(c, yol, new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, [Satir(d, 1, "KartOdemesi")])));
        var adaylar = await Post<List<EkstreEslesmeAdayiDto>>(c, $"/api/ekstre-aktar/{d.Id}/eslesme-adaylari", new EkstreEslesmeAdayiSorgu(G(5), 200m, 2));
        Assert.Equal("FAST DENİZ YILDIZ", Assert.Single(adaylar).Aciklama);
        Assert.Contains("Kaynak satır", await Reddet(c, $"/api/ekstre-aktar/{d.Id}/eslesme-adaylari", new EkstreEslesmeAdayiSorgu(G(5), 200m)));
    }

    [Fact]
    public async Task Nakit_satir_ekstreyle_eslesmez_elle_girilen_giderle_eslesir_kaydedilirse_kasayi_etkiler_hafiza_ogrenir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var banka = await Banka(c, S(G(5), "FAST KIRTASİYE DÜNYASI", -350));
        await Kaydet(c, banka, Satir(banka, 1));
        var d = await Yukle(c, Dosya(S(G(5), "1", "KIRTASİYE DÜNYASI", "Nakit Ödeme", "1", null, 350), S(G(4), "2", "TEMİZLİK AYŞE", "Nakit Ödeme", "2", null, 80)));
        var o = await Oneriler(c, d);
        Assert.Equal([ErpOneriDurumlari.Nakit, ErpOneriDurumlari.Nakit], o.Select(x => x.Durum));
        Assert.Equal((EkstreIslemTurleri.Gider, (string?)null), (o[0].IslemTuru, o[0].DagilimTuru));
        (await c.PostAsJsonAsync("/api/islemler", new { tarih = G(4), cari = "Temizlik", tutarTl = 80m, kanal = "MEZAT", tip = "Cari" }, Ct)).EnsureSuccessStatusCode();
        var elle = (await Oneriler(c, d))[1];
        Assert.Equal((ErpOneriDurumlari.Eslesme, EslesmeTurleri.Gider), (elle.Durum, Assert.Single(elle.Adaylar!).Tur));

        var once = await Kasa(c);
        var (kayitli, onizleme) = await Kaydet(c, d, Satir(d, 1));
        Assert.Equal(-350m, onizleme.KasaEtkisi);
        Assert.Equal(once - 350m, await Kasa(c));
        var islem = (await c.GetFromJsonAsync<List<JsonElement>>("/api/islemler", Ct))!.Single(i => i.GetProperty("not").GetString() == "ERP12 cari hareketi");
        Assert.Equal("KIRTASİYE DÜNYASI", islem.GetProperty("cari").GetString());
        // ERP12'den yazılan gider başka ERP12 satırına aday olmaz; hafıza aynı carinin nakdini öğrenir.
        var d2 = await Yukle(c, Dosya(S(G(3), "1", "KIRTASİYE DÜNYASI", "Nakit Ödeme", "7", null, 350)));
        var h = (await Oneriler(c, d2)).Single();
        Assert.Equal((EkstreHafizaDurumlari.Hazir, EkstreIslemTurleri.Gider, DagilimBicimleri.Genel), (h.Durum, h.IslemTuru, h.DagilimTuru));
        // İptal kasayı geri alır.
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/kayitlar/{kayitli.Kayitlar[0].Id}/iptal", new EkstreIptalYaz(Guid.NewGuid(), "Yanlış"));
        Assert.Equal(once, await Kasa(c));
    }

    [Fact]
    public async Task Nakit_olmayan_satir_kasaya_yazilacaksa_cift_sayim_uyarilir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var d = await Yukle(c, Dosya(S(G(5), "1", "DENİZ", "Havale Ödeme", "1", null, 350)));
        var onizleme = await Post<EkstreOnizlemeDto>(c, $"/api/ekstre-aktar/{d.Id}/onizleme", new EkstreKaydetYaz(Guid.NewGuid(), d.Surum, [Satir(d, 1)]));
        Assert.True(onizleme.TekrarOnayGerekli);
        Assert.Contains(onizleme.Satirlar[0].Uyarilar, u => u.StartsWith("Bu ERP12 hareketi nakit görünmüyor."));
    }

    [Fact]
    public async Task Cek_satiri_yalniz_cek_kaydiyla_eslesir_silinen_cek_kayit_yok_gosterir()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var cek = await Post<CekDto>(c, "/api/takip/cekler", new CekYaz(Guid.NewGuid(), 0, CekTurleri.Cek, CekYonleri.Verilen, "777", "Halk", "Deniz Ticaret", 8_000m, Bugun.AddDays(60), KanalEtiketleri.Ortak, false, null, null));
        var d = await Yukle(c, Dosya(S(G(5), "1", "DENİZ TİCARET LTD", "Verilen Çek", "C1", null, 8_000), S(G(5), "2", "BAŞKA", "Alınan Çek", "C2", null, null, 8_000)));
        var o = await Oneriler(c, d);
        Assert.Equal([ErpOneriDurumlari.Eslesme, ErpOneriDurumlari.Bekle], o.Select(x => x.Durum));
        Assert.Equal((EslesmeTurleri.CekKaydi, cek.Id), (o[0].Adaylar![0].Tur, o[0].Adaylar![0].Id));
        Assert.Contains("Çekler ekranında karşılığı yok", o[1].Aciklama);
        var (kayitli, _) = await Kaydet(c, d, Satir(d, 1, "Eslestir", EslesmeTurleri.CekKaydi, cek.Id));
        Assert.Equal(EslesmeDurumlari.Eslesti, kayitli.Kayitlar.Single().EslesmeDurumu);
        using var sil = new HttpRequestMessage(HttpMethod.Delete, $"/api/takip/cekler/{cek.Id}") { Content = JsonContent.Create(new CekSilYaz(Guid.NewGuid(), cek.Surum)) };
        Assert.Equal(HttpStatusCode.NoContent, (await c.SendAsync(sil, Ct)).StatusCode);
        Assert.Equal(EslesmeDurumlari.KayitYok, (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{d.Id}", Ct))!.Kayitlar.Single().EslesmeDurumu);
    }

    // ---- Cariler ----

    [Fact]
    public async Task Cari_listesi_bakiye_bekleyen_ve_durumlari_gosterir_arar_izleyici_okur()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var d = await Yukle(c, Dosya(S(G(9), "120", "Ahmet", "Satış Faturası", "F1", null, 1_000), S(G(8), "120", "AHMET YILMAZ", "Tahsilat Nakit", "T1", null, null, 400), S(G(7), "120", "AHMET YILMAZ", "Tahsilat Nakit", "T2", null, null, 100),
            S(G(6), null, "Ömer İnşaat", "Nakit Ödeme", "O1", null, 50), S(G(5), null, "ömer inşaat", "Nakit Ödeme", "O2", null, 60)));
        await Kaydet(c, d, Satir(d, 1, "Gelir"));
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/yok-say", new EkstreYokSayYaz(Guid.NewGuid(), d.Surum + 1, [2], true));
        var liste = (await c.GetFromJsonAsync<List<ErpCariDto>>("/api/erp/cariler", Ct))!;
        Assert.Equal([new("K:120", "120", "AHMET YILMAZ", 1_000m, 500m, 500m, 3, 0, G(7)), new("A:OMER INSAAT", null, "ömer inşaat", 110m, 0m, 110m, 2, 2, G(5))], liste);
        Assert.Equal("A:OMER INSAAT", Assert.Single((await c.GetFromJsonAsync<List<ErpCariDto>>("/api/erp/cariler?ara=OMER", Ct))!).Anahtar);
        var hareketler = (await c.GetFromJsonAsync<List<ErpCariHareketDto>>("/api/erp/cariler/hareketler?anahtar=K%3A120", Ct))!;
        Assert.Equal([(ErpHareketDurumlari.YokSayildi, "T2"), (ErpHareketDurumlari.Kaydedildi, "T1"), (ErpHareketDurumlari.KasaDisi, "F1")], hareketler.Select(h => (h.Durum, h.BelgeNo!)));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/erp/cariler/hareketler", Ct)).StatusCode);
        (await c.PutAsJsonAsync("/api/ayarlar/izleyici-sifre", new { yeniSifre = "izleyici-erp-sifresi" }, Ct)).EnsureSuccessStatusCode();
        using var izleyici = f.CreateClient();
        (await izleyici.PostAsJsonAsync("/api/auth/login", new { kullanici = "", sifre = "izleyici-erp-sifresi" }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await izleyici.GetAsync("/api/erp/cariler", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await izleyici.DeleteAsync($"/api/ekstre-aktar/{d.Id}/erp", Ct)).StatusCode);
    }

    [Fact]
    public async Task Islenmemis_erp_belgesi_silinir_ayni_dosya_yeniden_yuklenir_islenmis_silinmez()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        var dosya = Dosya(S(G(9), "1", "AHMET", "Nakit Ödeme", "1", null, 100), S(G(9), "1", "AHMET", "Satış Faturası", "2", null, 100));
        var d = await Yukle(c, dosya);
        await Post<EkstreBelgeDto>(c, $"/api/ekstre-aktar/{d.Id}/yok-say", new EkstreYokSayYaz(Guid.NewGuid(), d.Surum, [1], true));
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/ekstre-aktar/{d.Id}/erp", Ct)).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<List<ErpCariDto>>("/api/erp/cariler", Ct))!);
        d = await Yukle(c, dosya);
        await Kaydet(c, d, Satir(d, 1));
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/ekstre-aktar/{d.Id}/erp", Ct)).StatusCode);
        var banka = await Banka(c, S(G(5), "X", -1));
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/ekstre-aktar/{banka.Id}/erp", Ct)).StatusCode);
    }

    /// <summary>ERP12 bağı zayıftır: eşleşen kart harcaması "ekstreyle eşleşti" diye kilitlenmez.</summary>
    [Fact]
    public async Task Erp_bagi_kart_harcamasini_kilitlemez()
    {
        await using var f = KasaWebFactory.Sabit(Bugun);
        using var c = await Editor(f);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasaDbContext>();
        var kart = new KrediKartiEntity { Ad = "Kart", KesimTarihi = new(2026, 1, 10), SonOdemeTarihi = new(2026, 1, 20), Limit = 10_000m };
        db.KrediKartlari.Add(kart);
        db.SaveChanges();
        db.TakipKartlar.Add(new() { KrediKartiId = kart.Id, Baslangic = new DateOnly(Bugun.Year, 1, 1) });
        db.SaveChanges();
        var harcama = new TakipHarcamaEntity { KrediKartiId = kart.Id, Tarih = G(5), Aciklama = "DENİZ", Tutar = 90m, TaksitSayisi = 1, DagilimJson = "[]" };
        db.TakipHarcamalar.Add(harcama);
        db.SaveChanges();
        var d = await Yukle(c, Dosya(S(G(5), "1", "DENİZ TİCARET", "Kredi Kartı Ödeme", "1", null, 90)));
        var o = (await Oneriler(c, d)).Single();
        Assert.Equal((ErpOneriDurumlari.Eslesme, EslesmeTurleri.KartHarcama, harcama.Id), (o.Durum, o.Adaylar![0].Tur, o.Adaylar![0].Id));
        await Kaydet(c, d, Satir(d, 1, "Eslestir", EslesmeTurleri.KartHarcama, harcama.Id));
        Assert.Equal(1, db.EkstreKayitlar.AsNoTracking().Count(k => k.EslesmeTuru == EslesmeTurleri.KartHarcama && !k.Iptal));
        Assert.Equal(0, db.EkstreKayitlar.AsNoTracking().Count(k => k.KrediKartiId != null));
        db.ChangeTracker.Clear();
        var surum = db.TakipKartlar.AsNoTracking().Single().Surum;
        var iptal = await c.PostAsJsonAsync($"/api/takip/kartlar/{kart.Id}/harcamalar/{harcama.Id}/iptal", new TakipIptalYaz(Guid.NewGuid(), surum, "Yanlış"), Ct);
        Assert.True(iptal.IsSuccessStatusCode, await iptal.Content.ReadAsStringAsync(Ct));
        Assert.Equal(EslesmeDurumlari.KayitYok, (await c.GetFromJsonAsync<EkstreBelgeDto>($"/api/ekstre-aktar/{d.Id}", Ct))!.Kayitlar.Single().EslesmeDurumu);
    }
}
