using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HL7Gateway.Core.DbContexts;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/export")]
public class ExportController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;

    public ExportController(Hl7GatewayDbContext db) => _db = db;

    [HttpGet("messages")]
    public async Task<IActionResult> ExportMessages(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var query = _db.Hl7Messages.AsQueryable();
        if (from.HasValue) query = query.Where(m => m.ReceivedAt >= from.Value);
        if (to.HasValue) query = query.Where(m => m.ReceivedAt <= to.Value);

        var items = await query.OrderByDescending(m => m.ReceivedAt)
            .Select(m => new
            {
                m.MessageId, m.MessageControlId, m.MessageType, m.TriggerEvent,
                m.VersionId, m.SendingApp, m.SendingFacility, m.PatientId,
                m.SourceIp, m.ParseStatus, m.ErrorMessage, m.ReceivedAt
            }).ToListAsync();

        var csv = new StringBuilder();
        csv.AppendLine("MessageId,ControlId,Type,TriggerEvent,Version,SendingApp,SendingFacility,PatientId,SourceIp,ParseStatus,ErrorMessage,ReceivedAt");
        foreach (var m in items)
            csv.AppendLine($"{m.MessageId},{Escape(m.MessageControlId)},{Escape(m.MessageType)},{Escape(m.TriggerEvent)},{Escape(m.VersionId)},{Escape(m.SendingApp)},{Escape(m.SendingFacility)},{Escape(m.PatientId)},{Escape(m.SourceIp)},{m.ParseStatus},{Escape(m.ErrorMessage)},{m.ReceivedAt:O}");

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"messages_{DateTime.Now:yyyyMMdd}.csv");
    }

    [HttpGet("vitals")]
    public async Task<IActionResult> ExportVitals(
        [FromQuery] string? patientId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var query = _db.VitalSigns.AsQueryable();
        if (!string.IsNullOrEmpty(patientId)) query = query.Where(v => v.PatientId == patientId);
        if (from.HasValue) query = query.Where(v => v.ObservationDateTime >= from.Value);
        if (to.HasValue) query = query.Where(v => v.ObservationDateTime <= to.Value);

        var items = await query.OrderByDescending(v => v.ObservationDateTime)
            .Select(v => new
            {
                v.VitalSignId, v.PatientId, v.VisitId, v.VitalSignType,
                v.VitalSignName, v.ValueNumeric, v.ValueString, v.Units,
                v.Systolic, v.Diastolic, v.MeanPressure, v.ObservationDateTime
            }).ToListAsync();

        var csv = new StringBuilder();
        csv.AppendLine("VitalSignId,PatientId,VisitId,Type,Name,ValueNumeric,ValueString,Units,Systolic,Diastolic,MeanPressure,ObsDateTime");
        foreach (var v in items)
            csv.AppendLine($"{v.VitalSignId},{Escape(v.PatientId)},{Escape(v.VisitId)},{Escape(v.VitalSignType)},{Escape(v.VitalSignName)},{v.ValueNumeric},{Escape(v.ValueString)},{Escape(v.Units)},{v.Systolic},{v.Diastolic},{v.MeanPressure},{v.ObservationDateTime:O}");

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"vitals_{DateTime.Now:yyyyMMdd}.csv");
    }

    [HttpGet("adt")]
    public async Task<IActionResult> ExportAdtLogs()
    {
        var items = await _db.AdtLogs.OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.LogId, a.MessageType, a.PatientId, a.Status, a.ErrorMessage, a.CreatedAt })
            .ToListAsync();

        var csv = new StringBuilder();
        csv.AppendLine("LogId,MessageType,PatientId,Status,ErrorMessage,CreatedAt");
        foreach (var a in items)
            csv.AppendLine($"{a.LogId},{Escape(a.MessageType)},{Escape(a.PatientId)},{a.Status},{Escape(a.ErrorMessage)},{a.CreatedAt:O}");

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"adt_logs_{DateTime.Now:yyyyMMdd}.csv");
    }

    private static string Escape(string? s) =>
        s is null ? "" : $"\"{s.Replace("\"", "\"\"")}\"";
}
