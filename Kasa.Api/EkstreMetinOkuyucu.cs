using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kasa.Api.Servisler;

namespace Kasa.Api;

/// <summary>Conservative layout parser: amounts with ambiguous columns remain editable, never silently guessed.</summary>
public static class EkstreMetinOkuyucu
{
    private static readonly TimeSpan RegexLimit = TimeSpan.FromSeconds(1);
    private static Regex Rx(string pattern) => new(pattern, RegexOptions.CultureInvariant, RegexLimit);
    private static readonly Regex DateRx = Rx(@"(?<!\d)(?:\d{4}-\d{2}-\d{2}|\d{1,2}[./-]\d{1,2}[./-](?:\d{4}|\d{2}))(?!\d)");
    private static readonly Regex ShortDateRx = Rx(@"^\s*\d{1,2}[./]\d{1,2}(?![\d./])");
    private static readonly Regex MoneyRx = Rx(@"(?<![\p{L}\d.,])(?<sign>[+-]?)(?<n>(?:\d{1,3}(?:\.\d{3})+|\d+),\d{2}|(?:\d{1,3}(?:,\d{3})+|\d+)\.\d{2})(?<tail>[+-]?)(?:\s*(?<direction>B|A))?(?![\p{L}\d.,])");
    private static readonly Regex ColumnsRx = Rx(@"ISLEM TUTARI|BORC TUTARI|ALACAK TUTARI|BAKIYE|BORC|ALACAK|TUTAR");
    private static readonly Regex CurrencyRx = Rx(@"\b(USD|EUR|GBP|CHF|JPY|AUD|CAD|TRY|TL)\b|[€$£₺]");
    private static readonly Regex CurrencyLabelRx = Rx(@"PARA BIRIMI|HESAP CINSI|DOVIZ CINSI");
    // Etiket değerinin alanı kolon boşluğunda (2+ boşluk) ya da sonraki "Etiket:" başlangıcında biter.
    private static readonly Regex LabelFieldEndRx = Rx(@"\s{2,}|\s[^\s:]+\s*:");
    private static readonly Regex CurrencyNameRx = Rx(@"\b(?:TURK LIRASI|KANADA DOLARI|AVUSTRALYA DOLARI|DOLAR|DOLARI|EURO|AVRO|STERLIN|STERLINI)\b");
    // Adres kısaltması parantez içinde yazılmaz; "(TL)" gibi kod belge para birimidir.
    private static readonly Regex CurrencyHeaderRx = Rx(@"\(\s*(USD|EUR|GBP|CHF|JPY|AUD|CAD|TRY|TL|[€$£₺])\s*\)");
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
    private sealed record Column(string Kind, int Start);
    private sealed record Token(decimal Value, int Start, int End, string Direction, bool ExplicitSign);
    // InstallmentColumn: tablo başlığındaki "Taksit" kolonunun başlangıcı (yoksa null).
    private sealed record Segment(string Raw, List<Column> Columns, int Page, int? InstallmentColumn);
    // Direction yalnız Borç/Alacak kolonu veya B/A sonekiyle kesinleşir; işaretten okunan yön Sign'da (+1/-1) kalır.
    private sealed record Amount(decimal? Value, string Direction, int Sign, int TokenCount);

