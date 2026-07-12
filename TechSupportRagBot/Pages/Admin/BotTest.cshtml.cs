using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class BotTestModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly SupportBotService _bot;
    private readonly IRagSearchService _ragSearch;
    private readonly ChatTranslationService _translation;

    public BotTestModel(
        ApplicationDbContext db,
        SupportBotService bot,
        IRagSearchService ragSearch,
        ChatTranslationService translation)
    {
        _db = db;
        _bot = bot;
        _ragSearch = ragSearch;
        _translation = translation;
    }

    [BindProperty]
    public BotTestInput Input { get; set; } = new();

    public SelectList MachineOptions { get; private set; } = new(Array.Empty<object>());

    public IReadOnlyList<SelectListItem> LanguageOptions { get; } =
    [
        new("Русский", "Russian"),
        new("English", "English")
    ];

    public string? RetrievalQuestion { get; private set; }

    public RagSearchResult? SearchResult { get; private set; }

    public string? ContextForLlm { get; private set; }

    public BotAnswerResult? BotAnswer { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAskAsync(CancellationToken cancellationToken)
    {
        await LoadAsync();

        if (Input.MachineId is null)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.MachineId)}", "Выберите станок.");
        }

        if (string.IsNullOrWhiteSpace(Input.Question))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.Question)}", "Введите вопрос.");
        }

        if (!ModelState.IsValid || Input.MachineId is null)
        {
            return Page();
        }

        var language = ChatTranslationService.NormalizeLanguage(Input.Language);
        RetrievalQuestion = await _translation.TranslateToRussianForSearchAsync(
            Input.Question,
            language,
            cancellationToken);

        SearchResult = await _ragSearch.SearchAsync(new RagSearchRequest
        {
            Question = RetrievalQuestion,
            MachineId = Input.MachineId.Value,
            DenseTopK = 40,
            KeywordTopK = 40,
            FinalTopK = 8
        }, cancellationToken);

        ContextForLlm = _ragSearch.BuildContextForLlm(SearchResult);
        BotAnswer = await _bot.AnswerAsync(Input.Question, Input.MachineId.Value, language, null, cancellationToken);

        return Page();
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
    }
}
