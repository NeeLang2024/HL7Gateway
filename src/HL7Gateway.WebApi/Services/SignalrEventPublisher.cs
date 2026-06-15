using HL7Gateway.Core;
using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Services.Interfaces;
using HL7Gateway.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace HL7Gateway.WebApi.Services;

public class SignalrEventPublisher : IEventPublisher
{
    private readonly IHubContext<Hl7MonitorHub> _hubContext;
    private readonly ILogger<SignalrEventPublisher> _logger;

    public SignalrEventPublisher(IHubContext<Hl7MonitorHub> hubContext, ILogger<SignalrEventPublisher> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PublishMessageReceived(Hl7Message message, CancellationToken ct = default)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("MessageReceived", new
            {
                message.MessageId,
                message.MessageControlId,
                message.MessageType,
                message.TriggerEvent,
                message.SourceIp,
                message.PatientId,
                message.ParseStatus,
                message.ReceivedAt
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SignalR push failed for message {ControlId}", message.MessageControlId);
        }
    }

    public async Task PublishDeviceConnected(string sourceIp, int sourcePort, CancellationToken ct = default)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("DeviceConnected", new
            {
                SourceIp = sourceIp,
                SourcePort = sourcePort,
                ConnectedAt = ChinaTime.Now
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SignalR push failed for device connected {Ip}:{Port}", sourceIp, sourcePort);
        }
    }

    public async Task PublishDeviceDisconnected(string sourceIp, int sourcePort, CancellationToken ct = default)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("DeviceDisconnected", new
            {
                SourceIp = sourceIp,
                SourcePort = sourcePort,
                DisconnectedAt = ChinaTime.Now
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SignalR push failed for device disconnected {Ip}:{Port}", sourceIp, sourcePort);
        }
    }

    public Task PublishAdtSent(string adtType, string patientId, string visitId, string messageContent,
        string targetEndpoint, bool success, CancellationToken ct = default) => Task.CompletedTask;
}
