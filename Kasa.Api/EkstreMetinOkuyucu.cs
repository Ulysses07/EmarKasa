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
    private static readonly Regex CurrencyRx = Rx(@"\b(USD|EUR|GBP|CHF|JPY|AUD|CAD|TRY|TL)\b|[€$£]");
    private static readonly Regex FeeRx = Rx(@"KOMISYON|FAIZ|BSMV|KKDF|UCRET|MASRAF|AIDAT|VERGI");
    private static readonly Regex SummaryRx = Rx(@"(?:^|\s)(?:TOPLAM|DEVIR|DEVREDEN|ACILIS BAKIYESI|KAPANIS BAKIYESI|DONEM BORCU|EKSTRE BORCU|ASGARI|KULLANILABILIR LIMIT|KART LIMITI|HESAP KESIM|SON ODEME TARIHI|ONCEKI DONEM|DONEM OZETI|FAIZ ORANI|FAIZ ORANLARI)(?:\s|:|$)");
    private sealed record Column(string Kind, int Start);
    private sealed record Token(decimal Value, int Start, int End, string Direction, bool ExplicitSign);

    public static EkstreOkumaSonucu Oku(string text, string kaynak, string banka)
    {
        if (kaynak is not ("Kart" or "Banka")) throw new PdfOkumaException("Belge türü geçersiz.", 400);
        if (text.Length > 1_000_000) throw new PdfOkumaException("PDF metni çok uzun. Daha kısa bir tarih aralığı seçin.");
        var rows = new List<EkstreOkunanSatir>();
        var warnings = new List<string> { "Okunan satırları PDF ile karşılaştırın. Tutar, yön ve kanal bilgilerini onaylamadan kayıt yapılmaz.", "Yalnız TL hareketlerini kaydedin. Hesaplar arası transfer aynı parayı ikinci kez gelir/gider yapmamalı." };
        var globalCurrency = DocumentCurrency(text);
        var pages = text.Replace("\r", "").Split('\f');
        if (pages.Length > 51 || pages.Length == 51 && !string.IsNullOrWhiteSpace(pages[^1])) throw new PdfOkumaException("PDF en fazla 50 sayfa olmalı.");
        var summaryCount = 0;
        for (var pageIndex = 0; pageIndex < pages.Length; pageIndex++)
        {
            var lines = pages[pageIndex].Split('\n').Select(l => l.Replace("\t", "    ")).ToArray();
            List<Column> columns = [];
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index]; var normalized = Normalize(line);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (normalized.Contains("TARIH") && (normalized.Contains("ACIKLAMA") || normalized.Contains("ISLEM")) && ColumnsRx.IsMatch(normalized))
                { columns = ColumnsRx.Matches(normalized).Select(m => new Column(m.Value.Contains("BAKIYE") ? "Balance" : m.Value.Contains("ALACAK") ? "Credit" : m.Value.Contains("BORC") ? "Debit" : "Amount", m.Index)).ToList(); continue; }
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
                var row = Parse(raw, columns, kaynak, globalCurrency, pageIndex + 1, rows.Count + 1);
                rows.Add(row);
                if (rows.Count > 1500) throw new PdfOkumaException("Bir dosyada en fazla 1500 hareket okunabilir. Daha kısa tarih aralığı seçin.");
            }
        }
        if (summaryCount > 0) warnings.Add($"{summaryCount} toplam, devir, limit veya ekstre bilgi satırı mali hareket olarak alınmadı.");
        if (rows.Count == 0) warnings.Add("İşlem satırı bulunamadı. Bu belgenin düzeni otomatik okunamadı; farklı hesap hareketi PDF'si deneyin.");
        if (rows.Any(r => r.Tarih is null || r.Tutar is null || r.Yon == "Belirsiz")) warnings.Add("Bazı satırlarda tarih, tutar veya giriş/çıkış yönü belirsiz. Seçmeden önce düzeltin.");
        return new(rows, warnings);
    }

    private static EkstreOkunanSatir Parse(string raw, List<Column> columns, string source, string documentCurrency, int page, int number)
    {
        var warnings = new List<string>(); var dates = DateRx.Matches(raw);
        DateOnly? date = null;
        if (dates.Count > 0 && DateOnly.TryParseExact(dates[0].Value, ["dd.MM.yyyy","d.M.yyyy","dd/MM/yyyy","d/M/yyyy","dd-MM-yyyy","d-M-yyyy","yyyy-MM-dd","dd.MM.yy","d.M.yy","dd/MM/yy","d/M/yy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) date = parsed;
        if (date is null) warnings.Add("İşlem tarihi tam okunamadı; yılıyla birlikte girin.");
        if (dates.Count > 1) warnings.Add("Birden fazla tarih var; işlem tarihinin doğru olduğunu kontrol edin.");
        var masked = MaskDates(raw);
        var normalized = Normalize(raw);
        var tokens = MoneyRx.Matches(masked).Select(m => ToToken(m, masked)).Where(t => t is not null).Cast<Token>().ToList();
        decimal? amount = null; var direction = "Belirsiz";
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
            {
                amount = Math.Abs(cash[0].Token.Value);
                direction = cash[0].Kind == "Debit" ? "Cikis" : cash[0].Kind == "Credit" ? "Giris" : TokenDirection(cash[0].Token);
            }
        }
        if (amount is null && tokens.Count == 1 && !typed.Any(p => p.Kind == "Balance"))
        { amount = Math.Abs(tokens[0].Value); direction = TokenDirection(tokens[0]); }
        if (amount is null) warnings.Add(tokens.Count > 1 ? "Birden fazla tutar var. İşlem tutarını bakiye veya vergi toplamıyla karıştırmadan girin." : "İşlem tutarı okunamadı; PDF'den kontrol ederek girin.");
        var classification = normalized.Contains("KOMISYON") ? "Komisyon" : normalized.Contains("FAIZ") ? "Faiz"
            : normalized.Contains("BSMV") || normalized.Contains("KKDF") || normalized.Contains("VERGI") ? "Vergi"
            : normalized.Contains("UCRET") || normalized.Contains("MASRAF") || normalized.Contains("AIDAT") ? "Ucret"
            : normalized.Contains("VIRMAN") || normalized.Contains("HESAPLAR ARASI") ? "Transfer"
            : normalized.Contains("ODEME") || normalized.Contains("TAHSILAT") ? "Odeme" : "Hareket";
        var refund = normalized.Contains("IADE") || normalized.Contains("IPTAL");
        if (source == "Kart" && direction == "Belirsiz")
        {
            if (refund) direction = "Giris";
            else if (classification is "Faiz" or "Komisyon" or "Vergi" or "Ucret") direction = "Cikis";
            // Ordinary card charges remain a proposal; a payment must never be a new charge.
            else if (!normalized.Contains("ODEME") && !normalized.Contains("TAHSILAT")) direction = "Cikis";
        }
        if (source == "Banka" && direction == "Belirsiz")
        {
            if (classification is "Komisyon" or "Vergi" or "Ucret" && !refund) direction = "Cikis";
            if (normalized.Contains("GELEN EFT") || normalized.Contains("GELEN HAVALE") || normalized.Contains("GELEN FAST")) direction = "Giris";
            if (normalized.Contains("GIDEN EFT") || normalized.Contains("GIDEN HAVALE") || normalized.Contains("GIDEN FAST")) direction = "Cikis";
        }
        var currency = RowCurrency(raw, documentCurrency);
        if (currency == "Belirsiz") warnings.Add("Para birimi okunamadı; bu satırın TL olduğunu doğrulayın.");
        else if (currency != "TRY") warnings.Add("Bu satır farklı para biriminde; TL içe aktarmaya uygun değil.");
        var proposal = source == "Kart"
            ? refund ? "KartIade" : normalized.Contains("ODEME") || normalized.Contains("TAHSILAT") ? "KartOdemesi" : direction == "Cikis" ? "KartHarcama" : "Atla"
            : classification == "Transfer" ? "Atla" : normalized.Contains("KART") && normalized.Contains("ODEME") ? "KartOdemesi" : direction == "Giris" ? "Gelir" : direction == "Cikis" ? "Gider" : "Atla";
        if (classification == "Transfer") warnings.Add("Kendi hesaplarınız arası transfer yeni gelir/gider oluşturmayabilir. Seçmeden önce kontrol edin.");
        if (normalized.Contains("KREDI") && (normalized.Contains("TAKSIT") || normalized.Contains("KULLANDIR") || normalized.Contains("ODEME"))) warnings.Add("Kredi takibinde zaten işlenmiş olabilir; ikinci kez kaydetmeyin.");
        if (direction == "Belirsiz") warnings.Add("Giriş/çıkış yönü kesin okunamadı; işlem türünü seçin.");
        var description = ShortDateRx.Replace(DateRx.Replace(raw, " "), " ");
        description = MoneyRx.Replace(description, " ");
        description = Regex.Replace(description, @"\s+", " ", RegexOptions.CultureInvariant, RegexLimit).Trim(' ', '-', '+');
        if (description.Length == 0) description = "PDF hareketi";
        if (raw.Length > 2000) warnings.Add("Uzun kaynak satırı kısaltıldı; asıl PDF'yi kontrol edin.");
        return new(number, page, raw[..Math.Min(raw.Length, 2000)], date, description[..Math.Min(description.Length, 500)], amount,
            direction, proposal, classification, currency, warnings);
    }
    private static string TokenDirection(Token t) => t.Direction == "B" ? "Cikis" : t.Direction == "A" ? "Giris"
        : t.ExplicitSign ? t.Value < 0 ? "Cikis" : "Giris" : "Belirsiz";
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
    private static string DocumentCurrency(string text)
    {
        var lines = Normalize(text).Split('\n');
        foreach (var line in lines.Where(l => l.Contains("PARA BIRIMI") || l.Contains("HESAP CINSI") || l.Contains("DOVIZ CINSI")))
        { var found = CurrencyRx.Match(line); if (found.Success) return Currency(found.Value); if (line.Contains("TURK LIRASI")) return "TRY"; }
        var currencies = CurrencyRx.Matches(Normalize(text)).Select(m => Currency(m.Value)).Distinct().ToList();
        return currencies.Count == 1 ? currencies[0] : "Belirsiz";
    }
    private static string RowCurrency(string line, string fallback)
    { var found = CurrencyRx.Matches(Normalize(line)).Select(m => Currency(m.Value)).Distinct().ToList(); return found.Count == 1 ? found[0] : found.Count > 1 ? "Karisik" : fallback; }
    private static string Currency(string value) => value switch { "TL" or "TRY" => "TRY", "$" => "USD", "€" => "EUR", "£" => "GBP", _ => value };
    private static string MaskDates(string value) => ShortDateRx.Replace(DateRx.Replace(value, m => new string(' ', m.Length)), m => new string(' ', m.Length));
    public static string Normalize(string value) => new string(value.Replace('ı', 'I').Replace('İ', 'I').ToUpperInvariant().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
}
