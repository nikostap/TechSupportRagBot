using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Client;

[Authorize]
public class MachineDocumentsModel(ApplicationDbContext db, ComponentCatalogService catalog, FileStorageService storage) : PageModel
{
    public Machine? Machine { get; private set; }
    public IReadOnlyList<KnowledgeDocument> Documents { get; private set; } = [];
    public List<Machine> AvailableMachines { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int machineId, CancellationToken cancellationToken)
    {
        Machine = await AllowedMachineAsync(machineId, cancellationToken);
        if (Machine == null) return NotFound();
        Documents = await catalog.ClientDocumentsAsync(Machine, cancellationToken);
        var clientId = await CurrentClientIdAsync(cancellationToken);
        AvailableMachines = await db.ClientMachines.AsNoTracking().Where(x => x.ClientId == clientId)
            .Select(x => x.Machine!).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(int machineId, int documentId, CancellationToken cancellationToken)
    {
        var machine = await AllowedMachineAsync(machineId, cancellationToken);
        if (machine == null) return NotFound();
        var document = (await catalog.ClientDocumentsAsync(machine, cancellationToken))
            .FirstOrDefault(x => x.Id == documentId);
        if (document == null) return NotFound();
        var stored = await storage.OpenReadAsync(document.StorageProvider, document.FilePath, cancellationToken);
        if (stored == null) return NotFound();
        Response.OnCompleted(async () => await stored.DisposeAsync());
        Response.Headers.CacheControl = "private, no-store";
        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(document.OriginalFileName, out var contentType))
            contentType = "application/octet-stream";
        return File(stored.Stream, contentType, document.OriginalFileName);
    }

    private async Task<int?> CurrentClientIdAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => x.ClientId).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Machine?> AllowedMachineAsync(int machineId, CancellationToken cancellationToken)
    {
        var clientId = await CurrentClientIdAsync(cancellationToken);
        if (clientId == null) return null;
        return await db.ClientMachines.AsNoTracking()
            .Where(x => x.ClientId == clientId && x.MachineId == machineId)
            .Select(x => x.Machine).FirstOrDefaultAsync(cancellationToken);
    }
}
