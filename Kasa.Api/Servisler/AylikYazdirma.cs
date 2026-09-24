using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Kasa.Api.Data;
using Kasa.Core;
using Microsoft.EntityFrameworkCore;

namespace Kasa.Api.Servisler;

/// <summary>Yazdırılabilir aylık raporun verisi (bkz. <see cref="AylikYazdirma"/>).</summary>
public sealed record AylikYazdirVerisi(
    int Yil,
    int Ay,
    AylikRapor Rapor,
    KasaDokumu? Dokum,
    DateOnly Tarih,
    IReadOnlyList<AylikYazdirKart> Kartlar,
    IReadOnlyList<CekEntity> AlinanPortfoy,
    IReadOnlyList<CekEntity> VerilenPortfoy,
    KasaSayimEntity? SonSayim,
    bool Kilitli,
    DateTime? YayinZamaniUtc,
    DateTime OlusturmaUtc);

public sealed record AylikYazdirKart(string Ad, decimal Borc, decimal EkstreBorc, decimal Limit);

/// <summary>
/// Aylık raporun tek sayfalık (A4) yazdırılabilir HTML'i. PDF kütüphanesi yok: tarayıcı yazdırır ya da
/// PDF'e kaydeder. Kullanıcı metinleri (kanal, kart, kişi, not) HTML kaçışlıdır. Sayfa kendi sıkı
/// CSP'sini taşır: yalnız satır içi stil ve özeti (SHA-256) sabit tek yazdır betiği çalışır.
/// </summary>
public static class AylikYazdirma
{
    private static readonly CultureInfo Tr = Metin.Tr;

    /// <summary>Sayfadaki tek betik: "Yazdır" düğmesi window.print() çağırır.</summary>
    public const string YazdirBetigi = "document.getElementById('yazdir').addEventListener('click',function(){window.print();});";

    public static readonly string BetikOzeti = "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(YazdirBetigi)));

    /// <summary>Belgenin içindeki (meta) CSP. frame-ancestors meta'da geçersiz olduğundan yalnız başlıkta.</summary>
    public static readonly string MetaCsp =
        $"default-src 'none'; style-src 'unsafe-inline'; script-src '{BetikOzeti}'; img-src data:; base-uri 'none'; form-action 'none'";

    /// <summary>HTTP yanıt başlığındaki CSP.</summary>
    public static readonly string BaslikCsp = MetaCsp + "; frame-ancestors 'none'";

    public const string IcerikTipi = "text/html; charset=utf-8";

    public static string DosyaAdi(int yil, int ay) => $"kasa-aylik-rapor-{yil:D4}-{ay:D2}.html";

