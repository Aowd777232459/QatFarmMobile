using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QatFarm.Mobile.Services;

public static class ArabicVoiceText
{
    private static readonly Dictionary<string, decimal> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["صفر"] = 0,
        ["واحد"] = 1, ["واحده"] = 1, ["واحدة"] = 1,
        ["اثنين"] = 2, ["اثنان"] = 2, ["اثنتين"] = 2, ["ثنتين"] = 2, ["ثنين"] = 2,
        ["ثلاث"] = 3, ["ثلاثه"] = 3, ["ثلاثة"] = 3,
        ["اربع"] = 4, ["اربعه"] = 4, ["اربعة"] = 4,
        ["خمس"] = 5, ["خمسه"] = 5, ["خمسة"] = 5,
        ["ست"] = 6, ["سته"] = 6, ["ستة"] = 6,
        ["سبع"] = 7, ["سبعه"] = 7, ["سبعة"] = 7,
        ["ثمان"] = 8, ["ثمانيه"] = 8, ["ثمانية"] = 8,
        ["تسع"] = 9, ["تسعه"] = 9, ["تسعة"] = 9,
        ["عشر"] = 10, ["عشره"] = 10, ["عشرة"] = 10,
        ["احدعشر"] = 11, ["احدعشره"] = 11,
        ["اثناعشر"] = 12, ["اثنيعشر"] = 12,
        ["ثلاثطعش"] = 13, ["ثلاثتعشر"] = 13,
        ["اربعطعش"] = 14, ["اربعتعشر"] = 14,
        ["خمسطعش"] = 15, ["خمستعشر"] = 15,
        ["ستطعش"] = 16, ["ستتعشر"] = 16,
        ["سبعطعش"] = 17, ["سبعتعشر"] = 17,
        ["ثمنطعش"] = 18, ["ثمانتعشر"] = 18,
        ["تسعطعش"] = 19, ["تسعتعشر"] = 19,
        ["عشرين"] = 20, ["عشرون"] = 20,
        ["ثلاثين"] = 30, ["ثلاثون"] = 30,
        ["اربعين"] = 40, ["اربعون"] = 40,
        ["خمسين"] = 50, ["خمسون"] = 50,
        ["ستين"] = 60, ["ستون"] = 60,
        ["سبعين"] = 70, ["سبعون"] = 70,
        ["ثمانين"] = 80, ["ثمانون"] = 80,
        ["تسعين"] = 90, ["تسعون"] = 90,
        ["ميه"] = 100, ["مئه"] = 100, ["مائه"] = 100,
        ["مئتين"] = 200, ["مائتين"] = 200, ["ميتين"] = 200,
        ["ثلاثميه"] = 300, ["اربعمئه"] = 400, ["اربعمية"] = 400,
        ["خمسميه"] = 500, ["ستميه"] = 600, ["سبعميه"] = 700,
        ["ثمانميه"] = 800, ["تسعميه"] = 900
    };

    private static readonly Dictionary<string, int> MonthNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["يناير"] = 1, ["فبراير"] = 2, ["مارس"] = 3, ["ابريل"] = 4,
        ["مايو"] = 5, ["يونيو"] = 6, ["يوليو"] = 7, ["اغسطس"] = 8,
        ["سبتمبر"] = 9, ["اكتوبر"] = 10, ["نوفمبر"] = 11, ["ديسمبر"] = 12
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            builder.Append(ch switch
            {
                '٠' => '0', '١' => '1', '٢' => '2', '٣' => '3', '٤' => '4',
                '٥' => '5', '٦' => '6', '٧' => '7', '٨' => '8', '٩' => '9',
                'أ' or 'إ' or 'آ' => 'ا', 'ؤ' => 'و', 'ئ' or 'ى' => 'ي', 'ة' => 'ه',
                'ـ' => ' ', '،' => ' ', '؛' => ' ', ',' => ' ', ':' => ' ', ';' => ' ',
                _ => ch
            });
        }
        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    public static bool ContainsAny(string text, params string[] values)
    {
        var normalized = Normalize(text);
        return values.Any(x => normalized.Contains(Normalize(x), StringComparison.Ordinal));
    }

    public static decimal? ExtractMoneyAfter(string text, params string[] markers)
    {
        var normalized = Normalize(text);
        foreach (var marker in markers.OrderByDescending(x => x.Length))
        {
            var key = Normalize(marker);
            var index = normalized.IndexOf(key, StringComparison.Ordinal);
            if (index < 0) continue;
            var tail = normalized[(index + key.Length)..].Trim();
            var value = ParseLeadingNumber(tail);
            if (value.HasValue) return value;
        }
        return null;
    }

    public static int? ExtractQuantity(string text)
    {
        var normalized = Normalize(text);
        var unitMatch = Regex.Match(normalized,
            @"(?<n>\d+(?:\.\d+)?(?:\s*(?:الف|الاف))?)\s*(?:حبه|حبات|حزم|حزمه|كيلو|وحده|وحدات)");
        if (unitMatch.Success)
        {
            var parsed = ParseLeadingNumber(unitMatch.Groups["n"].Value);
            if (parsed.HasValue && parsed.Value > 0 && parsed.Value <= int.MaxValue)
                return (int)Math.Round(parsed.Value, 0, MidpointRounding.AwayFromZero);
        }

        var byMarker = ExtractMoneyAfter(normalized, "الكميه", "كمية", "كميه");
        if (byMarker.HasValue && byMarker.Value > 0 && byMarker.Value <= int.MaxValue)
            return (int)Math.Round(byMarker.Value, 0, MidpointRounding.AwayFromZero);
        return null;
    }

    public static long? ExtractIdAfter(string text, params string[] markers)
    {
        var value = ExtractMoneyAfter(text, markers);
        if (!value.HasValue || value.Value <= 0 || value.Value > long.MaxValue) return null;
        return (long)Math.Round(value.Value, 0, MidpointRounding.AwayFromZero);
    }

    public static int? ExtractMonth(string text)
    {
        var normalized = Normalize(text);
        foreach (var pair in MonthNames)
            if (normalized.Contains(pair.Key, StringComparison.Ordinal)) return pair.Value;

        var value = ExtractMoneyAfter(normalized, "شهر");
        if (value is >= 1 and <= 12) return (int)value.Value;
        return null;
    }

    public static int? ExtractYear(string text)
    {
        var normalized = Normalize(text);
        var match = Regex.Match(normalized, @"\b(20\d{2})\b");
        if (match.Success && int.TryParse(match.Value, out var year)) return year;
        return null;
    }

    public static T? FindMentioned<T>(string text, IEnumerable<T> values, Func<T, string> nameSelector) where T : class
    {
        var normalized = Normalize(text);
        T? best = null;
        var bestLength = -1;
        foreach (var value in values)
        {
            var name = Normalize(nameSelector(value));
            if (name.Length < 2) continue;
            if (normalized.Contains(name, StringComparison.Ordinal) && name.Length > bestLength)
            {
                best = value;
                bestLength = name.Length;
            }
        }
        return best;
    }

    public static decimal? ParseLeadingNumber(string text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized)) return null;

        var numeric = Regex.Match(normalized, @"^(?<n>\d+(?:\.\d+)?)\s*(?<m>الف|الاف|مليون|ملايين)?");
        if (numeric.Success && decimal.TryParse(numeric.Groups["n"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var digitValue))
        {
            var multiplier = numeric.Groups["m"].Value switch
            {
                "الف" or "الاف" => 1000m,
                "مليون" or "ملايين" => 1000000m,
                _ => 1m
            };
            return digitValue * multiplier;
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(8).ToArray();
        decimal total = 0m;
        decimal current = 0m;
        var consumed = false;

        foreach (var raw in words)
        {
            var word = raw;
            if (word == "و") continue;
            if (word.StartsWith('و') && word.Length > 1) word = word[1..];

            if (word is "الف" or "الاف" or "آلاف")
            {
                current = current == 0 ? 1 : current;
                total += current * 1000m;
                current = 0;
                consumed = true;
                continue;
            }
            if (word is "الفين")
            {
                total += 2000m;
                current = 0;
                consumed = true;
                continue;
            }
            if (word is "مليون" or "ملايين")
            {
                current = current == 0 ? 1 : current;
                total += current * 1000000m;
                current = 0;
                consumed = true;
                continue;
            }

            var compact = word.Replace(" ", string.Empty);
            if (NumberWords.TryGetValue(compact, out var number))
            {
                current += number;
                consumed = true;
                continue;
            }

            break;
        }

        return consumed ? total + current : null;
    }
}
