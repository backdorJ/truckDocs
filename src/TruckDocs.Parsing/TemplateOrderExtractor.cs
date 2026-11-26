using System.Globalization;
using System.Text.RegularExpressions;
using TruckDocs.Core.Abstractions;
using TruckDocs.Core.Models;
using TruckDocs.Core.Text;

namespace TruckDocs.Parsing;

/// <summary>
/// Парсер типовой формы «ДОГОВОР-ЗАЯВКА на перевозку груза» (пункты 1–10 с подписями полей в скобках).
/// В этой форме значение стоит строкой выше своей подписи: «Стройматериалы» над «(наименование груза)».
/// Работает на тексте из `pdftotext -layout`. Для чужих форм заявок — LlmOrderExtractor.
/// </summary>
public sealed partial class TemplateOrderExtractor : IOrderExtractor
{
    public string Name => "template:dogovor-zayavka";

    public Task<OrderData?> TryExtractAsync(string text, CancellationToken ct = default)
    {
        if (!text.Contains("ДОГОВОР-ЗАЯВКА", StringComparison.OrdinalIgnoreCase) || !text.Contains("(наименование груза)"))
            return Task.FromResult<OrderData?>(null);

        var lines = text.Replace("\r", "").Split('\n');

        // Стороны: «ООО "ПРОДЖЕКТ", ИНН 5009137680     ИП Иванов…, ИНН 1644…» — первый заказчик, второй перевозчик
        var parties = lines.SelectMany(Columns)
            .Select(c => PartyWithInn().Match(c))
            .Where(m => m.Success)
            .Select(m => new Party { ShortName = m.Groups[1].Value.Trim(), Inn = m.Groups[2].Value })
            .DistinctBy(p => p.Inn)
            .ToList();

        var loadingLine = ValueAbove(lines, "(дата, адрес места загрузки");
        var unloadingLine = ValueAbove(lines, "(дата, адрес места выгрузки");

        var vehicleCols = Columns(ValueAbove(lines, "(марка)") ?? "").ToArray();
        var plates = vehicleCols.Length > 1 ? vehicleCols[^1].Split('/', StringSplitOptions.TrimEntries) : Array.Empty<string>();

        var priceCols = Columns(ValueAbove(lines, "(стоимость услуги в рублях)") ?? "").ToArray();
        var priceText = priceCols.FirstOrDefault();

        var payment = Section(lines, "(стоимость услуги в рублях)", "9.");
        var paymentText = payment is null ? null : Regex.Replace(payment, @"\s+", " ").Trim();
        var days = paymentText is null ? null : PaymentDays().Match(paymentText);

        var order = new OrderData
        {
            OrderNumber = OrderNumber().Match(text) is { Success: true } n ? n.Groups[1].Value : null,
            OrderDate = RuText.ParseDate(DateLine().Match(text) is { Success: true } d ? d.Groups[1].Value : null),
            Customer = parties.ElementAtOrDefault(0) ?? new Party(),
            Carrier = parties.ElementAtOrDefault(1) ?? new Party(),
            CargoName = ValueAbove(lines, "(наименование груза)"),
            CargoPlaces = int.TryParse(ValueAbove(lines, "(количество грузовых мест)"), out var places) ? places : null,
            CargoWeightTons = ParseDecimal(ValueAbove(lines, "(масса нетто")),
            CargoValue = RuText.ParseMoney(priceCols.ElementAtOrDefault(1)),
            Loading = ParsePoint(loadingLine),
            Unloading = ParsePoint(unloadingLine),
            Vehicle = new Vehicle
            {
                Brand = vehicleCols.FirstOrDefault(),
                TruckPlate = plates.ElementAtOrDefault(0),
                TrailerPlate = plates.ElementAtOrDefault(1),
            },
            DriverName = ValueAbove(lines, "(фамилия, имя, отчество, данные о средствах связи"),
            Price = RuText.ParseMoney(priceText),
            PriceIncludesVat = priceText is null ? null : !Regex.IsMatch(priceText, @"без\s+НДС", RegexOptions.IgnoreCase),
            PaymentTerms = paymentText,
            PaymentDays = days is { Success: true } ? int.Parse(days.Groups[1].Value) : null,
            PaymentDaysAreBusinessDays = days is not { Success: true } || Regex.IsMatch(days.Groups[2].Value, "^(рабоч|банковск)", RegexOptions.IgnoreCase),
            RiskClauses = RiskClauses(Section(lines, "9. Условия", "10.")),
            ExtractedBy = Name,
        };

        return Task.FromResult<OrderData?>(order);
    }

