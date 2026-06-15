using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;

    public ConfigController(Hl7GatewayDbContext db) => _db = db;

    [HttpGet("export")]
    public async Task<IActionResult> ExportConfig()
    {
        var mappings = await _db.IdentifierMappings.ToListAsync();
        var wsi = await _db.WsiSubscriptions.ToListAsync();
        return Ok(new
        {
            exportedAt = DateTime.UtcNow,
            identifierMappings = mappings.Select(m => new
            {
                m.SourceSystem, m.SourceCode, m.SourceText,
                m.VitalSignType, m.VitalSignName, m.LoincCode, m.IsActive
            }),
            wsiSubscriptions = wsi.Select(w => new
            {
                w.NotificationUri, w.ClientId, w.PatientIdDomain,
                w.FacilityCode, w.IsActive
            })
        });
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportConfig([FromBody] ConfigExport data)
    {
        if (data.IdentifierMappings is { Count: > 0 })
        {
            _db.IdentifierMappings.RemoveRange(await _db.IdentifierMappings.ToListAsync());
            foreach (var m in data.IdentifierMappings)
            {
                _db.IdentifierMappings.Add(new IdentifierMapping
                {
                    SourceSystem = m.SourceSystem,
                    SourceCode = m.SourceCode,
                    SourceText = m.SourceText,
                    VitalSignType = m.VitalSignType,
                    VitalSignName = m.VitalSignName,
                    LoincCode = m.LoincCode,
                    IsActive = m.IsActive
                });
            }
        }
        if (data.WsiSubscriptions is { Count: > 0 })
        {
            _db.WsiSubscriptions.RemoveRange(await _db.WsiSubscriptions.ToListAsync());
            foreach (var w in data.WsiSubscriptions)
            {
                _db.WsiSubscriptions.Add(new WsiSubscription
                {
                    NotificationUri = w.NotificationUri,
                    ClientId = w.ClientId,
                    PatientIdDomain = w.PatientIdDomain,
                    FacilityCode = w.FacilityCode,
                    IsActive = w.IsActive
                });
            }
        }
        await _db.SaveChangesAsync();
        return Ok(new { message = "配置已导入" });
    }
}

public class ConfigExport
{
    public List<MappingItem>? IdentifierMappings { get; set; }
    public List<WsiItem>? WsiSubscriptions { get; set; }
}

public class MappingItem
{
    public string SourceSystem { get; set; } = "";
    public string SourceCode { get; set; } = "";
    public string? SourceText { get; set; }
    public string VitalSignType { get; set; } = "";
    public string VitalSignName { get; set; } = "";
    public string? LoincCode { get; set; }
    public bool IsActive { get; set; }
}

public class WsiItem
{
    public string NotificationUri { get; set; } = "";
    public string? ClientId { get; set; }
    public string? PatientIdDomain { get; set; }
    public string? FacilityCode { get; set; }
    public bool IsActive { get; set; }
}
