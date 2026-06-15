using HL7Gateway.Core.Entities;

namespace HL7Gateway.Core.Services.Interfaces;

public interface IWsiService
{
    Task<int> SubscribeAsync(string notificationUri, string? clientId, string? patientIdDomain, string? facilityCode, CancellationToken ct = default);
    Task<bool> UnsubscribeAsync(int subscriptionId, CancellationToken ct = default);
    Task<List<WsiSubscription>> GetActiveSubscriptionsAsync(CancellationToken ct = default);
    Task<string> BuildAdtXmlAsync(string adtType, Hl7Message message, Patient? patient, Visit? visit, CancellationToken ct = default);
    Task NotifySubscribersAsync(string adtType, Hl7Message message, CancellationToken ct = default);
}
