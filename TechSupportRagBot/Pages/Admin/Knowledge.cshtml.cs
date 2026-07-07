using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Pages.Admin;

[Authorize(Roles = "Admin")]
public class KnowledgeModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly KnowledgeIngestionService _ingestion;
    private readonly IWebHostEnvironment _environment;

    public KnowledgeModel(ApplicationDbContext db, KnowledgeIngestionService ingestion, IWebHostEnvironment environment)
    {
        _db = db;
        _ingestion = ingestion;
        _environment = environment;
    }

    [BindProperty]
    public KnowledgeInput Input { get; set; } = new();

    [BindProperty]
    public string? NewCategoryName { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? FilterMachineId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public List<KnowledgeDocument> Documents { get; private set; } = new();

    public List<KnowledgeCategory> Categories { get; private set; } = new();

    public SelectList MachineOptions { get; private set; } = new(Array.Empty<object>());

    public SelectList CategoryOptions { get; private set; } = new(Array.Empty<object>());

    public List<Machine> MachineSerialOptions { get; private set; } = new();

    public string MachineHintsJson { get; private set; } = "[]";

    public int TotalPages { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        ModelState.Remove(nameof(NewCategoryName));

        if (!ModelState.IsValid || Input.File == null)
        {
            if (Input.File == null)
            {
                ModelState.AddModelError(string.Empty, "Выберите файл.");
            }

            await LoadAsync();
            return Page();
        }

        var category = await _db.KnowledgeCategories.FindAsync(Input.CategoryId);
        if (category == null)
        {
            ModelState.AddModelError(string.Empty, "Выберите категорию из списка.");
            await LoadAsync();
            return Page();
        }

        var extension = Path.GetExtension(Input.File.FileName).ToLowerInvariant();
        if (extension is not ".pdf" and not ".docx" and not ".txt" and not ".xlsx" and not ".xls")
        {
            ModelState.AddModelError(string.Empty, "Поддерживаются PDF, DOCX, TXT, XLS и XLSX.");
            await LoadAsync();
            return Page();
        }

        var uploadRoot = Path.Combine(_environment.ContentRootPath, "uploads", "knowledge");
        Directory.CreateDirectory(uploadRoot);

        var storedName = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(uploadRoot, storedName);

        await using (var stream = System.IO.File.Create(path))
        {
            await Input.File.CopyToAsync(stream);
        }

        var document = new KnowledgeDocument
        {
            OriginalFileName = Input.File.FileName,
            StoredFileName = storedName,
            FilePath = path,
            Category = category.Name,
            SerialNumber = string.IsNullOrWhiteSpace(Input.SerialNumber) ? null : Input.SerialNumber.Trim(),
            MachineId = Input.MachineId,
            AppliesToAllMachines = Input.MachineId == null && string.IsNullOrWhiteSpace(Input.SerialNumber),
            UploadedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty
        };

        _db.KnowledgeDocuments.Add(document);
        await _db.SaveChangesAsync();

        await _ingestion.IndexDocumentAsync(document);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddCategoryAsync()
    {
        var name = NewCategoryName?.Trim();
        if (!string.IsNullOrWhiteSpace(name)
            && !await _db.KnowledgeCategories.AnyAsync(x => x.Name == name))
        {
            _db.KnowledgeCategories.Add(new KnowledgeCategory { Name = name });
            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync(int id)
    {
        var category = await _db.KnowledgeCategories.FindAsync(id);
        if (category != null
            && !await _db.KnowledgeDocuments.AnyAsync(x => x.Category == category.Name))
        {
            _db.KnowledgeCategories.Remove(category);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteDocumentAsync(int id)
    {
        await _ingestion.DeleteDocumentAsync(id);
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        const int pageSize = 10;
        PageNumber = Math.Max(1, PageNumber);

        var query = _db.KnowledgeDocuments
            .Include(x => x.Machine)
            .AsQueryable();

        if (FilterMachineId.HasValue)
        {
            query = query.Where(x => x.MachineId == FilterMachineId.Value);
        }

        var totalCount = await query.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        PageNumber = Math.Min(PageNumber, TotalPages);

        Documents = await query
            .OrderBy(x => x.Machine == null ? "Для всех станков" : x.Machine.Name)
            .ThenByDescending(x => x.UploadedAt)
            .Skip((PageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        Categories = await _db.KnowledgeCategories
            .OrderBy(x => x.Name)
            .ToListAsync();

        var machines = await _db.Machines.OrderBy(x => x.Name).ToListAsync();
        MachineSerialOptions = machines
            .Where(x => !string.IsNullOrWhiteSpace(x.SerialNumber))
            .DistinctBy(x => x.SerialNumber)
            .ToList();
        MachineOptions = new SelectList(machines, "Id", "Name", Input.MachineId ?? FilterMachineId);
        MachineHintsJson = JsonSerializer.Serialize(machines.Select(x => new
        {
            x.Id,
            x.Name,
            x.Model,
            x.SerialNumber
        }), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        CategoryOptions = new SelectList(Categories, "Id", "Name", Input.CategoryId == 0 ? null : Input.CategoryId);
    }

    public class KnowledgeInput
    {
        public IFormFile? File { get; set; }

        [Required]
        public int CategoryId { get; set; }

        public string? SerialNumber { get; set; }

        public int? MachineId { get; set; }
    }
}
