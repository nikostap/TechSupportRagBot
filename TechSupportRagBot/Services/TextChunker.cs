using System.Text;
using System.Text.RegularExpressions;

namespace TechSupportRagBot.Services;

public static class TextChunker
{
    private static readonly Regex ErrorCodeRegex = new(
        @"\b(?:E[- ]?\d{1,5}|ERR[- ]?\d{1,5}|ALARM[- ]?\d{1,5}|A[- ]?\d{1,5}|Ошибка\s*\d{1,5})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> Split(string text, int maxLength = 6500, int overlap = 800)
    {
        return SplitDetailed(new ExtractedDocument
        {
            FullText = text,
            DocumentType = DocumentType.GeneralDocument
        }, null)
            .Select(x => x.Text)
            .ToList();
    }

    public static IReadOnlyList<TextChunkInfo> SplitDetailed(ExtractedDocument document, string? machineModel)
    {
        return document.DocumentType switch
        {
            DocumentType.ErrorTable => SplitErrorTable(document.FullText, document.FileName, document.DocumentType, machineModel, null, null, true),
            DocumentType.Spreadsheet => SplitSpreadsheet(document, machineModel),
            DocumentType.ChatLog => SplitChatLog(document.FullText, document.FileName),
            DocumentType.Manual or DocumentType.Instruction or DocumentType.GeneralDocument => SplitStructuredText(document, machineModel),
            _ => SplitStructuredText(document, machineModel)
        };
    }

    public static IReadOnlyList<TextChunkInfo> SplitDetailed(string text, string? fileName)
    {
        var extracted = new ExtractedDocument
        {
            FileName = fileName ?? string.Empty,
            FullText = text,
            DocumentType = new DocumentTypeDetector().Detect(fileName ?? string.Empty, null, text)
        };

        return SplitDetailed(extracted, null);
    }

    private static List<TextChunkInfo> SplitSpreadsheet(ExtractedDocument document, string? machineModel)
    {
        var detector = new DocumentTypeDetector();
        var chunks = new List<TextChunkInfo>();

        foreach (var sheet in document.Sheets)
        {
            var sheetText = string.Join("\n", sheet.Rows.Select(x => x.Text));
            if (detector.LooksLikeErrorTable(sheetText))
            {
                chunks.AddRange(SplitErrorTable(sheetText, document.FileName, DocumentType.ErrorTable, machineModel, sheet.SheetName, null, true));
                continue;
            }

            foreach (var row in sheet.Rows.Skip(sheet.ColumnNames.Count > 0 ? 1 : 0))
            {
                if (string.IsNullOrWhiteSpace(row.Text))
                {
                    continue;
                }

                chunks.Add(new TextChunkInfo
                {
                    Text = row.Text,
                    NormalizedText = NormalizeForSearch(row.Text),
                    Title = $"{sheet.SheetName} row {row.RowNumber}",
                    FileName = document.FileName,
                    DocumentType = DocumentType.Spreadsheet.ToString(),
                    SheetName = sheet.SheetName,
                    RowNumber = row.RowNumber,
                    ColumnNames = string.Join(" | ", sheet.ColumnNames),
                    MachineModel = machineModel
                });
            }
        }

        return chunks;
    }

    private static List<TextChunkInfo> SplitErrorTable(
        string text,
        string? fileName,
        DocumentType documentType,
        string? machineModel,
        string? sheetName,
        int? page,
        bool wholeDocumentIsErrorTable)
    {
        var lines = NormalizeText(text)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();

        var chunks = new List<TextChunkInfo>();
        string? sectionTitle = null;
        foreach (var line in lines)
        {
            if (LooksLikeSectionTitle(line))
            {
                sectionTitle = line;
                continue;
            }

            if (!LooksLikeErrorRow(line, sectionTitle, wholeDocumentIsErrorTable))
            {
                continue;
            }

            var cells = line.Contains('|')
                ? line.Split('|', StringSplitOptions.TrimEntries).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                : new[] { line };

            var errorName = cells.ElementAtOrDefault(0);
            var cause = cells.ElementAtOrDefault(1);
            var solution = cells.ElementAtOrDefault(2);
            var fullText = cells.Length >= 3
                ? $"Ошибка: {errorName}\nПричина: {cause}\nРешение: {solution}"
                : line;

            chunks.Add(new TextChunkInfo
            {
                Text = fullText,
                NormalizedText = NormalizeForSearch(fullText),
                Title = errorName,
                FileName = fileName,
                Page = page,
                SectionTitle = sectionTitle,
                ErrorName = errorName,
                ErrorCode = ErrorCodeRegex.Match(line) is { Success: true } match ? match.Value : null,
                Cause = cause,
                Solution = solution,
                MachineModel = machineModel,
                NodeName = InferNodeName(line),
                DocumentType = documentType.ToString(),
                SheetName = sheetName
            });
        }

        return chunks;
    }

    private static List<TextChunkInfo> SplitStructuredText(ExtractedDocument document, string? machineModel)
    {
        var sourceBlocks = document.Pages.Count > 0
            ? document.Pages.Select(page => (Text: page.Text, Page: (int?)page.PageNumber))
            : new[] { (Text: document.FullText, Page: (int?)null) };

        var result = new List<TextChunkInfo>();
        foreach (var block in sourceBlocks)
        {
            if (document.DocumentType == DocumentType.ErrorTable)
            {
                var errorChunks = SplitErrorTable(
                    block.Text,
                    document.FileName,
                    DocumentType.ErrorTable,
                    machineModel,
                    null,
                    block.Page,
                    true);
                result.AddRange(errorChunks);
                continue;
            }

            result.AddRange(SplitRegularBlock(block.Text, document.FileName, document.DocumentType, machineModel, block.Page));
        }

        return result;
    }

    private static List<TextChunkInfo> SplitRegularBlock(
        string text,
        string? fileName,
        DocumentType documentType,
        string? machineModel,
        int? page)
    {
        const int maxLength = 7200;
        const int overlap = 900;
        var paragraphs = NormalizeText(text)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        var chunks = new List<TextChunkInfo>();
        var current = new StringBuilder();
        string? sectionTitle = null;
        string? subsectionTitle = null;

        foreach (var paragraph in paragraphs)
        {
            if (LooksLikeSectionTitle(paragraph))
            {
                if (current.Length > 0)
                {
                    AddRegularChunk(chunks, current.ToString(), fileName, documentType, machineModel, page, sectionTitle, subsectionTitle);
                    current.Clear();
                }

                sectionTitle = paragraph.Trim();
                subsectionTitle = null;
            }
            else if (LooksLikeSubsectionTitle(paragraph))
            {
                if (current.Length > 0)
                {
                    AddRegularChunk(chunks, current.ToString(), fileName, documentType, machineModel, page, sectionTitle, subsectionTitle);
                    current.Clear();
                }

                subsectionTitle = paragraph.Trim();
            }

            if (current.Length + paragraph.Length + 2 > maxLength && current.Length > 0)
            {
                AddRegularChunk(chunks, current.ToString(), fileName, documentType, machineModel, page, sectionTitle, subsectionTitle);
                var previous = current.ToString();
                current.Clear();
                current.Append(previous.Length > overlap ? previous[^overlap..] : previous);
                current.AppendLine();
            }

            current.AppendLine(paragraph);
            current.AppendLine();
        }

        if (current.Length > 0)
        {
            AddRegularChunk(chunks, current.ToString(), fileName, documentType, machineModel, page, sectionTitle, subsectionTitle);
        }

        return chunks;
    }

    private static List<TextChunkInfo> SplitChatLog(string text, string? fileName)
    {
        var cleanedLines = NormalizeText(text)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !IsChatNoise(line))
            .ToList();

        var chunks = new List<TextChunkInfo>();
        var current = new StringBuilder();
        var participants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in cleanedLines)
        {
            var speakerEnd = line.IndexOf(':');
            if (speakerEnd is > 0 and < 40)
            {
                participants.Add(line[..speakerEnd].Trim());
            }

            if (current.Length + line.Length > 3500 && current.Length > 0)
            {
                AddChatChunk(chunks, current.ToString(), fileName, participants);
                current.Clear();
                participants.Clear();
            }

            current.AppendLine(line);
        }

