using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("ADTQueue")]
public class AdtQueueItem
{
    [Key]
    public long QueueId { get; set; }

    public long? MessageId { get; set; }

    [MaxLength(20)]
    public string AdtMessageType { get; set; } = string.Empty;

    public int Priority { get; set; }

    public string MessageContent { get; set; } = string.Empty;

    [MaxLength(500)]
    public string TargetEndpoint { get; set; } = string.Empty;

    public byte Status { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;

    [MaxLength(2000)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;
    public DateTime? NextRetryAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? AckReceivedAt { get; set; }

    public string? AckContent { get; set; }
}
