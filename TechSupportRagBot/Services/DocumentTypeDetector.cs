using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public class DocumentTypeDetector
{
    private static readonly Regex ErrorRowRegex = new(
        @"\b(ошибк|авар|alarm|error|не\s+\w+|нет\s+\w+|датчик\s*[A-ZА-Я]?\d+|E[- ]?\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DocumentType Detect(string fileName, string? category, string text)
    {
        var documentNameAndCategory = $"{fileName}\n{category}".ToLowerInvariant();
        var haystack = $"{fileName}\n{category}\n{text}".ToLowerInvariant();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (LooksLikeChatLog(haystack))
        {
            return DocumentType.ChatLog;
        }

        var explicitlyErrorTable = documentNameAndCategory.Contains("ошиб")
            || documentNameAndCategory.Contains("авар")
            || documentNameAndCategory.Contains("error")
            || documentNameAndCategory.Contains("alarm");

        if (explicitlyErrorTable)
        {
            return DocumentType.ErrorTable;
        }

        if (extension is ".xlsx" or ".xls")
        {
            return DocumentType.Spreadsheet;
        }

        var explicitlyInstruction = documentNameAndCategory.Contains("инструкц")
            || documentNameAndCategory.Contains("настрой")
            || documentNameAndCategory.Contains("регулиров")
            || documentNameAndCategory.Contains("instruction")
            || documentNameAndCategory.Contains("setup");

        var explicitlyManual = documentNameAndCategory.Contains("руководство")
            || documentNameAndCategory.Contains("manual")
            || documentNameAndCategory.Contains("эксплуатац");

        // Если администратор явно выбрал категорию "Инструкция", не перекрашиваем весь документ
        // в таблицу ошибок только из-за наличия раздела с авариями внутри инструкции.
        if (explicitlyInstruction)
        {
            return DocumentType.Instruction;
        }

        if (explicitlyManual)
        {
            return DocumentType.Manual;
        }

        if (haystack.Contains("инструкция") || haystack.Contains("instruction"))
        {
            return DocumentType.Instruction;
        }

        if (haystack.Contains("руководство") || haystack.Contains("manual") || haystack.Contains("эксплуатац"))
        {
            return DocumentType.Manual;
        }

        return DocumentType.GeneralDocument;
    }

    public bool LooksLikeErrorTable(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return false;
        }

        var tableRows = lines.Count(line => line.Contains('|') && line.Split('|').Length >= 3);
        var errorRows = lines.Count(line => ErrorRowRegex.IsMatch(line));
        return tableRows >= 3 && errorRows >= 2 || errorRows >= 5;
    }

    private static bool LooksLikeChatLog(string text)
    {
        return text.Contains("клиент:")
            || text.Contains("оператор:")
            || text.Contains("менеджер:")
            || text.Contains("technician:")
            || text.Contains("operator:")
            || text.Contains("support:");
    }
}
