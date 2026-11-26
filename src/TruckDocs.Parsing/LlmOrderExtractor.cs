using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using TruckDocs.Core.Abstractions;
using TruckDocs.Core.Models;
using TruckDocs.Core.Text;

namespace TruckDocs.Parsing;

/// <summary>
/// Разбор заявки любой формы через LLM (Anthropic Messages API). Нужен, когда заказчик прислал
/// свою, нестандартную заявку и шаблонный парсер её не узнал.
/// Модель возвращает только JSON; всё равно проверяем ИНН/даты/суммы своим кодом в TripBuilder.
/// </summary>
public sealed class LlmOrderExtractor(HttpClient http, string apiKey, string model = "claude-haiku-5-5") : IOrderExtractor
{
    public string Name => $"llm:{model}";

    private const string Prompt = """
        Ниже текст договора-заявки на грузоперевозку. Извлеки данные и верни ТОЛЬКО JSON без пояснений, строго по схеме.
        Если поля нет в тексте — null. Ничего не придумывай. Даты в формате YYYY-MM-DD, суммы числом без пробелов.
        {
          "orderNumber": string|null, "orderDate": string|null,
          "customer": {"name": string|null, "inn": string|null, "kpp": string|null},
          "carrier":  {"name": string|null, "inn": string|null},
          "cargoName": string|null, "cargoPlaces": number|null, "cargoWeightTons": number|null, "cargoValue": number|null,
          "loading":   {"date": string|null, "address": string|null, "city": string|null, "contact": string|null},
          "unloading": {"date": string|null, "address": string|null, "city": string|null, "contact": string|null},
          "vehicle": {"brand": string|null, "truckPlate": string|null, "trailerPlate": string|null},
          "driverName": string|null,
          "price": number|null, "priceIncludesVat": boolean|null,
          "paymentTerms": string|null, "paymentDays": number|null, "paymentDaysAreBusinessDays": boolean|null,
          "riskClauses": [string]   // штрафы и условия, опасные для перевозчика, кратко
        }
        Текст заявки:
        """;

    public async Task<OrderData?> TryExtractAsync(string text, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = JsonContent.Create(new
            {
                model,
                max_tokens = 2000,
                messages = new[] { new { role = "user", content = Prompt + "\n" + text } },
            }),
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        var answer = body?["content"]?[0]?["text"]?.GetValue<string>();
        if (answer is null) return null;

        // На случай, если модель обернула ответ в ```json … ```
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        var json = JsonNode.Parse(answer[start..(end + 1)])!.AsObject();

        return Map(json, Name);
    }

    /// <summary>JSON от модели → OrderData. Вынесено отдельно, чтобы тестировать без сети.</summary>
    public static OrderData Map(JsonObject j, string extractedBy)
    {
        static string? S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
        static decimal? D(JsonNode? n) => n is JsonValue v ? v.TryGetValue<decimal>(out var d) ? d : RuText.ParseMoney(S(n)) : null;
        static DateOnly? Dt(JsonNode? n) => S(n) is { } s ? DateOnly.TryParse(s, out var d) ? d : RuText.ParseDate(s) : null;
        static bool? B(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

        RoutePoint Point(JsonNode? p) => new()
        {
            Date = Dt(p?["date"]),
            Address = S(p?["address"]),
            City = S(p?["city"]) ?? RuText.CityFromAddress(S(p?["address"])),
            Contact = S(p?["contact"]),
        };

        return new OrderData
        {
            OrderNumber = S(j["orderNumber"]),
            OrderDate = Dt(j["orderDate"]),
            Customer = new Party { ShortName = S(j["customer"]?["name"]), Inn = S(j["customer"]?["inn"]), Kpp = S(j["customer"]?["kpp"]) },
            Carrier = new Party { ShortName = S(j["carrier"]?["name"]), Inn = S(j["carrier"]?["inn"]) },
            CargoName = S(j["cargoName"]),
            CargoPlaces = D(j["cargoPlaces"]) is { } p ? (int)p : null,
            CargoWeightTons = D(j["cargoWeightTons"]),
            CargoValue = D(j["cargoValue"]),
            Loading = Point(j["loading"]),
            Unloading = Point(j["unloading"]),
            Vehicle = new Vehicle { Brand = S(j["vehicle"]?["brand"]), TruckPlate = S(j["vehicle"]?["truckPlate"]), TrailerPlate = S(j["vehicle"]?["trailerPlate"]) },
            DriverName = S(j["driverName"]),
            Price = D(j["price"]),
            PriceIncludesVat = B(j["priceIncludesVat"]),
            PaymentTerms = S(j["paymentTerms"]),
            PaymentDays = D(j["paymentDays"]) is { } pd ? (int)pd : null,
            PaymentDaysAreBusinessDays = B(j["paymentDaysAreBusinessDays"]) ?? true,
            RiskClauses = j["riskClauses"] is JsonArray arr ? arr.Select(S).OfType<string>().ToList() : new(),
            ExtractedBy = extractedBy,
        };
    }
}
