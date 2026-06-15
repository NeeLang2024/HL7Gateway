using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("WsiSubscriptions")]
public class WsiSubscription
{
    [Key]
    public int SubscriptionId { get; set; }

    [MaxLength(500)]
    public string NotificationUri { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ClientId { get; set; }

    [MaxLength(100)]
    public string? PatientIdDomain { get; set; }

    [MaxLength(500)]
    public string? FacilityCode { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? FilterCriteria { get; set; }

    public DateTime CreatedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;

    public DateTime ExpiresAt { get; set; } = HL7Gateway.Core.ChinaTime.Now.AddDays(30);

    public DateTime? LastNotifiedAt { get; set; }

    public int NotifyCount { get; set; }

    public int FailedCount { get; set; }

    public DateTime? LastFailedAt { get; set; }
}
