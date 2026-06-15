using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemLogsController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;

    public SystemLogsController(Hl7GatewayDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] byte? level,
        [FromQuery] string? category,
        [FromQuery] string? keyword,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.SystemLogs.AsNoTracking();

        if (level.HasValue)
            query = query.Where(l => l.Level == level.Value);
        if (!string.IsNullOrEmpty(category))
            query = query.Where(l => l.Category == category);
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(l =>
                l.Message.Contains(keyword) ||
                (l.Category != null && l.Category.Contains(keyword)) ||
                (l.StackTrace != null && l.StackTrace.Contains(keyword)));
        if (from.HasValue)
            query = query.Where(l => l.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(l => l.CreatedAt <= to.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpDelete]
    public async Task<IActionResult> ClearLogs([FromQuery] DateTime? before)
    {
        _db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

        if (before.HasValue)
        {
            await _db.SystemLogs.Where(l => l.CreatedAt < before.Value).ExecuteDeleteAsync();
        }
        else
        {
            var isSqlServer = _db.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer";
            if (isSqlServer)
                await _db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE [dbo].[SystemLogs]");
            else
                await _db.Database.ExecuteSqlRawAsync("DELETE FROM [SystemLogs]");
        }
        return Ok(new { message = "cleared" });
    }
}
