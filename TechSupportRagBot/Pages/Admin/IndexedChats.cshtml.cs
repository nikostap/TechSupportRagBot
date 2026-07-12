using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class IndexedChatsModel : PageModel
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50, 100];
    private const int DefaultPageSize = 10;
    private const string PageSizeCookiePrefix = "indexed-chats-page-size";

    private readonly ApplicationDbContext _db;
    private readonly KnowledgeIngestionService _ingestion;

    public IndexedChatsModel(ApplicationDbContext db, KnowledgeIngestionService ingestion)
    {
        _db = db;
        _ingestion = ingestion;
    }

    [BindProperty(SupportsGet = true)]
    public int? MachineId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int? PageSize { get; set; }

    [BindProperty]
    public ArchiveEditInput EditInput { get; set; } = new();

    public int[] PageSizeOptions => AllowedPageSizes;

    public int TotalPages { get; private set; }

    public SelectList MachineOptions { get; private set; } = new(Array.Empty<object>());

    public List<IndexedChatRow> Rows { get; private set; } = new();

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostReindexAsync(int id, CancellationToken cancellationToken)
    {
        var answer = await _db.ResolvedAnswers
            .Include(x => x.Ticket)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (answer == null)
        {
            return NotFound();
        }

        await _ingestion.IndexResolvedAnswerAsync(answer, cancellationToken);

        return RedirectToPage(new
        {
            MachineId,
            PageNumber,
            PageSize
        });
    }

    public async Task<IActionResult> OnPostSaveDraftAsync(int id, CancellationToken cancellationToken)
    {
        var answer = await _db.ResolvedAnswers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (answer == null) return NotFound();
        ApplyEdit(answer);
        await _db.SaveChangesAsync(cancellationToken);
        return RedirectToPage(new { MachineId, PageNumber, PageSize });
    }

    public async Task<IActionResult> OnPostIndexAsync(int id, CancellationToken cancellationToken)
    {
        var answer = await _db.ResolvedAnswers
            .Include(x => x.Ticket)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (answer == null) return NotFound();
        ApplyEdit(answer);
        if (string.IsNullOrWhiteSpace(answer.Question) || string.IsNullOrWhiteSpace(answer.Answer))
        {
            return RedirectToPage(new { MachineId, PageNumber, PageSize });
        }
        await _db.SaveChangesAsync(cancellationToken);
        await _ingestion.IndexResolvedAnswerAsync(answer, cancellationToken);
        return RedirectToPage(new { MachineId, PageNumber, PageSize });
    }

    public async Task<IActionResult> OnPostDeleteDraftAsync(int id, CancellationToken cancellationToken)
    {
        var answer = await _db.ResolvedAnswers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (answer == null) return NotFound();
        if (answer.Status == ResolvedAnswerStatuses.Draft)
        {
            _db.ResolvedAnswers.Remove(answer);
            await _db.SaveChangesAsync(cancellationToken);
        }
        return RedirectToPage(new { MachineId, PageNumber, PageSize });
    }

    public async Task<IActionResult> OnPostDeleteIndexedAsync(int id, CancellationToken cancellationToken)
    {
        var answer = await _db.ResolvedAnswers
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (answer == null)
        {
            return NotFound();
        }

        if (answer.Status != ResolvedAnswerStatuses.Draft)
        {
            await _ingestion.DeleteResolvedAnswerAsync(answer, cancellationToken);
        }

        return RedirectToPage(new
        {
            MachineId,
            PageNumber,
            PageSize
        });
    }

    private void ApplyEdit(ResolvedAnswer answer)
    {
        answer.Title = EditInput.Title?.Trim();
        answer.Question = EditInput.Question?.Trim() ?? string.Empty;
        answer.Answer = EditInput.Answer?.Trim() ?? string.Empty;
        answer.AlternativeQuestions = EditInput.AlternativeQuestions?.Trim();
        answer.Category = EditInput.Category?.Trim() ?? "Решённые обращения";
        answer.NodeName = EditInput.NodeName?.Trim();
        answer.ProblemType = EditInput.ProblemType?.Trim();
        answer.Tags = EditInput.Tags?.Trim();
    }

    private async Task LoadAsync()
    {
        var pageSize = ResolvePageSize();
        PageNumber = Math.Max(1, PageNumber);

        var query = _db.ResolvedAnswers
            .AsNoTracking()
            .Include(x => x.Ticket)
                .ThenInclude(x => x!.ClientUser)
            .Include(x => x.Machine)
            .Include(x => x.Ticket)
                .ThenInclude(x => x!.OperatorUser)
            .AsQueryable();

        if (MachineId.HasValue)
        {
            query = query.Where(x => x.MachineId == MachineId.Value);
        }

        var totalCount = await query.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        PageNumber = Math.Min(PageNumber, TotalPages);

        var answers = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((PageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var answerIds = answers.Select(x => x.Id).ToList();
        var chunks = await _db.KnowledgeChunks
            .AsNoTracking()
            .Where(x => x.ResolvedAnswerId != null && answerIds.Contains(x.ResolvedAnswerId.Value))
            .OrderBy(x => x.ChunkIndex)
            .ToListAsync();

        var chunksByAnswer = chunks
            .GroupBy(x => x.ResolvedAnswerId!.Value)
            .ToDictionary(x => x.Key, x => x.ToList());

        Rows = answers
            .Select(x => new IndexedChatRow(x, chunksByAnswer.TryGetValue(x.Id, out var answerChunks) ? answerChunks : []))
            .ToList();

        var machines = await _db.Machines
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                Name = string.IsNullOrWhiteSpace(x.Model)
                    ? x.Name
                    : x.Name + " · " + x.Model
            })
            .ToListAsync();

        MachineOptions = new SelectList(machines, "Id", "Name", MachineId);
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
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrWhiteSpace(userId)
            ? PageSizeCookiePrefix
            : $"{PageSizeCookiePrefix}-{userId}";
    }

    public sealed record IndexedChatRow(ResolvedAnswer Answer, IReadOnlyList<KnowledgeChunk> Chunks)
    {
        public bool IsIndexed => Chunks.Count > 0 && Chunks.Any(x => !string.IsNullOrWhiteSpace(x.QdrantPointId));

        public bool HasChunk => Chunks.Count > 0;
    }

    public sealed class ArchiveEditInput
    {
        public string? Title { get; set; }
        public string? Question { get; set; }
        public string? Answer { get; set; }
        public string? AlternativeQuestions { get; set; }
        public string? Category { get; set; }
        public string? NodeName { get; set; }
        public string? ProblemType { get; set; }
        public string? Tags { get; set; }
    }
}
