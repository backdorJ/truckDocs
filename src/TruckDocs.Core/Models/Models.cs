using System.Text.Json.Serialization;

namespace TruckDocs.Core.Models;

/// <summary>Банковские реквизиты.</summary>
public record BankDetails
{
    public string? BankName { get; init; }
    public string? Bik { get; init; }
    public string? AccountNumber { get; init; }        // расчётный счёт
    public string? CorrespondentAccount { get; init; } // корр. счёт
}

/// <summary>Контрагент: заказчик или перевозчик (ООО или ИП).</summary>
public record Party
{
    public string? FullName { get; init; }
    public string? ShortName { get; init; }
    public string? Inn { get; init; }
    public string? Kpp { get; init; }        // у ИП нет
    public string? Ogrn { get; init; }       // ОГРН / ОГРНИП
    public string? LegalAddress { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? DirectorName { get; init; }
    public string? DirectorPosition { get; init; }
    public BankDetails? Bank { get; init; }
    public string? EdoOperator { get; init; }
    public string? EdoId { get; init; }

    /// <summary>Имя для документов: сокращённое, если есть.</summary>
    [JsonIgnore]
    public string DisplayName => ShortName ?? FullName ?? "";
}

/// <summary>Точка погрузки / выгрузки.</summary>
public record RoutePoint
{
    public DateOnly? Date { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? Contact { get; init; }
    public string? Note { get; init; }
}

public record Vehicle
{
    public string? Brand { get; init; }        // Камаз
    public string? TruckPlate { get; init; }   // К872НУ716
    public string? TrailerPlate { get; init; } // АС724416
}

/// <summary>Что удалось вытащить из договора-заявки заказчика.</summary>
public record OrderData
{
    public string? OrderNumber { get; init; }
    public DateOnly? OrderDate { get; init; }
    public Party Customer { get; init; } = new();
    public Party Carrier { get; init; } = new();
    public string? CargoName { get; init; }
    public int? CargoPlaces { get; init; }
    public decimal? CargoWeightTons { get; init; }
    public decimal? CargoValue { get; init; }
    public RoutePoint Loading { get; init; } = new();
    public RoutePoint Unloading { get; init; } = new();
    public Vehicle Vehicle { get; init; } = new();
    public string? DriverName { get; init; }
    public decimal? Price { get; init; }
    public bool? PriceIncludesVat { get; init; }
    public string? PaymentTerms { get; init; }
    public int? PaymentDays { get; init; }
    public bool PaymentDaysAreBusinessDays { get; init; } = true;
    /// <summary>Штрафы и рискованные условия — показать перевозчику перед подписью.</summary>
    public List<string> RiskClauses { get; init; } = new();
    public string ExtractedBy { get; init; } = "";
}

/// <summary>Профиль перевозчика (папин ИП): заполняется один раз.</summary>
public record CarrierProfile
{
    public Party Company { get; init; } = new();
    public Vehicle Vehicle { get; init; } = new();
    public string? DriverName { get; init; }
    public string ServiceName { get; init; } = "Оказание транспортных услуг";
}

public enum TripStatus { Draft, DocumentsReady, DocumentsSent, Paid }

/// <summary>Рейс: всё, что нужно для счёта и акта.</summary>
public record Trip
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required OrderData Order { get; init; }
    public required Party Customer { get; init; }
    public required CarrierProfile Carrier { get; init; }
    public required decimal Price { get; init; }
    public string RouteText { get; init; } = "";
    public DateOnly? PaymentDueDate { get; init; }
    public TripStatus Status { get; init; } = TripStatus.Draft;
    /// <summary>Что надо проверить руками (не нашлось, не сошлось и т.п.).</summary>
    public List<string> Warnings { get; init; } = new();
}

public enum DocumentKind { Invoice, Act }

public record GeneratedDocument(DocumentKind Kind, string Number, DateOnly Date, string FileName, byte[] Content);
