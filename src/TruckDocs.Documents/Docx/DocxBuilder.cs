using System.IO.Compression;
using System.Security;
using System.Text;

namespace TruckDocs.Documents.Docx;

public enum Align { Left, Center, Right, Both }

/// <summary>Кусок текста с форматированием.</summary>
public record Run(string Text, bool Bold = false, double? SizePt = null);

public record Para(IReadOnlyList<Run> Runs, Align Align = Align.Left, double SpaceAfterPt = 0, bool BottomBorder = false)
{
    public static Para Of(string text, bool bold = false, double? size = null, Align align = Align.Left, double after = 0) =>
        new(new[] { new Run(text, bold, size) }, align, after);
    public static Para Empty(double after = 0) => new(Array.Empty<Run>(), SpaceAfterPt: after);
}

public record Cell(IReadOnlyList<Para> Paras, int Span = 1, bool Shade = false, bool NoBorder = false)
{
    public static Cell Of(string text, bool bold = false, Align align = Align.Left, int span = 1, double? size = null) =>
        new(text.Split('\n').Select(l => Para.Of(l, bold, size, align)).ToList(), span);
}

public record Table(IReadOnlyList<int> ColumnWidthsMm, IReadOnlyList<IReadOnlyList<Cell>> Rows, bool Borders = true);

/// <summary>
/// Минимальный генератор DOCX без сторонних библиотек: собирает WordprocessingML руками.
/// DOCX выбран как первый формат потому, что его можно поправить в Word перед отправкой;
/// PDF получается из него конвертацией (LibreOffice / Word) или позже — через QuestPDF.
/// </summary>
public sealed class DocxBuilder
{
    private readonly StringBuilder _body = new();
    private readonly string _font;
    private readonly double _fontSizePt;

    public DocxBuilder(string font = "Arial", double fontSizePt = 10)
    {
        _font = font;
        _fontSizePt = fontSizePt;
    }

    public DocxBuilder Add(Para p) { _body.Append(ParaXml(p)); return this; }
    public DocxBuilder Add(string text, bool bold = false, double? size = null, Align align = Align.Left, double after = 0) =>
        Add(Para.Of(text, bold, size, align, after));
    public DocxBuilder Add(Table t) { _body.Append(TableXml(t)); return this; }

    public byte[] Build()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
                </Types>
                """);
            Write(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Write(zip, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            var half = (int)(_fontSizePt * 2);
            Write(zip, "word/styles.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:docDefaults>
                  <w:rPrDefault><w:rPr><w:rFonts w:ascii="{_font}" w:hAnsi="{_font}" w:cs="{_font}" w:eastAsia="{_font}"/><w:sz w:val="{half}"/><w:szCs w:val="{half}"/><w:lang w:val="ru-RU"/></w:rPr></w:rPrDefault>
                  <w:pPrDefault><w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/></w:pPr></w:pPrDefault>
                </w:docDefaults>
                </w:styles>
                """);
            // Поля страницы: 15 мм слева/справа, 15 мм сверху/снизу (1 мм = 56.7 twips)
            Write(zip, "word/document.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>{_body}<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="850" w:right="850" w:bottom="850" w:left="850" w:header="0" w:footer="0" w:gutter="0"/></w:sectPr></w:body>
                </w:document>
                """);
        }
        return ms.ToArray();
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content.TrimStart());
    }

    private static int Twips(double mm) => (int)Math.Round(mm * 56.7);

    private static string ParaXml(Para p)
    {
        var jc = p.Align switch { Align.Center => "center", Align.Right => "right", Align.Both => "both", _ => "left" };
        var border = p.BottomBorder ? """<w:pBdr><w:bottom w:val="single" w:sz="12" w:space="1" w:color="000000"/></w:pBdr>""" : "";
        var sb = new StringBuilder($"""<w:p><w:pPr>{border}<w:spacing w:after="{(int)(p.SpaceAfterPt * 20)}"/><w:jc w:val="{jc}"/></w:pPr>""");
        foreach (var r in p.Runs)
        {
            var rpr = new StringBuilder();
            if (r.Bold) rpr.Append("<w:b/><w:bCs/>");
            if (r.SizePt is { } s) rpr.Append($"""<w:sz w:val="{(int)(s * 2)}"/><w:szCs w:val="{(int)(s * 2)}"/>""");
            sb.Append($"""<w:r><w:rPr>{rpr}</w:rPr><w:t xml:space="preserve">{SecurityElement.Escape(r.Text)}</w:t></w:r>""");
        }
        return sb.Append("</w:p>").ToString();
    }

    private static string TableXml(Table t)
    {
        var b = t.Borders ? "single" : "nil";
        var sb = new StringBuilder();
        sb.Append($"""
            <w:tbl><w:tblPr><w:tblW w:w="{Twips(t.ColumnWidthsMm.Sum())}" w:type="dxa"/><w:tblLayout w:type="fixed"/>
            <w:tblBorders><w:top w:val="{b}" w:sz="4"/><w:left w:val="{b}" w:sz="4"/><w:bottom w:val="{b}" w:sz="4"/><w:right w:val="{b}" w:sz="4"/><w:insideH w:val="{b}" w:sz="4"/><w:insideV w:val="{b}" w:sz="4"/></w:tblBorders>
            <w:tblCellMar><w:top w:w="28" w:type="dxa"/><w:left w:w="85" w:type="dxa"/><w:bottom w:w="28" w:type="dxa"/><w:right w:w="85" w:type="dxa"/></w:tblCellMar>
            </w:tblPr><w:tblGrid>
            """);
        foreach (var w in t.ColumnWidthsMm) sb.Append($"""<w:gridCol w:w="{Twips(w)}"/>""");
        sb.Append("</w:tblGrid>");

        foreach (var row in t.Rows)
        {
            sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");
            var col = 0;
            foreach (var cell in row)
            {
                var width = t.ColumnWidthsMm.Skip(col).Take(cell.Span).Sum();
                col += cell.Span;
                sb.Append($"""<w:tc><w:tcPr><w:tcW w:w="{Twips(width)}" w:type="dxa"/>""");
                if (cell.Span > 1) sb.Append($"""<w:gridSpan w:val="{cell.Span}"/>""");
                if (cell.NoBorder) sb.Append("""<w:tcBorders><w:top w:val="nil"/><w:left w:val="nil"/><w:bottom w:val="nil"/><w:right w:val="nil"/></w:tcBorders>""");
                if (cell.Shade) sb.Append("""<w:shd w:val="clear" w:color="auto" w:fill="EDEDED"/>""");
                sb.Append("</w:tcPr>");
                var paras = cell.Paras.Count > 0 ? cell.Paras : new[] { Para.Empty() };
                foreach (var p in paras) sb.Append(ParaXml(p));
                sb.Append("</w:tc>");
            }
            sb.Append("</w:tr>");
        }
        return sb.Append("</w:tbl>").ToString();
    }
}