    /// <summary>«26.11.2026 - Волгоградская обл, Волгоград, ул. … - 16 палет…, +7 953… Алексей»</summary>
    private static RoutePoint ParsePoint(string? line)
    {
        if (line is null) return new RoutePoint();
        var parts = Regex.Split(line, @"\s+-\s+");
        var date = RuText.ParseDate(parts[0]);
        var address = date is not null ? parts.ElementAtOrDefault(1) : parts[0];
        var rest = string.Join(" - ", parts.Skip(date is not null ? 2 : 1));
        var phone = Phone().Match(rest);
        return new RoutePoint
        {
            Date = date,
            Address = address?.Trim(),
            City = RuText.CityFromAddress(address),
            Contact = phone.Success ? rest[phone.Index..].Trim() : null,
            Note = phone.Success ? rest[..phone.Index].Trim(' ', ',') : rest.Trim(),
        };
    }

    /// <summary>Ближайшая непустая строка над строкой с подписью поля.</summary>
    private static string? ValueAbove(string[] lines, string caption)
    {
        var idx = Array.FindIndex(lines, l => l.Contains(caption, StringComparison.OrdinalIgnoreCase));
        for (var i = idx - 1; i >= 0 && idx > 0; i--)
        {
            var t = lines[i].Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith('(') || Regex.IsMatch(t, @"^\d+\.\s")) return null; // упёрлись в другую подпись или заголовок пункта
            return Regex.Replace(t, @"\s{3,}", "   ");
        }
        return null;
    }

    /// <summary>Текст между строкой с <paramref name="from"/> и строкой, начинающейся с <paramref name="toPrefix"/>.</summary>
    private static string? Section(string[] lines, string from, string toPrefix)
    {
        var start = Array.FindIndex(lines, l => l.Contains(from, StringComparison.OrdinalIgnoreCase));
        if (start < 0) return null;
        var parts = new List<string>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith(toPrefix) && !t.StartsWith(toPrefix + "1")) break; // «9.1.» внутри раздела 9 — не конец
            parts.Add(t);
        }
        return string.Join("\n", parts).Trim();
    }

    private static List<string> RiskClauses(string? section)
    {
        if (section is null) return new();
        var flat = Regex.Replace(section, @"\s+", " ");
        return Regex.Split(flat, @"(?=\b\d+\.\d+\.\s)")
            .Select(s => s.Trim())
            .Where(s => Regex.IsMatch(s, @"штраф|полную материальную|независимо|не зависимо", RegexOptions.IgnoreCase))
            .ToList();
    }

    private static IEnumerable<string> Columns(string line) =>
        Regex.Split(line.Trim(), @"\s{3,}").Where(c => c.Length > 0);

    private static decimal? ParseDecimal(string? s) =>
        s is not null && decimal.TryParse(s.Replace(',', '.').Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    [GeneratedRegex(@"ДОГОВОР-ЗАЯВКА\s*№\s*\D{0,2}?(\d[\w\-/]*)", RegexOptions.IgnoreCase)]
    private static partial Regex OrderNumber();

    [GeneratedRegex(@"Дата\s+(\d{1,2}\s+[А-Яа-яё]+\s+\d{4}|\d{1,2}\.\d{1,2}\.\d{2,4})")]
    private static partial Regex DateLine();

    [GeneratedRegex(@"^(.+?),\s*ИНН\s*(\d{12}|\d{10})\b")]
    private static partial Regex PartyWithInn();

    [GeneratedRegex(@"(\d+)\s*(рабоч\w*|календарн\w*|банковск\w*)?\s*дн", RegexOptions.IgnoreCase)]
    private static partial Regex PaymentDays();

    [GeneratedRegex(@"(\+7|8)[\s\-(]*\d{3}")]
    private static partial Regex Phone();
}
