using TruckDocs.Core;
using TruckDocs.Core.Abstractions;
using TruckDocs.Core.Models;

namespace TruckDocs.Parsing;

/// <summary>
/// Обработка одной входящей заявки: файлы → текст → карта предприятия + заявка → рейс.
/// Не знает, откуда пришла заявка (почта, Saby, сайт) — получает IncomingOrder.
/// </summary>
public sealed class OrderPipeline(CompositeTextExtractor textExtractor, IReadOnlyList<IOrderExtractor> orderExtractors)
{
    public async Task<PipelineResult> ProcessAsync(IncomingOrder incoming, CarrierProfile carrier, CancellationToken ct = default)
    {
        Party? card = null;
        OrderData? order = null;
        var log = new List<string>();

        foreach (var file in incoming.Files.Where(f => textExtractor.CanHandle(f.FileName)))
        {
            var text = await textExtractor.ExtractAsync(file, ct);

            if (card is null && CompanyCardParser.LooksLikeCompanyCard(text))
            {
                card = CompanyCardParser.Parse(text);
                log.Add($"{file.FileName}: карта предприятия ({card.DisplayName}, ИНН {card.Inn})");
                continue;
            }

            if (order is null)
            {
                foreach (var extractor in orderExtractors)
                {
                    order = await extractor.TryExtractAsync(text, ct);
                    if (order is not null)
                    {
                        log.Add($"{file.FileName}: заявка №{order.OrderNumber}, разобрана через {extractor.Name}");
                        break;
                    }
                }
                if (order is null) log.Add($"{file.FileName}: не похоже ни на заявку, ни на карту предприятия");
            }
        }

        if (order is null)
            return new PipelineResult(null, card, log, "Во вложениях не найдена заявка, которую удалось разобрать.");

        return new PipelineResult(TripBuilder.Build(order, card, carrier), card, log, null);
    }
}

public record PipelineResult(Trip? Trip, Party? CustomerCard, IReadOnlyList<string> Log, string? Error);
