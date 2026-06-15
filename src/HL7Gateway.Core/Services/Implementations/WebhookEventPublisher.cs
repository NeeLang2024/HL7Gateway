using HL7Gateway.Core;
using System.Net.Http.Json;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace HL7Gateway.Core.Services.Implementations;

public class WebhookEventPublisher : IEventPublisher, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<WebhookEventPublisher> _logger;
    private readonly string _webhookUrl;

    public WebhookEventPublisher(HttpClient http, ILogger<WebhookEventPublisher> logger, string webhookUrl)
    {
        _http = http;
        _logger = logger;
        _webhookUrl = webhookUrl.TrimEnd('/');
    }

    public async Task PublishMessageReceived(Hl7Message message, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Webhook posting MessageReceived to {Url}", _webhookUrl);
            var payload = new
            {
                EventType = "MessageReceived",
                Data = new
                {
                    message.MessageId,
                    message.MessageControlId,
                    message.MessageType,
                    message.TriggerEvent,
                    message.SourceIp,
                    message.PatientId,
                    message.VisitId,
                    message.ParseStatus,
                    message.ReceivedAt
                }
            };
            var response = await _http.PostAsJsonAsync($"{_webhookUrl}/api/webhook/event", payload, ct);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Webhook returned {Status}", response.StatusCode);
            else
                _logger.LogInformation("Webhook POST successful");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook push failed: {Msg}", ex.Message);
        }
    }

    public async Task PublishDeviceConnected(string sourceIp, int sourcePort, CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                EventType = "DeviceConnected",
                Data = new { SourceIp = sourceIp, SourcePort = sourcePort }
            };
            await _http.PostAsJsonAsync($"{_webhookUrl}/api/webhook/event", payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Webhook push failed");
        }
    }

    public async Task PublishDeviceDisconnected(string sourceIp, int sourcePort, CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                EventType = "DeviceDisconnected",
                Data = new { SourceIp = sourceIp, SourcePort = sourcePort }
            };
            await _http.PostAsJsonAsync($"{_webhookUrl}/api/webhook/event", payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Webhook push failed");
        }
    }

    public async Task PublishAdtSent(string adtType, string patientId, string visitId, string messageContent,
        string targetEndpoint, bool success, CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                EventType = "AdtSent",
                Data = new
                {
                    AdtType = adtType,
                    PatientId = patientId,
                    VisitId = visitId,
                    MessageContent = messageContent,
                    TargetEndpoint = targetEndpoint,
                    Success = success,
                    SentAt = ChinaTime.Now,
                }
            };
            await _http.PostAsJsonAsync($"{_webhookUrl}/api/webhook/event", payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AdtSent webhook push failed");
        }
    }

    public void Dispose() => _http.Dispose();
}
