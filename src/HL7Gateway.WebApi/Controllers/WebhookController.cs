using HL7Gateway.Core;
using System.Text.Json;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Services.Interfaces;
using HL7Gateway.WebApi.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public class WebhookController : ControllerBase
{
    private readonly IHubContext<Hl7MonitorHub> _hubContext;
    private readonly IWsiService _wsi;
    private readonly IRawWebSocketManager _rawWs;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IHubContext<Hl7MonitorHub> hubContext,
        IWsiService wsi,
        IRawWebSocketManager rawWs,
        ILogger<WebhookController> logger)
    {
        _hubContext = hubContext;
        _wsi = wsi;
        _rawWs = rawWs;
        _logger = logger;
    }

    [HttpPost("event")]
    public async Task<IActionResult> ReceiveEvent([FromBody] WebhookEventPayload payload)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(payload.EventType, payload.Data);

            if (payload.Data is JsonElement json)
            {
                TriggerWsiFromEvent(payload.EventType, json);

                if (payload.EventType == "MessageReceived")
                {
                    var raw = json.TryGetProperty("RawContent", out var rawProp)
                        ? rawProp.GetString() : null;
                    if (!string.IsNullOrEmpty(raw))
                        await _rawWs.BroadcastAsync(raw);
                }
            }

            return Ok(new { forwarded = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to forward webhook event {EventType}", payload.EventType);
            return Ok(new { forwarded = false });
        }
    }

    private void TriggerWsiFromEvent(string eventType, JsonElement json)
    {
        static string? Prop(JsonElement j, string name)
        {
            // Try PascalCase first, then camelCase
            if (j.TryGetProperty(name, out var v)) return v.GetString();
            if (name.Length > 1)
            {
                var cc = char.ToLowerInvariant(name[0]) + name[1..];
                if (j.TryGetProperty(cc, out v)) return v.GetString();
            }
            return null;
        }

        if (eventType == "AdtSent")
        {
            _ = NotifyWsiAsync(
                Prop(json, "AdtType") ?? "A01",
                Prop(json, "PatientId") ?? "",
                Prop(json, "VisitId") ?? "",
                Prop(json, "MessageContent") ?? "");
            return;
        }

        if (eventType == "MessageReceived")
        {
            var msgType = Prop(json, "MessageType") ?? "";
            if (msgType == "ADT")
            {
                _ = NotifyWsiAsync(
                    Prop(json, "TriggerEvent") ?? "A01",
                    Prop(json, "PatientId") ?? "",
                    Prop(json, "VisitId") ?? "",
                    null);
            }
        }
    }

    private async Task NotifyWsiAsync(string? adtType, string? patientId, string? visitId, string? rawContent)
    {
        if (string.IsNullOrEmpty(patientId)) return;
        try
        {
            var msg = new Hl7Message
            {
                MessageControlId = Guid.NewGuid().ToString("N"),
                MessageType = "ADT",
                TriggerEvent = adtType,
                PatientId = patientId,
                VisitId = visitId,
                RawContent = rawContent ?? "",
                ReceivedAt = ChinaTime.Now,
                ParseStatus = 1,
            };
            await _wsi.NotifySubscribersAsync(adtType ?? "A01", msg);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WSI notify from event {EventType} failed", adtType);
        }
    }
}

public class WebhookEventPayload
{
    public string EventType { get; set; } = string.Empty;
    public object? Data { get; set; }
}
