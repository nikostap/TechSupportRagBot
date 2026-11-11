using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using TechSupportRagBot.Data;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize]
public class BotTestModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly SupportBotService _bot;
    private readonly IDataProtector _historyProtector;

    public BotTestModel(
        ApplicationDbContext db,
        SupportBotService bot,
        IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _bot = bot;
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

    public BotAnswerResult? BotAnswer { get; private set; }

    public IReadOnlyList<BotTestTurn> History { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await LoadAsync();
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
        var saved = ReadHistory(Input.HistoryJson);
        var turns = Input.ChatMode && saved?.MachineId == Input.MachineId && saved.Language == language
            ? saved.Turns.TakeLast(12).ToList()
            : [];
        var context = Input.ChatMode
            ? string.Join("\n", turns.Select(x => $"{(x.IsBot ? "Бот" : "Клиент")}: {x.Text[..Math.Min(x.Text.Length, 700)]}"))
            : null;

        BotAnswer = await _bot.AnswerAsync(Input.Question, Input.MachineId.Value, language, context, cancellationToken);
        RetrievalQuestion = BotAnswer.RetrievalQuestion;
        SupplementalRetrievalQuestion = BotAnswer.SupplementalRetrievalQuestion;
        SearchResult = BotAnswer.SearchResult;
        ContextForLlm = BotAnswer.ContextForLlm;
        if (Input.ChatMode)
        {
            turns.Add(new BotTestTurn(false, Input.Question.Trim()[..Math.Min(Input.Question.Trim().Length, 1500)]));
            turns.Add(new BotTestTurn(true, BotAnswer.Text[..Math.Min(BotAnswer.Text.Length, 1500)]));
            History = turns.TakeLast(12).ToList();
            Input.HistoryJson = _historyProtector.Protect(JsonSerializer.Serialize(new BotTestHistory(Input.MachineId.Value, language, History)));
        }

        Input.Question = string.Empty;

        return Page();
    }

    public async Task<IActionResult> OnPostClearAsync()
    {
        await LoadAsync();
        Input.Question = string.Empty;
        Input.HistoryJson = string.Empty;
        History = [];
        return Page();
    }

    private BotTestHistory? ReadHistory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 35000) return null;
        try
        {
            var saved = JsonSerializer.Deserialize<BotTestHistory>(_historyProtector.Unprotect(json));
            if (saved?.Turns is null || saved.Turns.Count > 12 || saved.Turns.Any(x => x.Text.Length > 1500)) return null;
            return saved;
        }
        catch (Exception ex) when (ex is JsonException or System.Security.Cryptography.CryptographicException) { return null; }
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

        public bool ChatMode { get; set; }

        public string? HistoryJson { get; set; }
    }

    public sealed record BotTestTurn(bool IsBot, string Text);
    public sealed record BotTestHistory(int MachineId, string Language, IReadOnlyList<BotTestTurn> Turns);
}
