using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kasa.Api.Servisler;
using Kasa.Core.Kodlar;

namespace Kasa.Api;

/// <summary>Conservative layout parser: amounts with ambiguous columns remain editable, never silently guessed.</summary>
public static class EkstreMetinOkuyucu
{
    private static readonly TimeSpan RegexLimit = TimeSpan.FromSeconds(1);
    private static Regex Rx(string pattern) => new(pattern, RegexOptions.CultureInvariant, RegexLimit);
    private static readonly Regex DateRx = Rx(@"(?<!\d)(?:\d{4}-\d{2}-\d{2}|\d{1,2}[./-]\d{1,2}[./-](?:\d{4}|\d{2}))(?!\d)");
    private static readonly Regex ShortDateRx = Rx(@"^\s*\d{1,2}[./]\d{1,2}(?![\d./])");
    private static readonly Regex ShortDateRangeRx = Rx(@"(?<![\d./])\d{1,2}[./]\d{1,2}\s*[-–—]\s*\d{1,2}[./]\d{1,2}(?![\d./])");
    private static readonly Regex MoneyRx = Rx(@"(?<![\p{L}\d.,])(?<sign>[+-]?)(?<n>(?:\d{1,3}(?:\.\d{3})+|\d+),\d{2}|(?:\d{1,3}(?:,\d{3})+|\d+)\.\d{2})(?<tail>[+-]?)(?:\s*(?<direction>B|A))?(?![\p{L}\d.,])");
    private static readonly Regex ColumnsRx = Rx(@"ISLEM TUTARI|BORC TUTARI|ALACAK TUTARI|BAKIYE|BORC|ALACAK|TUTAR");
    private static readonly Regex CurrencyRx = Rx(@"\b(USD|EUR|GBP|CHF|JPY|AUD|CAD|TRY|TL)\b|[€$£₺]");
    private static readonly Regex CurrencyLabelRx = Rx(@"PARA BIRIMI|HESAP CINSI|DOVIZ CINSI");
    // Etiket değerinin alanı kolon boşluğunda (2+ boşluk) ya da sonraki "Etiket:" başlangıcında biter.
    private static readonly Regex LabelFieldEndRx = Rx(@"\s{2,}|\s[^\s:]+\s*:");
    private static readonly Regex CurrencyNameRx = Rx(@"\b(?:TURK LIRASI|KANADA DOLARI|AVUSTRALYA DOLARI|DOLAR|DOLARI|EURO|AVRO|STERLIN|STERLINI)\b");
    // Adres kısaltması parantez içinde yazılmaz; "(TL)" gibi kod belge para birimidir.
    private static readonly Regex CurrencyHeaderRx = Rx(@"\(\s*(USD|EUR|GBP|CHF|JPY|AUD|CAD|TRY|TL|[€$£₺])\s*\)");
    private static readonly Regex CurrencySectionRx = Rx(@"\b(?:ISLEMLERI|HAREKETLERI)\b");
    private static readonly string[] CurrencyCodes = ["TRY", "TL", "USD", "EUR", "GBP", "CHF", "JPY", "AUD", "CAD", "₺", "€", "$", "£"];
    private static readonly Regex FeeRx = Rx(@"KOMISYON|FAIZ|BSMV|KKDF|UCRET|MASRAF|AIDAT|VERGI");
    private static readonly Regex SummaryRx = Rx(@"(?:^|\s)(?:TOPLAM|DEVIR|DEVREDEN|ACILIS BAKIYESI|KAPANIS BAKIYESI|DONEM BORCU|EKSTRE BORCU|ASGARI|KULLANILABILIR LIMIT|KART LIMITI|HESAP KESIM|SON ODEME TARIHI|ONCEKI DONEM|DONEM OZETI|FAIZ ORANI|FAIZ ORANLARI)(?:\s|:|$)");
    // Kart satırının açıklamadaki anlamı: kart borcu ödemesi, ödeme kuruluşu/fatura harcaması, kart alacağı.
    // Yalnız "ödeme" kelimesi kart borcu ödemesi sayılmaz ("OTOMATIK ODEME TURKCELL" karttan ödenen fatura olabilir).
    private static readonly Regex CardPaymentRx = Rx(@"\b(?:(?:KREDI )?KART(?:I|A|INIZA)? ODEME(?:SI|NIZ)?|ODEMENIZ|ODEME\s*-?\s*TESEKKUR\w*|TESEKKUR(?:LER| EDERIZ)|BORCU? ODEME(?:SI)?|EKSTRE ODEME(?:SI)?|HESAPTAN (?:OTOMATIK )?ODEME|TAHSILAT\w*)\b");
    private static readonly Regex PaymentProviderRx = Rx(@"ODEME HIZMETLERI|ODEME KURULUSU|ODEME SISTEMLERI|FATURA ODEME|OTOMATIK ODEME TALIMAT|\bIYZICO|\bPAYTR\b|\bSIPAY\b|\bPARAM\b");
    private static readonly Regex CardCreditRx = Rx(@"INDIRIM|PUAN\b|\bBONUS\b|CASHBACK|CHIP ?PARA|MAXIMIL|PARAF ?PARA");
    private static readonly Regex InstallmentRx = Rx(@"(?<![\d./])(\d{1,2})\s*/\s*(\d{1,2})(?![\d/]|\.\d)");
    private static readonly Regex BracketInstallmentRx = Rx(@"\(\s*(\d{1,2})\s*/\s*(\d{1,2})\s*\)");
    // Banka belgesinde yalnız bu ifadeden oluşan satır (tarihli de yazılsa) hareket değil, bakiye zincirinin başlangıcıdır
    // (EkstreBakiyeZinciri). "GELEN HAVALE … ÖNCEKİ BAKİYE ÖDEMESİ" gibi hareketler bu desene uymaz.
    private static readonly Regex OpeningBalanceRx = Rx(@"^(?:DEVIR|DEVREDEN|ONCEKI|ACILIS|BASLANGIC)\s+(?:HESAP\s+|DONEM\s+)?BAKIYE(?:SI)?$");
    // Hesap kimliği: aynı etiketin numarası değişirse belge başka hesaba geçmiştir.
    private static readonly Regex AccountWordRx = Rx(@"\bIBAN\b|\bHESAP\b(?!\s+(?:HAREKET|OZET|EKSTRE|DOKUM|CINSI|TURU|ADI|SAHIBI|ACIKLAMA|BAKIYE))");
    private static readonly Regex AccountRx = Rx(@"\b(?<label>IBAN|HESAP NO|HESAP NUMARASI)\s*[:.\-]?\s*(?<no>(?:TR)?\d[\d \-/]*)|^\s*(?<label>HESAP)\s*[:#]?\s*(?<no>\d+)");
    // Yalnız başka kanıt yokken yön verir. Müşterinin yazabileceği ya da iki yönde de geçen ifadeler (maaş, çek tahsilatı, yalın POS,
    // POS satışı, faiz, "fatura ödemesi") burada yoktur; iki desene de uyan açıklama belirsiz kalır.
    private static readonly Regex IncomingRx = Rx(@"\bGELEN (?:EFT|HAVALE|FAST)\b|\b(?:PARA|NAKIT) YATIRMA\w*|\bPOS BLOKE COZUM\w*|\b(?:POS|UYE ISYERI) (?:ALACA[GK]|HAKEDIS)\w*|\bMEVDUAT FAIZ\w*");
    private static readonly Regex OutgoingRx = Rx(@"\bGIDEN (?:EFT|HAVALE|FAST)\b|\b(?:PARA|NAKIT) (?:CEKME|CEKIM)\w*|\bPOS (?:HARCAMA\w*|ALISVERIS\w*)|\bOTOMATIK ODEME\w*|\b(?:ELEKTRIK|SU|DOGAL ?GAZ|TELEFON|INTERNET|GSM|KURUM) FATURA\w*|\bKREDI KARTI ODEME\w*|\bKREDI TAKSI[TD]\w*|\bCEK ODEME\w*");
    // Giriş gibi görünen açıklamanın ("MEVDUAT FAİZİ", "POS HAKEDİŞ") kesinti, vergi ya da borç faizi olduğunu gösteren sözcükler.
    private static readonly Regex DeductionRx = Rx(@"KREDILI MEVDUAT|\bKMH\b|STOPAJ|VERGI|BSMV|KKDF|KESINTI|BEDEL|\bKIRA|KATKI|AIDAT|KOMISYON|UCRET|MASRAF|TAHSILAT|BORC");
    // Bankanın kestiği ücret/vergi; başka kanıt yokken çıkıştır. Havale/EFT açıklamasında ("HAVALE AYŞE KARGO ÜCRETİ") karşı tarafın
    // metni olabileceğinden yalnız işlemin kendi masrafı ("EFT ÜCRETİ", "HAVALE KOMİSYONU") sayılır.
    private static readonly Regex ChargeRx = Rx(@"KOMISYON|BSMV|KKDF|VERGI|STOPAJ|UCRET|MASRAF|AIDAT|KESINTI");
    private static readonly Regex TransferChargeRx = Rx(@"\b(?:EFT|HAVALE|FAST) (?:ISLEM |GONDERIM |GONDERME )?(?:KOMISYON|BSMV|UCRET|MASRAF)");
    // Vergi kimliği/dairesi bir vergi ödemesi değildir ("NAKİT YATIRMA ABC LTD VERGİ NO 123").
    private static readonly Regex TaxIdentityRx = Rx(@"\bVERGI (?:NO|NUMARASI|KIMLIK\w*|DAIRE\w*)\b|\bVKN\b");
    private static readonly Regex DirectionalTransferRx = Rx(@"\b(?<way>GELEN|GIDEN) (?:EFT|HAVALE|FAST)\b");
    // Bakiyenin yanında okunmayan eksi: tire türleri, parantez, borç yazısı (arada boşluk, para simgesi ya da TL/TRY olabilir).
    private static readonly Regex UnreadSignBeforeRx = Rx(@"(?:[\p{Pd}−­(])[  ]{0,2}(?:(?:[₺$€£]|TL|TRY)[  ]{0,2})?$");
    // Sonraki tutarın kendi işareti ("100,00 -500,00") bu tutara ait değildir.
    private static readonly Regex UnreadSignAfterRx = Rx(@"^[  ]{0,2}(?:(?:[₺$€£]|TL|TRY)[  ]{0,2})?(?:[\p{Pd}−­(](?!(?:[₺$€£]|TL|TRY)?\d)|\)|(?:BORC|BORÇ|Borç|borç|B)(?![\p{L}\d]))");
    private static readonly Regex TransferWordRx = Rx(@"\b(?:EFT|HAVALE|FAST)\b");
    private static readonly Regex IncomingWordRx = Rx(@"\bGELEN\b");
    private static readonly Regex OutgoingWordRx = Rx(@"\bGIDEN\b");
    private const int SkippedLimit = 300;
    private sealed record Column(string Kind, int Start);
    // UnreadSign: tutarın yanında okunamayan bir eksi/parantez işareti var ("-₺500,00", "(300,00)"); bakiye olarak kullanılmaz.
    private sealed record Token(decimal Value, int Start, int End, string Direction, bool ExplicitSign, bool UnreadSign);
    // InstallmentColumn: tablo başlığındaki "Taksit" kolonunun başlangıcı (yoksa null).
    private sealed record Segment(string Raw, List<Column> Columns, int Page, int? InstallmentColumn, string? SectionCurrency);
    // Direction yalnız Borç/Alacak kolonu veya B/A sonekiyle kesinleşir; işaretten okunan yön Sign'da (+1/-1) kalır.
    // UnreadSign: tutarın yanında okunamayan bir işaret var; açıklamadaki sözcük tek başına yön vermez.
    private sealed record Amount(decimal? Value, string Direction, int Sign, int TokenCount, bool UnreadSign = false);

