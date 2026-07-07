namespace TechSupportRagBot.Services;

public enum DocumentType
{
    ErrorTable,
    Manual,
    Instruction,
    Spreadsheet,
    ChatLog,
    QA,
    GeneralDocument
}

public sealed class ExtractedDocument
{
    public string FileName { get; set; } = string.Empty;

    public string FullText { get; set; } = string.Empty;

    public DocumentType DocumentType { get; set; } = DocumentType.GeneralDocument;

    public List<ExtractedPage> Pages { get; set; } = new();

    public List<ExtractedSheet> Sheets { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    public int PagesCount => Pages.Count;

    public int SheetsCount => Sheets.Count;
}

public sealed class ExtractedPage
{
    public int PageNumber { get; set; }

    public string Text { get; set; } = string.Empty;
}

public sealed class ExtractedSheet
{
    public string SheetName { get; set; } = string.Empty;

    public List<string> ColumnNames { get; set; } = new();

    public List<ExtractedSheetRow> Rows { get; set; } = new();
}

public sealed class ExtractedSheetRow
{
    public int RowNumber { get; set; }

    public List<string> Cells { get; set; } = new();

    public string Text => string.Join(" | ", Cells.Where(x => !string.IsNullOrWhiteSpace(x)));
}
