using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin.Machines;

[Authorize]
public class DetailsModel(ApplicationDbContext db, ComponentCatalogService catalog) : PageModel
{
    public Machine? Machine { get; private set; }
    public IReadOnlyList<ResolvedComponent> Components { get; private set; } = [];
    public IReadOnlyList<KnowledgeDocument> Documents { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        Machine = await db.Machines.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (Machine == null) return NotFound();
        Components = await catalog.ResolveAsync(Machine, cancellationToken);
        Documents = await catalog.ClientDocumentsAsync(Machine, cancellationToken);
        return Page();
    }
}
