using HL7Gateway.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IdentifierMappingsController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;

    public IdentifierMappingsController(Hl7GatewayDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? sourceSystem, [FromQuery] bool? isActive)
    {
        var query = _db.IdentifierMappings.AsQueryable();

        if (!string.IsNullOrEmpty(sourceSystem))
            query = query.Where(m => m.SourceSystem == sourceSystem);
        if (isActive.HasValue)
            query = query.Where(m => m.IsActive == isActive.Value);

        var items = await query.OrderBy(m => m.SourceSystem).ThenBy(m => m.SourceCode).ToListAsync();
        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _db.IdentifierMappings.FindAsync(id);
        if (item is null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] IdentifierMapping mapping)
    {
        mapping.CreatedAt = ChinaTime.Now;
        mapping.UpdatedAt = ChinaTime.Now;
        _db.IdentifierMappings.Add(mapping);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = mapping.MappingId }, mapping);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] IdentifierMapping mapping)
    {
        var existing = await _db.IdentifierMappings.FindAsync(id);
        if (existing is null) return NotFound();

        existing.SourceSystem = mapping.SourceSystem;
        existing.SourceCode = mapping.SourceCode;
        existing.SourceText = mapping.SourceText;
        existing.VitalSignType = mapping.VitalSignType;
        existing.VitalSignName = mapping.VitalSignName;
        existing.LoincCode = mapping.LoincCode;
        existing.IsActive = mapping.IsActive;
        existing.UpdatedAt = ChinaTime.Now;

        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.IdentifierMappings.FindAsync(id);
        if (item is null) return NotFound();

        _db.IdentifierMappings.Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
