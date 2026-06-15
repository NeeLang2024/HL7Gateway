using HL7Gateway.Core;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Integration.Routing;
using HL7Gateway.Core.Models;
using HL7Gateway.Core.Services.Implementations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/integration/routing")]
public class IntegrationRoutingController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;
    private readonly IntegrationHubSettingsService _settings;
    private readonly RoutingEngine _engine;

    public IntegrationRoutingController(
        Hl7GatewayDbContext db,
        IntegrationHubSettingsService settings,
        RoutingEngine engine)
    {
        _db = db;
        _settings = settings;
        _engine = engine;
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var settings = await _settings.GetSettingsAsync(_db, ct);
        var active = await _settings.IsRoutingActiveAsync(_db, ct);
        var ruleCount = await _db.RoutingRules.CountAsync(ct);
        return Ok(new { settings, routingActive = active, ruleCount });
    }

    [HttpPut("settings")]
    public async Task<IActionResult> PutSettings([FromBody] IntegrationHubSettings settings, CancellationToken ct)
    {
        var saved = await _settings.SaveSettingsAsync(_db, settings, ct);
        return Ok(new { settings = saved, routingActive = await _settings.IsRoutingActiveAsync(_db, ct) });
    }

    [HttpGet("rules")]
    public async Task<IActionResult> ListRules(CancellationToken ct)
    {
        var items = await _db.RoutingRules.AsNoTracking()
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("rules")]
    public async Task<IActionResult> CreateRule([FromBody] RoutingRule rule, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rule.Name))
            return BadRequest(new { message = "规则名称必填" });

        rule.Id = 0;
        rule.Name = rule.Name.Trim();
        rule.Action = NormalizeAction(rule.Action);
        var now = ChinaTime.Now;
        rule.CreatedAt = now;
        rule.UpdatedAt = now;
        _db.RoutingRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        return Ok(rule);
    }

    [HttpPut("rules/{id:long}")]
    public async Task<IActionResult> UpdateRule(long id, [FromBody] RoutingRule rule, CancellationToken ct)
    {
        var existing = await _db.RoutingRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (existing is null) return NotFound();

        existing.Name = string.IsNullOrWhiteSpace(rule.Name) ? existing.Name : rule.Name.Trim();
        existing.Priority = rule.Priority;
        existing.IsEnabled = rule.IsEnabled;
        existing.MessageType = NullIfEmpty(rule.MessageType);
        existing.TriggerEvent = NullIfEmpty(rule.TriggerEvent);
        existing.SourceIpPattern = NullIfEmpty(rule.SourceIpPattern);
        existing.SendingApp = NullIfEmpty(rule.SendingApp);
        existing.SendingFacility = NullIfEmpty(rule.SendingFacility);
        existing.Action = NormalizeAction(rule.Action);
        existing.WebhookUrl = NullIfEmpty(rule.WebhookUrl);
        existing.TransformJson = NullIfEmpty(rule.TransformJson);
        existing.Remark = NullIfEmpty(rule.Remark);
        existing.UpdatedAt = ChinaTime.Now;
        await _db.SaveChangesAsync(ct);
        return Ok(existing);
    }

    [HttpDelete("rules/{id:long}")]
    public async Task<IActionResult> DeleteRule(long id, CancellationToken ct)
    {
        var existing = await _db.RoutingRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (existing is null) return NotFound();
        _db.RoutingRules.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return Ok(new { deleted = true });
    }

    [HttpPost("test")]
    public async Task<IActionResult> TestMatch([FromBody] RoutingTestRequest request, CancellationToken ct)
    {
        var rules = await _db.RoutingRules.AsNoTracking()
            .Where(r => r.IsEnabled)
            .ToListAsync(ct);

        var msg = BuildMessageFromTest(request);
        var result = _engine.DescribeMatch(msg, rules);
        var transformed = request.Hl7;
        var hit = _engine.Match(msg, rules);
        if (hit is not null && !string.IsNullOrWhiteSpace(hit.Rule.TransformJson))
            transformed = Hl7FieldTransform.Apply(request.Hl7 ?? "", hit.Rule.TransformJson);

        return Ok(new { match = result, transformedHl7 = transformed });
    }

    private static Hl7Message BuildMessageFromTest(RoutingTestRequest request)
    {
        var hl7 = request.Hl7 ?? "";
        var msh = hl7.Split('\r', '\n').FirstOrDefault(l => l.StartsWith("MSH|", StringComparison.OrdinalIgnoreCase));
        var fields = msh?.Split('|') ?? Array.Empty<string>();
        var msgType = fields.Length > 8 ? fields[8] : "";
        var parts = msgType.Split('^');

        return new Hl7Message
        {
            MessageControlId = fields.Length > 9 ? fields[9] : "TEST",
            MessageType = request.MessageType ?? (parts.Length > 0 ? parts[0] : ""),
            TriggerEvent = request.TriggerEvent ?? (parts.Length > 1 ? parts[1] : ""),
            SourceIp = request.SourceIp ?? "127.0.0.1",
            SendingApp = request.SendingApp ?? (fields.Length > 2 ? fields[2] : null),
            SendingFacility = request.SendingFacility ?? (fields.Length > 3 ? fields[3] : null),
            RawContent = hl7
        };
    }

    private static string NormalizeAction(string? action)
    {
        var a = (action ?? RoutingActions.ForwardAdt).Trim();
        return a switch
        {
            RoutingActions.LegacyDefault or RoutingActions.ForwardAdt or RoutingActions.SkipForward
                or RoutingActions.Webhook or RoutingActions.ForwardAdtWebhook => a,
            _ => RoutingActions.ForwardAdt
        };
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public class RoutingTestRequest
{
    public string? Hl7 { get; set; }
    public string? MessageType { get; set; }
    public string? TriggerEvent { get; set; }
    public string? SourceIp { get; set; }
    public string? SendingApp { get; set; }
    public string? SendingFacility { get; set; }
}
