using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using HL7Gateway.Core;
using HL7Gateway.Core.DbContexts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MonitorController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;
    private readonly IMemoryCache _cache;

    public MonitorController(Hl7GatewayDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    [HttpGet]
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var payload = await _cache.GetOrCreateAsync("monitor:stats", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await BuildStatsAsync();
        });
        return Ok(payload);
    }

    private async Task<object> BuildStatsAsync()
    {
        var proc = Process.GetCurrentProcess();
        var now = ChinaTime.Now;
        var fiveMinAgo = now.AddMinutes(-5);
        var oneHourAgo = now.AddHours(-1);
        var twentyFourHoursAgo = now.AddHours(-24);

        var cpuTime = proc.TotalProcessorTime;
        var workingSet = proc.WorkingSet64;
        var uptime = now - proc.StartTime;
        var gcInfo = GC.GetGCMemoryInfo();
        var totalAvailableMb = gcInfo.TotalAvailableMemoryBytes > 0
            ? Math.Round(gcInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0, 2)
            : 0;
        var usedMemoryMb = Math.Round(workingSet / 1024.0 / 1024.0, 2);
        var memoryPercent = totalAvailableMb > 0
            ? Math.Round(usedMemoryMb / totalAvailableMb * 100, 1)
            : (double?)null;

        var msgCounts = await _db.Hl7Messages
            .AsNoTracking()
            .Where(m => m.ReceivedAt >= twentyFourHoursAgo)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Last5Minutes = g.Count(m => m.ReceivedAt >= fiveMinAgo),
                Last1Hour = g.Count(m => m.ReceivedAt >= oneHourAgo),
                Last24Hours = g.Count(),
            })
            .FirstOrDefaultAsync();

        var connectedDevices = await _db.DeviceConnections
            .AsNoTracking()
            .CountAsync(d => d.IsConnected);

        return new
        {
            timestamp = now,
            cpuPercent = (double?)null,
            memoryPercent,
            usedMemoryMB = usedMemoryMb,
            totalMemoryMB = totalAvailableMb > 0 ? totalAvailableMb : (double?)null,
            availableMemoryMB = totalAvailableMb > 0 ? Math.Max(0, totalAvailableMb - usedMemoryMb) : (double?)null,
            uptime = Math.Round(uptime.TotalHours, 2),
            processName = proc.ProcessName,
            processId = proc.Id,
            process = new
            {
                cpuTime = cpuTime.ToString(),
                cpuTimeMilliseconds = cpuTime.TotalMilliseconds,
                workingSetBytes = workingSet,
                workingSetMb = Math.Round(workingSet / 1024.0 / 1024.0, 2)
            },
            messages = new
            {
                last5Minutes = msgCounts?.Last5Minutes ?? 0,
                last1Hour = msgCounts?.Last1Hour ?? 0,
                last24Hours = msgCounts?.Last24Hours ?? 0
            },
            connectedDevices,
            serviceStatuses = GetServiceStatuses()
        };
    }

    private static object[] GetServiceStatuses()
    {
        if (!OperatingSystem.IsWindows()) return [];
        return GetWindowsServiceStatuses();
    }

    [SupportedOSPlatform("windows")]
    private static object[] GetWindowsServiceStatuses()
    {
        var names = new[] { "HL7GatewayService", "HL7GatewayWebApi", "PhilipsHifBridge" };
        return names.Select(name =>
        {
            try
            {
                using var controller = new ServiceController(name);
                return new
                {
                    name,
                    status = controller.Status.ToString(),
                    canStop = controller.CanStop
                } as object;
            }
            catch (Exception ex)
            {
                return new
                {
                    name,
                    status = "Unknown",
                    error = ex.Message
                } as object;
            }
        }).ToArray();
    }
}
