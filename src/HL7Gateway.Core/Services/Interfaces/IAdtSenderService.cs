namespace HL7Gateway.Core.Services.Interfaces;

public interface IAdtSenderService
{
    Task EnqueueAsync(string adtType, string messageContent, string targetEndpoint, int priority = 0);
    Task<int> ProcessQueueAsync(CancellationToken cancellationToken);
    int PendingCount { get; }
}
