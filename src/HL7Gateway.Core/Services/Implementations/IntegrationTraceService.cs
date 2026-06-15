using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace HL7Gateway.Core.Services.Implementations;

public class IntegrationTraceService
{
    public async Task AppendAsync(
        Hl7GatewayDbContext db,
        string traceId,
        string step,
        string category,
        string status,
        string? detail = null,
        string? partnerKey = null,
        int? durationMs = null,
        string? relatedEntityType = null,
        long? relatedEntityId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(traceId))
            return;

        db.IntegrationTraceEvents.Add(new IntegrationTraceEvent
        {
            TraceId = traceId.Trim(),
            Step = step,
            Category = category,
            Status = status,
            PartnerKey = partnerKey,
            Detail = detail,
            DurationMs = durationMs,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedAt = ChinaTime.Now
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Trace 写入失败不应阻断主流程
        }
    }

    public static string? ExtractControlId(string? hl7)
    {
        if (string.IsNullOrWhiteSpace(hl7))
            return null;

        var line = hl7.Split('\r', '\n').FirstOrDefault(l => l.StartsWith("MSH|", StringComparison.OrdinalIgnoreCase));
        if (line is null)
            return null;

        var fields = line.Split('|');
        return fields.Length > 9 ? fields[9].Trim() : null;
    }

    public async Task<List<IntegrationTraceEvent>> GetTimelineAsync(
        Hl7GatewayDbContext db,
        string traceId,
        int limit = 200,
        CancellationToken ct = default)
    {
        return await db.IntegrationTraceEvents
            .AsNoTracking()
            .Where(e => e.TraceId == traceId)
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
    }

    public async Task<object> GetRecentTracesAsync(
        Hl7GatewayDbContext db,
        int limit = 30,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var since = ChinaTime.Now.AddHours(-24);

        var groups = await db.IntegrationTraceEvents
            .AsNoTracking()
            .Where(e => e.CreatedAt >= since)
            .GroupBy(e => e.TraceId)
            .Select(g => new
            {
                traceId = g.Key,
                lastAt = g.Max(e => e.CreatedAt),
                stepCount = g.Count(),
                lastStep = g.OrderByDescending(e => e.CreatedAt).Select(e => e.Step).FirstOrDefault(),
                lastStatus = g.OrderByDescending(e => e.CreatedAt).Select(e => e.Status).FirstOrDefault()
            })
            .OrderByDescending(x => x.lastAt)
            .Take(limit)
            .ToListAsync(ct);

        return groups;
    }
}
