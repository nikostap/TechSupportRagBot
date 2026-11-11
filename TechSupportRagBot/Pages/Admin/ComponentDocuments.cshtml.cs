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
    SystemSettingsService settings, AccessProfileService access, KnowledgeIngestionService ingestion) : PageModel
{
    private static readonly HashSet<string> AllowedKinds = new(StringComparer.Ordinal)
    { "MachineManual", "ElectricalScheme", "PneumaticScheme", "ComponentManual", "ErrorCatalog", "SetupGuide" };
    [BindProperty] public DocumentInput Input { get; set; } = new();
    [BindProperty] public DocumentEditInput EditInput { get; set; } = new();
    public List<KnowledgeDocument> Documents { get; private set; } = [];
    public List<ComponentModel> Components { get; private set; } = [];
    public List<Machine> Machines { get; private set; } = [];
    public IReadOnlyList<string> MachineModels { get; private set; } = [];
    public bool CanPublish { get; private set; }
    public bool CanIndex(KnowledgeDocument document) =>
        Path.GetExtension(document.OriginalFileName).ToLowerInvariant() is ".pdf" or ".docx" or ".txt" or ".xlsx";
    public bool CanPublishDocument(KnowledgeDocument document) => !CanIndex(document)
        || document.IndexStatus is DocumentIndexStates.Ready or DocumentIndexStates.NoText;
    public string IndexStatusText(KnowledgeDocument document) =>
        UiText.T(HttpContext, "DocumentIndex" + document.IndexStatus);

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        var modelName = Input.MachineModel?.Trim();
        var machine = Input.MachineId is int machineId ? await db.Machines.FindAsync([machineId], cancellationToken) : null;
        var component = Input.ComponentModelId is int componentId ? await db.ComponentModels.FindAsync([componentId], cancellationToken) : null;
        var models = (await settings.GetMachineModelsAsync(cancellationToken))
            .Concat(await db.Machines.Select(x => x.Model).ToListAsync(cancellationToken));
        if (Input.File == null || Input.MachineId != null && machine == null
            || Input.ComponentModelId != null && (component == null || component.IsArchived)
            || !string.IsNullOrWhiteSpace(modelName) && !models.Contains(modelName, StringComparer.OrdinalIgnoreCase)
            || !AllowedKinds.Contains(Input.DocumentKind)
            || machine != null && !string.IsNullOrWhiteSpace(modelName) && machine.Model != modelName)
        {
            ModelState.AddModelError(string.Empty, UiText.T(HttpContext, "DocumentSelectionInvalid"));
            await LoadAsync();
            return Page();
        }

        try
        {
            await using var upload = await validation.ValidateAsync(Input.File, UploadPurpose.Knowledge, cancellationToken);
            if (upload.Kind == UploadKind.Video) throw new InvalidOperationException();
            var provider = await storage.GetSelectedProviderAsync(cancellationToken);
            var storedName = $"{Guid.NewGuid():N}{upload.Extension}";
            var key = $"knowledge/components/{storedName}";
            await storage.SaveAsync(provider, key, upload.Content, upload.ContentType, cancellationToken);
            db.KnowledgeDocuments.Add(new KnowledgeDocument
            {
                OriginalFileName = upload.OriginalFileName, StoredFileName = storedName,
                FilePath = key, StorageProvider = provider, Category = Input.DocumentKind,
                DocumentKind = Input.DocumentKind,
                Title = Input.Title?.Trim(), MachineId = machine?.Id,
                MachineModel = machine?.Model ?? modelName, ComponentModelId = component?.Id,
                AppliesToAllMachines = machine == null && component == null && string.IsNullOrWhiteSpace(modelName),
                UploadedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!, Status = "NeedsReview",
                ClientDownloadAllowed = false, ShowInClientLibrary = false
            });
            await db.SaveChangesAsync(cancellationToken);
            return RedirectToPage();
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            ModelState.AddModelError(string.Empty, UiText.T(HttpContext, "DocumentUploadInvalid"));
            await LoadAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostPublishAsync(int id, CancellationToken cancellationToken)
    {
        if (!await access.IsAllowedAsync(User, "QA", cancellationToken)) return Forbid();
        var document = await db.KnowledgeDocuments.FindAsync([id], cancellationToken);
        if (document == null || document.DocumentKind == "Other" || document.Status == "Retired"
            || document.MachineId == null && string.IsNullOrWhiteSpace(document.MachineModel)
                && document.ComponentModelId == null && !document.AppliesToAllMachines)
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
        var document = await db.KnowledgeDocuments.FindAsync([id], cancellationToken);
        if (document == null || document.DocumentKind == "Other" || document.Status == "Retired") return NotFound();
        if (document.ClientDownloadAllowed || document.IndexStatus is DocumentIndexStates.Queued or DocumentIndexStates.Processing)
            return BadRequest();
        var wasIndexed = document.IndexStatus is DocumentIndexStates.Ready or DocumentIndexStates.NoText
            || await db.KnowledgeChunks.AnyAsync(x => x.KnowledgeDocumentId == id, cancellationToken);
        var modelName = EditInput.MachineModel?.Trim();
        var machine = EditInput.MachineId is int machineId ? await db.Machines.FindAsync([machineId], cancellationToken) : null;
        var component = EditInput.ComponentModelId is int componentId ? await db.ComponentModels.FindAsync([componentId], cancellationToken) : null;
        var models = (await settings.GetMachineModelsAsync(cancellationToken))
            .Concat(await db.Machines.Select(x => x.Model).ToListAsync(cancellationToken));
        if (!AllowedKinds.Contains(EditInput.DocumentKind)
            || EditInput.MachineId != null && machine == null
            || EditInput.ComponentModelId != null && (component == null || component.IsArchived && component.Id != document.ComponentModelId)
            || !string.IsNullOrWhiteSpace(modelName) && !models.Contains(modelName, StringComparer.OrdinalIgnoreCase)
            || machine != null && !string.IsNullOrWhiteSpace(modelName) && machine.Model != modelName)
        {
            ModelState.AddModelError(string.Empty, UiText.T(HttpContext, "DocumentSelectionInvalid"));
            await LoadAsync();
            return Page();
        }
        document.Title = EditInput.Title?.Trim();
        document.DocumentKind = EditInput.DocumentKind;
        document.Category = EditInput.DocumentKind;
        document.MachineId = machine?.Id;
        document.MachineModel = machine?.Model ?? modelName;
        document.ComponentModelId = component?.Id;
        document.AppliesToAllMachines = machine == null && component == null && string.IsNullOrWhiteSpace(modelName);
        document.Status = "NeedsReview";
        document.IndexStatus = wasIndexed && CanIndex(document) ? DocumentIndexStates.Queued : DocumentIndexStates.NotStarted;
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
                x.IndexErrorCode, x.ClientDownloadAllowed, x.Status, x.OriginalFileName })
            .ToListAsync(cancellationToken);
        return new JsonResult(documents.Select(x => new
        {
            x.Id,
            x.IndexStatus,
            x.IndexTotalChunks,
            x.IndexCompletedChunks,
            Indexable = CanIndex(new KnowledgeDocument { OriginalFileName = x.OriginalFileName }),
            StatusText = UiText.T(HttpContext, "DocumentIndex" + x.IndexStatus),
            ErrorText = x.IndexErrorCode == null ? null : UiText.T(HttpContext, x.IndexErrorCode),
            CanPublish = !CanIndex(new KnowledgeDocument { OriginalFileName = x.OriginalFileName })
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
        Documents = await db.KnowledgeDocuments.AsNoTracking().Include(x => x.ComponentModel)
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
        public string DocumentKind { get; set; } = "";
        public string? Title { get; set; }
        public int? MachineId { get; set; }
        public string? MachineModel { get; set; }
        public int? ComponentModelId { get; set; }
    }

    public class DocumentEditInput
    {
        public string DocumentKind { get; set; } = "";
        public string? Title { get; set; }
        public int? MachineId { get; set; }
        public string? MachineModel { get; set; }
        public int? ComponentModelId { get; set; }
    }
}
