using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TechSupportRagBot.Data;
using TechSupportRagBot.Services;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: BotDialogueSmoke <repository-directory>");
    return 2;
}

Directory.SetCurrentDirectory(args[0]);
DotEnvLoader.LoadForLocalDevelopment();
var builder = WebApplication.CreateBuilder();
builder.Configuration.AddEnvironmentVariables();
builder.Environment.ContentRootPath = Path.Combine(args[0], "TechSupportRagBot");
builder.Logging.ClearProviders();
builder.Services.AddLogging();
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection("Rag"));
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<DeepSeekOptions>(builder.Configuration.GetSection("DeepSeek"));
builder.Services.Configure<QwenOptions>(builder.Configuration.GetSection("Qwen"));
builder.Services.Configure<AiTunnelOptions>(builder.Configuration.GetSection("AiTunnel"));
builder.Services.Configure<LibreTranslateOptions>(builder.Configuration.GetSection("LibreTranslate"));
builder.Services.Configure<FastTextLanguageOptions>(builder.Configuration.GetSection("FastTextLanguage"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<OllamaClient>();
builder.Services.AddHttpClient<QdrantKnowledgeClient>();
builder.Services.AddHttpClient<ChatTranslationService>();
builder.Services.AddSingleton<LanguageDetectionService>();
builder.Services.AddSingleton<RagAuditLogger>();
builder.Services.AddScoped<SystemSettingsService>();
builder.Services.AddScoped<ApiUsageService>();
builder.Services.AddScoped<IRagSearchService, RagSearchService>();
builder.Services.AddScoped<SupportBotService>();
using var app = builder.Build();
using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var machine = await db.Machines.AsNoTracking().FirstOrDefaultAsync(x => x.Model == "ALF-506" || x.Model == "ALF");
if (machine is null)
{
    Console.Error.WriteLine("No ALF machine found in the local database.");
    return 3;
}

var bot = scope.ServiceProvider.GetRequiredService<SupportBotService>();
var scenarios = new (string Name, string Language, string[] Questions)[]
{
    ("Brake coupling clarification", "Russian",
    [
        "Тормозная не работает",
        "муфта",
        "она не работает"
    ]),
    ("Label sensor clarification", "Russian",
    [
        "ALF не подаёт этикетку",
        "датчик S04",
        "индикатор не горит"
    ]),
    ("S03 and topic switch", "Russian",
    [
        "Как настроить нулевое положение кулачка приёмного лотка по датчику S03 на ALF?",
        "А как проверить этот датчик?",
        "Да",
        "Выходной сигнал датчика",
        "Какое рабочее натяжение установить для фольги на ALF?"
    ]),
    ("Tray and S04", "Russian",
    [
        "Почему рулон не выкладывается на выходной конвейер ALF и застревает в лотке?",
        "А если датчик S04 не срабатывает?"
    ]),
    ("Tension calibration", "Russian",
    [
        "Как откалибровать тензовал на ALF по эталонному грузу?",
        "Какую массу груза использовать?"
    ]),
    ("Glue in English", "English",
    [
        "How can I set up glue on the ALF?",
        "What if it does not flow?"
    ])
};
foreach (var scenario in scenarios)
{
    Console.WriteLine($"SCENARIO: {scenario.Name}");
    var transcript = new List<string>();
    foreach (var question in scenario.Questions)
    {
        var history = string.Join("\n", transcript.TakeLast(12));
        var plan = BotConversationPlanner.Create(question, history);
        Console.WriteLine($"PLAN: followUp={plan.IsFollowUp}, query={plan.SearchQuestion}");
        var answer = await bot.AnswerAsync(question, machine.Id, scenario.Language, history);
        Console.WriteLine($"CLIENT: {question}");
        Console.WriteLine($"SEARCH: {answer.RetrievalQuestion ?? "(none)"}");
        if (!string.IsNullOrWhiteSpace(answer.SupplementalRetrievalQuestion))
            Console.WriteLine($"SECOND SEARCH: {answer.SupplementalRetrievalQuestion}");
        Console.WriteLine($"CHUNKS: {string.Join(",", answer.SearchResult?.Chunks.Select(x => x.ChunkId) ?? [])}");
        Console.WriteLine($"BOT: {answer.Text}");
        Console.WriteLine();
        if (scenario.Name == "Tension calibration" && question.Contains("массу") && !answer.Text.Contains("16"))
            throw new InvalidOperationException("The ALF calibration follow-up lost the documented minimum load.");
        if (scenario.Name == "Tray and S04" && question.Contains("S04") && !answer.Text.Contains("S04"))
            throw new InvalidOperationException("The tray follow-up lost the S04 reference.");
        if (scenario.Name == "S03 and topic switch" && question.Contains("натяжение")
            && (answer.RetrievalQuestion?.Contains("S03") == true || !answer.Text.Contains("фольг")))
            throw new InvalidOperationException("The new foil question inherited the S03 topic.");
        if (scenario.Name == "S03 and topic switch" && question.Contains("Выходной сигнал")
            && !answer.Text.Contains("нет достаточных сведений"))
            throw new InvalidOperationException("The bot invented an undocumented S03 output-signal procedure.");
        if (scenario.Name == "Brake coupling clarification" && question == "она не работает"
            && (answer.RetrievalQuestion?.Contains("муфта", StringComparison.OrdinalIgnoreCase) != true
                || answer.RetrievalQuestion?.Contains("Тормозная", StringComparison.OrdinalIgnoreCase) != true))
            throw new InvalidOperationException("The brake coupling follow-up lost an earlier client detail.");
        if (scenario.Name == "Label sensor clarification" && question == "индикатор не горит"
            && answer.RetrievalQuestion?.Contains("S04", StringComparison.OrdinalIgnoreCase) != true)
            throw new InvalidOperationException("The sensor follow-up lost an earlier client detail.");
        transcript.Add($"Клиент: {question}");
        transcript.Add($"Бот: {answer.Text.Replace('\n', ' ')}");
    }
}
return 0;