    /// <summary>
    /// Raporun verisini toplar. Kart borçları ve çek portföyü ayın son gününde (ay bitmediyse bugün)
    /// geçerli haliyle gösterilir. Portföy o güne göre yeniden kurulur: düzenleme tarihi o günden önce
    /// olan ve bugün hâlâ portföyde ya da işlem (tahsil/ödeme/ciro) tarihi o günden sonra olan çekler.
    /// İade/karşılıksız çeklerin çıkış günü tutulmadığından bunlar portföyde sayılmaz.
    /// </summary>
    public static AylikYazdirVerisi Topla(KasaDbContext db, RaporServisi rapor, HesapServisi hesap, TimeProvider saat, int yil, int ay)
    {
        var ayBasi = new DateOnly(yil, ay, 1);
        var aySonu = AyBicimi.AySonu(ayBasi);
        var bugun = Saat.Bugun(saat);
        var tarih = aySonu < bugun ? aySonu : bugun;

        var harcamalar = db.Islemler.AsNoTracking().Where(i => i.KrediKartiId != null && i.Tarih <= tarih)
            .Select(i => new { Id = i.KrediKartiId!.Value, i.Tarih, i.TutarTl }).ToList()
            .ToLookup(x => x.Id, x => new KartHarcama(x.Tarih, x.TutarTl));
        var odemeler = db.KartOdemeler.AsNoTracking().Where(o => o.Tarih <= tarih)
            .Select(o => new { o.KrediKartiId, o.Tarih, o.Tutar }).ToList()
            .ToLookup(x => x.KrediKartiId, x => new KartOdeme(x.Tarih, x.Tutar));
        var kartlar = db.KrediKartlari.AsNoTracking().ToList().OrderBy(k => k.Ad, Metin.Sirala)
            .Select(k =>
            {
                var d = KartHesap.Durum(k.Borc, harcamalar[k.Id], odemeler[k.Id], k.KesimTarihi.Day, tarih);
                return new AylikYazdirKart(k.Ad, d.GuncelBorc, d.EkstreBorc, k.Limit);
            }).ToList();

        var portfoy = db.Cekler.AsNoTracking().Where(c => c.DuzenlemeTarihi <= tarih).ToList()
            .Where(c => c.Durum == CekDurumu.Portfoyde || (c.IslemTarihi is { } t && t > tarih))
            .OrderBy(c => c.VadeTarihi).ThenBy(c => c.Id).ToList();
        var sayim = db.KasaSayimlari.AsNoTracking().Where(s => s.Tarih <= aySonu)
            .OrderByDescending(s => s.Tarih).ThenByDescending(s => s.Id).FirstOrDefault();
        var kilitli = db.AyKilitleri.AsNoTracking().Any(k => k.Ay == ayBasi);
        var yayin = db.AyYayinlari.AsNoTracking().Where(y => y.Ay == ayBasi).Select(y => (DateTime?)y.YayinZamaniUtc).FirstOrDefault();

        return new AylikYazdirVerisi(yil, ay, hesap.Aylik(yil, ay), rapor.AyinKasaDokumu(yil, ay), tarih, kartlar,
            portfoy.Where(c => c.Yon == CekYonu.Alinan).ToList(), portfoy.Where(c => c.Yon == CekYonu.Verilen).ToList(),
            sayim, kilitli, yayin, saat.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// HTML kaçışı: &amp; &lt; &gt; " ' karakterleri varlığa çevrilir (metin ve çift tırnaklı öznitelik
    /// içinde güvenli). Türkçe harfler olduğu gibi kalır (belge UTF-8).
    /// </summary>
    public static string E(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 16);
        foreach (var ch in s)
            sb.Append(ch switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => ch.ToString(),
            });
        return sb.ToString();
    }
    private static string Tl(decimal d) => d.ToString("#,##0.00", Tr) + " ₺";
    private static string Tarih(DateOnly d) => d.ToString("dd.MM.yyyy", Tr);
    private static string Sinif(decimal d) => d < 0 ? " class=\"eksi\"" : "";

    /// <summary>Portföy listesinde yön başına gösterilen en yakın vadeli çek sayısı (tek sayfa).</summary>
    public const int PortfoyListeSiniri = 6;

