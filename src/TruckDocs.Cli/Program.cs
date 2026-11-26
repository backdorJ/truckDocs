using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using TruckDocs.Core.Abstractions;
using TruckDocs.Core.Models;
using TruckDocs.Core.Text;
using TruckDocs.Documents;
using TruckDocs.Parsing;

// Использование:
//   truckdocs <заявка.pdf> [карта.docx ...] [--carrier carrier.json] [--out out] [--number 3] [--date 27.11.2026]
// Без ключа ANTHROPIC_API_KEY работает только шаблонный парсер «ДОГОВОР-ЗАЯВКА».

var json = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    Converters = { new JsonStringEnumConverter() },
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

var files = new List<string>();
string carrierPath = "carrier.json", outDir = "out";
int? numberOverride = null;
DateOnly? dateOverride = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--carrier": carrierPath = args[++i]; break;
        case "--out": outDir = args[++i]; break;
        case "--number": numberOverride = int.Parse(args[++i]); break;
        case "--date": dateOverride = RuText.ParseDate(args[++i]); break;
        default: files.Add(args[i]); break;
    }
}

if (files.Count == 0)
{
    Console.WriteLine("Укажите файлы заявки и карты предприятия: truckdocs заявка.pdf карта.docx");
    return 1;
}

if (!File.Exists(carrierPath))
{
    File.WriteAllText(carrierPath, JsonSerializer.Serialize(SampleCarrier(), json));
    Console.WriteLine($"Создан шаблон профиля перевозчика {carrierPath} — заполните реквизиты ИП и банк и запустите снова.");
    return 2;
}
var carrier = JsonSerializer.Deserialize<CarrierProfile>(File.ReadAllText(carrierPath), json)!;

// Сборка конвейера
var extractors = new List<IOrderExtractor> { new TemplateOrderExtractor() };
if (Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is { Length: > 0 } key)
    extractors.Add(new LlmOrderExtractor(new HttpClient(), key));
var pipeline = new OrderPipeline(CompositeTextExtractor.Default(), extractors);

var incoming = new IncomingOrder(
    Source: "cli",
    Subject: null,
    BodyText: null,
    Files: files.Select(f => new Attachment(Path.GetFileName(f), "", File.ReadAllBytes(f))).ToList(),
    ReceivedAt: DateTimeOffset.Now);

var result = await pipeline.ProcessAsync(incoming, carrier);
foreach (var line in result.Log) Console.WriteLine("• " + line);
if (result.Trip is not { } trip)
{
    Console.WriteLine("Ошибка: " + result.Error);
    return 3;
}

Console.WriteLine();
Console.WriteLine($"Заказчик:  {trip.Customer.DisplayName}, ИНН {trip.Customer.Inn}");
Console.WriteLine($"Маршрут:   {trip.RouteText}  ({trip.Order.Loading.Date:dd.MM} → {trip.Order.Unloading.Date:dd.MM})");
Console.WriteLine($"Ставка:    {RuText.FormatMoney(trip.Price)} руб.");
Console.WriteLine($"Оплата до: {trip.PaymentDueDate:dd.MM.yyyy}  ({trip.Order.PaymentDays} дн.)");
if (trip.Warnings.Count > 0)
{
    Console.WriteLine("\nПроверьте:");
    foreach (var w in trip.Warnings) Console.WriteLine("  ! " + w);
}

// Нумерация документов: «N-ГГГГ», счётчик хранится в out/numbering.json и сбрасывается с новым годом
Directory.CreateDirectory(outDir);
var date = dateOverride ?? trip.Order.Unloading.Date ?? DateOnly.FromDateTime(DateTime.Today);
var numberingPath = Path.Combine(outDir, "numbering.json");
var numbering = File.Exists(numberingPath)
    ? JsonSerializer.Deserialize<Dictionary<int, int>>(File.ReadAllText(numberingPath))!
    : new Dictionary<int, int>();
var seq = numberOverride ?? numbering.GetValueOrDefault(date.Year) + 1;
numbering[date.Year] = Math.Max(seq, numbering.GetValueOrDefault(date.Year));
File.WriteAllText(numberingPath, JsonSerializer.Serialize(numbering));
var number = $"{seq}-{date.Year}";

Console.WriteLine();
foreach (var gen in new IDocumentGenerator[] { new InvoiceGenerator(), new ActGenerator() })
{
    var doc = gen.Generate(trip, number, date);
    var path = Path.Combine(outDir, doc.FileName);
    File.WriteAllBytes(path, doc.Content);
    Console.WriteLine($"Готово: {path}");
}
File.WriteAllText(Path.Combine(outDir, $"Рейс_{number}.json"), JsonSerializer.Serialize(trip, json));
return 0;

static CarrierProfile SampleCarrier() => new()
{
    Company = new Party
    {
        FullName = "Индивидуальный предприниматель Фамилия Имя Отчество",
        ShortName = "ИП Фамилия Имя Отчество",
        Inn = "000000000000",
        Ogrn = "000000000000000",
        LegalAddress = "адрес регистрации ИП",
        Phone = "+7 900 000-00-00",
        DirectorName = "Фамилия Имя Отчество",
        Bank = new BankDetails { BankName = "Банк", Bik = "000000000", AccountNumber = "00000000000000000000", CorrespondentAccount = "00000000000000000000" },
    },
    Vehicle = new Vehicle { Brand = "Камаз", TruckPlate = "А000АА000", TrailerPlate = "АА000000" },
    DriverName = "Фамилия Имя Отчество",
};
