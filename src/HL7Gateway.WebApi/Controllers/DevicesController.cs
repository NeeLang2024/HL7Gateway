using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;

    public DevicesController(Hl7GatewayDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetDevices([FromQuery] bool all = false)
    {
        var query = _db.DeviceConnections.AsQueryable();
        if (!all)
            query = query.Where(d => d.IsConnected);
        var devices = await query
            .OrderByDescending(d => d.LastActivity)
            .ToListAsync();
        return Ok(devices);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetDeviceStats([FromQuery] string? sourceIp, [FromQuery] int hours = 24)
    {
        var since = ChinaTime.Now.AddHours(-hours);

        var msgQuery = _db.Hl7Messages.AsQueryable();
        if (!string.IsNullOrEmpty(sourceIp))
            msgQuery = msgQuery.Where(m => m.SourceIp == sourceIp);

        var totalMessages = await msgQuery.CountAsync(m => m.ReceivedAt >= since);
        var successMessages = await msgQuery.CountAsync(m => m.ReceivedAt >= since && m.ParseStatus == 1);
        var failedMessages = await msgQuery.CountAsync(m => m.ReceivedAt >= since && m.ParseStatus == 2);

        var totalObservations = await _db.VitalSigns.CountAsync(o => o.ObservationDateTime >= since);

        var hourlyGroups = await msgQuery
            .Where(m => m.ReceivedAt >= since)
            .GroupBy(m => new { y = m.ReceivedAt.Year, m = m.ReceivedAt.Month, d = m.ReceivedAt.Day, h = m.ReceivedAt.Hour })
            .Select(g => new
            {
                g.Key.y,
                g.Key.m,
                g.Key.d,
                g.Key.h,
                count = g.Count()
            })
            .OrderBy(x => x.y).ThenBy(x => x.m).ThenBy(x => x.d).ThenBy(x => x.h)
            .ToListAsync();

        var hourlyMessages = hourlyGroups.Select(x => new
        {
            hour = $"{x.h:D2}:00",
            date = new DateTime(x.y, x.m, x.d).ToString("yyyy-MM-dd"),
            x.count
        });

        return Ok(new
        {
            since,
            totalMessages,
            successMessages,
            failedMessages,
            totalObservations,
            hourlyMessages
        });
    }
}
