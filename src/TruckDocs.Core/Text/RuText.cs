using System.Globalization;
using System.Text.RegularExpressions;

namespace TruckDocs.Core.Text;

public static partial class RuText
{
    public static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly string[] MonthsGenitive =
        { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" };

    /// <summary>«25 ноября 2026 г.» → дата.</summary>
    public static string FormatLong(DateOnly d) => $"{d.Day} {MonthsGenitive[d.Month - 1]} {d.Year} г.";

    /// <summary>Понимает «25.11.2026», «25.11.26», «25 ноября 2026».</summary>
    public static DateOnly? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = NumericDate().Match(s);
        if (m.Success)
        {
            var year = int.Parse(m.Groups[3].Value);
            if (year < 100) year += 2000;
            try { return new DateOnly(year, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value)); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        m = WordDate().Match(s);
        if (m.Success)
        {
            var monthWord = m.Groups[2].Value.ToLowerInvariant();
            var month = Array.FindIndex(MonthsGenitive, x => monthWord.StartsWith(x[..3]));
            if (month >= 0)
            {
                try { return new DateOnly(int.Parse(m.Groups[3].Value), month + 1, int.Parse(m.Groups[1].Value)); }
                catch (ArgumentOutOfRangeException) { return null; }
            }
        }
        return null;
    }

    /// <summary>«65 000», «678 534,28», «65000.00» → decimal.</summary>
    public static decimal? ParseMoney(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Money().Match(s);
        if (!m.Success) return null;
        var normalized = Regex.Replace(m.Value, @"[\s ]", "").Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static string FormatMoney(decimal v) => v.ToString("#,##0.00", Ru).Replace(' ', ' ');

    /// <summary>Из адреса достаёт город: «Волгоградская область, Волгоград, ул. …» → «Волгоград».</summary>
    public static string? CityFromAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        foreach (var raw in address.Split(','))
        {
            var part = raw.Trim();
            if (part.Length == 0 || Regex.IsMatch(part, @"^\d{6}$")) continue; // индекс
            if (Regex.IsMatch(part, @"(област|обл\.?$|\bобл\b|край|респ|район|р-н|округ|\bг\.?\s?о\.?\b|АО$)", RegexOptions.IgnoreCase)) continue;
            if (Regex.IsMatch(part, @"^(ул|улица|пр-т|проспект|пер|шоссе|ш\.|д\.|дом|стр|мкр)\b", RegexOptions.IgnoreCase)) break;
            return Regex.Replace(part, @"^(г\.|г |город|пос\.|пгт|с\.|село|дер\.|д\.)\s*", "", RegexOptions.IgnoreCase).Trim();
        }
        return null;
    }

    /// <summary>«К 872 НУ 716» → «К872НУ716».</summary>
    public static string CompactPlate(string plate) => Regex.Replace(plate, @"\s+", "").ToUpperInvariant();

    /// <summary>Прибавить N рабочих дней (без учёта праздников — для напоминаний достаточно).</summary>
    public static DateOnly AddBusinessDays(DateOnly from, int days)
    {
        var d = from;
        while (days > 0)
        {
            d = d.AddDays(1);
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days--;
        }
        return d;
    }

    [GeneratedRegex(@"\b(\d{1,2})\.(\d{1,2})\.(\d{4}|\d{2})\b")]
    private static partial Regex NumericDate();

    [GeneratedRegex(@"\b(\d{1,2})\s+([А-Яа-яё]+)\s+(\d{4})")]
    private static partial Regex WordDate();

    [GeneratedRegex(@"\d{1,3}(?:[\s ]\d{3})+(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?")]
    private static partial Regex Money();
}
