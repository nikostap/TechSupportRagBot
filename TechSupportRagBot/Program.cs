using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TechSupportRagBot.Data;
using TechSupportRagBot.Hubs;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

var builder = WebApplication.CreateBuilder(args);

StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var videoMaxUploadMb = builder.Configuration.GetValue("VideoProcessing:MaxUploadSizeMb", 200);
var videoMaxRequestBytes = (videoMaxUploadMb + 20) * 1024L * 1024L;
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = videoMaxRequestBytes;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = videoMaxRequestBytes;
});
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = videoMaxRequestBytes;
});

var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("ClassEngineeringSupport");

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

var databaseProvider = builder.Configuration.GetValue("Database:Provider", "Postgres");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (databaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        options.UseNpgsql(connectionString);
    }

    options.ConfigureWarnings(warnings =>
        warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
});

// Включаем страницу ошибок базы данных в режиме разработки.
// Это помогает видеть подробные ошибки миграций и базы.
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Подключаем систему пользователей Identity.
//
// Важно:
// Используем ApplicationUser, а не IdentityUser,
// потому что ApplicationUser — это наш расширенный пользователь
// с дополнительными полями, например FullName и CreatedAt.
//
// AddRoles<IdentityRole>() добавляет поддержку ролей:
// Admin, Operator, Client.
builder.Services
    .AddDefaultIdentity<ApplicationUser>(options =>
    {
        // Пока отключаем обязательное подтверждение email,
        // чтобы на этапе разработки можно было сразу входить в систему.
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Подключаем Razor Pages.
// На них будем делать админку, кабинет клиента и кабинет оператора.
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection("Rag"));
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<DeepSeekOptions>(builder.Configuration.GetSection("DeepSeek"));
builder.Services.Configure<LibreTranslateOptions>(builder.Configuration.GetSection("LibreTranslate"));
builder.Services.Configure<VideoProcessingOptions>(builder.Configuration.GetSection("VideoProcessing"));
builder.Services.AddHttpClient<OllamaClient>();
builder.Services.AddHttpClient<QdrantKnowledgeClient>();
builder.Services.AddHttpClient<ChatTranslationService>();
builder.Services.AddScoped<DocumentTextExtractor>();
builder.Services.AddSingleton<DocumentTypeDetector>();
builder.Services.AddScoped<KnowledgeIngestionService>();
builder.Services.AddScoped<SupportBotService>();
builder.Services.AddScoped<TicketDeletionService>();
builder.Services.AddScoped<ChatMessageDeletionService>();
builder.Services.AddScoped<KnowledgeFtsService>();
builder.Services.AddScoped<SmtpEmailSender>();
builder.Services.AddScoped<SystemSettingsService>();
builder.Services.AddScoped<ResolvedTicketKnowledgeService>();
builder.Services.AddScoped<QAService>();
builder.Services.AddScoped<OperatorTimeTrackingService>();
builder.Services.AddScoped<EmailNotificationService>();
builder.Services.AddScoped<AccessProfileService>();
builder.Services.AddScoped<IVideoProcessingService, VideoProcessingService>();
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddSingleton<RagAuditLogger>();
builder.Services.AddScoped<IRagSearchService, RagSearchService>();
builder.Services.AddHostedService<TicketAutoCloseService>();
builder.Services.AddHostedService<VideoProcessingBackgroundService>();
builder.Services.AddHostedService<EmailNotificationBackgroundService>();

var app = builder.Build();

// Настраиваем обработку ошибок и миграций.
if (app.Environment.IsDevelopment())
{
    // В разработке показываем страницу ошибок миграций.
    app.UseMigrationsEndPoint();
}
else
{
    // В production показываем стандартную страницу ошибки.
    app.UseExceptionHandler("/Error");

    // HSTS включает принудительное использование HTTPS в браузере.
    app.UseHsts();
}

// Перенаправляем HTTP на HTTPS.
// Для локального публичного теста за роутером можно отключить через:
// Security__UseHttpsRedirection=false
if (builder.Configuration.GetValue("Security:UseHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}

// Подключаем маршрутизацию.
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    var isMultipartPost = HttpMethods.IsPost(context.Request.Method)
        && (context.Request.ContentType?.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase) ?? false);

    try
    {
        await next();
    }
    catch (Exception ex) when (isMultipartPost)
    {
        var auditLogger = context.RequestServices.GetRequiredService<RagAuditLogger>();
        await auditLogger.WriteAsync("MultipartUploadException", new
        {
            path = context.Request.Path.Value,
            query = context.Request.QueryString.Value,
            contentType = context.Request.ContentType,
            contentLength = context.Request.ContentLength,
            exceptionType = ex.GetType().FullName,
            message = ex.Message
        }, context.TraceIdentifier);
        throw;
    }
    finally
    {
        if (isMultipartPost && context.Response.StatusCode >= 400)
        {
            var auditLogger = context.RequestServices.GetRequiredService<RagAuditLogger>();
            await auditLogger.WriteAsync("MultipartUploadRejected", new
            {
                path = context.Request.Path.Value,
                query = context.Request.QueryString.Value,
                contentType = context.Request.ContentType,
                contentLength = context.Request.ContentLength,
                statusCode = context.Response.StatusCode
            }, context.TraceIdentifier);
        }
    }
});

app.UseRouting();

// Подключаем аутентификацию.
// Она должна идти перед авторизацией, чтобы приложение знало, кто вошёл в систему.
app.UseAuthentication();

// Подключаем авторизацию.
// Без этого атрибуты [Authorize] работать не будут.
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var permission = AccessProfileService.PermissionForPath(context.Request.Path);
        if (!string.IsNullOrWhiteSpace(permission))
        {
            var access = context.RequestServices.GetRequiredService<AccessProfileService>();
            if (!await access.IsAllowedAsync(context.User, permission, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Access denied.", context.RequestAborted);
                return;
            }
        }
    }

    await next();
});

app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var path = context.Request.Path;
        var isAllowedPath = path.StartsWithSegments("/Identity/Account/ChangePassword")
            || path.StartsWithSegments("/Identity/Account/Logout")
            || path.StartsWithSegments("/css")
            || path.StartsWithSegments("/js")
            || path.StartsWithSegments("/lib")
            || path.StartsWithSegments("/favicon.ico");

        if (!isAllowedPath)
        {
            var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.GetUserAsync(context.User);
            if (user?.MustChangePassword == true)
            {
                context.Response.Redirect("/Identity/Account/ChangePassword");
                return;
            }
        }
    }

    await next();
});

// Подключаем статические файлы и Razor Pages.
app.MapStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

// Перед запуском приложения создаём стартовые роли и администратора.
// Это выполнится один раз при первом запуске,
// а при последующих запусках просто проверит, что всё уже существует.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    await services.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    await TechSupportRagBot.Services.DbInitializer.SeedAsync(services);
    await services.GetRequiredService<SystemSettingsService>().SeedDefaultsAsync();
    var accessProfiles = services.GetRequiredService<AccessProfileService>();
    await accessProfiles.SaveProfilesAsync(await accessProfiles.GetProfilesAsync());
    await accessProfiles.HardenClientProfilesAsync();
    await accessProfiles.FillMissingUserProfilesAsync();
    await services.GetRequiredService<KnowledgeFtsService>().RebuildAsync();
    if (builder.Configuration.GetValue("Rag:ReindexOnStartup", false))
    {
        await services.GetRequiredService<KnowledgeIngestionService>().ReindexAllDocumentsAsync();
    }
}

// Запускаем веб-приложение.
app.Run();

