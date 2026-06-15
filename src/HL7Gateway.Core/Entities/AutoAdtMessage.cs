using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("AutoAdtMessages")]
public class AutoAdtMessage
{
    [Key]
    public long Id { get; set; }

    public long EventId { get; set; }
    public long? AdtQueueId { get; set; }

    [MaxLength(20)]
    public string MessageType { get; set; } = string.Empty;

    [MaxLength(100)]
    public string MessageControlId { get; set; } = string.Empty;

    public string Hl7Raw { get; set; } = string.Empty;

    [MaxLength(30)]
    public string SendStatus { get; set; } = "Pending";

    public string? ResponseText { get; set; }
    public string? ErrorText { get; set; }

    public int RetryCount { get; set; }

    public DateTime CreatedAt { get; set; } = ChinaTime.Now;
    public DateTime? QueuedAt { get; set; }
    public DateTime? SentAt { get; set; }
}
