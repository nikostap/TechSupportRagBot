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
    SystemSettingsService settings, AccessProfileService access) : PageModel
{
    private static readonly HashSet<string> AllowedKinds = new(StringComparer.Ordinal)
    { "MachineManual", "ElectricalScheme", "PneumaticScheme", "ComponentManual", "ErrorCatalog", "SetupGuide" };
    [BindProperty] public DocumentInput Input { get; set; } = new();
    public List<KnowledgeDocument> Documents { get; private set; } = [];
    public List<ComponentModel> Components { get; private set; } = [];
    public List<Machine> Machines { get; private set; } = [];
    public IReadOnlyList<string> MachineModels { get; private set; } = [];
    public bool CanPublish { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        var modelName = Input.MachineModel?.Trim();
        var machine = Input.MachineId is int machineId ? await db.Machines.FindAsync([machineId], cancellationToken) : null;
        var component = Input.ComponentModelId is int componentId ? await db.ComponentModels.FindAsync([componentId], cancellationToken) : null;
        var models = (await settings.GetMachineModelsAsync(cancellationToken))
            .Concat(await db.Machines.Select(x => x.Model).ToListAsync(cancellationToken));
        if (Input.File == null || Input.MachineId != null && machine == null
            || Input.ComponentModelId != null && component == null
            || machine == null && component == null && (string.IsNullOrWhiteSpace(modelName)
                || !models.Contains(modelName, StringComparer.OrdinalIgnoreCase))
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
                DocumentKind = Input.DocumentKind, Revision = Input.Revision?.Trim(),
                Title = Input.Title?.Trim(), MachineId = machine?.Id,
                MachineModel = machine?.Model ?? modelName, ComponentModelId = component?.Id,
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
        if (document == null || document.DocumentKind == "Other"
            || document.MachineId == null && string.IsNullOrWhiteSpace(document.MachineModel) && document.ComponentModelId == null)
            return NotFound();
        await using var stored = await storage.OpenReadAsync(document.StorageProvider, document.FilePath, cancellationToken);
        if (stored == null) return NotFound();
        document.ClientDownloadAllowed = true;
        document.ShowInClientLibrary = true;
        document.Status = "Published";
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id, CancellationToken cancellationToken)
    {
        if (!await access.IsAllowedAsync(User, "QA", cancellationToken)) return Forbid();
        var document = await db.KnowledgeDocuments.FindAsync([id], cancellationToken);
        if (document == null) return NotFound();
        document.ClientDownloadAllowed = false;
        document.ShowInClientLibrary = false;
        document.Status = "Retired";
        await db.SaveChangesAsync(cancellationToken);
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Documents = await db.KnowledgeDocuments.AsNoTracking().Include(x => x.ComponentModel)
            .Where(x => x.DocumentKind != "Other").OrderByDescending(x => x.UploadedAt).ToListAsync();
        Components = await db.ComponentModels.AsNoTracking().OrderBy(x => x.Manufacturer).ThenBy(x => x.PartNumber).ToListAsync();
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
        public string? Revision { get; set; }
        public int? MachineId { get; set; }
        public string? MachineModel { get; set; }
        public int? ComponentModelId { get; set; }
    }
}
