using HL7Gateway.Core.Entities;
using HL7Gateway.Core.Services.Interfaces;

namespace HL7Gateway.Core.Services.Implementations;

public class NoopEventPublisher : IEventPublisher
{
    public Task PublishMessageReceived(Hl7Message message, CancellationToken ct = default) => Task.CompletedTask;
    public Task PublishDeviceConnected(string sourceIp, int sourcePort, CancellationToken ct = default) => Task.CompletedTask;
    public Task PublishDeviceDisconnected(string sourceIp, int sourcePort, CancellationToken ct = default) => Task.CompletedTask;
    public Task PublishAdtSent(string adtType, string patientId, string visitId, string messageContent,
        string targetEndpoint, bool success, CancellationToken ct = default) => Task.CompletedTask;
}
