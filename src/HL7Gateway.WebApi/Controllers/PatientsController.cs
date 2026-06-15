using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;

    public PatientsController(Hl7GatewayDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.Patients.AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(p =>
                p.PatientId.Contains(search) ||
                p.Name!.Contains(search));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.PatientId,
                p.PatientIdList,
                p.Name,
                p.DateOfBirth,
                p.Gender,
                p.Address,
                p.PhoneNumber,
                p.Ssn,
                p.Race,
                p.MaritalStatus,
                p.CreatedAt,
                p.UpdatedAt,
                CurrentBed = _db.Visits
                    .Where(v => v.PatientId == p.PatientId && v.DischargeDateTime == null)
                    .OrderByDescending(v => v.AdmitDateTime)
                    .Select(v => v.Bed)
                    .FirstOrDefault(),
                CurrentWard = _db.Visits
                    .Where(v => v.PatientId == p.PatientId && v.DischargeDateTime == null)
                    .OrderByDescending(v => v.AdmitDateTime)
                    .Select(v => v.Ward)
                    .FirstOrDefault(),
                CurrentDepartment = _db.Visits
                    .Where(v => v.PatientId == p.PatientId && v.DischargeDateTime == null)
                    .OrderByDescending(v => v.AdmitDateTime)
                    .Select(v => v.Department)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var patient = await _db.Patients.FindAsync(id);
        if (patient is null) return NotFound();
        return Ok(patient);
    }

    [HttpGet("{id}/visits")]
    public async Task<IActionResult> GetVisits(string id)
    {
        var visits = await _db.Visits
            .Where(v => v.PatientId == id)
            .OrderByDescending(v => v.AdmitDateTime)
            .ToListAsync();
        return Ok(visits);
    }
}