    public static string Olustur(AylikYazdirVerisi v)
    {
        var etiket = AyBicimi.Etiket(v.Yil, v.Ay);
        var olusturma = Saat.Simdi(v.OlusturmaUtc);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"tr\">\n<head>\n<meta charset=\"utf-8\">\n");
        // Sabit (güvenilir) metin; tek tırnaklar çift tırnaklı öznitelikte güvenlidir.
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"").Append(MetaCsp).Append("\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>Emar Kasa · ").Append(E(etiket)).Append(" aylık rapor</title>\n");
        sb.Append("<style>\n").Append(Stil).Append("</style>\n</head>\n<body>\n");

        // Başlık
        sb.Append("<header><div><h1>").Append(E(etiket)).Append(" · Aylık rapor</h1>");
        sb.Append("<p class=\"alt\">Emar Kasa · kasa.emarglobal.com · Hazırlanma: ")
          .Append(E(olusturma.ToString("dd.MM.yyyy HH:mm", Tr)));
        var durum = new List<string>();
        if (v.Kilitli) durum.Add("Ay kilitli");
        if (v.YayinZamaniUtc is { } y) durum.Add("Yayınlandı: " + Saat.Simdi(y).ToString("dd.MM.yyyy HH:mm", Tr));
        if (durum.Count > 0) sb.Append(" · ").Append(E(string.Join(" · ", durum)));
        sb.Append("</p></div><button id=\"yazdir\" type=\"button\">Yazdır</button></header>\n");

        // Kanal kârlılığı
        var l = v.Rapor.Kanallar;
        sb.Append("<section><h2>Kanal kârlılığı</h2><table><thead><tr><th>Kanal</th><th>Gelen</th><th>Çek tahs.</th>")
          .Append("<th>Cari</th><th>Sabit</th><th>K.K</th><th>Ortak pay</th><th>Çek öd.</th><th>Ay sonucu</th></tr></thead><tbody>");
        foreach (var k in l)
            sb.Append("<tr><td>").Append(E(k.Kanal)).Append("</td>")
              .Append(Hucre(k.Gelen)).Append(Hucre(k.CekGelen)).Append(Hucre(k.CariGiden)).Append(Hucre(k.SabitGider))
              .Append(Hucre(k.KrediKarti)).Append(Hucre(k.OrtakPay)).Append(Hucre(k.CekGiden))
              .Append("<td").Append(Sinif(k.AySonucu)).Append("><b>").Append(Tl(k.AySonucu)).Append("</b></td></tr>");
        if (l.Count == 0) sb.Append("<tr><td colspan=\"9\">Kanal yok.</td></tr>");
        sb.Append("</tbody><tfoot><tr><td>Toplam</td>")
          .Append(Hucre(l.Sum(k => k.Gelen))).Append(Hucre(l.Sum(k => k.CekGelen))).Append(Hucre(l.Sum(k => k.CariGiden)))
          .Append(Hucre(l.Sum(k => k.SabitGider))).Append(Hucre(l.Sum(k => k.KrediKarti))).Append(Hucre(l.Sum(k => k.OrtakPay)))
          .Append(Hucre(l.Sum(k => k.CekGiden)))
          .Append("<td").Append(Sinif(l.Sum(k => k.AySonucu))).Append("><b>").Append(Tl(l.Sum(k => k.AySonucu))).Append("</b></td></tr></tfoot></table></section>\n");

        // Kasa + kasa neden değişti
        sb.Append("<div class=\"iki\"><section><h2>Kasa</h2>");
        if (v.Dokum is { } d)
        {
            sb.Append("<table class=\"dar\"><tbody>")
              .Append("<tr><td>Ay açılışı (").Append(Tarih(d.Baslangic)).Append(")</td>").Append(Hucre(d.Acilis)).Append("</tr>")
              .Append("<tr><td>Kasaya giren</td>").Append(Hucre(d.ToplamGiren)).Append("</tr>")
              .Append("<tr><td>Kasadan çıkan</td>").Append(Hucre(-d.ToplamCikan)).Append("</tr>")
              .Append("<tr class=\"toplam\"><td>Ay kapanışı (").Append(Tarih(d.Bitis)).Append(")</td>").Append(Hucre(d.Kapanis)).Append("</tr>")
              .Append("</tbody></table>");
            var gruplar = d.Adimlar.GroupBy(a => a.Tur).Select(g => (Tur: g.Key, Tutar: g.Sum(a => a.Tutar))).ToList();
            if (gruplar.Count > 0)
            {
                sb.Append("<h3>Kasa neden değişti?</h3><table class=\"dar\"><tbody>");
                foreach (var g in gruplar)
                    sb.Append("<tr><td>").Append(E(RaporServisi.TurAdi(g.Tur))).Append("</td>").Append(Hucre(g.Tutar)).Append("</tr>");
                sb.Append("</tbody></table>");
            }
        }
        else sb.Append("<p>Bu ay takip dönemlerinin dışında.</p>");
        sb.Append("</section>");

        // Kart borçları
        sb.Append("<section><h2>Kart borçları (").Append(Tarih(v.Tarih)).Append(")</h2>");
        if (v.Kartlar.Count == 0) sb.Append("<p>Kayıtlı kart yok.</p>");
        else
        {
            sb.Append("<table class=\"dar\"><thead><tr><th>Kart</th><th>Borç</th><th>Ekstre</th><th>Limit</th></tr></thead><tbody>");
            foreach (var k in v.Kartlar)
                sb.Append("<tr><td>").Append(E(k.Ad)).Append("</td>").Append(Hucre(k.Borc)).Append(Hucre(k.EkstreBorc))
                  .Append(Hucre(k.Limit)).Append("</tr>");
            sb.Append("</tbody><tfoot><tr><td>Toplam</td>").Append(Hucre(v.Kartlar.Sum(k => k.Borc)))
              .Append(Hucre(v.Kartlar.Sum(k => k.EkstreBorc))).Append("<td></td></tr></tfoot></table>");
        }
        sb.Append("</section></div>\n");

        // Çek portföyü + son sayım
        sb.Append("<div class=\"iki\"><section><h2>Çek portföyü (").Append(Tarih(v.Tarih)).Append(")</h2>");
        Portfoy(sb, "Alınan (portföyde)", v.AlinanPortfoy);
        Portfoy(sb, "Verilen (ödenecek)", v.VerilenPortfoy);
        sb.Append("</section><section><h2>Son kasa sayımı</h2>");
        if (v.SonSayim is { } s)
        {
            sb.Append("<table class=\"dar\"><tbody>")
              .Append("<tr><td>Tarih</td><td>").Append(Tarih(s.Tarih)).Append("</td></tr>")
              .Append("<tr><td>Sayılan</td>").Append(Hucre(s.SayilanTutar)).Append("</tr>")
              .Append("<tr><td>Defterdeki (kayıt anında)</td>").Append(Hucre(s.HesaplananTutar)).Append("</tr>")
              .Append("<tr class=\"toplam\"><td>Fark</td>").Append(Hucre(s.SayilanTutar - s.HesaplananTutar)).Append("</tr>");
            if (!string.IsNullOrWhiteSpace(s.Not))
                sb.Append("<tr><td>Not</td><td class=\"metin\">").Append(E(s.Not)).Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        else sb.Append("<p>Bu ayın sonuna kadar kasa sayımı yok.</p>");
        sb.Append("</section></div>\n");

        sb.Append("<footer>Rakamlar Emar Kasa'nın haftalık/aylık raporlarıyla aynıdır. Kart ve çek durumu ")
          .Append(Tarih(v.Tarih)).Append(" gününe göredir.</footer>\n");
        sb.Append("<script>").Append(YazdirBetigi).Append("</script>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static string Hucre(decimal d) => $"<td{Sinif(d)}>{Tl(d)}</td>";

    private static void Portfoy(StringBuilder sb, string baslik, IReadOnlyList<CekEntity> cekler)
    {
        sb.Append("<h3>").Append(E(baslik)).Append(": ").Append(cekler.Count).Append(" adet · ")
          .Append(Tl(cekler.Sum(c => c.Tutar))).Append("</h3>");
        if (cekler.Count == 0) return;
        sb.Append("<table class=\"dar\"><thead><tr><th>Vade</th><th>Kişi/firma</th><th>Tutar</th></tr></thead><tbody>");
        foreach (var c in cekler.Take(PortfoyListeSiniri))
            sb.Append("<tr><td>").Append(Tarih(c.VadeTarihi)).Append("</td><td class=\"metin\">").Append(E(Metin.Kisalt(c.Kisi, 40)))
              .Append("</td>").Append(Hucre(c.Tutar)).Append("</tr>");
        if (cekler.Count > PortfoyListeSiniri)
            sb.Append("<tr><td colspan=\"3\">… ve ").Append(cekler.Count - PortfoyListeSiniri).Append(" çek daha</td></tr>");
        sb.Append("</tbody></table>");
    }

    private const string Stil = """
        @page { size: A4; margin: 12mm; }
        * { box-sizing: border-box; }
        body { font-family: "Segoe UI", Arial, sans-serif; font-size: 10pt; color: #1b1f1d; margin: 0 auto; max-width: 190mm; padding: 8mm 0; }
        header { display: flex; justify-content: space-between; align-items: flex-start; border-bottom: 2px solid #1f7a4d; padding-bottom: 4pt; margin-bottom: 8pt; }
        h1 { font-size: 16pt; margin: 0; }
        h2 { font-size: 11pt; margin: 8pt 0 4pt; color: #1f7a4d; }
        h3 { font-size: 9.5pt; margin: 6pt 0 3pt; }
        .alt { margin: 2pt 0 0; color: #5f6b66; font-size: 8.5pt; }
        table { width: 100%; border-collapse: collapse; }
        th, td { padding: 2pt 4pt; border-bottom: 1px solid #e3e8e5; text-align: right; white-space: nowrap; }
        th:first-child, td:first-child, td.metin { text-align: left; }
        td.metin { white-space: normal; }
        thead th { font-size: 8.5pt; color: #5f6b66; font-weight: 600; }
        tfoot td, tr.toplam td { font-weight: 700; border-top: 1px solid #1b1f1d; }
        .eksi { color: #b3261e; }
        .iki { display: flex; gap: 10mm; }
        .iki > section { flex: 1; min-width: 0; }
        table.dar td, table.dar th { font-size: 9pt; }
        p { margin: 2pt 0; }
        footer { margin-top: 10pt; color: #5f6b66; font-size: 8pt; border-top: 1px solid #e3e8e5; padding-top: 4pt; }
        button { font: inherit; padding: 4pt 12pt; border: 1px solid #1f7a4d; background: #1f7a4d; color: #fff; border-radius: 4pt; cursor: pointer; }
        @media print { button { display: none; } body { padding: 0; } }

        """;
}
