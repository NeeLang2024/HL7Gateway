using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("ADTLog")]
public class AdtLogEntry
{
    [Key]
    public long LogId { get; set; }

    public long? QueueId { get; set; }

    [MaxLength(20)]
    public string MessageType { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? PatientId { get; set; }

    [MaxLength(500)]
    public string TargetEndpoint { get; set; } = string.Empty;

    public byte Status { get; set; }

    public string? RequestContent { get; set; }
    public string? ResponseContent { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    public int? DurationMs { get; set; }

    public DateTime CreatedAt { get; set; } = HL7Gateway.Core.ChinaTime.Now;
}
