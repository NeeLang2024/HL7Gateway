using HL7Gateway.Core;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Services.Implementations;
using HL7Gateway.Core.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Service.Services;

public class MessageProcessorService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHl7ParserService _parserService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IAdtSenderService _adtSender;
    private readonly AutoAdtHisBindingSync _hisBindingSync;
    private readonly ILogger<MessageProcessorService> _logger;

    public MessageProcessorService(
        IServiceScopeFactory scopeFactory,
        IHl7ParserService parserService,
        IEventPublisher eventPublisher,
        IAdtSenderService adtSender,
        AutoAdtHisBindingSync hisBindingSync,
        ILogger<MessageProcessorService> logger)
    {
        _scopeFactory = scopeFactory;
        _parserService = parserService;
        _eventPublisher = eventPublisher;
        _adtSender = adtSender;
        _hisBindingSync = hisBindingSync;
        _logger = logger;
    }

    public async Task ProcessReceivedMessage(MllpMessageReceivedEventArgs e, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();

        var message = new Hl7Message
        {
            MessageControlId = ExtractControlId(e.RawMessage),
            MessageType = ExtractMessageType(e.RawMessage),
            TriggerEvent = ExtractTriggerEvent(e.RawMessage),
            SourceIp = e.SourceIp,
            SourcePort = e.SourcePort,
            RawContent = e.RawMessage,
            ReceivedAt = e.ReceivedAt,
            ParseStatus = 0,
        };

        var mshFields = GetMshFields(e.RawMessage);
        if (mshFields.Length > 11)
        {
            var verParts = mshFields[11].Split('^');
            message.VersionId = verParts[0];
        }
        if (mshFields.Length > 2) message.SendingApp = mshFields[2];
        if (mshFields.Length > 3) message.SendingFacility = mshFields[3];
        if (mshFields.Length > 5) message.ReceivingApp = mshFields[5];
        if (mshFields.Length > 6) message.ReceivingFacility = mshFields[6];
        if (mshFields.Length > 6 && DateTime.TryParseExact(mshFields[6], "yyyyMMddHHmmss", null,
                System.Globalization.DateTimeStyles.None, out var msgDt))
            message.MessageDateTime = msgDt;

        ExtractPatientAndVisit(e.RawMessage, message);

        var exists = await db.Hl7Messages.AnyAsync(m => m.MessageControlId == message.MessageControlId, ct);
        if (exists)
        {
            _logger.LogWarning("Duplicate message {ControlId}, skipped", message.MessageControlId);
            e.AckMessage = BuildAck(message.MessageControlId, "AR");
            return;
        }

        _logger.LogDebug("Saving message {ControlId}: RawContent.Length={RawLen}, MsgType={Type}, Trigger={Trigger}",
            message.MessageControlId, e.RawMessage?.Length ?? -1, message.MessageType, message.TriggerEvent);

        db.Hl7Messages.Add(message);
        await db.SaveChangesAsync(ct);

        List<SegmentParseResult> segments = [];
        List<Observation> observations = [];
        List<VitalSign> vitalSigns = [];
        List<Patient> patients = [];
        List<Visit> visits = [];
        string? error = null;
        var success = false;

        try
        {
            var mappings = await db.IdentifierMappings.Where(m => m.IsActive).ToListAsync(ct);

            (success, error, segments, observations, vitalSigns, patients, visits) = ParseWithResult(message, mappings);

            if (string.IsNullOrEmpty(message.VisitId) && !string.IsNullOrEmpty(message.PatientId))
            {
                var latest = await db.Visits
                    .Where(v => v.PatientId == message.PatientId)
                    .OrderByDescending(v => v.AdmitDateTime)
                    .FirstOrDefaultAsync(ct);
                if (latest is not null)
                {
                    message.VisitId = latest.VisitId;
                    foreach (var vs in vitalSigns)
                        vs.VisitId = latest.VisitId;
                }
            }

            if (success)
            {
                message.ParseStatus = 1;
                message.ProcessedAt = ChinaTime.Now;

                foreach (var seg in segments)
                {
                    db.ParsedSegments.Add(new ParsedSegment
                    {
                        MessageId = message.MessageId,
                        SegmentType = seg.SegmentType,
                        SegmentIndex = seg.SegmentIndex,
                        SegmentRaw = seg.Raw,
                    });
                }

                foreach (var obs in observations)
                    db.Observations.Add(obs);

                foreach (var vs in vitalSigns)
                    db.VitalSigns.Add(vs);

                foreach (var p in patients)
                {
                    var existing = await db.Patients.FindAsync([p.PatientId], ct);
                    if (existing is null)
                    {
                        db.Patients.Add(p);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(p.Name) && !p.Name.Contains('?') && !p.Name.Contains('\uFFFD'))
                            existing.Name = p.Name;
                        if (p.DateOfBirth.HasValue) existing.DateOfBirth = p.DateOfBirth;
                        if (!string.IsNullOrEmpty(p.Gender) && !p.Gender.Contains('?')) existing.Gender = p.Gender;
                        if (!string.IsNullOrEmpty(p.Address) && !p.Address.Contains('?')) existing.Address = p.Address;
                        if (!string.IsNullOrEmpty(p.PhoneNumber) && !p.PhoneNumber.Contains('?')) existing.PhoneNumber = p.PhoneNumber;
                    }
                }

                foreach (var v in visits)
                {
                    var existing = await db.Visits.FindAsync([v.VisitId], ct);
                    if (existing is null)
                    {
                        v.CreatedAt = ChinaTime.Now;
                        v.UpdatedAt = ChinaTime.Now;
                        db.Visits.Add(v);
                    }
                    else if (!string.IsNullOrEmpty(v.Bed) || !string.IsNullOrEmpty(v.Department) || !string.IsNullOrEmpty(v.Ward) || !string.IsNullOrEmpty(v.Room))
                    {
                        if (!string.IsNullOrEmpty(v.Department) && !v.Department.Contains('?')) existing.Department = v.Department;
                        if (!string.IsNullOrEmpty(v.Ward) && !v.Ward.Contains('?')) existing.Ward = v.Ward;
                        if (!string.IsNullOrEmpty(v.Room) && !v.Room.Contains('?')) existing.Room = v.Room;
                        if (!string.IsNullOrEmpty(v.Bed) && !v.Bed.Contains('?')) existing.Bed = v.Bed;
                        if (!string.IsNullOrEmpty(v.AttendingDoctor) && !v.AttendingDoctor.Contains('?')) existing.AttendingDoctor = v.AttendingDoctor;
                        if (!string.IsNullOrEmpty(v.PatientClass) && !v.PatientClass.Contains('?')) existing.PatientClass = v.PatientClass;
                        if (!string.IsNullOrEmpty(v.PatientType) && !v.PatientType.Contains('?')) existing.PatientType = v.PatientType;
                        if (v.AdmitDateTime.HasValue) existing.AdmitDateTime = v.AdmitDateTime;
                        existing.UpdatedAt = ChinaTime.Now;
                    }
                }
            }
            else
            {
                message.ParseStatus = 2;
                message.ErrorMessage = error;
                _logger.LogWarning("Parse failed for {ControlId}: {Error}", message.MessageControlId, error);
            }

            _logger.LogInformation("Message {ControlId} saved: ParseStatus={Status}, Seg={SegCount}, Obs={ObsCount}, Vitals={VitalCount}, Visits={VisitCount}, RawLen={RawLen}",
                message.MessageControlId, message.ParseStatus,
                segments.Count, observations.Count, vitalSigns.Count, visits.Count,
                message.RawContent?.Length ?? -1);

            // Auto-enqueue ADT messages for forwarding
            if (success && message.MessageType == "ADT" && !string.IsNullOrEmpty(message.TriggerEvent) && message.RawContent is not null)
            {
                try { await _adtSender.EnqueueAsync(message.TriggerEvent, message.RawContent, "Auto:MLLP", 1); }
                catch (Exception adtEx) { _logger.LogWarning(adtEx, "Failed to auto-enqueue ADT {Type}", message.TriggerEvent); }

                try
                {
                    await _hisBindingSync.TrySyncFromHl7Async(
                        db, message.RawContent, message.TriggerEvent, message.PatientId, message.VisitId, ct);
                }
                catch (Exception bindEx)
                {
                    _logger.LogWarning(bindEx, "HIS Auto ADT binding sync failed for {Type}", message.TriggerEvent);
                }
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message {ControlId}", message.MessageControlId);
            try
            {
                using var errorScope = _scopeFactory.CreateScope();
                var errorDb = errorScope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();
                var msg = await errorDb.Hl7Messages.FindAsync([message.MessageId], ct);
                if (msg is not null)
                {
                    msg.ParseStatus = 2;
                    msg.ErrorMessage = $"Processing failed: {ex.Message}";
                    await errorDb.SaveChangesAsync(ct);
                }
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to save error status for message {ControlId}", message.MessageControlId);
            }
        }

        await UpdateDeviceConnection(db, e, ct);

        await _eventPublisher.PublishMessageReceived(message, ct);

        _logger.LogInformation("Processed message {ControlId} ({Type}) from {Ip}:{Port}",
            message.MessageControlId, message.MessageType, e.SourceIp, e.SourcePort);
    }

    private (bool Success, string? Error, List<SegmentParseResult> Segments, List<Observation> Observations, List<VitalSign> VitalSigns, List<Patient> Patients, List<Visit> Visits)
        ParseWithResult(Hl7Message message, List<IdentifierMapping> mappings)
    {
        var (success, error) = _parserService.ParseMessage(
            message, out var segments, out var observations, out var vitalSigns, out var patients, out var visits, mappings);
        return (success, error, segments, observations, vitalSigns, patients, visits);
    }

    private static void ExtractPatientAndVisit(string raw, Hl7Message message)
    {
        var lines = raw.Split('\r');
        foreach (var line in lines)
        {
            if (line.StartsWith("PID|"))
            {
                var fields = line.Split('|');
                if (fields.Length > 3)
                {
                    var pidParts = fields[3].Split('^');
                    var rawId = pidParts.Length > 0 ? pidParts[0] : null;
                    message.PatientId = rawId?.Trim('"') is { Length: > 0 } tidied ? tidied : null;
                }
                if (fields.Length > 19 && string.IsNullOrEmpty(message.PatientId))
                {
                    var altParts = fields[19].Split('^');
                    var rawAlt = altParts.Length > 0 ? altParts[0] : fields[19];
                    message.PatientId = rawAlt?.Trim('"') is { Length: > 0 } tidied ? tidied : null;
                }
            }
            if (line.StartsWith("PV1|"))
            {
                var fields = line.Split('|');
                if (fields.Length > 19)
                {
                    var visitParts = fields[19].Split('^');
                    message.VisitId = visitParts.Length > 0 ? visitParts[0] : fields[19];
                }
                // PV1-3 assigned patient location. Captured even with no patient so that
                // monitor/ORU data from an unassigned bed remains identifiable.
                if (fields.Length > 3 && !string.IsNullOrWhiteSpace(fields[3]))
                    message.PatientLocation = FormatLocation(fields[3]);
            }
        }
    }

    private static string ExtractControlId(string raw)
    {
        var msh = GetMshFields(raw);
        if (msh.Length > 9)
        {
            var parts = msh[9].Split('^');
            var id = parts.Length > 0 ? parts[0]?.Trim() : null;
            // A blank MSH-10 (common for monitors on an empty/unassigned bed) must NOT collapse
            // into the same "" control id, otherwise every such ORU after the first is treated as
            // a duplicate and silently dropped. Fall back to a unique id instead.
            return string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        }
        return Guid.NewGuid().ToString("N");
    }

    private static string ExtractMessageType(string raw)
    {
        var msh = GetMshFields(raw);
        if (msh.Length > 8)
        {
            var parts = msh[8].Split('^');
            return parts.Length > 0 ? parts[0] : "UNKNOWN";
        }
        return "UNKNOWN";
    }

    private static string ExtractTriggerEvent(string raw)
    {
        var msh = GetMshFields(raw);
        if (msh.Length > 8)
        {
            var parts = msh[8].Split('^');
            return parts.Length > 1 ? parts[1] : null!;
        }
        return null!;
    }

    private static string[] GetMshFields(string raw)
    {
        var firstLine = raw.Split('\r')[0];
        return firstLine.Split('|');
    }

    /// <summary>
    /// Turn a raw PV1-3 location field (e.g. "ICU^^Bed4^ROOM01") into a compact readable
    /// string ("ICU / Bed4 / ROOM01"), dropping empty components and HL7 sub-components.
    /// </summary>
    private static string FormatLocation(string raw)
    {
        var parts = raw.Split('^')
            .Select(p => p.Split('&')[0].Trim())
            .Where(p => !string.IsNullOrEmpty(p));
        var text = string.Join(" / ", parts);
        if (string.IsNullOrWhiteSpace(text))
            text = raw.Trim();
        return text.Length > 200 ? text[..200] : text;
    }

    private static async Task UpdateDeviceConnection(Hl7GatewayDbContext db, MllpMessageReceivedEventArgs e, CancellationToken ct)
    {
        var conn = await db.DeviceConnections
            .Where(d => d.SourceIp == e.SourceIp)
            .OrderByDescending(d => d.LastActivity)
            .FirstOrDefaultAsync(ct);

        if (conn is null)
        {
            conn = new DeviceConnection
            {
                SourceIp = e.SourceIp,
                SourcePort = e.SourcePort,
                IsConnected = true,
                MessageCount = 1,
                FirstConnected = ChinaTime.Now,
                LastActivity = ChinaTime.Now,
            };
            db.DeviceConnections.Add(conn);
        }
        else
        {
            conn.SourcePort = e.SourcePort;
            conn.MessageCount++;
            conn.LastActivity = ChinaTime.Now;
            conn.IsConnected = true;
            conn.DisconnectedAt = null;

            // Clean up stale duplicate records for the same IP
            var stale = await db.DeviceConnections
                .Where(d => d.SourceIp == e.SourceIp && d.ConnectionId != conn.ConnectionId)
                .ToListAsync(ct);
            if (stale.Count > 0)
            {
                db.DeviceConnections.RemoveRange(stale);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static string BuildAck(string controlId, string ackCode)
    {
        return $"MSH|^~\\&|||||{ChinaTime.Now:yyyyMMddHHmmss}||ACK|{Guid.NewGuid():N}|P|2.4\rMSA|{ackCode}|{controlId}";
    }
}
