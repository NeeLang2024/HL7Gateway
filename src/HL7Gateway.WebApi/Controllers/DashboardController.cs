using HL7Gateway.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using HL7Gateway.Core.DbContexts;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;
    private readonly IMemoryCache _cache;

    public DashboardController(Hl7GatewayDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboard()
    {
        var payload = await _cache.GetOrCreateAsync("dashboard:stats", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await BuildDashboardAsync();
        });

        return Ok(payload);
    }

    private async Task<object> BuildDashboardAsync()
    {
        var now = ChinaTime.Now;
        var todayStart = now.Date;
        var oneHourAgo = now.AddHours(-1);
        var fiveMinAgo = now.AddMinutes(-5);
        var dayAgo = now.AddHours(-24);

        // 总消息数全表 COUNT 极慢，单独长缓存
        var totalMessages = await _cache.GetOrCreateAsync("dashboard:totalMessages", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await _db.Hl7Messages.AsNoTracking().CountAsync();
        });

        // 只扫近 25 小时数据算速率/今日/失败，避免全表 GroupBy
        var windowStart = todayStart.AddHours(-1);
        var msgStats = await _db.Hl7Messages
            .AsNoTracking()
            .Where(m => m.ReceivedAt >= windowStart)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Today = g.Count(m => m.ReceivedAt >= todayStart),
                ParseFailures24h = g.Count(m => m.ParseStatus == 2 && m.ReceivedAt >= dayAgo),
                LastHour = g.Count(m => m.ReceivedAt >= oneHourAgo),
                Last5Min = g.Count(m => m.ReceivedAt >= fiveMinAgo),
            })
            .FirstOrDefaultAsync();

        var todayMessages = msgStats?.Today ?? 0;
        var parseFailures24h = msgStats?.ParseFailures24h ?? 0;
        var messagesLastHour = msgStats?.LastHour ?? 0;
        var messagesLast5Min = msgStats?.Last5Min ?? 0;

        var connectedDevices = await _db.DeviceConnections.AsNoTracking().CountAsync(d => d.IsConnected);
        var lastMessage = await _db.Hl7Messages
            .AsNoTracking()
            .OrderByDescending(m => m.ReceivedAt)
            .Select(m => new { m.MessageId, m.MessageControlId, m.MessageType, m.PatientId, m.SourceIp, m.ReceivedAt })
            .FirstOrDefaultAsync();
        var todayByType = await _db.Hl7Messages
            .AsNoTracking()
            .Where(m => m.ReceivedAt >= todayStart)
            .GroupBy(m => m.MessageType)
            .Select(g => new { type = g.Key, count = g.Count() })
            .ToListAsync();

        var adtStats = await _db.AdtQueue
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Pending = g.Count(q => q.Status == 0),
                Failed = g.Count(q => q.Status == 3),
            })
            .FirstOrDefaultAsync();

        var recentErrors = await _db.Hl7Messages
            .AsNoTracking()
            .Where(m => m.ParseStatus == 2 && m.ReceivedAt >= dayAgo)
            .OrderByDescending(m => m.ReceivedAt)
            .Take(10)
            .Select(m => new
            {
                m.MessageId,
                m.MessageControlId,
                m.MessageType,
                m.PatientId,
                m.SourceIp,
                m.ReceivedAt,
                m.ErrorMessage
            })
            .ToListAsync();
        var devices = await _db.DeviceConnections
            .AsNoTracking()
            .Where(d => d.IsConnected)
            .OrderByDescending(d => d.LastActivity)
            .Take(8)
            .Select(d => new { d.SourceIp, d.SourcePort, connectedAt = d.FirstConnected, d.LastActivity, d.MessageCount })
            .ToListAsync();

        var messageRatePerMin = Math.Round(messagesLast5Min / 5.0, 2);
        var systemUptime = Environment.TickCount64 / 1000L;

        return new
        {
            totalMessages,
            todayMessages,
            parseFailures = parseFailures24h,
            connectedDevices,
            lastMessage,
            typeCounts = todayByType,
            todayByType,
            wcfSubscriberCount = 0,
            messagesLastHour,
            messagesToday = todayMessages,
            adtQueuePending = adtStats?.Pending ?? 0,
            adtQueueFailed = adtStats?.Failed ?? 0,
            avgProcessingTimeMs = 0,
            avgResponseTimeMs = 0,
            messageRatePerMin,
            messageRate = messageRatePerMin,
            systemUptime,
            uptimeHours = Math.Round(systemUptime / 3600.0, 2),
            recentErrors,
            devices,
            parseFailures24h
        };
    }
}