    public static EkstreOkumaSonucu Oku(string text, string kaynak, string banka)
    {
        if (kaynak is not (EkstreKaynaklari.Kart or EkstreKaynaklari.Banka))
            throw new PdfOkumaException("Belge türü geçersiz.", 400);
        if (text.Length > 1_000_000)
            throw new PdfOkumaException("PDF metni çok uzun. Daha kısa bir tarih aralığı seçin.");
        var segments = new List<Segment>();
        var warnings = new List<string> { "Okunan satırları PDF ile karşılaştırın. Tutar, yön ve kanal bilgilerini onaylamadan kayıt yapılmaz.", "Yalnız TL hareketlerini kaydedin. Hesaplar arası transfer aynı parayı ikinci kez gelir/gider yapmamalı." };
        var pages = text.Replace("\r", "").Split('\f');
        if (pages.Length > 51 || pages.Length == 51 && !string.IsNullOrWhiteSpace(pages[^1]))
            throw new PdfOkumaException("PDF en fazla 50 sayfa olmalı.");
        // Bir sayfanın döviz başlığı başka sayfadaki kodsuz hareketlere taşınmamalı.
        // Başlığı olmayan sayfalar yalnız tüm belgede tek bir para birimi varsa onu devralır.
        var pageCurrencies = pages.Select(PageCurrency).ToArray();
        // Karma para birimi bölümü olan bir sayfanın TL etiketi başlıksız sonraki sayfaya aktarılmaz.
        var sectionCurrencies = pages.SelectMany(p => p.Split('\n').Select(SectionCurrency)).Where(c => c is not null);
        var knownCurrencies = pageCurrencies.Where(c => c is not null).Concat(sectionCurrencies).Distinct().ToArray();
        var documentFallback = knownCurrencies.Length == 1 && knownCurrencies[0] != "Belirsiz"
            ? knownCurrencies[0]!
            : "Belirsiz";
        var summaryCount = 0;
        var skipped = new List<EkstreAtlananSatir>();
        var skippedCount = 0;
        // Belge sırası: hareketler (segment sırası) ve aralarındaki devir bakiyeleri.
        var balances = new List<BakiyeDugumu>();
        var accounts = new Dictionary<string, string>();
        string? lastSection = null;
        var bank = kaynak == EkstreKaynaklari.Banka;
        void Skip(string skippedLine, int page, string reason, bool summary = false, bool opening = false)
        {
            if (!MoneyRx.IsMatch(MaskDates(skippedLine)))
                return;
            if (!summary)
                skippedCount++;
            // Atlanan satır bakiyeyi değiştirmiş ya da bir hesabı bitirmiş olabilir: iki yanındaki bakiyeler karşılaştırılmaz.
            if (!opening)
                balances.Add(BakiyeDugumu.Kopma);
            var metin = Regex.Replace(GirdiDogrulama.Temizle(skippedLine), @"\s+", " ", RegexOptions.CultureInvariant, RegexLimit).Trim();
            if (skipped.Count < SkippedLimit && metin.Length > 0)
                skipped.Add(new(page, GirdiDogrulama.Temizle(metin[..Math.Min(metin.Length, 300)]), reason));
        }
        for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            var lines = pages[pageIndex].Split('\n').Select(l => l.Replace("\t", "    ")).ToArray();
            List<Column> columns = [];
            int? installmentColumn = null;
            var pageStart = segments.Count;
            // Sayfada numarası okunamayan bir hesap başlığı var: önceki sayfanın hesabının sürdüğü bilinmez.
            var unknownAccount = false;
            // Sayfanın ilerleyen bölümündeki döviz başlığı, önceki kodsuz satırları geriye dönük etiketlemez.
            var sectionCurrency = lines.Any(l => SectionCurrency(l) is not null)
                && !lines.Select(Normalize).Any(l => LabelCurrency(l) is not null || HeaderCurrencies(l).Any())
                ? "Belirsiz" : null;
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var normalized = Normalize(line);
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                if (SectionCurrency(line) is { } section)
                {
                    sectionCurrency = section == pageCurrencies[pageIndex] ? section : "Belirsiz";
                    if (segments.Count > 0 && section != lastSection)
                        balances.Add(BakiyeDugumu.Kopma);
                    lastSection = section;
                    continue;
                }
                if (normalized.Contains("TARIH") && (normalized.Contains("ACIKLAMA") || normalized.Contains("ISLEM")) && ColumnsRx.IsMatch(normalized))
                {
                    // Sayfanın ortasında yeniden başlayan tablo başka bir hesabın/dönemin olabilir.
                    if (segments.Count > pageStart)
                        balances.Add(BakiyeDugumu.Kopma);
                    columns = ColumnsRx.Matches(normalized).Select(m => new Column(m.Value.Contains("BAKIYE") ? "Balance" : m.Value.Contains("ALACAK") ? "Credit" : m.Value.Contains("BORC") ? "Debit" : "Amount", m.Index)).ToList();
                    installmentColumn = normalized.IndexOf("TAKSIT", StringComparison.Ordinal) is var taksit and >= 0 ? taksit : null;
                    continue;
                }
                var hasDate = DateRx.Match(line) is { Success: true } dateMatch && dateMatch.Index < 18;
                var shortDate = ShortDateRx.IsMatch(line);
                if (!hasDate && !shortDate && !MoneyRx.IsMatch(MaskDates(line)) && AccountWordRx.IsMatch(normalized))
                {
                    if (AccountRx.Match(normalized) is { Success: true } account)
                    {
                        var (label, no) = (account.Groups["label"].Value, new string(account.Groups["no"].Value.Where(char.IsLetterOrDigit).ToArray()));
                        if (accounts.TryGetValue(label, out var known) ? known != no : segments.Count > 0)
                            balances.Add(BakiyeDugumu.Kopma);
                        accounts[label] = no;
                    }
                    else
                        unknownAccount = true;
                }
                var opening = bank && OpeningBalance(line);
                if (opening)
                    balances.Add(Tokens(line) is [{ UnreadSign: false } openingBalance]
                        ? new(null, openingBalance.Value, BalanceSuffix(openingBalance), [], false) : BakiyeDugumu.Kopma);
                // Tarihli satır banka belgesinde yalnız ifadeyle başlıyorsa özettir ("GELEN HAVALE … DEVİR BAKİYESİ ÖDEMESİ" harekettir).
                var datedSummary = bank ? DescriptionText(line) is var summaryText && (summaryText.StartsWith("DEVIR BAKIYE", StringComparison.Ordinal) || summaryText.StartsWith("ONCEKI DONEM", StringComparison.Ordinal))
                    : normalized.Contains("DEVIR BAKIYESI") || normalized.Contains("ONCEKI DONEM");
                if (opening || SummaryRx.IsMatch(normalized) && (!hasDate && !shortDate || datedSummary))
                {
                    if (MoneyRx.IsMatch(line) || DateRx.IsMatch(line))
                        summaryCount++;
                    Skip(line, pageIndex + 1, "Toplam, devir, limit ya da ekstre bilgisi", summary: true, opening);
                    continue;
                }
                var isFee = FeeRx.IsMatch(normalized);
                if (!hasDate && !shortDate && (!isFee || !MoneyRx.IsMatch(line)))
                { Skip(line, pageIndex + 1, "Satırın başında tarih yok"); continue; }
                if (normalized.Contains('%') && !hasDate && !shortDate)
                { Skip(line, pageIndex + 1, "Oran bilgisi"); continue; }
                var raw = line;
                // PDF tables sometimes wrap a transaction onto following description/amount lines.
                for (var continuation = 0; continuation < 2 && index + 1 < lines.Length; continuation++)
                {
                    var next = lines[index + 1];
                    var nextNorm = Normalize(next);
                    if (string.IsNullOrWhiteSpace(next) || DateRx.IsMatch(next) || ShortDateRx.IsMatch(next)
                        || SectionCurrency(next) is not null
                        || SummaryRx.IsMatch(nextNorm) || nextNorm.Contains("TARIH") || nextNorm.Contains("SAYFA")
                        || nextNorm.Contains("IBAN") || nextNorm.Contains("HESAP NO") || nextNorm.Contains("KART NO") || bank && OpeningBalance(next))
                        break;
                    var currentMoney = MoneyRx.Matches(MaskDates(raw)).Count;
                    if (currentMoney > 0)
                        break;
                    raw += "\n" + next;
                    index++;
                    if (MoneyRx.IsMatch(next))
                        break;
                }
                var segment = new Segment(raw, columns, pageIndex + 1, installmentColumn, sectionCurrency);
                if (unknownAccount && segments.Count > 0)
                    balances.Add(BakiyeDugumu.Kopma);
                unknownAccount = false;
                balances.Add(BalanceNode(segment, segments.Count));
                segments.Add(segment);
                if (segments.Count > 1500)
                    throw new PdfOkumaException("Bir dosyada en fazla 1500 hareket okunabilir. Daha kısa tarih aralığı seçin.");
            }
        }
        // Kart ekstresinde eksi/artı işaretinin anlamı bankaya göre değişir; satırlar yorumlanmadan önce belgeden çıkarılır.
        var creditSign = kaynak == EkstreKaynaklari.Kart ? CreditSign(segments) : 0;
        // Yürüyen bakiye yalnız banka hareketlerinde vardır; kart ekstresindeki ikinci tutar bakiye sayılmaz.
        var chain = bank ? EkstreBakiyeZinciri.Coz(balances) : BakiyeCozumu.Bos;
        // Bırakılan zincirin yönü kullanılmaz; ama onunla çelişen sözcük de tek başına yön vermez.
        IReadOnlyDictionary<int, BakiyeBagi> rejected = new Dictionary<int, BakiyeBagi>();
        if (chain.Baglar.Count > 0 && Contradicts(segments, chain))
        {
            rejected = chain.Baglar;
            chain = BakiyeCozumu.Bos;
            warnings.Add("Bakiyeden okunan yönler PDF'deki açıklama, işaret ya da kolonlarla çelişiyor; yönler bakiyeden alınmadı.");
        }
        var rows = segments.Select((s, i) => Parse(s, kaynak, s.SectionCurrency ?? pageCurrencies[s.Page - 1] ?? documentFallback, i + 1, creditSign,
            chain.Baglar.GetValueOrDefault(i), chain.YalnizTutar.TryGetValue(i, out var only) ? only : null, rejected.GetValueOrDefault(i))).ToList();
        if (summaryCount > 0)
            warnings.Add($"{summaryCount} toplam, devir, limit veya ekstre bilgi satırı mali hareket olarak alınmadı.");
        if (skippedCount > 0)
            warnings.Add($"{skippedCount} satır tutar içerdiği hâlde harekete alınmadı. Atlanan satırları PDF ile karşılaştırın.");
        if (chain.Kopuk.Count > 0)
            warnings.Add($"Bakiye {string.Join(", ", chain.Kopuk.Take(10).Select(i => $"satır {i + 1}"))}{(chain.Kopuk.Count > 10 ? " ve sonrası" : "")} öncesiyle tutmuyor; arada okunamayan hareket olabilir. PDF ile karşılaştırın.");
        if (rows.Count == 0)
            warnings.Add("İşlem satırı bulunamadı. Bu belgenin düzeni otomatik okunamadı; farklı hesap hareketi PDF'si deneyin.");
        if (rows.Any(r => r.Tarih is null || r.Tutar is null || r.Yon == "Belirsiz"))
            warnings.Add("Bazı satırlarda tarih, tutar veya giriş/çıkış yönü belirsiz. Seçmeden önce düzeltin.");
        return new(rows, warnings, skipped);
    }

    /// <summary>Satır yalnız bir devir/açılış bakiyesi mi: tarih, tutar, para birimi ve noktalama çıkınca başka sözcük kalmaz.</summary>
    private static bool OpeningBalance(string line) => OpeningBalanceRx.IsMatch(DescriptionText(line));
    /// <summary>Satırın sadeleştirilmiş açıklaması: tarih, tutar, para birimi ve noktalama çıkar, boşluklar teke iner.</summary>
    private static string DescriptionText(string line) => Collapse(CurrencyRx.Replace(MoneyRx.Replace(MaskDates(Normalize(line)), " "), " "), @"[\s:.\-]+");
    private static string Collapse(string text, string pattern = @"\s+") => Regex.Replace(text, pattern, " ", RegexOptions.CultureInvariant, RegexLimit).Trim();

    /// <summary>Bakiyeden okunan yönler belgenin diğer kanıtlarıyla en az uyuştuğu kadar çelişiyorsa bakiyenin anlamı bilinmiyordur
    /// (işareti yazılmayan borç bakiye, kredi hesabı): zincir bütünüyle bırakılır. Kanıt: kolon, B/A eki, işaret ya da "gelen/giden
    /// EFT-havale-FAST"; tek başına bir açıklama (ör. geri çekilen EFT) zinciri düşürmez.</summary>
    private static bool Contradicts(List<Segment> segments, BakiyeCozumu chain)
    {
        var agree = 0;
        var disagree = 0;
        var hard = 0;
        foreach (var (index, link) in chain.Baglar)
        {
            var read = ReadAmount(segments[index].Raw, segments[index].Columns);
            var written = read.Direction != "Belirsiz" ? read.Direction == "Giris" ? 1 : -1 : read.Sign != 0 ? read.Sign : link.Yazilan.Yon;
            var normalized = Collapse(Normalize(segments[index].Raw));
            var evidence = written != 0 ? written
                : normalized.Contains("IADE") || normalized.Contains("IPTAL") ? 0
                : DirectionalTransferRx.Match(normalized) is { Success: true } way ? way.Groups["way"].Value == "GELEN" ? 1 : -1 : 0;
            if (evidence == 0)
                continue;
            if (evidence > 0 == link.Giris)
                agree++;
            else
            {
                disagree++;
                if (written != 0)
                    hard++;
            }
        }
        return disagree >= agree && (hard > 0 || disagree > 1);
    }

    private static DateOnly? FirstDate(MatchCollection dates) => dates.Count > 0 && DateOnly.TryParseExact(dates[0].Value,
        ["dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "dd.MM.yy", "d.M.yy", "dd/MM/yy", "d/M/yy"],
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;

    /// <summary>Satırın bakiye adayı ve diğer tutarları: başlıkta bakiye kolonu varsa o kolonun tek tutarı (açıklaması alt satıra
    /// kaymış satırda son tutar), başlık yoksa birden çok tutarın sonuncusu. Başlıkta bakiye kolonu yoksa bakiye aranmaz.</summary>
    private static BakiyeDugumu BalanceNode(Segment segment, int index)
    {
        var tokens = Tokens(segment.Raw);
        var hasColumn = segment.Columns.Any(c => c.Kind == "Balance");
        Token? balance = null;
        if (hasColumn && !segment.Raw.Contains('\n'))
        {
            var inColumn = tokens.Where(t => segment.Columns.LastOrDefault(c => t.End >= c.Start)?.Kind == "Balance").ToList();
            balance = inColumn.Count == 1 ? inColumn[0] : null;
        }
        else if ((hasColumn || segment.Columns.Count == 0) && tokens.Count > 1)
            balance = tokens[^1];
        // Yanında okunamayan eksi/parantez olan bakiyenin işareti bilinmez: zincir orada kopar.
        if (balance is { UnreadSign: true })
            balance = null;
        var amounts = tokens.Where(t => balance is not null && !ReferenceEquals(t, balance) && t.Value != 0)
            .Select(t => new BakiyeTutari(Math.Abs(t.Value), t.Direction == "B" ? -1 : t.Direction == "A" ? 1 : t.ExplicitSign ? Math.Sign(t.Value) : 0, t.Direction is "B" or "A", t.UnreadSign)).ToList();
        var day = DateRx.Match(segment.Raw) is { Success: true } date ? date.Value : ShortDateRx.Match(segment.Raw) is { Success: true } shortDate ? shortDate.Value.Trim() : null;
        return new(index, balance?.Value, balance is null ? 0 : BalanceSuffix(balance), amounts, hasColumn, day);
    }
    private static int BalanceSuffix(Token t) => t.Direction switch { "B" => -1, "A" => 1, _ => 0 };

    private static EkstreOkunanSatir Parse(Segment segment, string source, string documentCurrency, int number, int creditSign, BakiyeBagi? link, BakiyeTutari? onlyAmount, BakiyeBagi? rejected)
    {
        var (raw, columns, page, installmentColumn, _) = segment;
        var warnings = new List<string>();
        var dates = DateRx.Matches(raw);
        var date = FirstDate(dates);
        if (date is null)
            warnings.Add("İşlem tarihi tam okunamadı; yılıyla birlikte girin.");
        if (dates.Count > 1)
            warnings.Add("Birden fazla tarih var; işlem tarihinin doğru olduğunu kontrol edin.");
        var normalized = Normalize(raw);
        var (amount, direction, sign, tokenCount, unreadSign) = ReadAmount(raw, columns);
        // Tutarı okunamayan (tutar + bakiye) satırda bakiyenin doğruladığı tutar, yazıdaki işareti ya da B/A ekiyle alınır.
        if (amount is null && (link?.Yazilan ?? onlyAmount) is { } written)
        {
            amount = written.Tutar;
            unreadSign = written.OkunmayanIsaret;
            if (written.Ek)
                direction = written.Yon < 0 ? "Cikis" : "Giris";
            else
                sign = written.Yon;
        }
        var directionSource = direction == "Belirsiz" ? null : YonKaynaklari.Sutun;
        if (amount is null)
            warnings.Add(tokenCount > 1 ? "Birden fazla tutar var. İşlem tutarını bakiye veya vergi toplamıyla karıştırmadan girin." : "İşlem tutarı okunamadı; PDF'den kontrol ederek girin.");
        var classification = Classify(normalized);
        var (currency, looseForeign) = RowCurrency(raw, documentCurrency);
        if (looseForeign is not null)
            warnings.Add($"Satırda {looseForeign} geçiyor ama tutarın yanında değil; tutarın TL olduğunu PDF'den doğrulayın.");
        else if (currency == "Belirsiz")
            warnings.Add("Para birimi okunamadı; bu satırın TL olduğunu doğrulayın.");
        else if (currency != "TRY")
            warnings.Add("Bu satır farklı para biriminde; TL içe aktarmaya uygun değil.");
        string proposal;
        if (source == EkstreKaynaklari.Kart)
        {
            (direction, directionSource, proposal) = KartKarari(normalized, classification, direction, directionSource, sign, creditSign, warnings);
        }
        else
        {
            if (direction == "Belirsiz" && sign != 0)
            {
                direction = sign < 0 ? "Cikis" : "Giris";
                directionSource = YonKaynaklari.Isaret;
            }
            var described = DescribedDirection(normalized);
            if (rejected is not null && described != (rejected.Giris ? "Giris" : "Cikis"))
                described = null;
            // "NAKİT YATIRMA −100,00": yazılı eksi okunamadıysa açıklama onun yerine geçmez.
            if (unreadSign)
            {
                described = null;
                if (link is null && direction == "Belirsiz")
                    warnings.Add("Tutarın yanındaki işaret okunamadı; giriş/çıkış yönünü PDF'den kontrol edin.");
            }
            if (link is not null)
            {
                var balanceDirection = link.Giris ? "Giris" : "Cikis";
                if (direction == "Belirsiz" && amount == link.Yazilan.Tutar)
                {
                    direction = balanceDirection;
                    directionSource = YonKaynaklari.Bakiye;
                    if (described is not null && described != direction)
                        warnings.Add($"Açıklama {(described == "Giris" ? "girişe" : "çıkışa")} benziyor ama bakiye {(link.Giris ? "artmış" : "azalmış")}; yön bakiyeden alındı. PDF ile karşılaştırın.");
                }
                else if (direction != balanceDirection || amount != link.Yazilan.Tutar)
                {
                    direction = "Belirsiz";
                    directionSource = null;
                    warnings.Add($"PDF'de yazan tutar ya da yön bakiyeyle çelişiyor (bakiye {link.Yazilan.Tutar.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} TL {(link.Giris ? "artmış" : "azalmış")}); yön tahmin edilmedi.");
                }
            }
            else if (direction == "Belirsiz" && described is not null)
            {
                direction = described;
                directionSource = YonKaynaklari.Sozcuk;
            }
            proposal = BankaOnerisi(normalized, classification, direction);
        }
        OrtakUyarilar(normalized, classification, warnings);
        // Ekstredeki "2/6 TAKSIT" satırı taksitli bir alışın aylık payıdır; tek taksitli yeni harcama olarak önerilmez.
        if (source == EkstreKaynaklari.Kart && Installment(normalized, dates.Count > 0, installmentColumn, columns) is (var no, var count))
        {
            proposal = EkstreIslemTurleri.Atla;
            warnings.Add($"Taksitli işlemin {no}/{count}. taksidi. Harcama kartta taksitli girildiyse kaydetmeyin; ilk kez giriyorsanız Kartlar bölümünden toplam tutar ve {count} taksitle girin.");
        }
        if (direction == "Belirsiz")
            warnings.Add("Giriş/çıkış yönü kesin okunamadı; işlem türünü seçin.");
        var description = ShortDateRx.Replace(DateRx.Replace(raw, " "), " ");
        description = MoneyRx.Replace(description, " ");
        // PDF metni kullanıcı girdisi değildir: kontrol karakteri ve geçersiz Unicode reddedilmez, kullanıcı
        // girdisiyle aynı kuralla (GirdiDogrulama) temizlenir; kısaltma bir vekil çiftini bölerse o da temizlenir.
        description = Regex.Replace(GirdiDogrulama.Temizle(description), @"\s+", " ", RegexOptions.CultureInvariant, RegexLimit).Trim(' ', '-', '+');
        if (description.Length == 0)
            description = "PDF hareketi";
        if (raw.Length > 2000)
            warnings.Add("Uzun kaynak satırı kısaltıldı; asıl PDF'yi kontrol edin.");
        return new(number, page, GirdiDogrulama.Temizle(raw[..Math.Min(raw.Length, 2000)]), date,
            GirdiDogrulama.Temizle(description[..Math.Min(description.Length, 500)]).TrimEnd(), amount,
            direction, proposal, classification, currency, warnings, directionSource);
    }
    /// <summary>Kart satırının yönü ve önerilen türü (PDF ve Excel okuyucuları aynı kuralı kullanır). <paramref name="direction"/> kolondan ya da
    /// B/A ekinden okunan kesin yön, <paramref name="sign"/> yazılı işaret, <paramref name="creditSign"/> belgede alacağın işareti (0: bilinmiyor).</summary>
    internal static (string Direction, string? Source, string Proposal) KartKarari(string normalized, string classification, string direction, string? directionSource,
        int sign, int creditSign, List<string> warnings)
    {
        string proposal;
        var key = CardKey(normalized, classification);
        // ReadAmount yönü yalnız Borç/Alacak kolonundan ya da B/A sonekinden verir; bu yön açıklamadaki kelimeyle ezilmez.
        var certain = direction != "Belirsiz";
        // Borç/Alacak kolonu ve B/A soneki kesindir. Yalnız işaretten okunan yön belgenin işaret anlamıyla çözülür;
        // anlam bilinmiyorsa tahmin edilmez.
        if (direction == "Belirsiz" && sign != 0 && creditSign != 0)
        {
            direction = sign == creditSign ? "Giris" : "Cikis";
            directionSource = YonKaynaklari.Isaret;
        }
        if (direction == "Belirsiz")
        {
            direction = key switch
            {
                "Iade" or "Alacak" => "Giris",
                // Ordinary card charges remain a proposal; a payment must never be a new charge.
                "Odeme" or "BelirsizOdeme" => "Belirsiz",
                _ => IsFee(classification) || sign == 0 ? "Cikis" : "Belirsiz"
            };
            if (direction != "Belirsiz" && (key is "Iade" or "Alacak" || IsFee(classification)))
                directionSource = YonKaynaklari.Sozcuk;
        }
        proposal = key switch
        {
            "Iade" => EkstreIslemTurleri.KartIade,
            "Odeme" => EkstreIslemTurleri.KartOdemesi,
            // Adında "indirim/bonus/puan" geçen işyerinin borç satırı alacak sayılmaz.
            "Alacak" => certain && direction == "Cikis" ? EkstreIslemTurleri.KartHarcama : EkstreIslemTurleri.Atla,
            _ => direction == "Cikis" ? EkstreIslemTurleri.KartHarcama : key == "BelirsizOdeme" && direction == "Giris" ? EkstreIslemTurleri.KartOdemesi : EkstreIslemTurleri.Atla
        };
        if (key == "BelirsizOdeme")
            warnings.Add("Açıklamada ödeme geçiyor: kart borcu ödemesi mi, karttan ödenen fatura mı PDF'den kontrol edip türü seçin.");
        if (key == "Kurulus" && direction == "Cikis")
            warnings.Add("Açıklamada ödeme geçiyor ama ödeme kuruluşu/fatura harcaması görünüyor; kart borcu ödemesi değilse Kart harcaması seçin.");
        if (key == "Alacak" && certain && direction == "Cikis")
            warnings.Add("Açıklamada indirim/puan geçiyor ama PDF satırı borç olarak gösteriyor; Kart harcaması önerildi. İndirim/puan alacağıysa kaydetmeyin.");
        else if (key == "Alacak" || (key is null or "Kurulus") && direction == "Giris")
            warnings.Add("Kart alacağı (indirim, puan, iade) olabilir. Borç artışı olarak kaydetmeyin; iadeyse asıl harcamayı seçip Karta iade türünü kullanın.");
        if ((key is null or "Kurulus") && direction == "Belirsiz" && sign != 0)
            warnings.Add("Tutar işaretli; bu bankada eksi/artı işaretinin borç mu alacak mı olduğu belgeden anlaşılamadı. Türü PDF ile karşılaştırıp siz seçin.");
        return (direction, directionSource, proposal);
    }
    internal static void OrtakUyarilar(string normalized, string classification, List<string> warnings)
    {
        if (classification == "Transfer")
            warnings.Add("Kendi hesaplarınız arası transfer yeni gelir/gider oluşturmayabilir. Seçmeden önce kontrol edin.");
        if (normalized.Contains("KREDI") && (normalized.Contains("TAKSIT") || normalized.Contains("KULLANDIR") || normalized.Contains("ODEME")))
            warnings.Add("Kredi takibinde zaten işlenmiş olabilir; ikinci kez kaydetmeyin.");
    }
    /// <summary>Banka satırının önerilen türü.</summary>
    internal static string BankaOnerisi(string normalized, string classification, string direction) => classification == "Transfer" ? EkstreIslemTurleri.Atla
        : normalized.Contains("KART") && normalized.Contains("ODEME") ? EkstreIslemTurleri.KartOdemesi
        : direction == "Giris" ? EkstreIslemTurleri.Gelir : direction == "Cikis" ? EkstreIslemTurleri.Gider : EkstreIslemTurleri.Atla;
    /// <summary>Açıklamadaki taksit sırası ("2/6 TAKSIT"); kolonu olmayan kaynak (Excel) için.</summary>
    internal static (int No, int Count)? TaksitSirasi(string normalized) => Installment(normalized, true, null, []);
    /// <summary>Açıklamanın tek anlamlı yönü; iade/iptal geçiyorsa, iki yöne de uyuyorsa ya da ipucu yoksa null.</summary>
    internal static string? DescribedDirection(string normalized)
    {
        var text = TaxIdentityRx.Replace(Collapse(normalized), " ");
        if (text.Contains("IADE") || text.Contains("IPTAL"))
            return null;
        var transfer = TransferWordRx.IsMatch(text);
        var gelen = IncomingWordRx.IsMatch(text);
        var giden = OutgoingWordRx.IsMatch(text);
        // Yönü yazmayan havale/EFT'nin açıklaması karşı tarafındır; yalnız bankanın işlem masrafı yön verir.
        var unqualified = transfer && !gelen && !giden;
        var incoming = !giden && !unqualified && IncomingRx.IsMatch(text);
        // Giriş ifadesiyle kesinti/vergi/borç sözcüğü bir aradaysa ("MEVDUAT FAİZİ STOPAJI", "POS BLOKE ÇÖZÜM KOMİSYON SONRASI") ikisi de
        // olabilir; gelen havalede ise o sözcük gönderenin metnidir.
        if (incoming && !transfer && DeductionRx.IsMatch(text))
            return null;
        var outgoing = !gelen && !unqualified && OutgoingRx.IsMatch(text)
            || (transfer ? TransferChargeRx : ChargeRx).IsMatch(text);
        return incoming == outgoing ? null : incoming ? "Giris" : "Cikis";
    }
    private static List<Token> Tokens(string raw)
    {
        var masked = MaskDates(raw);
        return MoneyRx.Matches(masked).Select(m => ToToken(m, masked)).Where(t => t is not null).Cast<Token>().ToList();
    }
    private static Amount ReadAmount(string raw, List<Column> columns)
    {
        var tokens = Tokens(raw);
        var typed = new List<(Token Token, string Kind)>();
        if (!raw.Contains('\n') && columns.Count > 0)
        {
            foreach (var token in tokens)
            {
                // Header starts establish non-overlapping numeric columns. Right-aligned values
                // may start left of their header, so use the end of the numeric token as anchor.
                var column = columns.LastOrDefault(c => token.End >= c.Start);
                if (column is not null)
                    typed.Add((token, column.Kind));
            }
            var cash = typed.Where(p => p.Kind != "Balance" && p.Token.Value != 0).ToList();
            if (cash.Count == 1)
                return cash[0].Kind switch
                {
                    "Debit" => new(Math.Abs(cash[0].Token.Value), "Cikis", 0, tokens.Count),
                    "Credit" => new(Math.Abs(cash[0].Token.Value), "Giris", 0, tokens.Count),
                    _ => FromToken(cash[0].Token, tokens.Count)
                };
        }
        if (tokens.Count == 1 && !typed.Any(p => p.Kind == "Balance"))
            return FromToken(tokens[0], 1);
        return new(null, "Belirsiz", 0, tokens.Count, tokens.Any(t => t.UnreadSign));
    }
    private static Amount FromToken(Token t, int tokenCount) => new(Math.Abs(t.Value), t.Direction == "B" ? "Cikis" : t.Direction == "A" ? "Giris" : "Belirsiz",
        t.Direction is "B" or "A" || !t.ExplicitSign ? 0 : t.Value < 0 ? -1 : 1, tokenCount, t.UnreadSign);
    private static Token? ToToken(Match m, string line)
    {
        if (m.Index + m.Length < line.Length && line[m.Index + m.Length] == '%')
            return null;
        var value = m.Groups["n"].Value;
        value = value.LastIndexOf(',') > value.LastIndexOf('.') ? value.Replace(".", "").Replace(',', '.') : value.Replace(",", "");
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount > 999_999_999_999.99m)
            return null;
        var sign = m.Groups["sign"].Value + m.Groups["tail"].Value;
        if (sign.Contains('-'))
            amount = -amount;
        var direction = m.Groups["direction"].Value;
        var end = m.Index + m.Length;
        var numberEnd = m.Groups["tail"].Index + m.Groups["tail"].Length;
        // Ek tek başınaysa (satır/kolon sonu, ardından başka bir tutar) ektir. Ardından tek boşlukla metin geliyorsa ("8.500,00 B BLOK",
        // "100,00 A 101 MAĞAZA", "300,00 B ABC LTD") ek mi sözcük mü bilinmez: yön vermez, bakiye olarak da kullanılmaz.
        var ambiguous = direction.Length > 0 && !SuffixEnds(line, end);
        if (ambiguous)
        {
            direction = "";
            end = numberEnd;
        }
        return new(amount, m.Index, end, direction, sign.Length > 0,
            ambiguous || sign.Length == 0 && direction.Length == 0 && (UnreadSignBeforeRx.IsMatch(line[..m.Index]) || UnreadSignAfterRx.IsMatch(line[numberEnd..])));
    }
    private static bool SuffixEnds(string line, int end)
    {
        var rest = line[end..];
        var text = rest.TrimStart(' ', ' ');
        return text.Length == 0 || text[0] is '\n' or '\r' || rest.Length - text.Length > 1 || MoneyRx.Match(text) is { Success: true, Index: 0 };
    }
    internal static string Classify(string normalized) => normalized.Contains("KOMISYON") ? "Komisyon" : normalized.Contains("FAIZ") ? "Faiz"
        : normalized.Contains("BSMV") || normalized.Contains("KKDF") || normalized.Contains("VERGI") ? "Vergi"
        : normalized.Contains("UCRET") || normalized.Contains("MASRAF") || normalized.Contains("AIDAT") ? "Ucret"
        : normalized.Contains("VIRMAN") || normalized.Contains("HESAPLAR ARASI") ? "Transfer"
        : normalized.Contains("ODEME") || normalized.Contains("TAHSILAT") ? "Odeme" : "Hareket";
    internal static bool IsFee(string classification) => classification is "Faiz" or "Komisyon" or "Vergi" or "Ucret";
    // Öncelik sırası önemlidir: iade/iptal; indirim/puan alacağı ("PUAN ILE ODEME" nakit ödeme değildir); faiz/ücret
    // "ödeme" geçse de borçtur ("GECIKMIS ODEME FAIZI", "TAHSILAT UCRETI"); sonra açık kart borcu ödemesi; kalan
    // "ödeme" satırı ödeme kuruluşu/fatura harcaması ya da belirsiz ödemedir.
    internal static string? CardKey(string normalized, string classification)
    {
        if (normalized.Contains("IADE") || normalized.Contains("IPTAL"))
            return "Iade";
        var fee = IsFee(classification);
        if (CardCreditRx.IsMatch(normalized) && (!fee || normalized.Contains("INDIRIM")))
            return "Alacak";
        if (fee)
            return null;
        if (CardPaymentRx.IsMatch(normalized))
            return "Odeme";
        if (!normalized.Contains("ODEME"))
            return null;
        return PaymentProviderRx.IsMatch(normalized) ? "Kurulus" : "BelirsizOdeme";
    }
    // Yönü açıklamadan belli işaretli satırlar kanıttır: kart borcu ödemesi ve iade alacak, faiz/ücret/vergi borçtur.
    // "IPTAL UCRETI" gibi iki anlamlı satır kanıt sayılmaz. Kanıtlar tek anlamda birleşirse alacağın işareti (+1/-1)
    // döner; kanıt yoksa ya da çelişirse 0 (bilinmiyor).
    private static int CreditSign(IEnumerable<Segment> segments)
    {
        var evidence = new HashSet<int>();
        foreach (var segment in segments)
        {
            var read = ReadAmount(segment.Raw, segment.Columns);
            if (read.Sign == 0)
                continue;
            var normalized = Normalize(segment.Raw);
            var classification = Classify(normalized);
            var key = CardKey(normalized, classification);
            var fee = IsFee(classification);
            if (key == "Odeme" || key == "Iade" && !fee)
                evidence.Add(read.Sign);
            else if (key is null && fee)
                evidence.Add(-read.Sign);
        }
        return evidence.Count == 1 ? evidence.Single() : 0;
    }
    // "2/6 TAKSIT", "TAKSIT 03/12": taksit sırası ve sayısı. Tam tarih maskelenir; baştaki kısa tarih ("15/07") yalnız
    // tam tarih yoksa işlem tarihidir, aksi halde tarihten sonra gelen "2/6" taksit oranıdır. Satırda "TAKSIT" kelimesi
    // yoksa oran yalnız parantez içindeyse ("(2/6)") ya da başlıktaki Taksit kolonunun altındaysa sayılır; açıklamadaki
    // "1/2" gibi kesirler taksit değildir.
    private static (int No, int Count)? Installment(string normalized, bool hasFullDate, int? column, List<Column> columns)
    {
        var text = DateRx.Replace(normalized, m => new string(' ', m.Length));
        if (!hasFullDate)
            text = ShortDateRx.Replace(text, m => new string(' ', m.Length));
        IEnumerable<Match> candidates = BracketInstallmentRx.Matches(text);
        if (normalized.Contains("TAKSIT"))
            candidates = candidates.Concat(InstallmentRx.Matches(text));
        else if (column is int start)
        {
            // Kolon konumları yalnız ilk satırda başlıkla hizalıdır; kolon, başlıktaki sonraki tutar kolonunda biter.
            var first = text.Split('\n')[0];
            var end = columns.Where(c => c.Start > start).Select(c => c.Start).DefaultIfEmpty(first.Length).Min();
            candidates = candidates.Concat(InstallmentRx.Matches(first).Where(m => m.Index >= start - 3 && m.Index < end));
        }
        foreach (var m in candidates)
            if (int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var no)
                && int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                && count is >= 2 and <= 60 && no >= 1 && no <= count)
                return (no, count);
        return null;
    }
    private static string? PageCurrency(string text)
    {
        var lines = Normalize(text).Split('\n');
        var declared = lines.Select(LabelCurrency).Where(c => c is not null).Cast<string>()
            .Concat(lines.SelectMany(HeaderCurrencies)).Distinct().ToList();
        if (declared.Count > 0)
            return declared.Count == 1 ? declared[0] : "Belirsiz";
        // Başlık yoksa bu sayfadaki tüm kodlar sayılır ("USD İşlemleri" bölüm başlığı dahil).
        // Bağlamsız "CAD" Türkçe metinde Cadde kısaltmasıdır: yalnız tutara bitişikse ya da başlıktaysa sayılır.
        var currencies = lines.SelectMany(l => AmountCurrencies(l).Concat(HeaderCurrencies(l)).Concat(LooseCurrencies(l))).Distinct().ToList();
        return currencies.Count == 0 ? null : currencies.Count == 1 ? currencies[0] : "Belirsiz";
    }
    // Bölüm başlığı, sayfa başındaki para biriminden farklıysa kodsuz satırların birimi tahmin edilmez.
    // "USD İşlemleri" veya "Döviz Hareketleri: USD" gibi tarihsiz başlıklar burada ele alınır.
    private static string? SectionCurrency(string line)
    {
        var normalized = Normalize(line);
        if (!CurrencySectionRx.IsMatch(normalized))
            return null;
        var dates = DateRx.Matches(normalized);
        var fullDateRange = dates.Count >= 2 && normalized[(dates[0].Index + dates[0].Length)..dates[1].Index].Trim() is "-" or "–" or "—";
        var dateRange = fullDateRange || ShortDateRangeRx.IsMatch(normalized);
        // Satırın başındaki işlem tarihi, tutar sonraki satıra kaymış olsa da bölüm başlığına dönüşmez.
        if ((dates.Count > 0 && dates[0].Index < 18 && !fullDateRange) || (ShortDateRx.IsMatch(normalized) && !dateRange))
            return null;
        // Dönem başlığının "Toplam" tutarı hareket değildir; diğer tutarlı satırlar hareket olarak kalır.
        var amountText = ShortDateRangeRx.Replace(normalized, m => new string(' ', m.Length));
        if (MoneyRx.IsMatch(MaskDates(amountText)) && !(dateRange && SummaryRx.IsMatch(normalized)))
            return null;
        var codes = CurrencyRx.Matches(normalized)
            .Where(m => m.Value != "CAD" || m.Index + m.Length >= normalized.Length || normalized[m.Index + m.Length] != '.')
            .Select(m => Currency(m.Value)).Distinct().ToList();
        if (codes.Count > 1 || normalized.Contains("DOVIZ") || normalized.Contains("YABANCI PARA"))
            return "Belirsiz";
        return codes.Count == 1 ? codes[0] : null;
    }
    // "Para Birimi: Türk Lirası   Şube Adresi: Bağdat Cad." — değer yalnız etiketin kendi alanından okunur: alan kolon
    // boşluğunda ya da sonraki "Etiket:" başlangıcında biter; "CAD" yalnız alanın ilk kelimesiyse koddur ("CAD." değil).
    // Alanda para birimi yoksa ("Hesap Cinsi: VADESIZ") ya da birden fazlaysa etiket karar vermez.
    private static string? LabelCurrency(string line)
    {
        var labelledCurrencies = new HashSet<string>();
        foreach (Match label in CurrencyLabelRx.Matches(line))
        {
            var value = line[(label.Index + label.Length)..].TrimStart(' ', '\t', ':', '-', '.');
            var end = LabelFieldEndRx.Match(value);
            var field = end.Success ? value[..end.Index] : value;
            var codes = CurrencyNameRx.Matches(field).Select(m => CurrencyName(m.Value))
                .Concat(CurrencyRx.Matches(field).Where(m => m.Value != "CAD" || m.Index == 0 && !field.StartsWith("CAD.", StringComparison.Ordinal)).Select(m => Currency(m.Value)))
                .Distinct().ToList();
            if (codes.Count > 1)
                return "Belirsiz";
            if (codes.Count == 1)
                labelledCurrencies.Add(codes[0]);
        }
        return labelledCurrencies.Count == 0 ? null : labelledCurrencies.Count == 1 ? labelledCurrencies.Single() : "Belirsiz";
    }
    private static string CurrencyName(string name) => name switch
    { "TURK LIRASI" => "TRY", "KANADA DOLARI" => "CAD", "AVUSTRALYA DOLARI" => "AUD", "EURO" or "AVRO" => "EUR", "STERLIN" or "STERLINI" => "GBP", _ => "USD" };
    // Satırdaki bağımsız kodlar (tutara bitişik olsun olmasın); bağlamsız "CAD" hariç.
    private static IEnumerable<string> LooseCurrencies(string line) =>
        CurrencyRx.Matches(Normalize(line)).Where(m => m.Value != "CAD").Select(m => Currency(m.Value));
    // Tablo başlığındaki ("Tarih Açıklama Tutar TL") ya da parantezli ("Tutar (TL)") kod belge düzeyinde para birimidir.
    private static IEnumerable<string> HeaderCurrencies(string line)
    {
        var table = line.Contains("TARIH") && (line.Contains("ACIKLAMA") || line.Contains("ISLEM")) && ColumnsRx.IsMatch(line);
        return (table ? CurrencyRx.Matches(line).Select(m => m.Value) : CurrencyHeaderRx.Matches(line).Select(m => m.Groups[1].Value)).Select(Currency);
    }
    // Tutara bitişik kod satırın para birimidir. Bitişik kod yoksa satırdaki bağımsız döviz kodu (ayrı döviz kolonu,
    // açıklamada "USD") TL varsayımını bozar: satır sessizce belgenin TL'sine düşmez, Belirsiz ve uyarıyla gelir (ek
    // onayla kaydedilebilir). Belge etiketi dövizse açıklamadaki "TL" satırı TL'ye çevirmez. Loose: uyarıdaki kodlar.
    private static (string Currency, string? Loose) RowCurrency(string line, string fallback)
    {
        var found = AmountCurrencies(line).Distinct().ToList();
        if (found.Count > 0)
            return (found.Count == 1 ? found[0] : "Karisik", null);
        var foreign = LooseCurrencies(line).Where(c => c != "TRY").Distinct().ToList();
        return foreign.Count > 0 && fallback is "TRY" or "Belirsiz" ? ("Belirsiz", string.Join(", ", foreign)) : (fallback, null);
    }
    // Para birimi yalnız tutara bitişik koddan okunur: önce tutarın hemen sonrası (en fazla 4 boşluk), yoksa hemen
    // öncesi (en fazla 2 boşluk). Açıklamadaki bağımsız kelimeler sayılmaz; önde "CAD" Türkçe adreste Cadde kısaltmasıdır.
    private static IEnumerable<string> AmountCurrencies(string line)
    {
        var text = MaskDates(Normalize(line));
        foreach (Match m in MoneyRx.Matches(text))
            if ((AdjacentCurrency(text.AsSpan(m.Index + m.Length), after: true) ?? AdjacentCurrency(text.AsSpan(0, m.Index), after: false)) is { } code)
                yield return Currency(code);
    }
    private static string? AdjacentCurrency(ReadOnlySpan<char> side, bool after)
    {
        var trimmed = after ? side.TrimStart(" \t") : side.TrimEnd(" \t");
        if (side.Length - trimmed.Length > (after ? 4 : 2))
            return null;
        foreach (var code in CurrencyCodes)
        {
            if (after ? !trimmed.StartsWith(code, StringComparison.Ordinal) : code == "CAD" || !trimmed.EndsWith(code, StringComparison.Ordinal))
                continue;
            var boundary = after ? code.Length : trimmed.Length - code.Length - 1;
            if (boundary < 0 || boundary >= trimmed.Length || !char.IsLetterOrDigit(trimmed[boundary]))
                return code;
        }
        return null;
    }
    private static string Currency(string value) => value switch { "TL" or "TRY" or "₺" => "TRY", "$" => "USD", "€" => "EUR", "£" => "GBP", _ => value };
    private static string MaskDates(string value) => ShortDateRx.Replace(DateRx.Replace(value, m => new string(' ', m.Length)), m => new string(' ', m.Length));
    public static string Normalize(string value) => new string(value.Replace('ı', 'I').Replace('İ', 'I').ToUpperInvariant().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
}
