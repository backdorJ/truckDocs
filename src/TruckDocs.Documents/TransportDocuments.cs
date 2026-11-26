using TruckDocs.Core.Abstractions;
using TruckDocs.Core.Models;
using TruckDocs.Core.Text;
using TruckDocs.Documents.Docx;

namespace TruckDocs.Documents;

/// <summary>Общие куски счёта и акта: стороны, таблица услуг, подписи.</summary>
internal static class Blocks
{
    public static string PartyLine(Party p, bool withBank)
    {
        var parts = new List<string> { p.DisplayName };
        if (p.Inn is not null) parts.Add($"ИНН {p.Inn}");
        if (p.Kpp is not null) parts.Add($"КПП {p.Kpp}");
        if (p.Ogrn is not null) parts.Add($"{(p.Ogrn.Length == 15 ? "ОГРНИП" : "ОГРН")} {p.Ogrn}");
        if (p.LegalAddress is not null) parts.Add(p.LegalAddress);
        if (p.Phone is not null) parts.Add($"тел. {p.Phone}");
        if (withBank && p.Bank is { AccountNumber: not null } b)
            parts.Add($"р/с {b.AccountNumber} в {b.BankName}, БИК {b.Bik}, к/с {b.CorrespondentAccount}");
        return string.Join(", ", parts);
    }

    public static Table Parties(Trip trip) => new(
        new[] { 28, 152 },
        new[]
        {
            new[] { Cell.Of("Исполнитель:"), Cell.Of(PartyLine(trip.Carrier.Company, withBank: true), bold: true) },
            new[] { Cell.Of("Заказчик:"), Cell.Of(PartyLine(trip.Customer, withBank: true), bold: true) },
        },
        Borders: false);

    public static string? Basis(Trip trip) =>
        trip.Order.OrderNumber is null ? null
            : $"Основание: договор-заявка № {trip.Order.OrderNumber}" + (trip.Order.OrderDate is { } d ? $" от {d:dd.MM.yyyy}" : "");

    public static string ServiceDescription(Trip trip)
    {
        var v = trip.Carrier.Vehicle;
        var vehicle = string.Join(" ", new[] { v.Brand, v.TruckPlate }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var lines = new List<string> { trip.Carrier.ServiceName };
        var dates = (trip.Order.Loading.Date, trip.Order.Unloading.Date) switch
        {
            ({ } a, { } b) when a != b => $" ({a:dd.MM.yyyy}–{b:dd.MM.yyyy})",
            ({ } a, _) => $" ({a:dd.MM.yyyy})",
            _ => "",
        };
        lines.Add($"{trip.RouteText}{dates}" + (vehicle.Length > 0 ? $", автомобиль: {vehicle}" : ""));
        var driver = trip.Carrier.DriverName ?? trip.Order.DriverName;
        if (driver is not null) lines.Add($"Водитель: {driver}");
        return string.Join("\n", lines);
    }

    public static Table Items(Trip trip)
    {
        var price = RuText.FormatMoney(trip.Price);
        return new Table(
            new[] { 10, 92, 16, 14, 24, 24 },
            new[]
            {
                new[]
                {
                    Cell.Of("№", true, Align.Center) with { Shade = true }, Cell.Of("Наименование работ, услуг", true, Align.Center) with { Shade = true },
                    Cell.Of("Кол-во", true, Align.Center) with { Shade = true }, Cell.Of("Ед.", true, Align.Center) with { Shade = true },
                    Cell.Of("Цена", true, Align.Center) with { Shade = true }, Cell.Of("Сумма", true, Align.Center) with { Shade = true },
                },
                new[]
                {
                    Cell.Of("1", align: Align.Center), Cell.Of(ServiceDescription(trip)), Cell.Of("1", align: Align.Center),
                    Cell.Of("усл.", align: Align.Center), Cell.Of(price, align: Align.Right), Cell.Of(price, align: Align.Right),
                },
            });
    }

    public static Table Totals(Trip trip, string totalLabel)
    {
        var price = RuText.FormatMoney(trip.Price);
        return new Table(
            new[] { 132, 24, 24 },
            new[]
            {
                new[] { Cell.Of("Итого:", true, Align.Right) with { NoBorder = true }, Cell.Of("", span: 1) with { NoBorder = true }, Cell.Of(price, true, Align.Right) with { NoBorder = true } },
                new[] { Cell.Of("Без налога (НДС):", true, Align.Right) with { NoBorder = true }, Cell.Of("") with { NoBorder = true }, Cell.Of("-", true, Align.Right) with { NoBorder = true } },
                new[] { Cell.Of(totalLabel, true, Align.Right) with { NoBorder = true }, Cell.Of("") with { NoBorder = true }, Cell.Of(price, true, Align.Right) with { NoBorder = true } },
            },
            Borders: false);
    }

    public static Table Signatures(Trip trip, bool both)
    {
        var carrier = Initials(trip.Carrier.Company.DirectorName ?? trip.Carrier.DriverName);
        var customer = Initials(trip.Customer.DirectorName);
        var left = new List<Para>
        {
            Para.Of("ИСПОЛНИТЕЛЬ", bold: true, after: 14),
            Para.Of($"________________ / {carrier} /"),
            Para.Of("Печать не предусмотрена", size: 8),
        };
        var right = new List<Para>();
        if (both)
        {
            right.Add(Para.Of("ЗАКАЗЧИК", bold: true));
            right.Add(Para.Of(trip.Customer.DirectorPosition ?? "", size: 8, after: 6));
            right.Add(Para.Of($"________________ / {customer} /"));
            right.Add(Para.Of("М.П.", size: 8));
        }
        return new Table(new[] { 90, 90 }, new[] { new[] { new Cell(left), new Cell(right) } }, Borders: false);
    }

    /// <summary>«Молчанов Денис Васильевич» → «Молчанов Д.В.»</summary>
    public static string Initials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "                    ";
        var p = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length switch
        {
            >= 3 => $"{p[0]} {p[1][0]}.{p[2][0]}.",
            2 => $"{p[0]} {p[1][0]}.",
            _ => p[0],
        };
    }
}

