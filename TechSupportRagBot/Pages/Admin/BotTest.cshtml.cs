using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize]
public class BotTestModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly SupportBotService _bot;
    private readonly BotTestProgressStore _progress;
    private readonly IDataProtector _historyProtector;

    public BotTestModel(
        ApplicationDbContext db,
        SupportBotService bot,
        BotTestProgressStore progress,
        IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _bot = bot;
        _progress = progress;
        _historyProtector = dataProtectionProvider.CreateProtector("Admin.BotTest.History.v1");
    }

    [BindProperty]
    public BotTestInput Input { get; set; } = new();

    public SelectList MachineOptions { get; private set; } = new(Array.Empty<object>());

    public IReadOnlyList<SelectListItem> LanguageOptions =>
    [
        new(UiText.T(HttpContext, "RussianLanguage"), "Russian"),
        new(UiText.T(HttpContext, "EnglishLanguage"), "English")
    ];

    public string? RetrievalQuestion { get; private set; }

    public string? SupplementalRetrievalQuestion { get; private set; }

    public RagSearchResult? SearchResult { get; private set; }

    public string? ContextForLlm { get; private set; }

    public bool LlmInvoked { get; private set; }

    public IReadOnlyList<RagChunkCandidate> UnmatchedCodeCandidates { get; private set; } = [];

    public BotAnswerResult? BotAnswer { get; private set; }

    public IReadOnlyList<BotTestTurn> History { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await LoadAsync();
        var session = await CurrentSessionAsync();
        if (session is null) return;
        Input.MachineId = session.MachineId;
        Input.Language = session.Language;
        Input.HistoryJson = session.HistoryJson;
        History = ReadHistory(session.HistoryJson)?.Turns ?? [];
    }

    public async Task<IActionResult> OnPostAskAsync(CancellationToken cancellationToken)
    {
        await LoadAsync();

        if (Input.MachineId is null)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.MachineId)}", UiText.T(HttpContext, "MachineRequired"));
        }

        if (string.IsNullOrWhiteSpace(Input.Question))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Question)}", UiText.T(HttpContext, "QuestionRequired"));
        }

        if (!ModelState.IsValid || Input.MachineId is null)
        {
            return Page();
        }

        var language = ChatTranslationService.NormalizeLanguage(Input.Language);
        var session = await CurrentSessionAsync();
        var saved = ReadHistory(session?.HistoryJson);
        var turns = Input.ChatMode && saved?.MachineId == Input.MachineId && saved.Language == language
            ? saved.Turns.TakeLast(12).ToList()
            : [];
        var context = Input.ChatMode
            ? string.Join("\n", turns.Select(x => $"{(x.IsBot ? "Бот" : "Клиент")}: {NormalizeForContext(x.Text)}"))
            : null;

        Action<BotDecisionStep>? onStep = null;
        if (Input.ProgressId is Guid progressId && User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId)
        {
            _progress.Start(progressId, userId);
            onStep = step => _progress.Add(progressId, step);
        }
        BotAnswer = await _bot.AnswerAsync(Input.Question, Input.MachineId.Value, language, context,
            cancellationToken, onStep);
        RetrievalQuestion = BotAnswer.RetrievalQuestion;
        SupplementalRetrievalQuestion = BotAnswer.SupplementalRetrievalQuestion;
        SearchResult = BotAnswer.SearchResult;
        ContextForLlm = BotAnswer.ContextForLlm;
        LlmInvoked = BotAnswer.LlmInvoked;
        UnmatchedCodeCandidates = BotAnswer.UnmatchedCodeCandidates;
        if (Input.ChatMode)
        {
            turns.Add(new BotTestTurn(false, Input.Question.Trim()[..Math.Min(Input.Question.Trim().Length, 1500)]));
            turns.Add(new BotTestTurn(true, BotAnswer.Text[..Math.Min(BotAnswer.Text.Length, 4000)],
                BotAnswer.Media
                    .Where(x => x.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                        || x.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                    .Take(12)
                    .Select(x => new BotTestMedia(x.PublicId, x.OriginalFileName, x.ContentType))
                    .ToList(),
                BotAnswer.DecisionSteps.Take(20)
                    .Select(x => new BotDecisionStep(x.Key,
                        x.Detail is { Length: > 1200 } ? x.Detail[..1200] : x.Detail))
                    .ToList()));
            History = turns.TakeLast(12).ToList();
            Input.HistoryJson = _historyProtector.Protect(JsonSerializer.Serialize(new BotTestHistory(Input.MachineId.Value, language, History)));
            var sessionUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            session ??= new BotTestSession { UserId = sessionUserId };
            if (_db.Entry(session).State == EntityState.Detached) _db.BotTestSessions.Add(session);
            session.MachineId = Input.MachineId.Value;
            session.Language = language;
            session.HistoryJson = Input.HistoryJson;
            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        Input.Question = string.Empty;
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.Question)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.HistoryJson)}");

        return Page();
    }

    public async Task<IActionResult> OnPostClearAsync()
    {
        await LoadAsync();
        var session = await CurrentSessionAsync();
        if (session is not null)
        {
            _db.BotTestSessions.Remove(session);
            await _db.SaveChangesAsync();
        }
        Input.Question = string.Empty;
        Input.HistoryJson = string.Empty;
        History = [];
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.Question)}");
        ModelState.Remove($"{nameof(Input)}.{nameof(Input.HistoryJson)}");
        return Page();
    }

    public IActionResult OnGetProgress(Guid requestId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();
        return new JsonResult(new
        {
            steps = _progress.Read(requestId, userId).Select(step => new
            {
                label = UiText.T(HttpContext, step.Key),
                detail = step.Detail
            })
        });
    }

    private BotTestHistory? ReadHistory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 100000) return null;
        try
        {
            var saved = JsonSerializer.Deserialize<BotTestHistory>(_historyProtector.Unprotect(json));
            if (saved?.Turns is null || saved.Turns.Count > 12 || saved.Turns.Any(x =>
                x.Text.Length > (x.IsBot ? 4000 : 1500)
                || x.Media is { Count: > 12 }
                || x.Trace is { Count: > 20 }
                || (x.Trace?.Any(step => step.Key.Length > 80 || step.Detail?.Length > 1200) == true)
                || (x.Media?.Any(media => media.OriginalFileName.Length > 255
                    || media.ContentType.Length > 100
                    || !(media.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                        || media.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))) == true))) return null;
            return saved;
        }
        catch (Exception ex) when (ex is JsonException or System.Security.Cryptography.CryptographicException) { return null; }
    }

    private static string NormalizeForContext(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private Task<BotTestSession?> CurrentSessionAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null
            ? Task.FromResult<BotTestSession?>(null)
            : _db.BotTestSessions.FirstOrDefaultAsync(x => x.UserId == userId);
    }

    private async Task LoadAsync()
    {
        var machines = await _db.Machines
            .AsNoTracking()
            .OrderBy(x => x.Model)
            .ThenBy(x => x.SerialNumber)
            .Select(x => new
            {
                x.Id,
                Label = string.IsNullOrWhiteSpace(x.SerialNumber)
                    ? $"{x.Model} · {x.Name}"
                    : $"{x.Model} · {x.SerialNumber} · {x.Name}"
            })
            .ToListAsync();

        MachineOptions = new SelectList(machines, "Id", "Label");
    }

    public class BotTestInput
    {
        public int? MachineId { get; set; }

        public string Language { get; set; } = "Russian";

        public string Question { get; set; } = string.Empty;

        public bool ChatMode { get; set; } = true;

        public string? HistoryJson { get; set; }
        public Guid? ProgressId { get; set; }
    }

    public sealed record BotTestTurn(bool IsBot, string Text, IReadOnlyList<BotTestMedia>? Media = null,
        IReadOnlyList<BotDecisionStep>? Trace = null);
    public sealed record BotTestMedia(Guid PublicId, string OriginalFileName, string ContentType);
    public sealed record BotTestHistory(int MachineId, string Language, IReadOnlyList<BotTestTurn> Turns);
}
