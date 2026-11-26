using System.Text;

namespace TruckDocs.Core.Text;

/// <summary>Сумма прописью: 65000 → «Шестьдесят пять тысяч рублей 00 копеек».</summary>
public static class AmountInWords
{
    private static readonly string[] UnitsMale = { "", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять" };
    private static readonly string[] UnitsFemale = { "", "одна", "две", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять" };
    private static readonly string[] Teens = { "десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать", "пятнадцать", "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать" };
    private static readonly string[] Tens = { "", "", "двадцать", "тридцать", "сорок", "пятьдесят", "шестьдесят", "семьдесят", "восемьдесят", "девяносто" };
    private static readonly string[] Hundreds = { "", "сто", "двести", "триста", "четыреста", "пятьсот", "шестьсот", "семьсот", "восемьсот", "девятьсот" };

    // (одна, две-четыре, пять+), женский род?
    private static readonly (string One, string Few, string Many, bool Female)[] Scales =
    {
        ("рубль", "рубля", "рублей", false),
        ("тысяча", "тысячи", "тысяч", true),
        ("миллион", "миллиона", "миллионов", false),
        ("миллиард", "миллиарда", "миллиардов", false),
    };

    public static string Rubles(decimal amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var rubles = (long)Math.Truncate(amount);
        var kopecks = (int)((amount - rubles) * 100);

        var sb = new StringBuilder();
        if (rubles == 0)
        {
            sb.Append("ноль рублей");
        }
        else
        {
            var groups = new List<int>();
            for (var r = rubles; r > 0; r /= 1000) groups.Add((int)(r % 1000));
            if (groups.Count > Scales.Length) throw new ArgumentOutOfRangeException(nameof(amount));

            for (var i = groups.Count - 1; i >= 0; i--)
            {
                var g = groups[i];
                var scale = Scales[i];
                if (g == 0 && i != 0) continue;
                if (g != 0) Append(sb, Triplet(g, scale.Female));
                // слово «рублей» ставим всегда, даже если последняя группа нулевая
                if (g != 0 || i == 0) Append(sb, Plural(g, scale.One, scale.Few, scale.Many));
            }
        }

        sb.Append(' ').Append(kopecks.ToString("00")).Append(' ').Append(Plural(kopecks, "копейка", "копейки", "копеек"));
        var s = sb.ToString().Trim();
        return char.ToUpper(s[0]) + s[1..];
    }

    private static string Triplet(int n, bool female)
    {
        var parts = new List<string>();
        parts.Add(Hundreds[n / 100]);
        var rest = n % 100;
        if (rest is >= 10 and < 20) parts.Add(Teens[rest - 10]);
        else
        {
            parts.Add(Tens[rest / 10]);
            parts.Add((female ? UnitsFemale : UnitsMale)[rest % 10]);
        }
        return string.Join(' ', parts.Where(p => p.Length > 0));
    }

    public static string Plural(long n, string one, string few, string many)
    {
        var n100 = n % 100;
        if (n100 is >= 11 and <= 14) return many;
        return (n % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many };
    }

    private static void Append(StringBuilder sb, string s)
    {
        if (s.Length == 0) return;
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(s);
    }
}
