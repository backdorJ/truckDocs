using TruckDocs.Core.Models;
using TruckDocs.Core.Text;

namespace TruckDocs.Core;

/// <summary>
/// Собирает рейс из заявки, карты предприятия заказчика и профиля перевозчика.
/// Всё, что не нашлось или не сошлось, попадает в Warnings — это показывается человеку на проверку.
/// </summary>
public static class TripBuilder
{
    public static Trip Build(OrderData order, Party? customerCard, CarrierProfile carrier)
    {
        var warnings = new List<string>();

        // Заказчик: карта предприятия главнее, заявка — запасной вариант
        var customer = Merge(customerCard, order.Customer);
        if (customerCard is not null && order.Customer.Inn is not null && customerCard.Inn is not null
            && customerCard.Inn != order.Customer.Inn)
            warnings.Add($"ИНН в карте предприятия ({customerCard.Inn}) не совпадает с ИНН в заявке ({order.Customer.Inn}). Возможно, прислали карту другой компании.");

        if (customer.Inn is null) warnings.Add("Не найден ИНН заказчика.");
        else if (!Requisites.IsValidInn(customer.Inn)) warnings.Add($"ИНН заказчика {customer.Inn} не проходит проверку контрольной суммы.");

        var bank = customer.Bank;
        if (bank is null || bank.AccountNumber is null)
            warnings.Add("Нет банковских реквизитов заказчика (нужна карта предприятия).");
        else
        {
            if (!Requisites.IsValidBik(bank.Bik)) warnings.Add($"БИК заказчика «{bank.Bik}» выглядит неверно.");
            else if (!Requisites.IsValidAccount(bank.AccountNumber, bank.Bik))
                warnings.Add("Расчётный счёт заказчика не сходится с БИК по контрольному ключу — проверьте цифры.");
            if (bank.CorrespondentAccount is not null && Requisites.IsValidBik(bank.Bik)
                && !Requisites.IsValidCorrAccount(bank.CorrespondentAccount, bank.Bik))
                warnings.Add("Корр. счёт заказчика не сходится с БИК по контрольному ключу — проверьте цифры.");
        }

        // Реквизиты самого перевозчика: без них счёт бесполезен
        var own = carrier.Company;
        if (!Requisites.IsValidInn(own.Inn)) warnings.Add("Профиль перевозчика: ИНН не заполнен или неверный.");
        if (own.Bank is not { } ownBank || !Requisites.IsValidAccount(ownBank.AccountNumber, ownBank.Bik))
            warnings.Add("Профиль перевозчика: не заполнен или неверен расчётный счёт/БИК — по такому счёту не заплатят.");

        // Перевозчик в заявке должен быть наш
        if (order.Carrier.Inn is not null && carrier.Company.Inn is not null && order.Carrier.Inn != carrier.Company.Inn)
            warnings.Add($"В заявке указан перевозчик с ИНН {order.Carrier.Inn}, а в профиле — {carrier.Company.Inn}.");

        // Машина: сверяем номер из заявки с профилем
        if (order.Vehicle.TruckPlate is not null && carrier.Vehicle.TruckPlate is not null
            && RuText.CompactPlate(order.Vehicle.TruckPlate) != RuText.CompactPlate(carrier.Vehicle.TruckPlate))
            warnings.Add($"В заявке госномер {order.Vehicle.TruckPlate}, в профиле — {carrier.Vehicle.TruckPlate}.");

        if (order.Price is null) warnings.Add("Не найдена ставка за перевозку.");
        if (order.PriceIncludesVat == true) warnings.Add("Ставка в заявке указана с НДС — проверьте, работает ли перевозчик с НДС.");

        var from = order.Loading.City ?? RuText.CityFromAddress(order.Loading.Address);
        var to = order.Unloading.City ?? RuText.CityFromAddress(order.Unloading.Address);
        if (from is null || to is null) warnings.Add("Не удалось определить города маршрута — впишите вручную.");
        var route = $"{from ?? "?"} - {to ?? "?"}";

        DateOnly? due = null;
        if (order.Unloading.Date is { } unload && order.PaymentDays is { } days)
            due = order.PaymentDaysAreBusinessDays ? RuText.AddBusinessDays(unload, days) : unload.AddDays(days);

        warnings.AddRange(order.RiskClauses.Select(c => "Условие заявки: " + c));

        return new Trip
        {
            Order = order,
            Customer = customer,
            Carrier = carrier,
            Price = order.Price ?? 0,
            RouteText = route,
            PaymentDueDate = due,
            Warnings = warnings,
        };
    }

    private static Party Merge(Party? primary, Party fallback)
    {
        if (primary is null) return fallback;
        return primary with
        {
            FullName = primary.FullName ?? fallback.FullName,
            ShortName = primary.ShortName ?? fallback.ShortName,
            Inn = primary.Inn ?? fallback.Inn,
            Kpp = primary.Kpp ?? fallback.Kpp,
            LegalAddress = primary.LegalAddress ?? fallback.LegalAddress,
            DirectorName = primary.DirectorName ?? fallback.DirectorName,
        };
    }
}