        if (current.Length > 0)
        {
            AddChatChunk(chunks, current.ToString(), fileName, participants);
        }

        return chunks;
    }

    private static void AddRegularChunk(
        List<TextChunkInfo> chunks,
        string text,
        string? fileName,
        DocumentType documentType,
        string? machineModel,
        int? page,
        string? sectionTitle,
        string? subsectionTitle)
    {
        var normalized = text.Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        if (IsHeadingOnlyChunk(normalized))
        {
            return;
        }

        chunks.Add(new TextChunkInfo
        {
            Text = normalized,
            NormalizedText = NormalizeForSearch(normalized),
            Title = subsectionTitle ?? sectionTitle,
            FileName = fileName,
            Page = page,
            SectionTitle = sectionTitle,
            SubsectionTitle = subsectionTitle,
            MachineModel = machineModel,
            DocumentType = documentType.ToString(),
            ErrorCode = ErrorCodeRegex.Match(normalized) is { Success: true } match ? match.Value : null,
            NodeName = InferNodeName(normalized)
        });
    }

    private static void AddChatChunk(List<TextChunkInfo> chunks, string text, string? fileName, HashSet<string> participants)
    {
        var normalized = text.Trim();
        chunks.Add(new TextChunkInfo
        {
            Text = normalized,
            NormalizedText = NormalizeForSearch(normalized),
            Title = "Фрагмент переписки",
            FileName = fileName,
            DocumentType = DocumentType.ChatLog.ToString(),
            Participants = string.Join(", ", participants),
            Topic = InferTopic(normalized),
            SourceChat = fileName
        });
    }

    private static bool LooksLikeErrorRow(string line, string? sectionTitle, bool wholeDocumentIsErrorTable)
    {
        var lower = line.ToLowerInvariant();
        var section = sectionTitle?.ToLowerInvariant() ?? string.Empty;

        if (line.Contains('|') && line.Split('|').Length >= 3)
        {
            if (wholeDocumentIsErrorTable)
            {
                return true;
            }

            // Не любая таблица в инструкции является таблицей ошибок.
            // Например, "Положение ножа | влияние на хвост" или "Параметр ножа | тип материала"
            // должны оставаться Instruction, а не превращаться в ErrorTable.
            return section.Contains("таблица ошибок")
                || section.Contains("диагностик")
                || section.Contains("неисправ")
                || section.Contains("авар")
                || lower.Contains("симптом")
                || lower.Contains("ошиб")
                || lower.Contains("возможная причина")
                || lower.Contains("способ устран")
                || lower.Contains("код ошибки")
                || lower.Contains("alarm")
                || lower.Contains("fault");
        }

        return wholeDocumentIsErrorTable
            || ErrorCodeRegex.IsMatch(line)
            || lower.Contains("ошиб")
            || lower.Contains("авар")
            || lower.Contains("alarm")
            || lower.Contains("error");
    }

    private static bool LooksLikeSectionTitle(string text)
    {
        var line = text.Trim();
        if (line.Length is < 4 or > 180)
        {
            return false;
        }

        var lower = line.ToLowerInvariant();
        return lower.Contains("таблица ошибок")
            || lower.Contains("аварий")
            || lower.Contains("приложение")
            || Regex.IsMatch(line, @"^\d+(\.\d+)*\s+");
    }

    private static bool LooksLikeSubsectionTitle(string text)
    {
        var line = text.Trim();
        return line.Length is >= 4 and <= 120
            && !line.EndsWith('.')
            && line.Count(char.IsLetter) > 3;
    }

    private static bool IsHeadingOnlyChunk(string text)
    {
        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return lines.Count == 1
            && lines[0].Length < 90
            && (LooksLikeSectionTitle(lines[0]) || LooksLikeSubsectionTitle(lines[0]));
    }

    private static bool IsChatNoise(string line)
    {
        var lower = line.Trim().ToLowerInvariant();
        return lower is "привет" or "здравствуйте" or "спасибо" or "ок" or "окей" or "добрый день"
            || lower.StartsWith("> ")
            || lower.StartsWith("-----original message");
    }

    private static string? InferNodeName(string text)
    {
        var lower = text.ToLowerInvariant();
        string[] nodes =
        {
            "ремешковый узел",
            "узел ремешков",
            "ножевой узел",
            "отрезной нож",
            "пневмосистема",
            "датчик",
            "клапан",
            "револьверный узел",
            "намоточный узел"
        };

        return nodes.FirstOrDefault(lower.Contains);
    }

    private static string? InferTopic(string text)
    {
        var firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return firstLine != null && firstLine.Length <= 120 ? firstLine : null;
    }

    private static string NormalizeText(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    private static string NormalizeForSearch(string text)
    {
        return Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim();
    }
}

public class TextChunkInfo
{
    public string Text { get; set; } = string.Empty;

    public string? NormalizedText { get; set; }

    public string? Title { get; set; }

    public string? FileName { get; set; }

    public int? Page { get; set; }

    public string? SectionTitle { get; set; }

    public string? SubsectionTitle { get; set; }

    public string? ErrorName { get; set; }

    public string? ErrorCode { get; set; }

    public string? Cause { get; set; }

    public string? Solution { get; set; }

    public string? MachineModel { get; set; }

    public string? NodeName { get; set; }

    public string? DocumentType { get; set; }

    public string? SheetName { get; set; }

    public int? RowNumber { get; set; }

    public string? ColumnNames { get; set; }

    public string? ChatDate { get; set; }

    public string? Participants { get; set; }

    public string? Topic { get; set; }

    public string? SourceChat { get; set; }
}
