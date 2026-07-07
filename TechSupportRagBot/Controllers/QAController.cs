using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechSupportRagBot.Data;
using TechSupportRagBot.Models;
using TechSupportRagBot.Services;

namespace TechSupportRagBot.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/qa")]
public class QAController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly QAService _qa;

    public QAController(ApplicationDbContext db, QAService qa)
    {
        _db = db;
        _qa = qa;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken cancellationToken)
    {
        var query = _db.QAEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        var items = await query.OrderByDescending(x => x.UpdatedAt).Take(200).ToListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var item = await _db.QAEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return item == null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] QAEntry input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Question) || string.IsNullOrWhiteSpace(input.Answer))
        {
            return BadRequest(new { error = "Question and Answer are required." });
        }

        input.CreatedBy = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var item = await _qa.CreateAsync(input, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] QAEntry input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Question) || string.IsNullOrWhiteSpace(input.Answer))
        {
            return BadRequest(new { error = "Question and Answer are required." });
        }

        return await _qa.UpdateAsync(id, input, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        return await _qa.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromForm] IFormFile file, [FromForm] bool autoParse, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { error = "File is empty." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not ".docx" and not ".txt" and not ".md")
        {
            return BadRequest(new { error = "Supported formats: DOCX, TXT, MD." });
        }

        await using var stream = file.OpenReadStream();
        var items = await _qa.ImportAsync(file.FileName, stream, autoParse, User.FindFirstValue(ClaimTypes.NameIdentifier), cancellationToken);
        return Ok(new { imported = items.Count, items });
    }

    [HttpPost("{id:int}/verify")]
    public async Task<IActionResult> Verify(int id, CancellationToken cancellationToken)
    {
        return await _qa.VerifyAsync(id, cancellationToken) ? Ok(new { ok = true }) : NotFound();
    }

    [HttpPost("{id:int}/reindex")]
    public async Task<IActionResult> Reindex(int id, CancellationToken cancellationToken)
    {
        return await _qa.ReindexAsync(id, cancellationToken) ? Ok(new { ok = true }) : NotFound();
    }

    [HttpPost("{id:int}/generate-metadata")]
    public async Task<IActionResult> GenerateMetadata(int id, CancellationToken cancellationToken)
    {
        var item = await _qa.GenerateMetadataAsync(id, cancellationToken);
        return item == null ? NotFound() : Ok(item);
    }
}
