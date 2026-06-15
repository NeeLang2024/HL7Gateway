using HL7Gateway.Core.Entities;

namespace HL7Gateway.Core.Services.Interfaces;

public interface IEventPublisher
{
    Task PublishMessageReceived(Hl7Message message, CancellationToken ct = default);
    Task PublishDeviceConnected(string sourceIp, int sourcePort, CancellationToken ct = default);
    Task PublishDeviceDisconnected(string sourceIp, int sourcePort, CancellationToken ct = default);

    Task PublishAdtSent(string adtType, string patientId, string visitId, string messageContent,
        string targetEndpoint, bool success, CancellationToken ct = default);
}
