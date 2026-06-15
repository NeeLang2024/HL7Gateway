using HL7Gateway.Core;
using System.Text;
using System.Text.RegularExpressions;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Services.Implementations;
using HL7Gateway.Core.Services.Interfaces;
using HL7Gateway.Service.Services.Wcf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Service.Services;

public class AdtSenderService : IAdtSenderService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly PicixCallbackManager _callbackManager;
    private readonly PhilipsHifBridgeClient _hifBridgeClient;
    private readonly IWsiService _wsiService;
    private readonly IMllpSenderService _mllpSender;
    private readonly IConfiguration _configuration;
    private readonly IntegrationTraceService _trace;
    private readonly ILogger<AdtSenderService> _logger;

    public AdtSenderService(
        IServiceScopeFactory scopeFactory,
        IEventPublisher eventPublisher,
        PicixCallbackManager callbackManager,
        PhilipsHifBridgeClient hifBridgeClient,
        IWsiService wsiService,
        IMllpSenderService mllpSender,
        IConfiguration configuration,
        IntegrationTraceService trace,
        ILogger<AdtSenderService> logger)
    {
        _scopeFactory = scopeFactory;
        _eventPublisher = eventPublisher;
        _callbackManager = callbackManager;
        _hifBridgeClient = hifBridgeClient;
        _wsiService = wsiService;
        _mllpSender = mllpSender;
        _configuration = configuration;
        _trace = trace;
        _logger = logger;
    }

    private (string? Host, int Port) GetMllpTarget()
    {
        var host = _configuration["HL7:ADT:TargetHost"];
        var portStr = _configuration["HL7:ADT:TargetPort"];
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(portStr))
            return (null, 0);
        if (int.TryParse(portStr, out var port) && port > 0)
            return (host, port);
        return (null, 0);
    }

    public int PendingCount
    {
        get
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();
            return db.AdtQueue.Count(q => q.Status == 0);
        }
    }

    public async Task EnqueueAsync(string adtType, string messageContent, string targetEndpoint, int priority = 0)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();

        db.AdtQueue.Add(new AdtQueueItem
        {
            AdtMessageType = adtType,
            MessageContent = messageContent,
            TargetEndpoint = targetEndpoint,
            Priority = priority,
            Status = 0,
            CreatedAt = ChinaTime.Now,
            MaxRetries = 3,
        });

        await db.SaveChangesAsync();
        _logger.LogInformation("ADT {Type} queued for {Endpoint}", adtType, targetEndpoint);
    }

    public async Task<int> ProcessQueueAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Hl7GatewayDbContext>();

        var now = ChinaTime.Now;
        var pending = await db.AdtQueue
            .Where(q => q.Status == 0 && q.RetryCount < q.MaxRetries
                && (q.NextRetryAt == null || q.NextRetryAt <= now))
            .OrderBy(q => q.Priority)
            .ThenBy(q => q.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0) return 0;

        var sent = 0;
        foreach (var item in pending)
        {
            var traceId = IntegrationTraceService.ExtractControlId(item.MessageContent)
                ?? $"QUEUE-{item.QueueId}";

            item.Status = 1;
            item.RetryCount++;
            await db.SaveChangesAsync(cancellationToken);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            var success = false;
            string? response = null;
            string? error = null;

            var bridgeOk = false;
            if (_hifBridgeClient.IsEnabled)
            {
                var bridge = await _hifBridgeClient.PushAdtAsync(item.MessageContent, cancellationToken);
                bridgeOk = bridge.Success;
                await _trace.AppendAsync(db, traceId, "Bridge.Push", "Bridge",
                    bridgeOk ? "OK" : "Failed",
                    bridgeOk
                        ? TruncateTrace(bridge.Response ?? "accepted", 500)
                        : TruncateTrace(bridge.Error ?? bridge.Response ?? "failed", 500),
                    "hif-bridge", (int)sw.ElapsedMilliseconds, "AdtQueue", item.QueueId, cancellationToken);
                if (bridgeOk)
                {
                    success = true;
                    response = $"HIF bridge: {bridge.Response ?? "accepted"}";
                }
                else
                {
                    error = $"HIF bridge: {bridge.Error ?? "failed"}";
                    response = bridge.Response;
                }
            }

            var wcfOk = false;
            if (!success && _callbackManager.SubscriberCount > 0)
            {
                (wcfOk, response, error) = await PushViaWcfAsync(item, db, cancellationToken);
                if (wcfOk) success = true;
                await _trace.AppendAsync(db, traceId, "Wcf.Push", "Bridge",
                    wcfOk ? "OK" : "Failed",
                    TruncateTrace(wcfOk ? response : error, 500),
                    "hif-bridge", (int)sw.ElapsedMilliseconds, "AdtQueue", item.QueueId, cancellationToken);
            }

            var target = GetMllpTarget();
            if (target.Host is not null)
            {
                var (mllpOk, mllpResponse, mllpError) = await _mllpSender.SendAsync(
                    target.Host, target.Port, item.MessageContent);
                if (mllpOk)
                {
                    success = true;
                    response = (response ?? "") + " | MLLP:OK";
                    error = null;
                }
                else if (!wcfOk)
                {
                    success = false;
                    response = null;
                    error = $"{error ?? "WCF: no subscriber"} | MLLP: {mllpError}";
                }
                else
                {
                    response = (response ?? "") + $" | MLLP:{mllpError}";
                    error = null;
                }
            }
            else if (!success)
            {
                success = false;
                error ??= _hifBridgeClient.IsEnabled
                    ? "Philips HIF bridge did not accept ADT and no fallback target configured"
                    : "No Philips HIF bridge, PIC iX WCF subscriber, or MLLP target configured";
            }

            sw.Stop();

            if (success)
            {
                item.Status = 2;
                item.NextRetryAt = null;
                item.SentAt = now;
                item.AckReceivedAt = now;
                sent++;
                await _trace.AppendAsync(db, traceId, "AdtQueue.Sent", "Outbound", "OK",
                    TruncateTrace(response, 500), "adt-queue", (int)sw.ElapsedMilliseconds,
                    "AdtQueue", item.QueueId, cancellationToken);
                _logger.LogInformation(
                    "ADT {Type} queue #{QueueId} sent successfully to {Endpoint}: {Response}",
                    item.AdtMessageType,
                    item.QueueId,
                    item.TargetEndpoint,
                    response ?? "OK");

                if (bridgeOk)
                {
                    _logger.LogInformation(
                        "ADT {Type} queue #{QueueId} was accepted by the Philips HIF bridge. PIC iX business/bedside Auto ADT acceptance is not confirmed by this result.",
                        item.AdtMessageType,
                        item.QueueId);
                }

                var (pid, vid) = ExtractPatientAndVisit(item.MessageContent);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _eventPublisher.PublishAdtSent(
                            item.AdtMessageType, pid, vid,
                            item.MessageContent, item.TargetEndpoint, true);
                    }
                    catch { }
                }, cancellationToken);
            }
            else
            {
                if (item.RetryCount >= item.MaxRetries)
                    item.Status = 3;
                item.NextRetryAt = now + Backoff(item.RetryCount);
                item.LastError = error;
                await _trace.AppendAsync(db, traceId, "AdtQueue.Failed", "Outbound", "Failed",
                    TruncateTrace(error, 500), "adt-queue", (int)sw.ElapsedMilliseconds,
                    "AdtQueue", item.QueueId, cancellationToken);
                _logger.LogWarning(
                    "ADT {Type} queue #{QueueId} send failed ({Retry}/{MaxRetries}) to {Endpoint}: {Error}",
                    item.AdtMessageType,
                    item.QueueId,
                    item.RetryCount,
                    item.MaxRetries,
                    item.TargetEndpoint,
                    error ?? "unknown error");
            }

            db.AdtLogs.Add(new AdtLogEntry
            {
                QueueId = item.QueueId,
                MessageType = item.AdtMessageType,
                TargetEndpoint = item.TargetEndpoint,
                Status = item.Status,
                RequestContent = item.MessageContent,
                ResponseContent = response,
                ErrorMessage = error,
                DurationMs = (int)sw.ElapsedMilliseconds,
                CreatedAt = now,
            });

            await WriteBackAutoAdtAsync(db, item, success, response, error, now, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }

    /// <summary>
    /// B10: 队列项发送完成后，把状态回写到对应的 Auto ADT 消息与事件，
    /// 避免 Auto ADT 日志/看板长期停留在 "Queued"。
    /// </summary>
    private static async Task WriteBackAutoAdtAsync(
        Hl7GatewayDbContext db, AdtQueueItem item, bool success,
        string? response, string? error, DateTime now, CancellationToken ct)
    {
        var autoMsg = await db.AutoAdtMessages
            .FirstOrDefaultAsync(m => m.AdtQueueId == item.QueueId, ct);
        if (autoMsg is null) return;

        autoMsg.ResponseText = response;
        autoMsg.ErrorText = error;
        autoMsg.RetryCount = item.RetryCount;
        if (success)
        {
            autoMsg.SendStatus = "Sent";
            autoMsg.SentAt = now;
        }
        else
        {
            autoMsg.SendStatus = item.Status == 3 ? "Failed" : "Retrying";
        }

        var autoEvent = await db.AutoAdtEvents.FirstOrDefaultAsync(e => e.Id == autoMsg.EventId, ct);
        if (autoEvent is not null)
        {
            autoEvent.EventStatus = success ? "Sent" : (item.Status == 3 ? "Failed" : "Retrying");
            autoEvent.UpdatedAt = now;
        }
    }

    private async Task<(bool Success, string? Response, string? Error)> PushViaWcfAsync(
        AdtQueueItem item, Hl7GatewayDbContext db, CancellationToken ct)
    {
        try
        {
            Hl7Message? message = null;
            if (item.MessageId.HasValue)
                message = await db.Hl7Messages.FindAsync([item.MessageId.Value], ct);

            message ??= new Hl7Message
            {
                PatientId = ExtractPatientId(item.MessageContent),
                SendingApp = "HL7Gateway",
                SendingFacility = "",
                ReceivingApp = "PICiX",
                ReceivingFacility = "",
            };

            Patient? patient = null;
            Visit? visit = null;
            if (!string.IsNullOrEmpty(message.PatientId))
            {
                patient = await db.Patients.FindAsync([message.PatientId], ct);
                visit = !string.IsNullOrEmpty(message.VisitId)
                    ? await db.Visits.FindAsync([message.VisitId], ct)
                    : null;
            }

            var xml = await _wsiService.BuildAdtXmlAsync(NormalizeAdtTrigger(item.AdtMessageType), message, patient, visit, ct);

            var push = await _callbackManager.PushAdtXmlAsync(xml, ct);
            if (push.Success)
                return (true, push.Response, null);

            return (false, push.Response, $"PIC iX WCF push failed (subscriberCount={_callbackManager.SubscriberCount}): {push.Error}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PIC iX WCF push error for ADT queue #{QueueId}: {Msg}", item.QueueId, ex.Message);
            return (false, null, $"WCF push error: {ex.Message}");
        }
    }

    private static string ExtractPatientId(string raw)
    {
        foreach (var line in raw.Split('\r'))
        {
            if (line.StartsWith("PID|"))
            {
                var fields = line.Split('|');
                if (fields.Length > 3)
                {
                    var parts = fields[3].Split('^');
                    return parts.Length > 0 ? parts[0] : "";
                }
            }
        }
        return "";
    }

    private static string NormalizeAdtTrigger(string? adtType)
    {
        var value = (adtType ?? "A01").Trim();
        if (value.StartsWith("ADT^", StringComparison.OrdinalIgnoreCase))
            value = value[4..];
        if (value.StartsWith("ADT_", StringComparison.OrdinalIgnoreCase))
            value = value[4..];
        if (value.Length <= 2 && value.All(char.IsDigit))
            value = $"A{value.PadLeft(2, '0')}";
        return value.StartsWith('A') ? value : $"A{value.PadLeft(2, '0')}";
    }

    private static TimeSpan Backoff(int retryCount) => retryCount switch
    {
        1 => TimeSpan.FromSeconds(10),
        2 => TimeSpan.FromSeconds(30),
        _ => TimeSpan.FromMinutes(2),
    };

    private static string TruncateTrace(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "...";
    }

    private static (string PatientId, string VisitId) ExtractPatientAndVisit(string raw)
    {
        var pid = "";
        var vid = "";
        foreach (var line in raw.Split('\r'))
        {
            if (line.StartsWith("PID|"))
            {
                var fields = line.Split('|');
                if (fields.Length > 3)
                {
                    var parts = fields[3].Split('^');
                    pid = parts.Length > 0 ? parts[0] : "";
                }
            }
            if (line.StartsWith("PV1|"))
            {
                var fields = line.Split('|');
                if (fields.Length > 19)
                {
                    var parts = fields[19].Split('^');
                    vid = parts.Length > 0 ? parts[0] : fields[19];
                }
            }
        }
        return (pid, vid);
    }
}
