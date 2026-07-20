using System.Security.Claims;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class QAModel : PageModel
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50, 100];
    private const int DefaultPageSize = 10;
    private const string PageSizeCookiePrefix = "qa-page-size";

    private readonly ApplicationDbContext _db;
    private readonly QAService _qa;

    public QAModel(ApplicationDbContext db, QAService qa)
    {
        _db = db;
        _qa = qa;
    }

    [BindProperty]
    public QAInput Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> MediaFiles { get; set; } = new();

    [BindProperty]
    public List<IFormFile> EditMediaFiles { get; set; } = new();

    [BindProperty]
    public QAInput EditInput { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }

    [BindProperty]
    public QAImportInput Import { get; set; } = new();

    [BindProperty]
    public List<QAInput> ConfirmEntries { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? MachineFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SerialFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchQuery { get; set; }

    [BindProperty(SupportsGet = true)]
    public string SearchMode { get; set; } = "Keyword";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int? PageSize { get; set; }

    [BindProperty(SupportsGet = true)]
    public string SortOrder { get; set; } = "updated-desc";

    [BindProperty]
    public List<int> SelectedIds { get; set; } = new();

    public int[] PageSizeOptions => AllowedPageSizes;

    public List<QAEntry> Entries { get; private set; } = new();

    public List<KnowledgeCategory> Categories { get; private set; } = new();

    public List<Machine> Machines { get; private set; } = new();

    public List<string> MachineFilterOptions { get; private set; } = new();

    public List<string> SerialFilterOptions { get; private set; } = new();

    public List<QAInput> PreviewEntries { get; private set; } = new();

    public List<QAAttachment> EditAttachments { get; private set; } = new();

    public string MachineHintsJson { get; private set; } = "[]";

    public int TotalPages { get; private set; }

    public string[] Statuses { get; } =
    [
        QAEntryStatuses.Draft,
        QAEntryStatuses.Verified,
        QAEntryStatuses.NeedsReview,
        QAEntryStatuses.Deprecated
    ];

    public async Task OnGetAsync()
    {
        await LoadAsync();
        if (EditId.HasValue)
        {
            var entry = await _db.QAEntries
                .AsNoTracking()
                .Include(x => x.Attachments)
                .FirstOrDefaultAsync(x => x.Id == EditId.Value);
            if (entry != null)
            {
                EditInput = FromEntry(entry);
                EditAttachments = entry.Attachments.OrderBy(x => x.Id).ToList();
            }
        }
    }

    public IActionResult OnGetTemplateTxt()
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var content = encoding.GetPreamble()
            .Concat(encoding.GetBytes(QAService.BuildTxtTemplate()))
            .ToArray();
        return File(content, "text/plain; charset=utf-8", "qa-template.txt");
    }

    public IActionResult OnGetTemplateDocx()
    {
        return File(QAService.BuildDocxTemplate(), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "qa-template.docx");
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Input.Question) || string.IsNullOrWhiteSpace(Input.Answer))
        {
            ModelState.AddModelError(string.Empty, "Укажите вопрос и ответ.");
            await LoadAsync();
            return Page();
        }

        var entry = await _qa.CreateAsync(ToEntry(Input, QAEntrySources.Manual), cancellationToken);
        var savedAttachments = await _qa.AddAttachmentsAsync(entry.Id, MediaFiles, cancellationToken);
        if (MediaFiles.Any(x => x.Length > 0) && savedAttachments == 0)
        {
            ModelState.AddModelError(string.Empty, "Медиафайлы не сохранены. Проверьте формат: JPG, PNG, GIF, WEBP, MP4, MOV, WEBM или MKV.");
            await LoadAsync();
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPreviewMetadataAsync(CancellationToken cancellationToken)
    {
        var hadSelectedFiles = MediaFiles.Any(x => x.Length > 0);
        if (string.IsNullOrWhiteSpace(Input.Question) || string.IsNullOrWhiteSpace(Input.Answer))
        {
            ModelState.AddModelError(string.Empty, "Для генерации метаданных укажите вопрос и ответ.");
            await LoadAsync();
            return Page();
        }

        var preview = await _qa.PreviewMetadataAsync(ToEntry(Input, QAEntrySources.Manual), cancellationToken);
        Input = FromEntry(preview);
        ModelState.Clear();
        if (hadSelectedFiles)
        {
            ModelState.AddModelError(string.Empty, "После заполнения метаданных через LLM выберите медиафайлы заново перед сохранением.");
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewEditMetadataAsync(int id, CancellationToken cancellationToken)
    {
        EditId = id;
        var hadSelectedFiles = EditMediaFiles.Any(x => x.Length > 0);
        if (string.IsNullOrWhiteSpace(EditInput.Question) || string.IsNullOrWhiteSpace(EditInput.Answer))
        {
            ModelState.AddModelError(string.Empty, "Для генерации метаданных укажите вопрос и ответ.");
            await LoadAsync();
            return Page();
        }

        var preview = await _qa.PreviewMetadataAsync(ToEntry(EditInput, QAEntrySources.Manual), cancellationToken);
        EditInput = FromEntry(preview);
        ModelState.Clear();
        if (hadSelectedFiles)
        {
            ModelState.AddModelError(string.Empty, "После заполнения метаданных через LLM выберите медиафайлы заново перед сохранением.");
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(EditInput.Question) || string.IsNullOrWhiteSpace(EditInput.Answer))
        {
            EditId = id;
            ModelState.AddModelError(string.Empty, "Укажите вопрос и ответ.");
            await LoadAsync();
            return Page();
        }

        await _qa.UpdateAsync(id, ToEntry(EditInput, string.IsNullOrWhiteSpace(EditInput.Source) ? QAEntrySources.Manual : EditInput.Source), cancellationToken);
        var savedAttachments = await _qa.AddAttachmentsAsync(id, EditMediaFiles, cancellationToken);
        if (EditMediaFiles.Any(x => x.Length > 0) && savedAttachments == 0)
        {
            EditId = id;
            ModelState.AddModelError(string.Empty, "Медиафайлы не сохранены. Проверьте формат: JPG, PNG, GIF, WEBP, MP4, MOV, WEBM или MKV.");
            await LoadAsync();
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPreviewImportAsync(CancellationToken cancellationToken)
    {
        if (Import.File == null)
        {
            ModelState.AddModelError(string.Empty, "Выберите DOCX, TXT или MD файл.");
            await LoadAsync();
            return Page();
        }

        var extension = Path.GetExtension(Import.File.FileName).ToLowerInvariant();
        if (extension is not ".docx" and not ".txt" and not ".md")
        {
            ModelState.AddModelError(string.Empty, "QA импорт поддерживает DOCX, TXT и MD.");
            await LoadAsync();
            return Page();
        }

        await using var stream = Import.File.OpenReadStream();
        IReadOnlyList<QAEntry> parsed;
        try
        {
            parsed = await _qa.PreviewImportAsync(Import.File.FileName, stream, Import.AutoParse, cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            ModelState.AddModelError(string.Empty, "TXT и MD файлы должны быть сохранены в кодировке UTF-8.");
            await LoadAsync();
            return Page();
        }
        PreviewEntries = parsed.Select(FromEntry).ToList();
        if (PreviewEntries.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "В документе не найдены пары вопрос-ответ.");
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmImportAsync(CancellationToken cancellationToken)
    {
        var selected = ConfirmEntries
            .Where(x => x.ImportSelected && !string.IsNullOrWhiteSpace(x.Question) && !string.IsNullOrWhiteSpace(x.Answer))
            .Select(x => ToEntry(x, x.Source == QAEntrySources.Generated ? QAEntrySources.Generated : QAEntrySources.Import))
            .ToList();

        if (selected.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Выберите хотя бы одну QA-запись для импорта.");
            await LoadAsync();
            return Page();
        }

        await _qa.ImportEntriesAsync(selected, User.FindFirstValue(ClaimTypes.NameIdentifier), QAEntrySources.Import, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostVerifyAsync(int id, CancellationToken cancellationToken)
    {
        await _qa.VerifyAsync(id, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReindexAsync(int id, CancellationToken cancellationToken)
    {
        await _qa.ReindexAsync(id, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGenerateMetadataAsync(int id, CancellationToken cancellationToken)
    {
        await _qa.GenerateMetadataAsync(id, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        await _qa.DeleteAsync(id, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostBulkAsync(string bulkAction, CancellationToken cancellationToken)
    {
        var ids = SelectedIds.Distinct().ToArray();
        if (bulkAction == "verify")
        {
            foreach (var id in ids)
            {
                await _qa.VerifyAsync(id, cancellationToken);
            }
        }
        else if (bulkAction == "delete")
        {
            foreach (var id in ids)
            {
                await _qa.DeleteAsync(id, cancellationToken);
            }
        }

        return RedirectToPage(new { PageNumber, PageSize, StatusFilter, MachineFilter, SerialFilter, SearchQuery, SearchMode, SortOrder });
    }

    public async Task<IActionResult> OnPostDeleteAttachmentAsync(int id, int attachmentId, CancellationToken cancellationToken)
    {
        await _qa.DeleteAttachmentAsync(attachmentId, cancellationToken);
        return RedirectToPage(new { EditId = id });
    }

    private async Task LoadAsync()
    {
        var pageSize = ResolvePageSize();
        PageNumber = Math.Max(1, PageNumber);

        var query = _db.QAEntries
            .AsNoTracking()
            .Include(x => x.Attachments)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(StatusFilter))
        {
            query = query.Where(x => x.Status == StatusFilter);
        }

        if (!string.IsNullOrWhiteSpace(MachineFilter))
        {
            query = query.Where(x => x.MachineModel == MachineFilter);
        }

        if (!string.IsNullOrWhiteSpace(SerialFilter))
        {
            query = query.Where(x => x.SerialNumber == SerialFilter);
        }

        IReadOnlyList<int>? semanticIds = null;
        var search = SearchQuery?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            if (string.Equals(SearchMode, "Rag", StringComparison.OrdinalIgnoreCase))
            {
                semanticIds = await _qa.SearchSimilarIdsAsync(search, MachineFilter, SerialFilter, 150, HttpContext.RequestAborted);
                query = query.Where(x => semanticIds.Contains(x.Id));
            }
            else if (_db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                var pattern = $"%{search}%";
                query = query.Where(x =>
                    EF.Functions.ILike(x.Question, pattern)
                    || EF.Functions.ILike(x.Answer, pattern)
                    || (x.AlternativeQuestions != null && EF.Functions.ILike(x.AlternativeQuestions, pattern))
                    || (x.Keywords != null && EF.Functions.ILike(x.Keywords, pattern))
                    || (x.NodeName != null && EF.Functions.ILike(x.NodeName, pattern)));
            }
            else
            {
                var lower = search.ToLowerInvariant();
                query = query.Where(x =>
                    x.Question.ToLower().Contains(lower)
                    || x.Answer.ToLower().Contains(lower)
                    || (x.AlternativeQuestions != null && x.AlternativeQuestions.ToLower().Contains(lower))
                    || (x.Keywords != null && x.Keywords.ToLower().Contains(lower))
                    || (x.NodeName != null && x.NodeName.ToLower().Contains(lower)));
            }
        }

        var totalCount = await query.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        PageNumber = Math.Min(PageNumber, TotalPages);

        if (semanticIds != null)
        {
            var rank = semanticIds.Select((id, index) => new { id, index }).ToDictionary(x => x.id, x => x.index);
            var semanticEntries = await query.ToListAsync();
            Entries = semanticEntries
                .OrderBy(x => rank.GetValueOrDefault(x.Id, int.MaxValue))
                .Skip((PageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }
        else
        {
            var ordered = SortOrder switch
            {
                "updated-asc" => query.OrderBy(x => x.UpdatedAt),
                "question-asc" => query.OrderBy(x => x.Question),
                "question-desc" => query.OrderByDescending(x => x.Question),
                "status-asc" => query.OrderBy(x => x.Status).ThenByDescending(x => x.UpdatedAt),
                "model-asc" => query.OrderBy(x => x.MachineModel).ThenByDescending(x => x.UpdatedAt),
                _ => query.OrderByDescending(x => x.UpdatedAt)
            };
            Entries = await ordered
                .Skip((PageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        Categories = await _db.KnowledgeCategories.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        Machines = await _db.Machines.AsNoTracking().OrderBy(x => x.Model).ThenBy(x => x.Name).ToListAsync();
        MachineFilterOptions = await _db.QAEntries.AsNoTracking()
            .Where(x => x.MachineModel != null && x.MachineModel != "")
            .Select(x => x.MachineModel!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();
        SerialFilterOptions = await _db.QAEntries.AsNoTracking()
            .Where(x => x.SerialNumber != null && x.SerialNumber != "")
            .Select(x => x.SerialNumber!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();
        if (EditId.HasValue && EditAttachments.Count == 0)
        {
            EditAttachments = await _db.QAAttachments
                .AsNoTracking()
                .Where(x => x.QAEntryId == EditId.Value)
                .OrderBy(x => x.Id)
                .ToListAsync();
        }

        MachineHintsJson = JsonSerializer.Serialize(Machines.Select(x => new
        {
            x.Id,
            x.Name,
            x.Model,
            x.SerialNumber
        }), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private int ResolvePageSize()
    {
        var requested = PageSize;
        var cookieName = GetPageSizeCookieName();
        if (!requested.HasValue &&
            Request.Cookies.TryGetValue(cookieName, out var cookieValue) &&
            int.TryParse(cookieValue, out var cookiePageSize))
        {
            requested = cookiePageSize;
        }

        var pageSize = AllowedPageSizes.Contains(requested.GetValueOrDefault())
            ? requested!.Value
            : DefaultPageSize;

        PageSize = pageSize;
        Response.Cookies.Append(cookieName, pageSize.ToString(), new CookieOptions
        {
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });

        return pageSize;
    }

    private string GetPageSizeCookieName()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(userId)
            ? PageSizeCookiePrefix
            : $"{PageSizeCookiePrefix}-{userId}";
    }

    private QAEntry ToEntry(QAInput input, string source)
    {
        return new QAEntry
        {
            Question = input.Question.Trim(),
            Answer = input.Answer.Trim(),
            AlternativeQuestions = input.AlternativeQuestions,
            Keywords = input.Keywords,
            Category = input.Category,
            MachineModel = input.MachineModel,
            SerialNumber = input.SerialNumber,
            NodeName = input.NodeName,
            ProblemType = input.ProblemType,
            Status = string.IsNullOrWhiteSpace(input.Status) ? QAEntryStatuses.Verified : input.Status,
            Source = source,
            CreatedBy = User.FindFirstValue(ClaimTypes.NameIdentifier)
        };
    }

    private static QAInput FromEntry(QAEntry entry)
    {
        return new QAInput
        {
            ImportSelected = true,
            Question = entry.Question,
            Answer = entry.Answer,
            AlternativeQuestions = entry.AlternativeQuestions,
            Keywords = entry.Keywords,
            Category = entry.Category,
            MachineModel = entry.MachineModel,
            SerialNumber = entry.SerialNumber,
            NodeName = entry.NodeName,
            ProblemType = entry.ProblemType,
            Status = string.IsNullOrWhiteSpace(entry.Status) ? QAEntryStatuses.Verified : entry.Status,
            Source = string.IsNullOrWhiteSpace(entry.Source) ? QAEntrySources.Import : entry.Source
        };
    }

    public class QAInput
    {
        public bool ImportSelected { get; set; }

        public string Question { get; set; } = string.Empty;

        public string Answer { get; set; } = string.Empty;

        public string? AlternativeQuestions { get; set; }

        public string? Keywords { get; set; }

        public string? MachineModel { get; set; }

        public string? SerialNumber { get; set; }

        public string? NodeName { get; set; }

        public string? Category { get; set; }

        public string? ProblemType { get; set; }

        public string Status { get; set; } = QAEntryStatuses.Verified;

        public string Source { get; set; } = QAEntrySources.Import;
    }

    public class QAImportInput
    {
        public IFormFile? File { get; set; }

        public bool AutoParse { get; set; }
    }
}
