using HL7Gateway.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MessagesController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;

    public MessagesController(Hl7GatewayDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetMessages(
        [FromQuery] string? patientId,
        [FromQuery] string? messageType,
        [FromQuery] string? sourceIp,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.Hl7Messages.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(patientId))
            query = query.Where(m => m.PatientId == patientId);
        if (!string.IsNullOrEmpty(messageType))
            query = query.Where(m => m.MessageType == messageType);
        if (!string.IsNullOrEmpty(sourceIp))
            query = query.Where(m => m.SourceIp == sourceIp);
        if (from.HasValue)
            query = query.Where(m => m.ReceivedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(m => m.ReceivedAt <= to.Value);

        // 无筛选时默认只看近 30 天，避免 HL7Messages 全表 COUNT 卡死
        var hasFilter = !string.IsNullOrEmpty(patientId)
            || !string.IsNullOrEmpty(messageType)
            || !string.IsNullOrEmpty(sourceIp)
            || from.HasValue
            || to.HasValue;
        if (!hasFilter)
            query = query.Where(m => m.ReceivedAt >= ChinaTime.Now.AddDays(-30));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(m => m.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.MessageId,
                m.MessageControlId,
                m.MessageType,
                m.TriggerEvent,
                m.SendingApp,
                m.SendingFacility,
                m.SourceIp,
                m.PatientId,
                m.PatientLocation,
                m.ParseStatus,
                m.ReceivedAt,
                m.ErrorMessage
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetMessage(long id)
    {
        var msg = await _db.Hl7Messages.FindAsync(id);
        if (msg is null) return NotFound();

        var segments = await _db.ParsedSegments
            .Where(s => s.MessageId == id)
            .OrderBy(s => s.SegmentIndex)
            .Select(s => new
            {
                s.SegmentId,
                s.MessageId,
                s.SegmentType,
                s.SegmentIndex,
                s.SegmentRaw,
                s.JsonContent
            })
            .ToListAsync();

        var observations = await _db.Observations
            .Where(o => o.MessageId == id)
            .Select(o => new
            {
                o.ObservationId,
                o.MessageId,
                o.PatientId,
                o.SetId,
                o.ValueType,
                o.IdentifierCode,
                o.IdentifierText,
                o.IdentifierSystem,
                o.ObservationValue,
                o.Units,
                o.ReferenceRange,
                o.AbnormalFlags,
                o.ObservationDateTime,
                o.ProducerId,
                o.ObserveStatus,
                o.CreatedAt
            })
            .ToListAsync();

        var vitalSigns = await _db.VitalSigns
            .Where(v => v.MessageId == id)
            .Select(v => new
            {
                v.VitalSignId,
                v.MessageId,
                v.ObservationId,
                v.PatientId,
                v.VisitId,
                v.VitalSignType,
                v.VitalSignName,
                v.ValueNumeric,
                v.ValueString,
                v.Units,
                v.Systolic,
                v.Diastolic,
                v.MeanPressure,
                v.OriginalCode,
                v.OriginalText,
                v.OriginalSystem,
                v.AbnormalFlags,
                v.ReferenceRange,
                v.ObserveStatus,
                v.ObservationDateTime,
                v.ReceivedAt,
                v.DeviceId
            })
            .ToListAsync();

        return Ok(new
        {
            message = new
            {
                msg.MessageId,
                msg.MessageControlId,
                msg.MessageType,
                msg.TriggerEvent,
                msg.VersionId,
                msg.SendingApp,
                msg.SendingFacility,
                msg.ReceivingApp,
                msg.ReceivingFacility,
                msg.MessageDateTime,
                msg.SourceIp,
                msg.SourcePort,
                msg.ParseStatus,
                msg.PatientId,
                msg.VisitId,
                msg.PatientLocation,
                msg.ReceivedAt,
                msg.ProcessedAt,
                msg.ErrorMessage
            },
            segments,
            observations,
            vitalSigns
        });
    }

    [HttpGet("{id:long}/raw")]
    public async Task<IActionResult> GetRawMessage(long id)
    {
        var msg = await _db.Hl7Messages.FindAsync(id);
        if (msg is null) return NotFound();

        return Ok(new { raw = msg.RawContent });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeleteMessage(long id)
    {
        var msg = await _db.Hl7Messages.FindAsync(id);
        if (msg is null) return NotFound();

        _db.Hl7Messages.Remove(msg);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:long}/reparse")]
    public async Task<IActionResult> ReparseMessage(long id)
    {
        var msg = await _db.Hl7Messages.FindAsync(id);
        if (msg is null) return NotFound();
        if (msg.ParseStatus != 2)
            return BadRequest(new { message = "只有解析失败的消息可以重解析" });

        msg.ParseStatus = 0;
        msg.ErrorMessage = null;
        await _db.SaveChangesAsync();
        return Ok(new { message = "已重置为待解析状态，等待服务端重新处理" });
    }

    /// <summary>
    /// Full-text search on message raw content.
    /// GET /api/messages/search?q=keyword&amp;page=1&amp;pageSize=20
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchMessages(
        [FromQuery] string q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Search keyword 'q' is required" });

        var query = _db.Hl7Messages
            .Where(m => m.RawContent.Contains(q));

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(m => m.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.MessageId,
                m.MessageControlId,
                m.MessageType,
                m.TriggerEvent,
                m.SendingApp,
                m.SendingFacility,
                m.SourceIp,
                m.PatientId,
                m.PatientLocation,
                m.ParseStatus,
                m.ReceivedAt,
                m.ErrorMessage,
                snippet = m.RawContent.Substring(0, Math.Min(200, m.RawContent.Length))
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    /// <summary>
    /// Compare two messages side by side.
    /// GET /api/messages/compare?id1={id}&amp;id2={id}
    /// </summary>
    [HttpGet("compare")]
    public async Task<IActionResult> CompareMessages(
        [FromQuery] long id1,
        [FromQuery] long id2)
    {
        var msg1 = await _db.Hl7Messages.FindAsync(id1);
        var msg2 = await _db.Hl7Messages.FindAsync(id2);

        if (msg1 is null || msg2 is null)
            return NotFound(new { error = "One or both messages not found" });

        var seg1 = await _db.ParsedSegments
            .Where(s => s.MessageId == id1)
            .OrderBy(s => s.SegmentIndex)
            .Select(s => new
            {
                s.SegmentId,
                s.SegmentType,
                s.SegmentIndex,
                s.SegmentRaw,
                s.JsonContent
            })
            .ToListAsync();

        var seg2 = await _db.ParsedSegments
            .Where(s => s.MessageId == id2)
            .OrderBy(s => s.SegmentIndex)
            .Select(s => new
            {
                s.SegmentId,
                s.SegmentType,
                s.SegmentIndex,
                s.SegmentRaw,
                s.JsonContent
            })
            .ToListAsync();

        var obs1 = await _db.Observations
            .Where(o => o.MessageId == id1)
            .Select(o => new
            {
                o.ObservationId,
                o.PatientId,
                o.SetId,
                o.ValueType,
                o.IdentifierCode,
                o.IdentifierText,
                o.IdentifierSystem,
                o.ObservationValue,
                o.Units,
                o.ReferenceRange,
                o.AbnormalFlags,
                o.ObservationDateTime,
                o.ProducerId,
                o.ObserveStatus
            })
            .ToListAsync();

        var obs2 = await _db.Observations
            .Where(o => o.MessageId == id2)
            .Select(o => new
            {
                o.ObservationId,
                o.PatientId,
                o.SetId,
                o.ValueType,
                o.IdentifierCode,
                o.IdentifierText,
                o.IdentifierSystem,
                o.ObservationValue,
                o.Units,
                o.ReferenceRange,
                o.AbnormalFlags,
                o.ObservationDateTime,
                o.ProducerId,
                o.ObserveStatus
            })
            .ToListAsync();

        return Ok(new
        {
            message1 = new
            {
                msg1.MessageId,
                msg1.MessageControlId,
                msg1.MessageType,
                msg1.TriggerEvent,
                msg1.VersionId,
                msg1.SendingApp,
                msg1.SendingFacility,
                msg1.ReceivingApp,
                msg1.ReceivingFacility,
                msg1.MessageDateTime,
                msg1.SourceIp,
                msg1.SourcePort,
                msg1.ParseStatus,
                msg1.PatientId,
                msg1.VisitId,
                msg1.ReceivedAt,
                msg1.ProcessedAt,
                msg1.ErrorMessage,
                rawContent = msg1.RawContent,
                segments = seg1,
                observations = obs1
            },
            message2 = new
            {
                msg2.MessageId,
                msg2.MessageControlId,
                msg2.MessageType,
                msg2.TriggerEvent,
                msg2.VersionId,
                msg2.SendingApp,
                msg2.SendingFacility,
                msg2.ReceivingApp,
                msg2.ReceivingFacility,
                msg2.MessageDateTime,
                msg2.SourceIp,
                msg2.SourcePort,
                msg2.ParseStatus,
                msg2.PatientId,
                msg2.VisitId,
                msg2.ReceivedAt,
                msg2.ProcessedAt,
                msg2.ErrorMessage,
                rawContent = msg2.RawContent,
                segments = seg2,
                observations = obs2
            }
        });
    }
}
