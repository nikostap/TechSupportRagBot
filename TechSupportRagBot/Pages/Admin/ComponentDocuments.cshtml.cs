using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize]
public class ComponentDocumentsModel(
    ApplicationDbContext db, FileStorageService storage, FileUploadValidationService validation,
    SystemSettingsService settings, AccessProfileService access, KnowledgeIngestionService ingestion,
    PreparedManualService preparedManual, ILogger<ComponentDocumentsModel> logger) : PageModel
{
    private static readonly HashSet<string> AllowedKinds = new(StringComparer.Ordinal)
    { "MachineManual", "ElectricalScheme", "PneumaticScheme", "ComponentManual", "ErrorCatalog", "SetupGuide" };
    [BindProperty] public DocumentInput Input { get; set; } = new();
    [BindProperty] public DocumentEditInput EditInput { get; set; } = new();
    [BindProperty] public IFormFile? PreparedReplacement { get; set; }
    public List<KnowledgeDocument> Documents { get; private set; } = [];
    public List<ComponentModel> Components { get; private set; } = [];
    public List<Machine> Machines { get; private set; } = [];
    public IReadOnlyList<string> MachineModels { get; private set; } = [];
    public bool CanPublish { get; private set; }
    public bool CanIndex(KnowledgeDocument document) =>
        !string.IsNullOrWhiteSpace(document.PreparedFilePath)
        || Path.GetExtension(document.OriginalFileName).ToLowerInvariant() is ".pdf" or ".docx" or ".txt" or ".xlsx";
    public bool CanPublishDocument(KnowledgeDocument document) => !CanIndex(document)
        || document.IndexStatus is DocumentIndexStates.Ready or DocumentIndexStates.NoText;
    public string IndexStatusText(KnowledgeDocument document) =>
        UiText.T(HttpContext, "DocumentIndex" + document.IndexStatus);

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        var modelNames = Input.MachineModels.Select(x => x.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var machineIds = Input.MachineIds.Distinct().ToList();
        var machines = await db.Machines.Where(x => machineIds.Contains(x.Id)).ToListAsync(cancellationToken);
        var component = Input.ComponentModelId is int componentId ? await db.ComponentModels.FindAsync([componentId], cancellationToken) : null;
        var models = (await settings.GetMachineModelsAsync(cancellationToken))
            .Concat(await db.Machines.Select(x => x.Model).ToListAsync(cancellationToken));
        if (Input.File == null || machines.Count != machineIds.Count
            || Input.ComponentModelId != null && (component == null || component.IsArchived)
            || modelNames.Any(x => !models.Contains(x, StringComparer.OrdinalIgnoreCase))
            || !AllowedKinds.Contains(Input.DocumentKind))
        {
            ModelState.AddModelError(string.Empty, UiText.T(HttpContext, "DocumentSelectionInvalid"));
            await LoadAsync();
            return Page();
        }

        PreparedManual? prepared = null;
        if (Input.PreparedFile != null)
        {
            try { prepared = await ReadPreparedUploadAsync(Input.PreparedFile, cancellationToken); }
            catch (InvalidDataException)
            {
                ModelState.AddModelError(string.Empty, UiText.T(HttpContext, "PreparedManualInvalid"));
                await LoadAsync();
                return Page();
            }
        }
        var createdKeys = new List<string>();
        string? provider = null;
        try
        {
            await using var upload = await validation.ValidateAsync(Input.File, UploadPurpose.Knowledge, cancellationToken);
            if (upload.Kind == UploadKind.Video) throw new InvalidOperationException();
            provider = await storage.GetSelectedProviderAsync(cancellationToken);
            var storedName = $"{Guid.NewGuid():N}{upload.Extension}";
            var key = $"knowledge/components/{storedName}";
            createdKeys.Add(key);
            await storage.SaveAsync(provider, key, upload.Content, upload.ContentType, cancellationToken);
            string? preparedKey = null;
            if (prepared != null)
            {
                preparedKey = $"knowledge/components/prepared/{Guid.NewGuid():N}.json";
                createdKeys.Add(preparedKey);
                await using var preparedStream = new MemoryStream(preparedManual.Serialize(prepared));
                await storage.SaveAsync(provider, preparedKey, preparedStream, "application/json", cancellationToken);
            }
            db.KnowledgeDocuments.Add(new KnowledgeDocument
            {
                OriginalFileName = upload.OriginalFileName, StoredFileName = storedName,
                FilePath = key, StorageProvider = provider, Category = Input.DocumentKind,
                PreparedFilePath = preparedKey,
                PreparedFileName = preparedKey == null ? null : Path.GetFileName(Input.PreparedFile!.FileName)[..Math.Min(255, Path.GetFileName(Input.PreparedFile.FileName).Length)],
                IndexStatus = preparedKey == null ? DocumentIndexStates.NotStarted : DocumentIndexStates.Queued,
                IndexUpdatedAt = preparedKey == null ? null : DateTime.UtcNow,
                DocumentKind = Input.DocumentKind,
                Title = Input.Title?.Trim(), ComponentModelId = component?.Id,
                Targets = modelNames.Select(x => new KnowledgeDocumentTarget { MachineModel = x })
                    .Concat(machineIds.Select(x => new KnowledgeDocumentTarget { MachineId = x })).ToList(),
                AppliesToAllMachines = machineIds.Count == 0 && modelNames.Count == 0 && component == null,
                UploadedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!, Status = "NeedsReview",
                ClientDownloadAllowed = false, ShowInClientLibrary = false
            });
            await db.SaveChangesAsync(cancellationToken);
            createdKeys.Clear();
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            if (provider != null)
                foreach (var key in createdKeys)
                    try { await storage.DeleteAsync(provider, key, cancellationToken); }
                    catch (Exception cleanupError) { logger.LogWarning(cleanupError, "Failed to remove incomplete document upload"); }
            if (ex is not (InvalidOperationException or InvalidDataException)) throw;
            ModelState.AddModelError(string.Empty, UiText.T(HttpContext, "DocumentUploadInvalid"));
            await LoadAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUploadPreparedAsync(int id, CancellationToken cancellationToken)
    {
        if (!await access.IsAllowedAsync(User, "QA", cancellationToken)) return Forbid();
        var document = await db.KnowledgeDocuments.FindAsync([id], cancellationToken);
        if (document == null || document.DocumentKind == "Other" || document.Status == "Retired") return NotFound();
        if (document.ClientDownloadAllowed || document.IndexStatus is DocumentIndexStates.Queued or DocumentIndexStates.Processing)
            return BadRequest();
        try
        {
            var prepared = await ReadPreparedUploadAsync(PreparedReplacement, cancellationToken);
            var newKey = $"knowledge/components/prepared/{Guid.NewGuid():N}.json";
            await using var stream = new MemoryStream(preparedManual.Serialize(prepared));
            await storage.SaveAsync(document.StorageProvider, newKey, stream, "application/json", cancellationToken);
            var oldKey = document.PreparedFilePath;
            try
            {
                document.PreparedFilePath = newKey;
                document.PreparedFileName = Path.GetFileName(PreparedReplacement!.FileName)[..Math.Min(255, Path.GetFileName(PreparedReplacement.FileName).Length)];
                document.IndexStatus = DocumentIndexStates.Queued;
                document.IndexTotalChunks = 0;
                document.IndexCompletedChunks = 0;
                document.IndexErrorCode = null;
                document.IndexUpdatedAt = DateTime.UtcNow;
                document.Status = "NeedsReview";
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                try { await storage.DeleteAsync(document.StorageProvider, newKey, cancellationToken); }
                catch (Exception cleanupError) { logger.LogWarning(cleanupError, "Failed to remove incomplete prepared manual upload"); }
                throw;
            }
            if (oldKey != null)
                try { await storage.DeleteAsync(document.StorageProvider, oldKey, cancellationToken); }
                catch (Exception cleanupError) { logger.LogWarning(cleanupError, "Failed to remove replaced prepared manual"); }
            return RedirectToPage();
        }
        catch (InvalidDataException)
        {
            ModelState.AddModelError(string.Empty, UiText.T(HttpContext, "PreparedManualInvalid"));
            await LoadAsync();
            return Page();
        }
    }

    private async Task<PreparedManual> ReadPreparedUploadAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length is <= 0 or > PreparedManualService.MaxBytes
            || !string.Equals(Path.GetExtension(file.FileName), ".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Prepared manual must be a JSON file within the size limit.");
        await using var stream = file.OpenReadStream();
        return await preparedManual.ReadAsync(stream, cancellationToken);
    }

    public async Task<IActionResult> OnPostPublishAsync(int id, CancellationToken cancellationToken)
    {
        if (!await access.IsAllowedAsync(User, "QA", cancellationToken)) return Forbid();
        var document = await db.KnowledgeDocuments.FindAsync([id], cancellationToken);
        if (document == null || document.DocumentKind == "Other" || document.Status == "Retired"
            || document.MachineId == null && string.IsNullOrWhiteSpace(document.MachineModel)
                && document.ComponentModelId == null && !document.AppliesToAllMachines
                && !await db.KnowledgeDocumentTargets.AnyAsync(x => x.KnowledgeDocumentId == id, cancellationToken))
            return NotFound();
        if (!CanPublishDocument(document))
        {
            ModelState.AddModelError(string.Empty, UiText.T(HttpContext, "DocumentIndexRequiredBeforePublish"));
            await LoadAsync();
            return Page();
        }
        await using var stored = await storage.OpenReadAsync(document.StorageProvider, document.FilePath, cancellationToken);
        if (stored == null) return NotFound();
        document.ClientDownloadAllowed = true;
        document.ShowInClientLibrary = true;
        document.Status = "Published";
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, CancellationToken cancellationToken)
    {
        var document = await db.KnowledgeDocuments.Include(x => x.Targets)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (document == null || document.DocumentKind == "Other" || document.Status == "Retired") return NotFound();
        if (document.ClientDownloadAllowed || document.IndexStatus is DocumentIndexStates.Queued or DocumentIndexStates.Processing)
            return BadRequest();
        var wasIndexed = document.IndexStatus is DocumentIndexStates.Ready or DocumentIndexStates.NoText
            || await db.KnowledgeChunks.AnyAsync(x => x.KnowledgeDocumentId == id, cancellationToken);
        var modelNames = EditInput.MachineModels.Select(x => x.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var machineIds = EditInput.MachineIds.Distinct().ToList();
        var machines = await db.Machines.Where(x => machineIds.Contains(x.Id)).ToListAsync(cancellationToken);
        var component = EditInput.ComponentModelId is int componentId ? await db.ComponentModels.FindAsync([componentId], cancellationToken) : null;
        var models = (await settings.GetMachineModelsAsync(cancellationToken))
            .Concat(await db.Machines.Select(x => x.Model).ToListAsync(cancellationToken));
        if (!AllowedKinds.Contains(EditInput.DocumentKind)
            || machines.Count != machineIds.Count
            || EditInput.ComponentModelId != null && (component == null || component.IsArchived && component.Id != document.ComponentModelId)
            || modelNames.Any(x => !models.Contains(x, StringComparer.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(string.Empty, UiText.T(HttpContext, "DocumentSelectionInvalid"));
            await LoadAsync();
            return Page();
        }
        document.Title = EditInput.Title?.Trim();
        document.DocumentKind = EditInput.DocumentKind;
        document.Category = EditInput.DocumentKind;
        document.MachineId = null;
        document.MachineModel = null;
        document.ComponentModelId = component?.Id;
        document.Targets.Clear();
        foreach (var modelName in modelNames) document.Targets.Add(new KnowledgeDocumentTarget { MachineModel = modelName });
        foreach (var machineId in machineIds) document.Targets.Add(new KnowledgeDocumentTarget { MachineId = machineId });
        document.AppliesToAllMachines = machineIds.Count == 0 && modelNames.Count == 0 && component == null;
        document.Status = "NeedsReview";
        document.IndexStatus = (wasIndexed || document.PreparedFilePath != null) && CanIndex(document)
            ? DocumentIndexStates.Queued : DocumentIndexStates.NotStarted;
        document.IndexTotalChunks = 0;
        document.IndexCompletedChunks = 0;
        document.IndexErrorCode = null;
        document.IndexUpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var document = await db.KnowledgeDocuments.FindAsync([id], cancellationToken);
        if (document == null || document.DocumentKind == "Other") return NotFound();
        if (document.ClientDownloadAllowed && !await access.IsAllowedAsync(User, "QA", cancellationToken)) return Forbid();
        if (document.IndexStatus is DocumentIndexStates.Queued or DocumentIndexStates.Processing) return BadRequest();
        await ingestion.DeleteDocumentAsync(id, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostIndexAsync(int id, CancellationToken cancellationToken)
    {
        if (!await access.IsAllowedAsync(User, "QA", cancellationToken)) return Forbid();
        var document = await db.KnowledgeDocuments.FindAsync([id], cancellationToken);
        if (document == null || document.DocumentKind == "Other" || document.Status == "Retired") return NotFound();
        if (document.ClientDownloadAllowed) return BadRequest();
        if (!CanIndex(document)) return BadRequest();
        await db.KnowledgeDocuments.Where(x => x.Id == id && !x.ClientDownloadAllowed
                && x.IndexStatus != DocumentIndexStates.Queued && x.IndexStatus != DocumentIndexStates.Processing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.IndexStatus, DocumentIndexStates.Queued)
                .SetProperty(x => x.IndexCompletedChunks, 0)
                .SetProperty(x => x.IndexTotalChunks, 0)
                .SetProperty(x => x.IndexErrorCode, (string?)null)
                .SetProperty(x => x.IndexUpdatedAt, DateTime.UtcNow), cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetIndexStatusAsync(CancellationToken cancellationToken)
    {
        if (!await access.IsAllowedAsync(User, "ManageMachines", cancellationToken)) return Forbid();
        var documents = await db.KnowledgeDocuments.AsNoTracking()
            .Where(x => x.DocumentKind != "Other")
            .Select(x => new { x.Id, x.IndexStatus, x.IndexTotalChunks, x.IndexCompletedChunks,
                x.IndexErrorCode, x.ClientDownloadAllowed, x.Status, x.OriginalFileName, x.PreparedFilePath })
            .ToListAsync(cancellationToken);
        return new JsonResult(documents.Select(x => new
        {
            x.Id,
            x.IndexStatus,
            x.IndexTotalChunks,
            x.IndexCompletedChunks,
            Indexable = CanIndex(new KnowledgeDocument { OriginalFileName = x.OriginalFileName, PreparedFilePath = x.PreparedFilePath }),
            StatusText = UiText.T(HttpContext, "DocumentIndex" + x.IndexStatus),
            ErrorText = x.IndexErrorCode == null ? null : UiText.T(HttpContext, x.IndexErrorCode),
            CanPublish = !CanIndex(new KnowledgeDocument { OriginalFileName = x.OriginalFileName, PreparedFilePath = x.PreparedFilePath })
                || x.IndexStatus is DocumentIndexStates.Ready or DocumentIndexStates.NoText
        }));
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id, CancellationToken cancellationToken)
    {
        if (!await access.IsAllowedAsync(User, "QA", cancellationToken)) return Forbid();
        var document = await db.KnowledgeDocuments.FindAsync([id], cancellationToken);
        if (document == null) return NotFound();
        document.ClientDownloadAllowed = false;
        document.ShowInClientLibrary = false;
        document.Status = "NeedsReview";
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Documents = await db.KnowledgeDocuments.AsNoTracking().Include(x => x.ComponentModel).Include(x => x.Targets)
            .Where(x => x.DocumentKind != "Other").OrderByDescending(x => x.UploadedAt).ToListAsync();
        Components = await db.ComponentModels.AsNoTracking()
            .OrderBy(x => x.Manufacturer).ThenBy(x => x.PartNumber).ToListAsync();
        Machines = await db.Machines.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        MachineModels = (await settings.GetMachineModelsAsync(HttpContext.RequestAborted))
            .Concat(Machines.Select(x => x.Model)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        CanPublish = await access.IsAllowedAsync(User, "QA", HttpContext.RequestAborted);
    }

    public class DocumentInput
    {
        public IFormFile? File { get; set; }
        public IFormFile? PreparedFile { get; set; }
        public string DocumentKind { get; set; } = "";
        public string? Title { get; set; }
        public List<int> MachineIds { get; set; } = [];
        public List<string> MachineModels { get; set; } = [];
        public int? ComponentModelId { get; set; }
    }

    public class DocumentEditInput
    {
        public string DocumentKind { get; set; } = "";
        public string? Title { get; set; }
        public List<int> MachineIds { get; set; } = [];
        public List<string> MachineModels { get; set; } = [];
        public int? ComponentModelId { get; set; }
    }
}
