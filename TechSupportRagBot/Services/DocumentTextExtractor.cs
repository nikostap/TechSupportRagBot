using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using Paragraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace TechSupportRagBot.Services;

public class DocumentTextExtractor
{
    public async Task<string> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        return (await ExtractDocumentAsync(path, null, cancellationToken)).FullText;
    }

    public async Task<ExtractedDocument> ExtractDocumentAsync(
        string path,
        string? category,
        CancellationToken cancellationToken = default)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var result = new ExtractedDocument { FileName = fileName };

        switch (extension)
        {
            case ".txt":
                result.FullText = await File.ReadAllTextAsync(path, cancellationToken);
                break;
            case ".pdf":
                ExtractPdf(path, result);
                break;
            case ".docx":
                result.FullText = ExtractDocx(path);
                break;
            case ".xlsx":
                ExtractXlsx(path, result);
                break;
            case ".xls":
                result.Warnings.Add("XLS binary format is not supported yet. Save the file as XLSX for indexing.");
                break;
            default:
                throw new InvalidOperationException("Поддерживаются PDF, DOCX, TXT и XLSX. XLS требует конвертации в XLSX.");
        }

        result.DocumentType = new DocumentTypeDetector().Detect(fileName, category, result.FullText);
        if (extension == ".xlsx")
        {
            result.DocumentType = DocumentType.Spreadsheet;
        }

        return result;
    }

    private static void ExtractPdf(string path, ExtractedDocument result)
    {
        var builder = new StringBuilder();
        using var document = PdfDocument.Open(path);
        foreach (var page in document.GetPages())
        {
            var text = page.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                result.Warnings.Add($"PDF page {page.Number} has no extracted text. PDF may require OCR.");
                continue;
            }

            result.Pages.Add(new ExtractedPage
            {
                PageNumber = page.Number,
                Text = text
            });
            builder.AppendLine($"[Page {page.Number}]");
            builder.AppendLine(text);
            builder.AppendLine();
        }

        if (result.Pages.Count == 0)
        {
            result.Warnings.Add("PDF may require OCR.");
        }

        result.FullText = builder.ToString();
    }

    private static string ExtractDocx(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body == null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var element in body.Elements())
        {
            AppendElementText(element, builder);
        }

        return builder.ToString();
    }

    private static void AppendElementText(OpenXmlElement element, StringBuilder builder)
    {
        switch (element)
        {
            case Paragraph paragraph:
            {
                var text = paragraph.InnerText?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.AppendLine(text);
                    builder.AppendLine();
                }

                break;
            }
            case DocumentFormat.OpenXml.Wordprocessing.Table table:
            {
                foreach (var row in table.Elements<TableRow>())
                {
                    var cells = row.Elements<TableCell>()
                        .Select(cell => string.Join(" ", cell.Descendants<Text>().Select(x => x.Text)).Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();

                    if (cells.Count > 0)
                    {
                        builder.AppendLine(string.Join(" | ", cells));
                    }
                }

                builder.AppendLine();
                break;
            }
            default:
            {
                foreach (var child in element.Elements())
                {
                    AppendElementText(child, builder);
                }

                break;
            }
        }
    }

    private static void ExtractXlsx(string path, ExtractedDocument result)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart?.Workbook?.Sheets == null)
        {
            return;
        }

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var builder = new StringBuilder();

        foreach (var sheet in workbookPart.Workbook.Sheets.Elements<Sheet>())
        {
            if (sheet.Id?.Value == null)
            {
                continue;
            }

            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
            var sheetData = worksheetPart.Worksheet?.Elements<SheetData>().FirstOrDefault();
            if (sheetData == null)
            {
                continue;
            }

            var extractedSheet = new ExtractedSheet { SheetName = sheet.Name?.Value ?? "Sheet" };
            foreach (var row in sheetData.Elements<Row>())
            {
                var cells = row.Elements<Cell>()
                    .Select(cell => GetCellText(cell, sharedStrings))
                    .ToList();

                if (cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                var rowNumber = row.RowIndex?.Value is uint index ? (int)index : extractedSheet.Rows.Count + 1;
                if (extractedSheet.ColumnNames.Count == 0)
                {
                    extractedSheet.ColumnNames = cells;
                }

                extractedSheet.Rows.Add(new ExtractedSheetRow
                {
                    RowNumber = rowNumber,
                    Cells = cells
                });

                builder.AppendLine($"{extractedSheet.SheetName} #{rowNumber}: {string.Join(" | ", cells)}");
            }

            result.Sheets.Add(extractedSheet);
        }

        result.FullText = builder.ToString();
    }

    private static string GetCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.InnerText ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(value, out var sharedStringIndex)
            && sharedStrings != null)
        {
            return sharedStrings.ElementAt(sharedStringIndex).InnerText;
        }

        return value;
    }
}
