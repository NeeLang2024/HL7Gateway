using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HL7Gateway.Core.Entities;

[Table("IntegrationTraceEvents")]
public class IntegrationTraceEvent
{
    [Key]
    public long Id { get; set; }

    [MaxLength(100)]
    public string TraceId { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Step { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Status { get; set; } = "Info";

    [MaxLength(80)]
    public string? PartnerKey { get; set; }

    [MaxLength(2000)]
    public string? Detail { get; set; }

    public int? DurationMs { get; set; }

    [MaxLength(40)]
    public string? RelatedEntityType { get; set; }

    public long? RelatedEntityId { get; set; }

    public DateTime CreatedAt { get; set; } = ChinaTime.Now;
}
