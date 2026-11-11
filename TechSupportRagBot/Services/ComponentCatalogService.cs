using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;

namespace TechSupportRagBot.Services;

public sealed class ComponentCatalogService(ApplicationDbContext db)
{
    public async Task<IReadOnlyList<ResolvedComponent>> ResolveAsync(Machine machine, CancellationToken cancellationToken = default)
    {
        var installations = await db.ComponentInstallations.AsNoTracking()
            .Include(x => x.MachinePosition).Include(x => x.ComponentModel)
            .Where(x => x.IsConfirmed && x.ComponentModel.IsConfirmed && x.MachinePosition.MachineModel == machine.Model
                && (x.MachineId == null || x.MachineId == machine.Id))
            .ToListAsync(cancellationToken);

        var numericSerial = machine.SerialNumber.Length == 3
            && int.TryParse(machine.SerialNumber, out var serial) ? serial : (int?)null;
        var applicable = installations.Where(x => x.MachineId == machine.Id ||
            (x.MachineId == null && (x.SerialFrom == null && x.SerialTo == null ||
                numericSerial != null && x.SerialFrom <= numericSerial && x.SerialTo >= numericSerial)));

        var result = new List<ResolvedComponent>();
        foreach (var group in applicable.GroupBy(x => x.MachinePositionId))
        {
            var exact = group.Where(x => x.MachineId == machine.Id).ToList();
            var candidates = exact.Count > 0 ? exact : group.ToList();
            // An ambiguous rule never silently selects a device.
            if (candidates.Select(x => x.ComponentModelId).Distinct().Count() != 1) continue;
            var chosen = candidates[0];
            result.Add(new ResolvedComponent(chosen.MachinePosition, chosen.ComponentModel));
        }
        return result.OrderBy(x => x.Position.Name).ToList();
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> ClientDocumentsAsync(Machine machine, CancellationToken cancellationToken = default)
    {
        var componentIds = (await ResolveAsync(machine, cancellationToken))
            .Select(x => x.Component.Id).ToHashSet();
        var documents = await db.KnowledgeDocuments.AsNoTracking()
            .Where(x => x.ClientDownloadAllowed && x.ShowInClientLibrary)
            .ToListAsync(cancellationToken);
        return documents.Where(x => x.ComponentModelId is int componentId
                ? componentIds.Contains(componentId)
                : x.MachineId == machine.Id || x.MachineId == null
                    && !x.AppliesToAllMachines && x.MachineModel == machine.Model
                    && (string.IsNullOrWhiteSpace(x.SerialNumber) || x.SerialNumber == machine.SerialNumber))
            .OrderBy(x => x.DocumentKind).ThenBy(x => x.Title ?? x.OriginalFileName).ToList();
    }
}

public sealed record ResolvedComponent(MachinePosition Position, ComponentModel Component);
