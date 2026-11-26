using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using TruckDocs.Core.Abstractions;

namespace TruckDocs.Parsing;

/// <summary>
/// Текст из DOCX без внешних библиотек: DOCX — это zip с XML.
/// Таблицы превращаются в строки вида «ячейка | ячейка», это удобно для карт предприятия.
/// </summary>
public sealed class DocxTextExtractor : ITextExtractor
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public bool CanHandle(string fileName) => fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractAsync(Attachment file, CancellationToken ct = default)
    {
        using var zip = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("Это не DOCX: нет word/document.xml");
        using var stream = entry.Open();
        var body = XDocument.Load(stream).Root!.Element(W + "body")!;

        var sb = new StringBuilder();
        foreach (var el in body.Elements())
        {
            if (el.Name == W + "p") AppendLine(sb, ParagraphText(el));
            else if (el.Name == W + "tbl")
                foreach (var row in el.Descendants(W + "tr"))
                {
                    var cells = row.Elements(W + "tc")
                        .Select(tc => string.Join(" ", tc.Elements(W + "p").Select(ParagraphText).Where(t => t.Length > 0)))
                        .ToList();
                    AppendLine(sb, string.Join(" | ", cells));
                }
        }
        return Task.FromResult(sb.ToString());
    }

    private static string ParagraphText(XElement p)
    {
        var sb = new StringBuilder();
        foreach (var node in p.Descendants())
        {
            if (node.Name == W + "t") sb.Append(node.Value);
            else if (node.Name == W + "tab") sb.Append(' ');
            else if (node.Name == W + "br") sb.Append(' ');
        }
        return sb.ToString().Trim();
    }

    private static void AppendLine(StringBuilder sb, string line)
    {
        if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine(line);
    }
}

/// <summary>
/// Текст из PDF через poppler (`pdftotext -layout`). Режим layout сохраняет колонки,
/// на этом построен шаблонный парсер заявки.
/// Нужен установленный poppler-utils (apt install poppler-utils / choco install poppler).
/// Альтернатива без внешней программы — PdfPig, но тогда шаблонный парсер придётся подстроить под его вывод.
/// </summary>
public sealed class PdftotextExtractor(string executable = "pdftotext") : ITextExtractor
{
    public bool CanHandle(string fileName) => fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<string> ExtractAsync(Attachment file, CancellationToken ct = default)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"td_{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(tmp, file.Content, ct);
        try
        {
            var psi = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
            };
            foreach (var a in new[] { "-layout", "-enc", "UTF-8", tmp, "-" }) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить pdftotext");
            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = proc.StandardError.ReadToEndAsync(ct); // poppler пишет туда предупреждения о битых потоках — это не ошибка
            await proc.WaitForExitAsync(ct);
            var text = await stdout;
            await stderr;
            if (proc.ExitCode != 0 && string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException($"pdftotext завершился с кодом {proc.ExitCode}");
            return text;
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}

/// <summary>Выбирает экстрактор по расширению файла.</summary>
public sealed class CompositeTextExtractor(IEnumerable<ITextExtractor> extractors)
{
    private readonly List<ITextExtractor> _extractors = extractors.ToList();

    public static CompositeTextExtractor Default() => new(new ITextExtractor[] { new DocxTextExtractor(), new PdftotextExtractor() });

    public bool CanHandle(string fileName) => _extractors.Any(e => e.CanHandle(fileName));

    public Task<string> ExtractAsync(Attachment file, CancellationToken ct = default)
    {
        var ex = _extractors.FirstOrDefault(e => e.CanHandle(file.FileName))
                 ?? throw new NotSupportedException($"Не умею читать файл {file.FileName}");
        return ex.ExtractAsync(file, ct);
    }
}
