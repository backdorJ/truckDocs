using System.Text.RegularExpressions;
using TruckDocs.Core.Models;

namespace TruckDocs.Parsing;

/// <summary>
/// Разбирает карту предприятия. Карты у всех разные, но почти всегда это пары «название поля — значение»:
/// строки таблицы («ИНН | 5009137680») или текст («ИНН: 5009137680»). Поэтому ищем по ключевым словам в названии поля.
/// </summary>
public static partial class CompanyCardParser
{
    /// <summary>Похоже ли на карту предприятия (а не на заявку или счёт).</summary>
    public static bool LooksLikeCompanyCard(string text) =>
        Regex.IsMatch(text, @"карт[аы]\s+(предприятия|организации|партн[её]ра|контрагента)|реквизиты\s+(организации|компании|предприятия)", RegexOptions.IgnoreCase)
        || (text.Contains("БИК", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(text, @"(Р/с|Расч[её]тный сч[её]т)", RegexOptions.IgnoreCase)
            && !text.Contains("ДОГОВОР-ЗАЯВКА", StringComparison.OrdinalIgnoreCase));

    public static Party Parse(string text)
    {
        var pairs = ToPairs(text);

        string? Find(params string[] keys) =>
            pairs.FirstOrDefault(p => keys.Any(k => p.Label.Contains(k, StringComparison.OrdinalIgnoreCase)))?.Value;

        // Порядок важен: «Полное наименование банка» должно найтись раньше, чем «Полное наименование» фирмы
        var bankName = Find("наименование банка", "банк получателя", "банк");
        var labelsWithoutBank = pairs.Where(p => !p.Label.Contains("банк", StringComparison.OrdinalIgnoreCase)).ToList();
        string? FindNoBank(params string[] keys) =>
            labelsWithoutBank.FirstOrDefault(p => keys.Any(k => p.Label.Contains(k, StringComparison.OrdinalIgnoreCase)))?.Value;

        var director = Find("ФИО руководителя", "руководитель", "генеральный директор", "директор");
        string? directorName = director, directorPosition = null;
        if (director?.Split('/') is [var name, var position])
        {
            directorName = name.Trim();
            directorPosition = position.Trim();
        }

        return new Party
        {
            FullName = Clean(FindNoBank("полное наименование", "наименование организации", "наименование")),
            ShortName = Clean(FindNoBank("сокращенное", "сокращённое", "краткое")),
            Inn = Digits(Find("ИНН"), 10, 12),
            Kpp = Digits(Find("КПП"), 9),
            Ogrn = Digits(Find("ОГРН"), 13, 15),
            LegalAddress = Clean(Find("юридический адрес", "адрес регистрации", "адрес")),
            Phone = Clean(Find("телефон", "тел.")),
            Email = Clean(Find("e-mail", "email", "эл. почта", "электронная почта")),
            DirectorName = directorName,
            DirectorPosition = directorPosition,
            Bank = new BankDetails
            {
                BankName = Clean(bankName),
                Bik = Digits(Find("БИК"), 9),
                AccountNumber = Digits(Find("расчетный счет", "расчётный счёт", "расчетный счёт", "р/с", "р/счет"), 20),
                CorrespondentAccount = Digits(Find("корреспондентский", "корр", "к/с"), 20),
            },
            EdoOperator = Clean(Find("оператор")),
            EdoId = Clean(Find("идентификатор", "ID участника", "ID ЭДО")),
        };
    }

    private record Pair(string Label, string Value);

    private static List<Pair> ToPairs(string text)
    {
        var result = new List<Pair>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            string[] parts = line.Contains('|')
                ? line.Split('|').Select(s => s.Trim()).ToArray()
                : LabelValue().Match(line) is { Success: true } m ? new[] { m.Groups[1].Value, m.Groups[2].Value } : Array.Empty<string>();

            if (parts.Length >= 2 && parts[1].Length > 0)
                result.Add(new Pair(parts[0], string.Join(" ", parts.Skip(1)).Trim()));
        }
        return result;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>Из значения берём только цифровую последовательность нужной длины.</summary>
    private static string? Digits(string? s, params int[] lengths)
    {
        if (s is null) return null;
        var compact = Regex.Replace(s, @"[\s\-]", "");
        foreach (Match m in Regex.Matches(compact, @"\d+"))
            if (lengths.Contains(m.Value.Length)) return m.Value;
        return null;
    }

    [GeneratedRegex(@"^([^:]{2,60}):\s*(.+)$")]
    private static partial Regex LabelValue();
}
