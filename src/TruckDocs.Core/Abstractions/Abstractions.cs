using TruckDocs.Core.Models;

namespace TruckDocs.Core.Abstractions;

/// <summary>Файл, пришедший вместе с заявкой.</summary>
public record Attachment(string FileName, string ContentType, byte[] Content);

/// <summary>Заявка в едином формате, независимо от канала (почта, Saby, загрузка на сайте).</summary>
public record IncomingOrder(string Source, string? Subject, string? BodyText, IReadOnlyList<Attachment> Files, DateTimeOffset ReceivedAt);

/// <summary>Источник заявок: почта, Saby и т.д.</summary>
public interface IOrderSource
{
    string Name { get; }
    Task<IReadOnlyList<IncomingOrder>> FetchNewAsync(CancellationToken ct = default);
}

/// <summary>Достаёт текст из файла (PDF, DOCX…).</summary>
public interface ITextExtractor
{
    bool CanHandle(string fileName);
    Task<string> ExtractAsync(Attachment file, CancellationToken ct = default);
}

/// <summary>Раскладывает текст заявки по полям.</summary>
public interface IOrderExtractor
{
    string Name { get; }
    Task<OrderData?> TryExtractAsync(string text, CancellationToken ct = default);
}

/// <summary>Генерирует документ (счёт, акт) по рейсу.</summary>
public interface IDocumentGenerator
{
    DocumentKind Kind { get; }
    GeneratedDocument Generate(Trip trip, string number, DateOnly date);
}
