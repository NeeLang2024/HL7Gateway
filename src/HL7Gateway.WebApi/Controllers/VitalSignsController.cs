using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HL7Gateway.Core.DbContexts;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VitalSignsController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;

    public VitalSignsController(Hl7GatewayDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetVitalSigns(
        [FromQuery] string? patientId,
        [FromQuery] string? type,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        var query = _db.VitalSigns.AsQueryable();

        if (!string.IsNullOrEmpty(patientId))
            query = query.Where(v => v.PatientId == patientId);
        if (!string.IsNullOrEmpty(type))
            query = query.Where(v => v.VitalSignType == type);
        if (from.HasValue)
            query = query.Where(v => v.ObservationDateTime >= from.Value);
        if (to.HasValue)
            query = query.Where(v => v.ObservationDateTime <= to.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(v => v.ObservationDateTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new
            {
                v.VitalSignId,
                v.PatientId,
                v.VisitId,
                PatientName = _db.Patients.Where(p => p.PatientId == v.PatientId).Select(p => p.Name).FirstOrDefault(),
                Department = _db.Visits.Where(vis => vis.PatientId == v.PatientId).OrderByDescending(vis => vis.AdmitDateTime).Select(vis => vis.Department).FirstOrDefault(),
                Ward = _db.Visits.Where(vis => vis.PatientId == v.PatientId).OrderByDescending(vis => vis.AdmitDateTime).Select(vis => vis.Ward).FirstOrDefault(),
                Room = _db.Visits.Where(vis => vis.PatientId == v.PatientId).OrderByDescending(vis => vis.AdmitDateTime).Select(vis => vis.Room).FirstOrDefault(),
                Bed = _db.Visits.Where(vis => vis.PatientId == v.PatientId).OrderByDescending(vis => vis.AdmitDateTime).Select(vis => vis.Bed).FirstOrDefault(),
                v.VitalSignType,
                v.VitalSignName,
                v.ValueNumeric,
                v.ValueString,
                v.Systolic,
                v.Diastolic,
                v.MeanPressure,
                v.Units,
                v.AbnormalFlags,
                v.ReferenceRange,
                v.ObservationDateTime,
                v.DeviceId
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("trends")]
    public async Task<IActionResult> GetTrends(
        [FromQuery] string? patientId,
        [FromQuery] string? type,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int pointCount = 120)
    {
        if (string.IsNullOrEmpty(patientId) || string.IsNullOrEmpty(type))
            return BadRequest(new { message = "patientId 和 type 为必填" });

        var query = _db.VitalSigns.AsQueryable();
        query = query.Where(v => v.PatientId == patientId && v.VitalSignType == type);
        if (from.HasValue) query = query.Where(v => v.ObservationDateTime >= from.Value);
        if (to.HasValue) query = query.Where(v => v.ObservationDateTime <= to.Value);

        var totalCount = await query.CountAsync();
        if (totalCount == 0)
            return Ok(new { type, patientId, from, to, points = new List<object>() });

        var step = Math.Max(1, totalCount / pointCount);
        var raw = await query
            .OrderBy(v => v.ObservationDateTime)
            .Select(v => new { v.ObservationDateTime, v.ValueNumeric, v.VitalSignName, v.Units })
            .ToListAsync();

        var points = raw.Where((_, i) => i % step == 0).Select(v => new
        {
            t = v.ObservationDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            v.ValueNumeric,
        }).ToList();

        if (!points.Any() && raw.Any())
        {
            points = raw.Take(1).Select(v => new
            {
                t = v.ObservationDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                v.ValueNumeric,
            }).ToList();
        }

        var info = raw.First();
        return Ok(new
        {
            type,
            vitalSignName = info.VitalSignName,
            units = info.Units,
            patientId,
            from,
            to,
            points
        });
    }
}
