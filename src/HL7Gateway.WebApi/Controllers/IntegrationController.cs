using HL7Gateway.Core;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Services.Implementations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/integration")]
public class IntegrationController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;
    private readonly IntegrationTraceService _trace;
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public IntegrationController(
        Hl7GatewayDbContext db,
        IntegrationTraceService trace,
        HttpClient http,
        IConfiguration configuration)
    {
        _db = db;
        _trace = trace;
        _http = http;
        _configuration = configuration;
    }

    [HttpGet("partners")]
    public async Task<IActionResult> GetPartners(CancellationToken ct)
    {
        var now = ChinaTime.Now;
        var dayAgo = now.AddHours(-24);

        var mllpPort = _configuration.GetValue<int?>("HL7:Listener:Port") ?? 2575;
        var connectedDevices = await _db.DeviceConnections.AsNoTracking().CountAsync(d => d.IsConnected, ct);
        var messages24h = await _db.Hl7Messages.AsNoTracking().CountAsync(m => m.ReceivedAt >= dayAgo, ct);
        var lastInbound = await _db.Hl7Messages.AsNoTracking()
            .OrderByDescending(m => m.ReceivedAt)
            .Select(m => new { m.ReceivedAt, m.SourceIp, m.MessageType })
            .FirstOrDefaultAsync(ct);

        var queuePending = await _db.AdtQueue.AsNoTracking().CountAsync(q => q.Status == 0, ct);
        var queueFailed = await _db.AdtQueue.AsNoTracking().CountAsync(q => q.Status == 3, ct);
        var lastSent = await _db.AdtLogs.AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new { l.CreatedAt, l.MessageType, l.Status })
            .FirstOrDefaultAsync(ct);

        var bridge = await FetchBridgePartnerAsync(ct);
        var outbound = BuildOutboundPartner();

        var partners = new List<object>
        {
            new
            {
                key = "mllp-inbound",
                name = $"MLLP 入站 ({mllpPort})",
                status = connectedDevices > 0 || messages24h > 0 ? "ok" : "warn",
                detail = $"{connectedDevices} 设备连接 · 24h 消息 {messages24h}",
                metrics = new
                {
                    port = mllpPort,
                    connectedDevices,
                    messages24h,
                    lastInboundAt = lastInbound?.ReceivedAt,
                    lastSourceIp = lastInbound?.SourceIp,
                    lastMessageType = lastInbound?.MessageType
                }
            },
            bridge,
            new
            {
                key = "adt-queue",
                name = "ADT 发送队列",
                status = queueFailed > 0 ? "error" : queuePending > 5 ? "warn" : "ok",
                detail = $"待发送 {queuePending} · 失败 {queueFailed}",
                metrics = new
                {
                    pending = queuePending,
                    failed = queueFailed,
                    lastSentAt = lastSent?.CreatedAt,
                    lastMessageType = lastSent?.MessageType,
                    lastStatus = lastSent?.Status
                }
            },
            outbound
        };

        return Ok(new { timestamp = now, partners });
    }

    [HttpGet("traces")]
    public async Task<IActionResult> GetTraces(
        [FromQuery] string? traceId,
        [FromQuery] int limit = 200,
        [FromQuery] int recent = 30,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            var timeline = await _trace.GetTimelineAsync(_db, traceId.Trim(), limit, ct);
            return Ok(new { traceId = traceId.Trim(), events = timeline });
        }

        var recentTraces = await _trace.GetRecentTracesAsync(_db, recent, ct);
        return Ok(new { recent = recentTraces });
    }

    [HttpPost("simulate/inject")]
    public async Task<IActionResult> SimulateInject([FromBody] SimulateInjectRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Hl7))
            return BadRequest(new { message = "hl7 内容不能为空" });

        var hl7 = request.Hl7.Replace("\n", "\r").Trim();
        var controlId = IntegrationTraceService.ExtractControlId(hl7);
        if (string.IsNullOrWhiteSpace(controlId))
            return BadRequest(new { message = "无法从 MSH 解析 Message Control ID (MSH-10)" });

        var mode = (request.Mode ?? "adt-queue").Trim().ToLowerInvariant();

        if (mode == "bridge-direct")
        {
            var baseUrl = ResolveBridgeBaseUrl();
            var url = new Uri(new Uri(baseUrl), "adt");
            try
            {
                using var content = new StringContent(hl7, System.Text.Encoding.UTF8, "text/plain");
                using var response = await _http.PostAsync(url, content, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                await _trace.AppendAsync(_db, controlId, "Simulate.Inject", "Simulate",
                    response.IsSuccessStatusCode ? "OK" : "Failed",
                    $"bridge-direct HTTP {(int)response.StatusCode}: {Truncate(body, 500)}",
                    "hif-bridge", null, null, null, ct);
                return Ok(new
                {
                    traceId = controlId,
                    mode,
                    success = response.IsSuccessStatusCode,
                    httpStatus = (int)response.StatusCode,
                    response = body
                });
            }
            catch (Exception ex)
            {
                await _trace.AppendAsync(_db, controlId, "Simulate.Inject", "Simulate", "Failed",
                    ex.Message, "hif-bridge", null, null, null, ct);
                return StatusCode(502, new { traceId = controlId, message = ex.Message });
            }
        }

        var msgType = ExtractMessageType(hl7);
        var queueItem = new AdtQueueItem
        {
            AdtMessageType = msgType,
            MessageContent = hl7,
            TargetEndpoint = "Philips HIF bridge (simulate)",
            Priority = 1,
            Status = 0,
            CreatedAt = ChinaTime.Now,
            MaxRetries = 3
        };
        _db.AdtQueue.Add(queueItem);
        await _db.SaveChangesAsync(ct);

        await _trace.AppendAsync(_db, controlId, "Simulate.Inject", "Simulate", "OK",
            $"已入 ADT 队列 #{queueItem.QueueId}", "adt-queue", null, "AdtQueue", queueItem.QueueId, ct);
        await _trace.AppendAsync(_db, controlId, "AdtQueue.Pending", "Outbound", "Pending",
            $"队列项 #{queueItem.QueueId} 等待发送", "adt-queue", null, "AdtQueue", queueItem.QueueId, ct);

        return Ok(new
        {
            traceId = controlId,
            mode = "adt-queue",
            queueId = queueItem.QueueId,
            message = "已注入 ADT 队列，Service 进程将自动发送"
        });
    }

    [HttpPost("simulate/replay/{messageId:long}")]
    public async Task<IActionResult> SimulateReplay(long messageId, CancellationToken ct)
    {
        var msg = await _db.Hl7Messages.AsNoTracking().FirstOrDefaultAsync(m => m.MessageId == messageId, ct);
        if (msg is null)
            return NotFound(new { message = "消息不存在" });

        var hl7 = msg.RawContent;
        var controlId = msg.MessageControlId;
        if (string.IsNullOrWhiteSpace(controlId))
            controlId = IntegrationTraceService.ExtractControlId(hl7) ?? $"REPLAY{messageId}";

        if (!hl7.Contains("ADT^", StringComparison.OrdinalIgnoreCase)
            && !msg.MessageType.Equals("ADT", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "当前仅支持重放 ADT 类消息到发送队列；ORU 等入站重放请 Phase 3 支持",
                messageId,
                messageType = msg.MessageType
            });
        }

        var msgType = string.IsNullOrWhiteSpace(msg.TriggerEvent)
            ? ExtractMessageType(hl7)
            : $"ADT^{msg.TriggerEvent}";

        var queueItem = new AdtQueueItem
        {
            MessageId = messageId,
            AdtMessageType = msgType,
            MessageContent = hl7,
            TargetEndpoint = "Philips HIF bridge (replay)",
            Priority = 2,
            Status = 0,
            CreatedAt = ChinaTime.Now,
            MaxRetries = 3
        };
        _db.AdtQueue.Add(queueItem);
        await _db.SaveChangesAsync(ct);

        await _trace.AppendAsync(_db, controlId, "Simulate.Replay", "Simulate", "OK",
            $"重放 Hl7Message #{messageId} → 队列 #{queueItem.QueueId}", "adt-queue", null,
            "Hl7Message", messageId, ct);

        return Ok(new
        {
            traceId = controlId,
            messageId,
            queueId = queueItem.QueueId,
            message = "已重放入 ADT 队列"
        });
    }

    private object BuildOutboundPartner()
    {
        var host = _configuration["HL7:ADT:TargetHost"];
        var portStr = _configuration["HL7:ADT:TargetPort"];
        if (string.IsNullOrWhiteSpace(host) || !int.TryParse(portStr, out var port) || port <= 0)
        {
            return new
            {
                key = "mllp-outbound",
                name = "MLLP 出站 (ADT)",
                status = "idle",
                detail = "未配置 TargetHost/TargetPort",
                metrics = new { configured = false }
            };
        }

        return new
        {
            key = "mllp-outbound",
            name = $"MLLP 出站 ({host}:{port})",
            status = "ok",
            detail = "已配置 · 作为 ADT 发送备选通道",
            metrics = new { configured = true, host, port }
        };
    }

    private async Task<object> FetchBridgePartnerAsync(CancellationToken ct)
    {
        var enabled = _configuration.GetValue<bool?>("HL7:ADT:HifBridge:Enabled") ?? true;
        if (!enabled)
        {
            return new
            {
                key = "hif-bridge",
                name = "Philips HIF Bridge",
                status = "idle",
                detail = "桥接未启用",
                metrics = new { enabled = false }
            };
        }

        var baseUrl = ResolveBridgeBaseUrl();
        var url = new Uri(new Uri(baseUrl), "status");
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var raw = await _http.GetStringAsync(url, cts.Token);
            var parsed = ParseKeyValueStatus(raw);
            var subscriber = ResolveSubscriber(parsed);
            var status = !subscriber ? "warn" : "ok";
            return new
            {
                key = "hif-bridge",
                name = "Philips HIF Bridge",
                status,
                detail = subscriber
                    ? $"PIC iX 已订阅 · {parsed.GetValueOrDefault("name")}"
                    : !string.IsNullOrWhiteSpace(parsed.GetValueOrDefault("name")) && parsed.GetValueOrDefault("name") != "(null)"
                        ? $"桥接在线 · 订阅已断开或超时 · {parsed.GetValueOrDefault("name")}"
                        : "桥接在线 · PIC iX 未订阅",
                metrics = new
                {
                    enabled = true,
                    reachable = true,
                    baseUrl,
                    subscriber,
                    subscriberState = parsed.GetValueOrDefault("subscriberState"),
                    name = parsed.GetValueOrDefault("name"),
                    lastSubscriberActivityAt = parsed.GetValueOrDefault("lastSubscriberActivityAt"),
                    subscriberAgeSeconds = parsed.GetValueOrDefault("subscriberAgeSeconds"),
                    patients = parsed.GetValueOrDefault("patients"),
                    lastPushAt = parsed.GetValueOrDefault("lastPushAt"),
                    lastPushResult = parsed.GetValueOrDefault("lastPushResult")
                }
            };
        }
        catch (Exception ex)
        {
            return new
            {
                key = "hif-bridge",
                name = "Philips HIF Bridge",
                status = "error",
                detail = $"不可达: {ex.Message}",
                metrics = new { enabled = true, reachable = false, baseUrl }
            };
        }
    }

    private string ResolveBridgeBaseUrl()
    {
        var baseUrl = _configuration["HL7:ADT:HifBridge:BaseUrl"] ?? "http://localhost:5080/";
        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";
        return baseUrl;
    }

    private static bool ResolveSubscriber(Dictionary<string, string> parsed)
    {
        if (parsed.TryGetValue("subscriber", out var sub)
            && bool.TryParse(sub, out var b))
            return b;
        return false;
    }

    private static Dictionary<string, string> ParseKeyValueStatus(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            result[part[..idx].Trim()] = part[(idx + 1)..].Trim();
        }
        return result;
    }

    private static string ExtractMessageType(string hl7)
    {
        var line = hl7.Split('\r', '\n').FirstOrDefault(l => l.StartsWith("MSH|", StringComparison.OrdinalIgnoreCase));
        if (line is null) return "ADT^A01";
        var fields = line.Split('|');
        if (fields.Length > 8 && fields[8].Contains('^'))
            return fields[8];
        return "ADT^A01";
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "...";
    }
}

public class SimulateInjectRequest
{
    public string? Hl7 { get; set; }
    public string? Mode { get; set; }
}
