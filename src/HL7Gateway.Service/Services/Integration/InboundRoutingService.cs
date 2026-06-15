using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Integration.Routing;
using HL7Gateway.Core.Services.Implementations;
using HL7Gateway.Core.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Service.Services.Integration;

/// <summary>
/// 入站 ADT 路由模块。默认关闭；关闭或无规则时走 <see cref="ApplyLegacyAdtForwardAsync"/>，与升级前行为一致。
/// </summary>
public class InboundRoutingService
{
    private readonly IntegrationHubSettingsService _hubSettings;
    private readonly RoutingEngine _routingEngine;
    private readonly IAdtSenderService _adtSender;
    private readonly AutoAdtHisBindingSync _hisBindingSync;
    private readonly IntegrationTraceService _trace;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<InboundRoutingService> _logger;

    public InboundRoutingService(
        IntegrationHubSettingsService hubSettings,
        RoutingEngine routingEngine,
        IAdtSenderService adtSender,
        AutoAdtHisBindingSync hisBindingSync,
        IntegrationTraceService trace,
        IHttpClientFactory httpClientFactory,
        ILogger<InboundRoutingService> logger)
    {
        _hubSettings = hubSettings;
        _routingEngine = routingEngine;
        _adtSender = adtSender;
        _hisBindingSync = hisBindingSync;
        _trace = trace;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task HandleInboundAdtAsync(
        Hl7GatewayDbContext db,
        Hl7Message message,
        bool parseSuccess,
        CancellationToken ct)
    {
        if (!parseSuccess
            || !string.Equals(message.MessageType, "ADT", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(message.TriggerEvent)
            || message.RawContent is null)
            return;

        if (!await _hubSettings.IsRoutingActiveAsync(db, ct))
        {
            await ApplyLegacyAdtForwardAsync(db, message, ct);
            return;
        }

        var rules = await db.RoutingRules.AsNoTracking()
            .Where(r => r.IsEnabled)
            .ToListAsync(ct);

        var match = _routingEngine.Match(message, rules);
        if (match is null)
        {
            await ApplyLegacyAdtForwardAsync(db, message, ct);
            return;
        }

        var settings = await _hubSettings.GetSettingsAsync(db, ct);
        var traceId = message.MessageControlId;
        var payload = Hl7FieldTransform.Apply(message.RawContent, match.Rule.TransformJson);

        if (settings.RoutingTraceEnabled)
        {
            await _trace.AppendAsync(db, traceId, "Routing.Matched", "Routing", "OK",
                $"规则 #{match.Rule.Id} {match.Rule.Name} → {match.Action}",
                "routing", null, "RoutingRule", match.Rule.Id, ct);
        }

        if (RoutingActions.ForwardsAdt(match.Action))
        {
            try
            {
                await _adtSender.EnqueueAsync(message.TriggerEvent, payload, $"Route:{match.Rule.Name}", match.Rule.Priority);
                if (settings.RoutingTraceEnabled)
                {
                    await _trace.AppendAsync(db, traceId, "Routing.ForwardAdt", "Routing", "OK",
                        $"已入 ADT 队列 · 规则 {match.Rule.Name}", "adt-queue", null,
                        "RoutingRule", match.Rule.Id, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Routing ForwardAdt failed for {ControlId}", message.MessageControlId);
            }

            if (match.Action != RoutingActions.SkipForward)
            {
                try
                {
                    await _hisBindingSync.TrySyncFromHl7Async(
                        db, payload, message.TriggerEvent, message.PatientId, message.VisitId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "HIS binding sync after routing failed for {Type}", message.TriggerEvent);
                }
            }
        }
        else if (match.Action == RoutingActions.SkipForward && settings.RoutingTraceEnabled)
        {
            await _trace.AppendAsync(db, traceId, "Routing.SkipForward", "Routing", "OK",
                $"跳过 ADT 转发 · 规则 {match.Rule.Name}", "routing", null,
                "RoutingRule", match.Rule.Id, ct);
        }

        if (RoutingActions.CallsWebhook(match.Action) && !string.IsNullOrWhiteSpace(match.Rule.WebhookUrl))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await PostWebhookAsync(match.Rule.WebhookUrl!, payload, message, match.Rule);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Routing webhook failed for rule {RuleId}", match.Rule.Id);
                }
            }, CancellationToken.None);
        }
    }

    private async Task ApplyLegacyAdtForwardAsync(Hl7GatewayDbContext db, Hl7Message message, CancellationToken ct)
    {
        try
        {
            await _adtSender.EnqueueAsync(message.TriggerEvent!, message.RawContent!, "Auto:MLLP", 1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-enqueue ADT {Type}", message.TriggerEvent);
        }

        try
        {
            await _hisBindingSync.TrySyncFromHl7Async(
                db, message.RawContent!, message.TriggerEvent!, message.PatientId, message.VisitId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HIS Auto ADT binding sync failed for {Type}", message.TriggerEvent);
        }
    }

    private async Task PostWebhookAsync(string url, string hl7, Hl7Message message, RoutingRule rule)
    {
        var client = _httpClientFactory.CreateClient(nameof(InboundRoutingService));
        client.Timeout = TimeSpan.FromSeconds(10);
        using var content = new StringContent(hl7, System.Text.Encoding.UTF8, "text/plain");
        using var response = await client.PostAsync(url, content);
        _logger.LogInformation(
            "Routing webhook rule #{RuleId} POST {Url} → HTTP {Status}",
            rule.Id, url, (int)response.StatusCode);
    }
}
