using HL7Gateway.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Services.Interfaces;
using System.Text.Json.Nodes;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ADTController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public ADTController(Hl7GatewayDbContext db, HttpClient http, IConfiguration configuration)
    {
        _db = db;
        _http = http;
        _configuration = configuration;
    }

    [HttpGet("queue")]
    public async Task<IActionResult> GetQueue([FromQuery] byte? status)
    {
        var query = _db.AdtQueue.AsQueryable();
        if (status.HasValue)
            query = query.Where(q => q.Status == status.Value);
        var items = await query.OrderByDescending(q => q.CreatedAt).Take(100).ToListAsync();
        return Ok(items);
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var total = await _db.AdtLogs.CountAsync();
        var items = await _db.AdtLogs
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("bridge-status")]
    public async Task<IActionResult> GetBridgeStatus(CancellationToken ct)
    {
        var baseUrl = ResolveBridgeBaseUrl();
        var url = new Uri(new Uri(baseUrl), "status");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var raw = await _http.GetStringAsync(url, cts.Token);
            var parsed = ParseStatusText(raw);
            var subscriberName = parsed.GetValueOrDefault("name");
            var hasSubscriber = ResolveBridgeSubscriber(parsed);
            return Ok(new
            {
                enabled = ResolveBridgeEnabled(),
                reachable = true,
                baseUrl,
                raw,
                subscriber = hasSubscriber,
                subscriberState = parsed.GetValueOrDefault("subscriberState"),
                name = subscriberName,
                lastSubscriberActivityAt = parsed.GetValueOrDefault("lastSubscriberActivityAt"),
                subscriberAgeSeconds = ToInt(parsed.GetValueOrDefault("subscriberAgeSeconds")),
                subscriberStaleSeconds = ToInt(parsed.GetValueOrDefault("subscriberStaleSeconds")),
                patients = ToInt(parsed.GetValueOrDefault("patients")),
                loadedPatients = ToInt(parsed.GetValueOrDefault("loadedPatients")),
                searchCount = ToInt(parsed.GetValueOrDefault("searchCount")),
                lastSearchAt = parsed.GetValueOrDefault("lastSearchAt"),
                lastPushAt = parsed.GetValueOrDefault("lastPushAt"),
                lastPushResult = parsed.GetValueOrDefault("lastPushResult"),
                storageMode = parsed.GetValueOrDefault("storageMode"),
                store = parsed.GetValueOrDefault("store") ?? parsed.GetValueOrDefault("storePath"),
                storePath = parsed.GetValueOrDefault("store") ?? parsed.GetValueOrDefault("storePath")
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                enabled = ResolveBridgeEnabled(),
                reachable = false,
                baseUrl,
                raw = "",
                error = ex.Message
            });
        }
    }

    [HttpGet("bridge-logs")]
    public async Task<IActionResult> GetBridgeLogs([FromQuery] long sinceId = 0, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        var baseUrl = ResolveBridgeBaseUrl();
        var query = $"logs?sinceId={sinceId}&take={Math.Clamp(take, 1, 500)}";
        var url = new Uri(new Uri(baseUrl), query);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var raw = await _http.GetStringAsync(url, cts.Token);
            var node = JsonNode.Parse(raw);
            return Ok(new
            {
                reachable = true,
                baseUrl,
                items = node?["items"]?.AsArray()
            });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                reachable = false,
                baseUrl,
                error = ex.Message,
                items = Array.Empty<object>()
            });
        }
    }

    [HttpPost("send")]
    public async Task<IActionResult> EnqueueSend([FromBody] AdtSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageContent))
            return BadRequest(new { message = "ADT message content is required" });

        await UpsertPatientAndVisitFromHl7Async(request.MessageContent);

        _db.AdtQueue.Add(new AdtQueueItem
        {
            AdtMessageType = request.AdtType ?? "A01",
            MessageContent = request.MessageContent,
            TargetEndpoint = BuildTargetEndpoint(request.TargetHost, request.TargetPort),
            Priority = request.Priority,
            Status = 0,
            CreatedAt = ChinaTime.Now,
            MaxRetries = 3,
        });

        await _db.SaveChangesAsync();
        return Ok(new { message = "ADT message queued" });
    }

    [HttpPost("compose")]
    public IActionResult Compose([FromBody] AdtComposeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PatientId))
            return BadRequest(new { message = "Patient ID is required" });
        if (string.IsNullOrWhiteSpace(request.Department))
            return BadRequest(new { message = "Department is required" });
        if (string.IsNullOrWhiteSpace(request.Bed))
            return BadRequest(new { message = "Bed is required" });

        var adtType = (request.AdtType ?? "A01").Replace("ADT^", "");
        if (adtType.Length <= 2) adtType = $"A{adtType.PadLeft(2, '0')}";

        var msg = BuildAdtMessage(request);
        return Ok(new { message = "ADT message composed", adtType = $"ADT^{adtType}", content = msg });
    }

    [HttpDelete("queue/{id:long}")]
    public async Task<IActionResult> DeleteQueueItem(long id)
    {
        var item = await _db.AdtQueue.FindAsync(id);
        if (item is null) return NotFound();
        _db.AdtQueue.Remove(item);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    [HttpPost("queue/{id:long}/retry")]
    public async Task<IActionResult> RetryQueueItem(long id)
    {
        var item = await _db.AdtQueue.FindAsync(id);
        if (item is null) return NotFound();
        item.Status = 0;
        item.RetryCount = 0;
        item.LastError = null;
        await _db.SaveChangesAsync();
        return Ok(new { retry = true });
    }

    private static string BuildAdtMessage(AdtComposeRequest r)
    {
        var now = ChinaTime.Now;
        var dt = now.ToString("yyyyMMddHHmmss");
        var controlId = $"{now:yyyyMMddHHmmss}{Guid.NewGuid():N}"[..20];
        var adtType = (r.AdtType ?? "A01").Replace("ADT^", "");
        if (adtType.Length <= 2) adtType = $"A{adtType.PadLeft(2, '0')}";

        var pid = r.PatientId ?? "UNKNOWN";
        var name = r.PatientName ?? "";
        var dob = ParseDate(r.DateOfBirth);
        var gender = r.Gender ?? "";
        var visitId = r.VisitId ?? pid;

        var segSep = "\r";
        var msh = $"MSH|^~\\&|HL7Gateway|{r.SendingFacility}|PICiX|{r.ReceivingFacility}|{dt}||ADT^{adtType}|{controlId}|P|2.4";
        var evn = $"EVN|{adtType}|{dt}";
        var pidSeg = $"PID|1||{EscapeField(pid)}^^^^MR||{EscapeName(name)}||{dob}|{EscapeField(gender)}||||||||||{EscapeField(pid)}";
        var pv1Seg = BuildPv1(r, visitId);

        return string.Join(segSep, msh, evn, pidSeg, pv1Seg);
    }

    private static string BuildPv1(AdtComposeRequest r, string visitId)
    {
        var loc = string.Join("^", new[]
        {
            EscapeField(r.Department),
            EscapeField(r.Room),
            EscapeField(r.Bed),
            EscapeField(r.Ward)
        });
        var doc = EscapeField(r.AttendingDoctor);
        var diagnosis = EscapeField(r.AdmitDiagnosis);
        return $"PV1|1|I|{loc}||||{doc}|||||||{diagnosis}||||||||||{EscapeField(visitId)}";
    }

    private static string BuildTargetEndpoint(string? host, int? port)
    {
        if (!string.IsNullOrWhiteSpace(host) && port.HasValue && port.Value > 0)
            return $"{host}:{port.Value}";
        return "Philips HIF bridge";
    }

    private bool ResolveBridgeEnabled()
    {
        var serviceConfig = ReadServiceConfig();
        return serviceConfig?["HL7"]?["ADT"]?["HifBridge"]?["Enabled"]?.GetValue<bool>()
            ?? _configuration.GetValue<bool?>("HL7:ADT:HifBridge:Enabled")
            ?? true;
    }

    private string ResolveBridgeBaseUrl()
    {
        var serviceConfig = ReadServiceConfig();
        var configured = serviceConfig?["HL7"]?["ADT"]?["HifBridge"]?["BaseUrl"]?.GetValue<string>()
            ?? _configuration["HL7:ADT:HifBridge:BaseUrl"]
            ?? "http://localhost:5080/";
        return configured.EndsWith("/", StringComparison.Ordinal) ? configured : configured + "/";
    }

    private static JsonNode? ReadServiceConfig()
    {
        foreach (var path in CandidateServiceConfigPaths())
        {
            if (!System.IO.File.Exists(path)) continue;
            try
            {
                return JsonNode.Parse(System.IO.File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private static IEnumerable<string> CandidateServiceConfigPaths()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.GetFullPath(Path.Combine(baseDir, "..", "Service", "appsettings.json"));
        yield return Path.GetFullPath(Path.Combine(baseDir, "..", "service", "appsettings.json"));
        yield return Path.Combine(baseDir, "appsettings.json");
        yield return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "HL7Gateway.Service", "appsettings.json"));
    }

    private static Dictionary<string, string> ParseStatusText(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var index = part.IndexOf('=');
            if (index <= 0) continue;
            var key = part[..index].Trim();
            // lastPushResult 内可能再含 subscriber=xxx，勿覆盖前面的 subscriber=True
            if (result.ContainsKey(key)) continue;
            result[key] = part[(index + 1)..].Trim();
        }
        return result;
    }

    private static bool ResolveBridgeSubscriber(Dictionary<string, string> parsed)
    {
        return ToBool(parsed.GetValueOrDefault("subscriber"));
    }

    private static bool ToBool(string? value)
        => bool.TryParse(value, out var parsed) && parsed;

    private static int ToInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : 0;

    private async Task UpsertPatientAndVisitFromHl7Async(string raw)
    {
        var parsed = ParseAdt(raw);
        if (string.IsNullOrWhiteSpace(parsed.PatientId))
            return;

        var patient = await _db.Patients.FindAsync(parsed.PatientId);
        if (patient is null)
        {
            patient = new Patient
            {
                PatientId = parsed.PatientId,
                Name = parsed.PatientName,
                DateOfBirth = ToDateOnly(parsed.DateOfBirth),
                Gender = parsed.Gender,
            };
            _db.Patients.Add(patient);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(parsed.PatientName)) patient.Name = parsed.PatientName;
            if (!string.IsNullOrWhiteSpace(parsed.DateOfBirth)) patient.DateOfBirth = ToDateOnly(parsed.DateOfBirth);
            if (!string.IsNullOrWhiteSpace(parsed.Gender)) patient.Gender = parsed.Gender;
        }

        var visitId = string.IsNullOrWhiteSpace(parsed.VisitId)
            ? $"{parsed.PatientId}_V1"
            : parsed.VisitId;

        var visit = await _db.Visits.FindAsync(visitId);
        if (visit is null)
        {
            visit = new Visit
            {
                VisitId = visitId,
                PatientId = parsed.PatientId,
                PatientClass = "I",
                Department = parsed.Department,
                Ward = parsed.Ward,
                Room = parsed.Room,
                Bed = parsed.Bed,
                AttendingDoctor = parsed.AttendingDoctor,
                AdmitDiagnosis = parsed.AdmitDiagnosis,
                AdmitDateTime = ChinaTime.Now,
            };
            _db.Visits.Add(visit);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(parsed.Department)) visit.Department = parsed.Department;
            if (!string.IsNullOrWhiteSpace(parsed.Ward)) visit.Ward = parsed.Ward;
            if (!string.IsNullOrWhiteSpace(parsed.Room)) visit.Room = parsed.Room;
            if (!string.IsNullOrWhiteSpace(parsed.Bed)) visit.Bed = parsed.Bed;
            if (!string.IsNullOrWhiteSpace(parsed.AttendingDoctor)) visit.AttendingDoctor = parsed.AttendingDoctor;
            if (!string.IsNullOrWhiteSpace(parsed.AdmitDiagnosis)) visit.AdmitDiagnosis = parsed.AdmitDiagnosis;
        }
    }

    private static ParsedAdt ParseAdt(string raw)
    {
        var result = new ParsedAdt();
        foreach (var line in raw.Replace("\n", "\r").Split('\r', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('|');
            if (fields.Length == 0) continue;

            if (fields[0] == "PID")
            {
                result.PatientId = FirstComponent(GetField(fields, 3));
                if (string.IsNullOrWhiteSpace(result.PatientId))
                    result.PatientId = FirstComponent(GetField(fields, 2));
                if (string.IsNullOrWhiteSpace(result.PatientId))
                    result.PatientId = FirstComponent(GetField(fields, 4));

                result.PatientName = ParseName(GetField(fields, 5));
                result.DateOfBirth = GetField(fields, 7);
                result.Gender = GetField(fields, 8);
            }
            else if (fields[0] == "PV1")
            {
                var loc = GetField(fields, 3).Split('^');
                result.Department = loc.ElementAtOrDefault(0) ?? "";
                result.Room = loc.ElementAtOrDefault(1) ?? "";
                result.Bed = loc.ElementAtOrDefault(2) ?? "";
                result.Ward = loc.ElementAtOrDefault(3) ?? "";
                result.AttendingDoctor = GetField(fields, 7);
                result.AdmitDiagnosis = GetField(fields, 14);
                result.VisitId = GetField(fields, 19);
                if (string.IsNullOrWhiteSpace(result.VisitId))
                    result.VisitId = GetField(fields, 24);
            }
        }
        return result;
    }

    private static string GetField(string[] fields, int index)
        => index >= 0 && index < fields.Length ? fields[index] : "";

    private static string FirstComponent(string value)
        => value.Split('^').FirstOrDefault() ?? "";

    private static string ParseName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var parts = value.Split('^');
        var family = parts.ElementAtOrDefault(0) ?? "";
        var given = parts.ElementAtOrDefault(1) ?? "";
        return string.Join("", new[] { family, given }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string EscapeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var parts = name.Split(' ', 2);
        return parts.Length == 2 ? $"{EscapeField(parts[1])}^{EscapeField(parts[0])}" : $"{EscapeField(name)}";
    }

    private static string EscapeField(string? value)
        => (value ?? "")
            .Replace("\\", @"\E\")
            .Replace("|", @"\F\")
            .Replace("^", @"\S\")
            .Replace("~", @"\R\")
            .Replace("&", @"\T\");

    private static string ParseDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return "";
        // Handle yyyy-MM-dd (from HTML date input) or yyyyMMdd
        if (dateStr.Length == 10 && dateStr[4] == '-')
            return dateStr.Replace("-", "");
        if (dateStr.Length == 8 && dateStr.All(char.IsDigit))
            return dateStr;
        return "";
    }

    private static DateOnly? ToDateOnly(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;
        if (dateStr.Length == 10 && dateStr[4] == '-')
        {
            if (DateOnly.TryParse(dateStr, out var d)) return d;
        }
        if (dateStr.Length == 8 && dateStr.All(char.IsDigit))
        {
            if (DateOnly.TryParseExact(dateStr, "yyyyMMdd", out var d)) return d;
        }
        return null;
    }
}

public class AdtSendRequest
{
    public string? AdtType { get; set; }
    public string? TargetHost { get; set; }
    public int? TargetPort { get; set; }
    public string MessageContent { get; set; } = string.Empty;
    public int Priority { get; set; }
}

public class AdtComposeRequest
{
    public string? AdtType { get; set; }
    public int Priority { get; set; }

    public string PatientId { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string? DateOfBirth { get; set; }
    public string Gender { get; set; } = "";
    public string VisitId { get; set; } = "";
    public string Department { get; set; } = "";
    public string Ward { get; set; } = "";
    public string Room { get; set; } = "";
    public string Bed { get; set; } = "";
    public string AttendingDoctor { get; set; } = "";
    public string AdmitDiagnosis { get; set; } = "";
    public string SendingFacility { get; set; } = "";
    public string ReceivingFacility { get; set; } = "";
}

internal sealed class ParsedAdt
{
    public string PatientId { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string DateOfBirth { get; set; } = "";
    public string Gender { get; set; } = "";
    public string VisitId { get; set; } = "";
    public string Department { get; set; } = "";
    public string Ward { get; set; } = "";
    public string Room { get; set; } = "";
    public string Bed { get; set; } = "";
    public string AttendingDoctor { get; set; } = "";
    public string AdmitDiagnosis { get; set; } = "";
}
