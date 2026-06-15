using HL7Gateway.Core;
using HL7Gateway.Core.DbContexts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;
    private readonly IMemoryCache _cache;

    public MetricsController(Hl7GatewayDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var payload = await _cache.GetOrCreateAsync("metrics:summary", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await BuildMetricsAsync(ct);
        });
        return Ok(payload);
    }

    private async Task<object> BuildMetricsAsync(CancellationToken ct)
    {
        var now = ChinaTime.Now;
        var todayStart = now.Date;

        var queueStats = await _db.AdtQueue
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Pending = g.Count(q => q.Status == 0),
                Failed = g.Count(q => q.Status == 3),
                Due = g.Count(q => q.Status == 0
                    && q.RetryCount < q.MaxRetries
                    && (q.NextRetryAt == null || q.NextRetryAt <= now)),
            })
            .FirstOrDefaultAsync(ct);

        var todayMessages = await _db.Hl7Messages
            .AsNoTracking()
            .CountAsync(m => m.ReceivedAt >= todayStart, ct);

        var totalMessages = await _cache.GetOrCreateAsync("metrics:totalMessages", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await _db.Hl7Messages.AsNoTracking().CountAsync(ct);
        });

        var subStats = await _db.WsiSubscriptions
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Active = g.Count(s => s.IsActive),
                Total = g.Count(),
            })
            .FirstOrDefaultAsync(ct);

        return new
        {
            messages = new
            {
                total = totalMessages,
                today = todayMessages,
            },
            adtQueue = new
            {
                pending = queueStats?.Pending ?? 0,
                due = queueStats?.Due ?? 0,
                failed = queueStats?.Failed ?? 0,
            },
            subscriptions = new
            {
                active = subStats?.Active ?? 0,
                total = subStats?.Total ?? 0,
            },
            timestamp = now,
        };
    }
}