    public static EkstreOkumaSonucu Oku(string text, string kaynak, string banka)
    {
        if (kaynak is not ("Kart" or "Banka")) throw new PdfOkumaException("Belge türü geçersiz.", 400);
        if (text.Length > 1_000_000) throw new PdfOkumaException("PDF metni çok uzun. Daha kısa bir tarih aralığı seçin.");
        var segments = new List<Segment>();
        var warnings = new List<string> { "Okunan satırları PDF ile karşılaştırın. Tutar, yön ve kanal bilgilerini onaylamadan kayıt yapılmaz.", "Yalnız TL hareketlerini kaydedin. Hesaplar arası transfer aynı parayı ikinci kez gelir/gider yapmamalı." };
        var globalCurrency = DocumentCurrency(text);
        var pages = text.Replace("\r", "").Split('\f');
        if (pages.Length > 51 || pages.Length == 51 && !string.IsNullOrWhiteSpace(pages[^1])) throw new PdfOkumaException("PDF en fazla 50 sayfa olmalı.");
        var summaryCount = 0;
        for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            var lines = pages[pageIndex].Split('\n').Select(l => l.Replace("\t", "    ")).ToArray();
            List<Column> columns = []; int? installmentColumn = null;
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index]; var normalized = Normalize(line);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (normalized.Contains("TARIH") && (normalized.Contains("ACIKLAMA") || normalized.Contains("ISLEM")) && ColumnsRx.IsMatch(normalized))
                {
                    columns = ColumnsRx.Matches(normalized).Select(m => new Column(m.Value.Contains("BAKIYE") ? "Balance" : m.Value.Contains("ALACAK") ? "Credit" : m.Value.Contains("BORC") ? "Debit" : "Amount", m.Index)).ToList();
                    installmentColumn = normalized.IndexOf("TAKSIT", StringComparison.Ordinal) is var taksit and >= 0 ? taksit : null;
                    continue;
                }
                var hasDate = DateRx.Match(line) is { Success: true } dateMatch && dateMatch.Index < 18;
                var shortDate = ShortDateRx.IsMatch(line);
                if (SummaryRx.IsMatch(normalized) && (!hasDate && !shortDate || normalized.Contains("DEVIR BAKIYESI") || normalized.Contains("ONCEKI DONEM"))) { if (MoneyRx.IsMatch(line) || DateRx.IsMatch(line)) summaryCount++; continue; }
                var isFee = FeeRx.IsMatch(normalized);
                if (!hasDate && !shortDate && (!isFee || !MoneyRx.IsMatch(line))) continue;
                if (normalized.Contains('%') && !hasDate && !shortDate) continue;
                var raw = line;
                // PDF tables sometimes wrap a transaction onto following description/amount lines.
                for (var continuation = 0; continuation < 2 && index + 1 < lines.Length; continuation++)
                {
                    var next = lines[index + 1]; var nextNorm = Normalize(next);
                    if (string.IsNullOrWhiteSpace(next) || DateRx.IsMatch(next) || ShortDateRx.IsMatch(next)
                        || SummaryRx.IsMatch(nextNorm) || nextNorm.Contains("TARIH") || nextNorm.Contains("SAYFA")
                        || nextNorm.Contains("IBAN") || nextNorm.Contains("HESAP NO") || nextNorm.Contains("KART NO")) break;
                    var currentMoney = MoneyRx.Matches(MaskDates(raw)).Count;
                    if (currentMoney > 0) break;
                    raw += "\n" + next; index++;
                    if (MoneyRx.IsMatch(next)) break;
                }
                segments.Add(new(raw, columns, pageIndex + 1, installmentColumn));
                if (segments.Count > 1500) throw new PdfOkumaException("Bir dosyada en fazla 1500 hareket okunabilir. Daha kısa tarih aralığı seçin.");
            }
        }
        // Kart ekstresinde eksi/artı işaretinin anlamı bankaya göre değişir; satırlar yorumlanmadan önce belgeden çıkarılır.
        var creditSign = kaynak == "Kart" ? CreditSign(segments) : 0;
        var rows = segments.Select((s, i) => Parse(s, kaynak, globalCurrency, i + 1, creditSign)).ToList();
        if (summaryCount > 0) warnings.Add($"{summaryCount} toplam, devir, limit veya ekstre bilgi satırı mali hareket olarak alınmadı.");
        if (rows.Count == 0) warnings.Add("İşlem satırı bulunamadı. Bu belgenin düzeni otomatik okunamadı; farklı hesap hareketi PDF'si deneyin.");
        if (rows.Any(r => r.Tarih is null || r.Tutar is null || r.Yon == "Belirsiz")) warnings.Add("Bazı satırlarda tarih, tutar veya giriş/çıkış yönü belirsiz. Seçmeden önce düzeltin.");
        return new(rows, warnings);
    }

    private static EkstreOkunanSatir Parse(Segment segment, string source, string documentCurrency, int number, int creditSign)
    {
        var (raw, columns, page, installmentColumn) = segment;
        var warnings = new List<string>(); var dates = DateRx.Matches(raw);
        DateOnly? date = null;
        if (dates.Count > 0 && DateOnly.TryParseExact(dates[0].Value, ["dd.MM.yyyy","d.M.yyyy","dd/MM/yyyy","d/M/yyyy","dd-MM-yyyy","d-M-yyyy","yyyy-MM-dd","dd.MM.yy","d.M.yy","dd/MM/yy","d/M/yy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) date = parsed;
        if (date is null) warnings.Add("İşlem tarihi tam okunamadı; yılıyla birlikte girin.");
        if (dates.Count > 1) warnings.Add("Birden fazla tarih var; işlem tarihinin doğru olduğunu kontrol edin.");
        var normalized = Normalize(raw);
        var (amount, direction, sign, tokenCount) = ReadAmount(raw, columns);
        if (amount is null) warnings.Add(tokenCount > 1 ? "Birden fazla tutar var. İşlem tutarını bakiye veya vergi toplamıyla karıştırmadan girin." : "İşlem tutarı okunamadı; PDF'den kontrol ederek girin.");
        var classification = Classify(normalized);
        var refund = normalized.Contains("IADE") || normalized.Contains("IPTAL");
        var (currency, looseForeign) = RowCurrency(raw, documentCurrency);
        if (looseForeign is not null) warnings.Add($"Satırda {looseForeign} geçiyor ama tutarın yanında değil; tutarın TL olduğunu PDF'den doğrulayın.");
        else if (currency == "Belirsiz") warnings.Add("Para birimi okunamadı; bu satırın TL olduğunu doğrulayın.");
        else if (currency != "TRY") warnings.Add("Bu satır farklı para biriminde; TL içe aktarmaya uygun değil.");
        string proposal;
        if (source == "Kart")
        {
            var key = CardKey(normalized, classification);
            // ReadAmount yönü yalnız Borç/Alacak kolonundan ya da B/A sonekinden verir; bu yön açıklamadaki kelimeyle ezilmez.
            var certain = direction != "Belirsiz";
            // Borç/Alacak kolonu ve B/A soneki kesindir. Yalnız işaretten okunan yön belgenin işaret anlamıyla çözülür;
            // anlam bilinmiyorsa tahmin edilmez.
            if (direction == "Belirsiz" && sign != 0 && creditSign != 0) direction = sign == creditSign ? "Giris" : "Cikis";
            if (direction == "Belirsiz")
                direction = key switch
                {
                    "Iade" or "Alacak" => "Giris",
                    // Ordinary card charges remain a proposal; a payment must never be a new charge.
                    "Odeme" or "BelirsizOdeme" => "Belirsiz",
                    _ => IsFee(classification) || sign == 0 ? "Cikis" : "Belirsiz"
                };
            proposal = key switch
            {
                "Iade" => "KartIade",
                "Odeme" => "KartOdemesi",
                // Adında "indirim/bonus/puan" geçen işyerinin borç satırı alacak sayılmaz.
                "Alacak" => certain && direction == "Cikis" ? "KartHarcama" : "Atla",
                _ => direction == "Cikis" ? "KartHarcama" : key == "BelirsizOdeme" && direction == "Giris" ? "KartOdemesi" : "Atla"
            };
            if (key == "BelirsizOdeme") warnings.Add("Açıklamada ödeme geçiyor: kart borcu ödemesi mi, karttan ödenen fatura mı PDF'den kontrol edip türü seçin.");
            if (key == "Kurulus" && direction == "Cikis") warnings.Add("Açıklamada ödeme geçiyor ama ödeme kuruluşu/fatura harcaması görünüyor; kart borcu ödemesi değilse Kart harcaması seçin.");
            if (key == "Alacak" && certain && direction == "Cikis")
                warnings.Add("Açıklamada indirim/puan geçiyor ama PDF satırı borç olarak gösteriyor; Kart harcaması önerildi. İndirim/puan alacağıysa kaydetmeyin.");
            else if (key == "Alacak" || (key is null or "Kurulus") && direction == "Giris")
                warnings.Add("Kart alacağı (indirim, puan, iade) olabilir. Borç artışı olarak kaydetmeyin; iadeyse asıl harcamayı seçip Karta iade türünü kullanın.");
            if ((key is null or "Kurulus") && direction == "Belirsiz" && sign != 0)
                warnings.Add("Tutar işaretli; bu bankada eksi/artı işaretinin borç mu alacak mı olduğu belgeden anlaşılamadı. Türü PDF ile karşılaştırıp siz seçin.");
        }
        else
        {
            if (direction == "Belirsiz" && sign != 0) direction = sign < 0 ? "Cikis" : "Giris";
            if (direction == "Belirsiz")
            {
                if (classification is "Komisyon" or "Vergi" or "Ucret" && !refund) direction = "Cikis";
                if (normalized.Contains("GELEN EFT") || normalized.Contains("GELEN HAVALE") || normalized.Contains("GELEN FAST")) direction = "Giris";
                if (normalized.Contains("GIDEN EFT") || normalized.Contains("GIDEN HAVALE") || normalized.Contains("GIDEN FAST")) direction = "Cikis";
            }
            proposal = classification == "Transfer" ? "Atla" : normalized.Contains("KART") && normalized.Contains("ODEME") ? "KartOdemesi" : direction == "Giris" ? "Gelir" : direction == "Cikis" ? "Gider" : "Atla";
        }
        if (classification == "Transfer") warnings.Add("Kendi hesaplarınız arası transfer yeni gelir/gider oluşturmayabilir. Seçmeden önce kontrol edin.");
        if (normalized.Contains("KREDI") && (normalized.Contains("TAKSIT") || normalized.Contains("KULLANDIR") || normalized.Contains("ODEME"))) warnings.Add("Kredi takibinde zaten işlenmiş olabilir; ikinci kez kaydetmeyin.");
        // Ekstredeki "2/6 TAKSIT" satırı taksitli bir alışın aylık payıdır; tek taksitli yeni harcama olarak önerilmez.
        if (source == "Kart" && Installment(normalized, dates.Count > 0, installmentColumn, columns) is (var no, var count))
        {
            proposal = "Atla";
            warnings.Add($"Taksitli işlemin {no}/{count}. taksidi. Harcama kartta taksitli girildiyse kaydetmeyin; ilk kez giriyorsanız Kartlar bölümünden toplam tutar ve {count} taksitle girin.");
        }
        if (direction == "Belirsiz") warnings.Add("Giriş/çıkış yönü kesin okunamadı; işlem türünü seçin.");
        var description = ShortDateRx.Replace(DateRx.Replace(raw, " "), " ");
        description = MoneyRx.Replace(description, " ");
        // PDF metni kullanıcı girdisi değildir: kontrol karakteri ve geçersiz Unicode reddedilmez, kullanıcı
        // girdisiyle aynı kuralla (GirdiDogrulama) temizlenir; kısaltma bir vekil çiftini bölerse o da temizlenir.
        description = Regex.Replace(GirdiDogrulama.Temizle(description), @"\s+", " ", RegexOptions.CultureInvariant, RegexLimit).Trim(' ', '-', '+');
        if (description.Length == 0) description = "PDF hareketi";
        if (raw.Length > 2000) warnings.Add("Uzun kaynak satırı kısaltıldı; asıl PDF'yi kontrol edin.");
        return new(number, page, GirdiDogrulama.Temizle(raw[..Math.Min(raw.Length, 2000)]), date,
            GirdiDogrulama.Temizle(description[..Math.Min(description.Length, 500)]).TrimEnd(), amount,
            direction, proposal, classification, currency, warnings);
    }
    private static Amount ReadAmount(string raw, List<Column> columns)
    {
        var masked = MaskDates(raw);
        var tokens = MoneyRx.Matches(masked).Select(m => ToToken(m, masked)).Where(t => t is not null).Cast<Token>().ToList();
        var typed = new List<(Token Token, string Kind)>();
        if (!raw.Contains('\n') && columns.Count > 0)
        {
            foreach (var token in tokens)
            {
                // Header starts establish non-overlapping numeric columns. Right-aligned values
                // may start left of their header, so use the end of the numeric token as anchor.
                var column = columns.LastOrDefault(c => token.End >= c.Start);
                if (column is not null) typed.Add((token, column.Kind));
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
        if (tokens.Count == 1 && !typed.Any(p => p.Kind == "Balance")) return FromToken(tokens[0], 1);
        return new(null, "Belirsiz", 0, tokens.Count);
    }
    private static Amount FromToken(Token t, int tokenCount) => new(Math.Abs(t.Value), t.Direction == "B" ? "Cikis" : t.Direction == "A" ? "Giris" : "Belirsiz",
        t.Direction is "B" or "A" || !t.ExplicitSign ? 0 : t.Value < 0 ? -1 : 1, tokenCount);
    private static Token? ToToken(Match m, string line)
    {
        if (m.Index + m.Length < line.Length && line[m.Index + m.Length] == '%') return null;
        var value = m.Groups["n"].Value;
        value = value.LastIndexOf(',') > value.LastIndexOf('.') ? value.Replace(".", "").Replace(',', '.') : value.Replace(",", "");
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount > 999_999_999_999.99m) return null;
        var sign = m.Groups["sign"].Value + m.Groups["tail"].Value;
        if (sign.Contains('-')) amount = -amount;
        return new(amount, m.Index, m.Index + m.Length, m.Groups["direction"].Value, sign.Length > 0);
    }
    private static string Classify(string normalized) => normalized.Contains("KOMISYON") ? "Komisyon" : normalized.Contains("FAIZ") ? "Faiz"
        : normalized.Contains("BSMV") || normalized.Contains("KKDF") || normalized.Contains("VERGI") ? "Vergi"
        : normalized.Contains("UCRET") || normalized.Contains("MASRAF") || normalized.Contains("AIDAT") ? "Ucret"
        : normalized.Contains("VIRMAN") || normalized.Contains("HESAPLAR ARASI") ? "Transfer"
        : normalized.Contains("ODEME") || normalized.Contains("TAHSILAT") ? "Odeme" : "Hareket";
    private static bool IsFee(string classification) => classification is "Faiz" or "Komisyon" or "Vergi" or "Ucret";
    // Öncelik sırası önemlidir: iade/iptal; indirim/puan alacağı ("PUAN ILE ODEME" nakit ödeme değildir); faiz/ücret
    // "ödeme" geçse de borçtur ("GECIKMIS ODEME FAIZI", "TAHSILAT UCRETI"); sonra açık kart borcu ödemesi; kalan
    // "ödeme" satırı ödeme kuruluşu/fatura harcaması ya da belirsiz ödemedir.
    private static string? CardKey(string normalized, string classification)
    {
        if (normalized.Contains("IADE") || normalized.Contains("IPTAL")) return "Iade";
        var fee = IsFee(classification);
        if (CardCreditRx.IsMatch(normalized) && (!fee || normalized.Contains("INDIRIM"))) return "Alacak";
        if (fee) return null;
        if (CardPaymentRx.IsMatch(normalized)) return "Odeme";
        if (!normalized.Contains("ODEME")) return null;
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
            if (read.Sign == 0) continue;
            var normalized = Normalize(segment.Raw); var classification = Classify(normalized);
            var key = CardKey(normalized, classification);
            var fee = IsFee(classification);
            if (key == "Odeme" || key == "Iade" && !fee) evidence.Add(read.Sign);
            else if (key is null && fee) evidence.Add(-read.Sign);
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
        if (!hasFullDate) text = ShortDateRx.Replace(text, m => new string(' ', m.Length));
        IEnumerable<Match> candidates = BracketInstallmentRx.Matches(text);
        if (normalized.Contains("TAKSIT")) candidates = candidates.Concat(InstallmentRx.Matches(text));
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
                && count is >= 2 and <= 60 && no >= 1 && no <= count) return (no, count);
        return null;
    }
    private static string DocumentCurrency(string text)
    {
        var lines = Normalize(text).Split('\n', '\f');
        foreach (var line in lines) if (LabelCurrency(line) is { } labelled) return labelled;
        // Etiket yoksa belgedeki tüm kodlar sayılır ("USD İşlemleri" bölüm başlığı dahil); tek kod yoksa kodsuz satır tahmin
        // edilmez. Bağlamsız "CAD" Türkçe metinde Cadde kısaltmasıdır: yalnız tutara bitişikse ya da başlıktaysa sayılır.
        var currencies = lines.SelectMany(l => AmountCurrencies(l).Concat(HeaderCurrencies(l)).Concat(LooseCurrencies(l))).Distinct().ToList();
        return currencies.Count == 1 ? currencies[0] : "Belirsiz";
    }
    // "Para Birimi: Türk Lirası   Şube Adresi: Bağdat Cad." — değer yalnız etiketin kendi alanından okunur: alan kolon
    // boşluğunda ya da sonraki "Etiket:" başlangıcında biter; "CAD" yalnız alanın ilk kelimesiyse koddur ("CAD." değil).
    // Alanda para birimi yoksa ("Hesap Cinsi: VADESIZ") ya da birden fazlaysa etiket karar vermez.
    private static string? LabelCurrency(string line)
    {
        foreach (Match label in CurrencyLabelRx.Matches(line))
        {
            var value = line[(label.Index + label.Length)..].TrimStart(' ', '\t', ':', '-', '.');
            var end = LabelFieldEndRx.Match(value); var field = end.Success ? value[..end.Index] : value;
            var codes = CurrencyNameRx.Matches(field).Select(m => CurrencyName(m.Value))
                .Concat(CurrencyRx.Matches(field).Where(m => m.Value != "CAD" || m.Index == 0 && !field.StartsWith("CAD.", StringComparison.Ordinal)).Select(m => Currency(m.Value)))
                .Distinct().ToList();
            if (codes.Count == 1) return codes[0];
        }
        return null;
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
        if (found.Count > 0) return (found.Count == 1 ? found[0] : "Karisik", null);
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
        if (side.Length - trimmed.Length > (after ? 4 : 2)) return null;
        foreach (var code in CurrencyCodes)
        {
            if (after ? !trimmed.StartsWith(code, StringComparison.Ordinal) : code == "CAD" || !trimmed.EndsWith(code, StringComparison.Ordinal)) continue;
            var boundary = after ? code.Length : trimmed.Length - code.Length - 1;
            if (boundary < 0 || boundary >= trimmed.Length || !char.IsLetterOrDigit(trimmed[boundary])) return code;
        }
        return null;
    }
    private static string Currency(string value) => value switch { "TL" or "TRY" or "₺" => "TRY", "$" => "USD", "€" => "EUR", "£" => "GBP", _ => value };
    private static string MaskDates(string value) => ShortDateRx.Replace(DateRx.Replace(value, m => new string(' ', m.Length)), m => new string(' ', m.Length));
    public static string Normalize(string value) => new string(value.Replace('ı', 'I').Replace('İ', 'I').ToUpperInvariant().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
}
