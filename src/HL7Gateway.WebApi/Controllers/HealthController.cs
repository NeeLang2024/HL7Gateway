using HL7Gateway.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HL7Gateway.Core.DbContexts;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;

    public HealthController(Hl7GatewayDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var dbOk = false;
        try
        {
            dbOk = await _db.Database.CanConnectAsync();
        }
        catch { }

        return Ok(new
        {
            status = dbOk ? "healthy" : "degraded",
            timestamp = ChinaTime.Now,
            database = dbOk ? "connected" : "unreachable"
        });
    }
}
