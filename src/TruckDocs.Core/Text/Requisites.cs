namespace TruckDocs.Core.Text;

/// <summary>
/// Проверка контрольных сумм реквизитов. Ловит опечатки и ошибки распознавания
/// до того, как счёт с неверным ИНН или р/с уйдёт заказчику.
/// </summary>
public static class Requisites
{
    public static bool IsValidInn(string? inn)
    {
        if (inn is null || !inn.All(char.IsDigit)) return false;
        int[] d = inn.Select(c => c - '0').ToArray();
        return inn.Length switch
        {
            10 => Check(d, new[] { 2, 4, 10, 3, 5, 9, 4, 6, 8 }) == d[9],
            12 => Check(d, new[] { 7, 2, 4, 10, 3, 5, 9, 4, 6, 8 }) == d[10]
                  && Check(d, new[] { 3, 7, 2, 4, 10, 3, 5, 9, 4, 6, 8 }) == d[11],
            _ => false,
        };

        static int Check(int[] digits, int[] weights)
        {
            var sum = 0;
            for (var i = 0; i < weights.Length; i++) sum += digits[i] * weights[i];
            return sum % 11 % 10;
        }
    }

    public static bool IsValidBik(string? bik) => bik is { Length: 9 } && bik.All(char.IsDigit) && bik.StartsWith("04");

    /// <summary>Контрольный ключ расчётного счёта по БИК (алгоритм ЦБ).</summary>
    public static bool IsValidAccount(string? account, string? bik)
    {
        if (account is not { Length: 20 } || !account.All(char.IsDigit) || !IsValidBik(bik)) return false;
        return Key(bik![^3..] + account);
    }

    /// <summary>Корр. счёт проверяется с «0» + 5-6 цифры БИК.</summary>
    public static bool IsValidCorrAccount(string? account, string? bik)
    {
        if (account is not { Length: 20 } || !account.All(char.IsDigit) || !IsValidBik(bik)) return false;
        return Key("0" + bik!.Substring(4, 2) + account);
    }

    private static bool Key(string s)
    {
        int[] w = { 7, 1, 3 };
        var sum = 0;
        for (var i = 0; i < 23; i++) sum += (s[i] - '0') * w[i % 3] % 10;
        return sum % 10 == 0;
    }
}