/// <summary>Счёт на оплату.</summary>
public sealed class InvoiceGenerator : IDocumentGenerator
{
    public DocumentKind Kind => DocumentKind.Invoice;

    public GeneratedDocument Generate(Trip trip, string number, DateOnly date)
    {
        var c = trip.Carrier.Company;
        var bank = c.Bank ?? new BankDetails();
        var doc = new DocxBuilder();

        // Банковский блок получателя платежа (исполнителя), как в стандартном счёте
        doc.Add(new Table(
            new[] { 55, 45, 20, 60 },
            new[]
            {
                new[] { Cell.Of(bank.BankName ?? "", span: 2), Cell.Of("БИК"), Cell.Of(bank.Bik ?? "") },
                new[] { Cell.Of("Банк получателя", size: 8, span: 2), Cell.Of("Сч. №"), Cell.Of(bank.CorrespondentAccount ?? "") },
                new[] { Cell.Of($"ИНН {c.Inn}"), Cell.Of(c.Kpp is null ? "КПП" : $"КПП {c.Kpp}"), Cell.Of("Сч. №"), Cell.Of(bank.AccountNumber ?? "") },
                new[] { Cell.Of(c.DisplayName, span: 2), Cell.Of(""), Cell.Of("") },
                new[] { Cell.Of("Получатель", size: 8, span: 2), Cell.Of(""), Cell.Of("") },
            }));
        doc.Add(Para.Empty(8));
        doc.Add(new Para(new[] { new Run($"Счёт на оплату № {number} от {RuText.FormatLong(date)}", true, 14) }, SpaceAfterPt: 8, BottomBorder: true));
        doc.Add(Para.Empty(4));
        doc.Add(Blocks.Parties(trip));
        if (Blocks.Basis(trip) is { } basis) doc.Add(basis, after: 6);
        else doc.Add(Para.Empty(6));
        doc.Add(Blocks.Items(trip));
        doc.Add(Para.Empty()); // таблицы подряд Word склеивает в одну — разделяем пустым абзацем
        doc.Add(Blocks.Totals(trip, "Всего к оплате:"));
        doc.Add(Para.Empty(4));
        doc.Add($"Всего наименований 1, на сумму {RuText.FormatMoney(trip.Price)} руб.");
        doc.Add($"{AmountInWords.Rubles(trip.Price)}. Без НДС.", bold: true, after: 4);
        if (trip.PaymentDueDate is { } due) doc.Add($"Срок оплаты: до {due:dd.MM.yyyy} (по условиям договора-заявки).", size: 9);
        doc.Add(new Para(Array.Empty<Run>(), SpaceAfterPt: 10, BottomBorder: true));
        doc.Add(Para.Empty(6));
        doc.Add(Blocks.Signatures(trip, both: false));

        return new GeneratedDocument(Kind, number, date, $"Счет_{number}.docx", doc.Build());
    }
}

/// <summary>Акт оказанных услуг.</summary>
public sealed class ActGenerator : IDocumentGenerator
{
    public DocumentKind Kind => DocumentKind.Act;

    public GeneratedDocument Generate(Trip trip, string number, DateOnly date)
    {
        var doc = new DocxBuilder();
        doc.Add(new Para(new[] { new Run($"Акт № {number} от {RuText.FormatLong(date)}", true, 14) }, SpaceAfterPt: 8, BottomBorder: true));
        doc.Add(Para.Empty(4));
        doc.Add(Blocks.Parties(trip));
        if (Blocks.Basis(trip) is { } basis) doc.Add(basis, after: 6);
        else doc.Add(Para.Empty(6));
        doc.Add(Blocks.Items(trip));
        doc.Add(Para.Empty()); // таблицы подряд Word склеивает в одну — разделяем пустым абзацем
        doc.Add(Blocks.Totals(trip, "Всего:"));
        doc.Add(Para.Empty(4));
        doc.Add($"Всего оказано услуг 1, на сумму {RuText.FormatMoney(trip.Price)} руб.");
        doc.Add($"{AmountInWords.Rubles(trip.Price)}. Без НДС.", bold: true, after: 6);
        doc.Add("Вышеперечисленные услуги выполнены полностью и в срок. Заказчик претензий по объёму, качеству и срокам оказания услуг не имеет.", after: 4);
        doc.Add(new Para(Array.Empty<Run>(), SpaceAfterPt: 10, BottomBorder: true));
        doc.Add(Para.Empty(6));
        doc.Add(Blocks.Signatures(trip, both: true));

        return new GeneratedDocument(Kind, number, date, $"Акт_{number}.docx", doc.Build());
    }
}
